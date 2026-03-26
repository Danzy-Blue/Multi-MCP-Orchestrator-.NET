using DevOpsMcp;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
builder.Services.AddHttpClient();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<DevOpsWorkItemService>();
builder.Services
    .AddMcpServer()
    .WithHttpTransport()
    .WithToolsFromAssembly();

var app = builder.Build();

var httpPath = NormalizePath(builder.Configuration["MCP_HTTP_PATH"], "/mcp");

ConfigureUrls(app, builder.Configuration);

app.Use(async (context, next) =>
{
    var correlationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault()
        ?? Guid.NewGuid().ToString();
    context.Response.Headers["X-Correlation-ID"] = correlationId;
    context.Items["CorrelationId"] = correlationId;
    using var scope = app.Logger.BeginScope(new Dictionary<string, object?>
    {
        ["correlation_id"] = correlationId,
    });
    await next();
});

app.MapGet("/", () => Results.Ok(new
{
    name = "devops-tfs",
    kind = "api",
    health = "/health",
    mcp = httpPath,
})).WithName("GetApiInfo");

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = "devops-tfs",
})).WithName("GetHealth");

app.MapMcp(httpPath);
app.Run();

static void ConfigureUrls(WebApplication app, IConfiguration configuration)
{
    var portValue = configuration["PORT"];
    if (string.IsNullOrWhiteSpace(portValue))
    {
        return;
    }

    if (!int.TryParse(portValue, out var port))
    {
        throw new InvalidOperationException($"Invalid PORT value '{portValue}'.");
    }

    var host = configuration["MCP_HTTP_HOST"] ?? "0.0.0.0";
    app.Urls.Clear();
    app.Urls.Add($"http://{host}:{port}");
}

static string NormalizePath(string? configuredPath, string fallback)
{
    var path = string.IsNullOrWhiteSpace(configuredPath) ? fallback : configuredPath.Trim();
    if (!path.StartsWith('/'))
    {
        path = "/" + path;
    }

    return path;
}
