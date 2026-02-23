using ArchHarness.App.Core;

namespace ArchHarness.App.Tui;

public sealed class ChatTerminal
{
    private readonly OrchestratorRuntime _runtime;

    public ChatTerminal(OrchestratorRuntime runtime)
    {
        _runtime = runtime;
    }

    public async Task RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        Console.WriteLine("ArchHarness vNext (.NET 10)");
        Console.WriteLine("Screens: Chat/Setup, Run Monitor, Logs, Artefacts, Review Viewer");

        var request = BuildRequest(args);
        Console.WriteLine($"[Chat/Setup] task='{request.TaskPrompt}' mode='{request.WorkspaceMode}' path='{request.WorkspacePath}'");
        var artefacts = await _runtime.RunAsync(request, cancellationToken);

        Console.WriteLine($"[Run Monitor] RunId: {artefacts.RunId}");
        Console.WriteLine($"[Artefacts] {artefacts.RunDirectory}");
        Console.WriteLine($"[Logs] {Path.Combine(artefacts.RunDirectory, "events.jsonl")}");
        Console.WriteLine($"[Review Viewer] {Path.Combine(artefacts.RunDirectory, "ArchitectureReview.json")}");
    }

    private static RunRequest BuildRequest(string[] args)
    {
        if (args.Length >= 4 && args[0].Equals("run", StringComparison.OrdinalIgnoreCase))
        {
            return new RunRequest(
                TaskPrompt: args[1],
                WorkspacePath: args[2],
                WorkspaceMode: args[3],
                Workflow: args.Length >= 5 ? args[4] : "frontend_feature",
                ProjectName: args.Length >= 6 ? args[5] : null,
                ModelOverrides: null);
        }

        Console.Write("Task> ");
        var task = Console.ReadLine() ?? "Implement requested change";
        Console.Write("Workspace path> ");
        var path = Console.ReadLine() ?? Directory.GetCurrentDirectory();
        Console.Write("Workspace mode (new-project|existing-folder|existing-git)> ");
        var mode = Console.ReadLine() ?? "existing-folder";
        string? projectName = null;
        if (mode == "new-project")
        {
            Console.Write("Project name> ");
            projectName = Console.ReadLine();
        }

        return new RunRequest(task, path, mode, "frontend_feature", projectName, null);
    }
}
