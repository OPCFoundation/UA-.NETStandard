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

#nullable enable

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Opc.Ua.Redaction;

#pragma warning disable IDE0079 // Remove unnecessary suppression
#pragma warning disable CA2254 // Template should be a static expression
#pragma warning restore IDE0079 // Remove unnecessary suppression

namespace Opc.Ua
{
    /// <summary>
    /// Logger Utils methods.
    /// </summary>
    /// <remarks>
    /// To simplify porting from Utils.Trace and to avoid
    /// name collisions with anything that is called 'Log'
    /// the Utils class hosts the Logger class.
    /// </remarks>
    public partial class Utils
    {
        /// <summary>
        /// The high performance EventSource log interface.
        /// </summary>
        internal static OpcUaCoreEventSource EventLog { get; } = new OpcUaCoreEventSource();
#if DEBUG
        private static int s_traceOutput = (int)TraceOutput.DebugAndFile;
#else
        private static int s_traceOutput = (int)TraceOutput.FileOnly;
#endif

        private static string s_traceFileName = string.Empty;
        private static readonly Lock s_traceFileLock = new();

        /// <summary>
        /// The possible trace output mechanisms.
        /// </summary>
        public enum TraceOutput
        {
            /// <summary>
            /// No tracing
            /// </summary>
            Off = 0,

            /// <summary>
            /// Only write to file (if specified). Default for Release mode.
            /// </summary>
            FileOnly = 1,

            /// <summary>
            /// Write to debug trace listeners and a file (if specified). Default for Debug mode.
            /// </summary>
            DebugAndFile = 2
        }

        /// <summary>
        /// The masks used to filter trace messages.
        /// </summary>
        public static class TraceMasks
        {
            /// <summary>
            /// Do not output any messages.
            /// </summary>
            public const int None = 0x0;

            /// <summary>
            /// Output error messages.
            /// </summary>
            public const int Error = 0x1;

            /// <summary>
            /// Output informational messages.
            /// </summary>
            public const int Information = 0x2;

            /// <summary>
            /// Output stack traces.
            /// </summary>
            public const int StackTrace = 0x4;

            /// <summary>
            /// Output basic messages for service calls.
            /// </summary>
            public const int Service = 0x8;

            /// <summary>
            /// Output detailed messages for service calls.
            /// </summary>
            public const int ServiceDetail = 0x10;

            /// <summary>
            /// Output basic messages for each operation.
            /// </summary>
            public const int Operation = 0x20;

            /// <summary>
            /// Output detailed messages for each operation.
            /// </summary>
            public const int OperationDetail = 0x40;

            /// <summary>
            /// Output messages related to application initialization or shutdown
            /// </summary>
            public const int StartStop = 0x80;

            /// <summary>
            /// Output messages related to a call to an external system.
            /// </summary>
            public const int ExternalSystem = 0x100;

            /// <summary>
            /// Output messages related to security
            /// </summary>
            public const int Security = 0x200;

            /// <summary>
            /// Output all messages.
            /// </summary>
            public const int All = 0x3FF;
        }

        /// <summary>
        /// Sets the output for tracing (thread safe).
        /// </summary>
        public static void SetTraceOutput(TraceOutput output)
        {
            lock (s_traceFileLock)
            {
                s_traceOutput = (int)output;
            }
        }

        /// <summary>
        /// Gets the current trace mask settings.
        /// </summary>
        public static int TraceMask { get; private set; }
#if DEBUG
            = TraceMasks.All;
#else
            = TraceMasks.None;
#endif

        /// <summary>
        /// Sets the mask for tracing (thread safe).
        /// </summary>
        public static void SetTraceMask(int masks)
        {
            TraceMask = masks;
        }

        /// <summary>
        /// Returns Tracing class instance for event attaching.
        /// </summary>
        public static Tracing Tracing => Tracing.Instance;

        /// <summary>
        /// Sets the path to the log file to use for tracing.
        /// </summary>
        public static void SetTraceLog(string filePath, bool deleteExisting)
        {
            // turn tracing on.
            lock (s_traceFileLock)
            {
                // check if tracing is being turned off.
                if (string.IsNullOrEmpty(filePath))
                {
                    s_traceFileName = string.Empty;
                    return;
                }

                s_traceFileName = GetAbsoluteFilePath(filePath, true, false, true, true);

                if (s_traceOutput == (int)TraceOutput.Off)
                {
                    s_traceOutput = (int)TraceOutput.FileOnly;
                }

                try
                {
                    var file = new FileInfo(s_traceFileName);

                    if (deleteExisting && file.Exists)
                    {
                        file.Delete();
                    }

                    // write initial log message.
                    TraceWriteLine(string.Empty);
                    TraceWriteLine("{1} Logging started at {0}", DateTime.Now, new string('*', 25));
                }
                catch (Exception e)
                {
                    TraceWriteLine(e.Message);
                }
            }
        }

