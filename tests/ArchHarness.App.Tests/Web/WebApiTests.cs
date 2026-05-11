using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ArchHarness.App.Constants;
using ArchHarness.App.Core;
using ArchHarness.App.SourceControl;
using ArchHarness.App.Storage;

namespace ArchHarness.App.Tests.Web;

public sealed class WebApiTests
{
    /// <summary>
    /// BootstrapEndpoint — ReportsWhetherGitHubOAuthIsEnabled
    /// </summary>
    [Fact]
    public async Task BootstrapEndpoint_ReportsWhetherGitHubOAuthIsEnabled()
    {
        using TestWebApplicationFactory factory = new TestWebApplicationFactory();
        factory.ConfigureGitHubOAuth(isEnabled: true);
        using HttpClient client = factory.CreateClient();

        JsonDocument document = JsonDocument.Parse(await client.GetStringAsync("/api/bootstrap"));

        Assert.True(document.RootElement.GetProperty("gitHubOAuthEnabled").GetBoolean());
    }

    /// <summary>
    /// BootstrapEndpoint — ReflectsGitHubOAuthConfigurationAfterHostBuild
    /// </summary>
    [Fact]
    public async Task BootstrapEndpoint_ReflectsGitHubOAuthConfigurationAfterHostBuild()
    {
        using TestWebApplicationFactory factory = new TestWebApplicationFactory();
        using HttpClient client = factory.CreateClient();

        factory.ConfigureGitHubOAuth(isEnabled: false);

        JsonDocument document = JsonDocument.Parse(await client.GetStringAsync("/api/bootstrap"));

        Assert.False(document.RootElement.GetProperty("gitHubOAuthEnabled").GetBoolean());
    }

