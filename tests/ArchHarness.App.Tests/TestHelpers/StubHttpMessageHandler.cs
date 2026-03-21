namespace ArchHarness.App.Tests.TestHelpers;

/// <summary>
/// Provides deterministic HTTP responses for unit and integration tests.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _responseFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="StubHttpMessageHandler"/> class.
    /// </summary>
    /// <param name="responseFactory">Builds a response for each request.</param>
    public StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responseFactory)
    {
        this._responseFactory = responseFactory;
    }

    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(this._responseFactory(request, cancellationToken));
}
