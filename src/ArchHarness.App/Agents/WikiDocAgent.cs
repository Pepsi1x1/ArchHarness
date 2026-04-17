using System.Text.Json;
using ArchHarness.App.Copilot;
using ArchHarness.App.Core;
using Microsoft.Extensions.Options;

namespace ArchHarness.App.Agents;

/// <summary>
/// Inspects repositories and writes wiki documentation via tools, returning only a structured index.
/// </summary>
public sealed class WikiDocAgent : AgentBase
{
    private const string WIKIDOC_ROLE = "wikidoc";
    private const string WIKIDOC_AGENT_ROLE = "wikidoc";
    private const string WIKIDOC_SYSTEM_PROMPT_FALLBACK = """
        You are the WikiDoc agent.
        Inspect repositories using available read tools and write thorough, multi-page wiki documentation using available write tools.
        The documentation you produce must be suitable for publishing directly to an Azure DevOps wiki.
        Home.md is always an index page that links to sub-pages; the real content lives in the sub-pages.
        After writing ALL documentation files, return a strict JSON index as specified in the prompt.
        Do not wrap the JSON in markdown fences.
        Focus on producing accurate operator-facing documentation from the checked-in source.
        """;
    private const string WIKIDOC_REPOSITORY_PROMPT_FALLBACK = """
        Inspect the repository and write thorough, multi-page wiki documentation suitable for Azure DevOps wiki.

        Steps:
        1. Read and analyze the repository thoroughly using available read tools.
           Examine project files, source code, configuration, scripts, READMEs, and directory structure.
        2. Write multiple documentation pages to the output directory at {{OutputTarget}}.
           Create sub-pages as individual .md files, one per significant topic you discover.
           Thoroughly document the solution and any other aspects that an operator, developer, 
           or new team member would need to understand.
           Each sub-page should be a deep-dive into its topic with concrete facts from the source code.
           You may create as many or as few sub-pages as the repository warrants.
        3. Write Home.md as an INDEX page:
           - Repository title and a concise summary paragraph.
           - A table of contents with relative links to every sub-page you wrote (e.g., [Architecture](Architecture.md)).
           - Do NOT put substantive documentation in Home.md; it is an index only.
        4. After writing ALL documentation files, return ONLY a JSON summary index:
        {
          "repositoryName": "string",
          "summary": "string",
          "pages": ["string"],
          "concepts": [{"name": "string", "summary": "string"}]
        }

        Rules:
        - Do not ask follow-up questions.
        - Write ALL documentation files using file-write tools BEFORE returning the JSON.
        - `pages` lists every .md filename you wrote (including Home.md), e.g. ["Home.md", "Architecture.md", "Getting-Started.md"].
        - `concepts` should capture reusable cross-repository ideas, bounded to 8 items maximum.
        - Prefer concrete facts from the repository over generic advice.
        - If the repository is sparse, say that explicitly rather than inventing details; still write at least Home.md.
        - Use relative links between pages so the wiki works when published to Azure DevOps.
        - The final response text must be ONLY the JSON summary object.

        ScanRoot: {{ScanRoot}}
        RepositoryRoot: {{RepositoryRoot}}
        RepositoryRelativePath: {{RepositoryRelativePath}}
        RepositoryDisplayName: {{RepositoryDisplayName}}
        OutputTarget: {{OutputTarget}}
        """;

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static WikiDocRepositoryIndex NormalizeRepositoryIndex(WikiDocRepositoryIndex index)
    {
        string repositoryName = string.IsNullOrWhiteSpace(index.RepositoryName)
            ? "Repository"
            : index.RepositoryName.Trim();

        IReadOnlyList<string> pages = (index.Pages ?? Array.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return index with
        {
            RepositoryName = repositoryName,
            Summary = index.Summary?.Trim() ?? string.Empty,
            Pages = pages,
            Concepts = NormalizeConceptSeeds(index.Concepts)
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
        : base(copilotClient, modelResolver, toolPolicyProvider, agentsOptions, WIKIDOC_ROLE, Guid.NewGuid().ToString("N"))
    {
    }

    /// <summary>
    /// Inspects a repository, writes wiki documentation via tools, and returns a structured index.
    /// </summary>
    public Task<WikiDocRepositoryIndex> DocumentRepositoryAsync(
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

        return this.CompleteJsonAsync<WikiDocRepositoryIndex>(
            prompt,
            modelOverrides,
            agentId,
            NormalizeRepositoryIndex,
            cancellationToken);
    }

    private async Task<T> CompleteJsonAsync<T>(
        string basePrompt,
        IDictionary<string, string>? modelOverrides,
        string agentId,
        Func<T, T> normalize,
        CancellationToken cancellationToken,
        string? roleOverride = null)
    {
        string model = roleOverride is not null
            ? base.ResolveModelForRole(roleOverride, modelOverrides)
            : base.ResolveModel(modelOverrides);
        string systemPrompt = PromptLoader.Load("WikiDoc", "system.md", WIKIDOC_SYSTEM_PROMPT_FALLBACK);

        CopilotCompletionOptions baseOptions = new CopilotCompletionOptions
        {
            SystemMessage = systemPrompt,
            SystemMessageMode = CopilotSystemMessageMode.Append,
            ReasoningEffort = roleOverride is not null
                ? base.ResolveReasoningEffortForRole(roleOverride)
                : null
        };
        CopilotCompletionOptions options = base.ApplyToolPolicy(baseOptions);

        string? lastError = null;
        string? previousResponsePreview = null;
        for (int attempt = 0; attempt < 3; attempt++)
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
}
