using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using ArchHarness.App.Core;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArchHarness.App.Copilot;

/// <summary>
/// Defines the contract for creating Copilot sessions with specific model and option configurations.
/// </summary>
public interface ICopilotSessionFactory
{
    /// <summary>
    /// Creates a new Copilot session for the specified model and options.
    /// </summary>
    /// <param name="model">The model identifier.</param>
    /// <param name="options">Optional completion configuration.</param>
    /// <param name="agentId">Optional agent identifier for tracking.</param>
    /// <param name="agentRole">Optional agent role for tracking.</param>
    /// <returns>A configured Copilot session.</returns>
    ICopilotSession Create(
        string model,
        CopilotCompletionOptions? options = null,
        string? agentId = null,
        string? agentRole = null);
}

/// <summary>
/// Creates and caches Copilot sessions, handling SDK integration and session lifecycle.
/// </summary>
public sealed class CopilotSessionFactory : ICopilotSessionFactory, IAsyncDisposable
{
    private readonly CopilotOptions _options;
    private readonly ICopilotClientProvider _clientProvider;
    private readonly SessionHooksDependencies _hooks;
    private readonly CopilotSessionContext _sessionContext;
    private readonly IRunContextAccessor _runContextAccessor;
    private readonly RuntimeStateAccessors _stateAccessors;
    private readonly ILogger<CopilotSessionFactory> _logger;
    private readonly ConcurrentDictionary<SessionCacheKey, Lazy<Task<SessionHandle>>> _sessionHandles = new ConcurrentDictionary<SessionCacheKey, Lazy<Task<SessionHandle>>>();
    private readonly int _sessionInactivityTimeoutSeconds;
    private readonly int _sessionAbsoluteTimeoutSeconds;

    /// <summary>
    /// Initializes a new instance of the <see cref="CopilotSessionFactory"/> class.
    /// </summary>
    /// <param name="options">Copilot configuration options.</param>
    /// <param name="clientProvider">Provides the initialized SDK client.</param>
    /// <param name="hooks">Grouped governance, user-input, and permission-prompt hook dependencies.</param>
    /// <param name="sessionContext">Grouped session runtime dependencies.</param>
    /// <param name="stateAccessors">Grouped async-local runtime state accessors.</param>
    /// <param name="logger">Logger instance.</param>
    public CopilotSessionFactory(
        IOptions<CopilotOptions> options,
        ICopilotClientProvider clientProvider,
        SessionHooksDependencies hooks,
        CopilotSessionContext sessionContext,
        IRunContextAccessor runContextAccessor,
        RuntimeStateAccessors stateAccessors,
        ILogger<CopilotSessionFactory> logger)
    {
        this._options = options.Value;
        this._clientProvider = clientProvider;
        this._hooks = hooks;
        this._sessionContext = sessionContext;
        this._runContextAccessor = runContextAccessor;
        this._stateAccessors = stateAccessors;
        this._logger = logger;
        this._sessionInactivityTimeoutSeconds = Math.Max(0, options.Value.SessionResponseTimeoutSeconds);
        this._sessionAbsoluteTimeoutSeconds = Math.Max(0, options.Value.SessionAbsoluteTimeoutSeconds);
    }

    /// <inheritdoc />
    public ICopilotSession Create(
        string model,
        CopilotCompletionOptions? options = null,
        string? agentId = null,
        string? agentRole = null)
        => new SdkCopilotSession(
            model,
            options,
            this,
            this._sessionContext,
            new SessionIdentity(agentId, agentRole),
            new SessionTimeoutSettings(
                this._sessionInactivityTimeoutSeconds,
                this._sessionAbsoluteTimeoutSeconds));

