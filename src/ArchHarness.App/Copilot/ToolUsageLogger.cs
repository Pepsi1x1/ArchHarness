using System.Text.Json;
using ArchHarness.App.Core;
using ArchHarness.App.Storage;
using GitHub.Copilot.SDK;

namespace ArchHarness.App.Copilot;

public sealed record ToolUsageEvent(
    string Stage,
    string? ToolName,
    string? Decision,
    bool? DeniedByName,
    bool? DeniedByArgs,
    object? ToolArgs,
    string? RawInput
);

public interface IToolUsageLogger
{
    Task LogPreToolUseAsync(PreToolUseHookInput input, string decision, bool deniedByName, bool deniedByArgs);
    Task LogPostToolUseAsync(PostToolUseHookInput input);
}

public sealed class ToolUsageLogger : IToolUsageLogger
{
    private readonly IRunContextAccessor _runContextAccessor;
    private readonly IArtefactStore _artefactStore;

    public ToolUsageLogger(IRunContextAccessor runContextAccessor, IArtefactStore artefactStore)
    {
        _runContextAccessor = runContextAccessor;
        _artefactStore = artefactStore;
    }

    public Task LogPreToolUseAsync(PreToolUseHookInput input, string decision, bool deniedByName, bool deniedByArgs)
        => WriteAsync(new ToolUsageEvent(
            Stage: "pre",
            ToolName: input.ToolName,
            Decision: decision,
            DeniedByName: deniedByName,
            DeniedByArgs: deniedByArgs,
            ToolArgs: input.ToolArgs,
            RawInput: SafeSerialize(input)));

    public Task LogPostToolUseAsync(PostToolUseHookInput input)
        => WriteAsync(new ToolUsageEvent(
            Stage: "post",
            ToolName: input.ToolName,
            Decision: null,
            DeniedByName: null,
            DeniedByArgs: null,
            ToolArgs: null,
            RawInput: SafeSerialize(input)));

    private async Task WriteAsync(ToolUsageEvent toolEvent)
    {
        var context = _runContextAccessor.Current;
        if (context is null)
        {
            return;
        }

        await _artefactStore.AppendEventAsync(context.RunDirectory, new
        {
            runId = context.RunId,
            source = "copilot.tool",
            stage = toolEvent.Stage,
            toolName = toolEvent.ToolName,
            decision = toolEvent.Decision,
            deniedByName = toolEvent.DeniedByName,
            deniedByArgs = toolEvent.DeniedByArgs,
            toolArgs = toolEvent.ToolArgs,
            raw = toolEvent.RawInput
        }, CancellationToken.None);
    }

    private static string? SafeSerialize(object input)
    {
        try
        {
            return JsonSerializer.Serialize(input);
        }
        catch
        {
            return null;
        }
    }
}
