using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace McpGateway;

public sealed class McpConnection(ServerConfig config, IHttpClientFactory httpClientFactory, ILogger<McpConnection> logger)
    : IMcpConnection
{
    private readonly ServerConfig _config = config;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly ILogger<McpConnection> _logger = logger;
    private McpClient? _client;
    private Dictionary<string, McpClientTool> _toolMap = new(StringComparer.Ordinal);
    private Dictionary<string, McpClientPrompt> _promptMap = new(StringComparer.Ordinal);

    public IReadOnlyList<RegisteredTool> Tools { get; private set; } = [];
    public IReadOnlyList<RegisteredPrompt> Prompts { get; private set; } = [];

    public async Task<McpDiscovery> ConnectAsync(CancellationToken cancellationToken)
    {
        // Each gateway connection owns exactly one MCP client/session for one remote server.
        // Discovery runs once per connection so later tool/prompt calls can route locally.
        _logger.LogInformation("Connecting to MCP server {ServerAlias} at {ServerUrl}", _config.Alias, _config.Url);
        var httpClient = _httpClientFactory.CreateClient($"mcp-{_config.Alias}");
        httpClient.Timeout = TimeSpan.FromSeconds(135);

        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(_config.Url),
                Name = $"mcp-{_config.Alias}",
                TransportMode = HttpTransportMode.StreamableHttp,
            },
            httpClient);
        _client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
        var tools = await TryListToolsAsync(cancellationToken);
        var prompts = await TryListPromptsAsync(cancellationToken);
        _toolMap = tools.ToDictionary(tool => tool.Name, StringComparer.Ordinal);
        _promptMap = prompts.ToDictionary(prompt => prompt.Name, StringComparer.Ordinal);

        Tools = tools
            .Select(tool => new RegisteredTool(
                tool.Name,
                tool.Description,
                _config.Alias,
                ParseSchema(tool.JsonSchema)))
            .ToArray();
        Prompts = prompts
            .Select(prompt => new RegisteredPrompt(
                prompt.Name,
                prompt.Title,
                prompt.Description,
                _config.Alias,
                prompt.ProtocolPrompt.Arguments?
                    .Select(argument => new PromptArgumentRecord(
                        argument.Name,
                        argument.Title,
                        argument.Description,
                        argument.Required ?? false))
                    .ToArray()
                    ?? []))
            .ToArray();
        _logger.LogInformation(
            "Connected to MCP server {ServerAlias}. Discovered {ToolCount} tools and {PromptCount} prompts",
            _config.Alias,
            Tools.Count,
            Prompts.Count);
        return new McpDiscovery(Tools, Prompts);
    }

    public Task<string> CallToolAsync(string toolName, JsonObject arguments, CancellationToken cancellationToken)
    {
        if (_client is null)
        {
            throw new InvalidOperationException($"MCP session '{_config.Alias}' is not connected.");
        }

        return CallToolCoreAsync(toolName, arguments, cancellationToken);
    }

    public Task<RenderedPrompt> GetPromptAsync(string promptName, JsonObject arguments, CancellationToken cancellationToken)
    {
        if (_client is null)
        {
            throw new InvalidOperationException($"MCP session '{_config.Alias}' is not connected.");
        }

        return GetPromptCoreAsync(promptName, arguments, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_client is not null)
        {
            _logger.LogDebug("Disposing MCP client for server {ServerAlias}", _config.Alias);
            await _client.DisposeAsync();
            _client = null;
        }

        _toolMap.Clear();
        _promptMap.Clear();
    }

    private async Task<string> CallToolCoreAsync(
        string toolName,
        JsonObject arguments,
        CancellationToken cancellationToken)
    {
        if (!_toolMap.TryGetValue(toolName, out var tool))
        {
            throw new InvalidOperationException($"Tool '{toolName}' is not available on server '{_config.Alias}'.");
        }

        var args = DeserializeArguments(arguments);
        _logger.LogDebug(
            "Calling MCP tool {ToolName} on server {ServerAlias} with {ArgumentCount} arguments",
            toolName,
            _config.Alias,
            args.Count);
        var result = await tool.CallAsync(args, cancellationToken: cancellationToken);
        var lines = result.Content
            .Select(FormatContent)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();

        return lines.Length == 0 ? string.Empty : string.Join("\n", lines);
    }

    private async Task<RenderedPrompt> GetPromptCoreAsync(
        string promptName,
        JsonObject arguments,
        CancellationToken cancellationToken)
    {
        if (!_promptMap.TryGetValue(promptName, out var prompt))
        {
            throw new InvalidOperationException($"Prompt '{promptName}' is not available on server '{_config.Alias}'.");
        }

        var args = DeserializeArguments(arguments);
        // Prompts are fetched on demand. The host does not execute them automatically like tools;
        // it asks the server to render prompt content for the caller-supplied arguments.
        _logger.LogDebug(
            "Rendering MCP prompt {PromptName} on server {ServerAlias} with {ArgumentCount} arguments",
            promptName,
            _config.Alias,
            args.Count);
        var result = await prompt.GetAsync(args, cancellationToken: cancellationToken);
        return new RenderedPrompt(
            prompt.Name,
            prompt.Title,
            result.Description ?? prompt.Description,
            _config.Alias,
            result.Messages.Select(FormatPromptMessage).ToArray());
    }

    private async Task<IList<McpClientTool>> TryListToolsAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _client!.ListToolsAsync(cancellationToken: cancellationToken);
        }
        catch (McpProtocolException ex) when (IsCapabilityUnavailable(ex, "tools/list"))
        {
            _logger.LogInformation(
                ex,
                "MCP server {ServerAlias} does not expose tools/list. Continuing without tool discovery",
                _config.Alias);
            return [];
        }
    }

    private async Task<IList<McpClientPrompt>> TryListPromptsAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _client!.ListPromptsAsync(cancellationToken: cancellationToken);
        }
        catch (McpProtocolException ex) when (IsCapabilityUnavailable(ex, "prompts/list"))
        {
            _logger.LogInformation(
                ex,
                "MCP server {ServerAlias} does not expose prompts/list. Continuing without prompt discovery",
                _config.Alias);
            return [];
        }
    }

    private static Dictionary<string, object?> DeserializeArguments(JsonObject arguments) =>
        JsonSerializer.Deserialize<Dictionary<string, object?>>(
            arguments.ToJsonString(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
        ?? new Dictionary<string, object?>(StringComparer.Ordinal);

    private static JsonObject ParseSchema(JsonElement jsonSchema) =>
        JsonNode.Parse(jsonSchema.GetRawText())?.AsObject() ?? new JsonObject();

    // Prompt messages can carry multiple MCP content types. The gateway currently keeps text verbatim
    // and reduces non-text blocks to placeholders until resource support is added.
    private static PromptMessageRecord FormatPromptMessage(PromptMessage message) => new(
        message.Role.ToString().ToLowerInvariant(),
        GetContentType(message.Content),
        FormatContent(message.Content) ?? string.Empty);

    private static string GetContentType(ContentBlock content) =>
        content switch
        {
            TextContentBlock => "text",
            ImageContentBlock => "image",
            AudioContentBlock => "audio",
            EmbeddedResourceBlock => "embedded_resource",
            ResourceLinkBlock => "resource_link",
            _ => content.GetType().Name,
        };

    private static string? FormatContent(ContentBlock content) =>
        content switch
        {
            TextContentBlock text => text.Text,
            EmbeddedResourceBlock => "[embedded resource omitted]",
            ResourceLinkBlock => "[resource link omitted]",
            ImageContentBlock => "[image omitted]",
            AudioContentBlock => "[audio omitted]",
            _ => content.ToString(),
        };

    private static bool IsCapabilityUnavailable(McpProtocolException exception, string methodName) =>
        exception.ErrorCode == McpErrorCode.MethodNotFound
        || exception.Message.Contains(methodName, StringComparison.Ordinal);
}
