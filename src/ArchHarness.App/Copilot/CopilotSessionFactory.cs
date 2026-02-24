using System.Collections.Concurrent;
using System.Text;
using ArchHarness.App.Core;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArchHarness.App.Copilot;

public interface ICopilotSessionFactory
{
    ICopilotSession Create(
        string model,
        CopilotCompletionOptions? options = null,
        string? agentId = null,
        string? agentRole = null);
}

public sealed class CopilotSessionFactory : ICopilotSessionFactory, IAsyncDisposable
{
    private readonly CopilotOptions _options;
    private readonly CopilotClientProvider _clientProvider;
    private readonly ICopilotGovernancePolicy _governance;
    private readonly ICopilotUserInputBridge _userInputBridge;
    private readonly CopilotSessionContext _sessionContext;
    private readonly ILogger<CopilotSessionFactory> _logger;
    private readonly ConcurrentDictionary<SessionCacheKey, Lazy<Task<SessionHandle>>> _sessionHandles = new();
    private readonly int _sessionInactivityTimeoutSeconds;
    private readonly int _sessionAbsoluteTimeoutSeconds;

    /// <summary>
    /// Initializes a new instance of the <see cref="CopilotSessionFactory"/> class.
    /// </summary>
    /// <param name="options">Copilot configuration options.</param>
    /// <param name="clientProvider">Provides the initialized SDK client.</param>
    /// <param name="governance">Governance policy for tool-use hooks.</param>
    /// <param name="userInputBridge">Bridge for forwarding user-input requests from the SDK.</param>
    /// <param name="sessionContext">Grouped session runtime dependencies.</param>
    /// <param name="logger">Logger instance.</param>
    public CopilotSessionFactory(
        IOptions<CopilotOptions> options,
        CopilotClientProvider clientProvider,
        ICopilotGovernancePolicy governance,
        ICopilotUserInputBridge userInputBridge,
        CopilotSessionContext sessionContext,
        ILogger<CopilotSessionFactory> logger)
    {
        _options = options.Value;
        _clientProvider = clientProvider;
        _governance = governance;
        _userInputBridge = userInputBridge;
        _sessionContext = sessionContext;
        _logger = logger;
        _sessionInactivityTimeoutSeconds = Math.Max(0, options.Value.SessionResponseTimeoutSeconds);
        _sessionAbsoluteTimeoutSeconds = Math.Max(0, options.Value.SessionAbsoluteTimeoutSeconds);
    }

    public ICopilotSession Create(
        string model,
        CopilotCompletionOptions? options = null,
        string? agentId = null,
        string? agentRole = null)
        => new SdkCopilotSession(
            model,
            options,
            this,
            _sessionContext,
            new SessionIdentity(agentId, agentRole),
            new SessionTimeoutSettings(
                _sessionInactivityTimeoutSeconds,
                _sessionAbsoluteTimeoutSeconds));

    /// <summary>
    /// Disposes all cached session handles. Client disposal is owned by <see cref="CopilotClientProvider"/>.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        foreach (var lazyHandle in _sessionHandles.Values)
        {
            if (lazyHandle.IsValueCreated && lazyHandle.Value.IsCompletedSuccessfully)
            {
                var handle = await lazyHandle.Value;
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
                await _clientProvider.GetClientAsync().ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                await GetOrCreateSessionHandleAsync(model, options).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Warm-up was canceled by caller.
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Copilot session warm-up failed for model '{Model}'.", model);
            }
        }, CancellationToken.None);
    }

    internal Task<SessionHandle> GetOrCreateSessionHandleAsync(string model, CopilotCompletionOptions? options)
    {
        var key = BuildSessionCacheKey(model, options);
        var lazy = _sessionHandles.GetOrAdd(
            key,
            cacheKey => new Lazy<Task<SessionHandle>>(() => CreateSessionHandleAsync(model, options), LazyThreadSafetyMode.ExecutionAndPublication));
        return lazy.Value;
    }

    private async Task<SessionHandle> CreateSessionHandleAsync(string model, CopilotCompletionOptions? requestOptions)
    {
        var client = await _clientProvider.GetClientAsync();
        var config = new SessionConfig
        {
            Model = model,
            Streaming = _options.StreamingResponses,
            OnUserInputRequest = async (request, _) => await _userInputBridge.RequestInputAsync(request),
            Hooks = new SessionHooks
            {
                OnPreToolUse = async (input, _) => await _governance.OnPreToolUseAsync(input),
                OnPostToolUse = async (input, _) => await _governance.OnPostToolUseAsync(input)
            }
        };

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

        var availableTools = requestOptions?.AvailableTools is { Count: > 0 }
            ? requestOptions.AvailableTools
            : _options.AvailableTools;
        if (availableTools.Count > 0)
        {
            config.AvailableTools = availableTools.ToList();
        }

        var excludedTools = MergeExcludedTools(_options.ExcludedTools, requestOptions?.ExcludedTools);
        if (excludedTools.Length > 0)
        {
            config.ExcludedTools = excludedTools.ToList();
        }

        var session = await client.CreateSessionAsync(config);
        return new SessionHandle(session, new SemaphoreSlim(1, 1));
    }

    private static SessionCacheKey BuildSessionCacheKey(string model, CopilotCompletionOptions? options)
    {
        var systemMessage = options?.SystemMessage ?? string.Empty;
        var mode = options?.SystemMessageMode ?? CopilotSystemMessageMode.Append;
        var available = NormalizeToolList(options?.AvailableTools);
        var excluded = NormalizeToolList(options?.ExcludedTools);
        return new SessionCacheKey(model, systemMessage, mode, available, excluded);
    }

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
        var merged = global
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
        string ExcludedTools);

    internal sealed record SessionHandle(CopilotSession Session, SemaphoreSlim Gate);

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
            UserInputState = userInputState;
            SessionEventStream = sessionEventStream;
            AgentStream = agentStream;
        }

        /// <summary>Gets the user-input state tracker.</summary>
        public IUserInputState UserInputState { get; }

        /// <summary>Gets the session lifecycle event stream.</summary>
        public ICopilotSessionEventStream SessionEventStream { get; }

        /// <summary>Gets the agent delta content event stream.</summary>
        public IAgentStreamEventStream AgentStream { get; }
    }
}
