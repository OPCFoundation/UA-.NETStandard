/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Opc.Ua
{
    /// <summary>
    /// Defines various static utility functions.
    /// </summary>
    public static partial class Utils
    {
        /// <summary>
        /// Writes an informational message to the trace log.
        /// </summary>
        [Obsolete("Use Utils.LogInfo instead.")]
        public static void Trace(string message)
        {
            LogInformation(message);
        }

        /// <summary>
        /// Writes an informational message to the trace log.
        /// </summary>
        [Obsolete("Use Utils.LogInfo instead.")]
        public static void Trace(string format, params object[] args)
        {
            LogInformation(format, args);
        }

        /// <summary>
        /// Writes an informational message to the trace log.
        /// </summary>
        [Conditional("DEBUG")]
        [Obsolete("Use Utils.LogDebug instead.")]
        public static void TraceDebug(string format, params object[] args)
        {
            LogDebug(format, args);
        }

        /// <summary>
        /// Writes an exception/error message to the trace log.
        /// </summary>
        [Obsolete("Use Utils.LogError instead.")]
        public static void Trace(Exception e, string message)
        {
            LogError(e, message);
        }

        /// <summary>
        /// Writes an exception/error message to the trace log.
        /// </summary>
        [Obsolete("Use Utils.LogError instead.")]
        public static void Trace(Exception e, string format, params object[] args)
        {
            LogError(e, format, args);
        }

        /// <summary>
        /// Writes an exception/error message to the trace log.
        /// </summary>
        [Obsolete("Use Utils.LogError instead.")]
        public static void Trace(Exception e, string format, bool handled, params object[] args)
        {
            LogError(e, format, handled, args);
        }

        /// <summary>
        /// Writes a message to the trace log.
        /// </summary>
        [Obsolete("Use Utils.Log instead.")]
        public static void Trace(int traceMask, string format, params object[] args)
        {
            Log(traceMask, format, args);
        }

        /// <summary>
        /// Writes a message to the trace log.
        /// </summary>
        [Obsolete("Use Utils.Log instead.")]
        public static void Trace(int traceMask, string format, bool handled, params object[] args)
        {
            Log(traceMask, format, handled, args);
        }

        /// <summary>
        /// Writes a message to the trace log.
        /// </summary>
        /// <typeparam name="TState"></typeparam>
        [Obsolete("Use Utils.Log instead.")]
        public static void Trace<TState>(
            TState state,
            Exception exception,
            int traceMask,
            Func<TState, Exception, string> formatter)
        {
            Log(state, exception, traceMask, formatter);
        }

        /// <summary>
        /// Writes a message to the trace log.
        /// </summary>
        [Obsolete("Use Utils.Log instead.")]
        public static void Trace(
            Exception e,
            int traceMask,
            string format,
            bool handled,
            params object[] args)
        {
            Log(e, traceMask, format, handled, args);
        }

        /// <summary>
        /// Sets the LogLevel for the TraceEventLogger.
        /// </summary>
        /// <remarks>
        /// The setting is ignored if ILogger is replaced.
        /// </remarks>
        [Obsolete("Use ITelemetryContext ILoggerFactory and concrete ILoggers.")]
        public static void SetLogLevel(LogLevel logLevel)
        {
            // Do nothing
        }

        /// <summary>
        /// Sets the ILogger.
        /// </summary>
        [Obsolete("Use ITelemetryContext ILoggerFactory and concrete ILoggers.")]
        public static void SetLogger(ILogger logger)
        {
            // Do nothing
        }

        /// <summary>
        /// If the legacy trace event handler should be used.
        /// </summary>
        /// <remarks>By default true, however a call to SetLogger disables it.</remarks>
        [Obsolete("Use ITelemetryContext ILoggerFactory and concrete ILoggers.")]
        public static bool UseTraceEvent { get; set; } = true;
    }
}
