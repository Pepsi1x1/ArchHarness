using ArchHarness.Desktop;

namespace ArchHarness.App.Tests.Desktop;

public sealed class RunHistoryServiceTests : IDisposable
{
    private readonly string _workspaceRoot = Path.Combine(Path.GetTempPath(), "ArchHarnessDesktopTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void GetRecentRuns_ReturnsDescendingRunDirectories()
    {
        string runsRoot = Path.Combine(this._workspaceRoot, ".agent-harness", "runs");
        Directory.CreateDirectory(Path.Combine(runsRoot, "20260314T120000000"));
        Directory.CreateDirectory(Path.Combine(runsRoot, "20260314T121500000"));
        Directory.CreateDirectory(Path.Combine(runsRoot, "20260314T121000000"));

        RunHistoryService service = new RunHistoryService();

        IReadOnlyList<ArchHarness.Desktop.ViewModels.RunSummaryViewModel> runs = service.GetRecentRuns(this._workspaceRoot);

        Assert.Collection(
            runs,
            run => Assert.Equal("20260314T121500000", run.RunId),
            run => Assert.Equal("20260314T121000000", run.RunId),
            run => Assert.Equal("20260314T120000000", run.RunId));
    }

    [Fact]
    public void GetArtifacts_TruncatesLargePreviews()
    {
        string runDirectory = Path.Combine(this._workspaceRoot, ".agent-harness", "runs", "20260314T121500000");
        Directory.CreateDirectory(runDirectory);
        File.WriteAllText(Path.Combine(runDirectory, "events.jsonl"), new string('a', 3000));

        RunHistoryService service = new RunHistoryService();

        IReadOnlyList<ArchHarness.Desktop.ViewModels.ArtifactItemViewModel> artifacts = service.GetArtifacts(runDirectory, previewLength: 64);

        Assert.Single(artifacts);
        Assert.Contains("...", artifacts[0].Preview);
        Assert.True(artifacts[0].Preview.Length > 64);
        Assert.Equal("JSON lines", artifacts[0].Kind);
    }

    [Fact]
    public void GetArtifacts_PrettyPrintsJsonAndAddsMetadata()
    {
        string runDirectory = Path.Combine(this._workspaceRoot, ".agent-harness", "runs", "20260314T121600000");
        Directory.CreateDirectory(runDirectory);
        File.WriteAllText(Path.Combine(runDirectory, "BuildResult.json"), "{\"status\":\"ok\",\"warnings\":[1,2]}");

        RunHistoryService service = new RunHistoryService();

        IReadOnlyList<ArchHarness.Desktop.ViewModels.ArtifactItemViewModel> artifacts = service.GetArtifacts(runDirectory, previewLength: 512);

        Assert.Single(artifacts);
        Assert.Equal("JSON", artifacts[0].Kind);
        Assert.Contains(Environment.NewLine, artifacts[0].Preview);
        Assert.Contains("BuildResult.json", artifacts[0].Description);
    }

    public void Dispose()
    {
        if (Directory.Exists(this._workspaceRoot))
        {
            Directory.Delete(this._workspaceRoot, recursive: true);
        }
    }
}