namespace ArchHarness.Desktop.ViewModels;

/// <summary>
/// Represents a persisted run for display in the desktop run history list.
/// </summary>
public sealed class RunSummaryViewModel : ViewModelBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RunSummaryViewModel"/> class.
    /// </summary>
    /// <param name="runId">The unique timestamped run identifier.</param>
    /// <param name="runDirectory">The full file-system path to the run directory.</param>
    public RunSummaryViewModel(string runId, string runDirectory)
    {
        this.RunId = runId;
        this.RunDirectory = runDirectory;
    }

    /// <summary>Gets the unique timestamped run identifier.</summary>
    public string RunId { get; }

    /// <summary>Gets the full file-system path to the run directory.</summary>
    public string RunDirectory { get; }

    /// <summary>Gets the display title for this run.</summary>
    public string Title => $"Run {this.RunId}";

    /// <summary>Gets the abbreviated subtitle combining a truncated run ID and formatted timestamp.</summary>
    public string Subtitle => $"{this.RunId[..Math.Min(8, this.RunId.Length)]} • {this.TryFormatTimestamp()}";

    private string TryFormatTimestamp()
    {
        if (DateTimeOffset.TryParseExact(this.RunId, "yyyyMMddTHHmmssfff", null, System.Globalization.DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed))
        {
            return parsed.ToLocalTime().ToString("MMM d, HH:mm");
        }

        return "Unknown start";
    }
}