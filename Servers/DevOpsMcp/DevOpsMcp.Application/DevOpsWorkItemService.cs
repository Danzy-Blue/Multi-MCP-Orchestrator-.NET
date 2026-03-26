using System.Net;
using System.Text;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DevOpsMcp;

public sealed class DevOpsWorkItemService(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    IHttpContextAccessor httpContextAccessor,
    ILogger<DevOpsWorkItemService> logger)
{
    private const string CorrelationHeaderName = "X-Correlation-ID";

    public async Task<string> FetchWorkItemAsync(int work_item_id)
    {
        try
        {
            var authHeader = ResolveAuthHeader();
            var data = await GetWorkItemAsync(work_item_id, authHeader, RequestAborted);
            return FormatWorkItem(data);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch work item {ItemId}", work_item_id);
            return $"**Error:** {HandleApiError(ex, work_item_id)}";
        }
    }

    public async Task<string> SearchWorkItemsAsync(string query, string? work_item_type = null, int limit = 10)
    {
        try
        {
            var authHeader = ResolveAuthHeader();
            limit = Math.Clamp(limit, 1, 50);

            var typeClause = string.IsNullOrWhiteSpace(work_item_type)
                ? string.Empty
                : $" AND [System.WorkItemType] = '{EscapeWiql(work_item_type)}'";
            var wiql = new JsonObject
            {
                ["query"] =
                    $"SELECT [System.Id], [System.Title], [System.State], [System.WorkItemType], [System.AssignedTo] " +
                    $"FROM WorkItems WHERE ([System.Title] CONTAINS '{EscapeWiql(query)}' " +
                    $"OR [System.Description] CONTAINS '{EscapeWiql(query)}'){typeClause} " +
                    "ORDER BY [System.ChangedDate] DESC",
            };

            var wiqlUrl = $"{TfsBaseUrl}/_apis/wit/wiql?$top={limit}&api-version=7.1";
            var wiqlResponse = await SendJsonAsync(HttpMethod.Post, wiqlUrl, authHeader, wiql, RequestAborted);
            var ids = wiqlResponse?["workItems"]?.AsArray()
                .Select(item => item?["id"]?.GetValue<int>())
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .Take(limit)
                .ToArray() ?? [];

            if (ids.Length == 0)
            {
                return $"No work items found for **'{query}'**.";
            }

            var detailUrl =
                $"{TfsBaseUrl}/_apis/wit/workitems?ids={string.Join(",", ids)}" +
                "&fields=System.Id,System.Title,System.State,System.WorkItemType,System.AssignedTo&api-version=7.1";
            var details = await SendJsonAsync(HttpMethod.Get, detailUrl, authHeader, null, RequestAborted);
            var lines = new List<string> { $"### Results for '{query}' ({ids.Length} found)", string.Empty };
            foreach (var item in details?["value"]?.AsArray() ?? [])
            {
                var fields = item?["fields"] as JsonObject ?? new JsonObject();
                var assigned = fields["System.AssignedTo"]?["displayName"]?.GetValue<string>() ?? "Unassigned";
                lines.Add(
                    $"- **#{item?["id"]?.GetValue<int>()}** " +
                    $"[{fields["System.WorkItemType"]?.GetValue<string>() ?? "?"}] " +
                    $"{fields["System.Title"]?.GetValue<string>() ?? "?"} - " +
                    $"*{fields["System.State"]?.GetValue<string>() ?? "?"}* - {assigned}");
            }

            return string.Join("\n", lines);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to search work items");
            return $"**Error:** {HandleApiError(ex, 0)}";
        }
    }

    public async Task<string> ListSprintItemsAsync(string iteration_path, int limit = 20)
    {
        try
        {
            var authHeader = ResolveAuthHeader();
            limit = Math.Clamp(limit, 1, 100);
            var wiql = new JsonObject
            {
                ["query"] =
                    "SELECT [System.Id], [System.Title], [System.State], [System.WorkItemType], [System.AssignedTo], [Microsoft.VSTS.Scheduling.StoryPoints] " +
                    $"FROM WorkItems WHERE [System.IterationPath] = '{EscapeWiql(iteration_path)}' " +
                    "ORDER BY [System.WorkItemType], [System.State]",
            };

            var wiqlUrl = $"{TfsBaseUrl}/_apis/wit/wiql?$top={limit}&api-version=7.1";
            var idsResponse = await SendJsonAsync(HttpMethod.Post, wiqlUrl, authHeader, wiql, RequestAborted);
            var ids = idsResponse?["workItems"]?.AsArray()
                .Select(item => item?["id"]?.GetValue<int>())
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .Take(limit)
                .ToArray() ?? [];

            if (ids.Length == 0)
            {
                return $"No items found for sprint **{iteration_path}**.";
            }

            var detailUrl =
                $"{TfsBaseUrl}/_apis/wit/workitems?ids={string.Join(",", ids)}" +
                "&fields=System.Id,System.Title,System.State,System.WorkItemType,System.AssignedTo,Microsoft.VSTS.Scheduling.StoryPoints&api-version=7.1";
            var items = await SendJsonAsync(HttpMethod.Get, detailUrl, authHeader, null, RequestAborted);

            var lines = new List<string>
            {
                $"### Sprint: {iteration_path} ({ids.Length} items)",
                string.Empty,
                "| ID | Type | Title | State | Points | Assigned |",
                "|----|------|-------|-------|--------|----------|",
            };

            foreach (var item in items?["value"]?.AsArray() ?? [])
            {
                var fields = item?["fields"] as JsonObject ?? new JsonObject();
                var assigned = fields["System.AssignedTo"]?["displayName"]?.GetValue<string>() ?? "Unassigned";
                var points = fields["Microsoft.VSTS.Scheduling.StoryPoints"]?.GetValue<int?>()?.ToString() ?? "-";
                lines.Add(
                    $"| #{item?["id"]?.GetValue<int>()} " +
                    $"| {fields["System.WorkItemType"]?.GetValue<string>() ?? "?"} " +
                    $"| {fields["System.Title"]?.GetValue<string>() ?? "?"} " +
                    $"| {fields["System.State"]?.GetValue<string>() ?? "?"} " +
                    $"| {points} " +
                    $"| {assigned} |");
            }

            return string.Join("\n", lines);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to list sprint items");
            return $"**Error:** {HandleApiError(ex, 0)}";
        }
    }

    private HttpContext HttpContext =>
        httpContextAccessor.HttpContext ?? throw new InvalidOperationException("Missing HTTP context.");

    private CancellationToken RequestAborted => HttpContext.RequestAborted;

    private string TfsBaseUrl => (configuration["TFS_BASE_URL"] ?? string.Empty).TrimEnd('/');

    private Dictionary<string, string> ResolveAuthHeader()
    {
        var pat = NormalizePat(HttpContext.Request.Headers["X-TFS-PAT"].FirstOrDefault())
            ?? NormalizePat(HttpContext.Request.Headers.Authorization.ToString())
            ?? NormalizePat(configuration["TFS_PAT"]);

        if (string.IsNullOrWhiteSpace(pat))
        {
            throw new InvalidOperationException("Missing TFS PAT. Set TFS_PAT or send X-TFS-PAT.");
        }

        var patBytes = Encoding.UTF8.GetBytes($":{pat}");
        return new Dictionary<string, string>
        {
            ["Authorization"] = $"Basic {Convert.ToBase64String(patBytes)}",
        };
    }

    private static string? NormalizePat(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[7..].Trim();
        }

        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private async Task<JsonObject> GetWorkItemAsync(
        int itemId,
        IReadOnlyDictionary<string, string> authHeader,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(TfsBaseUrl))
        {
            throw new InvalidOperationException("Missing TFS_BASE_URL environment variable.");
        }

        var url = $"{TfsBaseUrl}/_apis/wit/workitems/{itemId}?$expand=all&api-version=7.1";
        return await SendJsonAsync(HttpMethod.Get, url, authHeader, null, cancellationToken)
            ?? new JsonObject();
    }

    private async Task<JsonObject?> SendJsonAsync(
        HttpMethod method,
        string url,
        IReadOnlyDictionary<string, string> headers,
        JsonObject? payload,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient("devops-api");
        client.Timeout = TimeSpan.FromSeconds(35);
        using var request = new HttpRequestMessage(method, url);
        foreach (var header in headers)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        var correlationId = HttpContext.Request.Headers[CorrelationHeaderName].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            request.Headers.TryAddWithoutValidation(CorrelationHeaderName, correlationId);
        }

        if (payload is not null)
        {
            request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        }

        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(body, null, response.StatusCode);
        }

        return await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellationToken);
    }

    private static string FormatWorkItem(JsonObject data)
    {
        var fields = data["fields"] as JsonObject ?? new JsonObject();
        var title = fields["System.Title"]?.GetValue<string>() ?? "-";
        var state = fields["System.State"]?.GetValue<string>() ?? "-";
        var workType = fields["System.WorkItemType"]?.GetValue<string>() ?? "Work Item";
        var description = StripHtml(fields["System.Description"]?.GetValue<string>() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(description))
        {
            description = "No description provided.";
        }

        var acceptance = StripHtml(fields["Microsoft.VSTS.Common.AcceptanceCriteria"]?.GetValue<string>() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(acceptance))
        {
            acceptance = "None specified.";
        }

        var assignedTo = fields["System.AssignedTo"]?["displayName"]?.GetValue<string>() ?? "Unassigned";
        var storyPoints = fields["Microsoft.VSTS.Scheduling.StoryPoints"]?.GetValue<int?>()?.ToString() ?? "-";
        var priority = fields["Microsoft.VSTS.Common.Priority"]?.GetValue<int?>()?.ToString() ?? "-";
        var iteration = fields["System.IterationPath"]?.GetValue<string>() ?? "-";
        var area = fields["System.AreaPath"]?.GetValue<string>() ?? "-";
        var createdBy = fields["System.CreatedBy"]?["displayName"]?.GetValue<string>() ?? "-";
        var tags = fields["System.Tags"]?.GetValue<string>() ?? "None";

        return string.Join("\n", new[]
        {
            $"## {workType} #{data["id"]?.GetValue<int>()}: {title}",
            string.Empty,
            "| Field | Value |",
            "|-------|--------|",
            $"| **State** | {state} |",
            $"| **Priority** | {priority} |",
            $"| **Points** | {storyPoints} |",
            $"| **Assigned to** | {assignedTo} |",
            $"| **Created by** | {createdBy} |",
            $"| **Sprint** | {iteration} |",
            $"| **Area** | {area} |",
            $"| **Tags** | {tags} |",
            string.Empty,
            "### Description",
            description,
            string.Empty,
            "### Acceptance Criteria",
            acceptance,
        });
    }

    private static string StripHtml(string text)
    {
        var clean = Regex.Replace(text, "<[^>]+>", string.Empty);
        return Regex.Replace(clean, "\n{3,}", "\n\n").Trim();
    }

    private static string EscapeWiql(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static string HandleApiError(Exception exception, int itemId)
    {
        if (exception is HttpRequestException { StatusCode: HttpStatusCode.NotFound })
        {
            return $"Work item #{itemId} not found.";
        }

        if (exception is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized })
        {
            return "Authentication failed - check your PAT.";
        }

        if (exception is HttpRequestException { StatusCode: HttpStatusCode.Forbidden })
        {
            return $"Access denied to work item #{itemId}.";
        }

        if (exception is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests })
        {
            return "Rate limit hit - retry later.";
        }

        if (exception is TaskCanceledException)
        {
            return "Request timed out after 35s.";
        }

        return $"Unexpected error: {exception.GetType().Name}: {exception.Message}";
    }
}
