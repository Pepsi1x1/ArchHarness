using System.Text.Json;
using ArchHarness.App.Core;
using ArchHarness.App.SourceControl;
using ArchHarness.App.Storage;
using ArchHarness.Web.Services;
using Markdig;

namespace ArchHarness.Web;

internal static class ProgramEndpointExtensions
{
    public static IEndpointRouteBuilder MapArchHarnessApi(this IEndpointRouteBuilder endpoints, JsonSerializerOptions eventJsonOptions, MarkdownPipeline markdownPipeline)
    {
        endpoints.MapGet("/api", ProgramHandlers.GetApiRoot);
        endpoints.MapGet("/api/bootstrap", ProgramHandlers.GetBootstrap);
        endpoints.MapGet("/api/health", ProgramHandlers.GetHealth);

        endpoints.MapGet("/api/projects", ProgramHandlers.GetProjects);
        endpoints.MapPost("/api/projects", ProgramHandlers.CreateProject);
        endpoints.MapGet("/api/projects/{projectId}/branch", ProgramHandlers.GetProjectBranch);
        endpoints.MapGet("/api/projects/{projectId}/git/changes", ProgramHandlers.GetProjectGitChanges);
        endpoints.MapGet("/api/projects/{projectId}/git/diff", ProgramHandlers.GetProjectGitDiff);
        endpoints.MapPost("/api/projects/{projectId}/git/stash", ProgramHandlers.StashProjectChanges);
        endpoints.MapPost("/api/projects/{projectId}/git/clone", ProgramHandlers.CloneProjectRepositoryAsync);
        endpoints.MapPost("/api/projects/{projectId}/branch", ProgramHandlers.SwitchProjectBranchAsync);
        endpoints.MapGet("/api/projects/{projectId}/pullrequests", ProgramHandlers.GetProjectPullRequestsAsync);
        endpoints.MapPut("/api/projects/{projectId}/source-control", ProgramHandlers.UpdateProjectSourceControl);

        endpoints.MapGet("/api/settings", ProgramHandlers.GetSettings);
        endpoints.MapPut("/api/settings", ProgramHandlers.UpdateSettings);
        endpoints.MapGet("/api/models", ProgramHandlers.GetModels);
        endpoints.MapGet("/api/preflight", ProgramHandlers.GetPreflightAsync);
        endpoints.MapPost("/api/setup-summary", ProgramHandlers.CreateSetupSummaryAsync);
        endpoints.MapPost("/api/markdown/render", (MarkdownRenderRequest request) => ProgramHandlers.RenderMarkdown(request, markdownPipeline));

        endpoints.MapGet("/api/providers", ProgramHandlers.GetProvidersAsync);
        endpoints.MapPost("/api/providers", ProgramHandlers.SaveProviderAsync);
        endpoints.MapDelete("/api/providers/{displayName}", ProgramHandlers.DeleteProviderAsync);
        endpoints.MapPost("/api/providers/test", ProgramHandlers.TestProviderConnectionAsync);
        endpoints.MapPost("/api/providers/github/oauth/device-flow", ProgramHandlers.StartGitHubOAuthDeviceFlowAsync);
        endpoints.MapGet("/api/providers/github/oauth/device-flow/{flowId}", ProgramHandlers.PollGitHubOAuthDeviceFlowAsync);
        endpoints.MapGet("/api/providers/{providerName}/pullrequests", ProgramHandlers.GetProviderPullRequestsAsync);
        endpoints.MapGet(
            "/api/providers/{providerName}/pullrequests/stream",
            (string providerName, string? project, string? repository, string? author, HttpContext context, IProviderConnectionCatalog providerCatalog, SourceControlProviderFactory providerFactory)
                => ProgramHandlers.StreamProviderPullRequestsAsync(
                    new PullRequestLookupContext(providerName, project, repository, author),
                    context,
                    providerCatalog,
                    providerFactory,
                    eventJsonOptions));
        endpoints.MapGet("/api/providers/{providerName}/pullrequests/{pullRequestId}/files", ProgramHandlers.GetProviderPullRequestFilesAsync);

        endpoints.MapGet("/api/runs", ProgramHandlers.GetRuns);
        endpoints.MapGet("/api/runs/{runId}/artifacts", ProgramHandlers.GetRunArtifacts);
        endpoints.MapGet("/api/runs/{runId}/events", ProgramHandlers.GetRunEvents);
        endpoints.MapGet("/api/runs/{runId}/state", ProgramHandlers.GetRunState);
        endpoints.MapPost("/api/runs", ProgramHandlers.StartRunAsync);
        endpoints.MapPost("/api/runs/{runId}/resume", ProgramHandlers.ResumeRunAsync);
        endpoints.MapDelete("/api/runs/active", ProgramHandlers.CancelActiveRunAsync);
        endpoints.MapGet("/api/runs/active", ProgramHandlers.GetActiveRun);
        endpoints.MapGet(
            "/api/runs/active/events",
            (HttpContext context, IWebRunSessionManager sessionManager, CancellationToken cancellationToken)
                => ProgramHandlers.StreamActiveRunEventsAsync(context, sessionManager, eventJsonOptions, cancellationToken));

        endpoints.MapGet("/api/interactions/pending", ProgramHandlers.GetPendingInteraction);
        endpoints.MapPost("/api/interactions/user-input", ProgramHandlers.SubmitUserInput);
        endpoints.MapPost("/api/interactions/permission", ProgramHandlers.SubmitPermission);

        return endpoints;
    }
}