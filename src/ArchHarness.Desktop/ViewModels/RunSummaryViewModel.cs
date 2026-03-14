namespace ArchHarness.Desktop.ViewModels;

public sealed class RunSummaryViewModel : ViewModelBase
{
    public RunSummaryViewModel(string runId, string runDirectory)
    {
        this.RunId = runId;
        this.RunDirectory = runDirectory;
    }

    public string RunId { get; }

    public string RunDirectory { get; }

    public string Title => $"Run {this.RunId}";

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