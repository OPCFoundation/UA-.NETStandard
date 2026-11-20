/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.Tests
{
    /// <summary>
    /// Thread-safe logger provider for asserting structured log contracts.
    /// </summary>
    public sealed class RecordingLoggerProvider : ILoggerProvider
    {
        /// <summary>
        /// Initializes a recording provider.
        /// </summary>
        /// <param name="minimumLevel">The minimum level to capture.</param>
        public RecordingLoggerProvider(LogLevel minimumLevel = LogLevel.Trace)
        {
            MinimumLevel = minimumLevel;
        }

        /// <summary>
        /// Gets or sets the minimum captured log level.
        /// </summary>
        public LogLevel MinimumLevel { get; set; }

        /// <summary>
        /// Gets a snapshot of the captured records.
        /// </summary>
        public IReadOnlyList<RecordedLogRecord> Records => m_records.ToArray();

        /// <inheritdoc/>
        public ILogger CreateLogger(string categoryName)
        {
            return new RecordingLogger(this, categoryName);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
        }

        private sealed class RecordingLogger : ILogger
        {
            public RecordingLogger(RecordingLoggerProvider provider, string categoryName)
            {
                m_provider = provider;
                m_categoryName = categoryName;
            }

            /// <inheritdoc/>
            public IDisposable BeginScope<TState>(TState state)
                where TState : notnull
            {
                return EmptyScope.Instance;
            }

            /// <inheritdoc/>
            public bool IsEnabled(LogLevel logLevel)
            {
                return logLevel >= m_provider.MinimumLevel &&
                    logLevel != LogLevel.None;
            }

            /// <inheritdoc/>
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

                var properties = new Dictionary<string, object?>(StringComparer.Ordinal);
                if (state is IEnumerable<KeyValuePair<string, object?>> values)
                {
                    foreach (KeyValuePair<string, object?> value in values)
                    {
                        properties[value.Key] = Snapshot(value.Value);
                    }
                }

                m_provider.m_records.Enqueue(
                    new RecordedLogRecord(
                        m_categoryName,
                        logLevel,
                        eventId,
                        formatter(state, exception),
                        properties,
                        exception));
            }

            /// <summary>
            /// The redaction-aware logger passes classified values as pooled redactor
            /// objects that are only valid during the Log call; keep their text.
            /// </summary>
            private static object? Snapshot(object? value)
            {
                if (value != null &&
                    value.GetType().Assembly.GetName().Name?.StartsWith(
                        "Microsoft.Extensions.", StringComparison.Ordinal) == true)
                {
                    return value.ToString();
                }
                return value;
            }

            private readonly RecordingLoggerProvider m_provider;
            private readonly string m_categoryName;
        }

        private sealed class EmptyScope : IDisposable
        {
            public static EmptyScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }

        private readonly ConcurrentQueue<RecordedLogRecord> m_records = new();
    }

    /// <summary>
    /// A structured log record captured by <see cref="RecordingLoggerProvider"/>.
    /// </summary>
    public sealed class RecordedLogRecord
    {
        /// <summary>
        /// Initializes a captured log record.
        /// </summary>
        public RecordedLogRecord(
            string categoryName,
            LogLevel logLevel,
            EventId eventId,
            string message,
            IReadOnlyDictionary<string, object?> properties,
            Exception? exception)
        {
            CategoryName = categoryName;
            LogLevel = logLevel;
            EventId = eventId;
            Message = message;
            Properties = properties;
            Exception = exception;
        }

        /// <summary>
        /// Gets the logger category.
        /// </summary>
        public string CategoryName { get; }

        /// <summary>
        /// Gets the log level.
        /// </summary>
        public LogLevel LogLevel { get; }

        /// <summary>
        /// Gets the event identity.
        /// </summary>
        public EventId EventId { get; }

        /// <summary>
        /// Gets the formatted message.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// Gets the structured log properties.
        /// </summary>
        public IReadOnlyDictionary<string, object?> Properties { get; }

        /// <summary>
        /// Gets the associated exception.
        /// </summary>
        public Exception? Exception { get; }
    }
}
