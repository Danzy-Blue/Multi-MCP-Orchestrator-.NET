using System.Net.Http;

namespace McpHost;

public sealed class CorrelationIdHandler(ICorrelationContextAccessor correlationContextAccessor)
    : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var correlationId = correlationContextAccessor.CorrelationId;
        if (!string.IsNullOrWhiteSpace(correlationId)
            && !request.Headers.Contains(CorrelationConstants.HeaderName))
        {
            request.Headers.TryAddWithoutValidation(CorrelationConstants.HeaderName, correlationId);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
