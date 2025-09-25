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

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging;

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
    public sealed class TraceLoggerProvider : ILoggerProvider
    {
        /// <summary>
        /// Gets the current trace mask settings.
        /// </summary>
        public int TraceMask { get; private set; }
#if DEBUG
            = Utils.TraceMasks.All;
#else
            = Utils.TraceMasks.None;
#endif

        /// <inheritdoc/>
        public ILogger CreateLogger(string categoryName)
        {
            return m_loggers.GetOrAdd(categoryName, name => new TraceLogger(this));
        }

        /// <inheritdoc/>
        public void Dispose()
        {
        }

        /// <summary>
        /// Sets the output for tracing (thread safe).
        /// </summary>
        public void SetTraceOutput(Utils.TraceOutput output)
        {
            lock (m_traceFileLock)
            {
                m_traceOutput = (int)output;
            }
        }

        /// <summary>
        /// Sets the mask for tracing (thread safe).
        /// </summary>
        public void SetTraceMask(int masks)
        {
            TraceMask = masks;
        }

        /// <summary>
        /// Returns Tracing class instance for event attaching.
        /// </summary>
        public Tracing Tracing => Tracing.Instance;

        /// <summary>
        /// Sets the path to the log file to use for tracing.
        /// </summary>
        public void SetTraceLog(string filePath, bool deleteExisting)
        {
            // turn tracing on.
            lock (m_traceFileLock)
            {
                // check if tracing is being turned off.
                if (string.IsNullOrEmpty(filePath))
                {
                    m_traceFileName = string.Empty;
                    return;
                }

                try
                {
                    m_traceFileName = Utils.GetAbsoluteFilePath(
                        filePath,
                        checkCurrentDirectory: true,
                        createAlways: true,
                        writable: true);
                }
                catch (Exception e)
                {
                    m_traceFileName = string.Empty;
                    TraceWriteLine("Could not create log file. Error={0}", e.Message);
                    return;
                }

                if (m_traceOutput == (int)Utils.TraceOutput.Off)
                {
                    m_traceOutput = (int)Utils.TraceOutput.FileOnly;
                }

                try
                {
                    var file = new FileInfo(m_traceFileName);

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
        /// Logger object
        /// </summary>
        internal class TraceLogger : ILogger, IDisposable
        {
            public TraceLogger(TraceLoggerProvider provider)
            {
                m_provider = provider;
            }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            {
                return this;
            }

            public void Dispose()
            {
                // no op.
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return Tracing.IsEnabled();
            }

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                m_provider.Log(
                    state,
                    exception,
                    GetTraceMask(eventId, logLevel),
                    formatter);
            }

            private readonly TraceLoggerProvider m_provider;
        }

        /// <summary>
        /// To determine a mask from the log level.
        /// </summary>
        /// <param name="eventId">The event id.</param>
        /// <param name="logLevel">The log level.</param>
        internal static int GetTraceMask(EventId eventId, LogLevel logLevel)
        {
            int mask = eventId.Id & Utils.TraceMasks.All;
            if (mask == 0)
            {
                switch (logLevel)
                {
                    case LogLevel.Critical:
                    case LogLevel.Warning:
                    case LogLevel.Error:
                        mask = Utils.TraceMasks.Error;
                        break;
                    case LogLevel.Information:
                        mask = Utils.TraceMasks.Information;
                        break;
#if DEBUG
                    case LogLevel.Debug:
#endif
                    case LogLevel.Trace:
                        mask = Utils.TraceMasks.Operation;
                        break;
                }
            }
            return mask;
        }

        /// <summary>
        /// Writes a message to the trace log.
        /// </summary>
        /// <typeparam name="TState"></typeparam>
        internal void Log<TState>(
            TState state,
            Exception? exception,
            int traceMask,
            Func<TState, Exception?, string> formatter)
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
        /// Create an exception/error message for a log.
        /// </summary>
        internal StringBuilder TraceExceptionMessage(
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
                if ((TraceMask & Utils.TraceMasks.StackTrace) != 0)
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
        internal void TraceWriteLine(string message, params object[] args)
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
        internal void TraceWriteLine(string output)
        {
            // write to the log file.
            lock (m_traceFileLock)
            {
                // write to debug trace listeners.
                if (m_traceOutput == (int)Utils.TraceOutput.DebugAndFile)
                {
                    Debug.WriteLine(output);
                }

                string traceFileName = m_traceFileName;

                if (m_traceOutput != (int)Utils.TraceOutput.Off && !string.IsNullOrEmpty(traceFileName))
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

#if DEBUG
        private int m_traceOutput = (int)Utils.TraceOutput.DebugAndFile;
#else
        private int m_traceOutput = (int)Utils.TraceOutput.FileOnly;
#endif
        private string m_traceFileName = string.Empty;
        private readonly Lock m_traceFileLock = new();
        private readonly ConcurrentDictionary<string, TraceLogger> m_loggers =
              new(StringComparer.OrdinalIgnoreCase);
    }
}
