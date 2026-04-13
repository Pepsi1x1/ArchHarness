using System.Text.Json;
using ArchHarness.App.Copilot;
using ArchHarness.App.Core;
using Microsoft.Extensions.Options;

namespace ArchHarness.App.Agents;

/// <summary>
/// Uses backend-developer tooling to inspect repositories and return structured wiki content.
/// </summary>
public sealed class WikiDocAgent : AgentBase
{
    private const string BACKEND_ROLE = "backend-developer";
    private const string WIKIDOC_AGENT_ROLE = "wikidoc";
    private const string WIKIDOC_SYSTEM_PROMPT_FALLBACK = """
        You are the WikiDoc backend workflow.
        Inspect the repository using available read tools, but do not modify workspace files directly.
        Return strict JSON only. Do not wrap the response in markdown fences.
        Focus on producing accurate operator-facing documentation from the checked-in source.
        """;
    private const string WIKIDOC_REPOSITORY_PROMPT_FALLBACK = """
        Inspect the repository rooted at the current working directory and return ONLY strict JSON with this schema:
        {
          "repositoryName": "string",
          "summary": "string",
          "homeMarkdown": "string",
          "concepts": [{"name": "string", "summary": "string"}]
        }

        Rules:
        - Do not ask follow-up questions.
        - Do not modify files.
        - `homeMarkdown` is the full content for wiki/Home.md.
        - `homeMarkdown` must include: title, purpose, major components, important commands/workflows, and notable conventions or risks.
        - Prefer concrete facts from the repository over generic advice.
        - `concepts` should capture reusable cross-repository ideas, bounded to 8 items maximum.
        - If the repository is sparse, say that explicitly in the markdown rather than inventing details.

        ScanRoot: {{ScanRoot}}
        RepositoryRoot: {{RepositoryRoot}}
        RepositoryRelativePath: {{RepositoryRelativePath}}
        RepositoryDisplayName: {{RepositoryDisplayName}}
        OutputTarget: {{OutputTarget}}
        """;
    private const string WIKIDOC_SYNTHESIS_PROMPT_FALLBACK = """
        You are synthesizing a megawiki across related repositories.
        Return ONLY strict JSON with this schema:
        {
          "megaWikiMarkdown": "string",
          "conceptPages": [{"slug": "string", "title": "string", "markdown": "string"}]
        }

        Rules:
        - Do not modify files.
        - `megaWikiMarkdown` is the full markdown for MegaWiki.md.
        - Summarize the repository set, major boundaries, and where each repository fits.
        - `conceptPages` must synthesize shared concepts that matter across repositories, not duplicate repository home pages.
        - Keep concept page slugs filesystem-safe and concise.
        - Use only the supplied repository summaries and concept seeds; do not invent repositories.

        ScanRoot: {{ScanRoot}}
        RepositorySummaryPayload:
        {{RepositorySummaryPayload}}
        """;

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static WikiDocRepositoryDocument NormalizeRepositoryDocument(WikiDocRepositoryDocument document)
    {
        if (string.IsNullOrWhiteSpace(document.HomeMarkdown))
        {
            throw new InvalidOperationException("WikiDoc repository response did not include homeMarkdown.");
        }

        string repositoryName = string.IsNullOrWhiteSpace(document.RepositoryName)
            ? "Repository"
            : document.RepositoryName.Trim();

        return document with
        {
            RepositoryName = repositoryName,
            Summary = document.Summary?.Trim() ?? string.Empty,
            HomeMarkdown = document.HomeMarkdown.Trim(),
            Concepts = NormalizeConceptSeeds(document.Concepts)
        };
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WikiDocAgent"/> class.
    /// </summary>
    public WikiDocAgent(
        ICopilotClient copilotClient,
        IModelResolver modelResolver,
        IAgentToolPolicyProvider toolPolicyProvider,
        IOptions<AgentsOptions> agentsOptions)
        : base(copilotClient, modelResolver, toolPolicyProvider, agentsOptions, BACKEND_ROLE, Guid.NewGuid().ToString("N"))
    {
    }

    /// <summary>
    /// Generates structured wiki content for a single repository.
    /// </summary>
    public Task<WikiDocRepositoryDocument> DocumentRepositoryAsync(
        string scanRoot,
        string repositoryRoot,
        string repositoryRelativePath,
        string repositoryDisplayName,
        string outputTarget,
        IDictionary<string, string>? modelOverrides,
        string agentId,
        CancellationToken cancellationToken)
    {
        string template = PromptLoader.Load("WikiDoc", "repository.md", WIKIDOC_REPOSITORY_PROMPT_FALLBACK);
        string prompt = PromptLoader.Render(
            template,
            ("{{ScanRoot}}", scanRoot),
            ("{{RepositoryRoot}}", repositoryRoot),
            ("{{RepositoryRelativePath}}", repositoryRelativePath),
            ("{{RepositoryDisplayName}}", repositoryDisplayName),
            ("{{OutputTarget}}", outputTarget));

        return this.CompleteJsonAsync<WikiDocRepositoryDocument>(
            prompt,
            modelOverrides,
            agentId,
            NormalizeRepositoryDocument,
            cancellationToken);
    }

    /// <summary>
    /// Synthesizes aggregate documentation and shared concept pages across repositories.
    /// </summary>
    public Task<WikiDocMegaWikiDocument> SynthesizeMegaWikiAsync(
        string scanRoot,
        string repositorySummaryPayload,
        IDictionary<string, string>? modelOverrides,
        string agentId,
        CancellationToken cancellationToken)
    {
        string template = PromptLoader.Load("WikiDoc", "megawiki.md", WIKIDOC_SYNTHESIS_PROMPT_FALLBACK);
        string prompt = PromptLoader.Render(
            template,
            ("{{ScanRoot}}", scanRoot),
            ("{{RepositorySummaryPayload}}", repositorySummaryPayload));

        return this.CompleteJsonAsync<WikiDocMegaWikiDocument>(
            prompt,
            modelOverrides,
            agentId,
            static document => !string.IsNullOrWhiteSpace(document.MegaWikiMarkdown)
                ? document with
                {
                    MegaWikiMarkdown = document.MegaWikiMarkdown.Trim(),
                    ConceptPages = NormalizeConceptPages(document.ConceptPages)
                }
                : throw new InvalidOperationException("WikiDoc megawiki response did not include megaWikiMarkdown."),
            cancellationToken);
    }

    private async Task<T> CompleteJsonAsync<T>(
        string basePrompt,
        IDictionary<string, string>? modelOverrides,
        string agentId,
        Func<T, T> normalize,
        CancellationToken cancellationToken)
    {
        string model = base.ResolveModel(modelOverrides);
        string systemPrompt = PromptLoader.Load("WikiDoc", "system.md", WIKIDOC_SYSTEM_PROMPT_FALLBACK);
        CopilotCompletionOptions options = base.ApplyToolPolicy(new CopilotCompletionOptions
        {
            SystemMessage = systemPrompt,
            SystemMessageMode = CopilotSystemMessageMode.Append
        });

        string? lastError = null;
        string? previousResponsePreview = null;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            string prompt = attempt == 0
                ? basePrompt
                : $"{basePrompt}\n\nIMPORTANT: Return ONLY the raw JSON object. No markdown, no commentary.\nValidation error: {lastError ?? "Unknown validation error."}\nPrevious response:\n{previousResponsePreview}";
            string completion = await base.CopilotClient.CompleteAsync(
                model,
                prompt,
                options,
                agentId: agentId,
                agentRole: WIKIDOC_AGENT_ROLE,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            string? json = ExecutionPlanParser.ExtractJson(completion);
            if (string.IsNullOrWhiteSpace(json))
            {
                lastError = "No JSON object was returned.";
                previousResponsePreview = BuildPreview(completion);
                continue;
            }

            try
            {
                T? result = JsonSerializer.Deserialize<T>(json, JsonOptions);
                if (result is null)
                {
                    throw new JsonException($"Unable to deserialize {typeof(T).Name}.");
                }

                return normalize(result);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                lastError = ex.Message;
                previousResponsePreview = BuildPreview(completion);
            }
        }

        throw new InvalidOperationException(lastError ?? $"WikiDoc generation failed for {typeof(T).Name}.");
    }

    private static string BuildPreview(string text)
        => text.Length <= 800 ? text : text[..800];

    private static IReadOnlyList<T> NormalizeDocumentList<T>(
        IReadOnlyList<T>? source,
        Func<T, bool> isValid,
        Func<T, T> trim,
        Func<IEnumerable<T>, IEnumerable<T>>? postProcess = null)
    {
        IEnumerable<T> items = (source ?? Array.Empty<T>())
            .Where(isValid)
            .Select(trim);
        if (postProcess is not null) items = postProcess(items);
        return items.ToArray();
    }

    private static IReadOnlyList<WikiDocConceptSeed> NormalizeConceptSeeds(IReadOnlyList<WikiDocConceptSeed>? concepts)
        => NormalizeDocumentList(
            concepts,
            isValid: concept => !string.IsNullOrWhiteSpace(concept.Name) && !string.IsNullOrWhiteSpace(concept.Summary),
            trim: concept => concept with { Name = concept.Name.Trim(), Summary = concept.Summary.Trim() },
            postProcess: items => items.DistinctBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Take(8));

    private static IReadOnlyList<WikiDocConceptPage> NormalizeConceptPages(IReadOnlyList<WikiDocConceptPage>? conceptPages)
        => NormalizeDocumentList(
            conceptPages,
            isValid: page => !string.IsNullOrWhiteSpace(page.Title) && !string.IsNullOrWhiteSpace(page.Markdown),
            trim: page => page with { Slug = page.Slug?.Trim() ?? string.Empty, Title = page.Title.Trim(), Markdown = page.Markdown.Trim() });
}
