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
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Opc.Ua.Tests
{
    public sealed class TelemetryParameterizable
    {
        public static TelemetryParameterizable<T> Create<T>(Func<ITelemetryContext, T> factory)
        {
            return new TelemetryParameterizable<T>(factory);
        }
    }

    public sealed class TelemetryParameterizable<T>
    {
        internal TelemetryParameterizable(Func<ITelemetryContext, T> factory)
        {
            m_factory = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        public T Create(ITelemetryContext telemetry)
        {
            return m_factory(telemetry);
        }

        private readonly Func<ITelemetryContext, T> m_factory;
    }

    public sealed class NUnitTelemetryContext : TelemetryContextBase
    {
        /// <summary>
        /// Create telemetry context
        /// </summary>
        private NUnitTelemetryContext(string context)
            : base(Microsoft.Extensions.Logging.LoggerFactory
                .Create(builder => builder.AddProvider(new NUnitLoggerProvider(context))))
        {
        }

        /// <summary>
        /// Use the test context output
        /// </summary>
        /// <returns></returns>
        public static ITelemetryContext Create(bool isServer = false)
        {
            return new NUnitTelemetryContext(!isServer ? "TEST" : "SERVER");
        }

        [ProviderAlias("BenchmarkDotNet")]
        internal sealed class BenchmarkDotNetProvider : ILoggerProvider
        {
            /// <inheritdoc/>
            public ILogger CreateLogger(string categoryName)
            {
                return m_loggers.GetOrAdd(categoryName, name => new Logger());
            }

            /// <inheritdoc/>
            public void Dispose()
            {
            }

            private sealed class Logger : ILogger, IDisposable
            {
                private readonly BenchmarkDotNet.Loggers.ILogger m_logger;

                public Logger(BenchmarkDotNet.Loggers.ILogger logger = null)
                {
                    m_logger = logger ?? BenchmarkDotNet.Loggers.ConsoleLogger.Default;
                }

                /// <inheritdoc/>
                public LogLevel MinimumLogLevel { get; set; } = LogLevel.Debug;

                /// <inheritdoc/>
                public IDisposable BeginScope<TState>(TState state)
                {
                    return this;
                }

                /// <inheritdoc/>
                public void Dispose()
                {
                }

                /// <inheritdoc/>
                public bool IsEnabled(LogLevel logLevel)
                {
                    return logLevel >= MinimumLogLevel;
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
                        StringBuilder sb = new StringBuilder()
                            .AppendFormat(
                                CultureInfo.InvariantCulture,
                                "{0:yy-MM-dd HH:mm:ss.fff}: ",
                                DateTime.UtcNow)
                            .Append(formatter(state, exception));
                        if (exception != null)
                        {
                            sb
                                .AppendLine()
                                .AppendException(exception, "\t");
                        }
                        m_logger.WriteLine(logLevel switch
                        {
                            LogLevel.Information => BenchmarkDotNet.Loggers.LogKind.Info,
                            LogLevel.Warning => BenchmarkDotNet.Loggers.LogKind.Warning,
                            LogLevel.Error or
                            LogLevel.Critical => BenchmarkDotNet.Loggers.LogKind.Error,
                            LogLevel.Trace or
                            LogLevel.Debug or
                            LogLevel.None => BenchmarkDotNet.Loggers.LogKind.Default,
                            _ => throw new ArgumentException("Unknown log level", nameof(logLevel))
                        },
                        sb.ToString());
                    }
                    catch
                    {
                        // intentionally ignored
                    }
                }
            }

            private readonly ConcurrentDictionary<string, Logger> m_loggers =
                  new(StringComparer.OrdinalIgnoreCase);
        }

        [ProviderAlias("NUnit")]
        internal sealed class NUnitLoggerProvider : ILoggerProvider
        {
            /// <summary>
            /// Create provider for context
            /// </summary>
            /// <param name="context"></param>
            public NUnitLoggerProvider(string context)
            {
                m_context = context;
            }

            /// <inheritdoc/>
            public ILogger CreateLogger(string categoryName)
            {
                return m_loggers.GetOrAdd(categoryName,
                    name => new Logger(m_context, categoryName));
            }

            /// <inheritdoc/>
            public void Dispose()
            {
            }

            private sealed class Logger : ILogger, IDisposable
            {
                public Logger(string context, string categoryName)
                {
                    m_context = context;
                    m_categoryName = categoryName;
                }

                /// <inheritdoc/>
                public LogLevel MinimumLogLevel { get; set; } = LogLevel.Debug;

                /// <inheritdoc/>
                public IDisposable BeginScope<TState>(TState state)
                {
                    return this;
                }

                /// <inheritdoc/>
                public void Dispose()
                {
                    // nothing to dispose
                }

                /// <inheritdoc/>
                public bool IsEnabled(LogLevel logLevel)
                {
                    if (logLevel < MinimumLogLevel)
                    {
                        return false;
                    }
                    switch (logLevel)
                    {
                        case LogLevel.Trace:
                        case LogLevel.Debug:
                        case LogLevel.Information:
                        case LogLevel.Warning:
                            return TestContext.Progress != null;
                        case LogLevel.Error:
                        case LogLevel.Critical:
                            return TestContext.Error != null;
                        default:
                            return false;
                    }
                }

                /// <inheritdoc/>
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
                        // Add the info to the test log if on current context
                        StringBuilder sb = new StringBuilder()
                            .AppendFormat(
                                CultureInfo.InvariantCulture,
                                "{0:HH:mm:ss.fff} ",
                                DateTime.UtcNow)
                            .Append('[')
                            .Append(m_categoryName)
                            .Append(']')
                            .Append(' ')
                            .Append(formatter(state, exception));
                        if (exception != null)
                        {
                            sb
                                .AppendLine()
                                .AppendException(exception, "\t");
                        }
                        string logRecord = sb.ToString();
                        TestContext.Out.WriteLine(logRecord);

                        // Also write to progress/error which captures all output not just test
                        logRecord = sb
                            .Clear()
                            .Append(TestContext.CurrentContext?.Test?.DisplayName ?? string.Empty)
                            .Append(' ')
                            .AppendLine(TestContext.CurrentContext?.Test?.Name ?? string.Empty)
                            .Append('\t')
                            .Append(m_context)
                            .Append(' ')
                            .Append(logRecord)
                            .ToString();
                        switch (logLevel)
                        {
                            case LogLevel.Trace:
                            case LogLevel.Debug:
                            case LogLevel.Information:
                            case LogLevel.Warning:
                                TestContext.Progress.WriteLine(logRecord);
                                break;
                            case LogLevel.Error:
                            case LogLevel.Critical:
                                TestContext.Error.WriteLine(logRecord);
                                break;
                            case LogLevel.None:
                                return;
                            default:
                                Debug.Fail($"Bad log level {logLevel}");
                                break;
                        }
                    }
                    catch
                    {
                        // intentionally ignored
                    }
                }

                private readonly string m_context;
                private readonly string m_categoryName;
            }

            private readonly ConcurrentDictionary<string, Logger> m_loggers =
                  new(StringComparer.OrdinalIgnoreCase);

            private readonly string m_context;
        }
    }
}
