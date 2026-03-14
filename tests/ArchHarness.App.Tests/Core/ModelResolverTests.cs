using ArchHarness.App.Copilot;
using ArchHarness.App.Core;
using Microsoft.Extensions.Options;

namespace ArchHarness.App.Tests.Core;

public sealed class ModelResolverTests
{
    [Fact]
    public void ValidateConfiguredModelsOrThrow_AllConfiguredModelsDiscovered_DoesNotThrow()
    {
        ModelResolver resolver = CreateResolver(new[]
        {
            "gpt-5-mini",
            "claude-opus-4.6",
            "claude-sonnet-4.6",
            "gpt-5.4",
            "gpt-4.1"
        });

        Exception? exception = Record.Exception(() => resolver.ValidateConfiguredModelsOrThrow());

        Assert.Null(exception);
    }

    [Fact]
    public void ValidateConfiguredModelsOrThrow_MissingConfiguredModel_ThrowsWithRoleDetails()
    {
        ModelResolver resolver = CreateResolver(new[]
        {
            "gpt-5-mini",
            "claude-opus-4.6",
            "claude-sonnet-4.6",
            "gpt-4.1"
        });

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => resolver.ValidateConfiguredModelsOrThrow());

        Assert.Contains("backend-developer=gpt-5.4", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateConfiguredModelsOrThrow_MissingOverrideModel_ThrowsWithOverrideDetails()
    {
        ModelResolver resolver = CreateResolver(new[]
        {
            "gpt-5-mini",
            "claude-opus-4.6",
            "claude-sonnet-4.6",
            "gpt-5.4",
            "gpt-4.1"
        });

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => resolver.ValidateConfiguredModelsOrThrow(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["conversation"] = "not-a-real-model"
            }));

        Assert.Contains("override:conversation=not-a-real-model", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateConfiguredModelsOrThrow_NoDiscoveredModels_DoesNotThrow()
    {
        ModelResolver resolver = CreateResolver(Array.Empty<string>());

        Exception? exception = Record.Exception(() => resolver.ValidateConfiguredModelsOrThrow());

        Assert.Null(exception);
    }

    private static ModelResolver CreateResolver(IEnumerable<string> discoveredModels)
    {
        AgentsOptions agentsOptions = new AgentsOptions
        {
            Orchestration = new AgentModelOptions { Model = "claude-opus-4.6" },
            FrontendDeveloper = new AgentModelOptions { Model = "claude-sonnet-4.6" },
            BackendDeveloper = new AgentModelOptions { Model = "gpt-5.4" },
            Build = new AgentModelOptions { Model = "gpt-4.1" },
            CodingStyle = new AgentModelOptions { Model = "gpt-5.4" },
            Security = new AgentModelOptions { Model = "gpt-5.4" },
            Architecture = new AgentModelOptions { Model = "claude-opus-4.6" }
        };

        CopilotOptions copilotOptions = new CopilotOptions
        {
            ConversationModel = "gpt-5-mini"
        };

        DiscoveredModelCatalog catalog = new DiscoveredModelCatalog();
        if (discoveredModels.Any())
        {
            catalog.ReplaceModels(discoveredModels);
        }

        return new ModelResolver(
            Options.Create(agentsOptions),
            Options.Create(copilotOptions),
            catalog);
    }
}