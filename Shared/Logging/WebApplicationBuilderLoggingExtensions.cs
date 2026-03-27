using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Shared.Logging;

public static class WebApplicationBuilderLoggingExtensions
{
    public static AppLoggingOptions AddConfiguredLogging(this WebApplicationBuilder builder, string defaultFileName)
    {
        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole();

        var options = builder.Configuration
            .GetSection(AppLoggingOptions.SectionName)
            .Get<AppLoggingOptions>() ?? new AppLoggingOptions();

        if (options.File.Enabled)
        {
            var resolvedPath = ResolveFilePath(builder.Environment.ContentRootPath, options.File.Path, defaultFileName);
            options.File.Path = resolvedPath;
            builder.Logging.AddProvider(new JsonLinesFileLoggerProvider(
                new FileLoggingRegistration(resolvedPath, options.File.MinimumLevel)));
        }

        return options;
    }

    private static string ResolveFilePath(string contentRootPath, string? configuredPath, string defaultFileName)
    {
        var path = string.IsNullOrWhiteSpace(configuredPath)
            ? defaultFileName
            : configuredPath.Trim();

        return Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(Path.Combine(contentRootPath, path));
    }
}
