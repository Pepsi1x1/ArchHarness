namespace ArchHarness.App.Constants;

/// <summary>
/// Well-known agent role names used in execution plan steps and dispatching.
/// </summary>
public static class AgentNames
{
    /// <summary>The frontend developer agent name.</summary>
    public const string FRONTEND_DEVELOPER = "FrontendDeveloper";

    /// <summary>The backend developer agent name.</summary>
    public const string BACKEND_DEVELOPER = "BackendDeveloper";

    /// <summary>The build verification agent name.</summary>
    public const string BUILD = "Build";

    /// <summary>The coding style enforcement agent name.</summary>
    public const string CODING_STYLE = "CodingStyle";

    /// <summary>The security review agent name.</summary>
    public const string SECURITY = "Security";

    /// <summary>The architecture review agent name.</summary>
    public const string ARCHITECTURE = "Architecture";
}
