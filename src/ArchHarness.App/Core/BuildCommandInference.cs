using System.Text.RegularExpressions;

namespace ArchHarness.App.Core;

public sealed record BuildCommandSelection(string? Command, bool Inferred, string Reason);

public static class BuildCommandInference
{
    private static readonly Regex TargetRegex = new("\\.(sln|csproj)(?=(\"|'|\\s|$))", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private sealed record InferenceRule(Func<string?, string, string?, bool> Predicate, Func<string?, string, string?, BuildCommandSelection> Result);

    private static readonly InferenceRule[] NoCommandRules = new InferenceRule[]
    {
        new InferenceRule(
            (target, mode, project) => !string.IsNullOrWhiteSpace(target),
            (target, mode, project) => new BuildCommandSelection($"dotnet build \"{target}\" --nologo", Inferred: true, Reason: "Discovered build target under workspace.")),
        new InferenceRule(
            (target, mode, project) => string.Equals(mode, "new-project", StringComparison.OrdinalIgnoreCase),
            (target, mode, project) => new BuildCommandSelection("dotnet build --nologo", Inferred: true, Reason: "New-project mode fallback before a concrete target exists."))
    };

    private static readonly BuildCommandSelection NoTargetFallback = new BuildCommandSelection(null, Inferred: false, Reason: "No suitable .sln or .csproj discovered in workspace.");

    public static BuildCommandSelection Select(
        string workspaceRoot,
        string? requestedBuildCommand,
        string workspaceMode,
        string? projectName)
    {
        var normalizedRoot = Path.GetFullPath(Environment.ExpandEnvironmentVariables(workspaceRoot));
        var target = ResolveBestBuildTarget(normalizedRoot, projectName);

        if (!string.IsNullOrWhiteSpace(requestedBuildCommand))
        {
            var trimmed = requestedBuildCommand.Trim();
            if (!trimmed.StartsWith("dotnet build", StringComparison.OrdinalIgnoreCase))
            {
                return new BuildCommandSelection(trimmed, Inferred: false, Reason: "User-specified build command is not dotnet build.");
            }

            if (ContainsBuildTarget(trimmed))
            {
                return new BuildCommandSelection(trimmed, Inferred: false, Reason: "User-specified build command already includes a target path.");
            }

            if (!string.IsNullOrWhiteSpace(target))
            {
                return new BuildCommandSelection(
                    InjectTargetIntoDotnetBuild(trimmed, target),
                    Inferred: true,
                    Reason: "Injected discovered solution/project target into user-specified dotnet build command.");
            }

            return new BuildCommandSelection(trimmed, Inferred: false, Reason: "No solution/project target discovered to inject.");
        }

        InferenceRule? matchedRule = NoCommandRules.FirstOrDefault(rule => rule.Predicate(target, workspaceMode, projectName));
        if (matchedRule != null)
        {
            return matchedRule.Result(target, workspaceMode, projectName);
        }

        return NoTargetFallback;
    }

    private static bool ContainsBuildTarget(string command)
        => TargetRegex.IsMatch(command);

    private static string InjectTargetIntoDotnetBuild(string command, string targetPath)
    {
        var prefix = "dotnet build";
        var remainder = command[prefix.Length..].Trim();
        return string.IsNullOrWhiteSpace(remainder)
            ? $"dotnet build \"{targetPath}\" --nologo"
            : $"dotnet build \"{targetPath}\" {remainder}";
    }

    private static string? ResolveBestBuildTarget(string workspaceRoot, string? projectName)
    {
        if (!Directory.Exists(workspaceRoot))
        {
            return null;
        }

        var slnFiles = Directory.GetFiles(workspaceRoot, "*.sln", SearchOption.AllDirectories)
            .Where(IsBuildCandidate)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (slnFiles.Length > 0)
        {
            var sln = PickByProjectNameOrFirst(slnFiles, projectName);
            return Path.GetFullPath(sln);
        }

        var csprojFiles = Directory.GetFiles(workspaceRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(IsBuildCandidate)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (csprojFiles.Length == 0)
        {
            return null;
        }

        var preferred = csprojFiles
            .Where(p => !Path.GetFileNameWithoutExtension(p).Contains("test", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Contains($"{Path.DirectorySeparatorChar}src{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var chosen = preferred.Length > 0 ? PickByProjectNameOrFirst(preferred, projectName) : PickByProjectNameOrFirst(csprojFiles, projectName);
        return Path.GetFullPath(chosen);
    }

    private static string PickByProjectNameOrFirst(IReadOnlyList<string> files, string? projectName)
    {
        if (!string.IsNullOrWhiteSpace(projectName))
        {
            var match = files.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Contains(projectName, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match))
            {
                return match;
            }
        }

        return files[0];
    }

    private static bool IsBuildCandidate(string path)
    {
        var normalized = path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        return !normalized.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            && !normalized.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
    }
}