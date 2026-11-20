/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
using Microsoft.Extensions.Logging;

namespace Opc.Ua
{
    /// <summary>
    /// Provides context for tracing, metrics and logging.
    /// </summary>
    public interface ITelemetryContext
    {
        /// <summary>
        /// Create the meter instance to create and record metrics.
        /// The caller is responsible to dispose the meter instance
        /// returned.
        /// </summary>
        Meter CreateMeter();

        /// <summary>
        /// Create the meter instance for the specified assembly.
        /// The caller is responsible to dispose the meter instance
        /// returned.
        /// </summary>
        /// <param name="assembly">The component assembly producing the metrics.</param>
        Meter CreateMeter(Assembly assembly);

        /// <summary>
        /// Access the logger factory to create logger objects.
        /// </summary>
        ILoggerFactory LoggerFactory { get; }

        /// <summary>
        /// Get a activity source for the current assembly.
        /// Do not dispose the activity source returned as it is
        /// held as part of the telemetry context.
        /// </summary>
        ActivitySource ActivitySource { get; }

        /// <summary>
        /// Get an activity source for the specified assembly.
        /// Do not dispose the activity source returned as it is
        /// held as part of the telemetry context.
        /// </summary>
        /// <param name="assembly">The component assembly producing the activities.</param>
        ActivitySource GetActivitySource(Assembly assembly);
    }
}
