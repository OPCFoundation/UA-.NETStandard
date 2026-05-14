/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00
 * ======================================================================*/

using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Opc.Ua;

namespace UaLens.Telemetry;

/// <summary>
/// Telemetry context that routes SDK <see cref="ILogger"/> output to a
/// <see cref="LogRingBuffer"/> displayed in the UI's log pane. The buffer is
/// thread-safe; consumers (the log view) sample it on the UI thread.
/// </summary>
internal sealed class AppTelemetryContext : TelemetryContextBase
{
    public LogRingBuffer Buffer { get; }

    public AppTelemetryContext(LogRingBuffer buffer)
#pragma warning disable CA2000 // RingBufferLoggerFactory ownership transferred to TelemetryContextBase.
        : base(new RingBufferLoggerFactory(buffer))
#pragma warning restore CA2000
    {
        Buffer = buffer;
    }

    private sealed class RingBufferLoggerFactory : ILoggerFactory
    {
        private readonly LogRingBuffer m_buffer;
        private readonly ConcurrentDictionary<string, ILogger> m_loggers = new();

        public RingBufferLoggerFactory(LogRingBuffer buffer) => m_buffer = buffer;

        public void AddProvider(ILoggerProvider provider) { /* not supported */ }

        public ILogger CreateLogger(string categoryName)
            => m_loggers.GetOrAdd(categoryName,
                name => new RingBufferLogger(m_buffer, name));

        public void Dispose() { }
    }

    private sealed class RingBufferLogger : ILogger
    {
        private readonly LogRingBuffer m_buffer;
        private readonly string m_category;

        public RingBufferLogger(LogRingBuffer buffer, string category)
        {
            m_buffer = buffer;
            m_category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

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
            string message = formatter(state, exception);
            if (exception != null)
            {
                message = $"{message}  {exception.GetType().Name}: {exception.Message}";
            }
            m_buffer.Add(new LogEntry(DateTime.UtcNow, logLevel, m_category, message));
        }
    }
}
