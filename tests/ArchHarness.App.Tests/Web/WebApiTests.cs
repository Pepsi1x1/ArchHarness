using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ArchHarness.App.Tests.Web;

public sealed class WebApiTests
{

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
        await File.WriteAllTextAsync(Path.Combine(runDirectory, "run-log.json"), """
            {
              "status": "completed",
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

    [Fact]
    public async Task ProjectsEndpoint_SynthesizesRunTitlesFromPersistedRequestEvents()
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

        Assert.Equal("Create the web shell and connect", run.GetProperty("runTitle").GetString());
    }

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

    [Fact]
    public async Task SettingsAndModelsEndpoints_ReturnStructuredGlobalSettingsPayloads()
    {
        using TestWebApplicationFactory factory = new TestWebApplicationFactory();
        using HttpClient client = factory.CreateClient();

        JsonDocument settingsDocument = JsonDocument.Parse(await client.GetStringAsync("/api/settings"));
        Assert.Equal("gpt-5-mini", settingsDocument.RootElement.GetProperty("agentModels").GetProperty("conversation").GetString());

        HttpResponseMessage updateResponse = await client.PutAsJsonAsync("/api/settings", new
        {
            agentModels = new
            {
                conversation = "gpt-5.4",
                orchestration = "claude-sonnet-4.6",
                frontendDeveloper = "claude-sonnet-4.6",
                backendDeveloper = "gpt-5.3-codex",
                build = "gpt-4.1",
                codingStyle = "claude-opus-4.6",
                security = "claude-opus-4.6",
                architecture = "claude-opus-4.6"
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
        Assert.Equal("gpt-5.4", updatedSettings.RootElement.GetProperty("agentModels").GetProperty("conversation").GetString());
        Assert.Equal("prompt", updatedSettings.RootElement.GetProperty("defaults").GetProperty("permissionHandlerMode").GetString());

        JsonDocument modelsDocument = JsonDocument.Parse(await client.GetStringAsync("/api/models"));
        Assert.Contains(modelsDocument.RootElement.GetProperty("models").EnumerateArray(),
            model => model.GetProperty("modelId").GetString() == "claude-opus-4.6"
                && model.GetProperty("costBand").GetString() == "3x");
    }
}