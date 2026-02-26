using System.Diagnostics;

namespace ArchHarness.App.Workspace;

public sealed class GitWorkspaceAdapter : FileSystemWorkspaceAdapter
{
    private HashSet<string> _initialChangedPaths = new(StringComparer.OrdinalIgnoreCase);

    public GitWorkspaceAdapter(string rootPath) : base(rootPath)
    {
    }

    public override async Task InitializeAsync(string? projectName, bool initGit, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(Path.Combine(RootPath, ".git")) && !initGit)
        {
            throw new InvalidOperationException("existing-git mode requires a .git directory.");
        }

        await base.InitializeAsync(projectName, initGit: true, cancellationToken);
        _initialChangedPaths = new HashSet<string>(await GetGitChangedPathsAsync(cancellationToken), StringComparer.OrdinalIgnoreCase);
    }

    public override async Task<string> DiffAsync(CancellationToken cancellationToken)
    {
        var gitChangedPaths = new HashSet<string>(await GetGitChangedPathsAsync(cancellationToken), StringComparer.OrdinalIgnoreCase);
        var snapshotChangedPaths = new HashSet<string>(ComputeChangedPathsSinceBaseline(), StringComparer.OrdinalIgnoreCase);

        // Exclude files that were already dirty at startup unless they changed since baseline.
        gitChangedPaths.ExceptWith(_initialChangedPaths);
        gitChangedPaths.UnionWith(snapshotChangedPaths);

        return string.Join(
            Environment.NewLine,
            gitChangedPaths
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
    }

    private async Task<IReadOnlyCollection<string>> GetGitChangedPathsAsync(CancellationToken cancellationToken)
    {
        var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var tracked = await RunGitCommandAsync("diff --name-only --relative HEAD", cancellationToken);
        AddPaths(changed, tracked);

        var untracked = await RunGitCommandAsync("ls-files --others --exclude-standard", cancellationToken);
        AddPaths(changed, untracked);

        return changed;
    }

    private async Task<string> RunGitCommandAsync(string arguments, CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo("git", $"-C {QuoteArgument(RootPath)} {arguments}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = info };
        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        _ = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            return string.Empty;
        }

        return stdout;
    }

    private static void AddPaths(ISet<string> output, string raw)
    {
        var lines = raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var line in lines.Where(line => !string.IsNullOrWhiteSpace(line)))
        {
            output.Add(line);
        }
    }

    private static string QuoteArgument(string value)
        => value.Contains(' ', StringComparison.Ordinal)
            ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : value;
}
