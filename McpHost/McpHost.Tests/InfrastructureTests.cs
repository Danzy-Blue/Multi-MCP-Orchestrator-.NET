using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using McpHost;

namespace McpHost.Tests;

public sealed class InfrastructureTests
{
    [Fact]
    public async Task CorrelationIdHandler_AddsCorrelationHeader_WhenMissing()
    {
        var accessor = new CorrelationContextAccessor
        {
            CorrelationId = "corr-123",
        };
        var inner = new RecordingHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var handler = new CorrelationIdHandler(accessor)
        {
            InnerHandler = inner,
        };
        using var invoker = new HttpMessageInvoker(handler);

        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://example.test"), CancellationToken.None);

        Assert.NotNull(inner.LastRequest);
        Assert.Equal(
            "corr-123",
            inner.LastRequest!.Headers.GetValues(CorrelationConstants.HeaderName).Single());
    }

    [Fact]
    public void McpRegistry_Extend_ThrowsOnDuplicateToolNames()
    {
        var registry = new McpRegistry();
        registry.Extend([
            new RegisteredTool("search", "First", "uw", new JsonObject()),
        ]);

        var error = Assert.Throws<InvalidOperationException>(() => registry.Extend([
            new RegisteredTool("search", "Second", "devops", new JsonObject()),
        ]));

        Assert.Contains("Duplicate tool name 'search'", error.Message);
        Assert.Contains("'uw'", error.Message);
        Assert.Contains("'devops'", error.Message);
    }

    [Fact]
    public async Task GeminiLlmService_MapsUnionSchemaTypes_AndClonesResponseContent_WithoutThrowing()
    {
        var handler = new RecordingHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new JsonObject
            {
                ["candidates"] = new JsonArray(
                    new JsonObject
                    {
                        ["content"] = new JsonObject
                        {
                            ["parts"] = new JsonArray(new JsonObject { ["text"] = "ok" }),
                        },
                    }),
            }),
        }));
        using var client = new HttpClient(handler);
        var service = new GeminiLlmService("test-key", new SingleClientFactory(client));
        var tool = new RegisteredTool(
            "search",
            "Search records",
            "uw",
            new JsonObject
            {
                ["type"] = new JsonArray(JsonValue.Create("object")),
                ["properties"] = new JsonObject
                {
                    ["query"] = new JsonObject
                    {
                        ["type"] = new JsonArray(JsonValue.Create("string"), JsonValue.Create("null")),
                        ["description"] = "Search text",
                    },
                },
                ["required"] = new JsonArray(JsonValue.Create("query")),
            });

        var chat = service.CreateChat("gemini-2.5-flash", [tool]);
        await service.SendUserMessageAsync(chat, "hello", CancellationToken.None);
        await service.SendUserMessageAsync(chat, "hello again", CancellationToken.None);

        Assert.NotNull(handler.LastRequest);
        var payload = JsonNode.Parse(await handler.LastRequest!.Content!.ReadAsStringAsync())!.AsObject();
        var schema = payload["tools"]!.AsArray()[0]!["functionDeclarations"]!.AsArray()[0]!["parameters"]!.AsObject();
        var queryProperty = schema["properties"]!.AsObject()["query"]!.AsObject();

        Assert.Equal("OBJECT", schema["type"]!.GetValue<string>());
        Assert.Equal("STRING", queryProperty["type"]!.GetValue<string>());
        Assert.True(payload["contents"]!.AsArray().Count >= 3);
    }
}
