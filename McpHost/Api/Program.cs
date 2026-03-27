using McpHost;
using Shared.Logging;

var builder = WebApplication.CreateBuilder(args);
var loggingOptions = builder.AddConfiguredLogging("mcp-host.log");

var appConfig = builder.Configuration.LoadAppConfig();
builder.WebHost.UseUrls($"http://0.0.0.0:{appConfig.ApiPort}");
builder.Services.AddMcpHostServices(appConfig);
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (appConfig.CorsOrigins.Contains("*"))
        {
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
        }
        else
        {
            policy.WithOrigins(appConfig.CorsOrigins.ToArray()).AllowAnyHeader().AllowAnyMethod();
        }
    });
});

var app = builder.Build();
app.UseCors();

if (loggingOptions.AppInsights.Enabled)
{
    app.Logger.LogWarning("Application Insights logging is configured but not enabled in this build.");
}

app.Use(async (context, next) =>
{
    // Correlation ids are generated once at the API edge and then propagated into every outbound
    // HTTP call, including MCP server traffic and any downstream APIs those servers call.
    var correlationContextAccessor = context.RequestServices.GetRequiredService<ICorrelationContextAccessor>();
    var correlationId = context.Request.Headers[CorrelationConstants.HeaderName].FirstOrDefault()
        ?? Guid.NewGuid().ToString();
    context.Response.Headers[CorrelationConstants.HeaderName] = correlationId;
    context.Items[CorrelationConstants.ItemKey] = correlationId;
    correlationContextAccessor.CorrelationId = correlationId;
    using var scope = app.Logger.BeginScope(new Dictionary<string, object?>
    {
        ["correlation_id"] = correlationId,
    });
    try
    {
        await next();
    }
    finally
    {
        correlationContextAccessor.CorrelationId = null;
    }
});

app.Logger.LogInformation(
    "Starting MCP host with provider {Provider}, model {Model}, and {ServerCount} configured servers",
    appConfig.LlmProvider,
    appConfig.LlmModel,
    appConfig.ServerConfigs.Count);

app.MapGet("/", (SessionManager sessionManager) => Results.Ok(new
{
    name = "mcp-host",
    kind = "api",
    health = "/health",
    sessions = "/sessions",
    chat = "/chat",
    prompts = "/prompts",
    render_prompt = "/prompts/render",
    provider = sessionManager.AppConfig.LlmProvider,
    model = sessionManager.AppConfig.LlmModel,
})).WithName("GetApiInfo");

app.MapGet("/health", (SessionManager sessionManager) => Results.Ok(new
{
    status = "ok",
    provider = sessionManager.AppConfig.LlmProvider,
    model = sessionManager.AppConfig.LlmModel,
    session_ttl_secs = sessionManager.AppConfig.SessionTtlSeconds,
    servers = sessionManager.AppConfig.ServerConfigs.Select(server => new
    {
        alias = server.Alias,
        url = server.Url,
    }),
    active_sessions = sessionManager.ActiveSessionCount,
}));

app.MapGet("/sessions", (SessionManager sessionManager) => Results.Ok(sessionManager.Snapshot()));

app.MapGet("/prompts", async (string? sessionId, SessionManager sessionManager, CancellationToken ct) =>
{
    app.Logger.LogDebug("HTTP GET /prompts for session {SessionId}", sessionId ?? "<new>");
    var result = await sessionManager.ListPromptsAsync(sessionId, ct);
    return Results.Ok(result);
});

app.MapPost("/prompts/render", async (
    PromptRenderRequest request,
    SessionManager sessionManager,
    CancellationToken ct) =>
{
    app.Logger.LogDebug(
        "HTTP POST /prompts/render for session {SessionId} and prompt {PromptName}",
        request.SessionId ?? "<new>",
        request.Name);
    var result = await sessionManager.RenderPromptAsync(request.SessionId, request.Name, request.Arguments, ct);
    return Results.Ok(result);
});

app.MapPost("/chat", async (ChatRequest request, SessionManager sessionManager, CancellationToken ct) =>
{
    app.Logger.LogDebug("HTTP POST /chat for session {SessionId}", request.SessionId ?? "<new>");
    var result = await sessionManager.HandleChatAsync(request.SessionId, request.Message, ct);
    return Results.Ok(new ChatResponse(result.SessionId, result.Reply, result.ToolCalls));
});

app.MapPost("/chat/close", async (
    CloseSessionRequest request,
    SessionManager sessionManager,
    CancellationToken ct) =>
{
    app.Logger.LogDebug("HTTP POST /chat/close for session {SessionId}", request.SessionId);
    var closed = await sessionManager.CloseAsync(request.SessionId, ct);
    return Results.Ok(new { session_id = request.SessionId, closed });
});

app.Lifetime.ApplicationStopping.Register(() =>
{
    app.Logger.LogInformation("Application stopping. Disposing session state");
    using var scope = app.Services.CreateScope();
    var sessionManager = scope.ServiceProvider.GetRequiredService<SessionManager>();
    sessionManager.DisposeAsync().AsTask().GetAwaiter().GetResult();
});

app.Run();
