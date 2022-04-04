/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
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
 
//
// Portions of this logging abstraction class were derived from:
//
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// https://github.com/dotnet/runtime/blob/main/src/libraries/Microsoft.Extensions.Logging.Abstractions/src/LoggerExtensions.cs
//

// Disable: 'Use the LoggerMessage delegates'
#pragma warning disable CA1848 

using System;
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Opc.Ua
{
    /// <summary>
    /// Logger Utils methods.
    /// </summary>
    /// <remarks>
    /// To simplify porting from Utils.Trace and to avoid
    /// name collisons with anything that is called 'Log'
    /// the Utils class hosts the Logger class.
    /// </remarks>
    public static partial class Utils
    {
        #region Public Logger Objects
        /// <summary>
        /// The high performance EventSource log interface.
        /// </summary>
        internal static OpcUaCoreEventSource EventLog { get; } = new OpcUaCoreEventSource();

        /// <summary>
        /// ILogger abstraction used by all Utils.LogXXX methods.
        /// </summary>
        public static ILogger Logger { get; private set; } = new TraceEventLogger();

        /// <summary>
        /// Sets the LogLevel for the TraceEventLogger.
        /// </summary>
        /// <remarks>
        /// The setting is ignored if ILogger is replaced. 
        /// </remarks>
        public static void SetLogLevel(LogLevel logLevel)
        {
            if (Logger is TraceEventLogger tlogger)
            {
                tlogger.LogLevel = logLevel;
            }
        }

        /// <summary>
        /// Sets the ILogger.
        /// </summary>
        public static void SetLogger(ILogger logger)
        {
            Logger = logger;
            UseTraceEvent = false;
        }

        /// <summary>
        /// If the legacy trace event handler should be used.
        /// </summary>
        /// <remarks>By default true, however a call to SetLogger disables it.</remarks>
        public static bool UseTraceEvent { get; set; } = true;
        #endregion

        #region Certificate Log Methods
        /// <summary>
        /// Formats and writes a log message for a certificate.
        /// </summary>
        /// <param name="message">The log message as string.</param>
        /// <param name="certificate">The certificate information to be logged.</param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        public static void LogCertificate(string message, X509Certificate2 certificate, params object[] args)
        {
            LogCertificate(LogLevel.Information, 0, message, certificate, args);
        }

        /// <summary>
        /// Formats and writes a log message for a certificate.
        /// </summary>
        /// <param name="logLevel">Entry will be written on this level.</param>
        /// <param name="message">The log message as string.</param>
        /// <param name="certificate">The certificate information to be logged.</param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        public static void LogCertificate(LogLevel logLevel, string message, X509Certificate2 certificate, params object[] args)
        {
            LogCertificate(logLevel, 0, message, certificate, args);
        }

        /// <summary>
        /// Formats and writes a log message for a certificate.
        /// </summary>
        /// <param name="eventId">The event id associated with the log.</param>
        /// <param name="message">The log message as string.</param>
        /// <param name="certificate">The certificate information to be logged.</param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        public static void LogCertificate(EventId eventId, string message, X509Certificate2 certificate, params object[] args)
        {
            LogCertificate(LogLevel.Information, eventId, message, certificate, args);
        }

        /// <summary>
        /// Formats and writes a log message for a certificate.
        /// </summary>
        /// <param name="logLevel">Entry will be written on this level.</param>
        /// <param name="eventId">The event id associated with the log.</param>
        /// <param name="message">The log message as string.</param>
        /// <param name="certificate">The certificate information to be logged.</param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        public static void LogCertificate(LogLevel logLevel, EventId eventId, string message, X509Certificate2 certificate, params object[] args)
        {
            if (Logger.IsEnabled(logLevel))
            {
                var builder = new StringBuilder()
                    .Append(message);
                if (certificate != null)
                {
                    int argsLength = args.Length;
                    builder.Append(" [{");
                    builder.Append(argsLength);
                    builder.Append("}] [{");
                    builder.Append(argsLength + 1);
                    builder.Append("}]");
                    object[] allArgs = new object[argsLength + 2];
                    for (int i = 0; i < argsLength; i++)
                    {
                        allArgs[i] = args[i];
                    }
                    allArgs[argsLength] = certificate.Subject;
                    allArgs[argsLength + 1] = certificate.Thumbprint;
                    Log(logLevel, eventId, builder.ToString(), allArgs);
                }
                else
                {
                    builder.Append(" (none)");
                    Log(logLevel, eventId, builder.ToString(), args);
                }
            }
        }
        #endregion

        #region Debug Log Methods
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
        #endregion

        #region Trace Log Methods
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
            if (EventLog.IsEnabled())
            {
                EventLog.Log(LogLevel.Trace, eventId, exception, message, args);
            }
            else if (Logger.IsEnabled(LogLevel.Trace))
            {
                Log(LogLevel.Trace, eventId, exception, message, args);
            }
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
            if (EventLog.IsEnabled())
            {
                EventLog.Log(LogLevel.Trace, eventId, message, args);
            }
            else if (Logger.IsEnabled(LogLevel.Trace))
            {
                Log(LogLevel.Trace, eventId, message, args);
            }
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
            if (EventLog.IsEnabled())
            {
                EventLog.Log(LogLevel.Trace, 0, exception, message, args);
            }
            else if (Logger.IsEnabled(LogLevel.Trace))
            {
                Log(LogLevel.Trace, 0, exception, message, args);
            }
        }

        /// <summary>
        /// Formats and writes a trace log message.
        /// </summary>
        /// <param name="message">Format string of the log message in message template format. Example: <code>"User {User} logged in from {Address}"</code></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogTrace("Processing request from {Address}", address)</example>
        public static void LogTrace(string message, params object[] args)
        {
            if (EventLog.IsEnabled())
            {
                EventLog.Log(LogLevel.Trace, 0, message, args);
            }
            else if (Logger.IsEnabled(LogLevel.Trace))
            {
                Log(LogLevel.Trace, 0, message, args);
            }
        }
        #endregion

        #region Information Log Methods
        /// <summary>
        /// Formats and writes an informational log message.
        /// </summary>
        /// <param name="eventId">The event id associated with the log.</param>
        /// <param name="exception">The exception to log.</param>
        /// <param name="message">Format string of the log message in message template format. Example: <code>"User {User} logged in from {Address}"</code></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogInformation(0, exception, "Error while processing request from {Address}", address)</example>
        public static void LogInfo(EventId eventId, Exception exception, string message, params object[] args)
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
        public static void LogInfo(EventId eventId, string message, params object[] args)
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
        public static void LogInfo(Exception exception, string message, params object[] args)
        {
            Log(LogLevel.Information, exception, message, args);
        }

        /// <summary>
        /// Formats and writes an informational log message.
        /// </summary>
        /// <param name="message">Format string of the log message in message template format. Example: <code>"User {User} logged in from {Address}"</code></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogInformation("Processing request from {Address}", address)</example>
        public static void LogInfo(string message, params object[] args)
        {
            Log(LogLevel.Information, message, args);
        }
        #endregion

        #region Warning Log Methods
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
        #endregion

        #region Error Log Methods
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
        #endregion

        #region Critical Log Methods
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
        #endregion

        #region Log Methods
        /// <summary>
        /// Formats and writes a log message at the specified log level.
        /// </summary>
        /// <param name="logLevel">Entry will be written on this level.</param>
        /// <param name="message">Format string of the log message.</param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
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
        public static void Log(LogLevel logLevel, EventId eventId, string message, params object[] args)
        {
            if (EventLog.IsEnabled())
            {
                EventLog.Log(logLevel, eventId, null, message, args);
            }
            else if (Logger.IsEnabled(logLevel))
            {
                // note: to support semantic logging strings
                if (UseTraceEvent && Tracing.IsEnabled())
                {
                    // call the legacy logging handler (TraceEvent)
                    int traceMask = GetTraceMask(eventId, logLevel);
                    Tracing.Instance.RaiseTraceEvent(new TraceEventArgs(traceMask, message, string.Empty, null, args));
                    // done if mask not enabled, otherwise legacy write handler is
                    // called via logger interface to handle semantic logging.
                    if ((s_traceMasks & traceMask) == 0)
                    {
                        return;
                    }
                }
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
        public static void Log(LogLevel logLevel, Exception exception, string message, params object[] args)
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
        public static void Log(LogLevel logLevel, EventId eventId, Exception exception, string message, params object[] args)
        {
            if (EventLog.IsEnabled())
            {
                EventLog.Log(logLevel, eventId, exception, message, args);
            }
            else if (Logger.IsEnabled(logLevel))
            {
                if (UseTraceEvent && Tracing.IsEnabled())
                {
                    // call the legacy logging handler (TraceEvent)
                    int traceMask = GetTraceMask(eventId, logLevel);
                    Tracing.Instance.RaiseTraceEvent(new TraceEventArgs(traceMask, message, string.Empty, exception, args));
                    // done if mask not enabled, otherwise legacy write handler is
                    // called via logger interface to handle semantic logging.
                    if ((s_traceMasks & traceMask) == 0)
                    {
                        return;
                    }
                }
                Logger.Log(logLevel, eventId, exception, message, args);
            }
        }
        #endregion

        #region Scope Method
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
            if (EventLog.IsEnabled())
            {
                return EventLog.BeginScope(messageFormat, args);
            }
            return Logger.BeginScope(messageFormat, args);
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// To determine a mask from the log level.
        /// </summary>
        /// <param name="eventId">The event id.</param>
        /// <param name="logLevel">The log level.</param>
        internal static int GetTraceMask(EventId eventId, LogLevel logLevel)
        {
            int mask = eventId.Id & TraceMasks.All;
            if (mask == 0)
            {
                switch (logLevel)
                {
                    case LogLevel.Critical:
                    case LogLevel.Warning:
                    case LogLevel.Error:
                        mask = TraceMasks.Error;
                        break;
                    case LogLevel.Information:
                        mask = TraceMasks.Information;
                        break;
#if DEBUG
                    case LogLevel.Debug:
#endif
                    case LogLevel.Trace:
                        mask = TraceMasks.Operation;
                        break;
                }
            }
            return mask;
        }
        #endregion
    }
}
