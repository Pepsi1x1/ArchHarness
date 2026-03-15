using System.Text.Json;
using System.Text.Json.Serialization;
using ArchHarness.App;
using ArchHarness.App.Core;
using ArchHarness.App.Copilot;
using ArchHarness.App.Storage;
using ArchHarness.Web.Services;
using Markdig;
using Microsoft.Extensions.Options;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
JsonSerializerOptions eventJsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
	DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};
const string DEFAULT_TASK_PROMPT = "Implement requested change";
const string DEFAULT_ARCH_LOOP_TASK_PROMPT = "Run coding style, security, and architecture review loop for the existing workspace and apply required remediation.";
MarkdownPipeline markdownPipeline = new MarkdownPipelineBuilder()
	.UseAdvancedExtensions()
	.DisableHtml()
	.Build();

string? webHostUrl = builder.Configuration["webHost:url"];
if (!string.IsNullOrWhiteSpace(webHostUrl))
{
	builder.WebHost.UseUrls(webHostUrl);
}

builder.Services.AddArchHarnessRuntimeServices(builder.Configuration);
builder.Services.AddArchHarnessInteractiveServices();
builder.Services.AddSingleton<WebInteractionCoordinator>();
builder.Services.AddSingleton<ICopilotUserInputBridge, WebCopilotUserInputBridge>();
builder.Services.AddSingleton<ICopilotPermissionPromptHandler, WebPermissionPromptHandler>();
builder.Services.AddSingleton<IWebRunSessionManager, WebRunSessionManager>();

WebApplication app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api", () => Results.Ok(new
{
	name = "ArchHarness.Web",
	mode = "local-only",
	status = "ready"
}));

app.MapGet("/api/bootstrap", (IOptions<AgentsOptions> agentsOptions, IWebRunSessionManager sessionManager) =>
{
	AgentsOptions config = agentsOptions.Value;
	ReviewLoopAgentSelection reviewLoopSelection = config.GetReviewLoopAgentSelection();
	return Results.Ok(new
	{
		workspacePath = Environment.CurrentDirectory,
		defaultTaskPrompt = config.Architecture.ArchitectureLoopMode ? DEFAULT_ARCH_LOOP_TASK_PROMPT : DEFAULT_TASK_PROMPT,
		workspaceModes = new[] { "existing-folder", "new-project", "existing-git" },
		permissionModes = new[] { "approve-all", "prompt" },
		workflow = config.Architecture.ArchitectureLoopMode ? "architecture-loop" : "auto",
		architectureLoopMode = config.Architecture.ArchitectureLoopMode,
		architectureLoopPrompt = config.Architecture.ArchitectureLoopPrompt,
		reviewLoopAgents = reviewLoopSelection,
		activeRun = sessionManager.GetSnapshot()
	});
});

app.MapGet("/api/health", () => Results.Ok(new { healthy = true }));

app.MapGet("/api/preflight", async (IStartupPreflightValidator validator, CancellationToken cancellationToken) =>
{
	PreflightValidationResult result = await validator.ValidateAsync(cancellationToken);
	return Results.Ok(result);
});

app.MapPost("/api/setup-summary", async (RunRequest request, SetupSummaryGenerator generator, CancellationToken cancellationToken) =>
{
	string summary = await generator.GenerateSetupSummaryAsync(request, cancellationToken);
	return Results.Ok(new { summary });
});

app.MapPost("/api/markdown/render", (MarkdownRenderRequest request) =>
{
	string html = Markdown.ToHtml(request.Markdown ?? string.Empty, markdownPipeline);
	return Results.Ok(new { html });
});

app.MapGet("/api/runs", (string workspacePath, int? maxCount, IRunHistoryCatalog catalog) =>
{
	if (string.IsNullOrWhiteSpace(workspacePath))
	{
		return Results.BadRequest(new { error = "workspacePath is required." });
	}

	return Results.Ok(catalog.GetRecentRuns(workspacePath, Math.Max(1, maxCount ?? 20)));
});

app.MapGet("/api/runs/{runId}/artifacts", (string runId, string workspacePath, int? previewLength, IRunHistoryCatalog catalog) =>
{
	if (string.IsNullOrWhiteSpace(workspacePath))
	{
		return Results.BadRequest(new { error = "workspacePath is required." });
	}

	if (!IsSafeRunId(runId))
	{
		return Results.BadRequest(new { error = "runId must be a single directory name." });
	}

	string runDirectory = Path.Combine(Path.GetFullPath(workspacePath), ".agent-harness", "runs", runId);
	return Results.Ok(catalog.GetArtifacts(runDirectory, Math.Max(32, previewLength ?? 2400)));
});

app.MapPost("/api/runs", async (RunRequest request, IWebRunSessionManager sessionManager, CancellationToken cancellationToken) =>
{
	WebRunSnapshot snapshot = await sessionManager.StartRunAsync(request, cancellationToken);
	return Results.Accepted("/api/runs/active", snapshot);
});

app.MapDelete("/api/runs/active", async (IWebRunSessionManager sessionManager) =>
{
	WebRunSnapshot snapshot = await sessionManager.CancelRunAsync();
	return Results.Ok(snapshot);
});

app.MapGet("/api/runs/active", (IWebRunSessionManager sessionManager) => Results.Ok(sessionManager.GetSnapshot()));

app.MapGet("/api/runs/active/events", async (HttpContext context, IWebRunSessionManager sessionManager, CancellationToken cancellationToken) =>
{
	context.Response.Headers.CacheControl = "no-cache";
	context.Response.Headers.Connection = "keep-alive";
	context.Response.ContentType = "text/event-stream";

	await foreach (WebRunEvent evt in sessionManager.ReadEventsAsync(cancellationToken))
	{
		string payload = JsonSerializer.Serialize(evt, eventJsonOptions);
		await context.Response.WriteAsync($"event: {evt.Kind}\n", cancellationToken);
		await context.Response.WriteAsync($"data: {payload}\n\n", cancellationToken);
		await context.Response.Body.FlushAsync(cancellationToken);
	}

	return Results.Empty;
});

app.MapGet("/api/interactions/pending", (WebInteractionCoordinator interactions) =>
{
	PendingInteractionSnapshot? pending = interactions.GetPending();
	return pending is null ? Results.NoContent() : Results.Ok(pending);
});

app.MapPost("/api/interactions/user-input", (UserInputSubmission submission, WebInteractionCoordinator interactions) =>
{
	if (!interactions.TrySubmitUserInput(submission.Answer))
	{
		return Results.Conflict(new { error = "No pending user-input request is active." });
	}

	return Results.Accepted();
});

app.MapPost("/api/interactions/permission", (PermissionSubmission submission, WebInteractionCoordinator interactions) =>
{
	if (!interactions.TrySubmitPermission(submission.Approved))
	{
		return Results.Conflict(new { error = "No pending permission request is active." });
	}

	return Results.Accepted();
});

await app.RunAsync();

static bool IsSafeRunId(string runId)
	=> !string.IsNullOrWhiteSpace(runId)
		&& runId.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }) < 0;

public partial class Program
{
	private Program()
	{
	}
}