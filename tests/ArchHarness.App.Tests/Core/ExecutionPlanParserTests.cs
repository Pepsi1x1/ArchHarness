using ArchHarness.App.Core;

namespace ArchHarness.App.Tests.Core;

public sealed class ExecutionPlanParserTests
{
    private readonly ExecutionPlanParser _parser = new ExecutionPlanParser(new WorkspaceContextAnalyzer());

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

            bool result = _parser.TryBuildExecutionPlan(json, workspaceRoot, out ExecutionPlan plan, out string? error);

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
            bool result = _parser.TryBuildExecutionPlan(json, workspaceRoot, out _, out string? error);

            Assert.False(result);
            Assert.NotNull(error);
            Assert.Contains(expectedErrorToken, error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CleanupTempWorkspace(workspaceRoot);
        }
    }

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

            bool result = _parser.TryBuildExecutionPlan(json, workspaceRoot, out _, out string? error);

            Assert.False(result);
            Assert.NotNull(error);
            Assert.Contains("dependsOn", error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CleanupTempWorkspace(workspaceRoot);
        }
    }

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

        List<ExecutionPlanStep> ordered = _parser.NormalizeStepOrdering(steps, languages);

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

        List<ExecutionPlanStep> ordered = _parser.NormalizeStepOrdering(steps, languages);

        ExecutionPlanStep archStep = ordered.Last(s => s.Agent == "Architecture");
        ExecutionPlanStep securityStep = ordered.Last(s => s.Agent == "Security");

        Assert.NotNull(archStep.DependsOnStepIds);
        Assert.Contains(securityStep.Id, archStep.DependsOnStepIds);
    }

    [Fact]
    public void TryBuildExecutionPlan_NoJsonInResponse_ReturnsFailure()
    {
        string workspaceRoot = CreateTempWorkspace();
        try
        {
            bool result = _parser.TryBuildExecutionPlan("No JSON here at all", workspaceRoot, out _, out string? error);

            Assert.False(result);
            Assert.NotNull(error);
            Assert.Contains("No JSON", error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CleanupTempWorkspace(workspaceRoot);
        }
    }

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

            bool result = _parser.TryBuildExecutionPlan(raw, workspaceRoot, out ExecutionPlan plan, out string? error);

            Assert.True(result, $"Expected success but got error: {error}");
            Assert.NotNull(plan);
        }
        finally
        {
            CleanupTempWorkspace(workspaceRoot);
        }
    }

    private static string CreateTempWorkspace()
    {
        string path = Path.Combine(Path.GetTempPath(), "ArchHarness.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "App.csproj"), "<Project/>");
        return path;
    }

    private static void CleanupTempWorkspace(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

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

            bool result = _parser.TryBuildExecutionPlan(json, workspaceRoot, out _, out string? error);

            Assert.False(result);
            Assert.NotNull(error);
            Assert.Contains("not recognized", error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CleanupTempWorkspace(workspaceRoot);
        }
    }

    [Fact]
    public void TryBuildExecutionPlan_MissingSecurityStep_ReturnsFailure()
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

            bool result = _parser.TryBuildExecutionPlan(json, workspaceRoot, out _, out string? error);

            Assert.False(result);
            Assert.NotNull(error);
            Assert.Contains("Security", error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CleanupTempWorkspace(workspaceRoot);
        }
    }
}
