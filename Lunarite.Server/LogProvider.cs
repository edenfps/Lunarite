using System;

using Microsoft.Extensions.Logging;

namespace Lunarite;

// Sends Microsoft.Extensions.Logging output to the server console and log file.
public sealed class LogProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new Logger();

    public void Dispose()
    {
    }

    private sealed class Logger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
                Lunarite.Log.Write(logLevel, formatter(state, exception));
        }
    }
}
