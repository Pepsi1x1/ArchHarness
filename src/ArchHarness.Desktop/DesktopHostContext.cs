using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ArchHarness.Desktop;

/// <summary>
/// Holds the shared <see cref="IHost"/> instance so that Avalonia components can resolve services
/// before the DI-aware constructor path is available.
/// </summary>
internal static class DesktopHostContext
{
    /// <summary>Gets or sets the current application host.</summary>
    public static IHost? CurrentHost { get; set; }

    /// <summary>
    /// Attempts to resolve a required service from the current host.
    /// </summary>
    /// <typeparam name="T">The service type to resolve.</typeparam>
    /// <param name="service">The resolved service instance, or <see langword="null"/> if the host is unavailable.</param>
    /// <returns><see langword="true"/> if the service was resolved; otherwise <see langword="false"/>.</returns>
    public static bool TryGetRequiredService<T>(out T? service)
        where T : class
    {
        if (CurrentHost is null)
        {
            service = null;
            return false;
        }

        service = CurrentHost.Services.GetRequiredService<T>();
        return true;
    }
}