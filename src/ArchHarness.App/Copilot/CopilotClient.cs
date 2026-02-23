using System.Collections.Concurrent;
using ArchHarness.App.Core;
using Microsoft.Extensions.Options;

namespace ArchHarness.App.Copilot;

public interface ICopilotClient
{
    Task<string> CompleteAsync(string model, string prompt, CancellationToken cancellationToken = default);
    IReadOnlyList<CopilotModelUsage> GetUsageSnapshot();
}

public sealed class CopilotClient : ICopilotClient
{
    private readonly ICopilotSessionFactory _sessionFactory;
    private readonly IModelResolver _modelResolver;
    private readonly CopilotOptions _options;
    private readonly ConcurrentDictionary<string, UsageCounter> _usage = new(StringComparer.OrdinalIgnoreCase);

    public CopilotClient(
        ICopilotSessionFactory sessionFactory,
        IModelResolver modelResolver,
        IOptions<CopilotOptions> options)
    {
        _sessionFactory = sessionFactory;
        _modelResolver = modelResolver;
        _options = options.Value;
    }

    public async Task<string> CompleteAsync(string model, string prompt, CancellationToken cancellationToken = default)
    {
        _modelResolver.ValidateOrThrow(model);
        var boundedPrompt = BoundLength(prompt, _options.MaxPromptCharacters);

        Exception? lastException = null;
        for (var attempt = 0; attempt <= _options.MaxRetries; attempt++)
        {
            try
            {
                var session = _sessionFactory.Create(model);
                var completion = await session.CompleteAsync(boundedPrompt, cancellationToken);
                var boundedCompletion = BoundLength(completion, _options.MaxCompletionCharacters);
                TrackUsage(model, boundedPrompt.Length, boundedCompletion.Length);
                return boundedCompletion;
            }
            catch (TimeoutException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < _options.MaxRetries)
            {
                lastException = ex;
                var backoff = _options.BaseRetryDelayMilliseconds * (int)Math.Pow(2, attempt);
                await Task.Delay(backoff, cancellationToken);
            }
        }

        throw new InvalidOperationException($"Copilot completion failed for model '{model}' after retries.", lastException);
    }

    public IReadOnlyList<CopilotModelUsage> GetUsageSnapshot()
        => _usage.Select(pair => new CopilotModelUsage(
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
        var counter = _usage.GetOrAdd(model, _ => new UsageCounter());
        counter.Increment(promptChars, completionChars);
    }

    private sealed class UsageCounter
    {
        public int Calls;
        public int PromptCharacters;
        public int CompletionCharacters;

        public void Increment(int promptChars, int completionChars)
        {
            Interlocked.Increment(ref Calls);
            Interlocked.Add(ref PromptCharacters, promptChars);
            Interlocked.Add(ref CompletionCharacters, completionChars);
        }
    }
}

public interface ICopilotSession
{
    Task<string> CompleteAsync(string prompt, CancellationToken cancellationToken);
}
