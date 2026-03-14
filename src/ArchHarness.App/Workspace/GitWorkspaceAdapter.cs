using System.Diagnostics;

namespace ArchHarness.App.Workspace;

/// <summary>
/// Git-backed workspace adapter that combines git status with file-system snapshot diffing.
/// </summary>
public sealed class GitWorkspaceAdapter : FileSystemWorkspaceAdapter
{
    private HashSet<string> _initialChangedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of <see cref="GitWorkspaceAdapter"/> for the specified root path.
    /// </summary>
    /// <param name="rootPath">The workspace root directory path.</param>
    public GitWorkspaceAdapter(string rootPath) : base(rootPath)
    {
    }

    /// <inheritdoc />
    public override async Task InitializeAsync(string? projectName, bool initGit, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(Path.Combine(this.RootPath, ".git")) && !initGit)
        {
            throw new InvalidOperationException("existing-git mode requires a .git directory.");
        }

        await base.InitializeAsync(projectName, initGit: true, cancellationToken);
        this._initialChangedPaths = new HashSet<string>(await this.GetGitChangedPathsAsync(cancellationToken), StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public override async Task<string> DiffAsync(CancellationToken cancellationToken)
    {
        HashSet<string> gitChangedPaths = new HashSet<string>(await this.GetGitChangedPathsAsync(cancellationToken), StringComparer.OrdinalIgnoreCase);
        HashSet<string> snapshotChangedPaths = new HashSet<string>(this.ComputeChangedPathsSinceBaseline(), StringComparer.OrdinalIgnoreCase);

        // Exclude files that were already dirty at startup unless they changed since baseline.
        gitChangedPaths.ExceptWith(this._initialChangedPaths);
        gitChangedPaths.UnionWith(snapshotChangedPaths);

        return string.Join(
            Environment.NewLine,
            gitChangedPaths
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
    }

    private async Task<IReadOnlyCollection<string>> GetGitChangedPathsAsync(CancellationToken cancellationToken)
    {
        HashSet<string> changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string tracked = await this.RunGitCommandAsync("diff --name-only --relative HEAD", cancellationToken);
        AddPaths(changed, tracked);

        string untracked = await this.RunGitCommandAsync("ls-files --others --exclude-standard", cancellationToken);
        AddPaths(changed, untracked);

        return changed;
    }

    private async Task<string> RunGitCommandAsync(string arguments, CancellationToken cancellationToken)
    {
        ProcessStartInfo info = new ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        info.ArgumentList.Add("-C");
        info.ArgumentList.Add(this.RootPath);
        foreach (string arg in arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            info.ArgumentList.Add(arg);
        }

        using Process process = new Process { StartInfo = info };
        process.Start();
        string stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
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
        string[] lines = raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string line in lines.Where(line => !string.IsNullOrWhiteSpace(line)))
        {
            output.Add(line);
        }
    }
}
