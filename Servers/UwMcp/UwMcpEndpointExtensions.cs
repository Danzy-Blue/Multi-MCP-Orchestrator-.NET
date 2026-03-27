using ModelContextProtocol.AspNetCore;

namespace UwMcp;

internal static class UwMcpEndpointExtensions
{
    public static void MapUwMcpEndpoints(this WebApplication app, IConfiguration configuration)
    {
        var httpPath = NormalizePath(configuration["MCP_HTTP_PATH"], "/mcp");

        ConfigureUrls(app, configuration);

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
            name = "underwriting",
            kind = "api",
            health = "/health",
            mcp = httpPath,
        })).WithName("GetApiInfo");

        app.MapGet("/health", () => Results.Ok(new
        {
            status = "ok",
            service = "underwriting",
        })).WithName("GetHealth");

        app.MapMcp(httpPath);
    }

    private static void ConfigureUrls(WebApplication app, IConfiguration configuration)
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

    private static string NormalizePath(string? configuredPath, string fallback)
    {
        var path = string.IsNullOrWhiteSpace(configuredPath) ? fallback : configuredPath.Trim();
        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }

        return path;
    }
}
