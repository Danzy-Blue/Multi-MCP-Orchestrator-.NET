using System.Text.Json.Nodes;
using GatewayConnectionFactory = McpGateway.IMcpConnectionFactory;
using GatewayConnection = McpGateway.IMcpConnection;
using GatewayDiscovery = McpGateway.McpDiscovery;
using GatewayPromptArgumentRecord = McpGateway.PromptArgumentRecord;
using GatewayPromptMessageRecord = McpGateway.PromptMessageRecord;
using GatewayRegisteredPrompt = McpGateway.RegisteredPrompt;
using GatewayRegisteredTool = McpGateway.RegisteredTool;
using GatewayRenderedPrompt = McpGateway.RenderedPrompt;
using GatewayServerConfig = McpGateway.ServerConfig;

namespace McpHost;

public sealed class GatewayMcpConnectionFactory(GatewayConnectionFactory innerFactory) : IMcpServerConnectionFactory
{
    public IMcpServerConnection Create(ServerConfig config)
    {
        var gatewayConfig = new GatewayServerConfig(config.Alias, config.Url);
        return new GatewayMcpConnectionAdapter(innerFactory.Create(gatewayConfig));
    }
}

internal sealed class GatewayMcpConnectionAdapter(GatewayConnection innerConnection) : IMcpServerConnection
{
    public IReadOnlyList<RegisteredTool> Tools { get; private set; } = [];
    public IReadOnlyList<RegisteredPrompt> Prompts { get; private set; } = [];

    public async Task<McpDiscovery> ConnectAsync(CancellationToken cancellationToken)
    {
        var discovery = await innerConnection.ConnectAsync(cancellationToken);
        Tools = discovery.Tools.Select(Map).ToArray();
        Prompts = discovery.Prompts.Select(Map).ToArray();
        return new McpDiscovery(Tools, Prompts);
    }

    public Task<string> CallToolAsync(string toolName, JsonObject arguments, CancellationToken cancellationToken) =>
        innerConnection.CallToolAsync(toolName, arguments, cancellationToken);

    public async Task<RenderedPrompt> GetPromptAsync(
        string promptName,
        JsonObject arguments,
        CancellationToken cancellationToken)
    {
        var prompt = await innerConnection.GetPromptAsync(promptName, arguments, cancellationToken);
        return Map(prompt);
    }

    public ValueTask DisposeAsync() => innerConnection.DisposeAsync();

    private static RegisteredTool Map(GatewayRegisteredTool tool) =>
        new(tool.Name, tool.Description, tool.ServerAlias, Clone(tool.InputSchema));

    private static RegisteredPrompt Map(GatewayRegisteredPrompt prompt) =>
        new(
            prompt.Name,
            prompt.Title,
            prompt.Description,
            prompt.ServerAlias,
            prompt.Arguments.Select(Map).ToArray());

    private static PromptArgumentRecord Map(GatewayPromptArgumentRecord argument) =>
        new(argument.Name, argument.Title, argument.Description, argument.Required);

    private static RenderedPrompt Map(GatewayRenderedPrompt prompt) =>
        new(
            prompt.Name,
            prompt.Title,
            prompt.Description,
            prompt.ServerAlias,
            prompt.Messages.Select(Map).ToArray());

    private static PromptMessageRecord Map(GatewayPromptMessageRecord message) =>
        new(message.Role, message.ContentType, message.Content);

    private static JsonObject Clone(JsonObject source) =>
        JsonNode.Parse(source.ToJsonString())?.AsObject() ?? new JsonObject();
}
