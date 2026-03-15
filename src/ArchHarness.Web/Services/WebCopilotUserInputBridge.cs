using ArchHarness.App.Copilot;
using GitHub.Copilot.SDK;

namespace ArchHarness.Web.Services;

/// <summary>
/// Web-host implementation of <see cref="ICopilotUserInputBridge"/>.
/// </summary>
public sealed class WebCopilotUserInputBridge : ICopilotUserInputBridge
{
    private readonly WebInteractionCoordinator _coordinator;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebCopilotUserInputBridge"/> class.
    /// </summary>
    /// <param name="coordinator">The interaction coordinator.</param>
    public WebCopilotUserInputBridge(WebInteractionCoordinator coordinator)
    {
        this._coordinator = coordinator;
    }

    /// <inheritdoc />
    public Task<UserInputResponse> RequestInputAsync(UserInputRequest request)
        => this._coordinator.RequestUserInputAsync(request);
}