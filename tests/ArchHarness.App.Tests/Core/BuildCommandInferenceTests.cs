using ArchHarness.App.Core;
using ArchHarness.App.Tests.TestHelpers;

namespace ArchHarness.App.Tests.Core;

/// <summary>
/// Verifies build command inference and target discovery logic.
/// </summary>
public sealed class BuildCommandInferenceTests
{
    /// <summary>
    /// A user command without a target should have the discovered target injected.
    /// </summary>
    [Fact]
    public void Select_InjectsTarget_WhenUserCommandHasNoTarget()
    {
        string root = CreateTempWorkspace();
        try
        {
            string slnPath = Path.Combine(root, "solution", "App.sln");
            Directory.CreateDirectory(Path.GetDirectoryName(slnPath)!);
            File.WriteAllText(slnPath, string.Empty);

            BuildCommandSelection selection = BuildCommandInference.Select(root, "dotnet build -c Release", "existing-folder", null);

            Assert.NotNull(selection.Command);
            Assert.Contains("dotnet build", selection.Command!, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(slnPath, selection.Command!, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("-c Release", selection.Command!, StringComparison.OrdinalIgnoreCase);
            Assert.True(selection.Inferred);
        }
        finally
        {
            CleanupTempWorkspace(root);
        }
    }

    /// <summary>
    /// When no command is provided, the non-test csproj should be auto-discovered.
    /// </summary>
    [Fact]
    public void Select_AutoDiscoversCsproj_WhenNoCommandProvided()
    {
        string root = CreateTempWorkspace();
        try
        {
            string appProj = Path.Combine(root, "src", "MyApp", "MyApp.csproj");
            string testProj = Path.Combine(root, "tests", "MyApp.Tests", "MyApp.Tests.csproj");
            Directory.CreateDirectory(Path.GetDirectoryName(appProj)!);
            Directory.CreateDirectory(Path.GetDirectoryName(testProj)!);
            File.WriteAllText(appProj, "<Project/>");
            File.WriteAllText(testProj, "<Project/>");

            BuildCommandSelection selection = BuildCommandInference.Select(root, null, "existing-folder", null);

            Assert.NotNull(selection.Command);
            Assert.Contains(appProj, selection.Command!, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(testProj, selection.Command!, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("--nologo", selection.Command!, StringComparison.OrdinalIgnoreCase);
            Assert.True(selection.Inferred);
        }
        finally
        {
            CleanupTempWorkspace(root);
        }
    }

    /// <summary>
    /// A new project with no build targets should fall back to a generic build command.
    /// </summary>
    [Fact]
    public void Select_NewProjectFallback_WhenNoTargetsExist()
    {
        string root = CreateTempWorkspace();
        try
        {
            BuildCommandSelection selection = BuildCommandInference.Select(root, null, "new-project", "DemoApp");

            Assert.Equal("dotnet build --nologo", selection.Command);
            Assert.True(selection.Inferred);
        }
        finally
        {
            CleanupTempWorkspace(root);
        }
    }

    /// <summary>
    /// A user command that already specifies a target should not be modified.
    /// </summary>
    [Fact]
    public void Select_LeavesUserTargetedCommandUntouched()
    {
        string root = CreateTempWorkspace();
        try
        {
            string command = "dotnet build \"./src/MyApp/MyApp.csproj\" -c Release";
            BuildCommandSelection selection = BuildCommandInference.Select(root, command, "existing-folder", null);

            Assert.Equal(command, selection.Command);
            Assert.False(selection.Inferred);
        }
        finally
        {
            CleanupTempWorkspace(root);
        }
    }

    private static string CreateTempWorkspace()
        => TempWorkspaceHelper.CreateTempWorkspace();

    private static void CleanupTempWorkspace(string path)
        => TempWorkspaceHelper.CleanupTempWorkspace(path);
}
