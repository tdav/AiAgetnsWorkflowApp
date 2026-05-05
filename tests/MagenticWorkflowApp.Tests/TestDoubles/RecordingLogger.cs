using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace MagenticWorkflowApp.Tests.TestDoubles;

public sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly List<RecordedLogEntry> entries = new();
    private readonly object syncRoot = new();

    public IReadOnlyList<RecordedLogEntry> Entries
    {
        get { lock (syncRoot) { return entries.ToList(); } }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var formatted = formatter(state, exception);
        var message = state?.ToString() ?? string.Empty;
        lock (syncRoot)
        {
            entries.Add(new RecordedLogEntry(logLevel, message, formatted, exception));
        }
    }
}

public sealed record RecordedLogEntry(
    LogLevel Level,
    string Message,
    string FormattedMessage,
    Exception? Exception);
