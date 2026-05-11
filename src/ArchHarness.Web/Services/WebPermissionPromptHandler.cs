using ArchHarness.App.Copilot;
using GitHub.Copilot.SDK;

namespace ArchHarness.Web.Services;

/// <summary>
/// Web-host implementation of <see cref="ICopilotPermissionPromptHandler"/>.
/// </summary>
public sealed class WebPermissionPromptHandler : ICopilotPermissionPromptHandler
{
    private readonly WebInteractionCoordinator _coordinator;

    /// <summary>
    /// Initializes a new instance of the <see cref="WebPermissionPromptHandler"/> class.
    /// </summary>
    /// <param name="coordinator">The interaction coordinator.</param>
    public WebPermissionPromptHandler(WebInteractionCoordinator coordinator)
    {
        this._coordinator = coordinator;
    }

    /// <inheritdoc />
    public Task<PermissionRequestResult> HandleAsync(PermissionRequest request, PermissionInvocation invocation)
        => this._coordinator.RequestPermissionAsync(request, invocation);
}
