namespace ArchHarness.App.Constants;

/// <summary>
/// Well-known event source identifiers used across orchestration components.
/// </summary>
public static class WellKnownSources
{
    /// <summary>The event source identifier for the orchestrator.</summary>
    public const string ORCHESTRATOR = "orchestrator";

    /// <summary>The event source identifier for the architecture review loop.</summary>
    public const string ARCHITECTURE_LOOP = "architecture-loop";
}
