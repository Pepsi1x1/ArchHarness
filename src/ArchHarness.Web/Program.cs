using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArchHarness.App;
using ArchHarness.App.Constants;
using ArchHarness.App.Core;
using ArchHarness.App.Copilot;
using ArchHarness.App.SourceControl;
using ArchHarness.App.Storage;
using ArchHarness.Web.Services;
using Markdig;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Options;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile(
    Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
    optional: true,
    reloadOnChange: false);
builder.Configuration.AddEnvironmentVariables();

JsonSerializerOptions eventJsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};
MarkdownPipeline markdownPipeline = new MarkdownPipelineBuilder()
    .UseAdvancedExtensions()
    .DisableHtml()
    .Build();

string? webHostUrl = builder.Configuration["webHost:url"];
if (!string.IsNullOrWhiteSpace(webHostUrl))
{
    builder.WebHost.UseUrls(webHostUrl);
}

// All application state is persisted as JSON files on the local file system.
// There is no SQL database or query surface in this application.
builder.Services.AddArchHarnessRuntimeServices(builder.Configuration);
builder.Services.AddArchHarnessInteractiveServices();
builder.Services.AddSingleton<WebInteractionCoordinator>();
builder.Services.AddSingleton<ICopilotUserInputBridge, WebCopilotUserInputBridge>();
builder.Services.AddSingleton<ICopilotPermissionPromptHandler, WebPermissionPromptHandler>();
builder.Services.AddSingleton<IWebRunSessionManager, WebRunSessionManager>();
builder.Services.AddSingleton<IModelMetadataProvider, ModelMetadataProvider>();

WebApplication app = builder.Build();

app.UseExceptionHandler(exceptionApp =>
{
    exceptionApp.Run(async context =>
    {
        ILogger logger = context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("ArchHarness.Web.UnhandledException");
        IExceptionHandlerPathFeature? feature = context.Features.Get<IExceptionHandlerPathFeature>();
        if (feature?.Error is not null)
        {
            logger.LogError(feature.Error, "Unhandled exception while processing {Path}.", context.Request.Path);
        }

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await Results.Problem(
            title: "An unexpected error occurred.",
            detail: "The request could not be completed.",
            statusCode: StatusCodes.Status500InternalServerError,
            extensions: new Dictionary<string, object?>
            {
                ["traceId"] = context.TraceIdentifier
            }).ExecuteAsync(context);
    });
});

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com https://cdnjs.cloudflare.com; font-src 'self' https://fonts.gstatic.com https://cdnjs.cloudflare.com; img-src 'self' data:; connect-src 'self'; script-src 'self'; frame-ancestors 'none'";
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
        defaultTaskPrompt = settings.DefaultArchitectureReviewMode ? DefaultPrompts.ARCHITECTURE_LOOP_TASK : DefaultPrompts.DEFAULT_TASK,
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

app.MapGet("/api/providers", async (ISourceControlProviderService providerService) =>
{
    IReadOnlyList<ProviderConnectionSettings> providers = await providerService.GetConfiguredProvidersAsync();
    return Results.Ok(providers);
});

