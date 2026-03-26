using McpGateway;
using Microsoft.Extensions.DependencyInjection;

namespace McpHost;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMcpHostServices(this IServiceCollection services, AppConfig appConfig)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton<ICorrelationContextAccessor, CorrelationContextAccessor>();
        services.AddTransient<CorrelationIdHandler>();
        services.ConfigureHttpClientDefaults(http =>
        {
            http.AddHttpMessageHandler<CorrelationIdHandler>();
        });

        services.AddSingleton(appConfig);
        services.AddHttpClient();
        services.AddSingleton<IMcpConnectionFactory, McpConnectionFactory>();
        services.AddSingleton<IMcpServerConnectionFactory, GatewayMcpConnectionFactory>();
        services.AddSingleton<ILlmService>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            return appConfig.LlmProvider switch
            {
                "gemini" => new GeminiLlmService(appConfig.GeminiApiKey, httpClientFactory),
                "openai" => new OpenAiLlmService(
                    appConfig.OpenAiApiKey,
                    appConfig.LlmReasoningEffort,
                    httpClientFactory),
                _ => throw new InvalidOperationException($"Unsupported LLM provider '{appConfig.LlmProvider}'."),
            };
        });
        services.AddSingleton<SessionManager>();
        services.AddHostedService<SessionCleanupService>();

        return services;
    }
}
