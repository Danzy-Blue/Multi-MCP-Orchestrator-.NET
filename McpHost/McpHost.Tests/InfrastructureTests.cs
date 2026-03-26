using System.Net;
using System.Text.Json.Nodes;
using McpGateway;

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
}
