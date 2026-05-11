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
        WikiDocRepositoryInfo repository,
        string outputTarget,
        IDictionary<string, string>? modelOverrides,
        string agentId,
        CancellationToken cancellationToken)
    {
        string prompt = BuildRepositoryPrompt(scanRoot, repository, outputTarget);

        return this.CompleteJsonAsync<WikiDocRepositoryIndex>(
            prompt,
            modelOverrides,
            agentId,
            NormalizeRepositoryIndex,
            cancellationToken);
    }

    private static string BuildRepositoryPrompt(string scanRoot, WikiDocRepositoryInfo repository, string outputTarget)
    {
        string template = PromptLoader.Load("WikiDoc", "repository.md");
        return PromptLoader.Render(
            template,
            ("{{ScanRoot}}", scanRoot),
            ("{{RepositoryRoot}}", repository.RepositoryRoot),
            ("{{RepositoryRelativePath}}", repository.RelativePath),
            ("{{RepositoryDisplayName}}", repository.DisplayName),
            ("{{OutputTarget}}", outputTarget));
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
        string systemPrompt = PromptLoader.Load("WikiDoc", "system.md");

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
        for (int attempt = 0; attempt < 3; attempt++)
        {
            string prompt = attempt == 0
                ? basePrompt
                : BuildValidationFollowUpPrompt(lastError);
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
            }
        }

        throw new InvalidOperationException(lastError ?? $"WikiDoc generation failed for {typeof(T).Name}.");
    }

    private static IReadOnlyList<T> NormalizeDocumentList<T>(
        IReadOnlyList<T>? source,
        Func<T, bool> isValid,
        Func<T, T> trim,
        Func<IEnumerable<T>, IEnumerable<T>>? postProcess = null)
    {
        source ??= [];

        IEnumerable<T> items = source
            .Where(isValid)
            .Select(trim);

        if (postProcess is not null)
        {
            items = postProcess(items);
        }

        return items.ToArray();
    }

    private static IReadOnlyList<WikiDocConceptSeed> NormalizeConceptSeeds(IReadOnlyList<WikiDocConceptSeed>? concepts)
        => NormalizeDocumentList(
            concepts,
            isValid: concept => !string.IsNullOrWhiteSpace(concept.Name) && !string.IsNullOrWhiteSpace(concept.Summary),
            trim: concept => concept with { Name = concept.Name.Trim(), Summary = concept.Summary.Trim() },
            postProcess: items => items.DistinctBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Take(8));
}
