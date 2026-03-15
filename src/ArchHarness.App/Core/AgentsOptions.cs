namespace ArchHarness.App.Core;

/// <summary>
/// Configuration options for a single agent role, including model selection and tool policies.
/// </summary>
public sealed class AgentModelOptions
{
    /// <summary>Gets or sets the model identifier for this agent role.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Gets or sets the tool policy options for this agent role.</summary>
    public AgentToolOptions Tools { get; set; } = new AgentToolOptions();

    /// <summary>Gets or sets whether guideline loading is disabled for this agent role.</summary>
    public bool DisableGuidelines { get; set; }

    /// <summary>Gets or sets whether architecture loop mode is enabled.</summary>
    public bool ArchitectureLoopMode { get; set; }

    /// <summary>Gets or sets the optional architecture loop prompt.</summary>
    public string? ArchitectureLoopPrompt { get; set; }

    /// <summary>Gets or sets whether this agent participates in the review loop when applicable.</summary>
    public bool UseInReviewLoop { get; set; } = true;

    /// <summary>Gets or sets the architecture analyzer toggles for this agent role.</summary>
    public ArchitectureAnalyzerOptions ArchitectureAnalyzers { get; set; } = new ArchitectureAnalyzerOptions();

    /// <summary>Gets or sets the security analyzer toggles for this agent role.</summary>
    public SecurityAnalyzerOptions SecurityAnalyzers { get; set; } = new SecurityAnalyzerOptions();
}

/// <summary>
/// Toggle set for the architecture review heuristics and analyzers.
/// </summary>
public sealed class ArchitectureAnalyzerOptions
{
    /// <summary>Gets or sets whether unfinished implementation markers should be reported.</summary>
    public bool CompletenessTodo { get; set; } = true;

    /// <summary>Gets or sets whether SRP analysis is enabled.</summary>
    public bool Srp { get; set; } = true;

    /// <summary>Gets or sets whether DIP analysis is enabled.</summary>
    public bool Dip { get; set; } = true;

    /// <summary>Gets or sets whether ISP analysis is enabled.</summary>
    public bool Isp { get; set; } = true;

    /// <summary>Gets or sets whether OCP/LSP analysis is enabled.</summary>
    public bool OcpLsp { get; set; } = true;

    /// <summary>Gets or sets whether DRY analysis is enabled.</summary>
    public bool Dry { get; set; } = true;

    /// <summary>Gets or sets whether missing-test findings are enabled.</summary>
    public bool MissingTests { get; set; } = true;
}

/// <summary>
/// Toggle set for the security review heuristics.
/// </summary>
public sealed class SecurityAnalyzerOptions
{
    /// <summary>Gets or sets whether hardcoded secret detection is enabled.</summary>
    public bool HardcodedSecrets { get; set; } = true;

    /// <summary>Gets or sets whether insecure transport detection is enabled.</summary>
    public bool InsecureTransport { get; set; } = true;

    /// <summary>Gets or sets whether SQL injection detection is enabled.</summary>
    public bool SqlInjection { get; set; } = true;

    /// <summary>Gets or sets whether XSS detection is enabled.</summary>
    public bool Xss { get; set; } = true;

    /// <summary>Gets or sets whether insecure TLS bypass detection is enabled.</summary>
    public bool InsecureTlsBypass { get; set; } = true;
}

/// <summary>
/// Effective enablement settings for review-loop agents.
/// </summary>
public sealed record ReviewLoopAgentSelection(bool CodingStyleEnabled, bool SecurityEnabled, bool ArchitectureEnabled)
{
    /// <summary>Gets whether any review-loop agent is enabled.</summary>
    public bool AnyEnabled => this.CodingStyleEnabled || this.SecurityEnabled || this.ArchitectureEnabled;

    /// <summary>Gets whether any finding-producing review agent is enabled.</summary>
    public bool AnyFindingReviewEnabled => this.SecurityEnabled || this.ArchitectureEnabled;

    /// <summary>Returns true when the specified agent is enabled for review-loop participation.</summary>
    public bool IsEnabled(string agentName)
        => agentName switch
        {
            "CodingStyle" => this.CodingStyleEnabled,
            "Security" => this.SecurityEnabled,
            "Architecture" => this.ArchitectureEnabled,
            _ => true
        };

