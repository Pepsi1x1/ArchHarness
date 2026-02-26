using ArchHarness.App.Copilot;

namespace ArchHarness.App.Core;

/// <summary>
/// Uses the Copilot model to extract user intent from a run request and generate
/// a human-readable setup summary. Separated from TUI concerns for testability.
/// </summary>
public sealed class SetupIntentExtractor
{
    private const string NONE_TEXT = "(none)";

    private readonly ICopilotClient _copilotClient;
    private readonly IModelResolver _modelResolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="SetupIntentExtractor"/> class.
    /// </summary>
    /// <param name="copilotClient">Client for Copilot completions.</param>
    /// <param name="modelResolver">Resolver for model identifiers.</param>
    public SetupIntentExtractor(ICopilotClient copilotClient, IModelResolver modelResolver)
    {
        this._copilotClient = copilotClient;
        this._modelResolver = modelResolver;
    }

    /// <summary>
    /// Extracts high-level intent from the run request by prompting the Copilot model.
    /// The result is advisory only and used to enrich the setup UX.
    /// </summary>
    /// <param name="request">The run request to extract intent from.</param>
    /// <param name="cancellationToken">Token to signal cancellation.</param>
    public async Task RunIntentExtractionAsync(RunRequest request, CancellationToken cancellationToken)
    {
        string model = this._modelResolver.Resolve("conversation", request.ModelOverrides);
        string prompt = $"""
            Extract intent for this run request and identify missing optional fields.
            Return a compact one-line summary.
            Task: {request.TaskPrompt}
            Workflow: {request.Workflow}
            WorkspaceMode: {request.WorkspaceMode}
            ProjectName: {request.ProjectName ?? NONE_TEXT}
            BuildCommand: {request.BuildCommand ?? NONE_TEXT}
            """;
        _ = await this._copilotClient.CompleteAsync(model, prompt, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Generates a concise bullet-point summary of the run configuration by prompting the Copilot model.
    /// </summary>
    /// <param name="request">The run request to summarize.</param>
    /// <param name="cancellationToken">Token to signal cancellation.</param>
    /// <returns>A redacted summary string suitable for display.</returns>
    public async Task<string> GenerateSetupSummaryAsync(RunRequest request, CancellationToken cancellationToken)
    {
        string model = this._modelResolver.Resolve("conversation", request.ModelOverrides);
        string prompt = $"""
            Summarize this run configuration in 4 concise bullet points.
            Task: {request.TaskPrompt}
            WorkspacePath: {request.WorkspacePath}
            WorkspaceMode: {request.WorkspaceMode}
            Workflow: {request.Workflow}
            ProjectName: {request.ProjectName ?? NONE_TEXT}
            BuildCommand: {request.BuildCommand ?? NONE_TEXT}
            Overrides: {FormatOverrides(request.ModelOverrides)}
            """;

        string completion = await this._copilotClient.CompleteAsync(model, prompt, cancellationToken: cancellationToken);
        return Redaction.RedactSecrets(completion);
    }

    private static string FormatOverrides(IDictionary<string, string>? overrides)
    {
        if (overrides is null || overrides.Count == 0)
        {
            return NONE_TEXT;
        }

        System.Text.StringBuilder builder = new System.Text.StringBuilder();
        foreach (KeyValuePair<string, string> pair in overrides)
        {
            if (builder.Length > 0)
            {
                builder.Append(", ");
            }

            builder.Append(pair.Key).Append('=').Append(pair.Value);
        }

        return builder.ToString();
    }
}
