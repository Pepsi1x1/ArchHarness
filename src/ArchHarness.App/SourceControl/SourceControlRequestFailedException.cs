using System.Net;

namespace ArchHarness.App.SourceControl;

/// <summary>
/// Represents a failed upstream source-control provider request with the original HTTP status code.
/// </summary>
public sealed class SourceControlRequestFailedException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SourceControlRequestFailedException"/> class.
    /// </summary>
    public SourceControlRequestFailedException(string message, HttpStatusCode statusCode)
        : base(message)
    {
        this.StatusCode = statusCode;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SourceControlRequestFailedException"/> class with an inner exception.
    /// </summary>
    public SourceControlRequestFailedException(string message, HttpStatusCode statusCode, Exception innerException)
        : base(message, innerException)
    {
        this.StatusCode = statusCode;
    }

    /// <summary>
    /// Gets the upstream HTTP status code returned by the source-control provider.
    /// </summary>
    public HttpStatusCode StatusCode { get; }
}