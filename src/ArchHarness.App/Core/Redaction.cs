using System.Text.RegularExpressions;

namespace ArchHarness.App.Core;

public static partial class Redaction
{
    public static string RedactSecrets(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var output = JsonSecretValueRegex().Replace(text, "$1***REDACTED***$3");
        output = EnvSecretValueRegex().Replace(output, "$1=***REDACTED***");
        output = GitHubTokenRegex().Replace(output, "***REDACTED***");
        return output;
    }

    [GeneratedRegex(@"(?i)(""(?:password|secret|token|api[_-]?key)""\s*:\s*"")[^""]*("")")]
    private static partial Regex JsonSecretValueRegex();

    [GeneratedRegex(@"(?i)(password|secret|token|api[_-]?key)\s*=\s*[^\s,;]+")]
    private static partial Regex EnvSecretValueRegex();

    [GeneratedRegex("gh[pousr]_[A-Za-z0-9_]{16,}")]
    private static partial Regex GitHubTokenRegex();
}
