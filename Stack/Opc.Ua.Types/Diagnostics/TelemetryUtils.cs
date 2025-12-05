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

#nullable enable

using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Opc.Ua
{
    /// <summary>
    /// Telemetry utilities
    /// </summary>
    public static class LoggerUtils
    {
        /// <summary>
        /// Typed null logger
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public sealed class Null<T> : Null, ILogger<T>
        {
            /// <summary>
            /// Get instance to a typed null logger
            /// </summary>
            public static new ILogger<T> Logger { get; } = new Null<T>();
        }

        /// <summary>
        /// Null logger factory.
        /// </summary>
        internal sealed class NullLoggerFactory : ILoggerFactory
        {
            /// <inheritdoc/>
            public void AddProvider(ILoggerProvider provider)
            {
            }

            /// <inheritdoc/>
            public ILogger CreateLogger(string categoryName)
            {
                return Null.Logger;
            }

            /// <inheritdoc/>
            public void Dispose()
            {
            }
        }

        /// <summary>
        /// Null logger telemetry context
        /// </summary>
        internal class NullTelemetryContext : TelemetryContextBase
        {
            public NullTelemetryContext()
                : base(new NullLoggerFactory())
            {
            }
        }

        /// <summary>
        /// Null logger. In debug builds it will assert if used. In retail
        /// it will ensure that no null reference exception occurrs in classes
        /// that initialize a logger field.
        /// </summary>
        public class Null : ILogger
        {
            /// <summary>
            /// Get an instance to the null logger
            /// </summary>
            public static ILogger Logger { get; } = new Null();

            /// <inheritdoc/>
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            {
                DebugCheck();
                return new DummyScope();
            }

            /// <inheritdoc/>
            public bool IsEnabled(LogLevel logLevel)
            {
                DebugCheck();
                return false;
            }

            /// <inheritdoc/>
            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                DebugCheck();
            }

            /// <inheritdoc/>
            public override string ToString()
            {
                return "NullLogger";
            }

            private sealed class DummyScope : IDisposable
            {
                /// <inheritdoc/>
                public void Dispose()
                {
                }
            }

            [Conditional("DEBUG")]
            private static void DebugCheck()
            {
                Debug.Fail("Using a NullLogger");
            }
        }

        /// <summary>
        /// Append the exception and all nested exception with no indent
        /// </summary>
        public static StringBuilder AppendException(
            this StringBuilder buffer,
            Exception exception)
        {
            return AppendException(buffer, exception, string.Empty);
        }

        /// <summary>
        /// Append the exception and all nested exception with indent
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="exception"/> or <paramref name="indent"/> is <c>null</c>.
        /// </exception>
        public static StringBuilder AppendException(
            this StringBuilder buffer,
            Exception exception,
            string indent)
        {
            if (exception == null)
            {
                throw new ArgumentNullException(nameof(exception));
            }
            if (indent == null)
            {
                throw new ArgumentNullException(nameof(indent));
            }
            for (int i = 0; i < 100; i++)
            {
                if (i > 0)
                {
                    buffer
                        .AppendLine()
                        .Append(indent)
                        .Append(">>>> (Inner #")
                        .Append(i)
                        .AppendLine(") >>>>");
                }

                buffer
                    .Append(indent)
                    .Append('[')
                    .Append(exception.GetType().Name)
                    .Append(']')
                    .Append(' ')
                    .Append(exception.Message ?? "(No message)");

                if (!string.IsNullOrEmpty(exception.StackTrace))
                {
                    AddStackTrace(buffer, exception.StackTrace, indent);
                }

                if (exception.InnerException == null)
                {
                    break;
                }
                exception = exception.InnerException;
            }
            return buffer;

            static void AddStackTrace(StringBuilder buffer, string stackTrace, string indent)
            {
                string[] trace = stackTrace.Split(Environment.NewLine.ToCharArray());
                for (int ii = 0; ii < trace.Length; ii++)
                {
                    if (!string.IsNullOrEmpty(trace[ii]))
                    {
                        buffer
                            .AppendLine()
                            .Append(indent)
                            .AppendFormat(CultureInfo.InvariantCulture, "--- {0}", trace[ii]);
                    }
                }
            }
        }
    }
}
