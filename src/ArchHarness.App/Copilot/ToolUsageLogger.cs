using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using ArchHarness.App.Core;
using ArchHarness.App.Storage;
using GitHub.Copilot.SDK;

namespace ArchHarness.App.Copilot;

/// <summary>
/// Records a single tool usage event for governance auditing.
/// </summary>
/// <param name="Stage">The execution stage (pre or post).</param>
/// <param name="ToolName">The tool name, or null if unknown.</param>
/// <param name="Decision">The governance decision (allow or deny), or null for post-stage events.</param>
/// <param name="DeniedByName">Whether the tool was denied by name match.</param>
/// <param name="DeniedByArgs">Whether the tool was denied by argument inspection.</param>
/// <param name="ToolArgs">The tool arguments, or null.</param>
/// <param name="RawInput">The serialized raw input, or null.</param>
public sealed record ToolUsageEvent(
    string Stage,
    string? ToolName,
    string? Decision,
    bool? DeniedByName,
    bool? DeniedByArgs,
    object? ToolArgs,
    string? RawInput
);

/// <summary>
/// Logs tool usage events for governance auditing during Copilot sessions.
/// </summary>
public interface IToolUsageLogger
{
    /// <summary>
    /// Logs a pre-tool-use event with the governance decision.
    /// </summary>
    /// <param name="input">The pre-tool-use hook input.</param>
    /// <param name="decision">The governance decision (allow or deny).</param>
    /// <param name="deniedByName">Whether the tool was denied by name match.</param>
    /// <param name="deniedByArgs">Whether the tool was denied by argument inspection.</param>
    Task LogPreToolUseAsync(PreToolUseHookInput input, string decision, bool deniedByName, bool deniedByArgs);

    /// <summary>
    /// Logs a post-tool-use event after tool execution.
    /// </summary>
    /// <param name="input">The post-tool-use hook input.</param>
    Task LogPostToolUseAsync(PostToolUseHookInput input);
}

/// <summary>
/// Default implementation of <see cref="IToolUsageLogger"/> that persists events to the artefact store.
/// </summary>
public sealed class ToolUsageLogger : IToolUsageLogger
{
    private readonly IRunContextAccessor _runContextAccessor;
    private readonly IArtefactStore _artefactStore;
    private readonly IAgentStreamEventStream _agentStreamEventStream;
    private readonly IAgentExecutionContextAccessor _agentExecutionContextAccessor;

    /// <summary>
    /// Initializes a new instance of <see cref="ToolUsageLogger"/>.
    /// </summary>
    /// <param name="runContextAccessor">Accessor for the current run context.</param>
    /// <param name="artefactStore">The artefact store for persisting events.</param>
    public ToolUsageLogger(
        IRunContextAccessor runContextAccessor,
        IArtefactStore artefactStore,
        IAgentStreamEventStream agentStreamEventStream,
        IAgentExecutionContextAccessor agentExecutionContextAccessor)
    {
        this._runContextAccessor = runContextAccessor;
        this._artefactStore = artefactStore;
        this._agentStreamEventStream = agentStreamEventStream;
        this._agentExecutionContextAccessor = agentExecutionContextAccessor;
    }

    /// <inheritdoc />
    public Task LogPreToolUseAsync(PreToolUseHookInput input, string decision, bool deniedByName, bool deniedByArgs)
        => this.WriteAsync(new ToolUsageEvent(
            Stage: "pre",
            ToolName: input.ToolName,
            Decision: decision,
            DeniedByName: deniedByName,
            DeniedByArgs: deniedByArgs,
            ToolArgs: input.ToolArgs,
            RawInput: SafeSerialize(input)));

    /// <inheritdoc />
    public Task LogPostToolUseAsync(PostToolUseHookInput input)
        => this.WriteAsync(new ToolUsageEvent(
            Stage: "post",
            ToolName: input.ToolName,
            Decision: null,
            DeniedByName: null,
            DeniedByArgs: null,
            ToolArgs: null,
            RawInput: SafeSerializePostToolUseInput(input)));

