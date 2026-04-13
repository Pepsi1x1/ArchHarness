namespace ArchHarness.App.Core;

/// <summary>
/// Groups wikidoc-specific workflow collaborators injected into <see cref="OrchestratedRunProcessor"/>,
/// providing a seam for future wikidoc extensions without growing the processor's constructor arity.
/// </summary>
public sealed class WikiDocRunServices
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WikiDocRunServices"/> class.
    /// </summary>
    public WikiDocRunServices(IWikiDocWorkflow workflow)
    {
        this.Workflow = workflow;
    }

    /// <summary>Gets the wikidoc workflow.</summary>
    public IWikiDocWorkflow Workflow { get; }
}
