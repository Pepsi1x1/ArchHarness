using ArchHarness.App.Storage;

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

        FileSystemRunHistoryCatalog service = new FileSystemRunHistoryCatalog();

        IReadOnlyList<PersistedRunSummary> runs = service.GetRecentRuns(this._workspaceRoot);

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

        FileSystemRunHistoryCatalog service = new FileSystemRunHistoryCatalog();

        IReadOnlyList<RunArtifactPreview> artifacts = service.GetArtifacts(runDirectory, previewLength: 64);

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

        FileSystemRunHistoryCatalog service = new FileSystemRunHistoryCatalog();

        IReadOnlyList<RunArtifactPreview> artifacts = service.GetArtifacts(runDirectory, previewLength: 512);

        Assert.Single(artifacts);
        Assert.Equal("JSON", artifacts[0].Kind);
        Assert.Contains(Environment.NewLine, artifacts[0].Preview);
        Assert.Contains("BuildResult.json", artifacts[0].Description);
    }

    [Fact]
    public void GetRecentRuns_ReadsRunTitlesAndProjectMetadataFromRunLog()
    {
        string runDirectory = Path.Combine(this._workspaceRoot, ".agent-harness", "runs", "20260314T121700000");
        Directory.CreateDirectory(runDirectory);
        File.WriteAllText(Path.Combine(runDirectory, "run-log.json"), """
            {
              "status": "completed",
              "projectId": "project-alpha",
              "projectName": "Alpha Workspace",
              "runTitle": "Sidebar Shell Audit"
            }
            """);

        FileSystemRunHistoryCatalog service = new FileSystemRunHistoryCatalog();

        PersistedRunSummary run = Assert.Single(service.GetRecentRuns(this._workspaceRoot));

        Assert.Equal("Sidebar Shell Audit", run.RunTitle);
        Assert.Equal("project-alpha", run.ProjectId);
        Assert.Equal("Alpha Workspace", run.ProjectName);
    }

    [Fact]
    public void GetRecentRuns_SynthesizesTitleAndProjectMetadataFromRequestEvent()
    {
        string runDirectory = Path.Combine(this._workspaceRoot, ".agent-harness", "runs", "20260314T121800000");
        Directory.CreateDirectory(runDirectory);
        File.WriteAllText(Path.Combine(runDirectory, "events.jsonl"), """
            {"runId":"20260314T121800000","source":"request","message":"Run request received","projectId":"project-beta","projectName":"Beta Workspace","taskPrompt":"Scaffold the project shell and wire settings persistence"}
            """);

        FileSystemRunHistoryCatalog service = new FileSystemRunHistoryCatalog();

        PersistedRunSummary run = Assert.Single(service.GetRecentRuns(this._workspaceRoot));

        Assert.Equal("Scaffold the project shell and wire", run.RunTitle);
        Assert.Equal("project-beta", run.ProjectId);
        Assert.Equal("Beta Workspace", run.ProjectName);
    }

    public void Dispose()
    {
        if (Directory.Exists(this._workspaceRoot))
        {
            Directory.Delete(this._workspaceRoot, recursive: true);
        }
    }
}