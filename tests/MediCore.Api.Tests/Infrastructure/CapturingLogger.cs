using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace MediCore.Api.Tests.Infrastructure;

/// <summary>Guarda todo lo que la aplicación escribe en sus bitácoras, para comprobar que no incluye información clínica.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _messages = new();

    public IReadOnlyCollection<string> Messages => _messages;

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _messages);

    public void Dispose() { }

    private sealed class CapturingLogger(string category, ConcurrentQueue<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => messages.Enqueue($"[{category}] {formatter(state, exception)} {exception}");
    }
}
