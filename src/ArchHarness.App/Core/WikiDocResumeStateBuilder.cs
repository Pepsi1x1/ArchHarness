using System.Text.Json;

namespace ArchHarness.App.Core;

/// <summary>
/// Reconstructs a <see cref="WikiDocResumeState"/> from a prior WikiDoc run's
/// <c>WikiDocCheckpoint.json</c> or, as a fallback, from the persisted
/// <c>copilot-sdk-events.jsonl</c> combined with on-disk wiki output.
/// </summary>
#pragma warning disable S2325 // DI-injectable sealed class; instance method is correct for the abstraction boundary even without current instance state.

public sealed class WikiDocResumeStateBuilder
{
    private const string CHECKPOINT_FILE = "WikiDocCheckpoint.json";
    private const string SDK_EVENTS_FILE = "copilot-sdk-events.jsonl";
    private const string WIKIDOC_SESSION_PREFIX = "wikidoc-";

    /// <summary>
    /// Attempts to load resume state for the specified run directory.
    /// Returns <c>null</c> when no recoverable progress exists.
    /// </summary>
    public WikiDocResumeState? TryBuild(
        string runDirectory,
        string scanRoot,
        IReadOnlyList<WikiDocRepositoryInfo> repositories,
        WikiDocOutputResolver resolver)
    {
        // Prefer the structured checkpoint when available.
        WikiDocResumeState? fromCheckpoint = TryLoadFromCheckpoint(runDirectory);
        if (fromCheckpoint is not null)
        {
            return fromCheckpoint;
        }

        // Fall back to reconstructing from SDK events + disk.
        return TryBuildFromSdkEventsAndDisk(runDirectory, scanRoot, repositories, resolver);
    }