    private async Task WriteAsync(ToolUsageEvent toolEvent)
    {
        RunContext? context = this._runContextAccessor.Current;
        if (context is null)
        {
            return;
        }

        await this._artefactStore.AppendEventAsync(context.RunDirectory, new
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

        this.PublishSubagentTranscriptIfAvailable(toolEvent);
    }

    private void PublishSubagentTranscriptIfAvailable(ToolUsageEvent toolEvent)
    {
        if (!string.Equals(toolEvent.Stage, "post", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(toolEvent.RawInput)
            || this._agentExecutionContextAccessor.Current is not AgentExecutionContext agentContext)
        {
            return;
        }

        ParsedToolResult? parsed = TryParseToolResult(toolEvent.RawInput);
        if (parsed is null
            || !string.Equals(parsed.ToolName, "task", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(parsed.TextResultForLlm))
        {
            return;
        }

        string markdown = Redaction.RedactSecrets(BuildSubagentMarkdown(parsed));
        this._agentStreamEventStream.Publish(new AgentStreamDeltaEvent(
            DateTimeOffset.UtcNow,
            agentContext.AgentId,
            agentContext.AgentRole,
            markdown,
            ContentFormat: "markdown",
            StreamKind: "subagent-report",
            Title: parsed.Description ?? "Subagent report"));
    }

    private static ParsedToolResult? TryParseToolResult(string rawInput)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(rawInput);
            JsonElement root = document.RootElement;
            string toolName = ReadString(root, "toolName") ?? string.Empty;
            JsonElement toolArgs = root.TryGetProperty("toolArgs", out JsonElement toolArgsElement)
                ? toolArgsElement
                : default;
            JsonElement toolResult = root.TryGetProperty("toolResult", out JsonElement toolResultElement)
                ? toolResultElement
                : default;

            return new ParsedToolResult(
                toolName,
                ReadString(toolArgs, "description"),
                ReadString(toolArgs, "agent_type"),
                ReadString(toolResult, "textResultForLlm"));
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Undefined
            || element.ValueKind == JsonValueKind.Null
            || !element.TryGetProperty(propertyName, out JsonElement property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
    }

    private static string BuildSubagentMarkdown(ParsedToolResult parsed)
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine();
        builder.AppendLine();
        builder.Append("## ");
        builder.AppendLine(string.IsNullOrWhiteSpace(parsed.Description) ? "Subagent report" : parsed.Description.Trim());

        if (!string.IsNullOrWhiteSpace(parsed.AgentType))
        {
            builder.AppendLine();
            builder.Append("Agent type: `");
            builder.Append(parsed.AgentType.Trim());
            builder.AppendLine("`");
        }

        builder.AppendLine();
        builder.AppendLine(parsed.TextResultForLlm!.Trim());
        return builder.ToString();
    }

    private static string? SafeSerialize(object input)
    {
        try
        {
            string json = JsonSerializer.Serialize(input);
            return Redaction.RedactSecrets(json);
        }
        catch
        {
            return null;
        }
    }

    private static string? SafeSerializePostToolUseInput(PostToolUseHookInput input)
    {
        try
        {
            Dictionary<string, object?> snapshot = new Dictionary<string, object?>
            {
                ["timestamp"] = input.Timestamp,
                ["cwd"] = input.Cwd,
                ["toolName"] = input.ToolName,
                ["toolArgs"] = SnapshotValue(input.ToolArgs, depth: 0, new HashSet<object>(ReferenceEqualityComparer.Instance))
            };

            object? toolResult = input.ToolResult;
            if (toolResult is not null)
            {
                snapshot["toolResult"] = SnapshotValue(toolResult, depth: 0, new HashSet<object>(ReferenceEqualityComparer.Instance));
            }

            object? error = TryReadProperty(input, "Error");
            if (error is not null)
            {
                snapshot["error"] = SnapshotValue(error, depth: 0, new HashSet<object>(ReferenceEqualityComparer.Instance));
            }

            return SafeSerialize(snapshot) ?? BuildFallbackJson(snapshot);
        }
        catch
        {
            return null;
        }
    }

    private static object? SnapshotValue(object? value, int depth, HashSet<object> visited)
    {
        if (value is null)
        {
            return null;
        }

        Type type = value.GetType();
        if (IsScalarValue(value))
        {
            return value;
        }

        if (value is JsonElement jsonElement)
        {
            return jsonElement.Clone();
        }

        if (depth >= 3)
        {
            return value.ToString();
        }

        if (!type.IsValueType && !visited.Add(value))
        {
            return "(cycle)";
        }

        try
        {
            if (value is System.Collections.IDictionary dictionary)
            {
                return SnapshotDictionary(dictionary, depth, visited);
            }

            if (value is System.Collections.IEnumerable enumerable && value is not string)
            {
                return SnapshotEnumerable(enumerable, depth, visited);
            }

            return SnapshotObject(value, type, depth, visited);
        }
        finally
        {
            if (!type.IsValueType)
            {
                visited.Remove(value);
            }
        }
    }

    private static bool IsScalarValue(object value)
        => value is string
            || value is bool
            || value is char
            || value is byte
            || value is sbyte
            || value is short
            || value is ushort
            || value is int
            || value is uint
            || value is long
            || value is ulong
            || value is float
            || value is double
            || value is decimal
            || value is Guid
            || value is DateTime
            || value is DateTimeOffset
            || value is TimeSpan;

    private static Dictionary<string, object?> SnapshotDictionary(System.Collections.IDictionary dictionary, int depth, HashSet<object> visited)
    {
        Dictionary<string, object?> snapshot = new Dictionary<string, object?>();
        foreach (System.Collections.DictionaryEntry entry in dictionary)
        {
            string key = entry.Key?.ToString() ?? string.Empty;
            snapshot[key] = SnapshotValue(entry.Value, depth + 1, visited);
        }

        return snapshot;
    }

    private static List<object?> SnapshotEnumerable(System.Collections.IEnumerable enumerable, int depth, HashSet<object> visited)
    {
        List<object?> items = new List<object?>();
        int count = 0;
        foreach (object? item in enumerable)
        {
            if (count++ >= 32)
            {
                items.Add("(truncated)");
                break;
            }

            items.Add(SnapshotValue(item, depth + 1, visited));
        }

        return items;
    }

    private static object? SnapshotObject(object value, Type type, int depth, HashSet<object> visited)
    {
        Dictionary<string, object?> objectSnapshot = new Dictionary<string, object?>();
        foreach (System.Reflection.PropertyInfo property in type.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
        {
            AddPropertySnapshot(objectSnapshot, value, property, depth, visited);
        }

        return objectSnapshot.Count == 0 ? value.ToString() : objectSnapshot;
    }

    private static void AddPropertySnapshot(
        Dictionary<string, object?> objectSnapshot,
        object value,
        System.Reflection.PropertyInfo property,
        int depth,
        HashSet<object> visited)
    {
        if (property.GetIndexParameters().Length > 0 || !property.CanRead)
        {
            return;
        }

        object? propertyValue;
        try
        {
            propertyValue = property.GetValue(value);
        }
        catch (Exception ex)
        {
            propertyValue = $"(unavailable: {ex.GetType().Name})";
        }

        objectSnapshot[property.Name] = SnapshotValue(propertyValue, depth + 1, visited);
    }

    private static object? TryReadProperty(object instance, string propertyName)
    {
        try
        {
            return instance.GetType().GetProperty(propertyName)?.GetValue(instance);
        }
        catch
        {
            return null;
        }
    }

    private static string? BuildFallbackJson(Dictionary<string, object?> snapshot)
    {
        try
        {
            return JsonSerializer.Serialize(snapshot.ToDictionary(pair => pair.Key, pair => pair.Value?.ToString()));
        }
        catch
        {
            return null;
        }
    }

    private sealed record ParsedToolResult(string ToolName, string? Description, string? AgentType, string? TextResultForLlm);
}
