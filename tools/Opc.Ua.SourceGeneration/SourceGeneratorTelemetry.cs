/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Source generation telemetry context
    /// </summary>
    internal sealed class SourceGeneratorTelemetry : TelemetryContextBase, IDisposable
    {
        /// <summary>
        /// Private constructor
        /// </summary>
        private SourceGeneratorTelemetry(ILoggerFactory factory)
            : base(factory)
        {
            m_ownedFactory = factory;
        }

        /// <summary>
        /// Dispose the owned logger factory.
        /// </summary>
        public void Dispose()
        {
            m_ownedFactory?.Dispose();
        }

        /// <summary>
        /// Create telemetry context. Warnings and errors raised by the
        /// generator libraries surface as compiler diagnostics; anything
        /// below is dropped because a generator has no build output sink.
        /// </summary>
        public static SourceGeneratorTelemetry Create(SourceProductionContext context)
        {
            ILoggerFactory factory = null;
            try
            {
                factory = new LoggerFactoryAdapter(context);
                var result = new SourceGeneratorTelemetry(factory);
                factory = null;
                return result;
            }
            finally
            {
                factory?.Dispose();
            }
        }

        /// <summary>
        /// Logger provider and adapter for logger factory
        /// </summary>
        private sealed class LoggerFactoryAdapter : ILoggerFactory, ILoggerProvider
        {
            /// <summary>
            /// Create adapter
            /// </summary>
            /// <param name="context"></param>
            public LoggerFactoryAdapter(SourceProductionContext context)
            {
                m_context = context;
            }

            /// <inheritdoc/>
            public void AddProvider(ILoggerProvider provider)
            {
            }

            /// <inheritdoc/>
            public ILogger CreateLogger(string categoryName)
            {
                return new LoggerAdapter(categoryName, m_context);
            }

            /// <inheritdoc/>
            public void Dispose()
            {
            }

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
                SourceProductionContext context)
            {
                m_categoryName = categoryName;
                m_context = context;
            }

            /// <inheritdoc/>
            public void Dispose()
            {
            }

            /// <inheritdoc/>
            public bool IsEnabled(LogLevel logLevel)
            {
                return logLevel > LogLevel.Information;
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
                        SourceGenerator.CreateFormattedDiagnostic(descriptor, message));
                    return;
                }

                // Anything without a descriptor only reaches a debugger-attached
                // host, so keep the full exception rather than just its message.
                Debug.WriteLine(exception is null
                    ? $"[{m_categoryName}] {message}"
                    : $"[{m_categoryName}] {message}\n{exception}");
            }

            /// <inheritdoc/>
            public IDisposable BeginScope<TState>(TState state) where TState : notnull
            {
                return this;
            }

            private readonly string m_categoryName;
            private readonly SourceProductionContext m_context;
        }

        private readonly ILoggerFactory m_ownedFactory;
    }
}
