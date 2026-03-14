using ArchHarness.App.Storage;
using ArchHarness.Desktop.ViewModels;

namespace ArchHarness.Desktop;

/// <summary>
/// Default implementation of <see cref="IRunHistoryService"/> that reads persisted runs from the file system.
/// </summary>
public sealed class RunHistoryService : IRunHistoryService
{
    private readonly IRunHistoryCatalog _catalog;

    /// <summary>
    /// Initializes a new instance of the <see cref="RunHistoryService"/> class.
    /// </summary>
    /// <param name="catalog">The shared persisted-run catalog.</param>
    public RunHistoryService(IRunHistoryCatalog catalog)
    {
        this._catalog = catalog;
    }

    /// <inheritdoc />
    public IReadOnlyList<RunSummaryViewModel> GetRecentRuns(string workspacePath, int maxCount = 20)
        => this._catalog
            .GetRecentRuns(workspacePath, maxCount)
            .Select(run => new RunSummaryViewModel(run.RunId, run.RunDirectory))
            .ToList();

    /// <inheritdoc />
    public IReadOnlyList<ArtifactItemViewModel> GetArtifacts(string runDirectory, int previewLength = 2400)
        => this._catalog
            .GetArtifacts(runDirectory, previewLength)
            .Select(artifact => new ArtifactItemViewModel(artifact.Name, artifact.FullPath, artifact.Kind, artifact.Description, artifact.Preview))
            .ToList();
}