namespace ArchHarness.Desktop.ViewModels;

public sealed class SessionEventItemViewModel : ViewModelBase
{
    public SessionEventItemViewModel(string title, string subtitle, string model, string sessionId, string detail)
    {
        this.Title = title;
        this.Subtitle = subtitle;
        this.Model = model;
        this.SessionId = sessionId;
        this.Detail = detail;
    }

    public string Title { get; }

    public string Subtitle { get; }

    public string Model { get; }

    public string SessionId { get; }

    public string Detail { get; }

    public string SessionLabel => $"{this.Model} • {this.SessionId}";
}