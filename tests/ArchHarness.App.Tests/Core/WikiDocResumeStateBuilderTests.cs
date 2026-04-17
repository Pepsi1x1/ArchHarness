using System.Text.Json;
using ArchHarness.App.Core;

namespace ArchHarness.App.Tests.Core;

public sealed class WikiDocResumeStateBuilderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ArchHarnessResumeTests", Guid.NewGuid().ToString("N"));
    private readonly WikiDocResumeStateBuilder _builder = new WikiDocResumeStateBuilder();
    private readonly WikiDocRepositoryDiscoverer _discoverer = new WikiDocRepositoryDiscoverer();
    private readonly WikiDocOutputResolver _resolver = new WikiDocOutputResolver();

    [Fact]
    public void TryBuild_ReturnsNull_WhenRunDirectoryIsEmpty()
    {
        string scanRoot = CreateScanRoot("empty");
        string runDir = CreateRunDirectory(scanRoot);
        IReadOnlyList<WikiDocRepositoryInfo> repos = _discoverer.Discover(scanRoot);

        WikiDocResumeState? result = _builder.TryBuild(runDir, scanRoot, repos, _resolver);

        Assert.Null(result);
    }

    [Fact]
    public void TryBuild_LoadsFromCheckpoint()
    {
        string scanRoot = CreateScanRoot("checkpoint");
        string runDir = CreateRunDirectory(scanRoot);

        // Create a wiki/Home.md on disk (the checkpoint references it).
        string wikiDir = Path.Combine(scanRoot, "wiki");
        Directory.CreateDirectory(wikiDir);
        File.WriteAllText(Path.Combine(wikiDir, "Home.md"), "# Home");

        WikiDocCheckpoint checkpoint = new WikiDocCheckpoint(
            new[]
            {
                new WikiDocCompletedRepository(
                    "wikidoc-root",
                    scanRoot,
                    ".",
                    "checkpoint-root",
                    wikiDir,
                    wikiDir,
                    Path.Combine(wikiDir, "Home.md"),
                    false,
                    null,
                    false,
                    null,
                    null,
                    null,
                    "Root summary",
                    Array.Empty<WikiDocConceptSeed>())
            },
            false,
            DateTimeOffset.UtcNow);

        string checkpointJson = JsonSerializer.Serialize(checkpoint, JsonDefaults.WEB_INDENTED);
        File.WriteAllText(Path.Combine(runDir, "WikiDocCheckpoint.json"), checkpointJson);
        IReadOnlyList<WikiDocRepositoryInfo> repos = _discoverer.Discover(scanRoot);

        WikiDocResumeState? result = _builder.TryBuild(runDir, scanRoot, repos, _resolver);

        Assert.NotNull(result);
        Assert.Single(result.CompletedRepositories);
        Assert.Equal("wikidoc-root", result.CompletedRepositories[0].DocumentationSessionKey);
        Assert.False(result.MegaWikiCompleted);
    }

    [Fact]
    public void TryBuild_ReconstructsFromSdkEventsAndDisk()
    {
        string scanRoot = CreateScanRoot("sdk-events");
        string runDir = CreateRunDirectory(scanRoot);

        // Create wiki output on disk.
        string wikiDir = Path.Combine(scanRoot, "wiki");
        Directory.CreateDirectory(wikiDir);
        File.WriteAllText(Path.Combine(wikiDir, "Home.md"), "# Home page");

        // Write SDK events that indicate a completed session for this repo root.
        string sessionId = "archharness-testrun-abcd1234";
        string sdkEventsPath = Path.Combine(runDir, "copilot-sdk-events.jsonl");
        string hookPayload = JsonSerializer.Serialize(new { data = new { input = new { cwd = scanRoot } } });
        string hookEvent = JsonSerializer.Serialize(new { sessionId, eventType = "hook.start", payloadJson = hookPayload });
        string turnEndEvent = JsonSerializer.Serialize(new { sessionId, eventType = "assistant.turn.end", payloadJson = "" });
        File.WriteAllLines(sdkEventsPath, new[] { hookEvent, turnEndEvent });

        IReadOnlyList<WikiDocRepositoryInfo> repos = _discoverer.Discover(scanRoot);

        WikiDocResumeState? result = _builder.TryBuild(runDir, scanRoot, repos, _resolver);

        Assert.NotNull(result);
        Assert.Single(result.CompletedRepositories);
        Assert.Equal("wikidoc-root", result.CompletedRepositories[0].DocumentationSessionKey);
    }

    [Fact]
    public void TryBuild_ReturnsNull_WhenSdkEventsExistButNoDiskOutput()
    {
        string scanRoot = CreateScanRoot("no-disk");
        string runDir = CreateRunDirectory(scanRoot);

        // SDK events say session completed, but no Home.md on disk.
        string sessionId = "archharness-testrun-efgh5678";
        string sdkEventsPath = Path.Combine(runDir, "copilot-sdk-events.jsonl");
        string hookPayload = JsonSerializer.Serialize(new { data = new { input = new { cwd = scanRoot } } });
        string hookEvent = JsonSerializer.Serialize(new { sessionId, eventType = "hook.start", payloadJson = hookPayload });
        string turnEndEvent = JsonSerializer.Serialize(new { sessionId, eventType = "assistant.turn.end", payloadJson = "" });
        File.WriteAllLines(sdkEventsPath, new[] { hookEvent, turnEndEvent });

        IReadOnlyList<WikiDocRepositoryInfo> repos = _discoverer.Discover(scanRoot);

        WikiDocResumeState? result = _builder.TryBuild(runDir, scanRoot, repos, _resolver);

        Assert.Null(result);
    }

    [Fact]
    public void ScanSdkEvents_ExtractsSessionCwdAndTurnEnd()
    {
        string scanRoot = CreateScanRoot("scan");
        string runDir = CreateRunDirectory(scanRoot);
        string sdkEventsPath = Path.Combine(runDir, "copilot-sdk-events.jsonl");

        string sessionA = "session-A";
        string sessionB = "session-B";
        string payloadA = JsonSerializer.Serialize(new { data = new { input = new { cwd = "/repo/a" } } });
        string payloadB = JsonSerializer.Serialize(new { data = new { input = new { cwd = "/repo/b" } } });

        string[] lines =
        {
            JsonSerializer.Serialize(new { sessionId = sessionA, eventType = "hook.start", payloadJson = payloadA }),
            JsonSerializer.Serialize(new { sessionId = sessionB, eventType = "hook.start", payloadJson = payloadB }),
            JsonSerializer.Serialize(new { sessionId = sessionA, eventType = "assistant.turn.end", payloadJson = "" }),
        };
        File.WriteAllLines(sdkEventsPath, lines);

        (Dictionary<string, string> cwds, HashSet<string> completed) = WikiDocResumeStateBuilder.ScanSdkEvents(sdkEventsPath);

        Assert.Equal(2, cwds.Count);
        Assert.Equal("/repo/a", cwds[sessionA]);
        Assert.Equal("/repo/b", cwds[sessionB]);
        Assert.Single(completed);
        Assert.Contains(sessionA, completed);
        Assert.DoesNotContain(sessionB, completed);
    }

    [Fact]
    public void HasCompletedOutputOnDisk_ReturnsTrueWhenHomeMdExists()
    {
        string dir = Path.Combine(_root, "has-home");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Home.md"), "# Home");

        Assert.True(WikiDocResumeStateBuilder.HasCompletedOutputOnDisk(dir));
    }

    [Fact]
    public void HasCompletedOutputOnDisk_ReturnsFalseWhenMissing()
    {
        string dir = Path.Combine(_root, "no-home");
        Directory.CreateDirectory(dir);

        Assert.False(WikiDocResumeStateBuilder.HasCompletedOutputOnDisk(dir));
    }

    [Fact]
    public void TryBuild_ReturnsEmptyState_WhenCheckpointHasZeroCompletedRepos()
    {
        string scanRoot = CreateScanRoot("empty-checkpoint");
        string runDir = CreateRunDirectory(scanRoot);

        WikiDocCheckpoint checkpoint = new WikiDocCheckpoint(
            Array.Empty<WikiDocCompletedRepository>(),
            false,
            DateTimeOffset.UtcNow);

        string checkpointJson = JsonSerializer.Serialize(checkpoint, JsonDefaults.WEB_INDENTED);
        File.WriteAllText(Path.Combine(runDir, "WikiDocCheckpoint.json"), checkpointJson);
        IReadOnlyList<WikiDocRepositoryInfo> repos = _discoverer.Discover(scanRoot);

        WikiDocResumeState? result = _builder.TryBuild(runDir, scanRoot, repos, _resolver);

        Assert.NotNull(result);
        Assert.Empty(result.CompletedRepositories);
    }

    [Fact]
    public void TryBuild_FallsBackToSdkEvents_WhenCheckpointIsCorrupt()
    {
        string scanRoot = CreateScanRoot("corrupt-checkpoint");
        string runDir = CreateRunDirectory(scanRoot);

        // Write corrupt JSON to the checkpoint file.
        File.WriteAllText(Path.Combine(runDir, "WikiDocCheckpoint.json"), "{ this is not valid json }}}");

        // Create wiki output on disk.
        string wikiDir = Path.Combine(scanRoot, "wiki");
        Directory.CreateDirectory(wikiDir);
        File.WriteAllText(Path.Combine(wikiDir, "Home.md"), "# Home page");

        // Write SDK events that indicate a completed session.
        string sessionId = "archharness-testrun-corrupt1";
        string sdkEventsPath = Path.Combine(runDir, "copilot-sdk-events.jsonl");
        string hookPayload = JsonSerializer.Serialize(new { data = new { input = new { cwd = scanRoot } } });
        string hookEvent = JsonSerializer.Serialize(new { sessionId, eventType = "hook.start", payloadJson = hookPayload });
        string turnEndEvent = JsonSerializer.Serialize(new { sessionId, eventType = "assistant.turn.end", payloadJson = "" });
        File.WriteAllLines(sdkEventsPath, new[] { hookEvent, turnEndEvent });

        IReadOnlyList<WikiDocRepositoryInfo> repos = _discoverer.Discover(scanRoot);

        WikiDocResumeState? result = _builder.TryBuild(runDir, scanRoot, repos, _resolver);

        Assert.NotNull(result);
        Assert.Single(result.CompletedRepositories);
    }

    private string CreateScanRoot(string name)
    {
        string scanRoot = Path.Combine(_root, name, "scan-root");
        Directory.CreateDirectory(Path.Combine(scanRoot, ".git"));
        return scanRoot;
    }

    private static string CreateRunDirectory(string scanRoot)
    {
        string runDir = Path.Combine(scanRoot, ".agent-harness", "runs", "resume-test");
        Directory.CreateDirectory(runDir);
        return runDir;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            foreach (string path in Directory.GetFileSystemEntries(_root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(path, FileAttributes.Normal);
            }

            File.SetAttributes(_root, FileAttributes.Normal);
            Directory.Delete(_root, recursive: true);
        }
    }
}
