/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

#nullable enable

namespace Opc.Ua
{
    /// <summary>
    /// Extensions for the <see cref="ITelemetryContext"/>.
    /// </summary>
    public static class TelemetryExtensions
    {
        /// <summary>
        /// Get a logger factory from a context with or without logger factory
        /// Returns the default logger factory if none is provided.
        /// </summary>
        /// <param name="telemetry">The telemetry context to use</param>
        /// <returns></returns>
        public static ILoggerFactory GetLoggerFactory(this ITelemetryContext? telemetry)
        {
            DebugCheck(telemetry);
            return telemetry?.LoggerFactory ?? s_default.Value.LoggerFactory;
        }

        /// <summary>
        /// Create a logger from a logger factory
        /// </summary>
        /// <typeparam name="TContext"></typeparam>
        /// <param name="telemetry">The telemetry context to use</param>
        /// <returns></returns>
        public static ILogger<TContext> CreateLogger<TContext>(this ITelemetryContext? telemetry)
        {
            return telemetry.GetLoggerFactory().CreateLogger<TContext>();
        }

        /// <summary>
        /// Create a logger from a logger factory
        /// </summary>
        /// <typeparam name="TContext"></typeparam>
        /// <param name="telemetry">The telemetry context to use</param>
        /// <param name="context">A context to infer the category name from</param>
        /// <returns></returns>
        public static ILogger CreateLogger<TContext>(this ITelemetryContext? telemetry, TContext context)
        {
            return telemetry.CreateLogger(context?.GetType().FullName ?? typeof(TContext).FullName!);
        }

        /// <summary>
        /// Create a logger from a logger factory
        /// </summary>
        /// <param name="telemetry">The telemetry context to use</param>
        /// <param name="categoryName">The category to create the logger for</param>
        /// <returns></returns>
        public static ILogger CreateLogger(this ITelemetryContext? telemetry, string categoryName)
        {
            return telemetry.GetLoggerFactory().CreateLogger(categoryName);
        }

        /// <summary>
        /// Get meter instance or a default one.
        /// </summary>
        /// <param name="telemetry">The telemetry context to use</param>
        /// <returns></returns>
        public static Meter CreateMeter(this ITelemetryContext? telemetry)
        {
            DebugCheck(telemetry);
            return telemetry?.CreateMeter() ?? s_default.Value.CreateMeter();
        }

        /// <summary>
        /// Get activity source
        /// </summary>
        /// <param name="telemetry">The telemetry context to use</param>
        /// <returns></returns>
        public static ActivitySource GetActivitySource(this ITelemetryContext? telemetry)
        {
            DebugCheck(telemetry);
            return telemetry?.ActivitySource ?? s_default.Value.ActivitySource;
        }

        /// <summary>
        /// Start activity
        /// </summary>
        /// <param name="telemetry">The telemetry context to use</param>
        /// <param name="name">The name of the caller</param>
        /// <param name="kind">The activity kind</param>
        /// <returns></returns>
        public static Activity? StartActivity(this ITelemetryContext? telemetry,
            [CallerMemberName] string name = "", ActivityKind kind = ActivityKind.Internal)
        {
            return telemetry.GetActivitySource().StartActivity(name, kind);
        }

        private static readonly Lazy<DefaultTelemetry> s_default =
            new(() => new DefaultTelemetry(), true);

        /// <summary>
        /// Perform a debug check to help analyze null telemetry anywhere and helping
        /// us to weed out areas that need telemetry plumbed through.
        /// </summary>
        /// <param name="telemetry">The telemetry context to use</param>
        [Conditional("DEBUG")]
        private static void DebugCheck(ITelemetryContext? telemetry)
        {
            DebugLog.Instance.CollectIf(telemetry == null);
        }

        /// <summary>
        /// Helper to dump stack traces. This is a useful in debug builds
        /// to help identify places where telemetry is not emitted because
        /// the Telemetry context is not passed to a class or method.
        /// </summary>
        private sealed class DebugLog
        {
            public static readonly DebugLog Instance = new(
#if STACKLOG_TO_FILE
                false
#else
                true
#endif
            );

            /// <summary>
            /// Create stack log
            /// </summary>
            /// <param name="justLog"></param>
            /// <param name="fileName"></param>
            public DebugLog(bool justLog, string? fileName = null)
            {
                if (justLog)
                {
                    return;
                }
                // Find the folder with the solution files
                string path = Environment.CurrentDirectory;
                while (System.IO.Directory.GetFiles(path, "*.slnx").Length == 0)
                {
                    System.IO.DirectoryInfo? parent = System.IO.Directory.GetParent(path);
                    if (parent == null)
                    {
                        break;
                    }
                    path = parent.FullName;
                }
                m_fileName = System.IO.Path.Combine(
                    path ?? string.Empty,
                    fileName ?? "stacktraces.log");
            }

            /// <summary>
            /// Collect the stack trace if it has not already been reported.
            /// </summary>
            public void CollectIf(bool condition)
            {
                if (!condition)
                {
                    return;
                }

                string traceString = new StackTrace(true).ToString();

                if (m_reported.TryAdd(traceString, true))
                {
                    // Filter out tests where this is allowed
                    if (traceString.Contains(
                        "<ReEncodeComplexTypeScopedContext>",
                        StringComparison.Ordinal))
                    {
                        return;
                    }

                    if (m_fileName == null)
                    {
                        Debug.WriteLine(traceString);
                        return;
                    }

                    lock (m_reported)
                    {
                        System.IO.File.AppendAllText(m_fileName, traceString + "\n\n");
                    }
                }
            }

            private readonly ConcurrentDictionary<string, bool> m_reported = new();
            private readonly string? m_fileName;
        }
    }
}
