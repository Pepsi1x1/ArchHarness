namespace ArchHarness.App.Core;

/// <summary>
/// Well-known roles for conversation messages inside a planning session.
/// </summary>
public static class ConversationRoles
{
    /// <summary>A message authored by the end user.</summary>
    public const string USER = "user";

    /// <summary>A message authored by the orchestration or planning agent.</summary>
    public const string ASSISTANT = "assistant";

    /// <summary>A system-authored message (e.g., handoff marker, status transition).</summary>
    public const string SYSTEM = "system";
}

/// <summary>
/// Well-known message kinds recorded in a planning-session conversation ledger.
/// </summary>
public static class ConversationMessageKinds
{
    /// <summary>A free-form user or assistant chat turn.</summary>
    public const string CHAT = "chat";

    /// <summary>A clarification question asked by the planning agent.</summary>
    public const string CLARIFICATION_QUESTION = "clarification-question";

    /// <summary>A clarification answer supplied by the user.</summary>
    public const string CLARIFICATION_ANSWER = "clarification-answer";

    /// <summary>A plan proposal published by the planning agent.</summary>
    public const string PLAN_PROPOSAL = "plan-proposal";

    /// <summary>A plan revision request from the user.</summary>
    public const string PLAN_REVISION = "plan-revision";

    /// <summary>A plan approval or rejection event.</summary>
    public const string PLAN_DECISION = "plan-decision";

    /// <summary>A handoff marker indicating a planning run spawned an implementation run.</summary>
    public const string HANDOFF = "handoff";

    /// <summary>A post-handoff follow-up request from the user targeting implementation.</summary>
    public const string FOLLOW_UP = "follow-up";
}

/// <summary>
/// Well-known attachment kinds carried by prompts and conversation messages.
/// </summary>
public static class PromptAttachmentKinds
{
    /// <summary>A raster or vector image supplied as visual context.</summary>
    public const string IMAGE = "image";
}

/// <summary>
/// A single attachment carried by a prompt or conversation message.
/// Attachments may be inlined via <paramref name="DataBase64"/> for small payloads,
/// or referenced via <paramref name="StoragePath"/> when persisted on disk.
/// Exactly one of the two MUST be populated.
/// </summary>
/// <param name="Id">Stable identifier for the attachment within its owning session/message.</param>
/// <param name="Kind">The attachment kind (e.g., <see cref="PromptAttachmentKinds.IMAGE"/>).</param>
/// <param name="MimeType">The IANA media type (e.g., "image/png").</param>
/// <param name="FileName">Optional original file name supplied by the user.</param>
/// <param name="SizeBytes">The size of the underlying content in bytes.</param>
/// <param name="DataBase64">Optional base64-encoded inline payload.</param>
/// <param name="StoragePath">Optional path to the persisted attachment payload.</param>
/// <param name="Caption">Optional user-supplied caption or description.</param>
public sealed record PromptAttachment(
    string Id,
    string Kind,
    string MimeType,
    string? FileName,
    long SizeBytes,
    string? DataBase64 = null,
    string? StoragePath = null,
    string? Caption = null);

/// <summary>
/// A single message in a planning-session conversation ledger.
/// </summary>
/// <param name="Id">Stable identifier for the message within its session.</param>
/// <param name="Role">The author role (see <see cref="ConversationRoles"/>).</param>
/// <param name="Kind">The message kind (see <see cref="ConversationMessageKinds"/>).</param>
/// <param name="Text">The message text. May be empty if only attachments are supplied.</param>
/// <param name="Attachments">Attachments carried by this message.</param>
/// <param name="TimestampUtc">When the message was recorded.</param>
/// <param name="AuthorAgent">Optional agent role when <paramref name="Role"/> is assistant.</param>
/// <param name="RelatedRunId">Optional run identifier this message was produced by or targets.</param>
public sealed record ConversationMessage(
    string Id,
    string Role,
    string Kind,
    string Text,
    IReadOnlyList<PromptAttachment> Attachments,
    DateTimeOffset TimestampUtc,
    string? AuthorAgent = null,
    string? RelatedRunId = null);

/// <summary>
/// A durable planning session that survives handoff and links a planning run with any
/// implementation runs it spawns. The session owns the conversation ledger, the current
/// clarification spec, the current approved plan hash, and any follow-up messages submitted
/// after handoff.
/// </summary>
/// <param name="Id">Stable planning-session identifier.</param>
/// <param name="CreatedAtUtc">When the session was created.</param>
/// <param name="UpdatedAtUtc">When the session was last mutated.</param>
/// <param name="PlanningRunId">The planning run that owns spec/plan generation.</param>
/// <param name="ImplementationRunId">Optional implementation run the session has handed off to.</param>
/// <param name="Messages">The ordered conversation ledger.</param>
/// <param name="Spec">The most recent clarification spec, if one was produced.</param>
/// <param name="Approval">The most recent plan approval, if one occurred.</param>
/// <param name="CurrentPlanHash">The most recently proposed plan hash, if any.</param>
public sealed record PlanningSession(
    string Id,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string PlanningRunId,
    string? ImplementationRunId,
    IReadOnlyList<ConversationMessage> Messages,
    ClarificationSpec? Spec = null,
    PlanApproval? Approval = null,
    string? CurrentPlanHash = null);
