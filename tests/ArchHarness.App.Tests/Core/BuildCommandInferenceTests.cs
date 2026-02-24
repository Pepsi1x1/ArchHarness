using ArchHarness.App.Core;

namespace ArchHarness.App.Tests.Core;

public sealed class BuildCommandInferenceTests
{
    [Fact]
    public void Select_InjectsTarget_WhenUserCommandHasNoTarget()
    {
        var root = CreateTempWorkspace();
        try
        {
            var slnPath = Path.Combine(root, "solution", "App.sln");
            Directory.CreateDirectory(Path.GetDirectoryName(slnPath)!);
            File.WriteAllText(slnPath, string.Empty);

            var selection = BuildCommandInference.Select(root, "dotnet build -c Release", "existing-folder", null);

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

    [Fact]
    public void Select_AutoDiscoversCsproj_WhenNoCommandProvided()
    {
        var root = CreateTempWorkspace();
        try
        {
            var appProj = Path.Combine(root, "src", "MyApp", "MyApp.csproj");
            var testProj = Path.Combine(root, "tests", "MyApp.Tests", "MyApp.Tests.csproj");
            Directory.CreateDirectory(Path.GetDirectoryName(appProj)!);
            Directory.CreateDirectory(Path.GetDirectoryName(testProj)!);
            File.WriteAllText(appProj, "<Project/>");
            File.WriteAllText(testProj, "<Project/>");

            var selection = BuildCommandInference.Select(root, null, "existing-folder", null);

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

    [Fact]
    public void Select_NewProjectFallback_WhenNoTargetsExist()
    {
        var root = CreateTempWorkspace();
        try
        {
            var selection = BuildCommandInference.Select(root, null, "new-project", "DemoApp");

            Assert.Equal("dotnet build --nologo", selection.Command);
            Assert.True(selection.Inferred);
        }
        finally
        {
            CleanupTempWorkspace(root);
        }
    }

    [Fact]
    public void Select_LeavesUserTargetedCommandUntouched()
    {
        var root = CreateTempWorkspace();
        try
        {
            var command = "dotnet build \"./src/MyApp/MyApp.csproj\" -c Release";
            var selection = BuildCommandInference.Select(root, command, "existing-folder", null);

            Assert.Equal(command, selection.Command);
            Assert.False(selection.Inferred);
        }
        finally
        {
            CleanupTempWorkspace(root);
        }
    }

    private static string CreateTempWorkspace()
    {
        var path = Path.Combine(Path.GetTempPath(), "ArchHarness.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void CleanupTempWorkspace(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}