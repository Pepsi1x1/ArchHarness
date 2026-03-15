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
builder.Services.AddSingleton<IModelMetadataProvider, ModelMetadataProvider>();

WebApplication app = builder.Build();

app.Use(async (context, next) =>
{
	context.Response.Headers["X-Content-Type-Options"] = "nosniff";
	context.Response.Headers["X-Frame-Options"] = "DENY";
	context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
	context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
	await next();
});

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api", () => Results.Ok(new
{
	name = "ArchHarness.Web",
	mode = "local-only",
	status = "ready"
}));

app.MapGet("/api/bootstrap", (IOptions<AgentsOptions> agentsOptions, IGlobalSettingsCatalog settingsCatalog, IWebRunSessionManager sessionManager) =>
{
	AgentsOptions config = agentsOptions.Value;
	PersistedGlobalSettings settings = settingsCatalog.GetSettings();
	ReviewLoopAgentSelection reviewLoopSelection = config.GetReviewLoopAgentSelection();
	return Results.Ok(new
	{
		workspacePath = Environment.CurrentDirectory,
		defaultTaskPrompt = settings.DefaultArchitectureReviewMode ? DEFAULT_ARCH_LOOP_TASK_PROMPT : DEFAULT_TASK_PROMPT,
		workspaceModes = new[] { "existing-folder", "new-project", "existing-git" },
		permissionModes = new[] { "approve-all", "prompt" },
		workflow = settings.DefaultArchitectureReviewMode ? "architecture-loop" : "auto",
		architectureLoopMode = settings.DefaultArchitectureReviewMode,
		architectureLoopPrompt = settings.DefaultArchitectureReviewPrompt,
		defaultPermissionHandlerMode = settings.DefaultPermissionHandlerMode,
		reviewLoopAgents = reviewLoopSelection,
		activeRun = sessionManager.GetSnapshot()
	});
});

app.MapGet("/api/health", () => Results.Ok(new { healthy = true }));

app.MapGet("/api/projects", (IProjectWorkspaceCatalog projectCatalog, IRunHistoryCatalog runHistoryCatalog, int? maxRunsPerProject) =>
{
	IReadOnlyList<PersistedProjectWorkspace> projects = projectCatalog.GetProjects();
	int runLimit = Math.Max(1, maxRunsPerProject ?? 20);
	return Results.Ok(projects.Select(project => new
	{
		projectId = project.ProjectId,
		displayName = project.DisplayName,
		workspacePath = project.WorkspacePath,
		workspaceMode = project.WorkspaceMode,
		permissionHandlerMode = project.PermissionHandlerMode,
		architectureReviewMode = project.ArchitectureReviewMode,
		architectureReviewPrompt = project.ArchitectureReviewPrompt,
		createdAtUtc = project.CreatedAtUtc,
		updatedAtUtc = project.UpdatedAtUtc,
		runs = runHistoryCatalog.GetRecentRuns(project.WorkspacePath, runLimit)
	}).ToArray());
});

app.MapPost("/api/projects", (CreateProjectRequest request, IProjectWorkspaceCatalog projectCatalog) =>
{
	PersistedProjectWorkspace project = projectCatalog.CreateProject(
		request.DisplayName,
		request.WorkspacePath,
		request.WorkspaceMode,
		request.PermissionHandlerMode,
		request.ArchitectureReviewMode,
		request.ArchitectureReviewPrompt);

	return Results.Created($"/api/projects/{project.ProjectId}", project);
});

app.MapGet("/api/settings", (IGlobalSettingsCatalog settingsCatalog) =>
{
	PersistedGlobalSettings settings = settingsCatalog.GetSettings();
	return Results.Ok(new
	{
		agentModels = new
		{
			conversation = settings.ConversationModel,
			orchestration = settings.OrchestrationModel,
			frontendDeveloper = settings.FrontendDeveloperModel,
			backendDeveloper = settings.BackendDeveloperModel,
			build = settings.BuildModel,
			codingStyle = settings.CodingStyleModel,
			security = settings.SecurityModel,
			architecture = settings.ArchitectureModel
		},
		defaults = new
		{
			permissionHandlerMode = settings.DefaultPermissionHandlerMode,
			architectureReviewMode = settings.DefaultArchitectureReviewMode,
			architectureReviewPrompt = settings.DefaultArchitectureReviewPrompt
		},
		updatedAtUtc = settings.UpdatedAtUtc
	});
});

