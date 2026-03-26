using System.Net;
using System.Text.Json.Nodes;

namespace UwMcp;

internal sealed record ApiResponse(HttpStatusCode? StatusCode, bool Success, JsonNode? Data, string? Error)
{
    public static ApiResponse ErrorResponse(string error) => new(null, false, null, error);
}
