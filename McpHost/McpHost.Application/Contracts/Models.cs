using System.Text.Json.Nodes;

namespace McpHost;

public sealed record ChatRequest(string Message, string? SessionId);
public sealed record CloseSessionRequest(string SessionId);
public sealed record PromptRenderRequest(string Name, JsonObject? Arguments, string? SessionId);
public sealed record ToolCallRecord(string Name, string? Server, JsonObject Args, string Result);
public sealed record ChatResponse(string SessionId, string Reply, IReadOnlyList<ToolCallRecord> ToolCalls);
public sealed record PromptListResponse(string SessionId, IReadOnlyList<RegisteredPrompt> Prompts);
public sealed record RenderedPromptResponse(
    string SessionId,
    string Name,
    string? Title,
    string? Description,
    string Server,
    IReadOnlyList<PromptMessageRecord> Messages);

public sealed record LlmToolCall(string Name, JsonObject Args, string? CallId = null);
public sealed record LlmToolResult(LlmToolCall Call, string Output);
public sealed record ChatResult(string SessionId, string Reply, IReadOnlyList<ToolCallRecord> ToolCalls);
