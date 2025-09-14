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
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
        /// <param name="telemetry"></param>
        /// <returns></returns>
        public static ILoggerFactory GetLoggerFactory(this ITelemetryContext? telemetry)
        {
            return telemetry?.LoggerFactory ?? s_loggerFactory.Value;
        }

        /// <summary>
        /// Create a logger from a logger factory
        /// </summary>
        /// <typeparam name="TContext"></typeparam>
        /// <param name="telemetry"></param>
        /// <returns></returns>
        public static ILogger<TContext> CreateLogger<TContext>(this ITelemetryContext? telemetry)
        {
            return telemetry.GetLoggerFactory().CreateLogger<TContext>();
        }

        /// <summary>
        /// Create a logger from a logger factory
        /// </summary>
        /// <typeparam name="TContext"></typeparam>
        /// <param name="telemetry"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public static ILogger<TContext> CreateLogger<TContext>(this ITelemetryContext? telemetry, TContext context)
        {
            return telemetry.GetLoggerFactory().CreateLogger<TContext>();
        }

        /// <summary>
        /// Create a logger from a logger factory
        /// </summary>
        /// <param name="telemetry"></param>
        /// <param name="categoryName"></param>
        /// <returns></returns>
        public static ILogger CreateLogger(this ITelemetryContext? telemetry, string categoryName)
        {
            return telemetry.GetLoggerFactory().CreateLogger(categoryName);
        }

        /// <summary>
        /// Get meter instance or a default one.
        /// </summary>
        /// <param name="telemetry"></param>
        /// <returns></returns>
        public static Meter GetMeter(this ITelemetryContext? telemetry)
        {
            return telemetry?.Meter ?? s_meter.Value;
        }

        /// <summary>
        /// Get activity source
        /// </summary>
        /// <param name="telemetry"></param>
        /// <returns></returns>
        public static ActivitySource GetActivitySource(this ITelemetryContext? telemetry)
        {
            return telemetry?.ActivitySource ?? s_activitySource.Value;
        }

        /// <summary>
        /// Start activity
        /// </summary>
        /// <param name="telemetry"></param>
        /// <param name="name"></param>
        /// <param name="kind"></param>
        /// <returns></returns>
        public static Activity? StartActivity(this ITelemetryContext? telemetry,
            [CallerMemberName] string name = "", ActivityKind kind = ActivityKind.Internal)
        {
            return telemetry.GetActivitySource().StartActivity(name, kind);
        }

        private static readonly Lazy<Meter> s_meter =
            new(() => new Meter("Opc.Ua", "1.0.0"));

        private static readonly Lazy<ActivitySource> s_activitySource =
            new(() => new ActivitySource("Opc.Ua", "1.0.0"));

        private static readonly Lazy<ILoggerFactory> s_loggerFactory =
            new(() => new NullLoggerFactory());
    }
}
