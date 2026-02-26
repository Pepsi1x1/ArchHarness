using System.Net.Http;

namespace ArchHarness.App.Copilot;

/// <summary>
/// Classifies Copilot exceptions as permanent or transient to drive retry decisions.
/// </summary>
internal static class CopilotErrorClassifier
{
    /// <summary>
    /// Determines whether the exception represents a permanent, non-retryable error.
    /// </summary>
    /// <param name="ex">The exception to classify.</param>
    /// <returns><c>true</c> if the error is permanent; otherwise <c>false</c>.</returns>
    public static bool IsPermanent(Exception ex)
    {
        string text = ex.ToString();
        return text.Contains("not authenticated", StringComparison.OrdinalIgnoreCase)
            || text.Contains("authentication", StringComparison.OrdinalIgnoreCase)
            || text.Contains("unauthorized", StringComparison.OrdinalIgnoreCase)
            || text.Contains("forbidden", StringComparison.OrdinalIgnoreCase)
            || text.Contains("invalid api key", StringComparison.OrdinalIgnoreCase)
            || text.Contains("unsupported model", StringComparison.OrdinalIgnoreCase)
            || text.Contains("model not found", StringComparison.OrdinalIgnoreCase)
            || text.Contains("unknown model", StringComparison.OrdinalIgnoreCase)
            || text.Contains("invalid model", StringComparison.OrdinalIgnoreCase)
            || text.Contains("not supported", StringComparison.OrdinalIgnoreCase)
            || text.Contains("bad request", StringComparison.OrdinalIgnoreCase)
            || text.Contains("status code 400", StringComparison.OrdinalIgnoreCase)
            || text.Contains("status code 401", StringComparison.OrdinalIgnoreCase)
            || text.Contains("status code 403", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Determines whether the exception represents a transient, retryable error.
    /// </summary>
    /// <param name="ex">The exception to classify.</param>
    /// <returns><c>true</c> if the error is transient; otherwise <c>false</c>.</returns>
    public static bool IsTransient(Exception ex)
    {
        if (ex is TimeoutException
            || ex is OperationCanceledException
            || ex is HttpRequestException)
        {
            return true;
        }

        string text = ex.ToString();
        return text.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
            || text.Contains("too many requests", StringComparison.OrdinalIgnoreCase)
            || text.Contains("status code 429", StringComparison.OrdinalIgnoreCase)
            || text.Contains("temporarily unavailable", StringComparison.OrdinalIgnoreCase)
            || text.Contains("connection reset", StringComparison.OrdinalIgnoreCase)
            || text.Contains("connection refused", StringComparison.OrdinalIgnoreCase)
            || text.Contains("network", StringComparison.OrdinalIgnoreCase);
    }
}
