namespace ArchHarness.App.Core;

/// <summary>
/// Groups run-lifecycle infrastructure services used during orchestrated runs:
/// artifact writing, event logging, and run context tracking.
/// </summary>
public sealed class RunInfrastructure
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RunInfrastructure"/> class.
    /// </summary>
    /// <param name="artifactWriter">Writes run artifacts to disk.</param>
    /// <param name="eventLogger">Logs structured events during a run.</param>
    /// <param name="runContextAccessor">Tracks the currently active run context.</param>
    public RunInfrastructure(
        IRunArtifactWriter artifactWriter,
        IRunEventLogger eventLogger,
        IRunContextAccessor runContextAccessor)
    {
        this.ArtifactWriter = artifactWriter;
        this.EventLogger = eventLogger;
        this.RunContextAccessor = runContextAccessor;
    }

    /// <summary>
    /// Gets the artifact writer used to persist run outputs.
    /// </summary>
    public IRunArtifactWriter ArtifactWriter { get; }

    /// <summary>
    /// Gets the event logger used to record structured run events.
    /// </summary>
    public IRunEventLogger EventLogger { get; }

    /// <summary>
    /// Gets the run context accessor used to track active run state.
    /// </summary>
    public IRunContextAccessor RunContextAccessor { get; }
}
