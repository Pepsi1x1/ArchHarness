namespace ArchHarness.App.Copilot;

using ArchHarness.App.Core;

/// <summary>
/// Specifies how the system message is combined with any existing base system message.
/// </summary>
public enum CopilotSystemMessageMode
{
    /// <summary>Appends to the existing system message.</summary>
    Append,

    /// <summary>Replaces the existing system message entirely.</summary>
    Replace
}

/// <summary>
/// Options for configuring a Copilot completion request, including system messages and tool policies.
/// </summary>
public sealed class CopilotCompletionOptions
{
    /// <summary>Gets or sets the optional system message to include.</summary>
    public string? SystemMessage { get; init; }

    /// <summary>Gets or sets how the system message is applied.</summary>
    public CopilotSystemMessageMode SystemMessageMode { get; init; } = CopilotSystemMessageMode.Append;

    /// <summary>Gets or sets the optional reasoning effort to use for compatible models.</summary>
    public string? ReasoningEffort { get; init; }

    /// <summary>Gets or sets the list of tools explicitly available for this request.</summary>
    public IReadOnlyList<string>? AvailableTools { get; init; }

    /// <summary>Gets or sets the list of tools explicitly excluded from this request.</summary>
    public IReadOnlyList<string>? ExcludedTools { get; init; }

    /// <summary>
    /// Gets or sets optional prompt attachments (e.g., images) to include with the message.
    /// When non-null and non-empty, the completion request is treated as multimodal and routed
    /// through the SDK's attachment transport.
    /// </summary>
    public IReadOnlyList<PromptAttachment>? Attachments { get; init; }
}