        /// <summary>
        /// Formats and writes a log message for a certificate.
        /// </summary>
        /// <param name="message">The log message as string.</param>
        /// <param name="certificate">The certificate information to be logged.</param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        public static void LogCertificate(
            string message,
            X509Certificate2 certificate,
            params object[] args)
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
        public static void LogCertificate(
            LogLevel logLevel,
            string message,
            X509Certificate2 certificate,
            params object[] args)
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
        public static void LogCertificate(
            EventId eventId,
            string message,
            X509Certificate2 certificate,
            params object[] args)
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
        public static void LogCertificate(
            LogLevel logLevel,
            EventId eventId,
            string message,
            X509Certificate2 certificate,
            params object[] args)
        {
            // TODO: if (Logger.IsEnabled(logLevel))
            {
                StringBuilder builder = new StringBuilder().Append(message);
                if (certificate != null)
                {
                    int argsLength = args.Length;
                    builder.Append(" [{")
                        .Append(argsLength)
                        .Append("}] [{")
                        .Append(argsLength + 1)
                        .Append("}]");
                    object[] allArgs = new object[argsLength + 2];
                    for (int i = 0; i < argsLength; i++)
                    {
                        allArgs[i] = args[i];
                    }
                    allArgs[argsLength] = Redact.Create(certificate.Subject);
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

        /// <summary>
        /// Formats and writes a debug log message.
        /// </summary>
        /// <param name="eventId">The event id associated with the log.</param>
        /// <param name="exception">The exception to log.</param>
        /// <param name="message">Format string of the log message in message template format. Example: <c>"User {User} logged in from {Address}"</c></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogDebug(0, exception, "Error while processing request from {Address}", address)</example>
        [Conditional("DEBUG")]
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
        public static void LogTrace(
            EventId eventId,
            Exception exception,
            string message,
            params object[] args)
        {
            if (EventLog.IsEnabled())
            {
                EventLog.Log(LogLevel.Trace, eventId, exception, message, args);
            }
            Log(LogLevel.Trace, eventId, exception, message, args);
        }

        /// <summary>
        /// Formats and writes a trace log message.
        /// </summary>
        /// <param name="eventId">The event id associated with the log.</param>
        /// <param name="message">Format string of the log message in message template format. Example: <c>"User {User} logged in from {Address}"</c></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogTrace(0, "Processing request from {Address}", address)</example>
        public static void LogTrace(EventId eventId, string message, params object[] args)
        {
            if (EventLog.IsEnabled())
            {
                EventLog.Log(LogLevel.Trace, eventId, message, args);
            }
            Log(LogLevel.Trace, eventId, message, args);
        }

        /// <summary>
        /// Formats and writes a trace log message.
        /// </summary>
        /// <param name="exception">The exception to log.</param>
        /// <param name="message">Format string of the log message in message template format. Example: <c>"User {User} logged in from {Address}"</c></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogTrace(exception, "Error while processing request from {Address}", address)</example>
        public static void LogTrace(Exception exception, string message, params object[] args)
        {
            if (EventLog.IsEnabled())
            {
                EventLog.Log(LogLevel.Trace, 0, exception, message, args);
            }
            Log(LogLevel.Trace, 0, exception, message, args);
        }

        /// <summary>
        /// Formats and writes a trace log message.
        /// </summary>
        /// <param name="message">Format string of the log message in message template format. Example: <c>"User {User} logged in from {Address}"</c></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogTrace("Processing request from {Address}", address)</example>
        public static void LogTrace(string message, params object[] args)
        {
            if (EventLog.IsEnabled())
            {
                EventLog.Log(LogLevel.Trace, 0, message, args);
            }
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
        public static void LogInfo(
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
        public static void LogInfo(EventId eventId, string message, params object[] args)
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
        public static void LogInfo(Exception exception, string message, params object[] args)
        {
            Log(LogLevel.Information, exception, message, args);
        }

        /// <summary>
        /// Formats and writes an informational log message.
        /// </summary>
        /// <param name="message">Format string of the log message in message template format. Example: <c>"User {User} logged in from {Address}"</c></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogInformation("Processing request from {Address}", address)</example>
        public static void LogInfo(string message, params object[] args)
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
        public static void LogError(string message, params object[] args)
        {
            Log(LogLevel.Error, message, args);
        }

        /// <summary>
        /// Formats and writes a critical log message.
        /// </summary>
        /// <param name="eventId">The event id associated with the log.</param>
        /// <param name="exception">The exception to log.</param>
        /// <param name="message">Format string of the log message in message template format. Example: <c>"User {User} logged in from {Address}"</c></param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
        /// <example>LogCritical(0, exception, "Error while processing request from {Address}", address)</example>
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
        public static void Log(
            LogLevel logLevel,
            EventId eventId,
            string message,
            params object[] args)
        {
            if (EventLog.IsEnabled())
            {
                EventLog.Log(logLevel, eventId, null, message, args);
            }
            else if (Tracing.IsEnabled())
            {
                // call the legacy logging handler (TraceEvent)
                int traceMask = GetTraceMask(eventId, logLevel);
                Tracing.Instance.RaiseTraceEvent(
                    new TraceEventArgs(traceMask, message, string.Empty, null, args));
                // done if mask not enabled, otherwise legacy write handler is
                // called via logger interface to handle semantic logging.
                if ((TraceMask & traceMask) == 0)
                {
                    return;
                }
            }
        }

        /// <summary>
        /// Formats and writes a log message at the specified log level.
        /// </summary>
        /// <param name="logLevel">Entry will be written on this level.</param>
        /// <param name="exception">The exception to log.</param>
        /// <param name="message">Format string of the log message.</param>
        /// <param name="args">An object array that contains zero or more objects to format.</param>
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
        public static void Log(
            LogLevel logLevel,
            EventId eventId,
            Exception? exception,
            string message,
            params object[] args)
        {
            if (EventLog.IsEnabled())
            {
                EventLog.Log(logLevel, eventId, exception, message, args);
            }
            else if (Tracing.IsEnabled())
            {
                // call the legacy logging handler (TraceEvent)
                int traceMask = GetTraceMask(eventId, logLevel);
                Tracing.Instance.RaiseTraceEvent(
                    new TraceEventArgs(traceMask, message, string.Empty, exception, args));
                // done if mask not enabled, otherwise legacy write handler is
                // called via logger interface to handle semantic logging.
                if ((TraceMask & traceMask) == 0)
                {
                    return;
                }
            }
        }

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

        /// <summary>
        /// Writes a message to the trace log.
        /// </summary>
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
                LogInfo(traceMask, format, args);
            }
            else
            {
                LogTrace(traceMask, format, args);
            }
        }

        /// <summary>
        /// Writes an exception/error message to the trace log.
        /// </summary>
        public static void LogError(Exception e, string format, bool handled, params object[] args)
        {
            StringBuilder message = TraceExceptionMessage(e, format, args);

            // trace message.
            Log(e, TraceMasks.Error, message.ToString(), handled);
        }

        /// <summary>
        /// Writes a message to the trace log.
        /// </summary>
        public static void Log(int traceMask, string format, bool handled, params object[] args)
        {
            Log(null, traceMask, format, handled, args);
        }

        /// <summary>
        /// Writes a message to the trace log.
        /// </summary>
        /// <typeparam name="TState"></typeparam>
        public static void Log<TState>(
            TState state,
            Exception exception,
            int traceMask,
            Func<TState, Exception, string> formatter)
        {
            // do nothing if mask not enabled.
            bool tracingEnabled = Tracing.IsEnabled();
            bool traceMaskEnabled = (TraceMask & traceMask) != 0;
            if (!traceMaskEnabled && !tracingEnabled)
            {
                return;
            }

            var message = new StringBuilder();
            try
            {
                // append process and timestamp.
                message.AppendFormat(
                    CultureInfo.InvariantCulture,
                    "{0:d} {0:HH:mm:ss.fff} ",
                    DateTime.UtcNow.ToLocalTime())
                    .Append(formatter(state, exception));
                if (exception != null)
                {
                    message.Append(TraceExceptionMessage(exception, string.Empty));
                }
            }
            catch (Exception)
            {
                return;
            }

            string output = message.ToString();
            if (tracingEnabled)
            {
                Tracing.Instance.RaiseTraceEvent(
                    new TraceEventArgs(traceMask, output, string.Empty, exception, []));
            }
            if (traceMaskEnabled)
            {
                TraceWriteLine(output);
            }
        }

        /// <summary>
        /// Writes a message to the trace log.
        /// </summary>
        private static void Log(
            Exception? e,
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
            if ((TraceMask & traceMask) == 0)
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

            TraceWriteLine(message.ToString());
        }

        /// <summary>
        /// Create an exception/error message for a log.
        /// </summary>
        internal static StringBuilder TraceExceptionMessage(
            Exception e,
            string format,
            params object[] args)
        {
            var message = new StringBuilder();

            // format message.
            if (args != null && args.Length > 0)
            {
                try
                {
                    message.AppendFormat(CultureInfo.InvariantCulture, format, args)
                        .AppendLine();
                }
                catch (Exception)
                {
                    message.AppendLine(format);
                }
            }
            else
            {
                message.AppendLine(format);
            }

            // append exception information.
            if (e != null)
            {
                if (e is ServiceResultException sre)
                {
                    message.AppendFormat(
                        CultureInfo.InvariantCulture,
                        " {0} '{1}'",
                        StatusCodes.GetBrowseName(sre.StatusCode),
                        sre.Message);
                }
                else
                {
                    message.AppendFormat(
                        CultureInfo.InvariantCulture,
                        " {0} '{1}'",
                        e.GetType().Name,
                        e.Message);
                }
                message.AppendLine();

                // append stack trace.
                if ((TraceMask & TraceMasks.StackTrace) != 0)
                {
                    message.AppendLine()
                        .AppendLine();
                    string separator = new('=', 40);
                    message.AppendLine(separator)
                        .AppendLine(new ServiceResult(e).ToLongString())
                        .AppendLine(separator);
                }
            }

            return message;
        }

        /// <summary>
        /// Writes a trace statement.
        /// </summary>
        private static void TraceWriteLine(string message, params object[] args)
        {
            // null strings not supported.
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            // format the message if format arguments provided.
            string output = message;

            if (args != null && args.Length > 0)
            {
                try
                {
                    output = string.Format(CultureInfo.InvariantCulture, message, args);
                }
                catch (Exception)
                {
                    output = message;
                }
            }

            TraceWriteLine(output);
        }

        /// <summary>
        /// Writes a trace statement.
        /// </summary>
        private static void TraceWriteLine(string output)
        {
            // write to the log file.
            lock (s_traceFileLock)
            {
                // write to debug trace listeners.
                if (s_traceOutput == (int)TraceOutput.DebugAndFile)
                {
                    Debug.WriteLine(output);
                }

                string traceFileName = s_traceFileName;

                if (s_traceOutput != (int)TraceOutput.Off && !string.IsNullOrEmpty(traceFileName))
                {
                    try
                    {
                        var file = new FileInfo(traceFileName);

                        // limit the file size
                        bool truncated = false;

                        if (file.Exists && file.Length > 10000000)
                        {
                            file.Delete();
                            truncated = true;
                        }

                        using var writer = new StreamWriter(
                            File.Open(
                                file.FullName,
                                FileMode.Append,
                                FileAccess.Write,
                                FileShare.Read));
                        if (truncated)
                        {
                            writer.WriteLine("WARNING - LOG FILE TRUNCATED.");
                        }

                        writer.WriteLine(output);
                        writer.Flush();
                    }
                    catch (Exception e)
                    {
                        Debug.WriteLine("Could not write to trace file. Error={0}", e.Message);
                        Debug.WriteLine("FilePath={1}", traceFileName);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Extensions for the <see cref="IObservabilityContext"/>.
    /// </summary>
    public static class ObservabilityContextExtensions
    {
        /// <summary>
        /// Get logger factory from observability context
        /// </summary>
        public static ILoggerFactory GetLoggerFactory(this IObservabilityContext? context)
        {
            return context?.LoggerFactory ?? s_loggerFactory.Value;
        }

        /// <summary>
        /// Create logger for a type name
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static ILogger<T> CreateLogger<T>(this IObservabilityContext? context)
        {
            return context.GetLoggerFactory().CreateLogger<T>();
        }

        private static readonly Lazy<ILoggerFactory> s_loggerFactory =
            new(() => new NullLoggerFactory());
    }
}
