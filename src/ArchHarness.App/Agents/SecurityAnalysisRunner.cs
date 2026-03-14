using System.Text.RegularExpressions;
using ArchHarness.App.Core;

namespace ArchHarness.App.Agents;

/// <summary>
/// Performs lightweight heuristic security analysis to support the security remediation loop.
/// </summary>
internal static class SecurityAnalysisRunner
{
    private const string SEVERITY_HIGH = "high";
    private const string SEVERITY_MEDIUM = "medium";

    private static readonly string[] CANDIDATE_EXTENSIONS = [".cs", ".json", ".config", ".ts", ".tsx", ".js", ".jsx", ".vue", ".csproj", ".props", ".targets", ".md"];

    /// <summary>
    /// Performs heuristic security analysis on the workspace and returns findings.
    /// </summary>
    /// <param name="diff">The current diff snapshot.</param>
    /// <param name="workspaceRoot">The workspace root directory path.</param>
    /// <param name="filesTouched">Files modified during the run.</param>
    /// <param name="languageScope">Optional explicit language scope.</param>
    /// <returns>A security review containing findings and required remediation actions.</returns>
    public static SecurityReview Analyze(
        string diff,
        string workspaceRoot,
        IReadOnlyList<string> filesTouched,
        IReadOnlyList<string>? languageScope)
    {
        List<SecurityFinding> findings = new List<SecurityFinding>();
        HashSet<string> requiredActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string file in ResolveCandidateFiles(diff, workspaceRoot, filesTouched))
        {
            string content = File.ReadAllText(file);
            string relativePath = Path.GetRelativePath(workspaceRoot, file);

            DetectHardcodedSecrets(content, relativePath, findings, requiredActions);
            DetectInsecureTransport(content, relativePath, findings, requiredActions);
            DetectSqlInjection(content, relativePath, findings, requiredActions);
            DetectXss(content, relativePath, findings, requiredActions);
            DetectInsecureTlsBypass(content, relativePath, findings, requiredActions);
        }

        return new SecurityReview(findings, requiredActions.ToArray());
    }

    private static IReadOnlyList<string> ResolveCandidateFiles(string diff, string workspaceRoot, IReadOnlyList<string> filesTouched)
    {
        HashSet<string> files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string normalizedRoot = Path.GetFullPath(workspaceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        foreach (string touched in filesTouched)
        {
            string fullPath = Path.IsPathRooted(touched)
                ? Path.GetFullPath(touched)
                : Path.GetFullPath(Path.Combine(workspaceRoot, touched));

            if (fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
                && File.Exists(fullPath) && IsCandidateFile(fullPath))
            {
                files.Add(fullPath);
            }
        }

        foreach (string line in diff.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string fullPath = Path.GetFullPath(Path.Combine(workspaceRoot, line));
            if (fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
                && File.Exists(fullPath) && IsCandidateFile(fullPath))
            {
                files.Add(fullPath);
            }
        }

        if (files.Count > 0)
        {
            return files.ToArray();
        }

        return Directory.GetFiles(workspaceRoot, "*.*", SearchOption.AllDirectories)
            .Where(IsCandidateFile)
            .ToArray();
    }

    private static bool IsCandidateFile(string path)
    {
        string normalized = path.Replace('\\', '/');
        if (normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/node_modules/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/.git/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return CANDIDATE_EXTENSIONS.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    }

    private static readonly Regex HardcodedSecretRegex = new Regex(
        "(?im)(password|pwd|secret|api[_-]?key|token|clientsecret|connectionstring)\\s*[:=]\\s*[\"'][^\"'\\r\\n]{6,}[\"']",
        RegexOptions.Compiled);

    private static void DetectHardcodedSecrets(string content, string file, ICollection<SecurityFinding> findings, ISet<string> requiredActions)
    {
        if (!HardcodedSecretRegex.IsMatch(content))
        {
            return;
        }

        findings.Add(new SecurityFinding(
            SEVERITY_HIGH,
            "HardcodedSecrets",
            file,
            null,
            "Potential hardcoded secret or credential detected in source or configuration.",
            "OWASP A02:2021 Cryptographic Failures"));
        requiredActions.Add("Remove hardcoded secrets and source them from secure configuration providers or environment variables.");
    }

    private static readonly Regex InsecureTransportRegex = new Regex(
        "http://(?!localhost|127\\.0\\.0\\.1)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static void DetectInsecureTransport(string content, string file, ICollection<SecurityFinding> findings, ISet<string> requiredActions)
    {
        if (!InsecureTransportRegex.IsMatch(content))
        {
            return;
        }

        findings.Add(new SecurityFinding(
            SEVERITY_MEDIUM,
            "InsecureTransport",
            file,
            null,
            "Non-localhost HTTP endpoint detected. Sensitive traffic should use HTTPS.",
            "OWASP A02:2021 Cryptographic Failures"));
        requiredActions.Add("Replace insecure HTTP endpoints with HTTPS or document why plaintext transport is safe and isolated.");
    }

    private static readonly Regex RawSqlRegex = new Regex(
        "(FromSqlRaw|ExecuteSqlRaw)\\s*\\([^\\)]*(\\$\"|\\+)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex ConcatenatedSqlRegex = new Regex(
        "(?is)(SELECT|INSERT|UPDATE|DELETE)[^;\\r\\n]{0,200}(\\+|\\{)",
        RegexOptions.Compiled);

    private static void DetectSqlInjection(string content, string file, ICollection<SecurityFinding> findings, ISet<string> requiredActions)
    {
        if (!RawSqlRegex.IsMatch(content) && !ConcatenatedSqlRegex.IsMatch(content))
        {
            return;
        }

        findings.Add(new SecurityFinding(
            SEVERITY_HIGH,
            "Injection",
            file,
            null,
            "Potential SQL injection pattern detected through raw SQL composition or string concatenation.",
            "OWASP A03:2021 Injection"));
        requiredActions.Add("Parameterize SQL queries and avoid raw string concatenation when building commands from external input.");
    }

    private static readonly Regex XssRegex = new Regex(
        "(v-html|innerHTML\\s*=|Html\\.Raw|dangerouslySetInnerHTML)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static void DetectXss(string content, string file, ICollection<SecurityFinding> findings, ISet<string> requiredActions)
    {
        if (!XssRegex.IsMatch(content))
        {
            return;
        }

        findings.Add(new SecurityFinding(
            SEVERITY_HIGH,
            "CrossSiteScripting",
            file,
            null,
            "Potential XSS sink detected. Raw HTML rendering requires strict sanitization or safer rendering patterns.",
            "OWASP A03:2021 Injection"));
        requiredActions.Add("Remove unsafe HTML sinks or sanitize untrusted content before rendering.");
    }

    private static readonly Regex TlsBypassRegex = new Regex(
        "(DangerousAcceptAnyServerCertificateValidator|ServerCertificateCustomValidationCallback\\s*=\\s*[^;]*=>\\s*true|rejectUnauthorized\\s*:\\s*false)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static void DetectInsecureTlsBypass(string content, string file, ICollection<SecurityFinding> findings, ISet<string> requiredActions)
    {
        if (!TlsBypassRegex.IsMatch(content))
        {
            return;
        }

        findings.Add(new SecurityFinding(
            SEVERITY_HIGH,
            "SecurityMisconfiguration",
            file,
            null,
            "Certificate validation bypass or insecure TLS configuration detected.",
            "OWASP A05:2021 Security Misconfiguration"));
        requiredActions.Add("Remove certificate validation bypasses and enforce secure TLS verification in all environments except isolated test harnesses.");
    }
}