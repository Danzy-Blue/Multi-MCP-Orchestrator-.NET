using Microsoft.Extensions.Configuration;

namespace McpHost;

public static class AppConfigBindingExtensions
{
    public static AppConfig LoadAppConfig(this IConfiguration configuration)
    {
        var appConfig = configuration.GetSection(AppConfig.SectionName).Get<AppConfig>() ?? new AppConfig();
        ApplyLegacyOverrides(appConfig, configuration);
        Normalize(appConfig);
        Validate(appConfig);
        return appConfig;
    }

    private static void ApplyLegacyOverrides(AppConfig appConfig, IConfiguration configuration)
    {
        appConfig.LlmProvider = FirstNonEmpty(configuration["LLM_PROVIDER"], appConfig.LlmProvider) ?? "gemini";

        var configuredModel = Normalize(configuration["LLM_MODEL"]);
        if (!string.IsNullOrWhiteSpace(configuredModel))
        {
            appConfig.LlmModel = configuredModel;
        }
        else
        {
            var providerModel = Normalize(appConfig.LlmProvider)?.ToLowerInvariant() switch
            {
                "gemini" => configuration["GEMINI_MODEL"],
                "openai" => configuration["OPENAI_MODEL"],
                _ => null,
            };

            appConfig.LlmModel = FirstNonEmpty(providerModel, appConfig.LlmModel) ?? string.Empty;
        }

        appConfig.LlmReasoningEffort = FirstNonEmpty(
            configuration["LLM_REASONING_EFFORT"],
            configuration["OPENAI_REASONING_EFFORT"],
            appConfig.LlmReasoningEffort);
        appConfig.GeminiApiKey = FirstNonEmpty(configuration["GEMINI_API_KEY"], appConfig.GeminiApiKey) ?? string.Empty;
        appConfig.OpenAiApiKey = FirstNonEmpty(configuration["OPENAI_API_KEY"], appConfig.OpenAiApiKey) ?? string.Empty;
        appConfig.SystemInstruction = FirstNonEmpty(configuration["SYSTEM_INSTRUCTION"], appConfig.SystemInstruction);

        if (int.TryParse(configuration["API_PORT"], out var apiPort))
        {
            appConfig.ApiPort = apiPort;
        }

        if (int.TryParse(configuration["SESSION_TTL_SECS"], out var ttl))
        {
            appConfig.SessionTtlSeconds = ttl;
        }

        var corsOrigins = ParseCsv(configuration["CORS_ORIGINS"]);
        if (corsOrigins.Count > 0)
        {
            appConfig.CorsOrigins = corsOrigins;
        }

        OverrideServer(appConfig.ServerConfigs, "uw", configuration["UW_SERVER_URL"]);
        OverrideServer(appConfig.ServerConfigs, "devops", configuration["DEVOPS_SERVER_URL"]);
    }

    private static void Normalize(AppConfig appConfig)
    {
        appConfig.LlmProvider = Normalize(appConfig.LlmProvider)?.ToLowerInvariant() ?? "gemini";
        appConfig.LlmModel = Normalize(appConfig.LlmModel) ?? string.Empty;
        appConfig.LlmReasoningEffort = Normalize(appConfig.LlmReasoningEffort);
        appConfig.GeminiApiKey = Normalize(appConfig.GeminiApiKey) ?? string.Empty;
        appConfig.OpenAiApiKey = Normalize(appConfig.OpenAiApiKey) ?? string.Empty;
        appConfig.AppTitle = Normalize(appConfig.AppTitle) ?? "MCP Chat API";
        appConfig.SystemInstruction = Normalize(appConfig.SystemInstruction);
        appConfig.CorsOrigins = appConfig.CorsOrigins
            .Select(Normalize)
            .Where(origin => !string.IsNullOrWhiteSpace(origin))
            .Distinct(StringComparer.Ordinal)
            .Cast<string>()
            .ToList();
        if (appConfig.CorsOrigins.Count == 0)
        {
            appConfig.CorsOrigins = ["*"];
        }

        appConfig.ServerConfigs = appConfig.ServerConfigs
            .Select(server => new ServerConfig(
                Normalize(server.Alias) ?? string.Empty,
                Normalize(server.Url) ?? string.Empty))
            .Where(server => !string.IsNullOrWhiteSpace(server.Alias) && !string.IsNullOrWhiteSpace(server.Url))
            .ToList();
    }

    private static void Validate(AppConfig appConfig)
    {
        if (appConfig.LlmProvider is not ("gemini" or "openai"))
        {
            throw new InvalidOperationException("AppConfig:LlmProvider must be either 'gemini' or 'openai'.");
        }

        if (string.IsNullOrWhiteSpace(appConfig.LlmModel))
        {
            throw new InvalidOperationException("AppConfig:LlmModel must be configured.");
        }

        if (appConfig.LlmProvider == "gemini" && string.IsNullOrWhiteSpace(appConfig.GeminiApiKey))
        {
            throw new InvalidOperationException("AppConfig:GeminiApiKey must be configured when using the gemini provider.");
        }

        if (appConfig.LlmProvider == "openai" && string.IsNullOrWhiteSpace(appConfig.OpenAiApiKey))
        {
            throw new InvalidOperationException("AppConfig:OpenAiApiKey must be configured when using the openai provider.");
        }

        if (appConfig.ServerConfigs.Count == 0)
        {
            throw new InvalidOperationException("At least one AppConfig:ServerConfigs entry must be configured.");
        }
    }

    private static void OverrideServer(ICollection<ServerConfig> servers, string alias, string? url)
    {
        var normalizedUrl = Normalize(url);
        if (string.IsNullOrWhiteSpace(normalizedUrl))
        {
            return;
        }

        var existing = servers
            .Select((server, index) => (server, index))
            .FirstOrDefault(entry => string.Equals(entry.server.Alias, alias, StringComparison.OrdinalIgnoreCase));
        if (existing.server is not null)
        {
            servers.Remove(existing.server);
        }

        servers.Add(new ServerConfig(alias, normalizedUrl));
    }

    private static List<string> ParseCsv(string? value)
    {
        return (value ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            var normalized = Normalize(value);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return normalized;
            }
        }

        return null;
    }

    private static string? Normalize(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }
}