    private static WikiDocResumeState? TryLoadFromCheckpoint(string runDirectory)
    {
        string checkpointPath = Path.Combine(runDirectory, CHECKPOINT_FILE);
        if (!File.Exists(checkpointPath))
        {
            return null;
        }

        try
        {
            string json = File.ReadAllText(checkpointPath);
            WikiDocCheckpoint? checkpoint = JsonSerializer.Deserialize<WikiDocCheckpoint>(json, JsonDefaults.WEB_INDENTED);
            if (checkpoint is null)
            {
                return null;
            }

            // A checkpoint with 0 completed repos is valid — it means the run started
            // but no repos finished. Return it so we don't fall through to the slower
            // SDK-events path and risk mismatching state.
            return new WikiDocResumeState(checkpoint.CompletedRepositories, checkpoint.MegaWikiCompleted);
        }
        catch (JsonException)
        {
            // Checkpoint file is corrupt (e.g. from a concurrent-write race in a prior version).
            // Fall through to SDK-events reconstruction.
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reconstructs resume state by correlating SDK event session CWDs with discovered
    /// repository roots, then verifying completion via both turn.end events and on-disk output.
    /// </summary>
    private static WikiDocResumeState? TryBuildFromSdkEventsAndDisk(
        string runDirectory,
        string scanRoot,
        IReadOnlyList<WikiDocRepositoryInfo> repositories,
        WikiDocOutputResolver resolver)
    {
        string sdkEventsPath = Path.Combine(runDirectory, SDK_EVENTS_FILE);
        if (!File.Exists(sdkEventsPath))
        {
            return null;
        }

        // Scan SDK events to build a mapping: sessionId -> CWD, and track which sessionIds
        // have a turn.end event (indicating the agent finished).
        (Dictionary<string, string> sessionCwds, HashSet<string> completedSessionIds) = ScanSdkEvents(sdkEventsPath);
        if (completedSessionIds.Count == 0)
        {
            return null;
        }

        // Build a set of repository roots that have a completed session by matching CWDs.
        HashSet<string> completedRepoPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool megaWikiCompleted = false;
        foreach (string sessionId in completedSessionIds)
        {
            if (!sessionCwds.TryGetValue(sessionId, out string? cwd) || string.IsNullOrWhiteSpace(cwd))
            {
                continue;
            }

            string normalizedCwd = Path.GetFullPath(cwd);
            completedRepoPaths.Add(normalizedCwd);

            // Detect megawiki completion: the synthesis session uses scanRoot as CWD.
            if (string.Equals(normalizedCwd, Path.GetFullPath(scanRoot), StringComparison.OrdinalIgnoreCase))
            {
                string megaWikiRoot = Path.Combine(scanRoot, "megawiki");
                string megaWikiFallback = Path.Combine(runDirectory, "wikidoc-fallback", "megawiki");
                WikiDocOutputResolution megaResolution = resolver.Resolve(megaWikiRoot, megaWikiFallback);
                if (HasCompletedOutputOnDisk(megaResolution.OutputRoot))
                {
                    megaWikiCompleted = true;
                }
            }
        }

        List<WikiDocCompletedRepository> completed = new List<WikiDocCompletedRepository>();
        foreach (WikiDocRepositoryInfo repository in repositories)
        {
            string fallbackRoot = Path.Combine(runDirectory, "wikidoc-fallback", repository.SafeKey);
            WikiDocOutputResolution resolution = resolver.Resolve(repository.RepositoryRoot, fallbackRoot);

            bool hasDiskOutput = HasCompletedOutputOnDisk(resolution.OutputRoot);
            if (!hasDiskOutput)
            {
                continue;
            }

            bool hasSessionEvidence = completedRepoPaths.Contains(Path.GetFullPath(repository.RepositoryRoot));
            if (!hasSessionEvidence)
            {
                // SDK events are available but no session matched this repo.
                // Only recover if the output directory has multiple pages (stronger evidence).
                int mdFileCount = 0;
                try
                {
                    mdFileCount = Directory.GetFiles(resolution.OutputRoot, "*.md", SearchOption.AllDirectories).Length;
                }
                catch (IOException)
                {
                    // Swallow filesystem errors when probing output directories that may not exist.
                }

                if (mdFileCount < 2)
                {
                    continue;
                }
            }

            string homePath = Path.Combine(resolution.OutputRoot, "Home.md");
            string sessionKey = $"{WIKIDOC_SESSION_PREFIX}{repository.SafeKey}";
            (string summary, IReadOnlyList<WikiDocConceptSeed> concepts) = TryReadRepositoryIndexFromReport(runDirectory, sessionKey);

            completed.Add(new WikiDocCompletedRepository(
                sessionKey,
                repository.RepositoryRoot,
                repository.RelativePath,
                repository.DisplayName,
                resolution.OutputRoot,
                resolution.RequestedLocalRoot,
                homePath,
                resolution.UsedFallback,
                resolution.RenameCandidate,
                resolution.RenameCandidateWasEligible,
                resolution.RenamedFrom,
                resolution.FallbackReasonCode,
                resolution.FallbackReason,
                summary,
                concepts));
        }

        return completed.Count > 0
            ? new WikiDocResumeState(completed, megaWikiCompleted)
            : null;
    }

    /// <summary>
    /// Scans the SDK events file to extract session-to-CWD mappings and identify sessions
    /// that reached a <c>turn.end</c> event.
    /// </summary>
    internal static (Dictionary<string, string> SessionCwds, HashSet<string> CompletedSessionIds) ScanSdkEvents(string sdkEventsPath)
    {
        Dictionary<string, string> sessionCwds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> completedSessionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (string line in File.ReadLines(sdkEventsPath))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    using JsonDocument doc = JsonDocument.Parse(line);
                    JsonElement root = doc.RootElement;

                    string? sessionId = root.TryGetProperty("sessionId", out JsonElement sidEl) ? sidEl.GetString() : null;
                    if (string.IsNullOrWhiteSpace(sessionId))
                    {
                        continue;
                    }

                    string? eventType = root.TryGetProperty("eventType", out JsonElement etEl) ? etEl.GetString() : null;

                    // Extract CWD from payloadJson when available.
                    if (!sessionCwds.ContainsKey(sessionId))
                    {
                        string? cwd = TryExtractCwdFromPayload(root);
                        if (!string.IsNullOrWhiteSpace(cwd))
                        {
                            sessionCwds[sessionId] = cwd;
                        }
                    }

                    // Detect turn completion.
                    // The SDK emits "assistant.turn_end" (underscore) but we also
                    // match "turn.end" (dot) and "TurnEnd" (camelCase) for defensive compatibility.
                    if (eventType is not null
                        && (eventType.Contains("turn_end", StringComparison.OrdinalIgnoreCase)
                            || eventType.Contains("turn.end", StringComparison.OrdinalIgnoreCase)
                            || eventType.Contains("TurnEnd", StringComparison.OrdinalIgnoreCase)))
                    {
                        completedSessionIds.Add(sessionId);
                    }
                }
                catch (JsonException)
                {
                    // Skip malformed lines.
                }
            }
        }
        catch (IOException)
        {
            // Return empty results on read failure.
        }