app.MapPost("/api/providers", async (ProviderConnectionSettings settings, ISourceControlProviderService providerService) =>
{
    Dictionary<string, string[]> validationErrors = ValidateProviderConnectionSettings(settings, requirePersonalAccessToken: false);
    if (validationErrors.Count > 0)
    {
        return Results.ValidationProblem(validationErrors);
    }

    try
    {
        await providerService.SaveProviderAsync(settings);
        ProviderConnectionSettings? savedProvider = (await providerService.GetConfiguredProvidersAsync())
            .FirstOrDefault(provider => string.Equals(provider.DisplayName, NormalizeText(settings.DisplayName), StringComparison.OrdinalIgnoreCase));
        return Results.Ok(savedProvider);
    }
    catch (PlainTextPersonalAccessTokenConfirmationRequiredException ex)
    {
        return Results.Conflict(CreatePersonalAccessTokenStorageConflict(ex.WarningMessage));
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapDelete("/api/providers/{displayName}", async (string displayName, ISourceControlProviderService providerService) =>
{
    if (string.IsNullOrWhiteSpace(displayName))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["displayName"] = new[] { "DisplayName is required." }
        });
    }

    try
    {
        await providerService.DeleteProviderAsync(displayName);
        return Results.NoContent();
    }
    catch (KeyNotFoundException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/providers/test", async (ProviderConnectionSettings settings, ISourceControlProviderService providerService) =>
{
    Dictionary<string, string[]> validationErrors = ValidateProviderConnectionSettings(settings, requirePersonalAccessToken: true);
    if (validationErrors.Count > 0)
    {
        return Results.ValidationProblem(validationErrors);
    }

    ConnectionTestResult result = await providerService.TestConnectionAsync(settings);
    return Results.Ok(result);
});

app.MapGet("/api/providers/{providerName}/pullrequests", async (string providerName, string? project, string? repository, string? author, IProviderConnectionCatalog providerCatalog, SourceControlProviderFactory providerFactory, CancellationToken cancellationToken) =>
{
    Dictionary<string, string[]> validationErrors = ValidatePullRequestLookupRequest(providerName, null, project, repository, author);
    if (validationErrors.Count > 0)
    {
        return Results.ValidationProblem(validationErrors);
    }

    string normalizedProviderName = NormalizeRouteValue(providerName)!;
    string? normalizedProject = NormalizeFilterValue(project);
    string? normalizedRepository = NormalizeFilterValue(repository);
    string? normalizedAuthor = NormalizeFilterValue(author);
    ProviderConnectionSettings? providerSettings = FindProviderByDisplayName(providerCatalog, normalizedProviderName);
    if (providerSettings is null)
    {
        return Results.NotFound(new { error = $"Source control provider '{normalizedProviderName}' was not found." });
    }

    if (!providerSettings.IsEnabled)
    {
        return Results.BadRequest(new { error = $"Source control provider '{providerSettings.DisplayName}' is not enabled." });
    }

    try
    {
        ISourceControlReviewProviderService provider = providerFactory.GetProvider(providerSettings.Provider);
        IReadOnlyList<PullRequestSummary> pullRequests = await provider.GetPullRequestsAsync(
            providerSettings,
            null,
            null,
            cancellationToken,
            normalizedProject,
            normalizedRepository,
            normalizedAuthor);
        return Results.Ok(pullRequests);
    }
    catch (SourceControlRequestFailedException ex)
    {
        return CreateSourceControlErrorResult(ex);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/providers/{providerName}/pullrequests/stream", async Task<IResult> (string providerName, string? project, string? repository, string? author, HttpContext context, IProviderConnectionCatalog providerCatalog, SourceControlProviderFactory providerFactory) =>
{
    CancellationToken cancellationToken = context.RequestAborted;
    Dictionary<string, string[]> validationErrors = ValidatePullRequestLookupRequest(providerName, null, project, repository, author);
    if (validationErrors.Count > 0)
    {
        return Results.ValidationProblem(validationErrors);
    }

    string normalizedProviderName = NormalizeRouteValue(providerName)!;
    string? normalizedProject = NormalizeFilterValue(project);
    string? normalizedRepository = NormalizeFilterValue(repository);
    string? normalizedAuthor = NormalizeFilterValue(author);
    ProviderConnectionSettings? providerSettings = FindProviderByDisplayName(providerCatalog, normalizedProviderName);
    if (providerSettings is null)
    {
        return Results.NotFound(new { error = $"Source control provider '{normalizedProviderName}' was not found." });
    }

    if (!providerSettings.IsEnabled)
    {
        return Results.BadRequest(new { error = $"Source control provider '{providerSettings.DisplayName}' is not enabled." });
    }

    try
    {
        ISourceControlReviewProviderService provider = providerFactory.GetProvider(providerSettings.Provider);
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.Connection = "keep-alive";
        context.Response.ContentType = "text/event-stream";

        await foreach (IReadOnlyList<PullRequestSummary> batch in provider.StreamPullRequestBatchesAsync(
            providerSettings,
            null,
            null,
            cancellationToken,
            normalizedProject,
            normalizedRepository,
            normalizedAuthor))
        {
            await WriteServerSentEventAsync(
                context.Response,
                "batch",
                new { pullRequests = batch },
                eventJsonOptions,
                cancellationToken);
        }

        await WriteServerSentEventAsync(
            context.Response,
            "completed",
            new { completed = true },
            eventJsonOptions,
            cancellationToken);
        return Results.Empty;
    }
    catch (SourceControlRequestFailedException ex)
    {
        if (!context.Response.HasStarted)
        {
            return CreateSourceControlErrorResult(ex);
        }

        await WriteServerSentEventAsync(
            context.Response,
            "error",
            new { error = ex.Message },
            eventJsonOptions,
            cancellationToken);
        return Results.Empty;
    }
    catch (InvalidOperationException ex)
    {
        if (!context.Response.HasStarted)
        {
            return Results.BadRequest(new { error = ex.Message });
        }

        await WriteServerSentEventAsync(
            context.Response,
            "error",
            new { error = ex.Message },
            eventJsonOptions,
            cancellationToken);
        return Results.Empty;
    }
});

app.MapGet("/api/providers/{providerName}/pullrequests/{pullRequestId}/files", async (string providerName, string pullRequestId, string? project, string? repository, IProviderConnectionCatalog providerCatalog, SourceControlProviderFactory providerFactory, CancellationToken cancellationToken) =>
{
    Dictionary<string, string[]> validationErrors = ValidatePullRequestLookupRequest(providerName, pullRequestId, project, repository, null);
    if (validationErrors.Count > 0)
    {
        return Results.ValidationProblem(validationErrors);
    }

    string normalizedProviderName = NormalizeRouteValue(providerName)!;
    string normalizedPullRequestId = NormalizePullRequestId(pullRequestId)!;
    string? normalizedProject = NormalizeFilterValue(project);
    string? normalizedRepository = NormalizeFilterValue(repository);
    ProviderConnectionSettings? providerSettings = FindProviderByDisplayName(providerCatalog, normalizedProviderName);
    if (providerSettings is null)
    {
        return Results.NotFound(new { error = $"Source control provider '{normalizedProviderName}' was not found." });
    }

    if (!providerSettings.IsEnabled)
    {
        return Results.BadRequest(new { error = $"Source control provider '{providerSettings.DisplayName}' is not enabled." });
    }

    try
    {
        ISourceControlReviewProviderService provider = providerFactory.GetProvider(providerSettings.Provider);
        IReadOnlyList<PullRequestFile> files = await provider.GetPullRequestFilesAsync(
            providerSettings,
            normalizedProject,
            normalizedRepository,
            normalizedPullRequestId,
            cancellationToken);
        return Results.Ok(files);
    }
    catch (SourceControlRequestFailedException ex)
    {
        return CreateSourceControlErrorResult(ex);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/projects/{projectId}/pullrequests", async (string projectId, IProjectWorkspaceCatalog projectCatalog, IProviderConnectionCatalog providerCatalog, SourceControlProviderFactory providerFactory, CancellationToken cancellationToken) =>
{
    PersistedProjectWorkspace? project = projectCatalog.GetProject(projectId);
    if (project is null)
    {
        return Results.NotFound(new { error = $"Project '{projectId}' was not found." });
    }

    if (string.IsNullOrWhiteSpace(project.SourceControlProviderName))
    {
        return Results.BadRequest(new { error = "Source control is not configured for this project." });
    }

    if (string.IsNullOrWhiteSpace(project.SourceControlRepositoryName))
    {
        return Results.BadRequest(new { error = "Repository name is not configured for this project." });
    }

    ProviderConnectionSettings? providerSettings = FindProviderByDisplayName(providerCatalog, project.SourceControlProviderName);
    if (providerSettings is null)
    {
        return Results.BadRequest(new { error = $"Source control provider '{project.SourceControlProviderName}' was not found." });
    }

    if (!providerSettings.IsEnabled)
    {
        return Results.BadRequest(new { error = $"Source control provider '{project.SourceControlProviderName}' is not enabled." });
    }

    try
    {
        ISourceControlReviewProviderService provider = providerFactory.GetProvider(providerSettings.Provider);
        IReadOnlyList<PullRequestSummary> pullRequests = await provider.GetPullRequestsAsync(
            providerSettings,
            project.SourceControlProjectName,
            project.SourceControlRepositoryName,
            cancellationToken,
            null,
            null,
            null);
        return Results.Ok(pullRequests);
    }
    catch (SourceControlRequestFailedException ex)
    {
        return CreateSourceControlErrorResult(ex);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPut("/api/projects/{projectId}/source-control", (string projectId, UpdateProjectSourceControlRequest request, IProjectWorkspaceCatalog projectCatalog) =>
{
    PersistedProjectWorkspace? updated = projectCatalog.UpdateProjectSourceControl(
        projectId,
        NormalizeText(request.ProviderName),
        NormalizeText(request.ProjectName),
        NormalizeText(request.RepositoryName));

    if (updated is null)
    {
        return Results.NotFound(new { error = $"Project '{projectId}' was not found." });
    }

    return Results.Ok(new
    {
        projectId = updated.ProjectId,
        sourceControlProviderName = updated.SourceControlProviderName,
        sourceControlProjectName = updated.SourceControlProjectName,
        sourceControlRepositoryName = updated.SourceControlRepositoryName,
        updatedAtUtc = updated.UpdatedAtUtc
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

app.MapGet("/api/runs", (string workspacePath, int? maxCount, IRunHistoryCatalog catalog, IProjectWorkspaceCatalog projectCatalog) =>
{
    if (string.IsNullOrWhiteSpace(workspacePath))
    {
        return Results.BadRequest(new { error = "workspacePath is required." });
    }

    if (!IsKnownWorkspacePath(workspacePath, projectCatalog))
    {
        return Results.BadRequest(new { error = "workspacePath does not match a registered project." });
    }

    return Results.Ok(catalog.GetRecentRuns(workspacePath, Math.Max(1, maxCount ?? 20)));
});

app.MapGet("/api/runs/{runId}/artifacts", (string runId, string workspacePath, int? previewLength, IRunHistoryCatalog catalog, IProjectWorkspaceCatalog projectCatalog) =>
{
    if (string.IsNullOrWhiteSpace(workspacePath))
    {
        return Results.BadRequest(new { error = "workspacePath is required." });
    }

    if (!IsSafeRunId(runId))
    {
        return Results.BadRequest(new { error = "runId must be a single directory name." });
    }

    if (!IsKnownWorkspacePath(workspacePath, projectCatalog))
    {
        return Results.BadRequest(new { error = "workspacePath does not match a registered project." });
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

static IResult CreateSourceControlErrorResult(SourceControlRequestFailedException ex)
    => ex.StatusCode switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => Results.Json(
            new { error = ex.Message },
            statusCode: StatusCodes.Status401Unauthorized),
        HttpStatusCode.NotFound => Results.NotFound(new { error = ex.Message }),
        _ => Results.BadRequest(new { error = ex.Message })
    };

static async Task WriteServerSentEventAsync(HttpResponse response, string eventName, object payload, JsonSerializerOptions serializerOptions, CancellationToken cancellationToken)
{
    string json = JsonSerializer.Serialize(payload, serializerOptions);
    await response.WriteAsync($"event: {eventName}\n", cancellationToken);
    await response.WriteAsync($"data: {json}\n\n", cancellationToken);
    await response.Body.FlushAsync(cancellationToken);
}

static bool IsSafeRunId(string runId)
    => !string.IsNullOrWhiteSpace(runId)
        && runId.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }) < 0
        && !runId.Contains("..");

static bool IsKnownWorkspacePath(string workspacePath, IProjectWorkspaceCatalog projectCatalog)
{
    string normalized = Path.GetFullPath(Environment.ExpandEnvironmentVariables(workspacePath))
        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    return projectCatalog.GetProjects()
        .Any(p => string.Equals(p.WorkspacePath, normalized, StringComparison.OrdinalIgnoreCase));
}

static Dictionary<string, string[]> ValidateProviderConnectionSettings(ProviderConnectionSettings settings, bool requirePersonalAccessToken)
{
    Dictionary<string, List<string>> errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

    static void AddError(IDictionary<string, List<string>> target, string key, string message)
    {
        if (!target.TryGetValue(key, out List<string>? messages))
        {
            messages = new List<string>();
            target[key] = messages;
        }

        messages.Add(message);
    }

    if (!Enum.IsDefined(settings.Provider))
    {
        AddError(errors, "provider", "Provider is required.");
    }

    string? displayName = NormalizeText(settings.DisplayName);
    if (string.IsNullOrWhiteSpace(displayName))
    {
        AddError(errors, "displayName", "DisplayName is required.");
    }
    else if (displayName.IndexOfAny(new[] { '/', '\\' }) >= 0)
    {
        AddError(errors, "displayName", "DisplayName cannot contain path separator characters.");
    }

    if (string.IsNullOrWhiteSpace(NormalizeText(settings.Organization)))
    {
        AddError(errors, "organization", "Organization is required.");
    }

    if (settings.Provider == SourceControlProvider.AzureDevOpsServer)
    {
        string? serverUrl = NormalizeText(settings.ServerUrl);
        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            AddError(errors, "serverUrl", "ServerUrl is required for Azure DevOps Server.");
        }
        else if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out Uri? parsedServerUrl))
        {
            AddError(errors, "serverUrl", "ServerUrl must be an absolute URL.");
        }
        else if (!string.Equals(parsedServerUrl.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            AddError(errors, "serverUrl", "ServerUrl must use HTTPS.");
        }
        else if (!string.IsNullOrEmpty(parsedServerUrl.UserInfo))
        {
            AddError(errors, "serverUrl", "ServerUrl cannot include embedded credentials.");
        }
    }

    if (requirePersonalAccessToken && string.IsNullOrWhiteSpace(NormalizeText(settings.PersonalAccessToken)))
    {
        AddError(errors, "personalAccessToken", "PersonalAccessToken is required.");
    }

    string? personalAccessToken = NormalizeText(settings.PersonalAccessToken);
    if (!string.IsNullOrWhiteSpace(personalAccessToken)
        && Uri.TryCreate(personalAccessToken, UriKind.Absolute, out Uri? parsedPat)
        && (string.Equals(parsedPat.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            || string.Equals(parsedPat.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
    {
        AddError(errors, "personalAccessToken", "PersonalAccessToken looks like a URL. Check browser autofill and re-enter the token.");
    }

    return errors.ToDictionary(entry => entry.Key, entry => entry.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
}

static string? NormalizeText(string? value)
    => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

static Dictionary<string, string[]> ValidatePullRequestLookupRequest(string providerName, string? pullRequestId, string? project, string? repository, string? author)
{
    Dictionary<string, List<string>> errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

    ValidateRequiredRouteValue(errors, "providerName", providerName, 128, allowPathSeparators: false);
    ValidateOptionalLookupValue(errors, "project", project, 200, allowPathSeparators: true);
    ValidateOptionalLookupValue(errors, "repository", repository, 200, allowPathSeparators: true);
    ValidateOptionalLookupValue(errors, "author", author, 200, allowPathSeparators: true);

    if (pullRequestId is not null)
    {
        string? normalizedPullRequestId = NormalizePullRequestId(pullRequestId);
        if (string.IsNullOrWhiteSpace(normalizedPullRequestId))
        {
            AddLookupValidationError(errors, "pullRequestId", "pullRequestId is required.");
        }
        else
        {
            if (normalizedPullRequestId.Length > 20)
            {
                AddLookupValidationError(errors, "pullRequestId", "pullRequestId must be 20 characters or fewer.");
            }

            if (!normalizedPullRequestId.All(char.IsDigit))
            {
                AddLookupValidationError(errors, "pullRequestId", "pullRequestId must be numeric.");
            }
        }
    }

    return errors.ToDictionary(entry => entry.Key, entry => entry.Value.ToArray(), StringComparer.OrdinalIgnoreCase);
}

static void ValidateRequiredRouteValue(
    IDictionary<string, List<string>> errors,
    string key,
    string? value,
    int maxLength,
    bool allowPathSeparators)
{
    string? normalized = NormalizeRouteValue(value);
    if (string.IsNullOrWhiteSpace(normalized))
    {
        AddLookupValidationError(errors, key, $"{key} is required.");
        return;
    }

    ValidateNormalizedLookupValue(errors, key, normalized, maxLength, allowPathSeparators);
}

static void ValidateOptionalLookupValue(
    IDictionary<string, List<string>> errors,
    string key,
    string? value,
    int maxLength,
    bool allowPathSeparators)
{
    string? normalized = NormalizeFilterValue(value);
    if (string.IsNullOrWhiteSpace(normalized))
    {
        return;
    }

    ValidateNormalizedLookupValue(errors, key, normalized, maxLength, allowPathSeparators);
}

static void ValidateNormalizedLookupValue(
    IDictionary<string, List<string>> errors,
    string key,
    string value,
    int maxLength,
    bool allowPathSeparators)
{
    if (value.Length > maxLength)
    {
        AddLookupValidationError(errors, key, $"{key} must be {maxLength} characters or fewer.");
    }

    if (ContainsControlCharacters(value))
    {
        AddLookupValidationError(errors, key, $"{key} contains unsupported control characters.");
    }

    if (!allowPathSeparators && value.IndexOfAny(new[] { '/', '\\' }) >= 0)
    {
        AddLookupValidationError(errors, key, $"{key} cannot contain path separator characters.");
    }
}

static void AddLookupValidationError(IDictionary<string, List<string>> errors, string key, string message)
{
    if (!errors.TryGetValue(key, out List<string>? messages))
    {
        messages = new List<string>();
        errors[key] = messages;
    }

    messages.Add(message);
}

static bool ContainsControlCharacters(string value)
    => value.Any(char.IsControl);

static string? NormalizeRouteValue(string? value)
    => NormalizeText(value);

static string? NormalizeFilterValue(string? value)
    => NormalizeText(value);

static string? NormalizePullRequestId(string? value)
    => NormalizeText(value);

static ProviderConnectionSettings? FindProviderByDisplayName(IProviderConnectionCatalog providerCatalog, string? providerName)
{
    string? normalizedProviderName = NormalizeText(providerName);
    return string.IsNullOrWhiteSpace(normalizedProviderName)
        ? null
        : providerCatalog.GetProviders()
            .FirstOrDefault(provider => string.Equals(provider.DisplayName, normalizedProviderName, StringComparison.OrdinalIgnoreCase));
}

static object CreatePersonalAccessTokenStorageConflict(string warningMessage)
    => new
    {
        code = "pat-protection-unavailable",
        error = warningMessage,
        warning = warningMessage,
        suggestedStorageMode = PersonalAccessTokenStorageMode.PlainText
    };

public partial class Program
{
    private Program()
    {
    }
}