    /// <summary>Builds a human-readable label describing enabled review-loop agents.</summary>
    public string DescribeEnabledAgents()
    {
        string[] enabled = this.GetEnabledAgentNames().ToArray();
        return enabled.Length == 0 ? "none" : string.Join(", ", enabled);
    }

    /// <summary>Builds a human-readable label describing disabled review-loop agents.</summary>
    public string DescribeDisabledAgents()
    {
        string[] disabled = this.GetDisabledAgentNames().ToArray();
        return disabled.Length == 0 ? "none" : string.Join(", ", disabled);
    }

    /// <summary>Returns completion criteria labels for enabled review-loop agents plus build verification.</summary>
    public IReadOnlyList<string> BuildCompletionCriteria()
    {
        List<string> criteria = new List<string>();
        if (this.CodingStyleEnabled)
        {
            criteria.Add("Coding style enforcement completed");
        }

        if (this.SecurityEnabled)
        {
            criteria.Add("No high severity security findings");
        }

        if (this.ArchitectureEnabled)
        {
            criteria.Add("No high severity architecture findings");
        }

        criteria.Add("Build passes");
        return criteria;
    }

    private IEnumerable<string> GetEnabledAgentNames()
    {
        if (this.CodingStyleEnabled)
        {
            yield return "CodingStyle";
        }

        if (this.SecurityEnabled)
        {
            yield return "Security";
        }

        if (this.ArchitectureEnabled)
        {
            yield return "Architecture";
        }
    }

    private IEnumerable<string> GetDisabledAgentNames()
    {
        if (!this.CodingStyleEnabled)
        {
            yield return "CodingStyle";
        }

        if (!this.SecurityEnabled)
        {
            yield return "Security";
        }

        if (!this.ArchitectureEnabled)
        {
            yield return "Architecture";
        }
    }
}

/// <summary>
/// Root configuration for all agent roles, providing per-role model and tool options.
/// </summary>
public sealed class AgentsOptions
{
    /// <summary>Gets or sets the orchestration agent options.</summary>
    public AgentModelOptions Orchestration { get; set; } = new AgentModelOptions() { Model = "claude-sonnet-4.6" };

    /// <summary>Gets or sets the frontend developer agent options.</summary>
    public AgentModelOptions FrontendDeveloper { get; set; } = new AgentModelOptions() { Model = "claude-sonnet-4.6" };

    /// <summary>Gets or sets the backend developer agent options.</summary>
    public AgentModelOptions BackendDeveloper { get; set; } = new AgentModelOptions() { Model = "gpt-5.3-codex" };

    /// <summary>Gets or sets the build agent options.</summary>
    public AgentModelOptions Build { get; set; } = new AgentModelOptions() { Model = "gpt-4.1" };

    /// <summary>Gets or sets the coding style agent options.</summary>
    public AgentModelOptions CodingStyle { get; set; } = new AgentModelOptions() { Model = "claude-opus-4.6" };

    /// <summary>Gets or sets the security agent options.</summary>
    public AgentModelOptions Security { get; set; } = new AgentModelOptions() { Model = "claude-opus-4.6" };

    /// <summary>Gets or sets the architecture agent options.</summary>
    public AgentModelOptions Architecture { get; set; } = new AgentModelOptions() { Model = "claude-opus-4.6" };

    /// <summary>
    /// Returns the agent model options for the specified role.
    /// </summary>
    /// <param name="role">The agent role identifier.</param>
    /// <returns>The matching agent model options.</returns>
    public AgentModelOptions ForRole(string role) => role.ToLowerInvariant() switch
    {
        "frontend-developer" => this.FrontendDeveloper,
        "backend-developer" => this.BackendDeveloper,
        "build" => this.Build,
        "coding-style" => this.CodingStyle,
        "security" => this.Security,
        "architecture" => this.Architecture,
        "orchestration" => this.Orchestration,
        _ => new AgentModelOptions()
    };

    /// <summary>
    /// Gets the effective review-loop agent enablement derived from per-role configuration.
    /// </summary>
    public ReviewLoopAgentSelection GetReviewLoopAgentSelection()
        => new ReviewLoopAgentSelection(
            CodingStyleEnabled: this.CodingStyle.UseInReviewLoop,
            SecurityEnabled: this.Security.UseInReviewLoop,
            ArchitectureEnabled: this.Architecture.UseInReviewLoop);
}
