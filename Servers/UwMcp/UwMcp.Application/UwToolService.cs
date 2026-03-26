using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace UwMcp;

public sealed class UwToolService(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    IHttpContextAccessor httpContextAccessor,
    ILogger<UwToolService> logger)
{
    private const string CorrelationHeaderName = "X-Correlation-ID";
    private readonly string _submissionTemplate = """
    {
      "Organisation":"ADG",
      "Description": "DEENA",
      "InceptionDate": "Tue Mar 17 2026 00:00:00.0000000+00:00",
      "NewRenewal": "New"
    }
    """;

    public async Task<string> GetSubmissionAsync(string contract_refrence)
    {
        var response = await CallApimAsync(HttpMethod.Get, $"{SubmissionUrl}/{contract_refrence}", null, RequestAborted);
        return FormatApiResponse(response);
    }

    public async Task<string> GetQuoteAsync(string contract_refrence)
    {
        var response = await CallApimAsync(HttpMethod.Get, $"{QuoteUrl}/{contract_refrence}", null, RequestAborted);
        return FormatApiResponse(response);
    }

    public async Task<string> CreateSubmissionAsync(string organisation, string inceptionDate, string newRenewal)
    {
        var payload = JsonNode.Parse(_submissionTemplate)!.AsObject();
        payload["Organisation"] = organisation;
        payload["Description"] = "MCP Submission";
        payload["InceptionDate"] = inceptionDate;
        payload["NewRenewal"] = newRenewal;

        var response = await CallApimAsync(HttpMethod.Post, SubmissionUrl, payload, RequestAborted);
        return FormatApiResponse(response);
    }

    public async Task<string> CreateSubmissionFromJsonAsync(JsonObject submission_json)
    {
        var response = await CallApimAsync(HttpMethod.Post, SubmissionUrl, submission_json, RequestAborted);
        return FormatApiResponse(response);
    }

    public async Task<string> EditSubmissionFromJsonAsync(string submission_refrence, JsonObject submission_json)
    {
        var response = await CallApimAsync(
            HttpMethod.Put,
            $"{SubmissionUrl}/{submission_refrence}",
            submission_json,
            RequestAborted);
        return FormatApiResponse(response);
    }

    private HttpContext HttpContext =>
        httpContextAccessor.HttpContext ?? throw new InvalidOperationException("Missing HTTP context.");

    private CancellationToken RequestAborted => HttpContext.RequestAborted;

    private string SubmissionUrl => configuration["SUBMISSION_URL"] ?? string.Empty;
    private string QuoteUrl => configuration["QUOTE_URL"] ?? string.Empty;
    private string ApimBaseUrl => configuration["APIM_BASE_URL"] ?? string.Empty;
    private string ApimSubscriptionKey => configuration["APIM_SUBSCRIPTION_KEY"] ?? string.Empty;
    private string ApimSubscriptionValue => configuration["APIM_SUBSCRIPTION_KEY_VALUE"] ?? string.Empty;

    private async Task<ApiResponse> CallApimAsync(
        HttpMethod method,
        string endpoint,
        JsonObject? payload,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ApimBaseUrl))
        {
            return ApiResponse.ErrorResponse("APIM_BASE_URL is not configured.");
        }

        var client = httpClientFactory.CreateClient("uw-apim");
        var relative = endpoint.Replace(ApimBaseUrl.TrimEnd('/') + "/", string.Empty, StringComparison.OrdinalIgnoreCase);
        var requestUri = $"{ApimBaseUrl.TrimEnd('/')}/{relative.TrimStart('/')}";

        using var request = new HttpRequestMessage(method, requestUri);
        request.Headers.TryAddWithoutValidation(ApimSubscriptionKey, ApimSubscriptionValue);
        request.Headers.TryAddWithoutValidation("User-Agent", "McpHost/1.0");
        var correlationId = httpContextAccessor.HttpContext?.Request.Headers[CorrelationHeaderName].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            request.Headers.TryAddWithoutValidation(CorrelationHeaderName, correlationId);
        }
        if (payload is not null)
        {
            request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        }

        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            JsonNode? data;
            try
            {
                data = JsonNode.Parse(content);
            }
            catch
            {
                data = JsonValue.Create(content);
            }

            return new ApiResponse(
                response.StatusCode,
                response.IsSuccessStatusCode,
                data,
                response.IsSuccessStatusCode ? null : content);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "UW APIM call failed for {Endpoint}", endpoint);
            return ApiResponse.ErrorResponse(ex.Message);
        }
    }

    private static string FormatApiResponse(ApiResponse response)
    {
        if (!response.Success)
        {
            return JsonSerializer.Serialize(new
            {
                error = "API call failed",
                details = new
                {
                    status = response.StatusCode is null ? (int?)null : (int)response.StatusCode,
                    success = false,
                    error = response.Error,
                    data = response.Data,
                },
            }, new JsonSerializerOptions { WriteIndented = true });
        }

        return response.Data?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "{}";
    }
}
