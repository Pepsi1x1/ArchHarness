using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArchHarness.App;
using ArchHarness.App.Constants;
using ArchHarness.App.Core;
using ArchHarness.App.Copilot;
using ArchHarness.App.SourceControl;
using ArchHarness.App.Storage;
using ArchHarness.Web;
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
const string contentSecurityPolicy = "default-src 'self'; style-src 'self' https://fonts.googleapis.com https://cdnjs.cloudflare.com; font-src 'self' https://fonts.gstatic.com https://cdnjs.cloudflare.com; img-src 'self' data:; connect-src 'self'; script-src 'self'; base-uri 'none'; object-src 'none'; frame-ancestors 'none'; form-action 'self'";
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
    context.Response.Headers["Content-Security-Policy"] = contentSecurityPolicy;
    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api", ProgramHandlers.GetApiRoot);

app.MapGet("/api/bootstrap", ProgramHandlers.GetBootstrap);

app.MapGet("/api/health", ProgramHandlers.GetHealth);

app.MapGet("/api/projects", ProgramHandlers.GetProjects);

app.MapPost("/api/projects", ProgramHandlers.CreateProject);

app.MapGet("/api/projects/{projectId}/branch", ProgramHandlers.GetProjectBranch);

app.MapGet("/api/projects/{projectId}/git/changes", ProgramHandlers.GetProjectGitChanges);

app.MapGet("/api/projects/{projectId}/git/diff", ProgramHandlers.GetProjectGitDiff);

app.MapPost("/api/projects/{projectId}/git/stash", ProgramHandlers.StashProjectChanges);

app.MapPost("/api/projects/{projectId}/git/clone", ProgramHandlers.CloneProjectRepositoryAsync);

app.MapPost("/api/projects/{projectId}/branch", ProgramHandlers.SwitchProjectBranch);

app.MapGet("/api/settings", ProgramHandlers.GetSettings);

app.MapPut("/api/settings", ProgramHandlers.UpdateSettings);

app.MapGet("/api/providers", ProgramHandlers.GetProvidersAsync);

app.MapPost("/api/providers", ProgramHandlers.SaveProviderAsync);

app.MapDelete("/api/providers/{displayName}", ProgramHandlers.DeleteProviderAsync);

app.MapPost("/api/providers/test", ProgramHandlers.TestProviderConnectionAsync);

app.MapGet("/api/providers/{providerName}/pullrequests", ProgramHandlers.GetProviderPullRequestsAsync);

app.MapGet("/api/providers/{providerName}/pullrequests/stream", (string providerName, string? project, string? repository, string? author, HttpContext context, IProviderConnectionCatalog providerCatalog, SourceControlProviderFactory providerFactory)
    => ProgramHandlers.StreamProviderPullRequestsAsync(new PullRequestLookupContext(providerName, project, repository, author), context, providerCatalog, providerFactory, eventJsonOptions));

app.MapGet("/api/providers/{providerName}/pullrequests/{pullRequestId}/files", ProgramHandlers.GetProviderPullRequestFilesAsync);

app.MapGet("/api/projects/{projectId}/pullrequests", ProgramHandlers.GetProjectPullRequestsAsync);

app.MapPut("/api/projects/{projectId}/source-control", ProgramHandlers.UpdateProjectSourceControl);

app.MapGet("/api/models", ProgramHandlers.GetModels);

app.MapGet("/api/preflight", ProgramHandlers.GetPreflightAsync);

app.MapPost("/api/setup-summary", ProgramHandlers.CreateSetupSummaryAsync);

app.MapPost("/api/markdown/render", (MarkdownRenderRequest request) => ProgramHandlers.RenderMarkdown(request, markdownPipeline));

app.MapGet("/api/runs", ProgramHandlers.GetRuns);

app.MapGet("/api/runs/{runId}/artifacts", ProgramHandlers.GetRunArtifacts);

app.MapPost("/api/runs", ProgramHandlers.StartRunAsync);

app.MapDelete("/api/runs/active", ProgramHandlers.CancelActiveRunAsync);

app.MapGet("/api/runs/active", ProgramHandlers.GetActiveRun);

app.MapGet("/api/runs/active/events", (HttpContext context, IWebRunSessionManager sessionManager, CancellationToken cancellationToken)
    => ProgramHandlers.StreamActiveRunEventsAsync(context, sessionManager, eventJsonOptions, cancellationToken));

app.MapGet("/api/interactions/pending", ProgramHandlers.GetPendingInteraction);

app.MapPost("/api/interactions/user-input", ProgramHandlers.SubmitUserInput);

app.MapPost("/api/interactions/permission", ProgramHandlers.SubmitPermission);

await app.RunAsync();

public partial class Program
{
    private Program()
    {
    }
}
