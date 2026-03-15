using System.Text;
using ArchHarness.App.Constants;
using ArchHarness.App.Copilot;

namespace ArchHarness.App.Core;

/// <summary>
/// Generates Copilot-assisted intent extraction and setup summary text.
/// </summary>
public sealed class SetupSummaryGenerator
{
    private const string NONE_TEXT = DisplayConstants.NONE_TEXT;

    private readonly ICopilotClient _copilotClient;
    private readonly IModelResolver _modelResolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="SetupSummaryGenerator"/> class.
    /// </summary>
    /// <param name="copilotClient">Client for Copilot completions.</param>
    /// <param name="modelResolver">Resolver for model selection.</param>
    public SetupSummaryGenerator(ICopilotClient copilotClient, IModelResolver modelResolver)
    {
        this._copilotClient = copilotClient;
        this._modelResolver = modelResolver;
    }

    /// <summary>
    /// Extracts intent from the run request via Copilot. This is advisory only.
    /// </summary>
    /// <param name="request">The run request to extract intent from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the async operation.</returns>
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
            """;
        _ = await this._copilotClient.CompleteAsync(model, prompt, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Generates a concise bullet-point summary of the run configuration via Copilot.
    /// </summary>
    /// <param name="request">The run request to summarize.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The redacted summary text.</returns>
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
            Overrides: {FormatOverrides(request.ModelOverrides)}
            """;

        string completion = await this._copilotClient.CompleteAsync(model, prompt, cancellationToken: cancellationToken);
        return Redaction.RedactSecrets(completion);
    }

    /// <summary>
    /// Ensures the run request has a human-friendly title derived from the request intent.
    /// </summary>
    /// <param name="request">The run request to title.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The request with a populated <see cref="RunRequest.RunTitle"/> value.</returns>
    public async Task<RunRequest> PopulateRunTitleAsync(RunRequest request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.RunTitle))
        {
            return request;
        }

        string fallbackTitle = BuildFallbackTitle(request);
        try
        {
            string model = this._modelResolver.Resolve("conversation", request.ModelOverrides);
            string prompt = $"""
                Generate a concise run title of at most 6 words for this ArchHarness request.
                Return plain text only. No markdown, no quotes, no numbering.
                Task: {request.TaskPrompt}
                Workflow: {request.Workflow}
                WorkspaceMode: {request.WorkspaceMode}
                ProjectName: {request.ProjectName ?? NONE_TEXT}
                ArchitectureLoopMode: {request.ArchitectureLoopMode}
                ArchitectureLoopPrompt: {request.ArchitectureLoopPrompt ?? NONE_TEXT}
                """;

            string completion = await this._copilotClient.CompleteAsync(model, prompt, cancellationToken: cancellationToken);
            string title = NormalizeGeneratedTitle(completion);
            return request with { RunTitle = string.IsNullOrWhiteSpace(title) ? fallbackTitle : title };
        }
        catch
        {
            return request with { RunTitle = fallbackTitle };
        }
    }

    /// <summary>
    /// Formats model overrides as a comma-separated string for display.
    /// </summary>
    /// <param name="overrides">The overrides dictionary.</param>
    /// <returns>A formatted string representation.</returns>
    internal static string FormatOverrides(IDictionary<string, string>? overrides)
    {
        if (overrides is null || overrides.Count == 0)
        {
            return NONE_TEXT;
        }

        StringBuilder builder = new StringBuilder();
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

    private static string BuildFallbackTitle(RunRequest request)
    {
        string source = request.ArchitectureLoopMode && !string.IsNullOrWhiteSpace(request.ArchitectureLoopPrompt)
            ? request.ArchitectureLoopPrompt
            : request.TaskPrompt;

        if (string.IsNullOrWhiteSpace(source))
        {
            return request.ArchitectureLoopMode ? "Architecture Review" : "Untitled Run";
        }

        string[] words = source
            .Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Take(6)
            .ToArray();

        string candidate = string.Join(" ", words).Trim();
        return string.IsNullOrWhiteSpace(candidate) ? "Untitled Run" : candidate;
    }

    private static string NormalizeGeneratedTitle(string completion)
    {
        string sanitized = Redaction.RedactSecrets(completion)
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Replace("\"", string.Empty)
            .Trim();

        if (sanitized.Length > 72)
        {
            sanitized = sanitized[..72].TrimEnd();
        }

        return sanitized.Trim(' ', '-', ':', '.', '*', '#');
    }
}
