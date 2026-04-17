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

    /// <summary>
    /// Temporary test: reads a real interrupted WikiDoc run and proves the resume state
    /// builder correctly reconstructs the completed repos from SDK events + disk.
    /// </summary>
    [Fact]
    public void TryBuild_ReconstructsFromRealRun_20260414T091700922()
    {
        string scanRoot = "/Users/davidthompson/source/carpsclone/DefaultCollection";
        string runDir = "/Users/davidthompson/source/carpsclone/DefaultCollection/.agent-harness/runs/20260414T091700922";

        if (!Directory.Exists(runDir))
        {
            return;
        }

        // Step 1: Discover repos.
        IReadOnlyList<WikiDocRepositoryInfo> repos = _discoverer.Discover(scanRoot);
        Assert.True(repos.Count > 0, "Should discover at least 1 repository.");

        // Step 2: Scan SDK events directly to inspect what we find.
        string sdkEventsPath = Path.Combine(runDir, "copilot-sdk-events.jsonl");
        Assert.True(File.Exists(sdkEventsPath), "SDK events file must exist.");

        (Dictionary<string, string> sessionCwds, HashSet<string> completedSessionIds) =
            WikiDocResumeStateBuilder.ScanSdkEvents(sdkEventsPath);

        // Diagnostic: how many sessions did we find?
        int totalSessions = sessionCwds.Count;
        int completedSessions = completedSessionIds.Count;

        // Step 3: Check which completed sessions match known repo roots.
        HashSet<string> normalizedRepoRoots = new HashSet<string>(
            repos.Select(r => Path.GetFullPath(r.RepositoryRoot)),
            StringComparer.OrdinalIgnoreCase);

        List<string> matchedCwds = new List<string>();
        List<string> unmatchedCwds = new List<string>();
        foreach (string sessionId in completedSessionIds)
        {
            if (sessionCwds.TryGetValue(sessionId, out string? cwd) && !string.IsNullOrWhiteSpace(cwd))
            {
                string normalized = Path.GetFullPath(cwd);
                if (normalizedRepoRoots.Contains(normalized))
                {
                    matchedCwds.Add($"{sessionId} -> {cwd}");
                }
                else
                {
                    unmatchedCwds.Add($"{sessionId} -> {cwd}");
                }
            }
        }

        // Step 4: Check which matched repos also have disk output.
        int withDiskOutput = 0;
        foreach (WikiDocRepositoryInfo repo in repos)
        {
            string fallbackRoot = Path.Combine(runDir, "wikidoc-fallback", repo.SafeKey);
            WikiDocOutputResolution resolution = _resolver.Resolve(repo.RepositoryRoot, fallbackRoot);
            if (WikiDocResumeStateBuilder.HasCompletedOutputOnDisk(resolution.OutputRoot))
            {
                withDiskOutput++;
            }
        }

        // Step 5: Now run the full TryBuild.
        WikiDocResumeState? result = _builder.TryBuild(runDir, scanRoot, repos, _resolver);

        // Report everything via a failure message if result is null.
        string diagnostic = $"""
            Repos discovered: {repos.Count}
            Total SDK sessions with CWD: {totalSessions}
            Completed SDK sessions: {completedSessions}
            Completed session CWDs matching repo roots: {matchedCwds.Count}
            Completed session CWDs NOT matching any repo: {unmatchedCwds.Count}
            Repos with disk output (Home.md): {withDiskOutput}
            TryBuild result: {(result is null ? "NULL" : $"{result.CompletedRepositories.Count} completed")}

            Sample unmatched CWDs (first 10):
            {string.Join("\n  ", unmatchedCwds.Take(10))}

            Sample matched CWDs (first 10):
            {string.Join("\n  ", matchedCwds.Take(10))}
            """;

        Assert.True(result is not null, diagnostic);
        Assert.True(result.CompletedRepositories.Count > 0,
            $"Expected at least one completed repository. {diagnostic}");

        // The real run had 22 repos with Home.md on disk and 25 with turn_end events.
        // Repos need BOTH session evidence AND disk output, so expect the overlap.
        Assert.True(result.CompletedRepositories.Count >= 20,
            $"Expected at least 20 completed repositories but got {result.CompletedRepositories.Count}. {diagnostic}");

        foreach (WikiDocCompletedRepository repo in result.CompletedRepositories)
        {
            Assert.True(File.Exists(repo.HomePath),
                $"Home.md missing for '{repo.RepositoryDisplayName}' at {repo.HomePath}");
        }

        Assert.False(result.MegaWikiCompleted,
            "The run was interrupted before megawiki; should not be marked completed.");
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
