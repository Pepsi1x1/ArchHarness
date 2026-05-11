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
    private readonly ICopilotClientProvider _clientProvider;
    private readonly IModelResolver _modelResolver;
    private readonly ICopilotSessionEventStream _sessionEventStream;
    private readonly ILogger<CopilotClient> _logger;
    private readonly CopilotOptions _options;
    private readonly ConcurrentDictionary<string, UsageCounter> _usage = new ConcurrentDictionary<string, UsageCounter>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="CopilotClient"/> class.
    /// </summary>
    /// <param name="sessionFactory">Factory for creating Copilot sessions.</param>
    /// <param name="clientProvider">Provider for the underlying SDK client.</param>
    /// <param name="modelResolver">Resolver for validating and resolving model identifiers.</param>
    /// <param name="sessionEventStream">Stream for publishing session lifecycle events.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="options">Copilot configuration options.</param>
    public CopilotClient(
        ICopilotSessionFactory sessionFactory,
        ICopilotClientProvider clientProvider,
        IModelResolver modelResolver,
        ICopilotSessionEventStream sessionEventStream,
        ILogger<CopilotClient> logger,
        IOptions<CopilotOptions> options)
    {
        this._sessionFactory = sessionFactory;
        this._clientProvider = clientProvider;
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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                this._sessionFactory.Invalidate(model, options);
                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;
                this.ThrowIfPermanentCompletionError(model, ex);
                if (!this.ShouldRetryCompletion(attempt, ex))
                {
                    break;
                }

                await this.ResetSessionIfNeededAsync(model, options, ex).ConfigureAwait(false);
                int backoff = this.ReportTransientRetry(model, attempt, ex);
                await Task.Delay(backoff, cancellationToken);
            }
        }

        throw new InvalidOperationException($"Copilot completion failed for model '{model}' after retries.", lastException);
    }

    private void ThrowIfPermanentCompletionError(string model, Exception exception)
    {
        if (!CopilotErrorClassifier.IsPermanent(exception))
        {
            return;
        }

        this._logger.LogError(exception, "Permanent Copilot completion error for model '{Model}'.", model);
        this.PublishClientLifecycleEvent(model, "client.completion.permanent_error", exception.Message);
        throw new InvalidOperationException(
            $"Permanent Copilot completion error for model '{model}': {exception.Message}",
            exception);
    }

    private bool ShouldRetryCompletion(int attempt, Exception exception)
        => attempt < this._options.MaxRetries && CopilotErrorClassifier.IsTransient(exception);

    private async Task ResetSessionIfNeededAsync(string model, CopilotCompletionOptions? options, Exception exception)
    {
        if (!CopilotErrorClassifier.RequiresSessionReset(exception))
        {
            return;
        }

        this._sessionFactory.Invalidate(model, options);
        this.PublishClientLifecycleEvent(model, "client.completion.session_reset", exception.Message);
        await this.RecycleClientIfNeededAsync(model, exception).ConfigureAwait(false);
    }

    private async Task RecycleClientIfNeededAsync(string model, Exception exception)
    {
        if (!CopilotErrorClassifier.RequiresClientRecycle(exception))
        {
            return;
        }

        try
        {
            await this._clientProvider.InvalidateAsync().ConfigureAwait(false);
        }
        catch (Exception recycleEx)
        {
            this._logger.LogDebug(recycleEx, "Client provider recycle failed.");
        }

        this.PublishClientLifecycleEvent(model, "client.completion.client_recycled", exception.Message);
    }

    private int ReportTransientRetry(string model, int attempt, Exception exception)
    {
        int backoff = this._options.BaseRetryDelayMilliseconds * (int)Math.Pow(2, attempt);
        this._logger.LogWarning(
            exception,
            "Transient Copilot completion error for model '{Model}' on attempt {Attempt}; retrying in {BackoffMs}ms.",
            model,
            attempt + 1,
            backoff);
        this.PublishClientLifecycleEvent(
            model,
            "client.completion.transient_retry",
            $"Attempt={attempt + 1}; BackoffMs={backoff}; Error={exception.Message}");
        return backoff;
    }

    private void PublishClientLifecycleEvent(string model, string eventType, string details)
        => this._sessionEventStream.Publish(new CopilotSessionLifecycleEvent(
            DateTimeOffset.UtcNow,
            "n/a",
            model,
            eventType,
            details));

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