    /// <summary>
    /// Disposes all cached session handles. Client disposal is owned by <see cref="CopilotClientProvider"/>.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        foreach (Lazy<Task<SessionHandle>> lazyHandle in this._sessionHandles.Values)
        {
            if (lazyHandle.IsValueCreated && lazyHandle.Value.IsCompletedSuccessfully)
            {
                SessionHandle handle = await lazyHandle.Value;
                await handle.Session.DisposeAsync();
                handle.Gate.Dispose();
            }
        }
    }

    /// <summary>
    /// Pre-warms a session for the specified model so the first real request avoids cold-start latency.
    /// </summary>
    /// <param name="model">The model identifier to warm up.</param>
    /// <param name="options">Optional completion options that affect session configuration.</param>
    /// <param name="cancellationToken">Token to cancel the warm-up.</param>
    /// <returns>A task that completes when warm-up finishes or is abandoned.</returns>
    public Task WarmUpAsync(
        string model,
        CopilotCompletionOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(async () =>
        {
            try
            {
                await this._clientProvider.GetClientAsync().ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                await this.GetOrCreateSessionHandleAsync(model, options).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Warm-up was canceled by caller.
            }
            catch (Exception ex)
            {
                this._logger.LogWarning(ex, "Copilot session warm-up failed for model '{Model}'.", model);
            }
        }, CancellationToken.None);
    }

    internal Task<SessionHandle> GetOrCreateSessionHandleAsync(string model, CopilotCompletionOptions? options)
    {
        string workspaceRoot = ResolveWorkspaceRoot(this._stateAccessors.WorkspaceRoot.Current);
        string permissionHandlerMode = PermissionHandlerModes.Normalize(this._stateAccessors.PermissionHandlerMode.Current);
        string? runId = this._runContextAccessor.Current?.RunId;
        SessionCacheKey key = BuildSessionCacheKey(model, options, workspaceRoot, permissionHandlerMode, runId);
        Lazy<Task<SessionHandle>> lazy = this._sessionHandles.GetOrAdd(
            key,
            cacheKey => new Lazy<Task<SessionHandle>>(() => this.CreateSessionHandleAsync(model, options, permissionHandlerMode, cacheKey), LazyThreadSafetyMode.ExecutionAndPublication));
        return this.AwaitSessionHandleAsync(key, lazy);
    }

    private async Task<SessionHandle> AwaitSessionHandleAsync(SessionCacheKey key, Lazy<Task<SessionHandle>> lazy)
    {
        try
        {
            return await lazy.Value.ConfigureAwait(false);
        }
        catch
        {
            this._sessionHandles.TryRemove(key, out _);
            throw;
        }
    }

    private async Task<SessionHandle> CreateSessionHandleAsync(string model, CopilotCompletionOptions? requestOptions, string permissionHandlerMode, SessionCacheKey cacheKey)
    {
        try
        {
            GitHub.Copilot.SDK.CopilotClient client = await this._clientProvider.GetClientAsync().ConfigureAwait(false);
            SessionConfig config = this.CreateSessionConfig(model, requestOptions, permissionHandlerMode, cacheKey);
            string? stableSessionId = BuildStableSessionId(cacheKey);
            CopilotSession session = await this.CreateOrResumeSessionAsync(client, config, stableSessionId, model, permissionHandlerMode, cacheKey).ConfigureAwait(false);

            return new SessionHandle(session, new SemaphoreSlim(1, 1));
        }
        catch (Exception ex)
        {
            string eventType = CopilotErrorClassifier.IsPermanent(ex)
                ? "session.create.permanent_error"
                : "session.create.transient_error";
            this._sessionContext.SessionEventStream.Publish(new CopilotSessionLifecycleEvent(
                DateTimeOffset.UtcNow,
                "n/a",
                model,
                eventType,
                ex.Message));
            if (CopilotErrorClassifier.IsPermanent(ex))
            {
                this._logger.LogError(ex, "Permanent Copilot session creation error for model '{Model}'.", model);
            }
            else
            {
                this._logger.LogWarning(ex, "Transient Copilot session creation error for model '{Model}'.", model);
            }

            throw;
        }
    }

    private SessionConfig CreateSessionConfig(string model, CopilotCompletionOptions? requestOptions, string permissionHandlerMode, SessionCacheKey cacheKey)
    {
        SessionConfig config = new SessionConfig
        {
            Model = model,
            Streaming = this._options.StreamingResponses,
            OnPermissionRequest = this.ResolvePermissionHandler(permissionHandlerMode),
            OnUserInputRequest = async (request, _) => await this._hooks.UserInputBridge.RequestInputAsync(request).ConfigureAwait(false),
            Hooks = this.CreateSessionHooks()
        };

        string? stableSessionId = BuildStableSessionId(cacheKey);
        if (!string.IsNullOrWhiteSpace(stableSessionId))
        {
            config.SessionId = stableSessionId;
        }

        if (!string.IsNullOrWhiteSpace(requestOptions?.SystemMessage))
        {
            config.SystemMessage = new SystemMessageConfig
            {
                Mode = requestOptions.SystemMessageMode == CopilotSystemMessageMode.Replace
                    ? SystemMessageMode.Replace
                    : SystemMessageMode.Append,
                Content = requestOptions.SystemMessage
            };
        }

        IReadOnlyList<string>? availableTools = requestOptions?.AvailableTools is { Count: > 0 }
            ? requestOptions.AvailableTools
            : this._options.AvailableTools;
        if (availableTools.Count > 0)
        {
            config.AvailableTools = availableTools.ToList();
        }

        string[] excludedTools = MergeExcludedTools(this._options.ExcludedTools, requestOptions?.ExcludedTools);
        if (excludedTools.Length > 0)
        {
            config.ExcludedTools = excludedTools.ToList();
        }

        return config;
    }

    private async Task<CopilotSession> CreateOrResumeSessionAsync(GitHub.Copilot.SDK.CopilotClient client, SessionConfig config, string? stableSessionId, string model, string permissionHandlerMode, SessionCacheKey cacheKey)
    {
        if (string.IsNullOrWhiteSpace(stableSessionId))
        {
            return await client.CreateSessionAsync(config).ConfigureAwait(false);
        }

        try
        {
            return await client.ResumeSessionAsync(stableSessionId, new ResumeSessionConfig
            {
                OnPermissionRequest = this.ResolvePermissionHandler(permissionHandlerMode),
                OnUserInputRequest = async (request, _) => await this._hooks.UserInputBridge.RequestInputAsync(request).ConfigureAwait(false),
                Hooks = this.CreateSessionHooks(),
                Model = model,
                Streaming = this._options.StreamingResponses,
                SystemMessage = config.SystemMessage,
                AvailableTools = config.AvailableTools,
                ExcludedTools = config.ExcludedTools,
                WorkingDirectory = cacheKey.WorkspaceRoot
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            this._logger.LogDebug(ex, "Falling back to new Copilot session '{SessionId}' for run-scoped cache key.", stableSessionId);
            return await client.CreateSessionAsync(config).ConfigureAwait(false);
        }
    }

    private SessionHooks CreateSessionHooks()
        => new SessionHooks
        {
            OnPreToolUse = async (input, _) => await this._hooks.Governance.OnPreToolUseAsync(input).ConfigureAwait(false),
            OnPostToolUse = async (input, _) => await this._hooks.Governance.OnPostToolUseAsync(input).ConfigureAwait(false)
        };

    private PermissionRequestHandler ResolvePermissionHandler(string permissionHandlerMode)
        => string.Equals(permissionHandlerMode, PermissionHandlerModes.PROMPT, StringComparison.OrdinalIgnoreCase)
            ? this._hooks.PermissionPromptHandler.HandleAsync
            : PermissionHandler.ApproveAll;

    private static SessionCacheKey BuildSessionCacheKey(string model, CopilotCompletionOptions? options, string workspaceRoot, string permissionHandlerMode, string? runId)
    {
        string systemMessage = options?.SystemMessage ?? string.Empty;
        CopilotSystemMessageMode mode = options?.SystemMessageMode ?? CopilotSystemMessageMode.Append;
        string available = NormalizeToolList(options?.AvailableTools);
        string excluded = NormalizeToolList(options?.ExcludedTools);
        return new SessionCacheKey(model, systemMessage, mode, available, excluded, workspaceRoot, permissionHandlerMode, runId ?? string.Empty);
    }

    private static string? BuildStableSessionId(SessionCacheKey key)
    {
        if (string.IsNullOrWhiteSpace(key.RunId))
        {
            return null;
        }

        string rawKey = string.Join("|", key.Model, key.SystemMessageMode, key.SystemMessage, key.AvailableTools, key.ExcludedTools, key.PermissionHandlerMode, key.WorkspaceRoot);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
        string suffix = Convert.ToHexString(hash[..8]).ToLowerInvariant();
        return $"archharness-{key.RunId}-{suffix}";
    }

    private static string ResolveWorkspaceRoot(string? workspaceRoot)
        => string.IsNullOrWhiteSpace(workspaceRoot)
            ? Directory.GetCurrentDirectory()
            : Path.GetFullPath(Environment.ExpandEnvironmentVariables(workspaceRoot));

    private static string NormalizeToolList(IReadOnlyList<string>? tools)
    {
        if (tools is null || tools.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(",", tools
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t, StringComparer.OrdinalIgnoreCase));
    }

    private static string[] MergeExcludedTools(IReadOnlyList<string> global, IReadOnlyList<string>? additional)
    {
        string[] merged = global
            .Concat(additional ?? Array.Empty<string>())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return merged;
    }

    private sealed record SessionCacheKey(
        string Model,
        string SystemMessage,
        CopilotSystemMessageMode SystemMessageMode,
        string AvailableTools,
        string ExcludedTools,
        string WorkspaceRoot,
        string PermissionHandlerMode,
        string RunId);

    internal sealed record SessionHandle(CopilotSession Session, SemaphoreSlim Gate);

    /// <summary>
    /// Groups the governance policy, user-input bridge, and permission prompt handler
    /// dependencies used during session creation.
    /// </summary>
    public sealed class SessionHooksDependencies
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SessionHooksDependencies"/> class.
        /// </summary>
        /// <param name="governance">Governance policy for tool-use hooks.</param>
        /// <param name="userInputBridge">Bridge for forwarding user-input requests from the SDK.</param>
        /// <param name="permissionPromptHandler">Handler for interactive console permission prompts.</param>
        public SessionHooksDependencies(
            ICopilotGovernancePolicy governance,
            ICopilotUserInputBridge userInputBridge,
            ICopilotPermissionPromptHandler permissionPromptHandler)
        {
            this.Governance = governance;
            this.UserInputBridge = userInputBridge;
            this.PermissionPromptHandler = permissionPromptHandler;
        }

        /// <summary>Gets the governance policy for tool-use hooks.</summary>
        public ICopilotGovernancePolicy Governance { get; }

        /// <summary>Gets the bridge for forwarding user-input requests from the SDK.</summary>
        public ICopilotUserInputBridge UserInputBridge { get; }

        /// <summary>Gets the handler for interactive console permission prompts.</summary>
        public ICopilotPermissionPromptHandler PermissionPromptHandler { get; }
    }

    /// <summary>
    /// Groups the session-scoped runtime dependencies injected into each <see cref="ICopilotSession"/>.
    /// </summary>
    public sealed class CopilotSessionContext
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CopilotSessionContext"/> class.
        /// </summary>
        /// <param name="userInputState">Tracks whether the agent is awaiting user input.</param>
        /// <param name="sessionEventStream">Publishes session lifecycle events.</param>
        /// <param name="agentStream">Publishes real-time agent delta content events.</param>
        public CopilotSessionContext(
            IUserInputState userInputState,
            ICopilotSessionEventStream sessionEventStream,
            IAgentStreamEventStream agentStream)
        {
            this.UserInputState = userInputState;
            this.SessionEventStream = sessionEventStream;
            this.AgentStream = agentStream;
        }

        /// <summary>Gets the user-input state tracker.</summary>
        public IUserInputState UserInputState { get; }

        /// <summary>Gets the session lifecycle event stream.</summary>
        public ICopilotSessionEventStream SessionEventStream { get; }

        /// <summary>Gets the agent delta content event stream.</summary>
        public IAgentStreamEventStream AgentStream { get; }
    }
}
