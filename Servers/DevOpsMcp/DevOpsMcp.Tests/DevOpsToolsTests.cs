using System.Net;
using System.Text;
using DevOpsMcp;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace DevOpsMcp.Tests;

public sealed class DevOpsToolsTests
{
    [Fact]
    public async Task FetchWorkItem_ForwardsAuthAndCorrelationHeaders()
    {
        var handler = new RecordingHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "id": 123,
                  "fields": {
                    "System.Title": "Test work item",
                    "System.State": "Active",
                    "System.WorkItemType": "Bug",
                    "System.Description": "<p>Investigate issue</p>",
                    "Microsoft.VSTS.Common.AcceptanceCriteria": "<p>Done</p>",
                    "System.AssignedTo": { "displayName": "Alex" },
                    "System.CreatedBy": { "displayName": "Pat" },
                    "System.IterationPath": "Sprint 1",
                    "System.AreaPath": "Area 1",
                    "System.Tags": "tag-a; tag-b"
                  }
                }
                """,
                Encoding.UTF8,
                "application/json"),
        }));
        using var client = new HttpClient(handler);
        var service = new DevOpsWorkItemService(
            new SingleClientFactory(client),
            BuildConfiguration(new Dictionary<string, string?>
            {
                ["TFS_BASE_URL"] = "https://tfs.example",
            }),
            BuildAccessor("pat-123", "corr-456"),
            NullLogger<DevOpsWorkItemService>.Instance);
        var tools = new DevOpsTools(service);

        var result = await tools.FetchWorkItem(123);

        Assert.Contains("## Bug #123: Test work item", result);
        Assert.Contains("Investigate issue", result);
        Assert.DoesNotContain("<p>", result);
        Assert.NotNull(handler.LastRequest);
        Assert.Equal(
            $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes(":pat-123"))}",
            handler.LastRequest!.Headers.GetValues("Authorization").Single());
        Assert.Equal("corr-456", handler.LastRequest.Headers.GetValues("X-Correlation-ID").Single());
    }

    [Fact]
    public async Task FetchWorkItem_WithoutPat_ReturnsHelpfulError()
    {
        var service = new DevOpsWorkItemService(
            new SingleClientFactory(new HttpClient(new RecordingHttpMessageHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))))),
            BuildConfiguration(new Dictionary<string, string?>
            {
                ["TFS_BASE_URL"] = "https://tfs.example",
            }),
            new HttpContextAccessor { HttpContext = new DefaultHttpContext() },
            NullLogger<DevOpsWorkItemService>.Instance);
        var tools = new DevOpsTools(service);

        var result = await tools.FetchWorkItem(123);

        Assert.Contains("Missing TFS PAT", result);
    }

    private static IConfiguration BuildConfiguration(IReadOnlyDictionary<string, string?> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

    private static IHttpContextAccessor BuildAccessor(string pat, string correlationId)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-TFS-PAT"] = pat;
        context.Request.Headers["X-Correlation-ID"] = correlationId;
        return new HttpContextAccessor { HttpContext = context };
    }
}
