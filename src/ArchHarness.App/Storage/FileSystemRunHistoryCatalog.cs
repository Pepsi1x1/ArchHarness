using System.Text;
using System.Text.Json;
using System.Security;
using System.Globalization;
using ArchHarness.App.Core;

namespace ArchHarness.App.Storage;

/// <summary>
/// Reads persisted runs and top-level artifact previews from the file system.
/// </summary>
public sealed class FileSystemRunHistoryCatalog : IRunHistoryCatalog
{
    /// <inheritdoc />
    public IReadOnlyList<PersistedRunSummary> GetRecentRuns(string workspacePath, int maxCount = 20)
    {
        string root = Path.Combine(Path.GetFullPath(workspacePath), ".agent-harness", "runs");
        return this.GetRecentRunsFromRoot(root, maxCount);
    }

    /// <inheritdoc />
    public IReadOnlyList<PersistedRunSummary> GetRecentRunsFromRoot(string runsRootDirectory, int maxCount = 20)
    {
        string root = Path.GetFullPath(runsRootDirectory);
        if (!Directory.Exists(root))
        {
            return Array.Empty<PersistedRunSummary>();
        }

        return Directory.GetDirectories(root)
            .OrderByDescending(path => path)
            .Take(maxCount)
            .Select(path =>
            {
                PersistedRunSummaryMetadata metadata = TryReadRunSummaryMetadata(path);
                return new PersistedRunSummary(
                    Path.GetFileName(path),
                    path,
                    metadata.RunTitle,
                    metadata.ProjectId,
                    metadata.ProjectName);
            })
            .ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<RunArtifactPreview> GetArtifacts(string runDirectory, int previewLength = 2400)
    {
        if (!Directory.Exists(runDirectory))
        {
            return Array.Empty<RunArtifactPreview>();
        }

        return Directory.GetFiles(runDirectory)
            .OrderBy(file => file)
            .Select(file => BuildArtifact(file, previewLength))
            .ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<PersistedRunEvent> GetEvents(string runDirectory)
    {
        if (!Directory.Exists(runDirectory))
        {
            return Array.Empty<PersistedRunEvent>();
        }

        string eventsPath = Path.Combine(runDirectory, "events.jsonl");
        if (!File.Exists(eventsPath))
        {
            return Array.Empty<PersistedRunEvent>();
        }

        List<PersistedRunEvent> events = new List<PersistedRunEvent>();

        try
        {
            foreach (string line in File.ReadLines(eventsPath))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    using JsonDocument document = JsonDocument.Parse(line);
                    PersistedRunEvent? evt = TryBuildRunEvent(document.RootElement);
                    if (evt is not null)
                    {
                        events.Add(evt);
                    }
                }
                catch (JsonException)
                {
                    // Ignore malformed lines and continue scanning.
                }
            }
        }
        catch (IOException)
        {
            return Array.Empty<PersistedRunEvent>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<PersistedRunEvent>();
        }
        catch (SecurityException)
        {
            return Array.Empty<PersistedRunEvent>();
        }

        return events
            .OrderBy(evt => evt.TimestampUtc)
            .ToList();
    }

    private static RunArtifactPreview BuildArtifact(string filePath, int previewLength)
    {
        string name = Path.GetFileName(filePath);
        string extension = Path.GetExtension(filePath).ToLowerInvariant();
        string rawText = TryReadText(filePath);
        string kind = Classify(extension);
        string preview = FormatPreview(rawText, extension, previewLength);
        long fileSizeBytes = new FileInfo(filePath).Length;
        string formattedSize = FormatSize(fileSizeBytes);
        string description = $"{kind} • {formattedSize} • {filePath}";
        return new RunArtifactPreview(name, filePath, kind, description, preview);
    }

    private static string TryReadText(string filePath)
    {
        try
        {
            return File.ReadAllText(filePath);
        }
        catch (DecoderFallbackException)
        {
            return "Binary file preview is not supported.";
        }
        catch (IOException)
        {
            return "Unable to read file preview.";
        }
        catch (UnauthorizedAccessException)
        {
            return "Unable to read file preview.";
        }
        catch (SecurityException)
        {
            return "Unable to read file preview.";
        }
    }

