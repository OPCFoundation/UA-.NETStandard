/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using IExternalLogger = SGF.Diagnostics.ILogger;
using ExternalLogLevel = SGF.Diagnostics.LogLevel;
using SourceProductionContext = SGF.SgfSourceProductionContext;
using System.Diagnostics;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Source generation telemetry context
    /// </summary>
    internal sealed class SourceGeneratorTelemetry : TelemetryContextBase
    {
        /// <summary>
        /// Private constructor
        /// </summary>
        private SourceGeneratorTelemetry(ILoggerFactory factory)
            : base(factory)
        {
        }

        /// <summary>
        /// Create telemetry context
        /// </summary>
        /// <returns></returns>
        public static SourceGeneratorTelemetry Create(
            IExternalLogger logger,
            SourceProductionContext context)
        {
            var factory = new LoggerFactoryAdapter(logger, context);
            return new SourceGeneratorTelemetry(factory);
        }

        /// <summary>
        /// Logger provider and adapter for logger factory
        /// </summary>
        private sealed class LoggerFactoryAdapter : ILoggerFactory, ILoggerProvider
        {
            /// <summary>
            /// Create adapter
            /// </summary>
            /// <param name="logger"></param>
            /// <param name="context"></param>
            public LoggerFactoryAdapter(
                IExternalLogger logger,
                SourceProductionContext context)
            {
                m_logger = logger;
                m_context = context;
            }

            /// <inheritdoc/>
            public void AddProvider(ILoggerProvider provider)
            {
            }

            /// <inheritdoc/>
            public ILogger CreateLogger(string categoryName)
            {
                return new LoggerAdapter(categoryName, m_context, m_logger);
            }

            /// <inheritdoc/>
            public void Dispose()
            {
            }

            private readonly IExternalLogger m_logger;
            private readonly SourceProductionContext m_context;
        }

        /// <summary>
        /// Logger adapter
        /// </summary>
        private sealed class LoggerAdapter : ILogger, IDisposable
        {
            /// <summary>
            /// Create logger adapter
            /// </summary>
            public LoggerAdapter(
                string categoryName,
                SourceProductionContext context,
                IExternalLogger logger)
            {
                m_categoryName = categoryName;
                m_context = context;
                m_logger = logger;
            }

            /// <inheritdoc/>
            public void Dispose()
            {
            }

            /// <inheritdoc/>
            public bool IsEnabled(LogLevel logLevel)
            {
                return logLevel > LogLevel.Information;
#if UNUSED
                return m_logger.IsEnabled(GetLogLevel(logLevel));
#endif
            }

            /// <inheritdoc/>
            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception exception,
                Func<TState, Exception, string> formatter)
            {
                if (!IsEnabled(logLevel))
                {
                    return;
                }

                string message = formatter(state, exception);
                if (SourceGenerator.TryGetDiagnostic(
                    logLevel,
                    eventId,
                    out DiagnosticDescriptor descriptor))
                {
                    m_context.ReportDiagnostic(
                        Diagnostic.Create(descriptor, Location.None, message));
                    return;
                }

                message = $"[{m_categoryName}] {message}";
                m_logger.Log(
                    GetLogLevel(logLevel),
                    exception,
                    message);

                Debug.WriteLine(message);
            }

            /// <inheritdoc/>
            public IDisposable BeginScope<TState>(TState state) where TState : notnull
            {
                return this;
            }

            private static ExternalLogLevel GetLogLevel(LogLevel logLevel)
            {
                return logLevel switch
                {
                    LogLevel.Trace or LogLevel.Debug => ExternalLogLevel.Debug,
                    LogLevel.Information => ExternalLogLevel.Information,
                    LogLevel.Warning => ExternalLogLevel.Warning,
                    LogLevel.Error or LogLevel.Critical => ExternalLogLevel.Error,
                    _ => ExternalLogLevel.Information
                };
            }

            private readonly string m_categoryName;
            private readonly IExternalLogger m_logger;
            private readonly SourceProductionContext m_context;
        }
    }
}
