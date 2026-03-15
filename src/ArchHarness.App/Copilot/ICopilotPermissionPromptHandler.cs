using GitHub.Copilot.SDK;

namespace ArchHarness.App.Copilot;

/// <summary>
/// Handles interactive Copilot permission requests for the active host.
/// </summary>
public interface ICopilotPermissionPromptHandler
{
    /// <summary>
    /// Requests approval or denial for a Copilot SDK permission request.
    /// </summary>
    /// <param name="request">The permission request details.</param>
    /// <param name="invocation">The invocation context for the permission request.</param>
    /// <returns>The approval result.</returns>
    Task<PermissionRequestResult> HandleAsync(PermissionRequest request, PermissionInvocation invocation);
}