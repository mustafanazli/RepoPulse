using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace RepoPulse.AuthApi.Tests;

// Captures every formatted log message written by the host during a test,
// so a test can assert that no sensitive OAuth value ever reached a logger.
internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    public ConcurrentBag<string> Messages { get; } = new();

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(Messages);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger : ILogger
    {
        private readonly ConcurrentBag<string> messages;

        public CapturingLogger(ConcurrentBag<string> messages)
        {
            this.messages = messages;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            messages.Add(formatter(state, exception));
            if (exception is not null)
            {
                messages.Add(exception.ToString());
            }
        }
    }
}
