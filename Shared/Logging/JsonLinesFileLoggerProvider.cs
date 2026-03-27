using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Shared.Logging;

internal sealed class JsonLinesFileLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly FileLoggingRegistration _registration;
    private readonly StreamWriter _writer;
    private readonly object _sync = new();
    private IExternalScopeProvider _scopeProvider = new LoggerExternalScopeProvider();
    private bool _disposed;

    public JsonLinesFileLoggerProvider(FileLoggingRegistration registration)
    {
        _registration = registration;

        var directory = Path.GetDirectoryName(_registration.Path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _writer = new StreamWriter(new FileStream(
            _registration.Path,
            FileMode.Append,
            FileAccess.Write,
            FileShare.ReadWrite))
        {
            AutoFlush = true,
        };
    }

    public ILogger CreateLogger(string categoryName) => new JsonLinesFileLogger(categoryName, this, _registration.MinimumLevel);

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _writer.Dispose();
        }
    }

    public void SetScopeProvider(IExternalScopeProvider scopeProvider)
    {
        _scopeProvider = scopeProvider;
    }

    internal IDisposable BeginScope<TState>(TState state) where TState : notnull => _scopeProvider.Push(state);

    internal void WriteLog(
        string categoryName,
        LogLevel logLevel,
        EventId eventId,
        string message,
        Exception? exception)
    {
        var scopes = new List<string>();
        _scopeProvider.ForEachScope((scope, state) =>
        {
            state.Add(scope?.ToString() ?? string.Empty);
        }, scopes);

        var payload = new JsonLinesFileLogEntry(
            DateTimeOffset.UtcNow,
            logLevel.ToString(),
            categoryName,
            eventId.Id,
            message,
            exception?.ToString(),
            scopes.Count == 0 ? null : scopes);

        var line = JsonSerializer.Serialize(payload);
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _writer.WriteLine(line);
        }
    }

    private sealed record JsonLinesFileLogEntry(
        DateTimeOffset TimestampUtc,
        string Level,
        string Category,
        int EventId,
        string Message,
        string? Exception,
        IReadOnlyList<string>? Scopes);
}

internal sealed class JsonLinesFileLogger(
    string categoryName,
    JsonLinesFileLoggerProvider provider,
    LogLevel minimumLevel) : ILogger
{
    public IDisposable BeginScope<TState>(TState state) where TState : notnull => provider.BeginScope(state);

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= minimumLevel;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var message = formatter(state, exception);
        if (string.IsNullOrWhiteSpace(message) && exception is null)
        {
            return;
        }

        provider.WriteLog(categoryName, logLevel, eventId, message, exception);
    }
}

internal sealed record FileLoggingRegistration(string Path, LogLevel MinimumLevel);
