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
    public WikiDocRunServices(IWikiDocWorkflow workflow, WikiDocResumeStateBuilder resumeStateBuilder, WikiDocRepositoryDiscoverer discoverer, WikiDocOutputResolver resolver)
    {
        this.Workflow = workflow;
        this.ResumeStateBuilder = resumeStateBuilder;
        this.Discoverer = discoverer;
        this.Resolver = resolver;
    }

    /// <summary>Gets the wikidoc workflow.</summary>
    public IWikiDocWorkflow Workflow { get; }

    /// <summary>Gets the resume state builder for reconstructing prior run progress.</summary>
    public WikiDocResumeStateBuilder ResumeStateBuilder { get; }

    /// <summary>Gets the repository discoverer.</summary>
    public WikiDocRepositoryDiscoverer Discoverer { get; }

    /// <summary>Gets the output resolver.</summary>
    public WikiDocOutputResolver Resolver { get; }
}
