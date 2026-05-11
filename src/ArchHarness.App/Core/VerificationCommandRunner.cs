using ArchHarness.App.SourceControl;

namespace ArchHarness.App.Core;

/// <summary>
/// Executes verification commands and returns durable evidence for completion validation.
/// </summary>
public interface IVerificationCommandRunner
{
    /// <summary>
    /// Runs the supplied verification commands inside the workspace.
    /// </summary>
    Task<IReadOnlyList<VerificationEvidence>> RunAsync(
        string workspaceRoot,
        IReadOnlyList<VerificationCommand> commands,
        IProgress<RuntimeProgressEvent>? progress,
        CancellationToken cancellationToken);
}

/// <summary>
/// Default implementation of <see cref="IVerificationCommandRunner"/>.
/// </summary>
public sealed class VerificationCommandRunner : IVerificationCommandRunner
{
    private const int MAX_CAPTURE_LENGTH = 4000;
    private readonly IShellCommandExecutor _shellCommandExecutor;

    /// <summary>
    /// Initializes a new instance of the <see cref="VerificationCommandRunner"/> class.
    /// </summary>
    public VerificationCommandRunner(IShellCommandExecutor shellCommandExecutor)
    {
        this._shellCommandExecutor = shellCommandExecutor;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<VerificationEvidence>> RunAsync(
        string workspaceRoot,
        IReadOnlyList<VerificationCommand> commands,
        IProgress<RuntimeProgressEvent>? progress,
        CancellationToken cancellationToken)
    {
        if (commands.Count == 0)
        {
            return Array.Empty<VerificationEvidence>();
        }

        List<VerificationEvidence> evidence = new List<VerificationEvidence>(commands.Count);
        foreach (VerificationCommand command in commands)
        {
            progress?.Report(new RuntimeProgressEvent(DateTimeOffset.UtcNow, "verification", $"Running verification: {command.Name}", command.Command));
            if (string.Equals(command.EvidenceType, "manual", StringComparison.OrdinalIgnoreCase))
            {
                evidence.Add(new VerificationEvidence(
                    command.EvidenceType,
                    command.Name,
                    Passed: !command.Required,
                    command.Command,
                    ExitCode: -1,
                    Summary: "Manual verification cannot be executed automatically by the harness.",
                    Criterion: command.Criterion ?? command.Name,
                    TimestampUtc: DateTimeOffset.UtcNow));
                continue;
            }

            LocalCommandResult result = await this._shellCommandExecutor.RunAsync(command.Command, workspaceRoot, cancellationToken).ConfigureAwait(false);
            evidence.Add(new VerificationEvidence(
                command.EvidenceType,
                command.Name,
                Passed: result.ExitCode == 0 || !command.Required,
                command.Command,
                result.ExitCode,
                BuildSummary(command, result),
                Criterion: command.Criterion ?? command.Name,
                Output: Truncate(result.StandardOutput),
                ErrorOutput: Truncate(result.StandardError),
                TimestampUtc: DateTimeOffset.UtcNow));
        }

        return evidence;
    }

    private static string BuildSummary(VerificationCommand command, SourceControl.LocalCommandResult result)
    {
        string primary = result.ExitCode == 0
            ? $"{command.Name} passed."
            : $"{command.Name} failed with exit code {result.ExitCode}.";

        string detail = FirstMeaningfulLine(result.StandardError) ?? FirstMeaningfulLine(result.StandardOutput) ?? "No output captured.";
        return $"{primary} {detail}";
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
