using ArchHarness.App.SourceControl;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ArchHarness.App.Tests.Core;

public sealed class ArchHarnessServiceCollectionExtensionsTests
{
    /// <summary>
    /// Verifies expected behavior.
    /// </summary>
    [Fact]
    public void AddArchHarnessRuntimeServices_RegistersGitHubOAuthDeviceFlowServiceAsSingleton()
    {
        ServiceCollection services = new ServiceCollection();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["gitHubOAuth:clientId"] = "client-id"
            })
            .Build();

        services.AddArchHarnessRuntimeServices(configuration);
        using ServiceProvider serviceProvider = services.BuildServiceProvider();

        IGitHubOAuthDeviceFlowService first = serviceProvider.GetRequiredService<IGitHubOAuthDeviceFlowService>();
        IGitHubOAuthDeviceFlowService second = serviceProvider.GetRequiredService<IGitHubOAuthDeviceFlowService>();
        GitHubOAuthDeviceFlowService concrete = serviceProvider.GetRequiredService<GitHubOAuthDeviceFlowService>();

        Assert.Same(first, second);
        Assert.Same(first, concrete);
    }
}
