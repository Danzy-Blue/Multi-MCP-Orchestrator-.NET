using System.Text.Json.Nodes;

namespace McpGateway;

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
