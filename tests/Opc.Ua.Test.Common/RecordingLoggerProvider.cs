/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

#nullable enable

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
                        properties[value.Key] = value.Value;
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
