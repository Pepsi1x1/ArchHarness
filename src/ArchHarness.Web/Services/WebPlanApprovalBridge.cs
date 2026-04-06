using ArchHarness.App.Core;

namespace ArchHarness.Web.Services;

/// <summary>
/// Web host implementation of <see cref="IPlanApprovalBridge"/> that delegates to the <see cref="WebInteractionCoordinator"/>.
/// </summary>
public sealed class WebPlanApprovalBridge : IPlanApprovalBridge
{
    private readonly WebInteractionCoordinator _coordinator;

    public WebPlanApprovalBridge(WebInteractionCoordinator coordinator)
    {
        this._coordinator = coordinator;
    }

    /// <inheritdoc />
    public async Task<PlanApprovalResponse> RequestApprovalAsync(
        PlanApprovalRequest request,
        CancellationToken cancellationToken)
    {
        return await this._coordinator.RequestPlanApprovalAsync(request).ConfigureAwait(false);
    }
}
