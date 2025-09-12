/* ========================================================================
 * Copyright (c) 2005-2023 The OPC Foundation, Inc. All rights reserved.
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

using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Opc.Ua.Tests
{
    public sealed class NUnitTelemetryContext : ITelemetryContext, ILoggerFactory
    {
        /// <inheritdoc/>
        public Meter Meter { get; }

        /// <inheritdoc/>
        public ILoggerFactory LoggerFactory { get; }

        /// <inheritdoc/>
        public ActivitySource ActivitySource { get; }

        /// <summary>
        /// Create telemetry context over a writer
        /// </summary>
        /// <param name="outputWriter"></param>
        private NUnitTelemetryContext(TextWriter outputWriter)
        {
            m_writer = outputWriter;
        }

        /// <inheritdoc/>
        public ILogger CreateLogger(string categoryName)
        {
            return new Logger(m_writer);
        }

        /// <summary>
        /// Create a telemetry context
        /// </summary>
        /// <param name="writer"></param>
        /// <returns></returns>
        public static ITelemetryContext Create(TextWriter writer)
        {
            return new NUnitTelemetryContext(writer);
        }

        /// <summary>
        /// Use the test context output
        /// </summary>
        /// <returns></returns>
        public static ITelemetryContext Create()
        {
            return Create(TestContext.Out);
        }

        /// <summary>
        /// Create a logger over the writer
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static ILogger<T> Create<T>()
        {
            return Create<T>(TestContext.Out);
        }

        /// <summary>
        /// Create a logger over the writer
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="writer"></param>
        /// <returns></returns>
        public static ILogger<T> Create<T>(TextWriter writer)
        {
            var traceLogger = new NUnitTelemetryContext(writer);

            // disable the built in tracing, use nunit trace output
            Utils.SetTraceMask(Utils.TraceMask & Utils.TraceMasks.StackTrace);
            Utils.SetTraceOutput(Utils.TraceOutput.Off);

            return traceLogger.CreateLogger<T>();
        }

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }

        private sealed class Logger : ILogger
        {
            public Logger(TextWriter outputWriter)
            {
                m_outputWriter = outputWriter;
            }

            public LogLevel MinimumLogLevel { get; set; } = LogLevel.Debug;

            public IDisposable BeginScope<TState>(TState state)
            {
                return null;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return logLevel >= MinimumLogLevel;
            }

            public void SetWriter(TextWriter outputWriter)
            {
                Interlocked.Exchange(ref m_outputWriter, outputWriter);
            }

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception exception,
                Func<TState, Exception, string> formatter)
            {
                if (logLevel < MinimumLogLevel)
                {
                    return;
                }

                try
                {
                    var sb = new StringBuilder();
                    sb.AppendFormat(
                        CultureInfo.InvariantCulture,
                        "{0:yy-MM-dd HH:mm:ss.fff}: ",
                        DateTime.UtcNow)
                        .Append(formatter(state, exception));

                    string logEntry = sb.ToString();

                    m_outputWriter.WriteLine(logEntry);
                }
                catch
                {
                    // intentionally ignored
                }
            }

            private TextWriter m_outputWriter;
        }

        private readonly TextWriter m_writer;
    }
}
