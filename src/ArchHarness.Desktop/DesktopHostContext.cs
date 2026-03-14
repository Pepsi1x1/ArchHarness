using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ArchHarness.Desktop;

internal static class DesktopHostContext
{
    public static IHost? CurrentHost { get; set; }

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