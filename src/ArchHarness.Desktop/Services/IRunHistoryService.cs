using ArchHarness.Desktop.ViewModels;

namespace ArchHarness.Desktop;

/// <summary>
/// Provides access to persisted run history and artifact metadata for the desktop host.
/// </summary>
public interface IRunHistoryService
{
    /// <summary>
    /// Returns recent run summaries for the specified workspace, ordered most-recent first.
    /// </summary>
    /// <param name="workspacePath">The workspace root path to scan.</param>
    /// <param name="maxCount">The maximum number of runs to return.</param>
    /// <returns>An ordered list of run summaries.</returns>
    IReadOnlyList<RunSummaryViewModel> GetRecentRuns(string workspacePath, int maxCount = 20);

    /// <summary>
    /// Returns artifact view models for all top-level files in the specified run directory.
    /// </summary>
    /// <param name="runDirectory">The full path to the run directory.</param>
    /// <param name="previewLength">The maximum character length for artifact text previews.</param>
    /// <returns>An ordered list of artifact view models.</returns>
    IReadOnlyList<ArtifactItemViewModel> GetArtifacts(string runDirectory, int previewLength = 2400);
}