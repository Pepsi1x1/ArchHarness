using System.Collections.ObjectModel;

namespace ArchHarness.Desktop.ViewModels;

/// <summary>
/// Builds and manages timeline item entries for the desktop main window,
/// encapsulating accent color selection and layout seeding logic.
/// </summary>
public static class TimelineBuilder
{
    /// <summary>Primary accent color.</summary>
    public const string ACCENT_PRIMARY = "#F16436";
    /// <summary>Informational accent color.</summary>
    public const string ACCENT_INFO = "#5AA7FF";
    /// <summary>Success accent color.</summary>
    public const string ACCENT_SUCCESS = "#5FD08C";
    /// <summary>Warning accent color.</summary>
    public const string ACCENT_WARNING = "#FFB347";
    /// <summary>Danger accent color.</summary>
    public const string ACCENT_DANGER = "#FF6B6B";
    /// <summary>Style accent color.</summary>
    public const string ACCENT_STYLE = "#F5C451";
    /// <summary>Muted accent color.</summary>
    public const string ACCENT_MUTED = "#AAB6C4";

    /// <summary>
    /// Seeds the timeline with a single startup-ready entry.
    /// </summary>
    /// <param name="items">The timeline collection to populate.</param>
    public static void SeedEmpty(ObservableCollection<TimelineItemViewModel> items)
    {
        items.Clear();
        items.Add(new TimelineItemViewModel(
            "Desktop runtime ready",
            "Foundation milestone",
            "Console and desktop hosts now share the same runtime registration path, and the desktop host can start orchestrated runs directly.",
            ACCENT_PRIMARY));
    }

    /// <summary>
    /// Resets the timeline to indicate no persisted runs were found for the workspace.
    /// </summary>
    /// <param name="items">The timeline collection to populate.</param>
    /// <param name="workspacePath">The workspace path that was scanned.</param>
    public static void ResetForNoRuns(ObservableCollection<TimelineItemViewModel> items, string workspacePath)
    {
        items.Clear();
        items.Add(new TimelineItemViewModel(
            "No persisted runs",
            "Workspace scan",
            $"Nothing was found under {Path.Combine(Path.GetFullPath(workspacePath), ".agent-harness", "runs")}",
            ACCENT_INFO));
        items.Add(new TimelineItemViewModel(
            "Next desktop milestone",
            "Live session hosting",
            "The shell is ready to ingest preflight status and stored run data while live run execution moves out of the console host.",
            ACCENT_PRIMARY));
    }

    /// <summary>
    /// Rebuilds the timeline from a selected run and its discovered artifacts.
    /// </summary>
    /// <param name="items">The timeline collection to populate.</param>
    /// <param name="run">The selected run summary.</param>
    /// <param name="artifacts">The artifacts discovered for the run.</param>
    public static void Rebuild(
        ObservableCollection<TimelineItemViewModel> items,
        RunSummaryViewModel run,
        IReadOnlyList<ArtifactItemViewModel> artifacts)
    {
        items.Clear();
        items.Add(new TimelineItemViewModel(
            run.Title,
            "Selected session",
            run.RunDirectory,
            ACCENT_PRIMARY));
        items.Add(new TimelineItemViewModel(
            "Artefacts indexed",
            $"{artifacts.Count} files discovered",
            artifacts.Count == 0 ? "No top-level files were found for this run." : string.Join(", ", artifacts.Take(6).Select(a => a.Name)),
            ACCENT_INFO));
        items.Add(new TimelineItemViewModel(
            "Desktop adaptation",
            "Reference-inspired layout",
            "The left rail, timeline surface, and detail pane now map to ArchHarness runs, live status, and artefacts instead of terminal screens.",
            ACCENT_SUCCESS));
    }

    /// <summary>
    /// Appends a single timeline item to the collection.
    /// </summary>
    /// <param name="items">The timeline collection to append to.</param>
    /// <param name="title">The item title.</param>
    /// <param name="subtitle">The item subtitle.</param>
    /// <param name="detail">The item detail text.</param>
    /// <param name="accent">The accent color hex string.</param>
    public static void Append(
        ObservableCollection<TimelineItemViewModel> items,
        string title,
        string subtitle,
        string detail,
        string accent)
    {
        items.Add(new TimelineItemViewModel(title, subtitle, detail, accent));
    }

    /// <summary>
    /// Returns the accent color hex string for a given event source identifier.
    /// </summary>
    /// <param name="source">The event source name.</param>
    /// <returns>A hex color string appropriate for the source.</returns>
    public static string AccentForSource(string source)
        => source.ToLowerInvariant() switch
        {
            "orchestrator" => ACCENT_PRIMARY,
            "build" => ACCENT_INFO,
            "security" => ACCENT_DANGER,
            "architecture" => ACCENT_SUCCESS,
            "codingstyle" => ACCENT_STYLE,
            _ => ACCENT_MUTED
        };
}
