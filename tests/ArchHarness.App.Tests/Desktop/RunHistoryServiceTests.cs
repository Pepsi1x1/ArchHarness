using System.Globalization;
using ArchHarness.App.Core;
using ArchHarness.App.Storage;

namespace ArchHarness.App.Tests.Desktop;

public sealed class RunHistoryServiceTests : IDisposable
{
    private readonly string _workspaceRoot = Path.Combine(Path.GetTempPath(), "ArchHarnessDesktopTests", Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Verifies that recent runs are returned in descending order by run directory name.
    /// </summary>
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

    /// <summary>
    /// Verifies that large artifact content is truncated and that a truncation marker is appended.
    /// </summary>
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

    /// <summary>
    /// Verifies that JSON artifacts are pretty-printed and include a description with the file name.
    /// </summary>
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

    /// <summary>
    /// Verifies that a friendly preview message is returned when a file cannot be read (non-Windows only).
    /// </summary>
    [Fact]
    public void GetArtifacts_ReturnsFriendlyPreviewForUnreadableFiles()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string runDirectory = Path.Combine(this._workspaceRoot, ".agent-harness", "runs", "20260314T121650000");
        Directory.CreateDirectory(runDirectory);

        string filePath = Path.Combine(runDirectory, "BuildResult.json");
        File.WriteAllText(filePath, "{\"status\":\"ok\"}");
        File.SetUnixFileMode(filePath, UnixFileMode.UserWrite);

        try
        {
            FileSystemRunHistoryCatalog service = new FileSystemRunHistoryCatalog();

            RunArtifactPreview artifact = Assert.Single(service.GetArtifacts(runDirectory));

            Assert.Equal("Unable to read file preview.", artifact.Preview);
        }
        finally
        {
            File.SetUnixFileMode(filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    /// <summary>
    /// Verifies that run titles and project metadata are read from the persisted run log.
    /// </summary>
    [Fact]
    public void GetRecentRuns_ReadsRunTitlesAndProjectMetadataFromRunLog()
    {
        string runDirectory = Path.Combine(this._workspaceRoot, ".agent-harness", "runs", "20260314T121700000");
        Directory.CreateDirectory(runDirectory);
        File.WriteAllText(Path.Combine(runDirectory, "run-log.json"), $$"""
                        {
                            "status": "{{RunStatuses.COMPLETED}}",
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

    /// <summary>
    /// Verifies that request-only runs use a generic title instead of exposing prompt content.
    /// </summary>
    [Fact]
    public void GetRecentRuns_UsesGenericTitleWhenOnlyRequestEventIsPresent()
    {
        string runDirectory = Path.Combine(this._workspaceRoot, ".agent-harness", "runs", "20260314T121800000");
        Directory.CreateDirectory(runDirectory);
        File.WriteAllText(Path.Combine(runDirectory, "events.jsonl"), """
            {"runId":"20260314T121800000","source":"request","message":"Run request received","projectId":"project-beta","projectName":"Beta Workspace","taskPrompt":"Scaffold the project shell and wire settings persistence"}
            """);

        FileSystemRunHistoryCatalog service = new FileSystemRunHistoryCatalog();

        PersistedRunSummary run = Assert.Single(service.GetRecentRuns(this._workspaceRoot));

        Assert.Equal("Run request", run.RunTitle);
        Assert.Equal("project-beta", run.ProjectId);
        Assert.Equal("Beta Workspace", run.ProjectName);
    }

    /// <summary>
    /// Verifies that artifact previews redact persisted prompt content before returning history.
    /// </summary>
    [Fact]
    public void GetArtifacts_RedactsPromptContentFromJsonPreviews()
    {
        string runDirectory = Path.Combine(this._workspaceRoot, ".agent-harness", "runs", "20260314T122000000");
        Directory.CreateDirectory(runDirectory);
        File.WriteAllText(Path.Combine(runDirectory, "run-state.json"), """
            {
              "taskPrompt": "Use github_pat_abcdefghijklmnopqrstuvwxyz123456 to clone the repo"
            }
            """);

        FileSystemRunHistoryCatalog service = new FileSystemRunHistoryCatalog();

        RunArtifactPreview artifact = Assert.Single(service.GetArtifacts(runDirectory, previewLength: 512));

        Assert.DoesNotContain("github_pat_", artifact.Preview, StringComparison.Ordinal);
        Assert.Contains("***REDACTED***", artifact.Preview, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that events are returned in chronological order by timestamp.
    /// </summary>
    [Fact]
    public void GetEvents_ReturnsReplayableRunEventsInChronologicalOrder()
    {
        string runDirectory = Path.Combine(this._workspaceRoot, ".agent-harness", "runs", "20260314T121900000");
        Directory.CreateDirectory(runDirectory);
        File.WriteAllText(Path.Combine(runDirectory, "events.jsonl"), $$"""
            {"runId":"20260314T121900000","source":"architecture","kind":"agent-delta","agentId":"architecture","agentRole":"Architecture","message":"Second chunk","contentFormat":"markdown","streamKind":"assistant","title":"Architecture","timestampUtc":"2026-03-14T12:19:02Z"}
            {"runId":"20260314T121900000","source":"request","message":"Run request received","taskPrompt":"Review the architecture boundary changes","timestampUtc":"2026-03-14T12:19:00Z"}
            {"runId":"20260314T121900000","source":"copilot.session","eventType":"session.resume","sessionId":"session-123","model":"{{WellKnownModelNames.GPT_5_4}}","details":"resumed","timestampUtc":"2026-03-14T12:19:03Z"}
            {"runId":"20260314T121900000","source":"architecture","kind":"agent-delta","agentId":"architecture","agentRole":"Architecture","message":"First chunk","contentFormat":"markdown","streamKind":"assistant","title":"Architecture","timestampUtc":"2026-03-14T12:19:01Z"}
            """);

        FileSystemRunHistoryCatalog service = new FileSystemRunHistoryCatalog();

        IReadOnlyList<PersistedRunEvent> events = service.GetEvents(runDirectory);

        Assert.Collection(
            events,
            evt =>
            {
                Assert.Equal("request", evt.Kind);
                Assert.Equal("Review the architecture boundary changes", evt.TaskPrompt);
            },
            evt =>
            {
                Assert.Equal("agent-delta", evt.Kind);
                Assert.Equal("First chunk", evt.Message);
                Assert.Equal("architecture", evt.AgentId);
            },
            evt =>
            {
                Assert.Equal("agent-delta", evt.Kind);
                Assert.Equal("Second chunk", evt.Message);
            },
            evt =>
            {
                Assert.Equal("copilot-session", evt.Kind);
                Assert.Equal("session-123", evt.SessionId);
                Assert.Equal("session.resume: resumed", evt.Message);
            });
    }

    /// <summary>
    /// Verifies that events with missing or malformed timestamps are skipped instead of being replayed at DateTimeOffset.MinValue.
    /// </summary>
    [Fact]
    public void GetEvents_SkipsEventsWithInvalidTimestamps()
    {
        string runDirectory = Path.Combine(this._workspaceRoot, ".agent-harness", "runs", "20260314T121910000");
        Directory.CreateDirectory(runDirectory);
        File.WriteAllText(Path.Combine(runDirectory, "events.jsonl"), $$"""
            {"runId":"20260314T121910000","source":"architecture","kind":"agent-delta","agentId":"architecture","agentRole":"Architecture","message":"Malformed timestamp","contentFormat":"markdown","streamKind":"assistant","title":"Architecture","timestampUtc":"not-a-timestamp"}
            {"runId":"20260314T121910000","source":"request","message":"Run request received","taskPrompt":"Review the architecture boundary changes","timestampUtc":"2026-03-14T12:19:00Z"}
            {"runId":"20260314T121910000","source":"architecture","kind":"agent-delta","agentId":"architecture","agentRole":"Architecture","message":"Missing timestamp","contentFormat":"markdown","streamKind":"assistant","title":"Architecture"}
            {"runId":"20260314T121910000","source":"copilot.session","eventType":"session.resume","sessionId":"session-123","model":"{{WellKnownModelNames.GPT_5_4}}","details":"resumed","timestampUtc":"2026-03-14T12:19:03Z"}
            """);

        FileSystemRunHistoryCatalog service = new FileSystemRunHistoryCatalog();

        IReadOnlyList<PersistedRunEvent> events = service.GetEvents(runDirectory);

        Assert.Collection(
            events,
            evt =>
            {
                Assert.Equal("request", evt.Kind);
                Assert.Equal(DateTimeOffset.Parse("2026-03-14T12:19:00Z", CultureInfo.InvariantCulture), evt.TimestampUtc);
            },
            evt =>
            {
                Assert.Equal("copilot-session", evt.Kind);
                Assert.Equal(DateTimeOffset.Parse("2026-03-14T12:19:03Z", CultureInfo.InvariantCulture), evt.TimestampUtc);
            });
    }

    /// <summary>
    /// Verifies that request events without an explicit timestamp can still replay using the run identifier timestamp.
    /// </summary>
    [Fact]
    public void GetEvents_ReplaysRequestEventUsingRunIdTimestampWhenTimestampMissing()
    {
        string runDirectory = Path.Combine(this._workspaceRoot, ".agent-harness", "runs", "20260320T142540661");
        Directory.CreateDirectory(runDirectory);
        File.WriteAllText(Path.Combine(runDirectory, "events.jsonl"), """
            {"runId":"20260320T142540661","source":"request","message":"Run request received","taskPrompt":"Investigate the replay issue"}
            {"runId":"20260320T142540661","source":"architecture","kind":"agent-delta","agentId":"architecture","agentRole":"Architecture","message":"Rendered output","contentFormat":"markdown","streamKind":"assistant","title":"Architecture","timestampUtc":"2026-03-20T14:25:41Z"}
            """);

        FileSystemRunHistoryCatalog service = new FileSystemRunHistoryCatalog();

        IReadOnlyList<PersistedRunEvent> events = service.GetEvents(runDirectory);

        Assert.Collection(
            events,
            evt =>
            {
                Assert.Equal("request", evt.Kind);
                Assert.Equal("Investigate the replay issue", evt.TaskPrompt);
                Assert.Equal(DateTimeOffset.Parse("2026-03-20T14:25:40.661Z", CultureInfo.InvariantCulture), evt.TimestampUtc);
            },
            evt =>
            {
                Assert.Equal("agent-delta", evt.Kind);
                Assert.Equal("Rendered output", evt.Message);
            });
    }

    /// <summary>
    /// Verifies that request-event task prompts are redacted before being returned from replay history.
    /// </summary>
    [Fact]
    public void GetEvents_RedactsRequestTaskPromptSecrets()
    {
        string runDirectory = Path.Combine(this._workspaceRoot, ".agent-harness", "runs", "20260320T150000000");
        Directory.CreateDirectory(runDirectory);
        File.WriteAllText(Path.Combine(runDirectory, "events.jsonl"), """
            {"runId":"20260320T150000000","source":"request","message":"Run request received","taskPrompt":"Use github_pat_abcdefghijklmnopqrstuvwxyz123456 with Bearer abc123secret to inspect the repo","timestampUtc":"2026-03-20T15:00:00Z"}
            """);

        FileSystemRunHistoryCatalog service = new FileSystemRunHistoryCatalog();

        PersistedRunEvent evt = Assert.Single(service.GetEvents(runDirectory));

        Assert.Equal("request", evt.Kind);
        Assert.NotNull(evt.TaskPrompt);
        Assert.DoesNotContain("github_pat_", evt.TaskPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("abc123secret", evt.TaskPrompt, StringComparison.Ordinal);
        Assert.Contains("***REDACTED***", evt.TaskPrompt, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(this._workspaceRoot))
        {
            Directory.Delete(this._workspaceRoot, recursive: true);
        }
    }
}
