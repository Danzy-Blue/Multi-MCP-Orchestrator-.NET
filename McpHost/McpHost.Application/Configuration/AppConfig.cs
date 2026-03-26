using McpGateway;

namespace McpHost;

public sealed class AppConfig
{
    public const string SectionName = "AppConfig";

    public string LlmProvider { get; set; } = "gemini";
    public string LlmModel { get; set; } = string.Empty;
    public string? LlmReasoningEffort { get; set; }
    public string GeminiApiKey { get; set; } = string.Empty;
    public string OpenAiApiKey { get; set; } = string.Empty;
    public int ApiPort { get; set; } = 8888;
    public int SessionTtlSeconds { get; set; } = 3600;
    public List<string> CorsOrigins { get; set; } = ["*"];
    public List<ServerConfig> ServerConfigs { get; set; } = [];
    public string AppTitle { get; set; } = "MCP Chat API";
    public string? SystemInstruction { get; set; }
}
