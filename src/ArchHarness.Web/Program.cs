using System.Text.Json;
using System.Text.Json.Serialization;
using ArchHarness.App.Copilot;
using ArchHarness.Web;
using Markdig;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.ConfigureArchHarnessWebHost();

JsonSerializerOptions eventJsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
{
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};
MarkdownPipeline markdownPipeline = new MarkdownPipelineBuilder()
    .UseAdvancedExtensions()
    .DisableHtml()
    .Build();
builder.Services.AddArchHarnessWebServices(builder.Configuration);

WebApplication app = builder.Build();
app.UseArchHarnessExceptionHandling()
    .UseArchHarnessSecurityHeaders()
    .UseDefaultFiles()
    .UseStaticFiles();

app.MapArchHarnessApi(eventJsonOptions, markdownPipeline);

await app.RunAsync();

public partial class Program
{
    private Program()
    {
    }
}
