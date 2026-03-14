using Avalonia.Media;

namespace ArchHarness.Desktop.ViewModels;

/// <summary>
/// Represents a single timeline entry for display in the desktop timeline surface.
/// </summary>
public sealed class TimelineItemViewModel : ViewModelBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TimelineItemViewModel"/> class.
    /// </summary>
    /// <param name="title">The primary title of the timeline entry.</param>
    /// <param name="subtitle">The secondary label for the timeline entry.</param>
    /// <param name="detail">The detailed description text.</param>
    /// <param name="accent">The hex color string used for the accent brush.</param>
    public TimelineItemViewModel(string title, string subtitle, string detail, string accent)
    {
        this.Title = title;
        this.Subtitle = subtitle;
        this.Detail = detail;
        this.AccentBrush = ParseBrushSafe(accent);
    }

    private static IBrush ParseBrushSafe(string accent)
    {
        try
        {
            return Brush.Parse(accent);
        }
        catch (FormatException)
        {
            return Brushes.Gray;
        }
    }

    /// <summary>Gets the primary title of the timeline entry.</summary>
    public string Title { get; }

    /// <summary>Gets the secondary label for the timeline entry.</summary>
    public string Subtitle { get; }

    /// <summary>Gets the detailed description text.</summary>
    public string Detail { get; }

    /// <summary>Gets the accent brush parsed from the hex color string.</summary>
    public IBrush AccentBrush { get; }
}