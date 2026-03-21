using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace ArchHarness.App.Tests.Web;

internal sealed class LoopbackRemoteAddressStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        => app =>
        {
            app.Use(async (context, middlewareNext) =>
            {
                context.Connection.RemoteIpAddress ??= IPAddress.Loopback;
                await middlewareNext();
            });

            next(app);
        };
}
