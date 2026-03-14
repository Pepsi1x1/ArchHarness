namespace ArchHarness.App.Storage;

/// <summary>
/// Provides access to persisted run history and artifact previews for host applications.
/// </summary>
public interface IRunHistoryCatalog
{
	/// <summary>
	/// Returns recent run summaries for the specified workspace, ordered most-recent first.
	/// </summary>
	/// <param name="workspacePath">The workspace root path to scan.</param>
	/// <param name="maxCount">The maximum number of runs to return.</param>
	/// <returns>An ordered list of persisted runs.</returns>
	IReadOnlyList<PersistedRunSummary> GetRecentRuns(string workspacePath, int maxCount = 20);

	/// <summary>
	/// Returns preview metadata for all top-level files in the specified run directory.
	/// </summary>
	/// <param name="runDirectory">The full path to the run directory.</param>
	/// <param name="previewLength">The maximum character length for artifact text previews.</param>
	/// <returns>An ordered list of artifact previews.</returns>
	IReadOnlyList<RunArtifactPreview> GetArtifacts(string runDirectory, int previewLength = 2400);
}