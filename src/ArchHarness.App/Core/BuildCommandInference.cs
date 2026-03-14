using System.Text.RegularExpressions;

namespace ArchHarness.App.Core;

/// <summary>
/// Represents the result of build command selection, including the command, whether it was inferred, and reasoning.
/// </summary>
/// <param name="Command">The selected build command, or null if none could be determined.</param>
/// <param name="Inferred">Whether the command was inferred rather than explicitly specified.</param>
/// <param name="Reason">Human-readable explanation of the selection decision.</param>
public sealed record BuildCommandSelection(string? Command, bool Inferred, string Reason);

/// <summary>
/// Infers or validates a dotnet build command for a given workspace by discovering solution and project files.
/// </summary>
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

    /// <summary>
    /// Selects the best build command by inspecting the workspace and optionally enriching a user-specified command.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root directory.</param>
    /// <param name="requestedBuildCommand">An optional user-specified build command.</param>
    /// <param name="workspaceMode">The workspace mode (existing-git, existing-folder, or new-project).</param>
    /// <param name="projectName">An optional project name used for target matching.</param>
    /// <returns>The selected build command with inference metadata.</returns>
    public static BuildCommandSelection Select(
        string workspaceRoot,
        string? requestedBuildCommand,
        string workspaceMode,
        string? projectName)
    {
        string normalizedRoot = Path.GetFullPath(Environment.ExpandEnvironmentVariables(workspaceRoot));
        string? target = ResolveBestBuildTarget(normalizedRoot, projectName);

        if (!string.IsNullOrWhiteSpace(requestedBuildCommand))
        {
            string trimmed = requestedBuildCommand.Trim();
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
        string prefix = "dotnet build";
        string remainder = command[prefix.Length..].Trim();
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

        string[] slnFiles = Directory.GetFiles(workspaceRoot, "*.sln", SearchOption.AllDirectories)
            .Where(IsBuildCandidate)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (slnFiles.Length > 0)
        {
            string sln = PickByProjectNameOrFirst(slnFiles, projectName);
            return Path.GetFullPath(sln);
        }

        string[] csprojFiles = Directory.GetFiles(workspaceRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(IsBuildCandidate)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (csprojFiles.Length == 0)
        {
            return null;
        }

        string[] preferred = csprojFiles
            .Where(p => !Path.GetFileNameWithoutExtension(p).Contains("test", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Contains($"{Path.DirectorySeparatorChar}src{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        string chosen = preferred.Length > 0 ? PickByProjectNameOrFirst(preferred, projectName) : PickByProjectNameOrFirst(csprojFiles, projectName);
        return Path.GetFullPath(chosen);
    }

    private static string PickByProjectNameOrFirst(IReadOnlyList<string> files, string? projectName)
    {
        if (!string.IsNullOrWhiteSpace(projectName))
        {
            string? match = files.FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Contains(projectName, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match))
            {
                return match;
            }
        }

        return files[0];
    }

    private static bool IsBuildCandidate(string path)
    {
        string normalized = path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        return !normalized.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            && !normalized.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
    }
}