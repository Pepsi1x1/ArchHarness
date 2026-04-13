using ArchHarness.App.Copilot;
using ArchHarness.App.Core;
using ArchHarness.App.Tui;
using Microsoft.Extensions.Options;

namespace ArchHarness.App.Tests.Tui;

public sealed class ChatTerminalTests
{
    [Fact]
    public async Task RunAsync_NonInteractiveWikiDoc_DisablesLiveMonitor()
    {
        RecordingChatTerminalRunController runController = new RecordingChatTerminalRunController();
        RecordingScreenNavigator screenNavigator = new RecordingScreenNavigator();
        ChatTerminal terminal = new ChatTerminal(
            CreateConversationController(),
            runController,
            screenNavigator,
            new StubPreflightValidator());

        await terminal.RunAsync(new[] { "wikidoc", @"C:\workspace\scan-root" }, CancellationToken.None);

        Assert.False(runController.EnableLiveMonitor);
        Assert.False(screenNavigator.WasShown);
    }

    private static ConversationController CreateConversationController()
        => new(
            new SetupSummaryGenerator(new StubCopilotClient(), new StubModelResolver()),
            Options.Create(new AgentsOptions()),
            new StubModelResolver(),
            new RuntimeStateAccessors(
                new PermissionHandlerModeAccessor(),
                new ReviewLoopAgentSelectionAccessor(),
                new AgentExecutionContextAccessor(),
                new WorkspaceRootAccessor()),
            new NullSetupStatusSink());

    private sealed class RecordingChatTerminalRunController : IChatTerminalRunController
    {
        public bool EnableLiveMonitor { get; private set; } = true;

        public Task<ChatTerminalRunResult?> ExecuteAsync(RunRequest request, bool enableLiveMonitor, CancellationToken cancellationToken)
        {
            _ = request;
            _ = cancellationToken;
            this.EnableLiveMonitor = enableLiveMonitor;
            return Task.FromResult<ChatTerminalRunResult?>(new ChatTerminalRunResult(
                new RunArtefacts("run-1", @"C:\workspace\.agent-harness\runs\run-1"),
                new List<RuntimeProgressEvent>()));
        }
    }

    private sealed class RecordingScreenNavigator : IChatTerminalScreenNavigator
    {
        public bool WasShown { get; private set; }

        public Task ShowAsync(RunRequest request, string setupSummary, RunArtefacts artefacts, List<RuntimeProgressEvent> runEvents, CancellationToken cancellationToken)
        {
            _ = request;
            _ = setupSummary;
            _ = artefacts;
            _ = runEvents;
            _ = cancellationToken;
            this.WasShown = true;
            return Task.CompletedTask;
        }
    }

    private sealed class StubPreflightValidator : IStartupPreflightValidator
    {
        public Task<PreflightValidationResult> ValidateAsync(CancellationToken cancellationToken = default)
        {
            _ = cancellationToken;
            return Task.FromResult(new PreflightValidationResult(true, "ok", Array.Empty<string>()));
        }
    }

    private sealed class StubCopilotClient : ICopilotClient
    {
        public Task<string> CompleteAsync(string model, string prompt, CopilotCompletionOptions? options = null, string? agentId = null, string? agentRole = null, CancellationToken cancellationToken = default)
        {
            _ = model;
            _ = options;
            _ = agentId;
            _ = agentRole;
            _ = cancellationToken;
            return Task.FromResult(prompt.Contains("Generate a concise run title", StringComparison.Ordinal)
                ? "WikiDoc Run"
                : "- Summary");
        }

        public IReadOnlyList<CopilotModelUsage> GetUsageSnapshot()
            => Array.Empty<CopilotModelUsage>();
    }

    private sealed class StubModelResolver : IModelResolver
    {
        public IReadOnlyCollection<string> GetSupportedModels()
            => new[] { "test-model" };

        public string Resolve(string role, IDictionary<string, string>? overrides)
        {
            _ = role;
            _ = overrides;
            return "test-model";
        }

        public string? ResolveReasoningEffort(string role)
        {
            _ = role;
            return null;
        }

        public void ValidateConfiguredModelsOrThrow(IDictionary<string, string>? overrides = null)
        {
            _ = overrides;
        }

        public void ValidateOrThrow(string model)
        {
            _ = model;
        }
    }
}
