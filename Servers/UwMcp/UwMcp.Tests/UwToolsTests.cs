using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using UwMcp;

namespace UwMcp.Tests;

public sealed class UwToolsTests
{
    [Fact]
    public async Task GetSubmission_ForwardsCorrelationAndCancellation()
    {
        var handler = new RecordingHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "contract": "RSK123",
                  "status": "quoted"
                }
                """,
                Encoding.UTF8,
                "application/json"),
        }));
        using var client = new HttpClient(handler);
        var cancellationSource = new CancellationTokenSource();
        var service = new UwToolService(
            new SingleClientFactory(client),
            BuildConfiguration(new Dictionary<string, string?>
            {
                ["APIM_BASE_URL"] = "https://apim.example",
                ["SUBMISSION_URL"] = "https://apim.example/submissions",
                ["APIM_SUBSCRIPTION_KEY"] = "Ocp-Apim-Subscription-Key",
                ["APIM_SUBSCRIPTION_KEY_VALUE"] = "sub-key",
            }),
            BuildAccessor("corr-789", cancellationSource.Token),
            NullLogger<UwToolService>.Instance);
        var tools = new UwTools(service);

        var result = await tools.GetSubmission("RSK123");

        Assert.Contains("\"contract\": \"RSK123\"", result);
        Assert.NotNull(handler.LastRequest);
        Assert.Equal("corr-789", handler.LastRequest!.Headers.GetValues("X-Correlation-ID").Single());
        Assert.Equal("sub-key", handler.LastRequest.Headers.GetValues("Ocp-Apim-Subscription-Key").Single());
        Assert.True(handler.LastCancellationToken.CanBeCanceled);
        Assert.Equal("https://apim.example/submissions/RSK123", handler.LastRequest.RequestUri?.ToString());
    }

    [Fact]
    public async Task GetSubmission_WhenApimBaseUrlMissing_ReturnsStructuredError()
    {
        var service = new UwToolService(
            new SingleClientFactory(new HttpClient(new RecordingHttpMessageHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))))),
            BuildConfiguration(new Dictionary<string, string?>
            {
                ["SUBMISSION_URL"] = "https://apim.example/submissions",
            }),
            BuildAccessor("corr-789", CancellationToken.None),
            NullLogger<UwToolService>.Instance);
        var tools = new UwTools(service);

        var result = await tools.GetSubmission("RSK123");

        Assert.Contains("API call failed", result);
        Assert.Contains("APIM_BASE_URL is not configured.", result);
    }

    private static IConfiguration BuildConfiguration(IReadOnlyDictionary<string, string?> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

    private static IHttpContextAccessor BuildAccessor(string correlationId, CancellationToken cancellationToken)
    {
        var context = new DefaultHttpContext
        {
            RequestAborted = cancellationToken,
        };
        context.Request.Headers["X-Correlation-ID"] = correlationId;
        return new HttpContextAccessor { HttpContext = context };
    }
}
