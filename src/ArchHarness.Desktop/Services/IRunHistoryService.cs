using ArchHarness.Desktop.ViewModels;

namespace ArchHarness.Desktop;

public interface IRunHistoryService
{
    IReadOnlyList<RunSummaryViewModel> GetRecentRuns(string workspacePath, int maxCount = 20);

    IReadOnlyList<ArtifactItemViewModel> GetArtifacts(string runDirectory, int previewLength = 2400);
}