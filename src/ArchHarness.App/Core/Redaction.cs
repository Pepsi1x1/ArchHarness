using System.Text.RegularExpressions;

namespace ArchHarness.App.Core;

/// <summary>
/// Provides secret redaction utilities for sanitizing sensitive data in output strings.
/// </summary>
public static partial class Redaction
{
    /// <summary>
    /// Replaces known secret patterns (JSON secret values, environment variable secrets, GitHub tokens) with redaction markers.
    /// </summary>
    /// <param name="text">The text to redact secrets from.</param>
    /// <returns>The text with secrets replaced by redaction markers.</returns>
    public static string RedactSecrets(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        string output = JsonSecretValueRegex().Replace(text, "$1***REDACTED***$2");
        output = EnvSecretValueRegex().Replace(output, "$1=***REDACTED***");
        output = GitHubTokenRegex().Replace(output, "***REDACTED***");
        output = GitHubFineGrainedTokenRegex().Replace(output, "***REDACTED***");
        output = BearerTokenRegex().Replace(output, "$1***REDACTED***");
        return output;
    }

    [GeneratedRegex(@"(?i)(""(?:password|secret|token|api[_-]?key|connectionstring|client[_-]?secret|taskprompt|architecturereviewprompt|defaultarchitecturereviewprompt)""\s*:\s*"")[^""]*("")")]
    private static partial Regex JsonSecretValueRegex();

    [GeneratedRegex(@"(?i)(password|secret|token|api[_-]?key|connectionstring|client[_-]?secret)\s*=\s*[^\s,;]+")]
    private static partial Regex EnvSecretValueRegex();

    [GeneratedRegex("gh[pousr]_[A-Za-z0-9_]{16,}")]
    private static partial Regex GitHubTokenRegex();

    [GeneratedRegex("github_pat_[A-Za-z0-9_]{20,}")]
    private static partial Regex GitHubFineGrainedTokenRegex();

    [GeneratedRegex(@"(?i)(Bearer\s+)[A-Za-z0-9\-._~+/]+=*")]
    private static partial Regex BearerTokenRegex();
}
