using ArchHarness.App.Core;
using ArchHarness.App.Storage;

namespace ArchHarness.App.Tests.Storage;

public sealed class PlanningSessionRecorderTests : IDisposable
{
    private readonly string _workspaceRoot;

    public PlanningSessionRecorderTests()
    {
        this._workspaceRoot = Path.Combine(Path.GetTempPath(), "ah-planning-recorder-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(this._workspaceRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(this._workspaceRoot))
            {
                Directory.Delete(this._workspaceRoot, recursive: true);
            }
        }
        catch
        {
            // best effort
        }
    }

    private static PlanningSessionRecorder CreateRecorder() => new PlanningSessionRecorder(new PlanningSessionStore());

    [Fact]
    public async Task EnsureAsync_CreatesEmptySession_WhenAbsent()
    {
        PlanningSessionRecorder recorder = CreateRecorder();

        PlanningSession session = await recorder.EnsureAsync(this._workspaceRoot, "session-1", "run-1", CancellationToken.None);

        Assert.Equal("session-1", session.Id);
        Assert.Equal("run-1", session.PlanningRunId);
        Assert.Empty(session.Messages);
        Assert.Null(session.ImplementationRunId);
    }

    [Fact]
    public async Task EnsureAsync_ReturnsExistingSession_WhenPresent()
    {
        PlanningSessionRecorder recorder = CreateRecorder();
        PlanningSession first = await recorder.EnsureAsync(this._workspaceRoot, "session-1", "run-1", CancellationToken.None);
        await recorder.AppendMessageAsync(
            this._workspaceRoot, "session-1", ConversationRoles.USER, ConversationMessageKinds.CHAT, "hello", cancellationToken: CancellationToken.None);

        PlanningSession again = await recorder.EnsureAsync(this._workspaceRoot, "session-1", "run-1", CancellationToken.None);

        Assert.Equal(first.Id, again.Id);
        Assert.Single(again.Messages);
    }

    [Fact]
    public async Task AppendMessageAsync_PersistsAttachmentsAndMetadata()
    {
        PlanningSessionRecorder recorder = CreateRecorder();
        await recorder.EnsureAsync(this._workspaceRoot, "session-a", "run-1", CancellationToken.None);

        PromptAttachment attachment = new PromptAttachment(
            Id: "att-1",
            Kind: PromptAttachmentKinds.IMAGE,
            MimeType: "image/png",
            FileName: "mock.png",
            SizeBytes: 512,
            DataBase64: "AAA=",
            Caption: "mock screenshot");

        PlanningSession? updated = await recorder.AppendMessageAsync(
            this._workspaceRoot,
            "session-a",
            ConversationRoles.USER,
            ConversationMessageKinds.FOLLOW_UP,
            "please address this",
            new[] { attachment },
            relatedRunId: "impl-1",
            cancellationToken: CancellationToken.None);

        Assert.NotNull(updated);
        ConversationMessage message = Assert.Single(updated!.Messages);
        Assert.Equal(ConversationMessageKinds.FOLLOW_UP, message.Kind);
        Assert.Equal("please address this", message.Text);
        Assert.Equal("impl-1", message.RelatedRunId);
        PromptAttachment persisted = Assert.Single(message.Attachments);
        Assert.Equal("att-1", persisted.Id);
        Assert.Equal("mock.png", persisted.FileName);
        Assert.Equal("mock screenshot", persisted.Caption);
    }

    [Fact]
    public async Task AppendMessageAsync_ReturnsNull_WhenSessionMissing()
    {
        PlanningSessionRecorder recorder = CreateRecorder();

        PlanningSession? result = await recorder.AppendMessageAsync(
            this._workspaceRoot,
            "does-not-exist",
            ConversationRoles.USER,
            ConversationMessageKinds.CHAT,
            "hi",
            cancellationToken: CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task LinkImplementationRunAsync_SetsImplementationRunId()
    {
        PlanningSessionRecorder recorder = CreateRecorder();
        await recorder.EnsureAsync(this._workspaceRoot, "session-b", "run-plan", CancellationToken.None);

        PlanningSession? linked = await recorder.LinkImplementationRunAsync(this._workspaceRoot, "session-b", "impl-99", CancellationToken.None);

        Assert.NotNull(linked);
        Assert.Equal("impl-99", linked!.ImplementationRunId);
    }

    [Fact]
    public async Task MessagesSurviveReopenViaStore()
    {
        PlanningSessionRecorder recorder = CreateRecorder();
        await recorder.EnsureAsync(this._workspaceRoot, "session-c", "run-plan", CancellationToken.None);
        await recorder.AppendMessageAsync(
            this._workspaceRoot, "session-c", ConversationRoles.USER, ConversationMessageKinds.CHAT, "first", cancellationToken: CancellationToken.None);
        await recorder.AppendMessageAsync(
            this._workspaceRoot, "session-c", ConversationRoles.ASSISTANT, ConversationMessageKinds.HANDOFF, "handed off", authorAgent: "Orchestrator", cancellationToken: CancellationToken.None);

        PlanningSessionStore store = new PlanningSessionStore();
        PlanningSession? loaded = store.Get(this._workspaceRoot, "session-c");

        Assert.NotNull(loaded);
        Assert.Equal(2, loaded!.Messages.Count);
        Assert.Equal(ConversationMessageKinds.CHAT, loaded.Messages[0].Kind);
        Assert.Equal(ConversationMessageKinds.HANDOFF, loaded.Messages[1].Kind);
        Assert.Equal("Orchestrator", loaded.Messages[1].AuthorAgent);
    }
}
