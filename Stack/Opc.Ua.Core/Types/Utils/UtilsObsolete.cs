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
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;
using Microsoft.Extensions.Logging;

namespace Opc.Ua
{
    /// <summary>
    /// Defines various static utility functions.
    /// </summary>
    public static partial class Utils
    {
        /// <summary>
        /// Sets the output for tracing (thread safe).
        /// </summary>
        [Obsolete("Use a customized ITelemetryContext.LoggerFactory instead.")]
        public static void SetTraceOutput(TraceOutput output)
        {
            LoggerProvider.SetTraceOutput(output);
        }

        /// <summary>
        /// Gets the current trace mask settings.
        /// </summary>
        [Obsolete("Use a customized ITelemetryContext.LoggerFactory instead.")]
        public static int TraceMask => LoggerProvider.TraceMask;

        /// <summary>
        /// Sets the mask for tracing (thread safe).
        /// </summary>
        [Obsolete("Use a customized ITelemetryContext.LoggerFactory instead.")]
        public static void SetTraceMask(int masks)
        {
            LoggerProvider.SetTraceMask(masks);
        }

        /// <summary>
        /// Returns Tracing class instance for event attaching.
        /// </summary>
        [Obsolete("Use a customized ITelemetryContext.LoggerFactory instead.")]
        public static Tracing Tracing => LoggerProvider.Tracing;

        /// <summary>
        /// Sets the path to the log file to use for tracing.
        /// </summary>
        [Obsolete("Use a customized ITelemetryContext.LoggerFactory instead.")]
        public static void SetTraceLog(string filePath, bool deleteExisting)
        {
            LoggerProvider.SetTraceLog(filePath, deleteExisting);
        }

        /// <summary>
        /// Writes an informational message to the trace log.
        /// </summary>
        [Obsolete("Use ITelemetryContext.CreateLogger and ILogger.LogTrace instead.")]
        public static void Trace(string message)
        {
            LogInformation(message);
        }

        /// <summary>
        /// Writes an informational message to the trace log.
        /// </summary>
        [Obsolete("Use ITelemetryContext.CreateLogger and ILogger.LogTrace instead.")]
        public static void Trace(string format, params object[] args)
        {
            LogInformation(format, args);
        }

        /// <summary>
        /// Writes an informational message to the trace log.
        /// </summary>
        [Conditional("DEBUG")]
        [Obsolete("Use ITelemetryContext.CreateLogger and ILogger.LogDebug instead.")]
        public static void TraceDebug(string format, params object[] args)
        {
            LogDebug(format, args);
        }

        /// <summary>
        /// Writes an exception/error message to the trace log.
        /// </summary>
        [Obsolete("Use ITelemetryContext.CreateLogger and ILogger.LogError instead.")]
        public static void Trace(Exception e, string message)
        {
            LogError(e, message);
        }

        /// <summary>
        /// Writes an exception/error message to the trace log.
        /// </summary>
        [Obsolete("Use ITelemetryContext.CreateLogger and ILogger.LogError instead.")]
        public static void Trace(Exception e, string format, params object[] args)
        {
            LogError(e, format, args);
        }

        /// <summary>
        /// Writes an exception/error message to the trace log.
        /// </summary>
        [Obsolete("Use ITelemetryContext.CreateLogger and ILogger.LogError instead.")]
        public static void Trace(Exception e, string format, bool handled, params object[] args)
        {
            LogError(e, format, handled, args);
        }

        /// <summary>
        /// Writes a message to the trace log.
        /// </summary>
        [Obsolete("Use ITelemetryContext.CreateLogger and ILogger.Log instead.")]
        public static void Trace(int traceMask, string format, params object[] args)
        {
            Log(traceMask, format, args);
        }

        /// <summary>
        /// Writes a message to the trace log.
        /// </summary>
        [Obsolete("Use ITelemetryContext.CreateLogger and ILogger.Log instead.")]
        public static void Trace(int traceMask, string format, bool handled, params object[] args)
        {
            Log(traceMask, format, handled, args);
        }

        /// <summary>
        /// Writes a message to the trace log.
        /// </summary>
        /// <typeparam name="TState"></typeparam>
        [Obsolete("Use ITelemetryContext.CreateLogger and ILogger.Log instead.")]
        public static void Trace<TState>(
            TState state,
            Exception exception,
            int traceMask,
            Func<TState, Exception, string> formatter)
        {
            LoggerProvider.Log(state, exception, traceMask, formatter);
        }