    [Fact]
    public async Task WorkflowsEndpoint_IncludesWikiDoc()
    {
        using TestWebApplicationFactory factory = new TestWebApplicationFactory();
        using HttpClient client = factory.CreateClient();

        JsonDocument document = JsonDocument.Parse(await client.GetStringAsync("/api/workflows"));

        Assert.Contains(
            document.RootElement.EnumerateArray().Select(item => item.GetProperty("id").GetString()),
            workflow => string.Equals(workflow, WorkflowNames.WIKIDOC, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// GitHubOAuthDeviceFlowEndpoints — ReturnConfiguredResults
    /// </summary>
    [Fact]
    public async Task GitHubOAuthDeviceFlowEndpoints_ReturnConfiguredResults()
    {
        using TestWebApplicationFactory factory = new TestWebApplicationFactory();
        GitHubOAuthDeviceFlowStartResult startResult = new GitHubOAuthDeviceFlowStartResult(
            "flow-123",
            "WXYZ-1234",
            "https://github.com/login/device",
            DateTimeOffset.Parse("2026-03-20T12:00:00Z", CultureInfo.InvariantCulture),
            7);
        factory.ConfigureGitHubOAuthStartResult(startResult);
        factory.ConfigureGitHubOAuthPollResult(
            "flow-123",
            new GitHubOAuthDeviceFlowPollResult(
                "authorized",
                "Connected to GitHub as octocat.",
                "oauth-token",
                GitHubAuthenticationMode.OAuthDeviceFlow,
                "octocat",
                "repo read:org"));
        using HttpClient client = factory.CreateClient();

        JsonDocument startDocument = JsonDocument.Parse(await (await client.PostAsync("/api/providers/github/oauth/device-flow", null)).Content.ReadAsStringAsync());
        Assert.Equal("flow-123", startDocument.RootElement.GetProperty("flowId").GetString());
        Assert.Equal("WXYZ-1234", startDocument.RootElement.GetProperty("userCode").GetString());

        JsonDocument pollDocument = JsonDocument.Parse(await client.GetStringAsync("/api/providers/github/oauth/device-flow/flow-123"));
        Assert.Equal("authorized", pollDocument.RootElement.GetProperty("status").GetString());
        Assert.Equal("oauth-token", pollDocument.RootElement.GetProperty("accessToken").GetString());
        Assert.Equal("octocat", pollDocument.RootElement.GetProperty("authenticatedUser").GetString());
    }

    /// <summary>
    /// ProjectsEndpoint — CreatesProjectAndReturnsGroupedRuns
    /// </summary>
    [Fact]
    public async Task ProjectsEndpoint_CreatesProjectAndReturnsGroupedRuns()
    {
        using TestWebApplicationFactory factory = new TestWebApplicationFactory();
        using HttpClient client = factory.CreateClient();
        string workspacePath = factory.CreateWorkspace("project-api-workspace");

        HttpResponseMessage createResponse = await client.PostAsJsonAsync("/api/projects", new
        {
            displayName = "API Workspace",
            workspacePath,
            workspaceMode = "new-project",
            permissionHandlerMode = "approve-all",
            architectureReviewMode = true,
            architectureReviewPrompt = "Review project scaffold"
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        string runDirectory = Path.Combine(workspacePath, ".agent-harness", "runs", "20260315T120000000");
        Directory.CreateDirectory(runDirectory);
        await File.WriteAllTextAsync(Path.Combine(runDirectory, "run-log.json"), $$"""
                        {
                            "status": "{{RunStatuses.COMPLETED}}",
                            "projectId": "ignored-by-grouping",
                            "projectName": "API Workspace",
                            "runTitle": "Initial Scaffold"
                        }
                        """);

        JsonDocument document = JsonDocument.Parse(await client.GetStringAsync("/api/projects?maxRunsPerProject=10"));
        JsonElement project = Assert.Single(document.RootElement.EnumerateArray());

        Assert.Equal("API Workspace", project.GetProperty("displayName").GetString());
        Assert.Equal("new-project", project.GetProperty("workspaceMode").GetString());
        JsonElement run = Assert.Single(project.GetProperty("runs").EnumerateArray());
        Assert.Equal("Initial Scaffold", run.GetProperty("runTitle").GetString());
    }

    /// <summary>
    /// ProjectsEndpoint — UsesGenericTitlesForRequestOnlyRuns
    /// </summary>
    [Fact]
    public async Task ProjectsEndpoint_UsesGenericTitlesForRequestOnlyRuns()
    {
        using TestWebApplicationFactory factory = new TestWebApplicationFactory();
        using HttpClient client = factory.CreateClient();
        string workspacePath = factory.CreateWorkspace("project-api-event-workspace");

        HttpResponseMessage createResponse = await client.PostAsJsonAsync("/api/projects", new
        {
            displayName = "Event Workspace",
            workspacePath,
            workspaceMode = "existing-folder",
            permissionHandlerMode = "approve-all",
            architectureReviewMode = false,
            architectureReviewPrompt = (string?)null
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        string runDirectory = Path.Combine(workspacePath, ".agent-harness", "runs", "20260315T121000000");
        Directory.CreateDirectory(runDirectory);
        await File.WriteAllTextAsync(Path.Combine(runDirectory, "events.jsonl"), """
            {"runId":"20260315T121000000","source":"request","message":"Run request received","taskPrompt":"Create the web shell and connect project history"}
            """);

        JsonDocument document = JsonDocument.Parse(await client.GetStringAsync("/api/projects?maxRunsPerProject=10"));
        JsonElement project = Assert.Single(document.RootElement.EnumerateArray());
        JsonElement run = Assert.Single(project.GetProperty("runs").EnumerateArray());

        Assert.Equal("Run request", run.GetProperty("runTitle").GetString());
    }

    /// <summary>
    /// RunEventsEndpoint — ReturnsPersistedReplayEvents
    /// </summary>
    [Fact]
    public async Task RunEventsEndpoint_ReturnsPersistedReplayEvents()
    {
        using TestWebApplicationFactory factory = new TestWebApplicationFactory();
        using HttpClient client = factory.CreateClient();
        string workspacePath = factory.CreateWorkspace("project-api-run-events-workspace");

        HttpResponseMessage createResponse = await client.PostAsJsonAsync("/api/projects", new
        {
            displayName = "Replay Workspace",
            workspacePath,
            workspaceMode = "existing-folder",
            permissionHandlerMode = "approve-all",
            architectureReviewMode = false,
            architectureReviewPrompt = (string?)null
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        string runDirectory = Path.Combine(workspacePath, ".agent-harness", "runs", "20260315T121500000");
        Directory.CreateDirectory(runDirectory);
        await File.WriteAllTextAsync(Path.Combine(runDirectory, "events.jsonl"), """
            {"runId":"20260315T121500000","source":"request","message":"Run request received","taskPrompt":"Show the persisted stream output"}
            {"runId":"20260315T121500000","source":"architecture","kind":"agent-delta","agentId":"architecture","agentRole":"Architecture","message":"Rendered output","contentFormat":"markdown","streamKind":"assistant","title":"Architecture","timestampUtc":"2026-03-15T12:15:01Z"}
            """);

        JsonDocument document = JsonDocument.Parse(await client.GetStringAsync($"/api/runs/20260315T121500000/events?workspacePath={Uri.EscapeDataString(workspacePath)}"));
        JsonElement[] events = document.RootElement.EnumerateArray().ToArray();

        Assert.Equal(2, events.Length);
        Assert.Equal("request", events[0].GetProperty("kind").GetString());
        Assert.Equal("Show the persisted stream output", events[0].GetProperty("taskPrompt").GetString());
        Assert.Equal("agent-delta", events[1].GetProperty("kind").GetString());
        Assert.Equal("architecture", events[1].GetProperty("agentId").GetString());
        Assert.Equal("Rendered output", events[1].GetProperty("message").GetString());
    }

    /// <summary>
    /// RunEventsEndpoint — RedactsSensitiveRequestTaskPrompt
    /// </summary>
    [Fact]
    public async Task RunEventsEndpoint_RedactsSensitiveRequestTaskPrompt()
    {
        using TestWebApplicationFactory factory = new TestWebApplicationFactory();
        using HttpClient client = factory.CreateClient();
        string workspacePath = factory.CreateWorkspace("project-api-redacted-run-events-workspace");

        HttpResponseMessage createResponse = await client.PostAsJsonAsync("/api/projects", new
        {
            displayName = "Replay Workspace",
            workspacePath,
            workspaceMode = "existing-folder",
            permissionHandlerMode = "approve-all",
            architectureReviewMode = false,
            architectureReviewPrompt = (string?)null
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        string runDirectory = Path.Combine(workspacePath, ".agent-harness", "runs", "20260315T121500001");
        Directory.CreateDirectory(runDirectory);
        await File.WriteAllTextAsync(Path.Combine(runDirectory, "events.jsonl"), """
            {"runId":"20260315T121500001","source":"request","message":"Run request received","taskPrompt":"Use github_pat_abcdefghijklmnopqrstuvwxyz123456 with Bearer abc123secret to inspect the repo"}
            """);

        JsonDocument document = JsonDocument.Parse(await client.GetStringAsync($"/api/runs/20260315T121500001/events?workspacePath={Uri.EscapeDataString(workspacePath)}"));
        JsonElement evt = Assert.Single(document.RootElement.EnumerateArray());
        string taskPrompt = evt.GetProperty("taskPrompt").GetString()!;

        Assert.Equal("request", evt.GetProperty("kind").GetString());
        Assert.DoesNotContain("github_pat_", taskPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("abc123secret", taskPrompt, StringComparison.Ordinal);
        Assert.Contains("***REDACTED***", taskPrompt, StringComparison.Ordinal);
    }

    /// <summary>
    /// RunStateAndResumeEndpoints — ExposeResumableRuns
    /// </summary>
    [Fact]
    public async Task RunStateAndResumeEndpoints_ExposeResumableRuns()
    {
        using TestWebApplicationFactory factory = new TestWebApplicationFactory();
        using HttpClient client = factory.CreateClient();
        string workspacePath = factory.CreateWorkspace("project-api-run-state-workspace");

        HttpResponseMessage createResponse = await client.PostAsJsonAsync("/api/projects", new
        {
            displayName = "Resume Workspace",
            workspacePath,
            workspaceMode = "existing-folder",
            permissionHandlerMode = "approve-all",
            architectureReviewMode = false,
            architectureReviewPrompt = (string?)null
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        string runId = "20260315T121700000";
        string runDirectory = Path.Combine(workspacePath, ".agent-harness", "runs", runId);
        Directory.CreateDirectory(runDirectory);
        await File.WriteAllTextAsync(Path.Combine(runDirectory, "run-state.json"), $$"""
                {
                    "runId": "{{runId}}",
                    "runDirectory": "{{runDirectory.Replace("\\", "\\\\")}}",
                    "workspaceRoot": "{{workspacePath.Replace("\\", "\\\\")}}",
                    "status": "{{RunStatuses.RUNNING}}",
                    "phase": "{{RunPhases.EXECUTING_PLAN}}",
                    "startedAtUtc": "2026-03-15T12:17:00Z",
                    "updatedAtUtc": "2026-03-15T12:18:00Z",
                    "request": {
                        "taskPrompt": "Resume the interrupted run",
                        "workspacePath": "{{workspacePath.Replace("\\", "\\\\")}}",
                        "workspaceMode": "existing-folder",
                        "workflow": "auto",
                        "projectName": "Resume Workspace",
                        "projectId": "resume-project",
                        "modelOverrides": null,
                        "buildCommand": null,
                        "permissionHandlerMode": "approve-all",
                        "reviewLoopAgents": {
                            "codingStyleEnabled": true,
                            "securityEnabled": true,
                            "architectureEnabled": true
                        },
                        "architectureLoopMode": false,
                        "architectureLoopPrompt": null,
                        "runTitle": "Resume interrupted run"
                    },
                    "completedStepIds": [1, 2],
                    "reviewIteration": 1,
                    "frontendPlan": "Continue work",
                    "filesTouched": ["src/Program.cs"],
                    "review": { "findings": [], "requiredActions": [] },
                    "securityReview": { "findings": [], "requiredActions": [] },
                    "failureMessage": null
                }
                """);

        JsonDocument stateDocument = JsonDocument.Parse(await client.GetStringAsync($"/api/runs/{runId}/state?workspacePath={Uri.EscapeDataString(workspacePath)}"));
        Assert.True(stateDocument.RootElement.GetProperty("canResume").GetBoolean());
        Assert.Equal(RunPhases.EXECUTING_PLAN, stateDocument.RootElement.GetProperty("phase").GetString());

        HttpResponseMessage resumeResponse = await client.PostAsync($"/api/runs/{runId}/resume?workspacePath={Uri.EscapeDataString(workspacePath)}", null);
        Assert.Equal(HttpStatusCode.Accepted, resumeResponse.StatusCode);

        JsonDocument resumeDocument = JsonDocument.Parse(await resumeResponse.Content.ReadAsStringAsync());
        Assert.Equal(RunStatuses.RESUMING, resumeDocument.RootElement.GetProperty("status").GetString());
        Assert.Equal(runId, resumeDocument.RootElement.GetProperty("runId").GetString());
    }

    [Fact]
    public async Task StartRunEndpoint_AppliesWikiDocDefaults()
    {
        using TestWebApplicationFactory factory = new TestWebApplicationFactory();
        using HttpClient client = factory.CreateClient();
        string workspacePath = factory.CreateWorkspace("wikidoc-run-workspace");

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/runs", new
        {
            taskPrompt = "",
            workspacePath,
            workspaceMode = "existing-folder",
            workflow = WorkflowNames.WIKIDOC,
            projectName = "WikiDoc Workspace"
        });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.NotNull(factory.LastStartedRequest);
        Assert.Equal(DefaultPrompts.WIKIDOC_TASK, factory.LastStartedRequest!.TaskPrompt);
        Assert.Equal(WorkflowNames.WIKIDOC, factory.LastStartedRequest.Workflow);
        Assert.NotNull(factory.LastStartedRequest.ReviewLoopAgents);
        Assert.False(factory.LastStartedRequest.ReviewLoopAgents!.CodingStyleEnabled);
        Assert.False(factory.LastStartedRequest.ReviewLoopAgents.SecurityEnabled);
        Assert.False(factory.LastStartedRequest.ReviewLoopAgents.ArchitectureEnabled);
    }

    /// <summary>
    /// RunStateEndpoint — ExposesPlanningHandoffAvailability
    /// </summary>
    [Fact]
    public async Task RunStateEndpoint_ExposesPlanningHandoffAvailability()
    {
        using TestWebApplicationFactory factory = new TestWebApplicationFactory();
        using HttpClient client = factory.CreateClient();
        string workspacePath = factory.CreateWorkspace("project-api-planning-state-workspace");

        JsonDocument createDocument = JsonDocument.Parse(await (await client.PostAsJsonAsync("/api/projects", new
        {
            displayName = "Planning Workspace",
            workspacePath,
            workspaceMode = "existing-folder",
            permissionHandlerMode = "approve-all",
            architectureReviewMode = false,
            architectureReviewPrompt = (string?)null
        })).Content.ReadAsStringAsync());
        string projectId = createDocument.RootElement.GetProperty("projectId").GetString()!;

        string runId = "20260315T130000000";
        string runDirectory = Path.Combine(workspacePath, ".agent-harness", "runs", runId);
        Directory.CreateDirectory(runDirectory);
        await File.WriteAllTextAsync(Path.Combine(runDirectory, "ExecutionPlan.json"), """
            {
              "steps": [],
              "iterationStrategy": { "maxIterations": 2, "reviewRequired": true },
              "completionCriteria": []
            }
            """);
        await File.WriteAllTextAsync(Path.Combine(runDirectory, "run-state.json"), $$"""
                {
                    "runId": "{{runId}}",
                    "runDirectory": "{{runDirectory.Replace("\\", "\\\\")}}",
                    "workspaceRoot": "{{workspacePath.Replace("\\", "\\\\")}}",
                    "status": "{{RunStatuses.COMPLETED}}",
                    "phase": "{{RunPhases.HANDOFF_READY}}",
                    "startedAtUtc": "2026-03-15T13:00:00Z",
                    "updatedAtUtc": "2026-03-15T13:05:00Z",
                    "request": {
                        "taskPrompt": "Plan the onboarding experience",
                        "workspacePath": "{{workspacePath.Replace("\\", "\\\\")}}",
                        "workspaceMode": "existing-folder",
                        "workflow": "{{WorkflowNames.PLANNING}}",
                        "projectName": "Planning Workspace",
                        "projectId": "{{projectId}}",
                        "modelOverrides": null,
                        "buildCommand": null,
                        "permissionHandlerMode": "approve-all",
                        "reviewLoopAgents": {
                            "codingStyleEnabled": true,
                            "securityEnabled": true,
                            "architectureEnabled": true
                        },
                        "architectureLoopMode": false,
                        "architectureLoopPrompt": null,
                        "runTitle": "Plan onboarding experience",
                        "planningSourceRunId": null
                    },
                    "completedStepIds": [1, 2],
                    "reviewIteration": 0,
                    "frontendPlan": "",
                    "filesTouched": [],
                    "review": { "findings": [], "requiredActions": [] },
                    "securityReview": { "findings": [], "requiredActions": [] },
                    "failureMessage": null,
                    "spec": {
                        "task": "Plan the onboarding experience",
                        "desiredOutcome": "An approved implementation plan",
                        "inScope": [],
                        "outOfScope": [],
                        "constraints": [],
                        "assumptions": [],
                        "acceptanceCriteria": ["Plan approved"],
                        "likelyTouchpoints": [],
                        "openQuestions": [],
                        "decisionNotes": []
                    },
                    "approval": {
                        "decision": "{{PlanApprovalDecisions.APPROVED}}",
                        "decidedAtUtc": "2026-03-15T13:04:00Z",
                        "planHash": "2",
                        "reason": null
                    },
                    "handoffRunId": null
                }
                """);

        JsonDocument stateDocument = JsonDocument.Parse(await client.GetStringAsync($"/api/runs/{runId}/state?workspacePath={Uri.EscapeDataString(workspacePath)}"));

        Assert.Equal(WorkflowNames.PLANNING, stateDocument.RootElement.GetProperty("workflow").GetString());
        Assert.True(stateDocument.RootElement.GetProperty("canHandoff").GetBoolean());
        Assert.False(stateDocument.RootElement.GetProperty("canResume").GetBoolean());
        Assert.Equal(JsonValueKind.Null, stateDocument.RootElement.GetProperty("handoffRunId").ValueKind);
    }

    /// <summary>
    /// PlanningHandoffEndpoint — StartsStandardRunAndUpdatesPlanningState
    /// </summary>
    [Fact]
    public async Task PlanningHandoffEndpoint_StartsStandardRunAndUpdatesPlanningState()
    {
        using TestWebApplicationFactory factory = new TestWebApplicationFactory();
        using HttpClient client = factory.CreateClient();
        string workspacePath = factory.CreateWorkspace("project-api-planning-handoff-workspace");

        JsonDocument createDocument = JsonDocument.Parse(await (await client.PostAsJsonAsync("/api/projects", new
        {
            displayName = "Planning Workspace",
            workspacePath,
            workspaceMode = "existing-folder",
            permissionHandlerMode = "approve-all",
            architectureReviewMode = false,
            architectureReviewPrompt = (string?)null
        })).Content.ReadAsStringAsync());
        string projectId = createDocument.RootElement.GetProperty("projectId").GetString()!;

        string runId = "20260315T131000000";
        string runDirectory = Path.Combine(workspacePath, ".agent-harness", "runs", runId);
        Directory.CreateDirectory(runDirectory);
        await File.WriteAllTextAsync(Path.Combine(runDirectory, "ExecutionPlan.json"), """
            {
              "steps": [
                { "id": 1, "agent": "BackendDeveloper", "objective": "Implement the approved onboarding flow", "dependsOn": [], "languages": null }
              ],
              "iterationStrategy": { "maxIterations": 2, "reviewRequired": true },
              "completionCriteria": ["Implementation started"]
            }
            """);
        await File.WriteAllTextAsync(Path.Combine(runDirectory, "run-state.json"), $$"""
                {
                    "runId": "{{runId}}",
                    "runDirectory": "{{runDirectory.Replace("\\", "\\\\")}}",
                    "workspaceRoot": "{{workspacePath.Replace("\\", "\\\\")}}",
                    "status": "{{RunStatuses.COMPLETED}}",
                    "phase": "{{RunPhases.HANDOFF_READY}}",
                    "startedAtUtc": "2026-03-15T13:10:00Z",
                    "updatedAtUtc": "2026-03-15T13:15:00Z",
                    "request": {
                        "taskPrompt": "Plan the onboarding experience",
                        "workspacePath": "{{workspacePath.Replace("\\", "\\\\")}}",
                        "workspaceMode": "existing-folder",
                        "workflow": "{{WorkflowNames.PLANNING}}",
                        "projectName": "Planning Workspace",
                        "projectId": "{{projectId}}",
                        "modelOverrides": null,
                        "buildCommand": null,
                        "permissionHandlerMode": "approve-all",
                        "reviewLoopAgents": {
                            "codingStyleEnabled": true,
                            "securityEnabled": true,
                            "architectureEnabled": true
                        },
                        "architectureLoopMode": false,
                        "architectureLoopPrompt": null,
                        "runTitle": "Plan onboarding experience",
                        "planningSourceRunId": null
                    },
                    "completedStepIds": [1],
                    "reviewIteration": 0,
                    "frontendPlan": "",
                    "filesTouched": [],
                    "review": { "findings": [], "requiredActions": [] },
                    "securityReview": { "findings": [], "requiredActions": [] },
                    "failureMessage": null,
                    "spec": {
                        "task": "Plan the onboarding experience",
                        "desiredOutcome": "An approved implementation plan",
                        "inScope": [],
                        "outOfScope": [],
                        "constraints": [],
                        "assumptions": [],
                        "acceptanceCriteria": ["Plan approved"],
                        "likelyTouchpoints": [],
                        "openQuestions": [],
                        "decisionNotes": []
                    },
                    "approval": {
                        "decision": "{{PlanApprovalDecisions.APPROVED}}",
                        "decidedAtUtc": "2026-03-15T13:14:00Z",
                        "planHash": "1",
                        "reason": null
                    },
                    "handoffRunId": null
                }
                """);

        HttpResponseMessage handoffResponse = await client.PostAsync($"/api/runs/{runId}/handoff?workspacePath={Uri.EscapeDataString(workspacePath)}", null);

        Assert.Equal(HttpStatusCode.Accepted, handoffResponse.StatusCode);
        JsonDocument handoffDocument = JsonDocument.Parse(await handoffResponse.Content.ReadAsStringAsync());
        Assert.Equal("test-run-001", handoffDocument.RootElement.GetProperty("runId").GetString());
        Assert.Equal(RunStatuses.STARTING, handoffDocument.RootElement.GetProperty("status").GetString());

        JsonDocument updatedState = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(runDirectory, "run-state.json")));
        Assert.Equal("test-run-001", updatedState.RootElement.GetProperty("handoffRunId").GetString());
    }

    /// <summary>
    /// ActiveRunEndpoint — PauseReturnsPausedSnapshot
    /// </summary>
    [Fact]
    public async Task ActiveRunEndpoint_PauseReturnsPausedSnapshot()
    {
        using TestWebApplicationFactory factory = new TestWebApplicationFactory();
        using HttpClient client = factory.CreateClient();
        string workspacePath = factory.CreateWorkspace("project-pause-workspace");

        HttpResponseMessage createResponse = await client.PostAsJsonAsync("/api/projects", new
        {
            displayName = "Pause Workspace",
            workspacePath,
            workspaceMode = "existing-folder",
            permissionHandlerMode = "approve-all",
            architectureReviewMode = false,
            architectureReviewPrompt = (string?)null
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        HttpResponseMessage runResponse = await client.PostAsJsonAsync("/api/runs", new
        {
            taskPrompt = "Pause the current run",
            runTitle = "Pause current run",
            workspacePath,
            workspaceMode = "existing-folder",
            workflow = "auto",
            projectName = "Pause Workspace",
            projectId = "pause-project",
            modelOverrides = (object?)null,
            buildCommand = (string?)null,
            permissionHandlerMode = "approve-all",
            architectureLoopMode = false,
            architectureLoopPrompt = (string?)null
        });

        Assert.Equal(HttpStatusCode.Accepted, runResponse.StatusCode);

        HttpResponseMessage pauseResponse = await client.PostAsync("/api/runs/active/pause", null);
        Assert.Equal(HttpStatusCode.OK, pauseResponse.StatusCode);

        JsonDocument pauseDocument = JsonDocument.Parse(await pauseResponse.Content.ReadAsStringAsync());
        Assert.Equal(RunStatuses.PAUSED, pauseDocument.RootElement.GetProperty("status").GetString());
        Assert.False(pauseDocument.RootElement.GetProperty("isRunning").GetBoolean());
        Assert.Equal("test-run-001", pauseDocument.RootElement.GetProperty("runId").GetString());
    }

    /// <summary>
    /// RunEndpoint — TransitionsNewProjectModeAfterFirstAcceptedRun
    /// </summary>
    [Fact]
    public async Task RunEndpoint_TransitionsNewProjectModeAfterFirstAcceptedRun()
    {
        using TestWebApplicationFactory factory = new TestWebApplicationFactory();
        using HttpClient client = factory.CreateClient();
        string workspacePath = factory.CreateWorkspace("project-transition-workspace");

        JsonDocument createdDocument = JsonDocument.Parse(await (await client.PostAsJsonAsync("/api/projects", new
        {
            displayName = "Transition Workspace",
            workspacePath,
            workspaceMode = "new-project",
            permissionHandlerMode = "approve-all",
            architectureReviewMode = false,
            architectureReviewPrompt = (string?)null
        })).Content.ReadAsStringAsync());
        string projectId = createdDocument.RootElement.GetProperty("projectId").GetString()!;

        HttpResponseMessage runResponse = await client.PostAsJsonAsync("/api/runs", new
        {
            taskPrompt = "Create the initial project structure",
            runTitle = "Create initial project structure",
            workspacePath,
            workspaceMode = "new-project",
            workflow = "auto",
            projectName = "Transition Workspace",
            projectId,
            modelOverrides = (object?)null,
            buildCommand = (string?)null,
            permissionHandlerMode = "approve-all",
            reviewLoopAgents = new
            {
                codingStyleEnabled = true,
                securityEnabled = true,
                architectureEnabled = true
            },
            architectureLoopMode = false,
            architectureLoopPrompt = (string?)null
        });

        Assert.Equal(HttpStatusCode.Accepted, runResponse.StatusCode);

        JsonDocument projectsDocument = JsonDocument.Parse(await client.GetStringAsync("/api/projects?maxRunsPerProject=10"));
        JsonElement project = Assert.Single(projectsDocument.RootElement.EnumerateArray());
        Assert.Equal("existing-folder", project.GetProperty("workspaceMode").GetString());
    }

    /// <summary>
    /// SettingsAndModelsEndpoints — ReturnStructuredGlobalSettingsPayloads
    /// </summary>
    [Fact]
    public async Task SettingsAndModelsEndpoints_ReturnStructuredGlobalSettingsPayloads()
    {
        using TestWebApplicationFactory factory = new TestWebApplicationFactory();
        using HttpClient client = factory.CreateClient();

        JsonDocument settingsDocument = JsonDocument.Parse(await client.GetStringAsync("/api/settings"));
        Assert.Equal(WellKnownModelNames.GPT_5_MINI, settingsDocument.RootElement.GetProperty("agentModels").GetProperty("conversation").GetString());
        Assert.Equal(WellKnownModelNames.GPT_5_4, settingsDocument.RootElement.GetProperty("agentModels").GetProperty("planning").GetString());
        Assert.Equal("xhigh", settingsDocument.RootElement.GetProperty("agentReasoningEfforts").GetProperty("planning").GetString());
        Assert.False(settingsDocument.RootElement.TryGetProperty("sourceControl", out _));

        HttpResponseMessage updateResponse = await client.PutAsJsonAsync("/api/settings", new
        {
            agentModels = new
            {
                conversation = WellKnownModelNames.GPT_5_4,
                orchestration = WellKnownModelNames.CLAUDE_SONNET_4_6,
                planning = WellKnownModelNames.GPT_5_4,
                frontendDeveloper = WellKnownModelNames.CLAUDE_SONNET_4_6,
                backendDeveloper = WellKnownModelNames.GPT_5_3_CODEX,
                build = WellKnownModelNames.GPT_4_1,
                codingStyle = WellKnownModelNames.CLAUDE_OPUS_4_6,
                security = WellKnownModelNames.CLAUDE_OPUS_4_6,
                architecture = WellKnownModelNames.CLAUDE_OPUS_4_6
            },
            agentReasoningEfforts = new
            {
                planning = "high"
            },
            defaults = new
            {
                permissionHandlerMode = "prompt",
                architectureReviewMode = true,
                architectureReviewPrompt = "Review changed boundaries"
            }
        });

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        JsonDocument updatedSettings = JsonDocument.Parse(await updateResponse.Content.ReadAsStringAsync());
        Assert.Equal(WellKnownModelNames.GPT_5_4, updatedSettings.RootElement.GetProperty("agentModels").GetProperty("conversation").GetString());
        Assert.Equal(WellKnownModelNames.GPT_5_4, updatedSettings.RootElement.GetProperty("agentModels").GetProperty("planning").GetString());
        Assert.Equal("high", updatedSettings.RootElement.GetProperty("agentReasoningEfforts").GetProperty("planning").GetString());
        Assert.Equal("prompt", updatedSettings.RootElement.GetProperty("defaults").GetProperty("permissionHandlerMode").GetString());
        Assert.False(updatedSettings.RootElement.TryGetProperty("sourceControl", out _));

        JsonDocument modelsDocument = JsonDocument.Parse(await client.GetStringAsync("/api/models"));
        Assert.Contains(modelsDocument.RootElement.GetProperty("models").EnumerateArray(),
            model => model.GetProperty("modelId").GetString() == WellKnownModelNames.CLAUDE_OPUS_4_6
                && model.GetProperty("costBand").GetString() == "3x");
        Assert.Contains(modelsDocument.RootElement.GetProperty("models").EnumerateArray(),
            model => model.GetProperty("modelId").GetString() == WellKnownModelNames.GPT_5_4
                && model.GetProperty("defaultReasoningEffort").GetString() == "medium"
                && model.GetProperty("supportedReasoningEfforts").EnumerateArray().Select(value => value.GetString()).SequenceEqual(new[] { "low", "medium", "high", "xhigh" }));
    }

    /// <summary>
    /// ProvidersEndpoints — SaveListAndDeleteConfiguredProviders
    /// </summary>
    [Fact]
    public async Task ProvidersEndpoints_SaveListAndDeleteConfiguredProviders()
    {
        using TestWebApplicationFactory factory = new TestWebApplicationFactory();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage saveResponse = await client.PostAsJsonAsync("/api/providers", new
        {
            provider = (int)SourceControlProvider.AzureDevOpsServices,
            displayName = "Contoso Cloud",
            serverUrl = (string?)null,
            organization = "contoso",
            personalAccessToken = "secret-pat",
            isEnabled = true
        });

        Assert.Equal(HttpStatusCode.OK, saveResponse.StatusCode);

        string savePayload = await saveResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("secret-pat", savePayload);
        JsonDocument savedDocument = JsonDocument.Parse(savePayload);
        Assert.Equal("Contoso Cloud", savedDocument.RootElement.GetProperty("displayName").GetString());
        Assert.Equal(JsonValueKind.Null, savedDocument.RootElement.GetProperty("personalAccessToken").ValueKind);
        Assert.Equal((int)PersonalAccessTokenStorageMode.Protected, savedDocument.RootElement.GetProperty("personalAccessTokenStorageMode").GetInt32());

        string providersPayload = await client.GetStringAsync("/api/providers");
        Assert.DoesNotContain("secret-pat", providersPayload);

        JsonDocument providersDocument = JsonDocument.Parse(providersPayload);
        JsonElement configuredProvider = Assert.Single(providersDocument.RootElement.EnumerateArray());
        Assert.Equal("contoso", configuredProvider.GetProperty("organization").GetString());

        HttpResponseMessage deleteResponse = await client.DeleteAsync("/api/providers/Contoso%20Cloud");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        JsonDocument emptyDocument = JsonDocument.Parse(await client.GetStringAsync("/api/providers"));
        Assert.Empty(emptyDocument.RootElement.EnumerateArray());
    }

    /// <summary>
    /// ProvidersEndpoint — ReturnsValidationProblemForMissingAzureDevOpsServerUrl
    /// </summary>
    [Fact]
    public async Task ProvidersEndpoint_ReturnsValidationProblemForMissingAzureDevOpsServerUrl()
    {
        using TestWebApplicationFactory factory = new TestWebApplicationFactory();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/providers", new
        {
            provider = (int)SourceControlProvider.AzureDevOpsServer,
            displayName = "Contoso On Prem",
            serverUrl = (string?)null,
            organization = "DefaultCollection",
            personalAccessToken = "ado-pat",
            isEnabled = true
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        string payload = await response.Content.ReadAsStringAsync();
        Assert.Contains("serverUrl", payload);
    }

    /// <summary>
    /// ProvidersTestEndpoint — ReturnsValidationProblemWhenPatLooksLikeUrl
    /// </summary>
    [Fact]
    public async Task ProvidersTestEndpoint_ReturnsValidationProblemWhenPatLooksLikeUrl()
    {
        using TestWebApplicationFactory factory = new TestWebApplicationFactory();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/providers/test", new
        {
            provider = (int)SourceControlProvider.AzureDevOpsServer,
            displayName = "Contoso On Prem",
            serverUrl = "https://ado.example.com",
            organization = "DefaultCollection",
            personalAccessToken = "https://ado.example.com",
            isEnabled = true
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        string payload = await response.Content.ReadAsStringAsync();
        Assert.Contains("PersonalAccessToken looks like a URL", payload);
    }

    /// <summary>
    /// ProvidersTestEndpoint — ReturnsConnectionResult
    /// </summary>
    [Fact]
    public async Task ProvidersTestEndpoint_ReturnsConnectionResult()
    {
        using TestWebApplicationFactory factory = new TestWebApplicationFactory();
        factory.ConfigureGitHubResponse((request, _) =>
        {
            Assert.Equal("https://api.github.com/user", request.RequestUri?.ToString());
            Assert.NotNull(request.Headers.Authorization);
            Assert.Equal("token", request.Headers.Authorization!.Scheme);
            Assert.Equal("github-pat", request.Headers.Authorization.Parameter);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{ "login": "octocat" }""", Encoding.UTF8, "application/json")
            };
        });
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/providers/test", new
        {
            provider = (int)SourceControlProvider.GitHub,
            displayName = "GitHub",
            serverUrl = (string?)null,
            organization = "octo-org",
            personalAccessToken = "github-pat",
            isEnabled = true
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("Successfully connected to GitHub.", document.RootElement.GetProperty("message").GetString());
    }

    /// <summary>
    /// ProvidersTestEndpoint — AllowsGitHubWithoutPersonalAccessToken
    /// </summary>
    [Fact]
    public async Task ProvidersTestEndpoint_AllowsGitHubWithoutPersonalAccessToken()
    {
        using TestWebApplicationFactory factory = new TestWebApplicationFactory();
        factory.ConfigureGitHubResponse((request, _) =>
        {
            Assert.Equal("https://api.github.com/orgs/octo-org", request.RequestUri?.ToString());
            Assert.Null(request.Headers.Authorization);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{ \"login\": \"octo-org\" }""", Encoding.UTF8, "application/json")
            };
        });
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/providers/test", new
        {
            provider = (int)SourceControlProvider.GitHub,
            displayName = "GitHub",
            serverUrl = (string?)null,
            organization = "octo-org",
            personalAccessToken = (string?)null,
            isEnabled = true
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("Successfully connected to GitHub.", document.RootElement.GetProperty("message").GetString());
    }

    /// <summary>
    /// ProvidersTestEndpoint — ReturnsHelpfulMessageForUnauthenticatedGitHubUserConnectionFailure
    /// </summary>
    [Fact]
    public async Task ProvidersTestEndpoint_ReturnsHelpfulMessageForUnauthenticatedGitHubUserConnectionFailure()
    {
        using TestWebApplicationFactory factory = new TestWebApplicationFactory();
        factory.ConfigureGitHubResponse((request, _) =>
        {
            Assert.Equal("https://api.github.com/users/octocat", request.RequestUri?.ToString());
            Assert.Null(request.Headers.Authorization);
            return new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("""{ "message": "API rate limit exceeded" }""", Encoding.UTF8, "application/json")
            };
        });
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/providers/test", new
        {
            provider = (int)SourceControlProvider.GitHub,
            displayName = "GitHub",
            serverUrl = (string?)null,
            organization = "octocat",
            gitHubOwnerType = (int)GitHubOwnerType.User,
            personalAccessToken = (string?)null,
            isEnabled = true
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("failed without authentication", document.RootElement.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("authentication was rejected", document.RootElement.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ProvidersEndpoint — ReturnsConflictWhenProtectedStorageIsUnavailable
    /// </summary>
    [Fact]
    public async Task ProvidersEndpoint_ReturnsConflictWhenProtectedStorageIsUnavailable()
    {
        using TestWebApplicationFactory factory = new TestWebApplicationFactory();
        factory.SetSecureTokenStorageAvailable(false);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/providers", new
        {
            provider = (int)SourceControlProvider.GitHub,
            displayName = "GitHub",
            serverUrl = (string?)null,
            organization = "octo-org",
            personalAccessToken = "github-pat",
            personalAccessTokenStorageMode = (int)PersonalAccessTokenStorageMode.Protected,
            isEnabled = true
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("pat-protection-unavailable", document.RootElement.GetProperty("code").GetString());
        Assert.Contains("secure", document.RootElement.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", document.RootElement.GetProperty("warning").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ProvidersEndpoint — AllowsPlainTextStorageSelection
    /// </summary>
    [Fact]
    public async Task ProvidersEndpoint_AllowsPlainTextStorageSelection()
    {
        using TestWebApplicationFactory factory = new TestWebApplicationFactory();
        factory.SetSecureTokenStorageAvailable(false);
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/providers", new
        {
            provider = (int)SourceControlProvider.GitHub,
            displayName = "GitHub",
            serverUrl = (string?)null,
            organization = "octo-org",
            personalAccessToken = "github-pat",
            personalAccessTokenStorageMode = (int)PersonalAccessTokenStorageMode.PlainText,
            isEnabled = true
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonDocument providers = JsonDocument.Parse(await client.GetStringAsync("/api/providers"));
        JsonElement savedProvider = Assert.Single(providers.RootElement.EnumerateArray());
        Assert.Equal((int)PersonalAccessTokenStorageMode.PlainText, savedProvider.GetProperty("personalAccessTokenStorageMode").GetInt32());
    }

    /// <summary>
    /// PullRequestsEndpoint — ReturnsBadRequestWhenSourceControlIsNotConfigured
    /// </summary>
    [Fact]
    public async Task PullRequestsEndpoint_ReturnsBadRequestWhenSourceControlIsNotConfigured()
    {
        using TestWebApplicationFactory factory = new TestWebApplicationFactory();
        using HttpClient client = factory.CreateClient();

        string workspacePath = factory.CreateWorkspace("project-without-source-control");
        JsonDocument createdProject = JsonDocument.Parse(await (await client.PostAsJsonAsync("/api/projects", new
        {
            displayName = "Workspace Without Source Control",
            workspacePath,
            workspaceMode = "existing-folder",
            permissionHandlerMode = "approve-all",
            architectureReviewMode = false,
            architectureReviewPrompt = (string?)null
        })).Content.ReadAsStringAsync());
        string projectId = createdProject.RootElement.GetProperty("projectId").GetString()!;

        HttpResponseMessage response = await client.GetAsync($"/api/projects/{projectId}/pullrequests");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// PullRequestsEndpoint — ReturnsConfiguredProviderPullRequests
    /// </summary>
    [Fact]
    public async Task PullRequestsEndpoint_ReturnsConfiguredProviderPullRequests()
    {
        using TestWebApplicationFactory factory = new TestWebApplicationFactory();
        await factory.SeedProviderConnectionsAsync(new ProviderConnectionSettings
        {
            Provider = SourceControlProvider.GitHub,
            DisplayName = "GitHub",
            Organization = "octo-org",
            PersonalAccessToken = "github-pat",
            IsEnabled = true
        });
        factory.ConfigureGitHubResponse((request, _) =>
        {
            Assert.Equal("https://api.github.com/repos/octo-org/archharness/pulls?state=open&per_page=100&page=1", request.RequestUri?.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    [
                      {
                        "number": 11,
                        "title": "Expose source control API",
                        "user": {
                          "login": "octocat"
                        },
                        "head": {
                          "ref": "feature/source-control"
                        },
                        "base": {
                          "ref": "main"
                        },
                        "state": "open",
                        "draft": false,
                        "html_url": "https://github.com/octo-org/archharness/pull/11",
                        "created_at": "2026-03-17T14:45:00Z"
                      }
                    ]
                    """, Encoding.UTF8, "application/json")
            };
        });
        using HttpClient client = factory.CreateClient();

        string workspacePath = factory.CreateWorkspace("project-with-source-control");
        JsonDocument createdProject = JsonDocument.Parse(await (await client.PostAsJsonAsync("/api/projects", new
        {
            displayName = "Workspace With Source Control",
            workspacePath,
            workspaceMode = "existing-folder",
            permissionHandlerMode = "approve-all",
            architectureReviewMode = false,
            architectureReviewPrompt = (string?)null
        })).Content.ReadAsStringAsync());
        string projectId = createdProject.RootElement.GetProperty("projectId").GetString()!;

        HttpResponseMessage configureResponse = await client.PutAsJsonAsync($"/api/projects/{projectId}/source-control", new
        {
            providerName = "GitHub",
            projectName = (string?)null,
            repositoryName = "archharness"
        });
        Assert.Equal(HttpStatusCode.OK, configureResponse.StatusCode);

        HttpResponseMessage response = await client.GetAsync($"/api/projects/{projectId}/pullrequests");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement pullRequest = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal("11", pullRequest.GetProperty("id").GetString());
        Assert.Equal("octocat", pullRequest.GetProperty("author").GetString());
        Assert.Equal("feature/source-control", pullRequest.GetProperty("sourceBranch").GetString());
        Assert.Equal("octo-org", pullRequest.GetProperty("projectName").GetString());
        Assert.Equal("archharness", pullRequest.GetProperty("repositoryName").GetString());
    }

    /// <summary>
    /// ProviderPullRequestsEndpoint — ReturnsFilteredProviderPullRequests
    /// </summary>
    [Fact]
    public async Task ProviderPullRequestsEndpoint_ReturnsFilteredProviderPullRequests()
    {
        using TestWebApplicationFactory factory = new TestWebApplicationFactory();
        await factory.SeedProviderConnectionsAsync(new ProviderConnectionSettings
        {
            Provider = SourceControlProvider.GitHub,
            DisplayName = "GitHub",
            Organization = "octo-org",
            PersonalAccessToken = "github-pat",
            IsEnabled = true
        });
        factory.ConfigureGitHubResponse((request, _) =>
        {
            string requestUri = request.RequestUri?.ToString() ?? string.Empty;
            if (requestUri == "https://api.github.com/orgs/octo-org/repos?type=all&per_page=100&page=1")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        [
                          { "name": "archharness" },
                          { "name": "other-repo" }
                        ]
                        """, Encoding.UTF8, "application/json")
                };
            }

            if (requestUri == "https://api.github.com/repos/octo-org/archharness/pulls?state=open&per_page=100&page=1")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        [
                          {
                            "number": 21,
                            "title": "Add review PR backend",
                            "user": {
                              "login": "octocat"
                            },
                            "head": {
                              "ref": "feature/review-pr"
                            },
                            "base": {
                              "ref": "main"
                            },
                            "state": "open",
                            "draft": false,
                            "html_url": "https://github.com/octo-org/archharness/pull/21",
                            "created_at": "2026-03-18T09:00:00Z"
                          }
                        ]
                        """, Encoding.UTF8, "application/json")
                };
            }

            throw new Xunit.Sdk.XunitException($"Unexpected GitHub request URI: {requestUri}");
        });
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/providers/GitHub/pullrequests?repository=archharness&author=octo");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement pullRequest = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal("21", pullRequest.GetProperty("id").GetString());
        Assert.Equal("octo-org", pullRequest.GetProperty("projectName").GetString());
        Assert.Equal("archharness", pullRequest.GetProperty("repositoryName").GetString());
    }

    /// <summary>
    /// ProviderPullRequestsEndpoint — UsesGitHubUserRepositoriesWhenConfigured
    /// </summary>
    [Fact]
    public async Task ProviderPullRequestsEndpoint_UsesGitHubUserRepositoriesWhenConfigured()
    {
        using TestWebApplicationFactory factory = new TestWebApplicationFactory();
        await factory.SeedProviderConnectionsAsync(new ProviderConnectionSettings
        {
            Provider = SourceControlProvider.GitHub,
            DisplayName = "GitHub",
            Organization = "octocat",
            GitHubOwnerType = GitHubOwnerType.User,
            PersonalAccessToken = null,
            IsEnabled = true
        });
        factory.ConfigureGitHubResponse((request, _) =>
        {
            string requestUri = request.RequestUri?.ToString() ?? string.Empty;
            if (requestUri == "https://api.github.com/users/octocat/repos?type=all&per_page=100&page=1")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        [
                          { "name": "archharness" }
                        ]
                        """, Encoding.UTF8, "application/json")
                };
            }

            if (requestUri == "https://api.github.com/repos/octocat/archharness/pulls?state=open&per_page=100&page=1")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        [
                          {
                            "number": 21,
                            "title": "Fix review PR flow",
                            "user": {
                              "login": "octocat"
                            },
                            "head": {
                              "ref": "feature/review-pr"
                            },
                            "base": {
                              "ref": "main"
                            },
                            "state": "open",
                            "draft": false,
                            "html_url": "https://github.com/octocat/archharness/pull/21",
                            "created_at": "2026-03-18T09:00:00Z"
                          }
                        ]
                        """, Encoding.UTF8, "application/json")
                };
            }

            throw new Xunit.Sdk.XunitException($"Unexpected GitHub request URI: {requestUri}");
        });
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/providers/GitHub/pullrequests");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement pullRequest = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal("21", pullRequest.GetProperty("id").GetString());
        Assert.Equal("octocat", pullRequest.GetProperty("projectName").GetString());
        Assert.Equal("archharness", pullRequest.GetProperty("repositoryName").GetString());
    }

    /// <summary>
    /// ProvidersEndpoint — PreservesGitHubPersonalAccessTokenWhenSavedBlank
    /// </summary>
    [Fact]
    public async Task ProvidersEndpoint_PreservesGitHubPersonalAccessTokenWhenSavedBlank()
    {
        using TestWebApplicationFactory factory = new TestWebApplicationFactory();
        await factory.SeedProviderConnectionsAsync(new ProviderConnectionSettings
        {
            Provider = SourceControlProvider.GitHub,
            DisplayName = "GitHub",
            Organization = "octo-org",
            PersonalAccessToken = "github-pat",
            IsEnabled = true
        });
        factory.ConfigureGitHubResponse((request, _) =>
        {
            string requestUri = request.RequestUri?.ToString() ?? string.Empty;
            Assert.NotNull(request.Headers.Authorization);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("github-pat", request.Headers.Authorization.Parameter);

            if (requestUri == "https://api.github.com/orgs/octo-org/repos?type=all&per_page=100&page=1")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        [
                          { "name": "archharness" }
                        ]
                        """, Encoding.UTF8, "application/json")
                };
            }

            if (requestUri == "https://api.github.com/repos/octo-org/archharness/pulls?state=open&per_page=100&page=1")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        [
                          {
                            "number": 21,
                            "title": "Public repo review",
                            "user": {
                              "login": "octocat"
                            },
                            "head": {
                              "ref": "feature/review-pr"
                            },
                            "base": {
                              "ref": "main"
                            },
                            "state": "open",
                            "draft": false,
                            "html_url": "https://github.com/octo-org/archharness/pull/21",
                            "created_at": "2026-03-18T09:00:00Z"
                          }
                        ]
                        """, Encoding.UTF8, "application/json")
                };
            }

            throw new Xunit.Sdk.XunitException($"Unexpected GitHub request URI: {requestUri}");
        });
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage saveResponse = await client.PostAsJsonAsync("/api/providers", new
        {
            provider = (int)SourceControlProvider.GitHub,
            displayName = "GitHub",
            serverUrl = (string?)null,
            organization = "octo-org",
            personalAccessToken = (string?)null,
            isEnabled = true
        });

        Assert.Equal(HttpStatusCode.OK, saveResponse.StatusCode);

        HttpResponseMessage response = await client.GetAsync("/api/providers/GitHub/pullrequests?repository=archharness");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement pullRequest = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal("21", pullRequest.GetProperty("id").GetString());
    }

    /// <summary>
    /// ProvidersEndpoint — ClearsGitHubPersonalAccessTokenWhenRequested
    /// </summary>
    [Fact]
    public async Task ProvidersEndpoint_ClearsGitHubPersonalAccessTokenWhenRequested()
    {
        using TestWebApplicationFactory factory = new TestWebApplicationFactory();
        await factory.SeedProviderConnectionsAsync(new ProviderConnectionSettings
        {
            Provider = SourceControlProvider.GitHub,
            DisplayName = "GitHub",
            Organization = "octo-org",
            PersonalAccessToken = "github-pat",
            IsEnabled = true
        });
        factory.ConfigureGitHubResponse((request, _) =>
        {
            string requestUri = request.RequestUri?.ToString() ?? string.Empty;
            Assert.Null(request.Headers.Authorization);

            if (requestUri == "https://api.github.com/orgs/octo-org/repos?type=all&per_page=100&page=1")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                                                [
                                                    { "name": "archharness" }
                                                ]
                                                """, Encoding.UTF8, "application/json")
                };
            }

            if (requestUri == "https://api.github.com/repos/octo-org/archharness/pulls?state=open&per_page=100&page=1")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                                                [
                                                    {
                                                        "number": 21,
                                                        "title": "Public repo review",
                                                        "user": {
                                                            "login": "octocat"
                                                        },
                                                        "head": {
                                                            "ref": "feature/review-pr"
                                                        },
                                                        "base": {
                                                            "ref": "main"
                                                        },
                                                        "state": "open",
                                                        "draft": false,
                                                        "html_url": "https://github.com/octo-org/archharness/pull/21",
                                                        "created_at": "2026-03-18T09:00:00Z"
                                                    }
                                                ]
                                                """, Encoding.UTF8, "application/json")
                };
            }

            throw new Xunit.Sdk.XunitException($"Unexpected GitHub request URI: {requestUri}");
        });
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage saveResponse = await client.PostAsJsonAsync("/api/providers", new
        {
            provider = (int)SourceControlProvider.GitHub,
            displayName = "GitHub",
            serverUrl = (string?)null,
            organization = "octo-org",
            personalAccessToken = (string?)null,
            clearPersonalAccessToken = true,
            isEnabled = true
        });

        Assert.Equal(HttpStatusCode.OK, saveResponse.StatusCode);

        JsonDocument savedDocument = JsonDocument.Parse(await saveResponse.Content.ReadAsStringAsync());
        Assert.False(savedDocument.RootElement.GetProperty("hasStoredPersonalAccessToken").GetBoolean());

        HttpResponseMessage response = await client.GetAsync("/api/providers/GitHub/pullrequests?repository=archharness");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement pullRequest = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal("21", pullRequest.GetProperty("id").GetString());
    }

    /// <summary>
    /// ProviderPullRequestsEndpoint — UsesAzureDevOpsProjectFilterWithoutListingAllProjects
    /// </summary>
    [Fact]
    public async Task ProviderPullRequestsEndpoint_UsesAzureDevOpsProjectFilterWithoutListingAllProjects()
    {
        using TestWebApplicationFactory factory = new TestWebApplicationFactory();
        await factory.SeedProviderConnectionsAsync(new ProviderConnectionSettings
        {
            Provider = SourceControlProvider.AzureDevOpsServer,
            DisplayName = "Carpenters",
            ServerUrl = "https://devops.carpenters-law.co.uk",
            Organization = "DefaultCollection",
            PersonalAccessToken = "ado-pat",
            IsEnabled = true
        });
        factory.ConfigureAzureDevOpsResponse((request, _) =>
        {
            string requestUri = request.RequestUri?.ToString() ?? string.Empty;
            if (requestUri == "https://devops.carpenters-law.co.uk/DefaultCollection/Harness Project/_apis/git/repositories?api-version=7.0")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        {
                          "value": [
                            { "name": "ArchHarness Repo" }
                          ]
                        }
                        """, Encoding.UTF8, "application/json")
                };
            }

            if (requestUri == "https://devops.carpenters-law.co.uk/DefaultCollection/Harness Project/_apis/git/repositories/ArchHarness Repo/pullrequests?api-version=7.0&searchCriteria.status=active")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        {
                          "value": [
                            {
                              "pullRequestId": 11,
                              "title": "Visible PR",
                              "createdBy": {
                                "displayName": "Dana"
                              },
                              "sourceRefName": "refs/heads/feature/visible-pr",
                              "targetRefName": "refs/heads/main",
                              "status": "active",
                              "creationDate": "2026-03-18T09:00:00Z",
                              "_links": {
                                "web": {
                                  "href": "https://devops.carpenters-law.co.uk/pr/11"
                                }
                              }
                            }
                          ]
                        }
                        """, Encoding.UTF8, "application/json")
                };
            }

            if (requestUri == "https://devops.carpenters-law.co.uk/DefaultCollection/_apis/projects?api-version=6.0")
            {
                throw new Xunit.Sdk.XunitException("Projects endpoint should not be called when the project filter is supplied.");
            }

            throw new Xunit.Sdk.XunitException($"Unexpected Azure DevOps request URI: {requestUri}");
        });
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/providers/Carpenters/pullrequests?project=Harness%20Project");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement pullRequest = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal("11", pullRequest.GetProperty("id").GetString());
        Assert.Equal("Harness Project", pullRequest.GetProperty("projectName").GetString());
        Assert.Equal("ArchHarness Repo", pullRequest.GetProperty("repositoryName").GetString());
    }

    /// <summary>
    /// ProviderPullRequestsEndpoint — ReturnsValidationProblemForInvalidProviderName
    /// </summary>
    [Fact]
    public async Task ProviderPullRequestsEndpoint_ReturnsValidationProblemForInvalidProviderName()
    {
        using TestWebApplicationFactory factory = new TestWebApplicationFactory();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/providers/GitHub%5C..%5Cevil/pullrequests");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string payload = await response.Content.ReadAsStringAsync();
        Assert.Contains("providerName", payload);
    }

    /// <summary>
    /// ProviderPullRequestsEndpoint — ReturnsValidationProblemForInvalidFilter
    /// </summary>
    [Fact]
    public async Task ProviderPullRequestsEndpoint_ReturnsValidationProblemForInvalidFilter()
    {
        using TestWebApplicationFactory factory = new TestWebApplicationFactory();
        await factory.SeedProviderConnectionsAsync(new ProviderConnectionSettings
        {
            Provider = SourceControlProvider.GitHub,
            DisplayName = "GitHub",
            Organization = "octo-org",
            PersonalAccessToken = "github-pat",
            IsEnabled = true
        });
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/providers/GitHub/pullrequests?author=octo%0Acat");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string payload = await response.Content.ReadAsStringAsync();
        Assert.Contains("author", payload);
    }

    /// <summary>
    /// ProviderPullRequestsEndpoint — ReturnsUnauthorizedWhenProviderAuthenticationIsRejected
    /// </summary>
    [Fact]
    public async Task ProviderPullRequestsEndpoint_ReturnsUnauthorizedWhenProviderAuthenticationIsRejected()
    {
        using TestWebApplicationFactory factory = new TestWebApplicationFactory();
        await factory.SeedProviderConnectionsAsync(new ProviderConnectionSettings
        {
            Provider = SourceControlProvider.AzureDevOpsServer,
            DisplayName = "Carpenters",
            ServerUrl = "https://devops.carpenters-law.co.uk",
            Organization = "DefaultCollection",
            PersonalAccessToken = "ado-pat",
            IsEnabled = true
        });
        factory.ConfigureAzureDevOpsResponse((request, _) =>
        {
            Assert.Equal("https://devops.carpenters-law.co.uk/DefaultCollection/_apis/projects?api-version=6.0", request.RequestUri?.ToString());
            Assert.NotNull(request.Headers.Authorization);
            Assert.Equal("Basic", request.Headers.Authorization!.Scheme);
            Assert.Equal(Convert.ToBase64String(Encoding.ASCII.GetBytes(":ado-pat")), request.Headers.Authorization.Parameter);
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        });
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/providers/Carpenters/pullrequests");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        string payload = await response.Content.ReadAsStringAsync();
        Assert.Contains("authentication was rejected", payload, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ProviderPullRequestsStreamEndpoint — ReturnsFriendlyErrorWhenTransportFails
    /// </summary>
    [Fact]
    public async Task ProviderPullRequestsStreamEndpoint_ReturnsFriendlyErrorWhenTransportFails()
    {
        using TestWebApplicationFactory factory = new TestWebApplicationFactory();
        await factory.SeedProviderConnectionsAsync(new ProviderConnectionSettings
        {
            Provider = SourceControlProvider.AzureDevOpsServer,
            DisplayName = "Carpenters",
            ServerUrl = "https://devops.carpenters-law.co.uk",
            Organization = "DefaultCollection",
            PersonalAccessToken = "ado-pat",
            IsEnabled = true
        });
        factory.ConfigureAzureDevOpsResponse((_, _) => throw new HttpRequestException("The response ended prematurely."));
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/providers/Carpenters/pullrequests/stream", HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        string payload = await response.Content.ReadAsStringAsync();
        Assert.Contains("Azure DevOps pull request retrieval failed. Unable to reach the server over HTTPS.", payload, StringComparison.Ordinal);
    }

    /// <summary>
    /// ProviderPullRequestsStreamEndpoint — ReturnsServerSentEvents
    /// </summary>
    [Fact]
    public async Task ProviderPullRequestsStreamEndpoint_ReturnsServerSentEvents()
    {
        using TestWebApplicationFactory factory = new TestWebApplicationFactory();
        await factory.SeedProviderConnectionsAsync(new ProviderConnectionSettings
        {
            Provider = SourceControlProvider.GitHub,
            DisplayName = "GitHub",
            Organization = "octo-org",
            PersonalAccessToken = "github-pat",
            IsEnabled = true
        });
        factory.ConfigureGitHubResponse((request, _) =>
        {
            string requestUri = request.RequestUri?.ToString() ?? string.Empty;
            if (requestUri == "https://api.github.com/orgs/octo-org/repos?type=all&per_page=100&page=1")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        [
                          { "name": "archharness" }
                        ]
                        """, Encoding.UTF8, "application/json")
                };
            }

            if (requestUri == "https://api.github.com/repos/octo-org/archharness/pulls?state=open&per_page=100&page=1")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                        [
                          {
                            "number": 21,
                            "title": "Add review PR backend",
                            "user": {
                              "login": "octocat"
                            },
                            "head": {
                              "ref": "feature/review-pr"
                            },
                            "base": {
                              "ref": "main"
                            },
                            "state": "open",
                            "draft": false,
                            "html_url": "https://github.com/octo-org/archharness/pull/21",
                            "created_at": "2026-03-18T09:00:00Z"
                          }
                        ]
                        """, Encoding.UTF8, "application/json")
                };
            }

            throw new Xunit.Sdk.XunitException($"Unexpected GitHub request URI: {requestUri}");
        });
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/providers/GitHub/pullrequests/stream", HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        string payload = await response.Content.ReadAsStringAsync();
        Assert.Contains("event: batch", payload, StringComparison.Ordinal);
        Assert.Contains("event: completed", payload, StringComparison.Ordinal);
        Assert.Contains("\"id\":\"21\"", payload, StringComparison.Ordinal);
    }

    /// <summary>
    /// ProviderPullRequestFilesEndpoint — ReturnsFilesForPullRequest
    /// </summary>
    [Fact]
    public async Task ProviderPullRequestFilesEndpoint_ReturnsFilesForPullRequest()
    {
        using TestWebApplicationFactory factory = new TestWebApplicationFactory();
        await factory.SeedProviderConnectionsAsync(new ProviderConnectionSettings
        {
            Provider = SourceControlProvider.GitHub,
            DisplayName = "GitHub",
            Organization = "octo-org",
            PersonalAccessToken = "github-pat",
            IsEnabled = true
        });
        factory.ConfigureGitHubResponse((request, _) =>
        {
            Assert.Equal("https://api.github.com/repos/octo-org/archharness/pulls/21/files?per_page=100&page=1", request.RequestUri?.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    [
                      {
                        "filename": "src/ArchHarness.App/SourceControl/PullRequestFile.cs",
                        "status": "added"
                      },
                      {
                        "filename": "src/ArchHarness.Web/Program.cs",
                        "status": "changed"
                      }
                    ]
                    """, Encoding.UTF8, "application/json")
            };
        });
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/providers/GitHub/pullrequests/21/files?repository=archharness");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement[] files = document.RootElement.EnumerateArray().ToArray();
        Assert.Equal(2, files.Length);
        Assert.Equal("src/ArchHarness.App/SourceControl/PullRequestFile.cs", files[0].GetProperty("path").GetString());
        Assert.Equal("Added", files[0].GetProperty("changeType").GetString());
        Assert.Equal("Modified", files[1].GetProperty("changeType").GetString());
    }

    /// <summary>
    /// ProviderPullRequestFilesEndpoint — ReturnsFriendlyErrorWhenTransportFails
    /// </summary>
    [Fact]
    public async Task ProviderPullRequestFilesEndpoint_ReturnsFriendlyErrorWhenTransportFails()
    {
        using TestWebApplicationFactory factory = new TestWebApplicationFactory();
        await factory.SeedProviderConnectionsAsync(new ProviderConnectionSettings
        {
            Provider = SourceControlProvider.GitHub,
            DisplayName = "GitHub",
            Organization = "octo-org",
            PersonalAccessToken = "github-pat",
            IsEnabled = true
        });
        factory.ConfigureGitHubResponse((_, _) => throw new HttpRequestException("The response ended prematurely."));
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/providers/GitHub/pullrequests/21/files?repository=archharness");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        string payload = await response.Content.ReadAsStringAsync();
        Assert.Contains("GitHub pull request file retrieval failed. Unable to reach GitHub over HTTPS.", payload, StringComparison.Ordinal);
    }

    /// <summary>
    /// ProviderPullRequestFilesEndpoint — ReturnsValidationProblemForInvalidPullRequestId
    /// </summary>
    [Fact]
    public async Task ProviderPullRequestFilesEndpoint_ReturnsValidationProblemForInvalidPullRequestId()
    {
        using TestWebApplicationFactory factory = new TestWebApplicationFactory();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/providers/GitHub/pullrequests/not-a-number/files?repository=archharness");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string payload = await response.Content.ReadAsStringAsync();
        Assert.Contains("pullRequestId", payload);
    }

    /// <summary>
    /// ProviderPullRequestFilesEndpoint — ReturnsUnauthorizedWhenProviderAuthenticationIsRejected
    /// </summary>
    [Fact]
    public async Task ProviderPullRequestFilesEndpoint_ReturnsUnauthorizedWhenProviderAuthenticationIsRejected()
    {
        using TestWebApplicationFactory factory = new TestWebApplicationFactory();
        await factory.SeedProviderConnectionsAsync(new ProviderConnectionSettings
        {
            Provider = SourceControlProvider.GitHub,
            DisplayName = "GitHub",
            Organization = "octo-org",
            PersonalAccessToken = "github-pat",
            IsEnabled = true
        });
        factory.ConfigureGitHubResponse((request, _) =>
        {
            Assert.Equal("https://api.github.com/repos/octo-org/archharness/pulls/21/files?per_page=100&page=1", request.RequestUri?.ToString());
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        });
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/providers/GitHub/pullrequests/21/files?repository=archharness");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        string payload = await response.Content.ReadAsStringAsync();
        Assert.Contains("authentication was rejected", payload, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ProvidersEndpoint — RejectsInsecureAzureDevOpsServerUrl
    /// </summary>
    [Fact]
    public async Task ProvidersEndpoint_RejectsInsecureAzureDevOpsServerUrl()
    {
        using TestWebApplicationFactory factory = new TestWebApplicationFactory();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/providers", new
        {
            provider = (int)SourceControlProvider.AzureDevOpsServer,
            displayName = "Contoso On Prem",
            serverUrl = "http://ado.example.com",
            organization = "DefaultCollection",
            personalAccessToken = "ado-pat",
            isEnabled = true
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string payload = await response.Content.ReadAsStringAsync();
        Assert.Contains("HTTPS", payload, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// ProviderPullRequestsEndpoint — DoesNotExposeUnhandledExceptionDetails
    /// </summary>
    [Fact]
    public async Task ProviderPullRequestsEndpoint_DoesNotExposeUnhandledExceptionDetails()
    {
        using TestWebApplicationFactory factory = new TestWebApplicationFactory();
        await factory.SeedProviderConnectionsAsync(new ProviderConnectionSettings
        {
            Provider = SourceControlProvider.GitHub,
            DisplayName = "GitHub",
            Organization = "octo-org",
            PersonalAccessToken = "github-pat",
            IsEnabled = true
        });
        factory.ConfigureGitHubResponse((_, _) => throw new Exception("top-secret-stack-trace"));
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/providers/GitHub/pullrequests?repository=archharness");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        string payload = await response.Content.ReadAsStringAsync();
        Assert.Contains("unexpected error", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("top-secret-stack-trace", payload, StringComparison.Ordinal);
    }

    /// <summary>
    /// ProvidersEndpoint — SavesAllProviderTypes
    /// </summary>
    [Fact]
    public async Task ProvidersEndpoint_SavesAllProviderTypes()
    {
        using TestWebApplicationFactory factory = new TestWebApplicationFactory();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage adoServicesResponse = await client.PostAsJsonAsync("/api/providers", new
        {
            provider = (int)SourceControlProvider.AzureDevOpsServices,
            displayName = "ADO Services No Project",
            serverUrl = (string?)null,
            organization = "contoso",
            personalAccessToken = "ado-pat",
            isEnabled = true
        });

        Assert.Equal(HttpStatusCode.OK, adoServicesResponse.StatusCode);
        JsonDocument adoServicesDoc = JsonDocument.Parse(await adoServicesResponse.Content.ReadAsStringAsync());
        Assert.Equal("ADO Services No Project", adoServicesDoc.RootElement.GetProperty("displayName").GetString());

        HttpResponseMessage adoServerResponse = await client.PostAsJsonAsync("/api/providers", new
        {
            provider = (int)SourceControlProvider.AzureDevOpsServer,
            displayName = "ADO Server No Project",
            serverUrl = "https://ado.example.com",
            organization = "DefaultCollection",
            personalAccessToken = "ado-pat",
            isEnabled = true
        });

        Assert.Equal(HttpStatusCode.OK, adoServerResponse.StatusCode);
        JsonDocument adoServerDoc = JsonDocument.Parse(await adoServerResponse.Content.ReadAsStringAsync());
        Assert.Equal("ADO Server No Project", adoServerDoc.RootElement.GetProperty("displayName").GetString());

        HttpResponseMessage githubResponse = await client.PostAsJsonAsync("/api/providers", new
        {
            provider = (int)SourceControlProvider.GitHub,
            displayName = "GitHub No Project",
            serverUrl = (string?)null,
            organization = "my-org",
            personalAccessToken = "github-pat",
            isEnabled = true
        });

        Assert.Equal(HttpStatusCode.OK, githubResponse.StatusCode);
        JsonDocument githubDoc = JsonDocument.Parse(await githubResponse.Content.ReadAsStringAsync());
        Assert.Equal("GitHub No Project", githubDoc.RootElement.GetProperty("displayName").GetString());

        JsonDocument providersDocument = JsonDocument.Parse(await client.GetStringAsync("/api/providers"));
        JsonElement[] configuredProviders = providersDocument.RootElement.EnumerateArray().ToArray();
        Assert.Equal(3, configuredProviders.Length);
        Assert.Contains(configuredProviders, provider => provider.GetProperty("displayName").GetString() == "ADO Services No Project");
        Assert.Contains(configuredProviders, provider => provider.GetProperty("displayName").GetString() == "ADO Server No Project");
        Assert.Contains(configuredProviders, provider => provider.GetProperty("displayName").GetString() == "GitHub No Project");
    }

    /// <summary>
    /// ProvidersEndpoint — AllowsGitHubProviderWithoutPersonalAccessToken
    /// </summary>
    [Fact]
    public async Task ProvidersEndpoint_AllowsGitHubProviderWithoutPersonalAccessToken()
    {
        using TestWebApplicationFactory factory = new TestWebApplicationFactory();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/providers", new
        {
            provider = (int)SourceControlProvider.GitHub,
            displayName = "GitHub Public",
            serverUrl = (string?)null,
            organization = "octo-org",
            personalAccessToken = (string?)null,
            isEnabled = true
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonDocument savedDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("GitHub Public", savedDocument.RootElement.GetProperty("displayName").GetString());
        Assert.Equal(JsonValueKind.Null, savedDocument.RootElement.GetProperty("personalAccessToken").ValueKind);

        JsonDocument providersDocument = JsonDocument.Parse(await client.GetStringAsync("/api/providers"));
        JsonElement provider = Assert.Single(providersDocument.RootElement.EnumerateArray());
        Assert.Equal("GitHub Public", provider.GetProperty("displayName").GetString());
        Assert.Equal(JsonValueKind.Null, provider.GetProperty("personalAccessToken").ValueKind);
    }

    /// <summary>
    /// ProvidersTestEndpoint — Succeeds — WhenProjectFieldIsAbsent
    /// </summary>
    [Fact]
    public async Task ProvidersTestEndpoint_Succeeds_WhenProjectFieldIsAbsent()
    {
        using TestWebApplicationFactory factory = new TestWebApplicationFactory();
        factory.ConfigureAzureDevOpsResponse((request, _) =>
        {
            Assert.Contains("/_apis/projects?api-version=6.0", request.RequestUri?.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{ "count": 1, "value": [] }""", Encoding.UTF8, "application/json")
            };
        });
        factory.ConfigureGitHubResponse((request, _) =>
        {
            Assert.Equal("https://api.github.com/user", request.RequestUri?.ToString());
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{ "login": "octocat" }""", Encoding.UTF8, "application/json")
            };
        });
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage adoServerResponse = await client.PostAsJsonAsync("/api/providers/test", new
        {
            provider = (int)SourceControlProvider.AzureDevOpsServer,
            displayName = "ADO Server",
            serverUrl = "https://ado.example.com/tfs",
            organization = "DefaultCollection",
            personalAccessToken = "ado-pat",
            isEnabled = true
        });

        Assert.Equal(HttpStatusCode.OK, adoServerResponse.StatusCode);
        JsonDocument adoServerDocument = JsonDocument.Parse(await adoServerResponse.Content.ReadAsStringAsync());
        Assert.True(adoServerDocument.RootElement.GetProperty("success").GetBoolean());

        HttpResponseMessage adoServicesResponse = await client.PostAsJsonAsync("/api/providers/test", new
        {
            provider = (int)SourceControlProvider.AzureDevOpsServices,
            displayName = "ADO Services",
            serverUrl = (string?)null,
            organization = "contoso",
            personalAccessToken = "ado-pat",
            isEnabled = true
        });

        Assert.Equal(HttpStatusCode.OK, adoServicesResponse.StatusCode);
        JsonDocument adoServicesDocument = JsonDocument.Parse(await adoServicesResponse.Content.ReadAsStringAsync());
        Assert.True(adoServicesDocument.RootElement.GetProperty("success").GetBoolean());

        HttpResponseMessage githubResponse = await client.PostAsJsonAsync("/api/providers/test", new
        {
            provider = (int)SourceControlProvider.GitHub,
            displayName = "GitHub",
            serverUrl = (string?)null,
            organization = "octo-org",
            personalAccessToken = "github-pat",
            isEnabled = true
        });

        Assert.Equal(HttpStatusCode.OK, githubResponse.StatusCode);
        JsonDocument githubDocument = JsonDocument.Parse(await githubResponse.Content.ReadAsStringAsync());
        Assert.True(githubDocument.RootElement.GetProperty("success").GetBoolean());
    }

    /// <summary>
    /// SettingsEndpoint — DoesNotContainLegacySingleProviderFields
    /// </summary>
    [Fact]
    public async Task SettingsEndpoint_DoesNotContainLegacySingleProviderFields()
    {
        using TestWebApplicationFactory factory = new TestWebApplicationFactory();
        using HttpClient client = factory.CreateClient();

        JsonDocument document = JsonDocument.Parse(await client.GetStringAsync("/api/settings"));
        JsonElement root = document.RootElement;

        Assert.False(root.TryGetProperty("projectName", out _), "Response must not contain a root-level 'projectName' field.");
        Assert.False(root.TryGetProperty("providerType", out _), "Response must not contain a root-level 'providerType' field.");
        Assert.False(root.TryGetProperty("repositoryName", out _), "Response must not contain a root-level 'repositoryName' field.");
        Assert.False(root.TryGetProperty("personalAccessToken", out _), "Response must not expose a root-level 'personalAccessToken' field.");
        Assert.False(root.TryGetProperty("sourceControl", out _), "Response must not contain the legacy single-provider 'sourceControl' block.");

        Assert.True(root.TryGetProperty("agentModels", out _));
        Assert.True(root.TryGetProperty("defaults", out _));
        Assert.True(root.TryGetProperty("updatedAtUtc", out _));
    }

    /// <summary>
    /// PlanningSessionEndpoint — ReturnsBadRequestWhenWorkspacePathMissing
    /// </summary>
    [Fact]
    public async Task PlanningSessionEndpoint_ReturnsBadRequestWhenWorkspacePathMissing()
    {
        using TestWebApplicationFactory factory = new TestWebApplicationFactory();
        using HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/planning-sessions/s-1?workspacePath=");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// PlanningSessionEndpoint — ReturnsNotFoundForUnknownSession
    /// </summary>
    [Fact]
    public async Task PlanningSessionEndpoint_ReturnsNotFoundForUnknownSession()
    {
        using TestWebApplicationFactory factory = new TestWebApplicationFactory();
        using HttpClient client = factory.CreateClient();
        string workspacePath = factory.CreateWorkspace("planning-session-missing");

        await client.PostAsJsonAsync("/api/projects", new
        {
            displayName = "Planning Session Missing",
            workspacePath,
            workspaceMode = "existing-folder",
            permissionHandlerMode = "approve-all",
            architectureReviewMode = false,
            architectureReviewPrompt = (string?)null
        });

        HttpResponseMessage response = await client.GetAsync($"/api/planning-sessions/missing-id?workspacePath={Uri.EscapeDataString(workspacePath)}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// PlanningSessionEndpoint — RoundTripsFollowUpMessageWithAttachment
    /// </summary>
    [Fact]
    public async Task PlanningSessionEndpoint_RoundTripsFollowUpMessageWithAttachment()
    {
        using TestWebApplicationFactory factory = new TestWebApplicationFactory();
        using HttpClient client = factory.CreateClient();
        string workspacePath = factory.CreateWorkspace("planning-session-roundtrip");

        HttpResponseMessage createResponse = await client.PostAsJsonAsync("/api/projects", new
        {
            displayName = "Planning Session Roundtrip",
            workspacePath,
            workspaceMode = "existing-folder",
            permissionHandlerMode = "approve-all",
            architectureReviewMode = false,
            architectureReviewPrompt = (string?)null
        });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        // Seed a planning session via the store so the recorder can append to it.
        ArchHarness.App.Storage.PlanningSessionStore store = new ArchHarness.App.Storage.PlanningSessionStore();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ArchHarness.App.Core.PlanningSession seed = new ArchHarness.App.Core.PlanningSession(
            "session-web-1",
            now,
            now,
            "plan-run-1",
            ImplementationRunId: null,
            Messages: Array.Empty<ArchHarness.App.Core.ConversationMessage>());
        await store.WriteAsync(workspacePath, seed, CancellationToken.None);

        object payload = new
        {
            workspacePath,
            role = ArchHarness.App.Core.ConversationRoles.USER,
            kind = ArchHarness.App.Core.ConversationMessageKinds.FOLLOW_UP,
            text = "please tweak the dashboard",
            authorAgent = (string?)null,
            relatedRunId = "impl-1",
            attachments = new[]
            {
                new
                {
                    id = "att-1",
                    kind = ArchHarness.App.Core.PromptAttachmentKinds.IMAGE,
                    mimeType = "image/png",
                    fileName = "mock.png",
                    sizeBytes = 512L,
                    dataBase64 = "AAA=",
                    storagePath = (string?)null,
                    caption = "screenshot"
                }
            }
        };

        HttpResponseMessage appendResponse = await client.PostAsJsonAsync(
            "/api/planning-sessions/session-web-1/messages",
            payload);
        Assert.Equal(HttpStatusCode.OK, appendResponse.StatusCode);

        HttpResponseMessage getResponse = await client.GetAsync(
            $"/api/planning-sessions/session-web-1?workspacePath={Uri.EscapeDataString(workspacePath)}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        JsonDocument document = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync());
        JsonElement messages = document.RootElement.GetProperty("messages");
        JsonElement message = Assert.Single(messages.EnumerateArray());
        Assert.Equal(ArchHarness.App.Core.ConversationMessageKinds.FOLLOW_UP, message.GetProperty("kind").GetString());
        Assert.Equal("please tweak the dashboard", message.GetProperty("text").GetString());
        Assert.Equal("impl-1", message.GetProperty("relatedRunId").GetString());
        JsonElement attachment = Assert.Single(message.GetProperty("attachments").EnumerateArray());
        Assert.Equal("att-1", attachment.GetProperty("id").GetString());
        Assert.Equal("mock.png", attachment.GetProperty("fileName").GetString());
    }
}