app.MapPut("/api/settings", (UpdateGlobalSettingsRequest request, IGlobalSettingsCatalog settingsCatalog, IModelMetadataProvider modelMetadataProvider) =>
{
	UpdatePersistedGlobalSettings update = new UpdatePersistedGlobalSettings(
		request.AgentModels.Conversation,
		request.AgentModels.Orchestration,
		request.AgentModels.FrontendDeveloper,
		request.AgentModels.BackendDeveloper,
		request.AgentModels.Build,
		request.AgentModels.CodingStyle,
		request.AgentModels.Security,
		request.AgentModels.Architecture,
		request.Defaults.PermissionHandlerMode,
		request.Defaults.ArchitectureReviewMode,
		request.Defaults.ArchitectureReviewPrompt);

	string? unknownModel = update
		.GetConfiguredModels()
		.Where(model => !string.IsNullOrWhiteSpace(model))
		.FirstOrDefault(model => !modelMetadataProvider.IsKnownModel(model));
	if (!string.IsNullOrWhiteSpace(unknownModel))
	{
		return Results.BadRequest(new { error = $"Unknown model '{unknownModel}'." });
	}

	PersistedGlobalSettings settings = settingsCatalog.UpdateSettings(update);
	return Results.Ok(new
	{
		agentModels = new
		{
			conversation = settings.ConversationModel,
			orchestration = settings.OrchestrationModel,
			frontendDeveloper = settings.FrontendDeveloperModel,
			backendDeveloper = settings.BackendDeveloperModel,
			build = settings.BuildModel,
			codingStyle = settings.CodingStyleModel,
			security = settings.SecurityModel,
			architecture = settings.ArchitectureModel
		},
		defaults = new
		{
			permissionHandlerMode = settings.DefaultPermissionHandlerMode,
			architectureReviewMode = settings.DefaultArchitectureReviewMode,
			architectureReviewPrompt = settings.DefaultArchitectureReviewPrompt
		},
		updatedAtUtc = settings.UpdatedAtUtc
	});
});

app.MapGet("/api/models", (IModelMetadataProvider modelMetadataProvider) =>
	Results.Ok(new
	{
		models = modelMetadataProvider.GetAvailableModels()
	}));

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

app.MapPost("/api/runs", async (RunRequest request, IWebRunSessionManager sessionManager, IProjectWorkspaceCatalog projectCatalog, SetupSummaryGenerator summaryGenerator, CancellationToken cancellationToken) =>
{
	PersistedProjectWorkspace project = string.IsNullOrWhiteSpace(request.ProjectId)
		? projectCatalog.EnsureProject(
			request.WorkspacePath,
			request.ProjectName,
			request.WorkspaceMode,
			request.PermissionHandlerMode,
			request.ArchitectureLoopMode,
			request.ArchitectureLoopPrompt)
		: projectCatalog.GetProject(request.ProjectId!)
			?? projectCatalog.EnsureProject(
				request.WorkspacePath,
				request.ProjectName,
				request.WorkspaceMode,
				request.PermissionHandlerMode,
				request.ArchitectureLoopMode,
				request.ArchitectureLoopPrompt);

	bool projectWasNew = string.Equals(project.WorkspaceMode, "new-project", StringComparison.OrdinalIgnoreCase);
	string fallbackWorkspaceMode = string.IsNullOrWhiteSpace(request.WorkspaceMode)
		? project.WorkspaceMode
		: request.WorkspaceMode;
	string effectiveWorkspaceMode = projectWasNew ? "new-project" : fallbackWorkspaceMode;

	RunRequest preparedRequest = request with
	{
		ProjectId = project.ProjectId,
		ProjectName = string.IsNullOrWhiteSpace(request.ProjectName) ? project.DisplayName : request.ProjectName,
		WorkspaceMode = effectiveWorkspaceMode
	};
	preparedRequest = await summaryGenerator.PopulateRunTitleAsync(preparedRequest, cancellationToken);
	WebRunSnapshot snapshot = await sessionManager.StartRunAsync(preparedRequest, cancellationToken);

	if (projectWasNew)
	{
		projectCatalog.EnsureProject(
			project.WorkspacePath,
			project.DisplayName,
			"existing-folder",
			preparedRequest.PermissionHandlerMode,
			preparedRequest.ArchitectureLoopMode,
			preparedRequest.ArchitectureLoopPrompt);
	}

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