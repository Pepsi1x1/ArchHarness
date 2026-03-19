using Microsoft.AspNetCore.Diagnostics;

namespace ArchHarness.Web;

internal static class ProgramApplicationExtensions
{
    private const string ContentSecurityPolicy = "default-src 'self'; style-src 'self' https://fonts.googleapis.com https://cdnjs.cloudflare.com; font-src 'self' https://fonts.gstatic.com https://cdnjs.cloudflare.com; img-src 'self' data:; connect-src 'self'; script-src 'self'; base-uri 'none'; object-src 'none'; frame-ancestors 'none'; form-action 'self'";

    public static WebApplication UseArchHarnessExceptionHandling(this WebApplication app)
    {
        app.UseExceptionHandler(exceptionApp =>
        {
            exceptionApp.Run(async context =>
            {
                ILogger logger = context.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("ArchHarness.Web.UnhandledException");
                IExceptionHandlerPathFeature? feature = context.Features.Get<IExceptionHandlerPathFeature>();
                if (feature?.Error is not null)
                {
                    logger.LogError(feature.Error, "Unhandled exception while processing {Path}.", context.Request.Path);
                }

                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                await Results.Problem(
                    title: "An unexpected error occurred.",
                    detail: "The request could not be completed.",
                    statusCode: StatusCodes.Status500InternalServerError,
                    extensions: new Dictionary<string, object?>
                    {
                        ["traceId"] = context.TraceIdentifier
                    }).ExecuteAsync(context);
            });
        });

        return app;
    }

    public static WebApplication UseArchHarnessSecurityHeaders(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            context.Response.Headers["Content-Security-Policy"] = ContentSecurityPolicy;
            await next();
        });

        return app;
    }
}