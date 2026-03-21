namespace ArchHarness.App.SourceControl;

/// <summary>
/// Identifies the supported source control providers.
/// </summary>
public enum SourceControlProvider
{
    /// <summary>
    /// Azure DevOps Server (on-premises).
    /// </summary>
    AzureDevOpsServer,

    /// <summary>
    /// Azure DevOps Services (cloud-hosted).
    /// </summary>
    AzureDevOpsServices,

    /// <summary>
    /// GitHub (cloud-hosted).
    /// </summary>
    GitHub
}