        /// <summary>
        /// Writes a message to the trace log.
        /// </summary>
        [Obsolete("Use ITelemetryContext.CreateLogger and ILogger.Log instead.")]
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
        /// Sets the LogLevel for the global Logger.
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

        /// <summary>
        /// Formats and writes a debug log message.
        /// </summary>
        /// <param name="eventId">The event id associated with the log.</param>
        /// <param name="exception">The exception to log.</param>
        /// <param name="message">Format string of the log message in message template format. Example: <c>"User {User} logged in from {Address}"</c></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogDebug(0, exception, "Error while processing request from {Address}", address)</example>
        [Conditional("DEBUG")]
        [Obsolete("Use ITelemetryContext ILoggerFactory and ILogger.LogDebug.")]
        public static void LogDebug(
            EventId eventId,
            Exception exception,
            string message,
            params object[] args)
        {
            Log(LogLevel.Debug, eventId, exception, message, args);
        }

        /// <summary>
        /// Formats and writes a debug log message.
        /// </summary>
        /// <param name="eventId">The event id associated with the log.</param>
        /// <param name="message">Format string of the log message in message template format. Example: <c>"User {User} logged in from {Address}"</c></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogDebug(0, "Processing request from {Address}", address)</example>
        [Conditional("DEBUG")]
        [Obsolete("Use ITelemetryContext ILoggerFactory and ILogger.LogDebug.")]
        public static void LogDebug(EventId eventId, string message, params object[] args)
        {
            Log(LogLevel.Debug, eventId, message, args);
        }

        /// <summary>
        /// Formats and writes a debug log message.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="message">Format string of the log message in message template format. Example: <c>"User {User} logged in from {Address}"</c></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogDebug(exception, "Error while processing request from {Address}", address)</example>
        [Conditional("DEBUG")]
        [Obsolete("Use ITelemetryContext ILoggerFactory and ILogger.LogDebug.")]
        public static void LogDebug(Exception exception, string message, params object[] args)
        {
            Log(LogLevel.Debug, exception, message, args);
        }

        /// <summary>
        /// Formats and writes a debug log message.
        /// </summary>
        /// <param name="message">Format string of the log message in message template format. Example: <c>"User {User} logged in from {Address}"</c></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogDebug("Processing request from {Address}", address)</example>
        [Conditional("DEBUG")]
        [Obsolete("Use ITelemetryContext ILoggerFactory and ILogger.LogDebug.")]
        public static void LogDebug(string message, params object[] args)
        {
            Log(LogLevel.Debug, message, args);
        }

        /// <summary>
        /// Formats and writes a trace log message.
        /// </summary>
        /// <param name="eventId">The event id associated with the log.</param>
        /// <param name="exception">The exception to log.</param>
        /// <param name="message">Format string of the log message in message template format. Example: <c>"User {User} logged in from {Address}"</c></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogTrace(0, exception, "Error while processing request from {Address}", address)</example>
        [Obsolete("Use ITelemetryContext ILoggerFactory and ILogger.LogTrace.")]
        public static void LogTrace(
            EventId eventId,
            Exception exception,
            string message,
            params object[] args)
        {
            Log(LogLevel.Trace, eventId, exception, message, args);
        }

        /// <summary>
        /// Formats and writes a trace log message.
        /// </summary>
        /// <param name="eventId">The event id associated with the log.</param>
        /// <param name="message">Format string of the log message in message template format. Example: <c>"User {User} logged in from {Address}"</c></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogTrace(0, "Processing request from {Address}", address)</example>
        [Obsolete("Use ITelemetryContext ILoggerFactory and ILogger.LogTrace.")]
        public static void LogTrace(EventId eventId, string message, params object[] args)
        {
            Log(LogLevel.Trace, eventId, message, args);
        }

        /// <summary>
        /// Formats and writes a trace log message.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="message">Format string of the log message in message template format. Example: <c>"User {User} logged in from {Address}"</c></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogTrace(exception, "Error while processing request from {Address}", address)</example>
        [Obsolete("Use ITelemetryContext ILoggerFactory and ILogger.LogTrace.")]
        public static void LogTrace(Exception exception, string message, params object[] args)
        {
            Log(LogLevel.Trace, 0, exception, message, args);
        }

