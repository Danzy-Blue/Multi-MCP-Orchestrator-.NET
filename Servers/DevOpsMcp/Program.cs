using DevOpsMcp;
using ModelContextProtocol.Server;
using Shared.Logging;

var builder = WebApplication.CreateBuilder(args);
var loggingOptions = builder.AddConfiguredLogging("devops-mcp.log");
builder.Services.AddHttpClient();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<DevOpsWorkItemService>();
builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

if (loggingOptions.AppInsights.Enabled)
{
    app.Logger.LogWarning("Application Insights logging is configured but not enabled in this build.");
}

app.MapDevOpsMcpEndpoints(builder.Configuration);
app.Run();
