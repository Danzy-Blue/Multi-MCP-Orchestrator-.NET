using System.Net;
using System.Text.Json.Nodes;
using McpHost;

namespace McpHost.Tests;

internal sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => client;
}

internal sealed class RecordingHttpMessageHandler(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler = handler;

    public HttpRequestMessage? LastRequest { get; private set; }
    public CancellationToken LastCancellationToken { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        LastCancellationToken = cancellationToken;
        return _handler(request, cancellationToken);
    }
}

internal sealed class CoordinatedLlmService : ILlmService
{
    private readonly ManualResetEventSlim _createChatEntered = new(false);
    private readonly ManualResetEventSlim _releaseCreateChat = new(false);
    private int _createChatCallCount;

    public int CreateChatCallCount => _createChatCallCount;
    public ManualResetEventSlim CreateChatEntered => _createChatEntered;

    public void ReleaseCreateChat() => _releaseCreateChat.Set();

    public object CreateChat(string model, IEnumerable<RegisteredTool> tools, string? systemInstruction = null)
    {
        if (Interlocked.Increment(ref _createChatCallCount) == 1)
        {
            _createChatEntered.Set();
            _releaseCreateChat.Wait(TimeSpan.FromSeconds(5));
        }

        return new object();
    }

    public Task<JsonObject> SendUserMessageAsync(object chat, string message, CancellationToken cancellationToken) =>
        Task.FromResult(new JsonObject
        {
            ["reply"] = $"echo:{message}",
        });

    public Task<JsonObject> SendToolOutputsAsync(
        object chat,
        IReadOnlyList<LlmToolResult> toolResults,
        CancellationToken cancellationToken) =>
        Task.FromResult(new JsonObject());

    public IReadOnlyList<LlmToolCall> ExtractToolCalls(JsonObject response) => [];

    public string ExtractTextResponse(JsonObject response) => response["reply"]?.GetValue<string>() ?? string.Empty;
}

internal sealed class StubLlmService : ILlmService
{
    public object CreateChat(string model, IEnumerable<RegisteredTool> tools, string? systemInstruction = null) => new object();

    public Task<JsonObject> SendUserMessageAsync(object chat, string message, CancellationToken cancellationToken) =>
        Task.FromResult(new JsonObject
        {
            ["reply"] = message,
        });

    public Task<JsonObject> SendToolOutputsAsync(
        object chat,
        IReadOnlyList<LlmToolResult> toolResults,
        CancellationToken cancellationToken) =>
        Task.FromResult(new JsonObject());

    public IReadOnlyList<LlmToolCall> ExtractToolCalls(JsonObject response) => [];

    public string ExtractTextResponse(JsonObject response) => response["reply"]?.GetValue<string>() ?? string.Empty;
}

internal sealed class ToolCapturingLlmService : ILlmService
{
    public int LastCreateChatToolCount { get; private set; } = -1;

    public object CreateChat(string model, IEnumerable<RegisteredTool> tools, string? systemInstruction = null)
    {
        LastCreateChatToolCount = tools.Count();
        return new object();
    }

    public Task<JsonObject> SendUserMessageAsync(object chat, string message, CancellationToken cancellationToken) =>
        Task.FromResult(new JsonObject
        {
            ["reply"] = message,
        });

    public Task<JsonObject> SendToolOutputsAsync(
        object chat,
        IReadOnlyList<LlmToolResult> toolResults,
        CancellationToken cancellationToken) =>
        Task.FromResult(new JsonObject());

    public IReadOnlyList<LlmToolCall> ExtractToolCalls(JsonObject response) => [];

    public string ExtractTextResponse(JsonObject response) => response["reply"]?.GetValue<string>() ?? string.Empty;
}

internal sealed class FakeMcpConnectionFactory(Func<ServerConfig, IMcpServerConnection> factory) : IMcpServerConnectionFactory
{
    private readonly Func<ServerConfig, IMcpServerConnection> _factory = factory;

    public IMcpServerConnection Create(ServerConfig config) => _factory(config);
}

internal sealed class FakeMcpConnection(
    IReadOnlyList<RegisteredTool>? tools = null,
    IReadOnlyList<RegisteredPrompt>? prompts = null,
    Func<string, JsonObject, CancellationToken, Task<string>>? callToolAsync = null,
    Func<string, JsonObject, CancellationToken, Task<RenderedPrompt>>? getPromptAsync = null) : IMcpServerConnection
{
    private readonly Func<string, JsonObject, CancellationToken, Task<string>> _callToolAsync =
        callToolAsync ?? ((_, _, _) => Task.FromResult(string.Empty));
    private readonly Func<string, JsonObject, CancellationToken, Task<RenderedPrompt>> _getPromptAsync =
        getPromptAsync ?? ((name, _, _) => Task.FromResult(
            new RenderedPrompt(name, null, null, "test", [])));

    public IReadOnlyList<RegisteredTool> Tools { get; private set; } = tools ?? [];
    public IReadOnlyList<RegisteredPrompt> Prompts { get; private set; } = prompts ?? [];

    public Task<McpDiscovery> ConnectAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new McpDiscovery(Tools, Prompts));

    public Task<string> CallToolAsync(string toolName, JsonObject arguments, CancellationToken cancellationToken) =>
        _callToolAsync(toolName, arguments, cancellationToken);

    public Task<RenderedPrompt> GetPromptAsync(string promptName, JsonObject arguments, CancellationToken cancellationToken) =>
        _getPromptAsync(promptName, arguments, cancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