        return (sessionCwds, completedSessionIds);
    }

    /// <summary>
    /// Extracts the <c>cwd</c> field from the nested <c>payloadJson</c> property of an SDK event.
    /// </summary>
    private static string? TryExtractCwdFromPayload(JsonElement root)
    {
        if (!root.TryGetProperty("payloadJson", out JsonElement payloadJsonEl))
        {
            return null;
        }

        string? payloadJsonStr = payloadJsonEl.GetString();
        if (string.IsNullOrWhiteSpace(payloadJsonStr))
        {
            return null;
        }

        try
        {
            using JsonDocument payloadDoc = JsonDocument.Parse(payloadJsonStr);
            JsonElement payloadRoot = payloadDoc.RootElement;

            // CWD can appear at root level or nested under data.input.
            if (payloadRoot.TryGetProperty("data", out JsonElement data))
            {
                if (data.TryGetProperty("input", out JsonElement input) && input.TryGetProperty("cwd", out JsonElement cwdInner))
                {
                    return cwdInner.GetString();
                }

                if (data.TryGetProperty("cwd", out JsonElement cwdData))
                {
                    return cwdData.GetString();
                }
            }

            if (payloadRoot.TryGetProperty("cwd", out JsonElement cwdRoot))
            {
                return cwdRoot.GetString();
            }
        }
        catch (JsonException)
        {
            // Payload is not valid JSON.
        }

        return null;
    }

    private static (string Summary, IReadOnlyList<WikiDocConceptSeed> Concepts) TryReadRepositoryIndexFromReport(
        string runDirectory,
        string sessionKey)
    {
        string reportPath = Path.Combine(runDirectory, "WikiDocReport.json");
        if (!File.Exists(reportPath))
        {
            return ("(resumed – summary unavailable)", Array.Empty<WikiDocConceptSeed>());
        }

        try
        {
            string json = File.ReadAllText(reportPath);
            using JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("repositoryOutputs", out JsonElement outputs))
            {
                return ("(resumed – summary unavailable)", Array.Empty<WikiDocConceptSeed>());
            }

            foreach (JsonElement output in outputs.EnumerateArray())
            {
                string? key = output.TryGetProperty("documentationSessionKey", out JsonElement dsk) ? dsk.GetString() : null;
                if (!string.Equals(key, sessionKey, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string summary = output.TryGetProperty("summary", out JsonElement s) ? s.GetString() ?? string.Empty : string.Empty;
                List<WikiDocConceptSeed> concepts = new List<WikiDocConceptSeed>();
                if (output.TryGetProperty("concepts", out JsonElement conceptsElement))
                {
                    foreach (JsonElement c in conceptsElement.EnumerateArray())
                    {
                        string name = c.TryGetProperty("name", out JsonElement n) ? n.GetString() ?? string.Empty : string.Empty;
                        string conceptSummary = c.TryGetProperty("summary", out JsonElement cs) ? cs.GetString() ?? string.Empty : string.Empty;
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            concepts.Add(new WikiDocConceptSeed(name, conceptSummary));
                        }
                    }
                }

                return (summary, concepts);
            }
        }
        catch (Exception)
        {
            // Fall through to placeholder.
        }

        return ("(resumed – summary unavailable)", Array.Empty<WikiDocConceptSeed>());
    }

    /// <summary>
    /// Determines whether a repository has verifiable completion evidence on disk.
    /// </summary>
    internal static bool HasCompletedOutputOnDisk(string outputRoot)
    {
        try
        {
            string homePath = Path.Combine(outputRoot, "Home.md");
            return File.Exists(homePath);
        }
        catch (IOException)
        {
            return false;
        }
    }
}
