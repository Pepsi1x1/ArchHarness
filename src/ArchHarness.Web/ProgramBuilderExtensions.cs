using ArchHarness.App;
using ArchHarness.App.Copilot;
using ArchHarness.Web.Services;
using System.Net;

namespace ArchHarness.Web;

internal static class ProgramBuilderExtensions
{
    public static WebApplicationBuilder ConfigureArchHarnessWebHost(this WebApplicationBuilder builder)
    {
        builder.Configuration.AddJsonFile(
            Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
            optional: true,
            reloadOnChange: false);
        builder.Configuration.AddEnvironmentVariables();

        string? webHostUrl = builder.Configuration["webHost:url"];
        if (!string.IsNullOrWhiteSpace(webHostUrl))
        {
            // http:// is intentional here: the web host binds to the loopback interface only
            // (validated by ValidateLoopbackWebHostUrl below). Traffic never leaves the machine,
            // so TLS is unnecessary and would complicate self-hosted deployment.
            ValidateLoopbackWebHostUrl(webHostUrl);
            builder.WebHost.UseUrls(webHostUrl);
        }

        return builder;
    }

    public static IServiceCollection AddArchHarnessWebServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddArchHarnessRuntimeServices(configuration);
        services.AddArchHarnessInteractiveServices();
        services.AddSingleton<WebInteractionCoordinator>();
        services.AddSingleton<ICopilotUserInputBridge, WebCopilotUserInputBridge>();
        services.AddSingleton<ICopilotPermissionPromptHandler, WebPermissionPromptHandler>();
        services.AddSingleton<IWebRunEventHub, WebRunEventHub>();
        services.AddSingleton<IWebRunSnapshotStore, WebRunSnapshotStore>();
        services.AddSingleton<IWebRunExecutionRunner, WebRunExecutionRunner>();
        services.AddSingleton<IWebRunSessionManager, WebRunSessionManager>();
        services.AddSingleton<IModelMetadataProvider, ModelMetadataProvider>();
        return services;
    }

    private static void ValidateLoopbackWebHostUrl(string webHostUrl)
    {
        foreach (string candidate in webHostUrl.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri))
            {
                throw new InvalidOperationException("webHost:url must be an absolute URL.");
            }

            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("webHost:url must use HTTP or HTTPS.");
            }

            if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!IPAddress.TryParse(uri.Host.Trim('[', ']'), out IPAddress? parsedAddress) || !IPAddress.IsLoopback(parsedAddress))
            {
                throw new InvalidOperationException("webHost:url must bind to localhost or a loopback address.");
            }
        }
    }
}
