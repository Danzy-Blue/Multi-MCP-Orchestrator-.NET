using Microsoft.Extensions.Configuration;

namespace McpHost.Tests;

public sealed class AppConfigTests
{
    [Fact]
    public void LoadAppConfig_BindsAppSettingsSectionAndAppliesLegacyOverrides()
    {
        var values = new Dictionary<string, string?>
        {
            [$"{AppConfig.SectionName}:LlmProvider"] = "gemini",
            [$"{AppConfig.SectionName}:LlmModel"] = "gemini-2.0-flash",
            [$"{AppConfig.SectionName}:GeminiApiKey"] = "gemini-key",
            [$"{AppConfig.SectionName}:ServerConfigs:0:Alias"] = "uw",
            [$"{AppConfig.SectionName}:ServerConfigs:0:Url"] = "https://uw.from-appsettings/mcp",
            ["LLM_PROVIDER"] = "openai",
            ["OPENAI_MODEL"] = "gpt-4.1-mini",
            ["OPENAI_API_KEY"] = "test-key",
            ["CORS_ORIGINS"] = "https://chat.example,https://admin.example",
            ["DEVOPS_SERVER_URL"] = "https://devops.example/mcp",
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var config = configuration.LoadAppConfig();

        Assert.Equal("openai", config.LlmProvider);
        Assert.Equal("gpt-4.1-mini", config.LlmModel);
        Assert.Equal("test-key", config.OpenAiApiKey);
        Assert.Equal(8888, config.ApiPort);
        Assert.Equal(3600, config.SessionTtlSeconds);
        Assert.Equal(["https://chat.example", "https://admin.example"], config.CorsOrigins);
        Assert.Equal(2, config.ServerConfigs.Count);
        Assert.Contains(config.ServerConfigs, server => server is { Alias: "uw", Url: "https://uw.from-appsettings/mcp" });
        Assert.Contains(config.ServerConfigs, server => server is { Alias: "devops", Url: "https://devops.example/mcp" });
    }

    [Fact]
    public void LoadAppConfig_Throws_WhenNoServersConfigured()
    {
        var values = new Dictionary<string, string?>
        {
            [$"{AppConfig.SectionName}:LlmProvider"] = "openai",
            [$"{AppConfig.SectionName}:LlmModel"] = "gpt-4.1-mini",
            [$"{AppConfig.SectionName}:OpenAiApiKey"] = "test-key",
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

        var error = Assert.Throws<InvalidOperationException>(() => configuration.LoadAppConfig());

        Assert.Contains("AppConfig:ServerConfigs", error.Message);
    }
}
