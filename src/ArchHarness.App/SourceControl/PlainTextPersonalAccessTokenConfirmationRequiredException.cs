namespace ArchHarness.App.SourceControl;

/// <summary>
/// Indicates that secure token storage is unavailable and plain-text storage must be explicitly confirmed.
/// </summary>
public sealed class PlainTextPersonalAccessTokenConfirmationRequiredException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PlainTextPersonalAccessTokenConfirmationRequiredException"/> class.
    /// </summary>
    public PlainTextPersonalAccessTokenConfirmationRequiredException(string warningMessage)
        : base(warningMessage)
    {
        this.WarningMessage = warningMessage;
    }

    /// <summary>
    /// Gets the warning message that should be shown before allowing plain-text storage.
    /// </summary>
    public string WarningMessage { get; }
}