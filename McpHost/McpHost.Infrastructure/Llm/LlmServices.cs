using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace McpHost;

internal sealed class OpenAiConversation
{
    public OpenAiConversation(string model, JsonArray tools, string? systemInstruction)
    {
        Model = model;
        Tools = tools;
        SystemInstruction = systemInstruction;
    }

    public string Model { get; }
    public JsonArray Tools { get; }
    public string? SystemInstruction { get; }
    public string? PreviousResponseId { get; set; }
}

public sealed class OpenAiLlmService(
    string apiKey,
    string? reasoningEffort,
    IHttpClientFactory httpClientFactory) : ILlmService
{
    public object CreateChat(string model, IEnumerable<RegisteredTool> tools, string? systemInstruction = null)
    {
        return new OpenAiConversation(model, BuildTools(tools), systemInstruction);
    }

    public Task<JsonObject> SendUserMessageAsync(object chat, string message, CancellationToken cancellationToken)
    {
        return CreateResponseAsync((OpenAiConversation)chat, JsonValue.Create(message), cancellationToken);
    }

    public Task<JsonObject> SendToolOutputsAsync(
        object chat,
        IReadOnlyList<LlmToolResult> toolResults,
        CancellationToken cancellationToken)
    {
        var inputItems = new JsonArray();
        foreach (var toolResult in toolResults)
        {
            if (string.IsNullOrWhiteSpace(toolResult.Call.CallId))
            {
                throw new InvalidOperationException(
                    $"OpenAI tool result for '{toolResult.Call.Name}' is missing call_id.");
            }

            inputItems.Add(new JsonObject
            {
                ["type"] = "function_call_output",
                ["call_id"] = toolResult.Call.CallId,
                ["output"] = toolResult.Output,
            });
        }

        return CreateResponseAsync((OpenAiConversation)chat, inputItems, cancellationToken);
    }

    public IReadOnlyList<LlmToolCall> ExtractToolCalls(JsonObject response)
    {
        var calls = new List<LlmToolCall>();
        foreach (var item in response["output"]?.AsArray() ?? [])
        {
            if (item?["type"]?.GetValue<string>() != "function_call")
            {
                continue;
            }

            var argumentsNode = item["arguments"];
            JsonObject arguments = new();
            if (argumentsNode is JsonValue value && value.TryGetValue<string>(out var raw))
            {
                arguments = JsonNode.Parse(raw)?.AsObject() ?? new JsonObject();
            }
            else if (argumentsNode is JsonObject objectNode)
            {
                arguments = objectNode;
            }

            calls.Add(new LlmToolCall(
                item["name"]?.GetValue<string>() ?? string.Empty,
                arguments,
                item["call_id"]?.GetValue<string>()));
        }

        return calls;
    }

    public string ExtractTextResponse(JsonObject response)
    {
        var directText = response["output_text"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(directText))
        {
            return directText;
        }

        var parts = new List<string>();
        foreach (var item in response["output"]?.AsArray() ?? [])
        {
            if (item?["type"]?.GetValue<string>() != "message")
            {
                continue;
            }

            foreach (var content in item["content"]?.AsArray() ?? [])
            {
                var text = content?["text"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    parts.Add(text);
                }
            }
        }

        return string.Join("\n", parts);
    }

    private async Task<JsonObject> CreateResponseAsync(
        OpenAiConversation chat,
        JsonNode input,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("openai");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var payload = new JsonObject
        {
            ["model"] = chat.Model,
            ["input"] = input,
        };
        if (!string.IsNullOrWhiteSpace(chat.SystemInstruction))
        {
            payload["instructions"] = chat.SystemInstruction;
        }

        if (chat.Tools.Count > 0)
        {
            payload["tools"] = chat.Tools;
        }

        if (!string.IsNullOrWhiteSpace(chat.PreviousResponseId))
        {
            payload["previous_response_id"] = chat.PreviousResponseId;
        }

        if (!string.IsNullOrWhiteSpace(reasoningEffort))
        {
            payload["reasoning"] = new JsonObject
            {
                ["effort"] = reasoningEffort,
            };
        }

        request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var node = await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("OpenAI returned an empty response.");
        var json = node.AsObject();
        chat.PreviousResponseId = json["id"]?.GetValue<string>();
        return json;
    }

    private static JsonArray BuildTools(IEnumerable<RegisteredTool> tools)
    {
        var items = new JsonArray();
        foreach (var tool in tools)
        {
            items.Add(new JsonObject
            {
                ["type"] = "function",
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["parameters"] = DeepClone(tool.InputSchema),
            });
        }

        return items;
    }

    private static JsonObject DeepClone(JsonObject source) =>
        JsonNode.Parse(source.ToJsonString())?.AsObject() ?? new JsonObject();
}

internal sealed record GeminiConversation(
    string Model,
    JsonArray Tools,
    JsonArray Contents,
    JsonObject? SystemInstruction);