    private static string Classify(string extension)
        => extension switch
        {
            ".json" => "JSON",
            ".jsonl" => "JSON lines",
            ".md" => "Markdown",
            ".txt" => "Text",
            _ => string.IsNullOrWhiteSpace(extension) ? "Text" : extension.TrimStart('.').ToUpperInvariant()
        };

    private static string FormatPreview(string rawText, string extension, int previewLength)
    {
        string normalized = rawText.Replace("\r\n", "\n");
        string formatted = extension switch
        {
            ".json" => PrettyPrintJson(normalized),
            ".jsonl" => FormatJsonLines(normalized),
            ".md" => FormatMarkdown(normalized),
            _ => normalized
        };

        if (formatted.Length <= previewLength)
        {
            return formatted;
        }

        return formatted[..previewLength] + Environment.NewLine + Environment.NewLine + "...";
    }

    private static string PrettyPrintJson(string input)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(input);
            return JsonSerializer.Serialize(document.RootElement, JsonDefaults.INDENTED);
        }
        catch (JsonException)
        {
            return input;
        }
    }

    private static string FormatJsonLines(string input)
    {
        string[] lines = input.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        IEnumerable<string> previewLines = lines.Take(20).Select((line, index) => $"{index + 1,2}: {line}");
        return string.Join(Environment.NewLine, previewLines);
    }

    private static string FormatMarkdown(string input)
    {
        string[] lines = input.Split('\n');
        StringBuilder builder = new StringBuilder();
        foreach (string line in lines)
        {
            string trimmed = line.TrimEnd();
            if (trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                builder.AppendLine(trimmed.ToUpperInvariant());
                continue;
            }

            builder.AppendLine(trimmed);
        }

        return builder.ToString().TrimEnd();
    }

    private static string FormatSize(long sizeInBytes)
    {
        if (sizeInBytes < 1024)
        {
            return $"{sizeInBytes} B";
        }

        if (sizeInBytes < 1024 * 1024)
        {
            return $"{sizeInBytes / 1024d:F1} KB";
        }

        return $"{sizeInBytes / 1024d / 1024d:F1} MB";
    }

    private static PersistedRunSummaryMetadata TryReadRunSummaryMetadata(string runDirectory)
    {
        PersistedRunSummaryMetadata runLogMetadata = TryReadRunLogMetadata(runDirectory);
        PersistedRunSummaryMetadata requestMetadata = TryReadRequestMetadata(runDirectory);

        string? runTitle = FirstNonEmpty(runLogMetadata.RunTitle, requestMetadata.RunTitle);
        if (string.IsNullOrWhiteSpace(runTitle))
        {
            runTitle = BuildFallbackTitle(requestMetadata.TaskPrompt);
        }

        return new PersistedRunSummaryMetadata(
            runTitle,
            FirstNonEmpty(runLogMetadata.ProjectId, requestMetadata.ProjectId),
            FirstNonEmpty(runLogMetadata.ProjectName, requestMetadata.ProjectName),
            requestMetadata.TaskPrompt);
    }

    private static PersistedRunSummaryMetadata TryReadRunLogMetadata(string runDirectory)
    {
        string runLogPath = Path.Combine(runDirectory, "run-log.json");
        if (!File.Exists(runLogPath))
        {
            return PersistedRunSummaryMetadata.Empty;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(runLogPath));
            JsonElement root = document.RootElement;
            return new PersistedRunSummaryMetadata(
                ReadString(root, "runTitle"),
                ReadString(root, "projectId"),
                ReadString(root, "projectName"),
                null);
        }
        catch (IOException)
        {
            return PersistedRunSummaryMetadata.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return PersistedRunSummaryMetadata.Empty;
        }
        catch (SecurityException)
        {
            return PersistedRunSummaryMetadata.Empty;
        }
        catch (JsonException)
        {
            return PersistedRunSummaryMetadata.Empty;
        }
    }

    private static PersistedRunSummaryMetadata TryReadRequestMetadata(string runDirectory)
    {
        string eventsPath = Path.Combine(runDirectory, "events.jsonl");
        if (!File.Exists(eventsPath))
        {
            return PersistedRunSummaryMetadata.Empty;
        }

        try
        {
            foreach (string line in File.ReadLines(eventsPath))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    using JsonDocument document = JsonDocument.Parse(line);
                    JsonElement root = document.RootElement;
                    if (!string.Equals(ReadString(root, "source"), "request", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    return new PersistedRunSummaryMetadata(
                        ReadString(root, "runTitle"),
                        ReadString(root, "projectId"),
                        ReadString(root, "projectName"),
                        ReadString(root, "taskPrompt"));
                }
                catch (JsonException)
                {
                    // Ignore malformed lines and continue scanning for the request event.
                }
            }
        }
        catch (IOException)
        {
            return PersistedRunSummaryMetadata.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return PersistedRunSummaryMetadata.Empty;
        }
        catch (SecurityException)
        {
            return PersistedRunSummaryMetadata.Empty;
        }

        return PersistedRunSummaryMetadata.Empty;
    }

    private static string? FirstNonEmpty(string? primary, string? fallback)
        => string.IsNullOrWhiteSpace(primary) ? fallback : primary;

    private static string? BuildFallbackTitle(string? taskPrompt)
    {
        if (string.IsNullOrWhiteSpace(taskPrompt))
        {
            return null;
        }

        string[] words = taskPrompt
            .Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Take(6)
            .ToArray();

        string candidate = string.Join(" ", words).Trim();
        return string.IsNullOrWhiteSpace(candidate) ? null : candidate;
    }

    private static PersistedRunEvent? TryBuildRunEvent(JsonElement root)
    {
        string? source = ReadString(root, "source");
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        DateTimeOffset timestampUtc = ReadTimestamp(root);

        if (string.Equals(source, "request", StringComparison.OrdinalIgnoreCase))
        {
            string? taskPrompt = ReadString(root, "taskPrompt");
            if (string.IsNullOrWhiteSpace(taskPrompt))
            {
                return null;
            }

            return new PersistedRunEvent(
                timestampUtc,
                "request",
                source,
                ReadString(root, "message") ?? "Run request received",
                TaskPrompt: taskPrompt);
        }

        string? kind = ReadString(root, "kind");
        if (string.Equals(kind, "agent-delta", StringComparison.OrdinalIgnoreCase))
        {
            string? message = ReadString(root, "message");
            if (string.IsNullOrWhiteSpace(message))
            {
                return null;
            }

            return new PersistedRunEvent(
                timestampUtc,
                "agent-delta",
                source,
                message,
                AgentId: ReadString(root, "agentId"),
                AgentRole: ReadString(root, "agentRole"),
                ContentFormat: ReadString(root, "contentFormat"),
                StreamKind: ReadString(root, "streamKind"),
                Title: ReadString(root, "title"));
        }

        if (string.Equals(source, "copilot.session", StringComparison.OrdinalIgnoreCase))
        {
            string eventType = ReadString(root, "eventType") ?? "copilot.session";
            string? details = ReadString(root, "details");
            string message = string.IsNullOrWhiteSpace(details)
                ? eventType
                : $"{eventType}: {details}";
            return new PersistedRunEvent(
                timestampUtc,
                "copilot-session",
                source,
                message,
                SessionId: ReadString(root, "sessionId"),
                Model: ReadString(root, "model"),
                Details: eventType);
        }

        return null;
    }

    private static DateTimeOffset ReadTimestamp(JsonElement root)
    {
        string? rawTimestamp = ReadString(root, "timestampUtc") ?? ReadString(root, "timestamp");
        if (DateTimeOffset.TryParse(rawTimestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset timestampUtc))
        {
            return timestampUtc;
        }

        return DateTimeOffset.MinValue;
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
    }

    private sealed record PersistedRunSummaryMetadata(string? RunTitle, string? ProjectId, string? ProjectName, string? TaskPrompt)
    {
        public static readonly PersistedRunSummaryMetadata Empty = new PersistedRunSummaryMetadata(null, null, null, null);
    }
}