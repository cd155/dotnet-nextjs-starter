using Microsoft.Extensions.Logging;

namespace PlatformServer.Tests.Utilities;

/// <summary>One logged entry: its message, and its structured state as a sink would see it.</summary>
public sealed record LogEntry(LogLevel Level, string Message, string State, string? Exception)
{
    public bool Mentions(string text) =>
        Message.Contains(text, StringComparison.OrdinalIgnoreCase)
        || State.Contains(text, StringComparison.OrdinalIgnoreCase)
        || Exception?.Contains(text, StringComparison.OrdinalIgnoreCase) == true;
}

/// <summary>
/// Every logger it makes writes to one list. Pass it where a class takes an <see cref="ILoggerFactory"/>, or add it
/// to a host as a provider (<c>logging.AddProvider</c>), where the host's level settings still apply.
/// </summary>
public sealed class LogCapture : ILoggerFactory, ILoggerProvider, ILogger
{
    private readonly List<LogEntry> _entries = [];

    public IReadOnlyList<LogEntry> Entries
    {
        get
        {
            lock (_entries)
            {
                return [.. _entries];
            }
        }
    }

    public ILogger CreateLogger(string categoryName) => this;

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var properties = state is IEnumerable<KeyValuePair<string, object?>> pairs
            ? string.Join(";", pairs.Select(pair => $"{pair.Key}={pair.Value}"))
            : state?.ToString() ?? string.Empty;
        lock (_entries)
        {
            _entries.Add(new LogEntry(logLevel, formatter(state, exception), properties, exception?.ToString()));
        }
    }
}
