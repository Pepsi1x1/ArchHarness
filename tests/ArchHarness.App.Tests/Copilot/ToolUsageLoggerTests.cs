using System.Text.Json;
using ArchHarness.App.Copilot;
using ArchHarness.App.Core;
using ArchHarness.App.Storage;
using GitHub.Copilot.SDK;

namespace ArchHarness.App.Tests.Copilot;

public sealed class ToolUsageLoggerTests
{
    [Fact]
    public async Task LogPostToolUseAsync_DerivedFailureInput_SnapshotsBoundedFieldsIncludingError()
    {
        RunContextAccessor runContextAccessor = new RunContextAccessor();
        runContextAccessor.SetCurrent(new RunContext("run-1", "c:\\fake-run"));
        FakeArtefactStore artefactStore = new FakeArtefactStore();
        ToolUsageLogger logger = new ToolUsageLogger(
            runContextAccessor,
            artefactStore,
            new AgentStreamEventStream(),
            new AgentExecutionContextAccessor());

        FailurePostToolUseHookInput input = new FailurePostToolUseHookInput
        {
            Timestamp = 123,
            Cwd = "c:\\repo",
            ToolName = "web_fetch",
            ToolArgs = new { url = "https://example.test/missing", max_length = 8000 },
            Error = "Error: Failed to fetch - status code 404"
        };

        await logger.LogPostToolUseAsync(input);

        Assert.Single(artefactStore.AppendedEvents);
        string json = JsonSerializer.Serialize(artefactStore.AppendedEvents[0]);
        Assert.Contains("\"toolName\":\"web_fetch\"", json);
        Assert.Contains("\"stage\":\"post\"", json);
        Assert.Contains("status code 404", json);
        Assert.Contains("https://example.test/missing", json);
    }

    private sealed class FailurePostToolUseHookInput : PostToolUseHookInput
    {
        public string? Error { get; set; }
    }

    private sealed class FakeArtefactStore : IArtefactStore
    {
        public List<object> AppendedEvents { get; } = new List<object>();

        public Task WriteExecutionPlanAsync(string runDirectory, ExecutionPlan plan, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task WriteArchitectureReviewAsync(string runDirectory, ArchitectureReview review, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task WriteSecurityReviewAsync(string runDirectory, SecurityReview review, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task WriteFinalSummaryAsync(string runDirectory, string summary, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task WriteBuildResultAsync(string runDirectory, object payload, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task WriteCompletionValidationAsync(string runDirectory, CompletionValidationResult validation, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task AppendEventAsync(string runDirectory, object evt, CancellationToken cancellationToken)
        {
            this.AppendedEvents.Add(evt);
            return Task.CompletedTask;
        }

        public Task AppendSdkEventAsync(string runDirectory, object evt, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task WriteClarificationSpecAsync(string runDirectory, ClarificationSpec spec, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task WritePlanApprovalAsync(string runDirectory, PlanApproval approval, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task CompleteRunAsync(string runDirectory, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
