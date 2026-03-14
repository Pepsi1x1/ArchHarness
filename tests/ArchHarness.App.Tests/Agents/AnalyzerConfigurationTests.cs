using ArchHarness.App.Agents;
using ArchHarness.App.Core;
using ArchHarness.App.Tests.TestHelpers;

namespace ArchHarness.App.Tests.Agents;

/// <summary>
/// Verifies that analyzer toggles enable and disable the expected review heuristics.
/// </summary>
public sealed class AnalyzerConfigurationTests
{
    [Fact]
    public void Analyze_WhenArchitectureAnalyzersDisabled_SuppressesFindings()
    {
        string workspace = TempWorkspaceHelper.CreateTempWorkspace();
        try
        {
            string filePath = Path.Combine(workspace, "Sample.cs");
            File.WriteAllText(filePath, """
                public class Sample
                {
                    public void Run()
                    {
                        // TODO: finish implementation
                    }
                }
                """);

            ArchitectureReview review = AnalysisRunner.Analyze(
                diff: "TODO in Sample.cs",
                workspaceRoot: workspace,
                filesTouched: new[] { filePath },
                analyzerOptions: new ArchitectureAnalyzerOptions
                {
                    CompletenessTodo = false,
                    Srp = false,
                    Dip = false,
                    Isp = false,
                    OcpLsp = false,
                    Dry = false,
                    MissingTests = false
                });

            Assert.Empty(review.Findings);
            Assert.Empty(review.RequiredActions);
        }
        finally
        {
            TempWorkspaceHelper.CleanupTempWorkspace(workspace);
        }
    }

    [Fact]
    public void Analyze_WhenTodoAnalyzerEnabled_ReportsCompletenessFinding()
    {
        string workspace = TempWorkspaceHelper.CreateTempWorkspace();
        try
        {
            string filePath = Path.Combine(workspace, "Sample.cs");
            File.WriteAllText(filePath, "public class Sample { }\n");

            ArchitectureReview review = AnalysisRunner.Analyze(
                diff: "TODO in Sample.cs",
                workspaceRoot: workspace,
                filesTouched: new[] { filePath },
                analyzerOptions: new ArchitectureAnalyzerOptions
                {
                    CompletenessTodo = true,
                    Srp = false,
                    Dip = false,
                    Isp = false,
                    OcpLsp = false,
                    Dry = false,
                    MissingTests = false
                });

            ArchitectureFinding finding = Assert.Single(review.Findings);
            Assert.Equal("Completeness", finding.Rule);
        }
        finally
        {
            TempWorkspaceHelper.CleanupTempWorkspace(workspace);
        }
    }

    [Fact]
    public void Analyze_WhenSecurityAnalyzersDisabled_SuppressesFindings()
    {
        string workspace = TempWorkspaceHelper.CreateTempWorkspace();
        try
        {
            string filePath = Path.Combine(workspace, "SecuritySample.cs");
            File.WriteAllText(filePath, """
                public static class SecuritySample
                {
                    public const string Password = "secret-value";
                    public const string Endpoint = "http://example.com";
                }
                """);

            SecurityReview review = SecurityAnalysisRunner.Analyze(
                diff: string.Empty,
                workspaceRoot: workspace,
                filesTouched: new[] { filePath },
                languageScope: null,
                analyzerOptions: new SecurityAnalyzerOptions
                {
                    HardcodedSecrets = false,
                    InsecureTransport = false,
                    SqlInjection = false,
                    Xss = false,
                    InsecureTlsBypass = false
                });

            Assert.Empty(review.Findings);
            Assert.Empty(review.RequiredActions);
        }
        finally
        {
            TempWorkspaceHelper.CleanupTempWorkspace(workspace);
        }
    }

    [Fact]
    public void Analyze_WhenOnlyHardcodedSecretsEnabled_ReportsOnlyThatFinding()
    {
        string workspace = TempWorkspaceHelper.CreateTempWorkspace();
        try
        {
            string filePath = Path.Combine(workspace, "SecuritySample.cs");
            File.WriteAllText(filePath, """
                public static class SecuritySample
                {
                    public const string Password = "secret-value";
                    public const string Endpoint = "http://example.com";
                }
                """);

            SecurityReview review = SecurityAnalysisRunner.Analyze(
                diff: string.Empty,
                workspaceRoot: workspace,
                filesTouched: new[] { filePath },
                languageScope: null,
                analyzerOptions: new SecurityAnalyzerOptions
                {
                    HardcodedSecrets = true,
                    InsecureTransport = false,
                    SqlInjection = false,
                    Xss = false,
                    InsecureTlsBypass = false
                });

            SecurityFinding finding = Assert.Single(review.Findings);
            Assert.Equal("HardcodedSecrets", finding.Rule);
        }
        finally
        {
            TempWorkspaceHelper.CleanupTempWorkspace(workspace);
        }
    }
}