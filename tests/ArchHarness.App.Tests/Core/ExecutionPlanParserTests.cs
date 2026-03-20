using ArchHarness.App.Core;
using ArchHarness.App.Tests.TestHelpers;
using Microsoft.Extensions.Options;

namespace ArchHarness.App.Tests.Core;

/// <summary>
/// Verifies execution plan parsing, validation, and step ordering normalization.
/// </summary>
public sealed class ExecutionPlanParserTests
{
    private readonly ExecutionPlanParser _parser = new ExecutionPlanParser(new WorkspaceContextAnalyzer());

    /// <summary>
    /// Valid JSON should parse into a well-formed execution plan.
    /// </summary>
    [Fact]
    public void TryBuildExecutionPlan_ValidJson_ReturnsCorrectPlan()
    {
        string workspaceRoot = CreateTempWorkspace();
        try
        {
            string json = """
                {
                    "steps": [
                        {"id":1,"agent":"BackendDeveloper","objective":"Implement feature X"},
                        {"id":2,"agent":"CodingStyle","objective":"Review and enforce coding style"},
                        {"id":3,"agent":"Security","objective":"Review and enforce security","dependsOn":[2]},
                        {"id":4,"agent":"Architecture","objective":"Review and enforce architecture","dependsOn":[3]}
                    ],
                    "iterationStrategy": {"maxIterations": 3, "reviewRequired": true},
                    "completionCriteria": ["No high severity coding style findings","No high severity security findings","No high severity architecture findings","Build passes"]
                }
                """;

            bool result = this._parser.TryBuildExecutionPlan(json, workspaceRoot, out ExecutionPlan plan, out string? error);

            Assert.True(result, $"Expected success but got error: {error}");
            Assert.Null(error);
            Assert.NotNull(plan);
            Assert.True(plan.Steps.Count >= 3);
            Assert.Equal(3, plan.IterationStrategy.MaxIterations);
            Assert.True(plan.IterationStrategy.ReviewRequired);
            Assert.True(plan.CompletionCriteria.Count >= 3);
        }
        finally
        {
            CleanupTempWorkspace(workspaceRoot);
        }
    }