        /// <summary>
        /// Formats and writes a trace log message.
        /// </summary>
        /// <param name="message">Format string of the log message in message template format. Example: <c>"User {User} logged in from {Address}"</c></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogTrace("Processing request from {Address}", address)</example>
        [Obsolete("Use ITelemetryContext ILoggerFactory and ILogger.LogTrace.")]
        public static void LogTrace(string message, params object[] args)
        {
            Log(LogLevel.Trace, 0, message, args);
        }

        /// <summary>
        /// Formats and writes an informational log message.
        /// </summary>
        /// <param name="eventId">The event id associated with the log.</param>
        /// <param name="exception">The exception to log.</param>
        /// <param name="message">Format string of the log message in message template format. Example: <c>"User {User} logged in from {Address}"</c></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogInformation(0, exception, "Error while processing request from {Address}", address)</example>
        [Obsolete("Use ITelemetryContext ILoggerFactory and ILogger.LogInformation.")]
        public static void LogInformation(
            EventId eventId,
            Exception exception,
            string message,
            params object[] args)
        {
            Log(LogLevel.Information, eventId, exception, message, args);
        }

        /// <summary>
        /// Formats and writes an informational log message.
        /// </summary>
        /// <param name="eventId">The event id associated with the log.</param>
        /// <param name="message">Format string of the log message in message template format. Example: <c>"User {User} logged in from {Address}"</c></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogInformation(0, "Processing request from {Address}", address)</example>
        [Obsolete("Use ITelemetryContext ILoggerFactory and ILogger.LogInformation.")]
        public static void LogInformation(EventId eventId, string message, params object[] args)
        {
            Log(LogLevel.Information, eventId, message, args);
        }

        /// <summary>
        /// Formats and writes an informational log message.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="message">Format string of the log message in message template format. Example: <c>"User {User} logged in from {Address}"</c></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogInformation(exception, "Error while processing request from {Address}", address)</example>
        [Obsolete("Use ITelemetryContext ILoggerFactory and ILogger.LogInformation.")]
        public static void LogInformation(Exception exception, string message, params object[] args)
        {
            Log(LogLevel.Information, exception, message, args);
        }

        /// <summary>
        /// Formats and writes an informational log message.
        /// </summary>
        /// <param name="message">Format string of the log message in message template format. Example: <c>"User {User} logged in from {Address}"</c></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogInformation("Processing request from {Address}", address)</example>
        [Obsolete("Use ITelemetryContext ILoggerFactory and ILogger.LogInformation.")]
        public static void LogInformation(string message, params object[] args)
        {
            Log(LogLevel.Information, message, args);
        }

        /// <summary>
        /// Formats and writes a warning log message.
        /// </summary>
        /// <param name="eventId">The event id associated with the log.</param>
        /// <param name="exception">The exception to log.</param>
        /// <param name="message">Format string of the log message in message template format. Example: <c>"User {User} logged in from {Address}"</c></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogWarning(0, exception, "Error while processing request from {Address}", address)</example>
        [Obsolete("Use ITelemetryContext ILoggerFactory and ILogger.LogWarning.")]
        public static void LogWarning(
            EventId eventId,
            Exception exception,
            string message,
            params object[] args)
        {
            Log(LogLevel.Warning, eventId, exception, message, args);
        }

        /// <summary>
        /// Formats and writes a warning log message.
        /// </summary>
        /// <param name="eventId">The event id associated with the log.</param>
        /// <param name="message">Format string of the log message in message template format. Example: <c>"User {User} logged in from {Address}"</c></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogWarning(0, "Processing request from {Address}", address)</example>
        [Obsolete("Use ITelemetryContext ILoggerFactory and ILogger.LogWarning.")]
        public static void LogWarning(EventId eventId, string message, params object[] args)
        {
            Log(LogLevel.Warning, eventId, message, args);
        }