public sealed class GeminiLlmService(string apiKey, IHttpClientFactory httpClientFactory) : ILlmService
{
    public object CreateChat(string model, IEnumerable<RegisteredTool> tools, string? systemInstruction = null)
    {
        return new GeminiConversation(
            model,
            BuildTools(tools),
            new JsonArray(),
            string.IsNullOrWhiteSpace(systemInstruction)
                ? null
                : new JsonObject
                {
                    ["parts"] = new JsonArray(new JsonObject { ["text"] = systemInstruction }),
                });
    }

    public async Task<JsonObject> SendUserMessageAsync(
        object chat,
        string message,
        CancellationToken cancellationToken)
    {
        var conversation = (GeminiConversation)chat;
        conversation.Contents.Add(new JsonObject
        {
            ["role"] = "user",
            ["parts"] = new JsonArray(new JsonObject { ["text"] = message }),
        });

        var response = await GenerateContentAsync(conversation, cancellationToken);
        AppendModelContent(conversation, response);
        return response;
    }

    public async Task<JsonObject> SendToolOutputsAsync(
        object chat,
        IReadOnlyList<LlmToolResult> toolResults,
        CancellationToken cancellationToken)
    {
        var conversation = (GeminiConversation)chat;
        foreach (var toolResult in toolResults)
        {
            conversation.Contents.Add(new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray(
                    new JsonObject
                    {
                        ["functionResponse"] = new JsonObject
                        {
                            ["name"] = toolResult.Call.Name,
                            ["response"] = new JsonObject
                            {
                                ["result"] = toolResult.Output,
                            },
                        },
                    }),
            });
        }

        var response = await GenerateContentAsync(conversation, cancellationToken);
        AppendModelContent(conversation, response);
        return response;
    }

    public IReadOnlyList<LlmToolCall> ExtractToolCalls(JsonObject response)
    {
        var calls = new List<LlmToolCall>();
        foreach (var candidate in response["candidates"]?.AsArray() ?? [])
        {
            foreach (var part in candidate?["content"]?["parts"]?.AsArray() ?? [])
            {
                if (part?["functionCall"] is not JsonObject functionCall)
                {
                    continue;
                }

                calls.Add(new LlmToolCall(
                    functionCall["name"]?.GetValue<string>() ?? string.Empty,
                    functionCall["args"] as JsonObject ?? new JsonObject()));
            }
        }

        return calls;
    }

    public string ExtractTextResponse(JsonObject response)
    {
        var parts = new List<string>();
        foreach (var candidate in response["candidates"]?.AsArray() ?? [])
        {
            foreach (var part in candidate?["content"]?["parts"]?.AsArray() ?? [])
            {
                var text = part?["text"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    parts.Add(text);
                }
            }
        }

        return string.Join("\n", parts);
    }

    private async Task<JsonObject> GenerateContentAsync(
        GeminiConversation conversation,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("gemini");
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{conversation.Model}:generateContent?key={apiKey}";
        var payload = new JsonObject
        {
            ["contents"] = conversation.Contents,
        };

        if (conversation.SystemInstruction is not null)
        {
            payload["systemInstruction"] = conversation.SystemInstruction;
        }

        if (conversation.Tools.Count > 0)
        {
            payload["tools"] = conversation.Tools;
        }

        using var response = await client.PostAsync(
            url,
            new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var node = await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Gemini returned an empty response.");
        return node.AsObject();
    }

    private static void AppendModelContent(GeminiConversation conversation, JsonObject response)
    {
        var content = response["candidates"]?.AsArray().FirstOrDefault()?["content"] as JsonObject;
        if (content is not null)
        {
            conversation.Contents.Add(content);
        }
    }

    private static JsonArray BuildTools(IEnumerable<RegisteredTool> tools)
    {
        var declarations = new JsonArray();
        foreach (var tool in tools)
        {
            declarations.Add(new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["parameters"] = MapSchema(tool.InputSchema),
            });
        }

        return new JsonArray(new JsonObject
        {
            ["functionDeclarations"] = declarations,
        });
    }

    private static JsonObject MapSchema(JsonObject schema)
    {
        var mapped = new JsonObject
        {
            ["type"] = MapType(schema["type"]?.GetValue<string>() ?? "object"),
            ["description"] = schema["description"]?.GetValue<string>() ?? string.Empty,
        };

        if (schema["properties"] is JsonObject properties)
        {
            var mappedProperties = new JsonObject();
            foreach (var property in properties)
            {
                mappedProperties[property.Key] = property.Value is JsonObject child
                    ? MapSchema(child)
                    : new JsonObject();
            }

            mapped["properties"] = mappedProperties;
        }

        if (schema["required"] is JsonArray required)
        {
            mapped["required"] = JsonNode.Parse(required.ToJsonString());
        }

        if (schema["items"] is JsonObject items)
        {
            mapped["items"] = MapSchema(items);
        }

        if (schema["enum"] is JsonArray enumValues)
        {
            mapped["enum"] = JsonNode.Parse(enumValues.ToJsonString());
        }

        return mapped;
    }

    private static string MapType(string jsonType) => jsonType.ToLowerInvariant() switch
    {
        "string" => "STRING",
        "integer" => "INTEGER",
        "number" => "NUMBER",
        "boolean" => "BOOLEAN",
        "array" => "ARRAY",
        _ => "OBJECT",
    };
}