    /// <summary>
    /// Missing required fields should cause a parse failure with a descriptive error.
    /// </summary>
    [Theory]
    [InlineData("""{"iterationStrategy":{},"completionCriteria":["ok"]}""", "steps")]
    [InlineData("""{"steps":[],"iterationStrategy":{},"completionCriteria":["ok"]}""", "empty")]
    [InlineData("""{"steps":[{"id":1,"agent":"BackendDeveloper","objective":"build"}],"completionCriteria":["ok"]}""", "iterationStrategy")]
    [InlineData("""{"steps":[{"id":1,"agent":"BackendDeveloper","objective":"build"}],"iterationStrategy":{}}""", "completionCriteria")]
    public void TryBuildExecutionPlan_MissingRequiredFields_ReturnsFailure(string json, string expectedErrorToken)
    {
        string workspaceRoot = CreateTempWorkspace();
        try
        {
            bool result = this._parser.TryBuildExecutionPlan(json, workspaceRoot, out _, out string? error);

            Assert.False(result);
            Assert.NotNull(error);
            Assert.Contains(expectedErrorToken, error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CleanupTempWorkspace(workspaceRoot);
        }
    }

    /// <summary>
    /// Invalid dependency IDs should cause a parse failure.
    /// </summary>
    [Fact]
    public void TryBuildExecutionPlan_InvalidDependencyIds_ReturnsFailure()
    {
        string workspaceRoot = CreateTempWorkspace();
        try
        {
            string json = """
                {
                    "steps": [
                        {"id":1,"agent":"BackendDeveloper","objective":"build things"},
                        {"id":2,"agent":"CodingStyle","objective":"review coding style","dependsOn":[0]},
                        {"id":3,"agent":"Security","objective":"review security"},
                        {"id":4,"agent":"Architecture","objective":"review architecture"}
                    ],
                    "iterationStrategy": {"maxIterations": 2, "reviewRequired": true},
                    "completionCriteria": ["Build passes"]
                }
                """;

            bool result = this._parser.TryBuildExecutionPlan(json, workspaceRoot, out _, out string? error);

            Assert.False(result);
            Assert.NotNull(error);
            Assert.Contains("dependsOn", error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CleanupTempWorkspace(workspaceRoot);
        }
    }

    /// <summary>
    /// Invalid agent errors should list the full supported set, including accepted aliases.
    /// </summary>
    [Fact]
    public void TryBuildExecutionPlan_InvalidAgent_ListsSupportedAliasesInError()
    {
        string workspaceRoot = CreateTempWorkspace();
        try
        {
            string json = """
                {
                    "steps": [
                        {"id":1,"agent":"unknown-agent","objective":"build things"}
                    ],
                    "iterationStrategy": {"maxIterations": 2, "reviewRequired": true},
                    "completionCriteria": ["Build passes"]
                }
                """;

            bool result = this._parser.TryBuildExecutionPlan(json, workspaceRoot, out _, out string? error);

            Assert.False(result);
            Assert.NotNull(error);
            Assert.Contains("secure", error, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("review", error, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("frontend-developer", error, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("backend-developer", error, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("coding-style", error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CleanupTempWorkspace(workspaceRoot);
        }
    }

    /// <summary>
    /// Step ordering should place CodingStyle before Security before Architecture.
    /// </summary>
    [Fact]
    public void NormalizeStepOrdering_ProducesCodingStyleBeforeArchitecture()
    {
        List<ExecutionPlanStep> steps = new List<ExecutionPlanStep>
        {
            new ExecutionPlanStep(1, "BackendDeveloper", "Implement feature"),
            new ExecutionPlanStep(2, "CodingStyle", "Review and enforce coding style conventions"),
            new ExecutionPlanStep(3, "Security", "Review and enforce security patterns"),
            new ExecutionPlanStep(4, "Architecture", "Review and enforce architecture patterns")
        };
        IReadOnlyList<string> languages = new[] { "dotnet" };

        List<ExecutionPlanStep> ordered = this._parser.NormalizeStepOrdering(steps, languages);

        int codingStyleIndex = ordered.FindIndex(s => s.Agent == "CodingStyle");
        int securityIndex = ordered.FindIndex(s => s.Agent == "Security");
        int archIndex = ordered.FindIndex(s => s.Agent == "Architecture");

        Assert.True(codingStyleIndex >= 0, "CodingStyle step must be present");
        Assert.True(securityIndex >= 0, "Security step must be present");
        Assert.True(archIndex >= 0, "Architecture step must be present");
        Assert.True(codingStyleIndex < securityIndex, "CodingStyle must come before Security");
        Assert.True(codingStyleIndex < archIndex, "CodingStyle must come before Architecture");
        Assert.True(securityIndex < archIndex, "Security must come before Architecture");
    }

    /// <summary>
    /// Architecture step should depend on the Security step after normalization.
    /// </summary>
    [Fact]
    public void NormalizeStepOrdering_ArchitectureDependsOnSecurity()
    {
        List<ExecutionPlanStep> steps = new List<ExecutionPlanStep>
        {
            new ExecutionPlanStep(1, "BackendDeveloper", "Build the project"),
            new ExecutionPlanStep(2, "CodingStyle", "Enforce coding style rules"),
            new ExecutionPlanStep(3, "Security", "Enforce security rules"),
            new ExecutionPlanStep(4, "Architecture", "Enforce architecture rules")
        };
        IReadOnlyList<string> languages = new[] { "dotnet" };

        List<ExecutionPlanStep> ordered = this._parser.NormalizeStepOrdering(steps, languages);

        ExecutionPlanStep archStep = ordered.Last(s => s.Agent == "Architecture");
        ExecutionPlanStep securityStep = ordered.Last(s => s.Agent == "Security");

        Assert.NotNull(archStep.DependsOnStepIds);
        Assert.Contains(securityStep.Id, archStep.DependsOnStepIds);
    }

    /// <summary>
    /// Input containing no JSON should return a parse failure.
    /// </summary>
    [Fact]
    public void TryBuildExecutionPlan_NoJsonInResponse_ReturnsFailure()
    {
        string workspaceRoot = CreateTempWorkspace();
        try
        {
            bool result = this._parser.TryBuildExecutionPlan("No JSON here at all", workspaceRoot, out _, out string? error);

            Assert.False(result);
            Assert.NotNull(error);
            Assert.Contains("No JSON", error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CleanupTempWorkspace(workspaceRoot);
        }
    }

    /// <summary>
    /// JSON wrapped in a markdown fence should be extracted and parsed successfully.
    /// </summary>
    [Fact]
    public void TryBuildExecutionPlan_JsonInMarkdownFence_ParsesSuccessfully()
    {
        string workspaceRoot = CreateTempWorkspace();
        try
        {
            string raw = """
                Here is the plan:
                ```json
                {
                    "steps": [
                        {"id":1,"agent":"BackendDeveloper","objective":"Build feature"},
                        {"id":2,"agent":"CodingStyle","objective":"Review coding style enforcement"},
                        {"id":3,"agent":"Security","objective":"Review security enforcement"},
                        {"id":4,"agent":"Architecture","objective":"Review architecture enforcement"}
                    ],
                    "iterationStrategy": {"maxIterations": 2, "reviewRequired": true},
                    "completionCriteria": ["Coding style clean","Security clean","Architecture clean","Build passes"]
                }
                ```
                """;

            bool result = this._parser.TryBuildExecutionPlan(raw, workspaceRoot, out ExecutionPlan plan, out string? error);

            Assert.True(result, $"Expected success but got error: {error}");
            Assert.NotNull(plan);
        }
        finally
        {
            CleanupTempWorkspace(workspaceRoot);
        }
    }

    /// <summary>
    /// Truncated JSON that is only missing closing brackets/braces should be repaired and parsed.
    /// </summary>
    [Fact]
    public void TryBuildExecutionPlan_TruncatedJsonWithMissingClosers_ParsesSuccessfully()
    {
        string workspaceRoot = CreateTempWorkspace();
        try
        {
            string raw = """
                {
                    "steps": [
                        {"id":1,"agent":"BackendDeveloper","objective":"Build feature"},
                        {"id":2,"agent":"CodingStyle","objective":"Review coding style enforcement"},
                        {"id":3,"agent":"Security","objective":"Review security enforcement"},
                        {"id":4,"agent":"Architecture","objective":"Review architecture enforcement"}
                    ],
                    "iterationStrategy": {"maxIterations": 2, "reviewRequired": true},
                    "completionCriteria": ["Coding style clean", "Security clean", "Architecture clean", "Build passes"]
                """;

            bool result = this._parser.TryBuildExecutionPlan(raw, workspaceRoot, out ExecutionPlan plan, out string? error);

            Assert.True(result, $"Expected success but got error: {error}");
            Assert.NotNull(plan);
            Assert.Equal(2, plan.IterationStrategy.MaxIterations);
        }
        finally
        {
            CleanupTempWorkspace(workspaceRoot);
        }
    }

    /// <summary>
    /// A dangling trailing property after a valid plan should be trimmed back to the last valid JSON boundary.
    /// </summary>
    [Fact]
    public void TryBuildExecutionPlan_TruncatedJsonWithDanglingProperty_ParsesSuccessfully()
    {
        string workspaceRoot = CreateTempWorkspace();
        try
        {
            string raw = """
                {
                    "steps": [
                        {"id":1,"agent":"BackendDeveloper","objective":"Build feature"},
                        {"id":2,"agent":"CodingStyle","objective":"Review coding style enforcement"},
                        {"id":3,"agent":"Security","objective":"Review security enforcement"},
                        {"id":4,"agent":"Architecture","objective":"Review architecture enforcement"}
                    ],
                    "iterationStrategy": {"maxIterations": 2, "reviewRequired": true},
                    "completionCriteria": ["Coding style clean", "Security clean", "Architecture clean", "Build passes"],
                    "notes":
                """;

            bool result = this._parser.TryBuildExecutionPlan(raw, workspaceRoot, out ExecutionPlan plan, out string? error);

            Assert.True(result, $"Expected success but got error: {error}");
            Assert.NotNull(plan);
            Assert.True(plan.CompletionCriteria.Count >= 4);
        }
        finally
        {
            CleanupTempWorkspace(workspaceRoot);
        }
    }

    private static string CreateTempWorkspace()
    {
        string path = TempWorkspaceHelper.CreateTempWorkspace();
        File.WriteAllText(Path.Combine(path, "App.csproj"), "<Project/>");
        return path;
    }

    private static void CleanupTempWorkspace(string path)
        => TempWorkspaceHelper.CleanupTempWorkspace(path);

    /// <summary>
    /// A legacy agent name should cause a parse failure.
    /// </summary>
    [Fact]
    public void TryBuildExecutionPlan_LegacyStyleAgentName_ReturnsFailure()
    {
        string workspaceRoot = CreateTempWorkspace();
        try
        {
            string json = """
                {
                    "steps": [
                        {"id":1,"agent":"BackendDeveloper","objective":"Implement feature X"},
                        {"id":2,"agent":"Style","objective":"Review and enforce style"},
                        {"id":3,"agent":"Security","objective":"Review and enforce security","dependsOn":[2]},
                        {"id":4,"agent":"Architecture","objective":"Review and enforce architecture","dependsOn":[3]}
                    ],
                    "iterationStrategy": {"maxIterations": 3, "reviewRequired": true},
                    "completionCriteria": ["No high severity coding style findings","No high severity security findings","No high severity architecture findings","Build passes"]
                }
                """;

            bool result = this._parser.TryBuildExecutionPlan(json, workspaceRoot, out _, out string? error);

            Assert.False(result);
            Assert.NotNull(error);
            Assert.Contains("not recognized", error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CleanupTempWorkspace(workspaceRoot);
        }
    }

    /// <summary>
    /// A plan missing the Security step should have one automatically inserted.
    /// </summary>
    [Fact]
    public void TryBuildExecutionPlan_MissingSecurityStep_InsertsDefaultReviewStep()
    {
        string workspaceRoot = CreateTempWorkspace();
        try
        {
            string json = """
                {
                    "steps": [
                        {"id":1,"agent":"BackendDeveloper","objective":"Implement feature X"},
                        {"id":2,"agent":"CodingStyle","objective":"Review and enforce coding style"},
                        {"id":3,"agent":"Architecture","objective":"Review and enforce architecture","dependsOn":[2]}
                    ],
                    "iterationStrategy": {"maxIterations": 3, "reviewRequired": true},
                    "completionCriteria": ["No high severity coding style findings","No high severity architecture findings","Build passes"]
                }
                """;

            bool result = this._parser.TryBuildExecutionPlan(json, workspaceRoot, out ExecutionPlan plan, out string? error);

            Assert.True(result, $"Expected success but got error: {error}");
            Assert.Null(error);
            Assert.Contains(plan.Steps, step => step.Agent == "Security");
            Assert.Equal("Security", plan.Steps[^2].Agent);
            Assert.Equal("Architecture", plan.Steps[^1].Agent);
        }
        finally
        {
            CleanupTempWorkspace(workspaceRoot);
        }
    }

    /// <summary>
    /// A Build-only plan should have review steps auto-injected.
    /// </summary>
    [Fact]
    public void TryBuildExecutionPlan_BuildStep_ReturnsCorrectPlan()
    {
        string workspaceRoot = CreateTempWorkspace();
        try
        {
            string json = """
                {
                    "steps": [
                        {"id":1,"agent":"Build","objective":"Run the solution build and summarize failures"}
                    ],
                    "iterationStrategy": {"maxIterations": 1, "reviewRequired": true},
                    "completionCriteria": ["Build status summarized"]
                }
                """;

            bool result = this._parser.TryBuildExecutionPlan(json, workspaceRoot, out ExecutionPlan plan, out string? error);

            Assert.True(result, $"Expected success but got error: {error}");
            Assert.Null(error);
            Assert.Equal("Build", plan.Steps[0].Agent);
            Assert.Contains(plan.Steps, step => step.Agent == "CodingStyle");
            Assert.Contains(plan.Steps, step => step.Agent == "Security");
            Assert.Contains(plan.Steps, step => step.Agent == "Architecture");
        }
        finally
        {
            CleanupTempWorkspace(workspaceRoot);
        }
    }

    /// <summary>
    /// A trailing Build step should run after the full review chain.
    /// </summary>
    [Fact]
    public void TryBuildExecutionPlan_FinalValidationBuildStep_RunsAfterReviewChain()
    {
        string workspaceRoot = CreateTempWorkspace();
        try
        {
            string json = """
                {
                    "steps": [
                        {"id":1,"agent":"Build","objective":"Run a baseline build and record warnings/errors"},
                        {"id":2,"agent":"CodingStyle","objective":"Review and enforce coding style","dependsOn":[1],"languages":["dotnet"]},
                        {"id":3,"agent":"Security","objective":"Review and enforce security","dependsOn":[2],"languages":["dotnet"]},
                        {"id":4,"agent":"Architecture","objective":"Review and enforce architecture","dependsOn":[3],"languages":["dotnet"]},
                        {"id":5,"agent":"Build","objective":"Run a final validation build of the solution. Confirm the build succeeds with zero errors. Confirm all remediation applied in prior steps has not broken the build.","dependsOn":[4]}
                    ],
                    "iterationStrategy": {"maxIterations": 2, "reviewRequired": true},
                    "completionCriteria": ["No high severity coding style findings","No high severity security findings","No high severity architecture findings","Build passes"]
                }
                """;

            bool result = this._parser.TryBuildExecutionPlan(json, workspaceRoot, out ExecutionPlan plan, out string? error);

            Assert.True(result, $"Expected success but got error: {error}");
            Assert.Null(error);
            Assert.Equal(new[] { "Build", "CodingStyle", "Security", "Architecture", "Build" }, plan.Steps.Select(step => step.Agent).ToArray());
            Assert.Equal(new[] { 1 }, plan.Steps[1].DependsOnStepIds);
            Assert.Equal(new[] { 2 }, plan.Steps[2].DependsOnStepIds);
            Assert.Equal(new[] { 3 }, plan.Steps[3].DependsOnStepIds);
            Assert.Equal(new[] { 4 }, plan.Steps[4].DependsOnStepIds);
        }
        finally
        {
            CleanupTempWorkspace(workspaceRoot);
        }
    }

    /// <summary>
    /// Disabled review agents should not be auto-injected into a build-only plan.
    /// </summary>
    [Fact]
    public void TryBuildExecutionPlan_DisabledSecurityAndArchitecture_DoesNotInjectThem()
    {
        string workspaceRoot = CreateTempWorkspace();
        try
        {
            ExecutionPlanParser parser = CreateParser(options =>
            {
                options.Security.UseInReviewLoop = false;
                options.Architecture.UseInReviewLoop = false;
            });

            string json = """
                {
                    "steps": [
                        {"id":1,"agent":"Build","objective":"Run the solution build and summarize failures"}
                    ],
                    "iterationStrategy": {"maxIterations": 1, "reviewRequired": true},
                    "completionCriteria": ["Build status summarized"]
                }
                """;

            bool result = parser.TryBuildExecutionPlan(json, workspaceRoot, out ExecutionPlan plan, out string? error);

            Assert.True(result, $"Expected success but got error: {error}");
            Assert.Equal(new[] { "Build", "CodingStyle" }, plan.Steps.Select(step => step.Agent).ToArray());
            Assert.False(plan.IterationStrategy.ReviewRequired);
        }
        finally
        {
            CleanupTempWorkspace(workspaceRoot);
        }
    }

    /// <summary>
    /// Explicit disabled review steps should be removed during normalization.
    /// </summary>
    [Fact]
    public void TryBuildExecutionPlan_DisabledSecurity_RemovesExplicitSecurityStep()
    {
        string workspaceRoot = CreateTempWorkspace();
        try
        {
            ExecutionPlanParser parser = CreateParser(options => options.Security.UseInReviewLoop = false);

            string json = """
                {
                    "steps": [
                        {"id":1,"agent":"BackendDeveloper","objective":"Implement feature X"},
                        {"id":2,"agent":"CodingStyle","objective":"Review and enforce coding style"},
                        {"id":3,"agent":"Security","objective":"Review and enforce security"},
                        {"id":4,"agent":"Architecture","objective":"Review and enforce architecture"}
                    ],
                    "iterationStrategy": {"maxIterations": 2, "reviewRequired": true},
                    "completionCriteria": ["Build passes"]
                }
                """;

            bool result = parser.TryBuildExecutionPlan(json, workspaceRoot, out ExecutionPlan plan, out string? error);

            Assert.True(result, $"Expected success but got error: {error}");
            Assert.DoesNotContain(plan.Steps, step => step.Agent == "Security");
            Assert.Contains(plan.Steps, step => step.Agent == "Architecture");
        }
        finally
        {
            CleanupTempWorkspace(workspaceRoot);
        }
    }

    private static ExecutionPlanParser CreateParser(Action<AgentsOptions> configure)
    {
        AgentsOptions options = new AgentsOptions();
        configure(options);
        return new ExecutionPlanParser(new WorkspaceContextAnalyzer(), Options.Create(options));
    }
}
