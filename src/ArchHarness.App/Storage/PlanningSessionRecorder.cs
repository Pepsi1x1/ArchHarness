using ArchHarness.App.Core;

namespace ArchHarness.App.Storage;

/// <summary>
/// High-level helpers for recording conversation turns into an <see cref="IPlanningSessionStore"/>.
/// Centralizes message-id generation, timestamping, and session creation so callers do not
/// re-implement ledger bookkeeping.
/// </summary>
public sealed class PlanningSessionRecorder
{
    private readonly IPlanningSessionStore _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlanningSessionRecorder"/> class.
    /// </summary>
    /// <param name="store">The durable planning-session store.</param>
    public PlanningSessionRecorder(IPlanningSessionStore store)
    {
        this._store = store;
    }

    /// <summary>
    /// Ensures a session exists for the given id, creating an empty one when absent.
    /// </summary>
    public async Task<PlanningSession> EnsureAsync(
        string workspaceRoot,
        string sessionId,
        string planningRunId,
        CancellationToken cancellationToken)
    {
        PlanningSession? existing = this._store.Get(workspaceRoot, sessionId);
        if (existing is not null)
        {
            return existing;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        PlanningSession created = new(
            sessionId,
            now,
            now,
            planningRunId,
            ImplementationRunId: null,
            Messages: Array.Empty<ConversationMessage>());
        await this._store.WriteAsync(workspaceRoot, created, cancellationToken).ConfigureAwait(false);
        return created;
    }

    /// <summary>
    /// Loads the session with the given id, or null when absent.
    /// </summary>
    public PlanningSession? Get(string workspaceRoot, string sessionId)
        => this._store.Get(workspaceRoot, sessionId);

    /// <summary>
    /// Creates a conversation message with recorder-owned id and timestamp metadata.
    /// </summary>
    public static ConversationMessage CreateMessage(
        string role,
        string kind,
        string text,
        IReadOnlyList<PromptAttachment>? attachments = null,
        string? authorAgent = null,
        string? relatedRunId = null)
        => new(
            Guid.NewGuid().ToString("N"),
            role,
            kind,
            text ?? string.Empty,
            attachments ?? Array.Empty<PromptAttachment>(),
            DateTimeOffset.UtcNow,
            authorAgent,
            relatedRunId);

    /// <summary>
    /// Appends a single message to the session's conversation ledger.
    /// </summary>
    public Task<PlanningSession?> AppendMessageAsync(
        string workspaceRoot,
        string sessionId,
        ConversationMessage message,
        CancellationToken cancellationToken = default)
    {
        return this._store.UpdateAsync(
            workspaceRoot,
            sessionId,
            existing =>
            {
                if (existing is null)
                {
                    // Cannot append to a non-existent session.
                    return null;
                }

                List<ConversationMessage> next = new(existing.Messages.Count + 1);
                next.AddRange(existing.Messages);
                next.Add(message);
                return existing with { Messages = next };
            },
            cancellationToken);
    }

    /// <summary>
    /// Links an implementation run id to the session (at handoff time).
    /// </summary>
    public Task<PlanningSession?> LinkImplementationRunAsync(
        string workspaceRoot,
        string sessionId,
        string implementationRunId,
        CancellationToken cancellationToken)
        => this._store.UpdateAsync(
            workspaceRoot,
            sessionId,
            existing =>
            {
                if (existing is null)
                {
                    return null;
                }

                return existing with { ImplementationRunId = implementationRunId };
            },
            cancellationToken);

    /// <summary>
    /// Updates the session's current spec/approval/plan hash metadata.
    /// </summary>
    public Task<PlanningSession?> UpdateArtifactsAsync(
        string workspaceRoot,
        string sessionId,
        ClarificationSpec? spec,
        PlanApproval? approval,
        string? currentPlanHash,
        CancellationToken cancellationToken)
        => this._store.UpdateAsync(
            workspaceRoot,
            sessionId,
            existing =>
            {
                if (existing is null)
                {
                    return null;
                }

                return existing with
                {
                    Spec = spec ?? existing.Spec,
                    Approval = approval ?? existing.Approval,
                    CurrentPlanHash = currentPlanHash ?? existing.CurrentPlanHash,
                };
            },
            cancellationToken);
}
