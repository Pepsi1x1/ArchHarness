using System.Collections.Concurrent;
using ArchHarness.App.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArchHarness.App.Copilot;

/// <summary>
/// Defines the contract for completing prompts via the Copilot service.
/// </summary>
public interface ICopilotClient
{
    /// <summary>
    /// Sends a prompt to the specified model and returns the completion text.
    /// </summary>
    /// <param name="model">The model identifier to use.</param>
    /// <param name="prompt">The prompt text to complete.</param>
    /// <param name="options">Optional completion configuration.</param>
    /// <param name="agentId">Optional agent identifier for tracking.</param>
    /// <param name="agentRole">Optional agent role for tracking.</param>
    /// <param name="cancellationToken">Token to signal cancellation.</param>
    /// <returns>The completion text from the model.</returns>
    Task<string> CompleteAsync(
        string model,
        string prompt,
        CopilotCompletionOptions? options = null,
        string? agentId = null,
        string? agentRole = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a snapshot of per-model usage counters accumulated during the session.
    /// </summary>
    /// <returns>A list of per-model usage records.</returns>
    IReadOnlyList<CopilotModelUsage> GetUsageSnapshot();
}

/// <summary>
/// Copilot client that handles retries, error classification, prompt bounding, and usage tracking.
/// </summary>
public sealed class CopilotClient : ICopilotClient
{
    private readonly ICopilotSessionFactory _sessionFactory;
    private readonly IModelResolver _modelResolver;
    private readonly ICopilotSessionEventStream _sessionEventStream;
    private readonly ILogger<CopilotClient> _logger;
    private readonly CopilotOptions _options;
    private readonly ConcurrentDictionary<string, UsageCounter> _usage = new ConcurrentDictionary<string, UsageCounter>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="CopilotClient"/> class.
    /// </summary>
    /// <param name="sessionFactory">Factory for creating Copilot sessions.</param>
    /// <param name="modelResolver">Resolver for validating and resolving model identifiers.</param>
    /// <param name="sessionEventStream">Stream for publishing session lifecycle events.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="options">Copilot configuration options.</param>
    public CopilotClient(
        ICopilotSessionFactory sessionFactory,
        IModelResolver modelResolver,
        ICopilotSessionEventStream sessionEventStream,
        ILogger<CopilotClient> logger,
        IOptions<CopilotOptions> options)
    {
        this._sessionFactory = sessionFactory;
        this._modelResolver = modelResolver;
        this._sessionEventStream = sessionEventStream;
        this._logger = logger;
        this._options = options.Value;
    }

    /// <inheritdoc />
    public async Task<string> CompleteAsync(
        string model,
        string prompt,
        CopilotCompletionOptions? options = null,
        string? agentId = null,
        string? agentRole = null,
        CancellationToken cancellationToken = default)
    {
        this._modelResolver.ValidateOrThrow(model);
        string boundedPrompt = BoundLength(prompt, this._options.MaxPromptCharacters);

        Exception? lastException = null;
        for (int attempt = 0; attempt <= this._options.MaxRetries; attempt++)
        {
            try
            {
                ICopilotSession session = this._sessionFactory.Create(model, options, agentId, agentRole);
                string completion = await session.CompleteAsync(boundedPrompt, cancellationToken);
                string boundedCompletion = BoundLength(completion, this._options.MaxCompletionCharacters);
                this.TrackUsage(model, boundedPrompt.Length, boundedCompletion.Length);
                return boundedCompletion;
            }
            catch (Exception ex)
            {
                lastException = ex;
                if (CopilotErrorClassifier.IsPermanent(ex))
                {
                    this._logger.LogError(ex, "Permanent Copilot completion error for model '{Model}'.", model);
                    this._sessionEventStream.Publish(new CopilotSessionLifecycleEvent(
                        DateTimeOffset.UtcNow,
                        "n/a",
                        model,
                        "client.completion.permanent_error",
                        ex.Message));
                    throw new InvalidOperationException(
                        $"Permanent Copilot completion error for model '{model}': {ex.Message}",
                        ex);
                }

                if (attempt >= this._options.MaxRetries || !CopilotErrorClassifier.IsTransient(ex))
                {
                    break;
                }

                int backoff = this._options.BaseRetryDelayMilliseconds * (int)Math.Pow(2, attempt);
                this._logger.LogWarning(
                    ex,
                    "Transient Copilot completion error for model '{Model}' on attempt {Attempt}; retrying in {BackoffMs}ms.",
                    model,
                    attempt + 1,
                    backoff);
                this._sessionEventStream.Publish(new CopilotSessionLifecycleEvent(
                    DateTimeOffset.UtcNow,
                    "n/a",
                    model,
                    "client.completion.transient_retry",
                    $"Attempt={attempt + 1}; BackoffMs={backoff}; Error={ex.Message}"));
                await Task.Delay(backoff, cancellationToken);
            }
        }

        throw new InvalidOperationException($"Copilot completion failed for model '{model}' after retries.", lastException);
    }

    /// <inheritdoc />
    public IReadOnlyList<CopilotModelUsage> GetUsageSnapshot()
        => this._usage.Select(pair => new CopilotModelUsage(
            pair.Key,
            pair.Value.Calls,
            pair.Value.PromptCharacters,
            pair.Value.CompletionCharacters)).ToArray();

    private static string BoundLength(string text, int maxCharacters)
    {
        if (maxCharacters <= 0 || text.Length <= maxCharacters)
        {
            return text;
        }

        return text[..maxCharacters];
    }

    private void TrackUsage(string model, int promptChars, int completionChars)
    {
        UsageCounter counter = this._usage.GetOrAdd(model, _ => new UsageCounter());
        counter.Increment(promptChars, completionChars);
    }

    private sealed class UsageCounter
    {
        public int Calls;
        public int PromptCharacters;
        public int CompletionCharacters;

        public void Increment(int promptChars, int completionChars)
        {
            Interlocked.Increment(ref this.Calls);
            Interlocked.Add(ref this.PromptCharacters, promptChars);
            Interlocked.Add(ref this.CompletionCharacters, completionChars);
        }
    }
}

/// <summary>
/// Defines the contract for a single Copilot session capable of completing prompts.
/// </summary>
public interface ICopilotSession
{
    /// <summary>
    /// Completes a prompt within the current session context.
    /// </summary>
    /// <param name="prompt">The prompt text to complete.</param>
    /// <param name="cancellationToken">Token to signal cancellation.</param>
    /// <returns>The completion text.</returns>
    Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken);
}
