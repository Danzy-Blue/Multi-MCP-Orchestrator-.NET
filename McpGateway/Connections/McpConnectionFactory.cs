using Microsoft.Extensions.Logging;

namespace McpGateway;

public sealed class McpConnectionFactory(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory) : IMcpConnectionFactory
{
    public IMcpConnection Create(ServerConfig config) =>
        new McpConnection(config, httpClientFactory, loggerFactory.CreateLogger<McpConnection>());
}