        /// <summary>
        /// Formats and writes a warning log message.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="message">Format string of the log message in message template format. Example: <c>"User {User} logged in from {Address}"</c></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogWarning(exception, "Error while processing request from {Address}", address)</example>
        [Obsolete("Use ITelemetryContext ILoggerFactory and ILogger.LogWarning.")]
        public static void LogWarning(Exception exception, string message, params object[] args)
        {
            Log(LogLevel.Warning, exception, message, args);
        }

        /// <summary>
        /// Formats and writes a warning log message.
        /// </summary>
        /// <param name="message">Format string of the log message in message template format. Example: <c>"User {User} logged in from {Address}"</c></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogWarning("Processing request from {Address}", address)</example>
        [Obsolete("Use ITelemetryContext ILoggerFactory and ILogger.LogWarning.")]
        public static void LogWarning(string message, params object[] args)
        {
            Log(LogLevel.Warning, message, args);
        }

        /// <summary>
        /// Formats and writes an error log message.
        /// </summary>
        /// <param name="eventId">The event id associated with the log.</param>
        /// <param name="exception">The exception to log.</param>
        /// <param name="message">Format string of the log message in message template format. Example: <c>"User {User} logged in from {Address}"</c></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogError(0, exception, "Error while processing request from {Address}", address)</example>
        [Obsolete("Use ITelemetryContext ILoggerFactory and ILogger.LogError.")]
        public static void LogError(
            EventId eventId,
            Exception exception,
            string message,
            params object[] args)
        {
            Log(LogLevel.Error, eventId, exception, message, args);
        }

        /// <summary>
        /// Formats and writes an error log message.
        /// </summary>
        /// <param name="eventId">The event id associated with the log.</param>
        /// <param name="message">Format string of the log message in message template format. Example: <c>"User {User} logged in from {Address}"</c></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogError(0, "Processing request from {Address}", address)</example>
        [Obsolete("Use ITelemetryContext ILoggerFactory and ILogger.LogError.")]
        public static void LogError(EventId eventId, string message, params object[] args)
        {
            Log(LogLevel.Error, eventId, message, args);
        }

        /// <summary>
        /// Formats and writes an error log message.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="message">Format string of the log message in message template format. Example: <c>"User {User} logged in from {Address}"</c></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogError(exception, "Error while processing request from {Address}", address)</example>
        [Obsolete("Use ITelemetryContext ILoggerFactory and ILogger.LogError.")]
        public static void LogError(Exception exception, string message, params object[] args)
        {
            Log(LogLevel.Error, exception, message, args);
        }

        /// <summary>
        /// Formats and writes an error log message.
        /// </summary>
        /// <param name="message">Format string of the log message in message template format. Example: <c>"User {User} logged in from {Address}"</c></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogError("Processing request from {Address}", address)</example>
        [Obsolete("Use ITelemetryContext ILoggerFactory and ILogger.LogError.")]
        public static void LogError(string message, params object[] args)
        {
            Log(LogLevel.Error, message, args);
        }

        /// <summary>
        /// Writes an exception/error message to the trace log.
        /// </summary>
        [Obsolete("Use ITelemetryContext ILoggerFactory and ILogger.LogError.")]
        public static void LogError(Exception e, string format, bool handled, params object[] args)
        {
            StringBuilder message = LoggerProvider.TraceExceptionMessage(e, format, args);

            // trace message.
            Log(e, TraceMasks.Error, message.ToString(), handled);
        }

        /// <summary>
        /// Formats and writes a critical log message.
        /// </summary>
        /// <param name="eventId">The event id associated with the log.</param>
        /// <param name="exception">The exception to log.</param>
        /// <param name="message">Format string of the log message in message template format. Example: <c>"User {User} logged in from {Address}"</c></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogCritical(0, exception, "Error while processing request from {Address}", address)</example>
        [Obsolete("Use ITelemetryContext ILoggerFactory and ILogger.LogCritical.")]
        public static void LogCritical(
            EventId eventId,
            Exception exception,
            string message,
            params object[] args)
        {
            Log(LogLevel.Critical, eventId, exception, message, args);
        }

        /// <summary>
        /// Formats and writes a critical log message.
        /// </summary>
        /// <param name="eventId">The event id associated with the log.</param>
        /// <param name="message">Format string of the log message in message template format. Example: <c>"User {User} logged in from {Address}"</c></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogCritical(0, "Processing request from {Address}", address)</example>
        [Obsolete("Use ITelemetryContext ILoggerFactory and ILogger.LogCritical.")]
        public static void LogCritical(EventId eventId, string message, params object[] args)
        {
            Log(LogLevel.Critical, eventId, message, args);
        }

