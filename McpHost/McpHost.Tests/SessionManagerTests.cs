using System.Text.Json.Nodes;
using McpGateway;
using Microsoft.Extensions.Logging.Abstractions;

namespace McpHost.Tests;

public sealed class SessionManagerTests
{
    [Fact]
    public async Task HandleChatAsync_ConcurrentRequestsForSameSession_InitializeOnce()
    {
        var appConfig = new AppConfig
        {
            LlmProvider = "gemini",
            LlmModel = "gemini-test",
            GeminiApiKey = "api-key",
        };
        var llmService = new CoordinatedLlmService();
        var httpClient = new HttpClient(new RecordingHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK))));
        await using var sessionManager = new SessionManager(
            appConfig,
            llmService,
            new FakeMcpConnectionFactory(_ => new FakeMcpConnection()),
            NullLogger<SessionManager>.Instance);

        var first = Task.Run(() => sessionManager.HandleChatAsync("session-1", "hello", CancellationToken.None));
        Assert.True(llmService.CreateChatEntered.Wait(TimeSpan.FromSeconds(5)));

        var second = Task.Run(() => sessionManager.HandleChatAsync("session-1", "hello again", CancellationToken.None));
        await Task.Delay(100);
        llmService.ReleaseCreateChat();

        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, llmService.CreateChatCallCount);
        Assert.Equal(1, sessionManager.ActiveSessionCount);
        Assert.All(results, result => Assert.Equal("session-1", result.SessionId));
    }

    [Fact]
    public async Task ListPromptsAsync_ReturnsPromptCatalogFromConnectedServers()
    {
        var appConfig = new AppConfig
        {
            LlmProvider = "gemini",
            LlmModel = "gemini-test",
            GeminiApiKey = "api-key",
            ServerConfigs = [new ServerConfig("uw", "https://uw.example/mcp")],
        };
        var prompt = new RegisteredPrompt(
            "summarize_submission",
            "Summarize Submission",
            "Summarize a submission for the user.",
            "uw",
            [new PromptArgumentRecord("reference", "Reference", "Submission reference", true)]);
        await using var sessionManager = new SessionManager(
            appConfig,
            new StubLlmService(),
            new FakeMcpConnectionFactory(_ => new FakeMcpConnection(prompts: [prompt])),
            NullLogger<SessionManager>.Instance);

        var result = await sessionManager.ListPromptsAsync("prompt-session", CancellationToken.None);

        Assert.Equal("prompt-session", result.SessionId);
        Assert.Collection(
            result.Prompts,
            item =>
            {
                Assert.Equal("summarize_submission", item.Name);
                Assert.Equal("uw", item.ServerAlias);
                Assert.Single(item.Arguments);
                Assert.True(item.Arguments[0].Required);
            });
    }

    [Fact]
    public async Task RenderPromptAsync_ReturnsRenderedPromptMessages()
    {
        var appConfig = new AppConfig
        {
            LlmProvider = "gemini",
            LlmModel = "gemini-test",
            GeminiApiKey = "api-key",
            ServerConfigs = [new ServerConfig("uw", "https://uw.example/mcp")],
        };
        var prompt = new RegisteredPrompt(
            "summarize_submission",
            "Summarize Submission",
            "Summarize a submission for the user.",
            "uw",
            []);
        await using var sessionManager = new SessionManager(
            appConfig,
            new StubLlmService(),
            new FakeMcpConnectionFactory(_ => new FakeMcpConnection(
                prompts: [prompt],
                getPromptAsync: (_, arguments, _) => Task.FromResult(
                    new RenderedPrompt(
                        "summarize_submission",
                        "Summarize Submission",
                        "Summarize a submission for the user.",
                        "uw",
                        [new PromptMessageRecord(
                            "user",
                            "text",
                            arguments["reference"]?.GetValue<string>() ?? string.Empty)])))),
            NullLogger<SessionManager>.Instance);

        var result = await sessionManager.RenderPromptAsync(
            "prompt-session",
            "summarize_submission",
            new JsonObject
            {
                ["reference"] = "RSK-001",
            },
            CancellationToken.None);

        Assert.Equal("prompt-session", result.SessionId);
        Assert.Equal("summarize_submission", result.Name);
        Assert.Equal("uw", result.Server);
        Assert.Collection(
            result.Messages,
            message =>
            {
                Assert.Equal("user", message.Role);
                Assert.Equal("text", message.ContentType);
                Assert.Equal("RSK-001", message.Content);
            });
    }
}
