using System.Text.Json.Nodes;

namespace McpHost;

public interface ILlmService
{
    object CreateChat(string model, IEnumerable<RegisteredTool> tools, string? systemInstruction = null);
    Task<JsonObject> SendUserMessageAsync(object chat, string message, CancellationToken cancellationToken);
    Task<JsonObject> SendToolOutputsAsync(
        object chat,
        IReadOnlyList<LlmToolResult> toolResults,
        CancellationToken cancellationToken);
    IReadOnlyList<LlmToolCall> ExtractToolCalls(JsonObject response);
    string ExtractTextResponse(JsonObject response);
}