        /// <summary>
        /// Formats and writes a critical log message.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="message">Format string of the log message in message template format. Example: <c>"User {User} logged in from {Address}"</c></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogCritical(exception, "Error while processing request from {Address}", address)</example>
        [Obsolete("Use ITelemetryContext ILoggerFactory and ILogger.LogCritical.")]
        public static void LogCritical(Exception exception, string message, params object[] args)
        {
            Log(LogLevel.Critical, exception, message, args);
        }

        /// <summary>
        /// Formats and writes a critical log message.
        /// </summary>
        /// <param name="message">Format string of the log message in message template format. Example: <c>"User {User} logged in from {Address}"</c></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogCritical("Processing request from {Address}", address)</example>
        [Obsolete("Use ITelemetryContext ILoggerFactory and ILogger.LogCritical.")]
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
        [Obsolete("Use ITelemetryContext ILoggerFactory and ILogger.Log.")]
        public static void Log(LogLevel logLevel, string message, params object[] args)
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
        [Obsolete("Use ITelemetryContext ILoggerFactory and ILogger.Log.")]
        public static void Log(
            LogLevel logLevel,
            EventId eventId,
            string message,
            params object[] args)
        {
            if (Tracing.IsEnabled())
            {
                // call the legacy logging handler (TraceEvent)
                int traceMask = TraceLoggerProvider.GetTraceMask(eventId, logLevel);
                Tracing.Instance.RaiseTraceEvent(
                    new TraceEventArgs(traceMask, message, string.Empty, null, args));
            }
        }

        /// <summary>
        /// Formats and writes a log message at the specified log level.
        /// </summary>
        /// <param name="logLevel">Entry will be written on this level.</param>
        /// <param name="exception">The exception to log.</param>
        /// <param name="message">Format string of the log message.</param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        [Obsolete("Use ITelemetryContext ILoggerFactory and ILogger.Log.")]
        public static void Log(
            LogLevel logLevel,
            Exception exception,
            string message,
            params object[] args)
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
        [Obsolete("Use ITelemetryContext ILoggerFactory and ILogger.Log.")]
        public static void Log(
            LogLevel logLevel,
            EventId eventId,
            Exception exception,
            string message,
            params object[] args)
        {
            if (Tracing.IsEnabled())
            {
                // call the legacy logging handler (TraceEvent)
                int traceMask = TraceLoggerProvider.GetTraceMask(eventId, logLevel);
                LoggerProvider.Tracing.RaiseTraceEvent(
                    new TraceEventArgs(traceMask, message, string.Empty, exception, args));
            }
        }

        /// <summary>
        /// Writes a message to the trace log.
        /// </summary>
        [Obsolete("Use ITelemetryContext ILoggerFactory and ILogger.Log.")]
        public static void Log(int traceMask, string format, params object[] args)
        {
            const int informationMask = TraceMasks.Information |
                TraceMasks.StartStop |
                TraceMasks.Security;
            const int errorMask = TraceMasks.Error | TraceMasks.StackTrace;
            if ((traceMask & errorMask) != 0)
            {
                LogError(traceMask, format, args);
            }
            else if ((traceMask & informationMask) != 0)
            {
                LogInformation(traceMask, format, args);
            }
            else
            {
                LogTrace(traceMask, format, args);
            }
        }

        /// <summary>
        /// Writes a message to the trace log.
        /// </summary>
        [Obsolete("Use ITelemetryContext ILoggerFactory and ILogger.Log.")]
        public static void Log(int traceMask, string format, bool handled, params object[] args)
        {
            Log(null, traceMask, format, handled, args);
        }

        /// <summary>
        /// Creates a X509 certificate object from the DER encoded bytes.
        /// </summary>
        [Obsolete("Use ParseCertificateBlob with telemetry context.")]
        public static X509Certificate2 ParseCertificateBlob(
            ReadOnlyMemory<byte> certificateData,
            bool useAsnParser = false)
        {
            return ParseCertificateBlob(certificateData, null, useAsnParser);
        }

