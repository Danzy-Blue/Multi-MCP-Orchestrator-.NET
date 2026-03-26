using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using McpGateway;
using Microsoft.Extensions.Logging;

namespace McpHost;

public sealed class SessionManager(
    AppConfig appConfig,
    ILlmService llmService,
    IMcpConnectionFactory mcpConnectionFactory,
    ILogger<SessionManager> logger)
    : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, ChatSession> _sessions = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _sessionCreationGate = new(1, 1);
    private readonly ILogger<SessionManager> _logger = logger;

    public AppConfig AppConfig => appConfig;
    public int ActiveSessionCount => _sessions.Count;

    public async Task<ChatResult> HandleChatAsync(
        string? sessionId,
        string message,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Handling chat request for session {SessionId}", sessionId ?? "<new>");
        var session = await GetOrCreateAsync(sessionId, cancellationToken);
        var result = await session.SendMessageAsync(message, cancellationToken);
        return result with { SessionId = session.Id };
    }

    public async Task<bool> CloseAsync(string sessionId, CancellationToken cancellationToken)
    {
        if (_sessions.TryRemove(sessionId, out var session))
        {
            _logger.LogInformation("Closing session {SessionId}", sessionId);
            await session.DisposeAsync();
            return true;
        }

        _logger.LogDebug("Close requested for unknown session {SessionId}", sessionId);
        return false;
    }

    public async Task<PromptListResponse> ListPromptsAsync(string? sessionId, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Listing prompts for session {SessionId}", sessionId ?? "<new>");
        var session = await GetOrCreateAsync(sessionId, cancellationToken);
        return new PromptListResponse(session.Id, session.ListPrompts());
    }

    public async Task<RenderedPromptResponse> RenderPromptAsync(
        string? sessionId,
        string name,
        JsonObject? arguments,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Rendering prompt {PromptName} for session {SessionId}",
            name,
            sessionId ?? "<new>");
        var session = await GetOrCreateAsync(sessionId, cancellationToken);
        var prompt = await session.GetPromptAsync(name, arguments ?? new JsonObject(), cancellationToken);
        return new RenderedPromptResponse(
            session.Id,
            prompt.Name,
            prompt.Title,
            prompt.Description,
            prompt.ServerAlias,
            prompt.Messages);
    }

    public object Snapshot()
    {
        return new
        {
            active_session_count = _sessions.Count,
            sessions = _sessions.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Snapshot(),
                StringComparer.Ordinal),
        };
    }

    public async ValueTask DisposeAsync()
    {
        _logger.LogInformation("Disposing SessionManager with {SessionCount} active sessions", _sessions.Count);
        foreach (var session in _sessions.Values)
        {
            await session.DisposeAsync();
        }

        _sessions.Clear();
        _sessionCreationGate.Dispose();
    }

    public async Task CleanupExpiredSessionsAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var expired = _sessions.Values
            .Where(session => now - session.LastActiveUtc > TimeSpan.FromSeconds(appConfig.SessionTtlSeconds))
            .Select(session => session.Id)
            .ToArray();

        if (expired.Length > 0)
        {
            _logger.LogInformation("Cleaning up {ExpiredCount} expired sessions", expired.Length);
        }

        foreach (var sessionId in expired)
        {
            await CloseAsync(sessionId, cancellationToken);
        }
    }

    private async Task<ChatSession> GetOrCreateAsync(string? sessionId, CancellationToken cancellationToken)
    {
        var resolvedId = string.IsNullOrWhiteSpace(sessionId) ? Guid.NewGuid().ToString() : sessionId;
        if (_sessions.TryGetValue(resolvedId, out var existing))
        {
            existing.Touch();
            _logger.LogDebug("Reusing existing session {SessionId}", resolvedId);
            return existing;
        }

        // Session creation is serialized so only one MCP/LLM state graph is built for a given session id.
        await _sessionCreationGate.WaitAsync(cancellationToken);
        try
        {
            if (_sessions.TryGetValue(resolvedId, out existing))
            {
                existing.Touch();
                _logger.LogDebug("Reusing existing session {SessionId} after create lock", resolvedId);
                return existing;
            }

            _logger.LogInformation("Creating new session {SessionId}", resolvedId);
            var created = new ChatSession(resolvedId, appConfig, llmService, mcpConnectionFactory, _logger);
            await created.InitializeAsync(cancellationToken);
            _sessions[resolvedId] = created;
            return created;
        }
        finally
        {
            _sessionCreationGate.Release();
        }
    }
}

