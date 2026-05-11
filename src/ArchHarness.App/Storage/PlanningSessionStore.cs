using System.Collections.Concurrent;
using System.Security;
using System.Text.Json;
using ArchHarness.App.Core;

namespace ArchHarness.App.Storage;

/// <summary>
/// Durable storage for <see cref="PlanningSession"/> records. Persists under
/// <c>{workspaceRoot}/.agent-harness/planning-sessions/{id}.json</c> so sessions survive
/// handoff, resume, and process restarts.
/// </summary>
public interface IPlanningSessionStore
{
    /// <summary>
    /// Loads the session with the given id from the specified workspace, or null if absent.
    /// </summary>
    PlanningSession? Get(string workspaceRoot, string sessionId);

    /// <summary>
    /// Writes the session atomically.
    /// </summary>
    Task WriteAsync(string workspaceRoot, PlanningSession session, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically loads, mutates, and writes a session. Returns null when the mutator returns null.
    /// </summary>
    Task<PlanningSession?> UpdateAsync(
        string workspaceRoot,
        string sessionId,
        Func<PlanningSession?, PlanningSession?> mutator,
        CancellationToken cancellationToken);
}

/// <summary>
/// File-system-backed implementation of <see cref="IPlanningSessionStore"/>.
/// </summary>
public sealed class PlanningSessionStore : IPlanningSessionStore
{
    private const string PLANNING_SESSIONS_DIRECTORY = "planning-sessions";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> WriteGates = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public PlanningSession? Get(string workspaceRoot, string sessionId)
    {
        string filePath = GetSessionFilePath(workspaceRoot, sessionId);
        return ReadSession(filePath);
    }

    /// <inheritdoc />
    public async Task WriteAsync(string workspaceRoot, PlanningSession session, CancellationToken cancellationToken)
    {
        if (session is null)
        {
            throw new ArgumentNullException(nameof(session));
        }

        string filePath = GetSessionFilePath(workspaceRoot, session.Id);
        SemaphoreSlim gate = GetWriteGate(filePath);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await FileSystemStorageHelper.WriteJsonFileAsync(filePath, session, JsonDefaults.WEB_INDENTED, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<PlanningSession?> UpdateAsync(
        string workspaceRoot,
        string sessionId,
        Func<PlanningSession?, PlanningSession?> mutator,
        CancellationToken cancellationToken)
    {
        if (mutator is null)
        {
            throw new ArgumentNullException(nameof(mutator));
        }

        string filePath = GetSessionFilePath(workspaceRoot, sessionId);
        SemaphoreSlim gate = GetWriteGate(filePath);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            PlanningSession? current = ReadSession(filePath);
            PlanningSession? updated = mutator(current);
            if (updated is null)
            {
                return null;
            }

            updated = updated with { UpdatedAtUtc = DateTimeOffset.UtcNow };
            await FileSystemStorageHelper.WriteJsonFileAsync(filePath, updated, JsonDefaults.WEB_INDENTED, cancellationToken).ConfigureAwait(false);
            return updated;
        }
        finally
        {
            gate.Release();
        }
    }

    private static string GetSessionFilePath(string workspaceRoot, string sessionId)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            throw new ArgumentException("workspaceRoot must be provided", nameof(workspaceRoot));
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("sessionId must be provided", nameof(sessionId));
        }

        string safeId = SanitizeSessionId(sessionId);
        string directory = FileSystemStorageHelper.NormalizePath(
            Path.Combine(workspaceRoot, ".agent-harness", PLANNING_SESSIONS_DIRECTORY));
        return FileSystemStorageHelper.NormalizePath(Path.Combine(directory, $"{safeId}.json"));
    }

    private static PlanningSession? ReadSession(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        try
        {
            using FileStream stream = FileSystemStorageHelper.OpenReadStreamShared(filePath);
            return JsonSerializer.Deserialize<PlanningSession>(stream, JsonDefaults.WEB_INDENTED);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (SecurityException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static SemaphoreSlim GetWriteGate(string filePath)
        => WriteGates.GetOrAdd(filePath, _ => new SemaphoreSlim(1, 1));

    private static string SanitizeSessionId(string sessionId)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            sessionId = sessionId.Replace(invalid, '_');
        }

        return sessionId;
    }
}