        /// <summary>
        /// Creates a X509 certificate collection object from the DER encoded bytes.
        /// </summary>
        [Obsolete("Use ParseCertificateChainBlobs with telemetry context.")]
        public static X509Certificate2Collection ParseCertificateChainBlob(
            ReadOnlyMemory<byte> certificateData,
            bool useAsnParser = false)
        {
            return ParseCertificateChainBlob(certificateData, null, useAsnParser);
        }

        /// <summary>
        /// Looks for an extension with the specified type and uses the DataContractSerializer to serializes its replacement.
        /// </summary>
        /// <typeparam name="T">The type of extension.</typeparam>
        [Obsolete("Use UpdateExtension with telemetry context.")]
        public static void UpdateExtension<T>(
            ref XmlElementCollection extensions,
            XmlQualifiedName elementName,
            object value)
        {
            UpdateExtension<T>(ref extensions, elementName, value, null);
        }

        /// <summary>
        /// Looks for an extension with the specified type and uses the DataContractSerializer to parse it.
        /// </summary>
        /// <typeparam name="T">The type of extension.</typeparam>
        [Obsolete("Use ParseExtension with telemetry context.")]
        public static T ParseExtension<T>(
            IList<XmlElement> extensions,
            XmlQualifiedName elementName)
        {
            return ParseExtension<T>(extensions, elementName, null);
        }

        /// <summary>
        /// Checks if the file path is a relative path and returns an absolute path relative to the EXE location.
        /// </summary>
        [Obsolete("Catch exceptions from GetAbsoluteFilePath and handle !throwOnError")]
        public static string GetAbsoluteFilePath(
            string filePath,
            bool checkCurrentDirectory,
            bool throwOnError,
            bool createAlways,
            bool writable = false)
        {
            try
            {
                return GetAbsoluteFilePath(filePath, checkCurrentDirectory, createAlways, writable);
            }
            catch (Exception e) when (!throwOnError)
            {
                Fallback.Logger.LogError(e, "Could not get absolute path for {FileName}", filePath);
                return null;
            }
        }

        /// <summary>
        /// Finds the file by search the common file folders and then bin directories in the source tree
        /// </summary>
        /// <param name="fileName">Name of the file.</param>
        /// <returns>The path to the file. Null if not found.</returns>
        [Obsolete("Not used in stack")]
        public static string FindInstalledFile(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return null;
            }

            string path = null;

            // check source tree.
            var directory = new DirectoryInfo(Directory.GetCurrentDirectory());

            while (directory != null)
            {
                var buffer = new StringBuilder();
                buffer.Append(directory.FullName)
                    .Append(Path.DirectorySeparatorChar)
                    .Append("Bin")
                    .Append(Path.DirectorySeparatorChar)
                    .Append(fileName);

                try
                {
                    path = GetAbsoluteFilePath(
                        buffer.ToString(),
                        checkCurrentDirectory: false,
                        createAlways: false);
                }
                catch (Exception e)
                {
                    Fallback.Logger.LogTrace(e, "Could not find installed file: {FileName}", fileName);
                }

                if (path != null)
                {
                    break;
                }

                directory = directory.Parent;
            }

            // return what was found.
            return path;
        }

        /// <summary>
        /// Writes a message to the trace log.
        /// </summary>
        private static void Log(
            Exception e,
            int traceMask,
            string format,
            bool handled,
            params object[] args)
        {
            if (!handled)
            {
                Tracing.Instance
                    .RaiseTraceEvent(new TraceEventArgs(traceMask, format, string.Empty, e, args));
            }

            // do nothing if mask not enabled.
            if ((LoggerProvider.TraceMask & traceMask) == 0)
            {
                return;
            }

            var message = new StringBuilder();

            // append process and timestamp.
            message.AppendFormat(
                CultureInfo.InvariantCulture,
                "{0:d} {0:HH:mm:ss.fff} ",
                DateTime.UtcNow.ToLocalTime());

            // format message.
            if (args != null && args.Length > 0)
            {
                try
                {
                    message.AppendFormat(CultureInfo.InvariantCulture, format, args);
                }
                catch (Exception)
                {
                    message.Append(format);
                }
            }
            else
            {
                message.Append(format);
            }

            LoggerProvider.TraceWriteLine(message.ToString());
        }
    }
}