internal sealed class ChatSession(
    string id,
    AppConfig appConfig,
    ILlmService llmService,
    IMcpConnectionFactory mcpConnectionFactory,
    ILogger logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, IMcpConnection> _serverConnections = new(StringComparer.Ordinal);
    private readonly McpRegistry _toolRegistry = new();
    private readonly McpPromptRegistry _promptRegistry = new();
    private readonly ILogger _logger = logger;
    private object? _chat;

    public string Id { get; } = id;
    public DateTimeOffset LastActiveUtc { get; private set; } = DateTimeOffset.UtcNow;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var tools = new List<RegisteredTool>();
        try
        {
            // A chat session owns its own MCP client connections so tool/prompt discovery and any
            // server-side conversational state stay aligned with one host-side conversation.
            foreach (var server in appConfig.ServerConfigs)
            {
                _logger.LogDebug("Session {SessionId} connecting to server {ServerAlias}", Id, server.Alias);
                var connection = mcpConnectionFactory.Create(server);
                var discovered = await connection.ConnectAsync(cancellationToken);
                _serverConnections[server.Alias] = connection;
                _toolRegistry.Extend(discovered.Tools);
                _promptRegistry.Extend(discovered.Prompts);
                tools.AddRange(discovered.Tools);
            }

            _chat = llmService.CreateChat(appConfig.LlmModel, tools, appConfig.SystemInstruction);
            LastActiveUtc = DateTimeOffset.UtcNow;
            _logger.LogInformation(
                "Session {SessionId} initialized with {ServerCount} servers, {ToolCount} tools, and {PromptCount} prompts",
                Id,
                _serverConnections.Count,
                _toolRegistry.Count,
                _promptRegistry.Count);
        }
        catch
        {
            _logger.LogWarning("Session {SessionId} failed during initialization. Disposing partial state", Id);
            await DisposeAsync();
            throw;
        }
    }

    public void Touch() => LastActiveUtc = DateTimeOffset.UtcNow;

    public IReadOnlyList<RegisteredPrompt> ListPrompts()
    {
        Touch();
        _logger.LogDebug("Session {SessionId} returning {PromptCount} prompts", Id, _promptRegistry.Count);
        return _promptRegistry.All();
    }

    public async Task<ChatResult> SendMessageAsync(string message, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_chat is null)
            {
                throw new InvalidOperationException("Session is not initialized.");
            }

            LastActiveUtc = DateTimeOffset.UtcNow;
            _logger.LogDebug("Session {SessionId} sending user message to LLM", Id);
            var response = await llmService.SendUserMessageAsync(_chat, message, cancellationToken);
            var toolCalls = new List<ToolCallRecord>();

            while (true)
            {
                var functionCalls = llmService.ExtractToolCalls(response);
                if (functionCalls.Count == 0)
                {
                    break;
                }

                var toolResults = new List<LlmToolResult>();
                foreach (var functionCall in functionCalls)
                {
                    var toolInfo = _toolRegistry.Get(functionCall.Name);
                    var serverAlias = toolInfo?.ServerAlias;
                    string toolOutput;

                    if (toolInfo is null || string.IsNullOrWhiteSpace(serverAlias)
                        || !_serverConnections.TryGetValue(serverAlias, out var connection))
                    {
                        _logger.LogWarning(
                            "Session {SessionId} could not resolve MCP tool {ToolName} to a registered server",
                            Id,
                            functionCall.Name);
                        toolOutput = $"Tool error: no MCP server registered for tool '{functionCall.Name}'";
                    }
                    else
                    {
                        try
                        {
                            _logger.LogDebug(
                                "Session {SessionId} routing tool {ToolName} to server {ServerAlias}",
                                Id,
                                functionCall.Name,
                                serverAlias);
                            toolOutput = await connection.CallToolAsync(
                                functionCall.Name,
                                functionCall.Args,
                                cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(
                                ex,
                                "Session {SessionId} failed while executing tool {ToolName} on server {ServerAlias}",
                                Id,
                                functionCall.Name,
                                serverAlias);
                            toolOutput = $"Tool error: {ex.Message}";
                        }
                    }

                    toolCalls.Add(new ToolCallRecord(
                        functionCall.Name,
                        serverAlias,
                        DeepClone(functionCall.Args),
                        toolOutput));
                    toolResults.Add(new LlmToolResult(functionCall, toolOutput));
                }

                response = await llmService.SendToolOutputsAsync(_chat, toolResults, cancellationToken);
            }

            _logger.LogDebug("Session {SessionId} completed chat turn with {ToolCallCount} tool calls", Id, toolCalls.Count);
            return new ChatResult(
                string.Empty,
                llmService.ExtractTextResponse(response),
                toolCalls);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<RenderedPrompt> GetPromptAsync(
        string name,
        JsonObject arguments,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            LastActiveUtc = DateTimeOffset.UtcNow;
            var prompt = _promptRegistry.Get(name);
            var serverAlias = prompt?.ServerAlias;
            if (prompt is null || string.IsNullOrWhiteSpace(serverAlias)
                || !_serverConnections.TryGetValue(serverAlias, out var connection))
            {
                _logger.LogWarning("Session {SessionId} could not resolve MCP prompt {PromptName}", Id, name);
                throw new InvalidOperationException($"No MCP server registered for prompt '{name}'.");
            }

            _logger.LogDebug(
                "Session {SessionId} rendering MCP prompt {PromptName} from server {ServerAlias}",
                Id,
                name,
                serverAlias);
            return await connection.GetPromptAsync(name, arguments, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public object Snapshot()
    {
        return new
        {
            session_id = Id,
            last_active_utc = LastActiveUtc,
            servers = _serverConnections.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Tools.Select(tool => tool.Name).ToArray(),
                StringComparer.Ordinal),
            tool_count = _toolRegistry.Count,
            prompt_count = _promptRegistry.Count,
        };
    }

    public async ValueTask DisposeAsync()
    {
        _logger.LogDebug("Disposing session {SessionId}", Id);
        foreach (var connection in _serverConnections.Values)
        {
            await connection.DisposeAsync();
        }

        _serverConnections.Clear();
        _toolRegistry.Clear();
        _promptRegistry.Clear();
        _chat = null;
    }

    private static JsonObject DeepClone(JsonObject source) =>
        JsonNode.Parse(source.ToJsonString())?.AsObject() ?? new JsonObject();
}
