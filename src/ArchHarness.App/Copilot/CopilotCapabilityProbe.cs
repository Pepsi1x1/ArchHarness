namespace ArchHarness.App.Copilot;

/// <summary>
/// Probe that reports whether the current Copilot runtime supports multimodal prompt attachments
/// (e.g., inline image blobs). Callers that require attachment transport should consult this gate
/// and fail closed when the runtime cannot carry attachments.
/// </summary>
public interface ICopilotCapabilityProbe
{
    /// <summary>
    /// Gets a value indicating whether the runtime can transport inline image/blob attachments on
    /// a Copilot completion request.
    /// </summary>
    bool SupportsMultimodalAttachments { get; }
}

/// <summary>
/// Default capability probe that introspects the installed SDK for the expected attachment type.
/// </summary>
public sealed class CopilotCapabilityProbe : ICopilotCapabilityProbe
{
    private readonly Lazy<bool> _supportsAttachments;

    /// <summary>
    /// Initializes a new instance of the <see cref="CopilotCapabilityProbe"/> class.
    /// </summary>
    public CopilotCapabilityProbe()
    {
        this._supportsAttachments = new Lazy<bool>(ProbeSupportsAttachments);
    }

    /// <inheritdoc />
    public bool SupportsMultimodalAttachments => this._supportsAttachments.Value;

    private static bool ProbeSupportsAttachments()
    {
        try
        {
            System.Reflection.Assembly sdkAssembly = typeof(GitHub.Copilot.SDK.MessageOptions).Assembly;
            Type? blobType = sdkAssembly.GetType("GitHub.Copilot.SDK.UserMessageDataAttachmentsItemBlob");
            if (blobType is null)
            {
                return false;
            }

            return blobType.GetProperty("Data") is not null
                && blobType.GetProperty("MimeType") is not null;
        }
        catch
        {
            return false;
        }
    }
}
