using Microsoft.Extensions.DependencyInjection;

namespace ArchHarness.App.SourceControl;

/// <summary>
/// Resolves the correct source control provider implementation for a configuration.
/// </summary>
public sealed class SourceControlProviderFactory
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="SourceControlProviderFactory"/> class.
    /// </summary>
    public SourceControlProviderFactory(IServiceProvider serviceProvider)
    {
        this._serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Gets the provider service that handles the specified source control provider type.
    /// </summary>
    /// <param name="providerType">The provider type to resolve.</param>
    /// <returns>The matching provider service.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the provider type is unsupported.</exception>
    public ISourceControlReviewProviderService GetProvider(SourceControlProvider providerType)
        => providerType switch
        {
            SourceControlProvider.AzureDevOpsServer or SourceControlProvider.AzureDevOpsServices
                => this._serviceProvider.GetRequiredService<AzureDevOpsSourceControlService>(),
            SourceControlProvider.GitHub
                => this._serviceProvider.GetRequiredService<GitHubSourceControlService>(),
            _ => throw new InvalidOperationException($"Unsupported source control provider type '{providerType}'.")
        };
}
