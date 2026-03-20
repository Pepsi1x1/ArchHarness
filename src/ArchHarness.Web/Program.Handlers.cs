using System.Net;
using System.Text.Json;
using ArchHarness.App;
using ArchHarness.App.Constants;
using ArchHarness.App.Core;
using ArchHarness.App.Copilot;
using ArchHarness.App.SourceControl;
using ArchHarness.App.Storage;
using ArchHarness.Web.Services;
using Markdig;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace ArchHarness.Web;

internal static class ProgramHandlers
{
    private const string AgentHarnessDirectoryName = ".agent-harness";
    private const string InvalidRunIdMessage = "runId must be a single directory name.";
    private const string UnknownWorkspaceMessage = "workspacePath does not match a registered project.";
    private const string WorkspacePathRequiredMessage = "workspacePath is required.";

    public static IResult GetApiRoot()
        => Results.Ok(new
        {
            name = "ArchHarness.Web",
            mode = "local-only",
            status = "ready"
        });

    public static IResult GetBootstrap(IOptions<AgentsOptions> agentsOptions, IGlobalSettingsCatalog settingsCatalog, IWebRunSessionManager sessionManager, IGitHubOAuthDeviceFlowService gitHubOAuthDeviceFlowService)
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
            gitHubOAuthEnabled = gitHubOAuthDeviceFlowService.IsEnabled,
            activeRun = sessionManager.GetSnapshot()
        });
    }

    public static IResult GetHealth()
        => Results.Ok(new { healthy = true });

    public static IResult GetProjects(IProjectWorkspaceCatalog projectCatalog, IRunHistoryCatalog runHistoryCatalog, int? maxRunsPerProject)
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
    }

    public static IResult CreateProject(CreateProjectRequest request, IProjectWorkspaceCatalog projectCatalog)
    {
        PersistedProjectWorkspace project = projectCatalog.CreateProject(
            request.DisplayName,
            request.WorkspacePath,
            request.WorkspaceMode,
            request.PermissionHandlerMode,
            request.ArchitectureReviewMode,
            request.ArchitectureReviewPrompt);

        return Results.Created($"/api/projects/{project.ProjectId}", project);
    }

    public static IResult GetProjectBranch(string projectId, IProjectWorkspaceCatalog projectCatalog, IGitRepositoryInfoService gitRepositoryInfoService)
    {
        PersistedProjectWorkspace? project = projectCatalog.GetProject(projectId);
        if (project is null)
        {
            return Results.NotFound(new { error = $"Project '{projectId}' was not found." });
        }

        GitRepositoryBranchInfo branchInfo = gitRepositoryInfoService.GetBranchInfo(project.WorkspacePath);
        return Results.Ok(new
        {
            projectId = project.ProjectId,
            isGitRepository = branchInfo.IsGitRepository,
            currentBranch = branchInfo.CurrentBranch,
            branches = branchInfo.Branches
        });
    }

    public static IResult GetProjectGitChanges(string projectId, IProjectWorkspaceCatalog projectCatalog, IGitRepositoryInfoService gitRepositoryInfoService)
    {
        PersistedProjectWorkspace? project = projectCatalog.GetProject(projectId);
        if (project is null)
        {
            return Results.NotFound(new { error = $"Project '{projectId}' was not found." });
        }

        GitWorkingTreeStatus changeStatus = gitRepositoryInfoService.GetWorkingTreeStatus(project.WorkspacePath);
        return Results.Ok(new
        {
            projectId = project.ProjectId,
            isGitRepository = changeStatus.IsGitRepository,
            currentBranch = changeStatus.CurrentBranch,
            hasChanges = changeStatus.HasChanges,
            files = changeStatus.Files.Select(file => new
            {
                path = file.Path,
                status = file.Status,
                previousPath = file.PreviousPath,
                isStaged = file.IsStaged,
                isUntracked = file.IsUntracked
            })
        });
    }

    public static IResult GetProjectGitDiff(string projectId, string path, IProjectWorkspaceCatalog projectCatalog, IGitRepositoryInfoService gitRepositoryInfoService)
    {
        PersistedProjectWorkspace? project = projectCatalog.GetProject(projectId);
        if (project is null)
        {
            return Results.NotFound(new { error = $"Project '{projectId}' was not found." });
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return Results.BadRequest(new { error = "A repository-relative path is required." });
        }

        GitWorkingTreeDiffResult diffResult = gitRepositoryInfoService.GetWorkingTreeDiff(project.WorkspacePath, path);
        if (!diffResult.IsGitRepository)
        {
            return Results.BadRequest(new { error = diffResult.ErrorMessage, failureCode = "not-git-repository" });
        }

        if (!diffResult.HasDiff)
        {
            return Results.NotFound(new { error = diffResult.ErrorMessage, failureCode = "diff-not-found", path = diffResult.Path });
        }

        return Results.Ok(new
        {
            projectId = project.ProjectId,
            path = diffResult.Path,
            diffText = diffResult.DiffText
        });
    }

    public static IResult StashProjectChanges(string projectId, StashProjectChangesRequest request, IProjectWorkspaceCatalog projectCatalog, IGitRepositoryInfoService gitRepositoryInfoService)
    {
        PersistedProjectWorkspace? project = projectCatalog.GetProject(projectId);
        if (project is null)
        {
            return Results.NotFound(new { error = $"Project '{projectId}' was not found." });
        }

        GitStashChangesResult stashResult = gitRepositoryInfoService.StashWorkingTreeChanges(project.WorkspacePath, request.Message);
        object responsePayload = new
        {
            projectId = project.ProjectId,
            success = stashResult.Succeeded,
            error = stashResult.ErrorMessage,
            failureCode = stashResult.FailureCode,
            branchInfo = new
            {
                isGitRepository = stashResult.BranchInfo.IsGitRepository,
                currentBranch = stashResult.BranchInfo.CurrentBranch,
                branches = stashResult.BranchInfo.Branches
            },
            workingTreeStatus = new
            {
                isGitRepository = stashResult.WorkingTreeStatus.IsGitRepository,
                currentBranch = stashResult.WorkingTreeStatus.CurrentBranch,
                hasChanges = stashResult.WorkingTreeStatus.HasChanges,
                files = stashResult.WorkingTreeStatus.Files.Select(file => new
                {
                    path = file.Path,
                    status = file.Status,
                    previousPath = file.PreviousPath,
                    isStaged = file.IsStaged,
                    isUntracked = file.IsUntracked
                })
            }
        };

        if (!stashResult.Succeeded)
        {
            return stashResult.FailureCode switch
            {
                "not-git-repository" => Results.BadRequest(responsePayload),
                "no-changes" => Results.Conflict(responsePayload),
                _ => Results.Conflict(responsePayload)
            };
        }

        return Results.Ok(responsePayload);
    }

    public static async Task<IResult> CloneProjectRepositoryAsync(string projectId, CloneProjectRepositoryRequest request, IProjectWorkspaceCatalog projectCatalog, IProviderConnectionCatalog providerCatalog, SourceControlProviderFactory providerFactory, IGitRepositoryInfoService gitRepositoryInfoService, CancellationToken cancellationToken)
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

        ProviderConnectionSettings? providerSettings = await FindProviderByDisplayNameAsync(providerCatalog, project.SourceControlProviderName).ConfigureAwait(false);
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
            string cloneUrl = await provider.GetRepositoryCloneUrlAsync(
                providerSettings,
                project.SourceControlProjectName,
                project.SourceControlRepositoryName,
                cancellationToken);
            GitCloneResult result = gitRepositoryInfoService.CloneRepository(
                project.WorkspacePath,
                cloneUrl,
                NormalizeText(request.BranchName),
                BuildGitAuthenticationOptions(providerSettings));

            if (!result.Succeeded)
            {
                object errorPayload = new
                {
                    error = result.ErrorMessage,
                    failureCode = result.FailureCode,
                    branchInfo = new
                    {
                        isGitRepository = result.BranchInfo.IsGitRepository,
                        currentBranch = result.BranchInfo.CurrentBranch,
                        branches = result.BranchInfo.Branches
                    }
                };

                return result.FailureCode switch
                {
                    "already-git-repository" => Results.Conflict(errorPayload),
                    _ => Results.BadRequest(errorPayload)
                };
            }

            return Results.Ok(new
            {
                projectId = project.ProjectId,
                isGitRepository = result.BranchInfo.IsGitRepository,
                currentBranch = result.BranchInfo.CurrentBranch,
                branches = result.BranchInfo.Branches
            });
        }
        catch (SourceControlRequestFailedException ex)
        {
            return CreateSourceControlErrorResult(ex);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (HttpRequestException)
        {
            return CreateSourceControlTransportErrorResult(providerSettings.Provider, "pull request retrieval");
        }
    }

    public static async Task<IResult> SwitchProjectBranchAsync(string projectId, SwitchProjectBranchRequest request, IProjectWorkspaceCatalog projectCatalog, IProviderConnectionCatalog providerCatalog, IGitRepositoryInfoService gitRepositoryInfoService)
    {
        PersistedProjectWorkspace? project = projectCatalog.GetProject(projectId);
        if (project is null)
        {
            return Results.NotFound(new { error = $"Project '{projectId}' was not found." });
        }

        ProviderConnectionSettings? providerSettings = await FindProviderByDisplayNameAsync(providerCatalog, project.SourceControlProviderName).ConfigureAwait(false);
        GitBranchCheckoutResult result = gitRepositoryInfoService.CheckoutBranch(
            project.WorkspacePath,
            request.BranchName,
            BuildGitAuthenticationOptions(providerSettings));
        if (!result.Succeeded)
        {
            object errorPayload = new
            {
                error = result.ErrorMessage,
                failureCode = result.FailureCode,
                branchInfo = new
                {
                    isGitRepository = result.BranchInfo.IsGitRepository,
                    currentBranch = result.BranchInfo.CurrentBranch,
                    branches = result.BranchInfo.Branches
                }
            };

            return result.FailureCode switch
            {
                "branch-not-found" => Results.NotFound(errorPayload),
                "dirty-worktree" or "checkout-conflict" => Results.Conflict(errorPayload),
                _ => Results.BadRequest(errorPayload)
            };
        }

        return Results.Ok(new
        {
            projectId = project.ProjectId,
            isGitRepository = result.BranchInfo.IsGitRepository,
            currentBranch = result.BranchInfo.CurrentBranch,
            branches = result.BranchInfo.Branches
        });
    }

    public static IResult GetSettings(IGlobalSettingsCatalog settingsCatalog)
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
    }

    public static IResult UpdateSettings(UpdateGlobalSettingsRequest request, IGlobalSettingsCatalog settingsCatalog, IModelMetadataProvider modelMetadataProvider)
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
    }

    public static async Task<IResult> GetProvidersAsync(ISourceControlProviderService providerService)
    {
        IReadOnlyList<ProviderConnectionSettings> providers = await providerService.GetConfiguredProvidersAsync();
        return Results.Ok(providers);
    }

    public static async Task<IResult> SaveProviderAsync(ProviderConnectionSettings settings, ISourceControlProviderService providerService, IProviderConnectionSettingsCoordinator settingsCoordinator)
    {
        ProviderConnectionSettings preparedSettings = await settingsCoordinator.PrepareForSaveAsync(settings);
        Dictionary<string, string[]> validationErrors = settingsCoordinator.GetValidationErrors(
            preparedSettings,
            requirePersonalAccessToken: false);
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
        catch (InvalidOperationException ex)
        {
            if (ShouldOfferPlainTextPersonalAccessTokenFallback(settings, ex))
            {
                // The frontend uses this conflict response to warn the user before allowing plain-text fallback.
                return Results.Conflict(new
                {
                    code = "pat-protection-unavailable",
                    error = ex.Message,
                    warning = ex.Message
                });
            }

            return Results.BadRequest(new { error = ex.Message });
        }
    }

    private static bool ShouldOfferPlainTextPersonalAccessTokenFallback(ProviderConnectionSettings settings, InvalidOperationException exception)
        // This predicate protects the intentional plain-text fallback path. Keep it aligned with the UI confirmation flow
        // so plain-text storage only happens after the user is warned and opts in.
        => settings.PersonalAccessTokenStorageMode == PersonalAccessTokenStorageMode.Protected
            && !string.IsNullOrWhiteSpace(settings.PersonalAccessToken)
            && exception.Message.Contains("secure", StringComparison.OrdinalIgnoreCase);

    public static async Task<IResult> DeleteProviderAsync(string displayName, ISourceControlProviderService providerService)
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
    }

    public static async Task<IResult> TestProviderConnectionAsync(ProviderConnectionSettings settings, ISourceControlProviderService providerService, IProviderConnectionSettingsCoordinator settingsCoordinator)
    {
        ProviderConnectionSettings preparedSettings = await settingsCoordinator.PrepareForConnectionTestAsync(settings);
        Dictionary<string, string[]> validationErrors = settingsCoordinator.GetValidationErrors(
            preparedSettings,
            requirePersonalAccessToken: RequiresPersonalAccessTokenForConnectionTest(preparedSettings.Provider));
        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }

        ConnectionTestResult result = await providerService.TestConnectionAsync(settings);
        return Results.Ok(result);
    }

    public static async Task<IResult> StartGitHubOAuthDeviceFlowAsync(IGitHubOAuthDeviceFlowService gitHubOAuthDeviceFlowService, CancellationToken cancellationToken)
    {
        if (!gitHubOAuthDeviceFlowService.IsEnabled)
        {
            return Results.Json(
                new { error = "GitHub OAuth is not configured. Set gitHubOAuth.clientId before authorizing with GitHub." },
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        try
        {
            GitHubOAuthDeviceFlowStartResult result = await gitHubOAuthDeviceFlowService.StartAsync(cancellationToken);
            return Results.Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (HttpRequestException)
        {
            return CreateSourceControlTransportErrorResult(SourceControlProvider.GitHub, "OAuth authorization");
        }
    }

    public static async Task<IResult> PollGitHubOAuthDeviceFlowAsync(string flowId, IGitHubOAuthDeviceFlowService gitHubOAuthDeviceFlowService, CancellationToken cancellationToken)
    {
        string? normalizedFlowId = NormalizeText(flowId);
        if (string.IsNullOrWhiteSpace(normalizedFlowId))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["flowId"] = new[] { "flowId is required." }
            });
        }

        try
        {
            GitHubOAuthDeviceFlowPollResult result = await gitHubOAuthDeviceFlowService.PollAsync(normalizedFlowId, cancellationToken);
            return Results.Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (HttpRequestException)
        {
            return CreateSourceControlTransportErrorResult(SourceControlProvider.GitHub, "OAuth authorization");
        }
    }

    public static async Task<IResult> GetProviderPullRequestsAsync(string providerName, string? project, string? repository, string? author, IProviderConnectionCatalog providerCatalog, SourceControlProviderFactory providerFactory, CancellationToken cancellationToken)
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
        ProviderConnectionSettings? providerSettings = await FindProviderByDisplayNameAsync(providerCatalog, normalizedProviderName).ConfigureAwait(false);
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
    }

    public static async Task<IResult> StreamProviderPullRequestsAsync(PullRequestLookupContext lookup, HttpContext context, IProviderConnectionCatalog providerCatalog, SourceControlProviderFactory providerFactory, JsonSerializerOptions eventJsonOptions)
    {
        CancellationToken cancellationToken = context.RequestAborted;
        Dictionary<string, string[]> validationErrors = ValidatePullRequestLookupRequest(lookup.ProviderName, null, lookup.Project, lookup.Repository, lookup.Author);
        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }

        string normalizedProviderName = NormalizeRouteValue(lookup.ProviderName)!;
        string? normalizedProject = NormalizeFilterValue(lookup.Project);
        string? normalizedRepository = NormalizeFilterValue(lookup.Repository);
        string? normalizedAuthor = NormalizeFilterValue(lookup.Author);
        ProviderConnectionSettings? providerSettings = await FindProviderByDisplayNameAsync(providerCatalog, normalizedProviderName).ConfigureAwait(false);
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
        catch (HttpRequestException)
        {
            string errorMessage = BuildSourceControlTransportErrorMessage(providerSettings.Provider, "pull request retrieval");
            if (!context.Response.HasStarted)
            {
                return Results.Json(new { error = errorMessage }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            await WriteServerSentEventAsync(
                context.Response,
                "error",
                new { error = errorMessage },
                eventJsonOptions,
                cancellationToken);
            return Results.Empty;
        }
    }

    public static async Task<IResult> GetProviderPullRequestFilesAsync(string providerName, string pullRequestId, string? project, string? repository, IProviderConnectionCatalog providerCatalog, SourceControlProviderFactory providerFactory, CancellationToken cancellationToken)
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
        ProviderConnectionSettings? providerSettings = await FindProviderByDisplayNameAsync(providerCatalog, normalizedProviderName).ConfigureAwait(false);
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
        catch (HttpRequestException)
        {
            return CreateSourceControlTransportErrorResult(providerSettings.Provider, "pull request file retrieval");
        }
    }

    public static async Task<IResult> GetProjectPullRequestsAsync(string projectId, IProjectWorkspaceCatalog projectCatalog, IProviderConnectionCatalog providerCatalog, SourceControlProviderFactory providerFactory, CancellationToken cancellationToken)
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

        ProviderConnectionSettings? providerSettings = await FindProviderByDisplayNameAsync(providerCatalog, project.SourceControlProviderName);
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
        catch (HttpRequestException)
        {
            return CreateSourceControlTransportErrorResult(providerSettings.Provider, "pull request retrieval");
        }
    }

    public static IResult UpdateProjectSourceControl(string projectId, UpdateProjectSourceControlRequest request, IProjectWorkspaceCatalog projectCatalog)
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
    }

    public static IResult GetModels(IModelMetadataProvider modelMetadataProvider)
        => Results.Ok(new
        {
            models = modelMetadataProvider.GetAvailableModels()
        });

    public static async Task<IResult> GetPreflightAsync(IStartupPreflightValidator validator, CancellationToken cancellationToken)
    {
        PreflightValidationResult result = await validator.ValidateAsync(cancellationToken);
        return Results.Ok(result);
    }

    public static async Task<IResult> CreateSetupSummaryAsync(RunRequest request, SetupSummaryGenerator generator, CancellationToken cancellationToken)
    {
        string summary = await generator.GenerateSetupSummaryAsync(request, cancellationToken);
        return Results.Ok(new { summary });
    }

    public static IResult RenderMarkdown(MarkdownRenderRequest request, MarkdownPipeline markdownPipeline)
    {
        string html = Markdown.ToHtml(request.Markdown ?? string.Empty, markdownPipeline);
        return Results.Ok(new { html });
    }

    public static IResult GetRuns(string workspacePath, int? maxCount, IRunHistoryCatalog catalog, IProjectWorkspaceCatalog projectCatalog)
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            return Results.BadRequest(new { error = WorkspacePathRequiredMessage });
        }

        if (!IsKnownWorkspacePath(workspacePath, projectCatalog))
        {
            return Results.BadRequest(new { error = UnknownWorkspaceMessage });
        }

        return Results.Ok(catalog.GetRecentRuns(workspacePath, Math.Max(1, maxCount ?? 20)));
    }

    public static IResult GetRunArtifacts(string runId, string workspacePath, int? previewLength, IRunHistoryCatalog catalog, IProjectWorkspaceCatalog projectCatalog)
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            return Results.BadRequest(new { error = WorkspacePathRequiredMessage });
        }

        if (!IsSafeRunId(runId))
        {
            return Results.BadRequest(new { error = InvalidRunIdMessage });
        }

        if (!IsKnownWorkspacePath(workspacePath, projectCatalog))
        {
            return Results.BadRequest(new { error = UnknownWorkspaceMessage });
        }

        string runDirectory = Path.Combine(Path.GetFullPath(workspacePath), AgentHarnessDirectoryName, "runs", runId);
        return Results.Ok(catalog.GetArtifacts(runDirectory, Math.Max(32, previewLength ?? 2400)));
    }

    public static IResult GetRunEvents(string runId, string workspacePath, IRunHistoryCatalog catalog, IProjectWorkspaceCatalog projectCatalog)
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            return Results.BadRequest(new { error = WorkspacePathRequiredMessage });
        }

        if (!IsSafeRunId(runId))
        {
            return Results.BadRequest(new { error = InvalidRunIdMessage });
        }

        if (!IsKnownWorkspacePath(workspacePath, projectCatalog))
        {
            return Results.BadRequest(new { error = UnknownWorkspaceMessage });
        }

        string runDirectory = Path.Combine(Path.GetFullPath(workspacePath), AgentHarnessDirectoryName, "runs", runId);
        return Results.Ok(catalog.GetEvents(runDirectory).Select(evt => new WebRunEvent(
            evt.TimestampUtc,
            evt.Kind,
            evt.Source,
            evt.Message,
            evt.AgentId,
            evt.AgentRole,
            evt.SessionId,
            evt.Model,
            evt.Details,
            evt.ContentFormat,
            evt.StreamKind,
            evt.Title)));
    }

    public static IResult GetRunState(string runId, string workspacePath, IRunStateStore runStateStore, IProjectWorkspaceCatalog projectCatalog)
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            return Results.BadRequest(new { error = WorkspacePathRequiredMessage });
        }

        if (!IsSafeRunId(runId))
        {
            return Results.BadRequest(new { error = InvalidRunIdMessage });
        }

        if (!IsKnownWorkspacePath(workspacePath, projectCatalog))
        {
            return Results.BadRequest(new { error = UnknownWorkspaceMessage });
        }

        string runDirectory = Path.Combine(Path.GetFullPath(workspacePath), AgentHarnessDirectoryName, "runs", runId);
        PersistedRunState? runState = runStateStore.GetState(runDirectory);
        if (runState is null)
        {
            return Results.NotFound(new { error = $"Run '{runId}' does not have persisted resume state." });
        }

        return Results.Ok(new
        {
            runState.RunId,
            runState.Status,
            runState.Phase,
            runState.StartedAtUtc,
            runState.UpdatedAtUtc,
            runState.FailureMessage,
            runState.CanResume,
            completedStepIds = runState.CompletedStepIds,
            runState.ReviewIteration
        });
    }

    public static async Task<IResult> StartRunAsync(RunRequest request, IWebRunSessionManager sessionManager, IProjectWorkspaceCatalog projectCatalog, SetupSummaryGenerator summaryGenerator, CancellationToken cancellationToken)
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
    }

    public static async Task<IResult> ResumeRunAsync(string runId, string workspacePath, IWebRunSessionManager sessionManager, IRunStateStore runStateStore, IProjectWorkspaceCatalog projectCatalog, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            return Results.BadRequest(new { error = WorkspacePathRequiredMessage });
        }

        if (!IsSafeRunId(runId))
        {
            return Results.BadRequest(new { error = InvalidRunIdMessage });
        }

        if (!IsKnownWorkspacePath(workspacePath, projectCatalog))
        {
            return Results.BadRequest(new { error = UnknownWorkspaceMessage });
        }

        string runDirectory = Path.Combine(Path.GetFullPath(workspacePath), AgentHarnessDirectoryName, "runs", runId);
        PersistedRunState? runState = runStateStore.GetState(runDirectory);
        if (runState is null)
        {
            return Results.NotFound(new { error = $"Run '{runId}' does not have persisted resume state." });
        }

        if (!runState.CanResume)
        {
            return Results.Conflict(new { error = $"Run '{runId}' is not resumable.", runState.Status, runState.Phase });
        }

        WebRunSnapshot snapshot = await sessionManager.ResumeRunAsync(runState, cancellationToken);
        return Results.Accepted("/api/runs/active", snapshot);
    }

    public static async Task<IResult> CancelActiveRunAsync(IWebRunSessionManager sessionManager)
    {
        WebRunSnapshot snapshot = await sessionManager.CancelRunAsync();
        return Results.Ok(snapshot);
    }

    public static IResult GetActiveRun(IWebRunSessionManager sessionManager)
        => Results.Ok(sessionManager.GetSnapshot());

    public static async Task<IResult> StreamActiveRunEventsAsync(HttpContext context, IWebRunSessionManager sessionManager, JsonSerializerOptions eventJsonOptions, CancellationToken cancellationToken)
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
    }

    public static IResult GetPendingInteraction(WebInteractionCoordinator interactions)
    {
        PendingInteractionSnapshot? pending = interactions.GetPending();
        return pending is null ? Results.NoContent() : Results.Ok(pending);
    }

    public static IResult SubmitUserInput(UserInputSubmission submission, WebInteractionCoordinator interactions)
    {
        if (!interactions.TrySubmitUserInput(submission.Answer))
        {
            return Results.Conflict(new { error = "No pending user-input request is active." });
        }

        return Results.Accepted();
    }

    public static IResult SubmitPermission(PermissionSubmission submission, WebInteractionCoordinator interactions)
    {
        if (!interactions.TrySubmitPermission(submission.Approved))
        {
            return Results.Conflict(new { error = "No pending permission request is active." });
        }

        return Results.Accepted();
    }

    private static IResult CreateSourceControlErrorResult(SourceControlRequestFailedException ex)
        => ex.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => Results.Json(
                new { error = ex.Message },
                statusCode: StatusCodes.Status401Unauthorized),
            HttpStatusCode.NotFound => Results.NotFound(new { error = ex.Message }),
            _ => Results.BadRequest(new { error = ex.Message })
        };

    private static IResult CreateSourceControlTransportErrorResult(SourceControlProvider provider, string operationName)
        => Results.Json(
            new { error = BuildSourceControlTransportErrorMessage(provider, operationName) },
            statusCode: StatusCodes.Status503ServiceUnavailable);

    private static string BuildSourceControlTransportErrorMessage(SourceControlProvider provider, string operationName)
        => provider switch
        {
            SourceControlProvider.GitHub => $"GitHub {operationName} failed. Unable to reach GitHub over HTTPS.",
            SourceControlProvider.AzureDevOpsServer or SourceControlProvider.AzureDevOpsServices => $"Azure DevOps {operationName} failed. Unable to reach the server over HTTPS.",
            _ => $"Source control {operationName} failed due to a network error."
        };

    private static async Task WriteServerSentEventAsync(HttpResponse response, string eventName, object payload, JsonSerializerOptions serializerOptions, CancellationToken cancellationToken)
    {
        string json = JsonSerializer.Serialize(payload, serializerOptions);
        await response.WriteAsync($"event: {eventName}\n", cancellationToken);
        await response.WriteAsync($"data: {json}\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

    private static bool IsSafeRunId(string runId)
        => !string.IsNullOrWhiteSpace(runId)
            && runId.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }) < 0
            && !runId.Contains("..");

    private static bool IsKnownWorkspacePath(string workspacePath, IProjectWorkspaceCatalog projectCatalog)
    {
        string normalized = Path.GetFullPath(Environment.ExpandEnvironmentVariables(workspacePath))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return projectCatalog.GetProjects()
            .Any(p => string.Equals(p.WorkspacePath, normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static bool RequiresPersonalAccessTokenForConnectionTest(SourceControlProvider provider)
        => provider is not SourceControlProvider.GitHub;

    private static string? NormalizeText(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Dictionary<string, string[]> ValidatePullRequestLookupRequest(string providerName, string? pullRequestId, string? project, string? repository, string? author)
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

    private static void ValidateRequiredRouteValue(
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

    private static void ValidateOptionalLookupValue(
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

    private static void ValidateNormalizedLookupValue(
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

    private static void AddLookupValidationError(IDictionary<string, List<string>> errors, string key, string message)
    {
        if (!errors.TryGetValue(key, out List<string>? messages))
        {
            messages = new List<string>();
            errors[key] = messages;
        }

        messages.Add(message);
    }

    private static bool ContainsControlCharacters(string value)
        => value.Any(char.IsControl);

    private static string? NormalizeRouteValue(string? value)
        => NormalizeText(value);

    private static string? NormalizeFilterValue(string? value)
        => NormalizeText(value);

    private static string? NormalizePullRequestId(string? value)
        => NormalizeText(value);

    private static async Task<ProviderConnectionSettings?> FindProviderByDisplayNameAsync(IProviderConnectionCatalog providerCatalog, string? providerName)
    {
        string? normalizedProviderName = NormalizeText(providerName);
        return string.IsNullOrWhiteSpace(normalizedProviderName)
            ? null
            : (await providerCatalog.GetProvidersAsync())
                .FirstOrDefault(provider => string.Equals(provider.DisplayName, normalizedProviderName, StringComparison.OrdinalIgnoreCase));
    }

    private static GitAuthenticationOptions? BuildGitAuthenticationOptions(ProviderConnectionSettings? providerSettings)
    {
        if (providerSettings is null || string.IsNullOrWhiteSpace(providerSettings.PersonalAccessToken))
        {
            return null;
        }

        string username = providerSettings.Provider == SourceControlProvider.GitHub
            ? "x-access-token"
            : "pat";
        return new GitAuthenticationOptions(username, providerSettings.PersonalAccessToken.Trim());
    }
}

internal sealed record PullRequestLookupContext(string ProviderName, string? Project, string? Repository, string? Author);

