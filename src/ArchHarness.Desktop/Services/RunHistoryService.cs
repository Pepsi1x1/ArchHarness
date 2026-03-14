using System.Text;
using System.Text.Json;
using ArchHarness.Desktop.ViewModels;

namespace ArchHarness.Desktop;

public sealed class RunHistoryService : IRunHistoryService
{
    public IReadOnlyList<RunSummaryViewModel> GetRecentRuns(string workspacePath, int maxCount = 20)
    {
        string root = Path.Combine(Path.GetFullPath(workspacePath), ".agent-harness", "runs");
        if (!Directory.Exists(root))
        {
            return Array.Empty<RunSummaryViewModel>();
        }

        return Directory.GetDirectories(root)
            .OrderByDescending(path => path)
            .Take(maxCount)
            .Select(path => new RunSummaryViewModel(Path.GetFileName(path), path))
            .ToList();
    }

    public IReadOnlyList<ArtifactItemViewModel> GetArtifacts(string runDirectory, int previewLength = 2400)
    {
        if (!Directory.Exists(runDirectory))
        {
            return Array.Empty<ArtifactItemViewModel>();
        }

        return Directory.GetFiles(runDirectory)
            .OrderBy(file => file)
            .Select(file => BuildArtifact(file, previewLength))
            .ToList();
    }

    private static ArtifactItemViewModel BuildArtifact(string filePath, int previewLength)
    {
        string name = Path.GetFileName(filePath);
        string extension = Path.GetExtension(filePath).ToLowerInvariant();
        string rawText = TryReadText(filePath);
        string kind = Classify(extension);
        string preview = FormatPreview(rawText, extension, previewLength);
        string description = $"{kind} • {FormatSize(new FileInfo(filePath).Length)} • {filePath}";
        return new ArtifactItemViewModel(name, filePath, kind, description, preview);
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
        catch (IOException ex)
        {
            return $"Unable to read file preview: {ex.Message}";
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
            return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
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
}