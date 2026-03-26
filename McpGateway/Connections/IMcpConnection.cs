using System.Text.Json.Nodes;

namespace McpGateway;

public interface IMcpConnection : IAsyncDisposable
{
    IReadOnlyList<RegisteredTool> Tools { get; }
    IReadOnlyList<RegisteredPrompt> Prompts { get; }
    Task<McpDiscovery> ConnectAsync(CancellationToken cancellationToken);
    Task<string> CallToolAsync(string toolName, JsonObject arguments, CancellationToken cancellationToken);
    Task<RenderedPrompt> GetPromptAsync(string promptName, JsonObject arguments, CancellationToken cancellationToken);
}

public interface IMcpConnectionFactory
{
    IMcpConnection Create(ServerConfig config);
}
