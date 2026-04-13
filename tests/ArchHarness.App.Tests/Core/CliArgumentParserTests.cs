using ArchHarness.App.Core;

namespace ArchHarness.App.Tests.Core;

public sealed class CliArgumentParserTests
{
    [Fact]
    public void TryParseCliArgs_WikiDocCommandBuildsNonInteractiveRequest()
    {
        AgentsOptions agentsOptions = new AgentsOptions();

        RunRequest? request = CliArgumentParser.TryParseCliArgs(
            new[]
            {
                "wikidoc",
                @"C:\repos\scan-root",
                "Docs Workspace",
                "backend-developer=gpt-5.4"
            },
            agentsOptions);

        Assert.NotNull(request);
        Assert.Equal(DefaultPrompts.WIKIDOC_TASK, request!.TaskPrompt);
        Assert.Equal(@"C:\repos\scan-root", request.WorkspacePath);
        Assert.Equal(WorkspaceModes.EXISTING_FOLDER, request.WorkspaceMode);
        Assert.Equal(WorkflowNames.WIKIDOC, request.Workflow);
        Assert.Equal("Docs Workspace", request.ProjectName);
        KeyValuePair<string, string> overrideEntry = Assert.Single(request.ModelOverrides!);
        Assert.Equal("backend-developer", overrideEntry.Key);
        Assert.Equal("gpt-5.4", overrideEntry.Value);
    }

    [Fact]
    public void IsNonInteractiveCommand_WikiDocReturnsTrue()
    {
        Assert.True(CliArgumentParser.IsNonInteractiveCommand(new[] { "wikidoc", @"C:\repos\scan-root" }));
        Assert.False(CliArgumentParser.IsNonInteractiveCommand(new[] { "run", "task", @"C:\repos\scan-root", "existing-folder" }));
    }
}
