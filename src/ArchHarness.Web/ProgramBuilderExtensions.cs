using ArchHarness.App;
using ArchHarness.App.Copilot;
using ArchHarness.Web.Services;

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
        services.AddSingleton<IWebRunSessionManager, WebRunSessionManager>();
        services.AddSingleton<IModelMetadataProvider, ModelMetadataProvider>();
        return services;
    }
}