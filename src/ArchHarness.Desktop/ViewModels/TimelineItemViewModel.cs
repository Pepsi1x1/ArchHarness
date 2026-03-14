using Avalonia.Media;

namespace ArchHarness.Desktop.ViewModels;

public sealed class TimelineItemViewModel : ViewModelBase
{
    public TimelineItemViewModel(string title, string subtitle, string detail, string accent)
    {
        this.Title = title;
        this.Subtitle = subtitle;
        this.Detail = detail;
        this.AccentBrush = Brush.Parse(accent);
    }

    public string Title { get; }

    public string Subtitle { get; }

    public string Detail { get; }

    public IBrush AccentBrush { get; }
}