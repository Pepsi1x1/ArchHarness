using ArchHarness.App.Copilot;
using ArchHarness.App.Core;
using ArchHarness.App.Workspace;
using Microsoft.Extensions.Options;

namespace ArchHarness.App.Agents;

/// <summary>
/// Build agent responsible for baseline and intermediate build execution in the workspace.
/// </summary>
public sealed class BuildAgent : AgentBase
{
    private const int MAX_CAPTURE_LENGTH = 4000;
    private readonly IShellCommandExecutor _shellCommandExecutor;

    /// <summary>
    /// Initializes a new instance of the <see cref="BuildAgent"/> class.
    /// </summary>
    /// <param name="copilotClient">The Copilot client for model completions.</param>
    /// <param name="modelResolver">Resolves which model to use for this agent.</param>
    /// <param name="toolPolicyProvider">Provides tool access policies for the agent.</param>
    /// <param name="agentsOptions">Configuration options for agent behavior.</param>
    public BuildAgent(
        ICopilotClient copilotClient,
        IModelResolver modelResolver,
        IAgentToolPolicyProvider toolPolicyProvider,
        IShellCommandExecutor shellCommandExecutor,
        IOptions<AgentsOptions> agentsOptions)
        : base(copilotClient, modelResolver, toolPolicyProvider, agentsOptions, "build", Guid.NewGuid().ToString("N"))
    {
        this._shellCommandExecutor = shellCommandExecutor;
    }

    /// <summary>
    /// Runs the delegated build task in the workspace.
    /// </summary>
    /// <param name="workspace">The workspace adapter for file operations.</param>
    /// <param name="objective">The delegated prompt describing what build work to perform.</param>
    /// <param name="buildCommand">The build command to execute.</param>
    /// <param name="modelOverrides">Optional model override mappings.</param>
    /// <param name="stepId">The plan step ID producing this build result.</param>
    /// <param name="agentId">Optional agent identifier override.</param>
    /// <param name="agentRole">Optional agent role override.</param>
    /// <param name="cancellationToken">Token to signal cancellation.</param>
    /// <returns>A structured build outcome indicating pass/fail and summary.</returns>
    public async Task<BuildOutcome> RunBuildAsync(
        IWorkspaceAdapter workspace,
        string objective,
        string? buildCommand,
        IDictionary<string, string>? modelOverrides,
        int stepId = 0,
        string? agentId = null,
        string? agentRole = null,
        CancellationToken cancellationToken = default)
    {
        _ = objective;
        _ = modelOverrides;
        _ = agentId;
        _ = agentRole;

        if (string.IsNullOrWhiteSpace(buildCommand))
        {
            return new BuildOutcome(false, "No build command was configured for the build step.", stepId, DateTimeOffset.UtcNow);
        }

        ArchHarness.App.SourceControl.LocalCommandResult result = await this._shellCommandExecutor.RunAsync(buildCommand, workspace.RootPath, cancellationToken).ConfigureAwait(false);
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;
        string detail = FirstMeaningfulLine(result.StandardError) ?? FirstMeaningfulLine(result.StandardOutput) ?? "No output captured.";
        string summary = result.ExitCode == 0
            ? $"Build passed. {detail}"
            : $"Build failed with exit code {result.ExitCode}. {detail}";

        return new BuildOutcome(
            result.ExitCode == 0,
            summary,
            stepId,
            timestamp,
            buildCommand,
            result.ExitCode,
            Truncate(result.StandardOutput),
            Truncate(result.StandardError));
    }

    private static string? FirstMeaningfulLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
    }

    private static string? Truncate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length <= MAX_CAPTURE_LENGTH)
        {
            return value;
        }

        return value[..MAX_CAPTURE_LENGTH] + Environment.NewLine + "...[truncated]";
    }
}
