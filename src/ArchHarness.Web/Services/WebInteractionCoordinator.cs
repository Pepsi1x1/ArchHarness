using ArchHarness.App.Copilot;
using GitHub.Copilot.SDK;

namespace ArchHarness.Web.Services;

/// <summary>
/// Coordinates pending web-host interactions for user input and permission requests.
/// </summary>
public sealed class WebInteractionCoordinator
{
    private readonly IUserInputState _state;
    private readonly SemaphoreSlim _interactionGate = new SemaphoreSlim(1, 1);
    private readonly object _sync = new object();
    private PendingInteraction? _pending;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebInteractionCoordinator"/> class.
    /// </summary>
    /// <param name="state">Shared awaiting-input state.</param>
    public WebInteractionCoordinator(IUserInputState state)
    {
        this._state = state;
    }

    /// <summary>
    /// Returns the active pending interaction, if one exists.
    /// </summary>
    /// <returns>The current pending interaction snapshot, or null.</returns>
    public PendingInteractionSnapshot? GetPending()
    {
        lock (this._sync)
        {
            return this._pending?.Snapshot;
        }
    }

    /// <summary>
    /// Queues a user-input request and waits for the web client to respond.
    /// </summary>
    /// <param name="request">The user-input request.</param>
    /// <returns>The completed input response.</returns>
    public async Task<UserInputResponse> RequestUserInputAsync(UserInputRequest request)
    {
        await this._interactionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            string question = request.Question ?? string.Empty;
            this._state.SetAwaiting(question);
            TaskCompletionSource<object> responseSource = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            PendingInteractionSnapshot snapshot = new PendingInteractionSnapshot(
                Kind: "user-input",
                Question: question,
                Choices: request.Choices?.ToArray() ?? Array.Empty<string>(),
                PermissionKind: null,
                SessionId: null,
                ToolName: null);

            lock (this._sync)
            {
                this._pending = new PendingInteraction("user-input", snapshot, responseSource);
            }

            object response = await responseSource.Task.ConfigureAwait(false);
            return (UserInputResponse)response;
        }
        finally
        {
            lock (this._sync)
            {
                this._pending = null;
            }

            this._state.Clear();
            this._interactionGate.Release();
        }
    }

    /// <summary>
    /// Queues multiple user-input requests for one interaction round and waits for the web client to respond.
    /// </summary>
    /// <param name="requests">The ordered user-input requests.</param>
    /// <returns>The completed input responses in order.</returns>
    public async Task<IReadOnlyList<UserInputResponse>> RequestUserInputsAsync(IReadOnlyList<UserInputRequest> requests)
    {
        ArgumentNullException.ThrowIfNull(requests);

        if (requests.Count == 0)
        {
            return Array.Empty<UserInputResponse>();
        }

        if (requests.Count == 1)
        {
            return new[] { await this.RequestUserInputAsync(requests[0]).ConfigureAwait(false) };
        }

        await this._interactionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            string question = $"Answer {requests.Count} planning questions to continue.";
            this._state.SetAwaiting(question);
            TaskCompletionSource<object> responseSource = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            PendingInteractionSnapshot snapshot = new PendingInteractionSnapshot(
                Kind: "user-input",
                Question: question,
                Choices: Array.Empty<string>(),
                PermissionKind: null,
                SessionId: null,
                ToolName: null,
                Questions: requests.Select(request => request.Question ?? string.Empty).ToArray());

            lock (this._sync)
            {
                this._pending = new PendingInteraction("user-input", snapshot, responseSource);
            }

            object response = await responseSource.Task.ConfigureAwait(false);
            return (IReadOnlyList<UserInputResponse>)response;
        }
        finally
        {
            lock (this._sync)
            {
                this._pending = null;
            }

            this._state.Clear();
            this._interactionGate.Release();
        }
    }

    /// <summary>
    /// Queues a permission request and waits for the web client to approve or deny it.
    /// </summary>
    /// <param name="request">The permission request details.</param>
    /// <param name="invocation">The invocation context.</param>
    /// <returns>The completed permission decision.</returns>
    public async Task<PermissionRequestResult> RequestPermissionAsync(PermissionRequest request, PermissionInvocation invocation)
    {
        await this._interactionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            string question = PermissionPromptFormatter.BuildQuestion(request, invocation);
            this._state.SetAwaiting(question);
            TaskCompletionSource<object> responseSource = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            PendingInteractionSnapshot snapshot = new PendingInteractionSnapshot(
                Kind: "permission",
                Question: question,
                Choices: Array.Empty<string>(),
                PermissionKind: request.Kind.ToString(),
                SessionId: invocation.SessionId,
                ToolName: ResolveToolName(request));

            lock (this._sync)
            {
                this._pending = new PendingInteraction("permission", snapshot, responseSource);
            }

            object response = await responseSource.Task.ConfigureAwait(false);
            return (PermissionRequestResult)response;
        }
        finally
        {
            lock (this._sync)
            {
                this._pending = null;
            }

            this._state.Clear();
            this._interactionGate.Release();
        }
    }

    /// <summary>
    /// Submits a response for the active user-input request.
    /// </summary>
    /// <param name="answer">The answer text supplied by the web client.</param>
    /// <returns>True when a pending user-input request was completed.</returns>
    public bool TrySubmitUserInput(string? answer)
    {
        lock (this._sync)
        {
            if (this._pending is null || !string.Equals(this._pending.Kind, "user-input", StringComparison.Ordinal))
            {
                return false;
            }

            string resolvedAnswer = string.IsNullOrWhiteSpace(answer) && this._pending.Snapshot.Choices.Count > 0
                ? this._pending.Snapshot.Choices[0]
                : answer ?? string.Empty;

            return this._pending.ResponseSource.TrySetResult(new UserInputResponse
            {
                Answer = resolvedAnswer,
                WasFreeform = true
            });
        }
    }

    /// <summary>
    /// Submits responses for the active batched user-input request.
    /// </summary>
    /// <param name="answers">The ordered answer texts supplied by the web client.</param>
    /// <returns>True when a pending batched user-input request was completed.</returns>
    public bool TrySubmitUserInputs(IReadOnlyList<string>? answers)
    {
        lock (this._sync)
        {
            if (this._pending is null || !string.Equals(this._pending.Kind, "user-input", StringComparison.Ordinal))
            {
                return false;
            }

            IReadOnlyList<string> questions = this._pending.Snapshot.Questions ?? Array.Empty<string>();
            if (questions.Count == 0)
            {
                return false;
            }

            List<UserInputResponse> responses = new List<UserInputResponse>(questions.Count);
            for (int index = 0; index < questions.Count; index++)
            {
                string answer = index < (answers?.Count ?? 0)
                    ? answers![index] ?? string.Empty
                    : string.Empty;

                responses.Add(new UserInputResponse
                {
                    Answer = answer,
                    WasFreeform = true
                });
            }

            return this._pending.ResponseSource.TrySetResult(responses);
        }
    }

    /// <summary>
    /// Submits a decision for the active permission request.
    /// </summary>
    /// <param name="approved">Whether the request is approved.</param>
    /// <returns>True when a pending permission request was completed.</returns>
    public bool TrySubmitPermission(bool approved)
    {
        lock (this._sync)
        {
            if (this._pending is null || !string.Equals(this._pending.Kind, "permission", StringComparison.Ordinal))
            {
                return false;
            }

            PermissionRequestResultKind kind = approved
                ? PermissionRequestResultKind.Approved
                : PermissionRequestResultKind.DeniedInteractivelyByUser;
            return this._pending.ResponseSource.TrySetResult(new PermissionRequestResult { Kind = kind });
        }
    }

    private static string? ResolveToolName(PermissionRequest request)
        => request switch
        {
            PermissionRequestMcp mcp => $"{mcp.ServerName}/{mcp.ToolName}",
            PermissionRequestCustomTool customTool => customTool.ToolName,
            PermissionRequestHook hook => hook.ToolName,
            _ => null
        };

    private sealed record PendingInteraction(string Kind, PendingInteractionSnapshot Snapshot, TaskCompletionSource<object> ResponseSource);

    /// <summary>
    /// Queues a plan-approval request and waits for the web client to respond.
    /// </summary>
    public async Task<ArchHarness.App.Core.PlanApprovalResponse> RequestPlanApprovalAsync(
        ArchHarness.App.Core.PlanApprovalRequest request)
    {
        await this._interactionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            string question = $"Review and approve the execution plan:\n{request.SpecMarkdown}";
            this._state.SetAwaiting(question);
            TaskCompletionSource<object> responseSource = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            PendingInteractionSnapshot snapshot = new PendingInteractionSnapshot(
                Kind: "plan-approval",
                Question: question,
                Choices: new[] { "Approve", "Regenerate", "Cancel" },
                PermissionKind: null,
                SessionId: null,
                ToolName: null,
                SpecMarkdown: request.SpecMarkdown,
                PlanSummary: request.PlanSummary);

            lock (this._sync)
            {
                this._pending = new PendingInteraction("plan-approval", snapshot, responseSource);
            }

            object response = await responseSource.Task.ConfigureAwait(false);
            return (ArchHarness.App.Core.PlanApprovalResponse)response;
        }
        finally
        {
            lock (this._sync)
            {
                this._pending = null;
            }

            this._state.Clear();
            this._interactionGate.Release();
        }
    }

    /// <summary>
    /// Submits an approval decision for the active plan-approval request.
    /// </summary>
    /// <param name="decision">The decision: approved, regenerate, or canceled.</param>
    /// <param name="reason">Optional reason for the decision.</param>
    /// <returns>True when a pending plan-approval request was completed.</returns>
    public bool TrySubmitPlanApproval(string decision, string? reason = null)
    {
        lock (this._sync)
        {
            if (this._pending is null || !string.Equals(this._pending.Kind, "plan-approval", StringComparison.Ordinal))
            {
                return false;
            }

            return this._pending.ResponseSource.TrySetResult(
                new ArchHarness.App.Core.PlanApprovalResponse(decision, reason));
        }
    }
}
