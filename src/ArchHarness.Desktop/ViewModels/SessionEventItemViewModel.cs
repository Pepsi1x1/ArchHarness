namespace ArchHarness.Desktop.ViewModels;

/// <summary>
/// Represents a single Copilot session lifecycle event for display in the desktop UI.
/// </summary>
public sealed class SessionEventItemViewModel : ViewModelBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SessionEventItemViewModel"/> class.
    /// </summary>
    /// <param name="title">The event type title.</param>
    /// <param name="subtitle">A secondary label such as the formatted timestamp.</param>
    /// <param name="model">The model identifier associated with the session.</param>
    /// <param name="sessionId">The unique session identifier.</param>
    /// <param name="detail">Additional descriptive detail for the event.</param>
    public SessionEventItemViewModel(string title, string subtitle, string model, string sessionId, string detail)
    {
        this.Title = title;
        this.Subtitle = subtitle;
        this.Model = model;
        this.SessionId = sessionId;
        this.Detail = detail;
    }

    /// <summary>Gets the event type title.</summary>
    public string Title { get; }

    /// <summary>Gets the secondary label such as the formatted timestamp.</summary>
    public string Subtitle { get; }

    /// <summary>Gets the model identifier associated with the session.</summary>
    public string Model { get; }

    /// <summary>Gets the unique session identifier.</summary>
    public string SessionId { get; }

    /// <summary>Gets the additional descriptive detail for the event.</summary>
    public string Detail { get; }

    /// <summary>Gets a composite label combining the model and session identifier.</summary>
    public string SessionLabel => $"{this.Model} • {this.SessionId}";
}