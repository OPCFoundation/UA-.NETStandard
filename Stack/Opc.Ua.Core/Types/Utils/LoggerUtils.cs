// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Opc.Ua
{
    /// <summary>
    /// Logger Utils methods.
    /// </summary>
    public static partial class Utils
    {
        /// <summary>
        /// The high performance EventSource log interface.
        /// </summary>
        public static OpcUaEventSource EventLog => OpcUaEventSource.Log;

        /// <summary>
        /// ILogger abstraction used by all Utils.LogXXX methods.
        /// </summary>
        public static ILogger Logger { get; private set; } = new TraceEventLogger();

        /// <summary>
        /// Sets the ILogger.
        /// </summary>
        public static void SetLogger(ILogger logger)
        {
            Logger = logger;
        }

        //------------------------------------------DEBUG------------------------------------------//

        /// <summary>
        /// Formats and writes a debug log message.
        /// </summary>
        /// <param name="eventId">The event id associated with the log.</param>
        /// <param name="exception">The exception to log.</param>
        /// <param name="message">Format string of the log message in message template format. Example: <code>"User {User} logged in from {Address}"</code></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogDebug(0, exception, "Error while processing request from {Address}", address)</example>
        [Conditional("DEBUG")]
        public static void LogDebug(EventId eventId, Exception exception, string message, params object[] args)
        {
            Log(LogLevel.Debug, eventId, exception, message, args);
        }

        /// <summary>
        /// Formats and writes a debug log message.
        /// </summary>
        /// <param name="eventId">The event id associated with the log.</param>
        /// <param name="message">Format string of the log message in message template format. Example: <code>"User {User} logged in from {Address}"</code></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogDebug(0, "Processing request from {Address}", address)</example>
        [Conditional("DEBUG")]
        public static void LogDebug(EventId eventId, string message, params object[] args)
        {
            Log(LogLevel.Debug, eventId, message, args);
        }

        /// <summary>
        /// Formats and writes a debug log message.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="message">Format string of the log message in message template format. Example: <code>"User {User} logged in from {Address}"</code></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogDebug(exception, "Error while processing request from {Address}", address)</example>
        [Conditional("DEBUG")]
        public static void LogDebug(Exception exception, string message, params object[] args)
        {
            Log(LogLevel.Debug, exception, message, args);
        }

        /// <summary>
        /// Formats and writes a debug log message.
        /// </summary>
        /// <param name="message">Format string of the log message in message template format. Example: <code>"User {User} logged in from {Address}"</code></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogDebug("Processing request from {Address}", address)</example>
        [Conditional("DEBUG")]
        public static void LogDebug(string message, params object[] args)
        {
            Log(LogLevel.Debug, message, args);
        }

        //------------------------------------------TRACE------------------------------------------//

        /// <summary>
        /// Formats and writes a trace log message.
        /// </summary>
        /// <param name="eventId">The event id associated with the log.</param>
        /// <param name="exception">The exception to log.</param>
        /// <param name="message">Format string of the log message in message template format. Example: <code>"User {User} logged in from {Address}"</code></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogTrace(0, exception, "Error while processing request from {Address}", address)</example>
        public static void LogTrace(EventId eventId, Exception exception, string message, params object[] args)
        {
            Log(LogLevel.Trace, eventId, exception, message, args);
        }

        /// <summary>
        /// Formats and writes a trace log message.
        /// </summary>
        /// <param name="eventId">The event id associated with the log.</param>
        /// <param name="message">Format string of the log message in message template format. Example: <code>"User {User} logged in from {Address}"</code></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogTrace(0, "Processing request from {Address}", address)</example>
        public static void LogTrace(EventId eventId, string message, params object[] args)
        {
            Log(LogLevel.Trace, eventId, message, args);
        }

        /// <summary>
        /// Formats and writes a trace log message.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="message">Format string of the log message in message template format. Example: <code>"User {User} logged in from {Address}"</code></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogTrace(exception, "Error while processing request from {Address}", address)</example>
        public static void LogTrace(Exception exception, string message, params object[] args)
        {
            Log(LogLevel.Trace, exception, message, args);
        }

        /// <summary>
        /// Formats and writes a trace log message.
        /// </summary>
        /// <param name="message">Format string of the log message in message template format. Example: <code>"User {User} logged in from {Address}"</code></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogTrace("Processing request from {Address}", address)</example>
        public static void LogTrace(string message, params object[] args)
        {
            Log(LogLevel.Trace, message, args);
        }

        //------------------------------------------INFORMATION------------------------------------------//

        /// <summary>
        /// Formats and writes an informational log message.
        /// </summary>
        /// <param name="eventId">The event id associated with the log.</param>
        /// <param name="exception">The exception to log.</param>
        /// <param name="message">Format string of the log message in message template format. Example: <code>"User {User} logged in from {Address}"</code></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogInformation(0, exception, "Error while processing request from {Address}", address)</example>
        public static void LogInformation(EventId eventId, Exception exception, string message, params object[] args)
        {
            Log(LogLevel.Information, eventId, exception, message, args);
        }

        /// <summary>
        /// Formats and writes an informational log message.
        /// </summary>
        /// <param name="eventId">The event id associated with the log.</param>
        /// <param name="message">Format string of the log message in message template format. Example: <code>"User {User} logged in from {Address}"</code></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogInformation(0, "Processing request from {Address}", address)</example>
        public static void LogInformation(EventId eventId, string message, params object[] args)
        {
            Log(LogLevel.Information, eventId, message, args);
        }

        /// <summary>
        /// Formats and writes an informational log message.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="message">Format string of the log message in message template format. Example: <code>"User {User} logged in from {Address}"</code></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogInformation(exception, "Error while processing request from {Address}", address)</example>
        public static void LogInformation(Exception exception, string message, params object[] args)
        {
            Log(LogLevel.Information, exception, message, args);
        }

        /// <summary>
        /// Formats and writes an informational log message.
        /// </summary>
        /// <param name="message">Format string of the log message in message template format. Example: <code>"User {User} logged in from {Address}"</code></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogInformation("Processing request from {Address}", address)</example>
        public static void LogInformation(string message, params object[] args)
        {
            Log(LogLevel.Information, message, args);
        }

        //------------------------------------------WARNING------------------------------------------//

        /// <summary>
        /// Formats and writes a warning log message.
        /// </summary>
        /// <param name="eventId">The event id associated with the log.</param>
        /// <param name="exception">The exception to log.</param>
        /// <param name="message">Format string of the log message in message template format. Example: <code>"User {User} logged in from {Address}"</code></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogWarning(0, exception, "Error while processing request from {Address}", address)</example>
        public static void LogWarning(EventId eventId, Exception exception, string message, params object[] args)
        {
            Log(LogLevel.Warning, eventId, exception, message, args);
        }

        /// <summary>
        /// Formats and writes a warning log message.
        /// </summary>
        /// <param name="eventId">The event id associated with the log.</param>
        /// <param name="message">Format string of the log message in message template format. Example: <code>"User {User} logged in from {Address}"</code></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogWarning(0, "Processing request from {Address}", address)</example>
        public static void LogWarning(EventId eventId, string message, params object[] args)
        {
            Log(LogLevel.Warning, eventId, message, args);
        }

        /// <summary>
        /// Formats and writes a warning log message.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="message">Format string of the log message in message template format. Example: <code>"User {User} logged in from {Address}"</code></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogWarning(exception, "Error while processing request from {Address}", address)</example>
        public static void LogWarning(Exception exception, string message, params object[] args)
        {
            Log(LogLevel.Warning, exception, message, args);
        }

        /// <summary>
        /// Formats and writes a warning log message.
        /// </summary>
        /// <param name="message">Format string of the log message in message template format. Example: <code>"User {User} logged in from {Address}"</code></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogWarning("Processing request from {Address}", address)</example>
        public static void LogWarning(string message, params object[] args)
        {
            Log(LogLevel.Warning, message, args);
        }

        //------------------------------------------ERROR------------------------------------------//

        /// <summary>
        /// Formats and writes an error log message.
        /// </summary>
        /// <param name="eventId">The event id associated with the log.</param>
        /// <param name="exception">The exception to log.</param>
        /// <param name="message">Format string of the log message in message template format. Example: <code>"User {User} logged in from {Address}"</code></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogError(0, exception, "Error while processing request from {Address}", address)</example>
        public static void LogError(EventId eventId, Exception exception, string message, params object[] args)
        {
            Log(LogLevel.Error, eventId, exception, message, args);
        }

        /// <summary>
        /// Formats and writes an error log message.
        /// </summary>
        /// <param name="eventId">The event id associated with the log.</param>
        /// <param name="message">Format string of the log message in message template format. Example: <code>"User {User} logged in from {Address}"</code></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogError(0, "Processing request from {Address}", address)</example>
        public static void LogError(EventId eventId, string message, params object[] args)
        {
            Log(LogLevel.Error, eventId, message, args);
        }

        /// <summary>
        /// Formats and writes an error log message.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="message">Format string of the log message in message template format. Example: <code>"User {User} logged in from {Address}"</code></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogError(exception, "Error while processing request from {Address}", address)</example>
        public static void LogError(Exception exception, string message, params object[] args)
        {
            Log(LogLevel.Error, exception, message, args);
        }

        /// <summary>
        /// Formats and writes an error log message.
        /// </summary>
        /// <param name="message">Format string of the log message in message template format. Example: <code>"User {User} logged in from {Address}"</code></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogError("Processing request from {Address}", address)</example>
        public static void LogError(string message, params object[] args)
        {
            Log(LogLevel.Error, message, args);
        }

        //------------------------------------------CRITICAL------------------------------------------//

        /// <summary>
        /// Formats and writes a critical log message.
        /// </summary>
        /// <param name="eventId">The event id associated with the log.</param>
        /// <param name="exception">The exception to log.</param>
        /// <param name="message">Format string of the log message in message template format. Example: <code>"User {User} logged in from {Address}"</code></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogCritical(0, exception, "Error while processing request from {Address}", address)</example>
        public static void LogCritical(EventId eventId, Exception exception, string message, params object[] args)
        {
            Log(LogLevel.Critical, eventId, exception, message, args);
        }

        /// <summary>
        /// Formats and writes a critical log message.
        /// </summary>
        /// <param name="eventId">The event id associated with the log.</param>
        /// <param name="message">Format string of the log message in message template format. Example: <code>"User {User} logged in from {Address}"</code></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogCritical(0, "Processing request from {Address}", address)</example>
        public static void LogCritical(EventId eventId, string message, params object[] args)
        {
            Log(LogLevel.Critical, eventId, message, args);
        }

        /// <summary>
        /// Formats and writes a critical log message.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="message">Format string of the log message in message template format. Example: <code>"User {User} logged in from {Address}"</code></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogCritical(exception, "Error while processing request from {Address}", address)</example>
        public static void LogCritical(Exception exception, string message, params object[] args)
        {
            Log(LogLevel.Critical, exception, message, args);
        }

        /// <summary>
        /// Formats and writes a critical log message.
        /// </summary>
        /// <param name="message">Format string of the log message in message template format. Example: <code>"User {User} logged in from {Address}"</code></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogCritical("Processing request from {Address}", address)</example>
        public static void LogCritical(string message, params object[] args)
        {
            Log(LogLevel.Critical, message, args);
        }

        /// <summary>
        /// Formats and writes a log message at the specified log level.
        /// </summary>
        /// <param name="logLevel">Entry will be written on this level.</param>
        /// <param name="message">Format string of the log message.</param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        private static void Log(LogLevel logLevel, string message, params object[] args)
        {
            Log(logLevel, 0, null, message, args);
        }

        /// <summary>
        /// Formats and writes a log message at the specified log level.
        /// </summary>
        /// <param name="logLevel">Entry will be written on this level.</param>
        /// <param name="eventId">The event id associated with the log.</param>
        /// <param name="message">Format string of the log message.</param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        private static void Log(LogLevel logLevel, EventId eventId, string message, params object[] args)
        {
            if (Tracing.IsEnabled())
            {
                // call the legacy logging handler (TraceEvent)
                Utils.Trace(null, GetTraceMask(eventId, logLevel), message, false, args);
            }
            else
            {
                Logger.Log(logLevel, eventId, null, message, args);
            }
        }

        /// <summary>
        /// Formats and writes a log message at the specified log level.
        /// </summary>
        /// <param name="logLevel">Entry will be written on this level.</param>
        /// <param name="exception">The exception to log.</param>
        /// <param name="message">Format string of the log message.</param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        private static void Log(LogLevel logLevel, Exception exception, string message, params object[] args)
        {
            Log(logLevel, 0, exception, message, args);
        }

        /// <summary>
        /// Formats and writes a log message at the specified log level.
        /// </summary>
        /// <param name="logLevel">Entry will be written on this level.</param>
        /// <param name="eventId">The event id associated with the log.</param>
        /// <param name="exception">The exception to log.</param>
        /// <param name="message">Format string of the log message.</param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        private static void Log(LogLevel logLevel, EventId eventId, Exception exception, string message, params object[] args)
        {
            if (Tracing.IsEnabled())
            {
                // call the legacy logging handler (TraceEvent)
                Utils.Trace(exception, GetTraceMask(eventId, logLevel), message, false, args);
            }
            else
            {
                Logger.Log(logLevel, eventId, exception, message, args);
            }
        }

        //------------------------------------------Scope------------------------------------------//

        /// <summary>
        /// Formats the message and creates a scope.
        /// </summary>
        /// <param name="messageFormat">Format string of the log message in message template format. Example: <code>"User {User} logged in from {Address}"</code></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <returns>A disposable scope object. Can be null.</returns>
        /// <example>
        /// using(BeginScope("Processing request from {Address}", address))
        /// {
        /// }
        /// </example>
        public static IDisposable BeginScope(
            string messageFormat,
            params object[] args)
        {
            return Logger.BeginScope(messageFormat, args);
        }

        //------------------------------------------Private------------------------------------------//

        /// <summary>
        /// To determine a mask from the log level.
        /// </summary>
        /// <param name="eventId">The event id.</param>
        /// <param name="logLevel">The log level.</param>
        private static int GetTraceMask(EventId eventId, LogLevel logLevel)
        {
            int mask = eventId.Id & TraceMasks.All;
            if (mask == 0)
            {
                switch (logLevel)
                {
                    case LogLevel.Error:
                        mask = TraceMasks.Error | TraceMasks.StackTrace;
                        break;
                    default:
                    case LogLevel.Information:
                        mask = TraceMasks.Information; break;
                }
            }
            return mask;
        }
    }
}
