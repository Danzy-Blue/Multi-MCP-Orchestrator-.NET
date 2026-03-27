using Microsoft.Extensions.Logging;

namespace Shared.Logging;

public sealed class AppLoggingOptions
{
    public const string SectionName = "Observability";

    public FileLoggingOptions File { get; set; } = new();
    public AppInsightsLoggingOptions AppInsights { get; set; } = new();
}

public sealed class FileLoggingOptions
{
    public bool Enabled { get; set; }
    public string? Path { get; set; }
    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;
}

public sealed class AppInsightsLoggingOptions
{
    public bool Enabled { get; set; }
    public string? ConnectionString { get; set; }
}
