using System;
using Microsoft.Extensions.Logging;

namespace Paycheck4.Core.Usb
{
    /// <summary>
    /// Extensions for ILogger to help with type conversion
    /// </summary>
    internal static class LoggerExtensions
    {
        /// <summary>
        /// Creates a new logger instance that wraps the source logger with a different type parameter
        /// </summary>
        public static ILogger<T> AsLogger<T>(this ILogger logger)
        {
            return new TypedLoggerWrapper<T>(logger);
        }

        private class TypedLoggerWrapper<T> : ILogger<T>
        {
            private readonly ILogger _logger;

            public TypedLoggerWrapper(ILogger logger)
            {
                _logger = logger;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull
                => _logger.BeginScope(state);

            public bool IsEnabled(LogLevel logLevel)
                => _logger.IsEnabled(logLevel);

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => _logger.Log(logLevel, eventId, state, exception, formatter);
        }
    }
}