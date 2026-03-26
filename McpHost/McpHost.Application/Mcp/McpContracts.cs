using System.Text.Json.Nodes;

namespace McpHost;

public sealed record ServerConfig(string Alias, string Url);
public sealed record RegisteredTool(string Name, string Description, string ServerAlias, JsonObject InputSchema);
public sealed record PromptArgumentRecord(string Name, string? Title, string? Description, bool Required);
public sealed record RegisteredPrompt(
    string Name,
    string? Title,
    string? Description,
    string ServerAlias,
    IReadOnlyList<PromptArgumentRecord> Arguments);
public sealed record PromptMessageRecord(string Role, string ContentType, string Content);
public sealed record RenderedPrompt(
    string Name,
    string? Title,
    string? Description,
    string ServerAlias,
    IReadOnlyList<PromptMessageRecord> Messages);
public sealed record McpDiscovery(
    IReadOnlyList<RegisteredTool> Tools,
    IReadOnlyList<RegisteredPrompt> Prompts);

public interface IMcpServerConnection : IAsyncDisposable
{
    IReadOnlyList<RegisteredTool> Tools { get; }
    IReadOnlyList<RegisteredPrompt> Prompts { get; }
    Task<McpDiscovery> ConnectAsync(CancellationToken cancellationToken);
    Task<string> CallToolAsync(string toolName, JsonObject arguments, CancellationToken cancellationToken);
    Task<RenderedPrompt> GetPromptAsync(string promptName, JsonObject arguments, CancellationToken cancellationToken);
}

public interface IMcpServerConnectionFactory
{
    IMcpServerConnection Create(ServerConfig config);
}
