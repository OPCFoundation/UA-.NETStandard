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

using System;
using Opc.Ua;
using Serilog;
using Serilog.Events;

namespace Quickstarts.ReferenceServer
{
    /// <summary>
    /// A sample serilog trace logger replacement.
    /// </summary>
    public class SerilogTraceLogger
    {
        private int m_traceMask;
        private ILogger m_logger;

        /// <summary>
        /// Create a serilog trace logger which replaces the default logging.
        /// </summary>
        /// <param name="config">The application configuration.</param>
        /// <param name="fileMinimumLevel">The min log level for file output.</param>
        public static SerilogTraceLogger Create(
            ApplicationConfiguration config,
            LogEventLevel fileMinimumLevel = LogEventLevel.Information)
        {
            return Create(null, config, fileMinimumLevel);
        }

        /// <summary>
        /// Create a serilog trace logger which replaces the default logging.
        /// </summary>
        /// <param name="loggerConfiguration">The logger configuration.</param>
        /// <param name="config">The application configuration.</param>
        /// <param name="fileMinimumLevel">The min log level for file output.</param>
        public static SerilogTraceLogger Create(
            LoggerConfiguration loggerConfiguration,
            ApplicationConfiguration config,
            LogEventLevel fileMinimumLevel = LogEventLevel.Verbose)
        {
            if (loggerConfiguration == null)
            {
                loggerConfiguration = new LoggerConfiguration();
            }
            // add file logging
            if (!string.IsNullOrWhiteSpace(config.TraceConfiguration.OutputFilePath))
            {
                loggerConfiguration.WriteTo.File(
                    Utils.ReplaceSpecialFolderNames(config.TraceConfiguration.OutputFilePath),
                    rollingInterval: RollingInterval.Infinite,
                    rollOnFileSizeLimit: true,
                    restrictedToMinimumLevel: fileMinimumLevel,
                    retainedFileCountLimit: 10,
                    flushToDiskInterval: TimeSpan.FromSeconds(13));
            }

            ILogger logger = loggerConfiguration
                .MinimumLevel.Verbose()
                .CreateLogger();

            SerilogTraceLogger traceLogger = new SerilogTraceLogger(logger, config.TraceConfiguration.TraceMasks);

            // disable the built in tracing, use serilog
            Utils.SetTraceMask(Utils.TraceMask & Utils.TraceMasks.StackTrace);
            Utils.SetTraceOutput(Utils.TraceOutput.Off);
            Utils.Tracing.TraceEventHandler += traceLogger.TraceEventHandler;

            return traceLogger;
        }

        /// <summary>
        /// Ctor of trace logger.
        /// </summary>
        /// <param name="logger">The logger</param>
        /// <param name="traceMask">The trace mask</param>
        public SerilogTraceLogger(ILogger logger, int traceMask)
        {
            m_logger = logger;
            m_traceMask = traceMask;
        }

        /// <summary>
        /// Callback for logging OPC UA stack trace output
        /// </summary>
        /// <param name="sender">Sender object</param>
        /// <param name="e">The trace event args.</param>
        public void TraceEventHandler(object sender, TraceEventArgs e)
        {
            if ((e.TraceMask & m_traceMask) != 0)
            {
                if (e.Exception != null)
                {
                    m_logger.Error(e.Exception, e.Format, e.Arguments);
                    return;
                }
                switch (e.TraceMask)
                {
                    case Utils.TraceMasks.StartStop:
                    case Utils.TraceMasks.Information: m_logger.Information(e.Format, e.Arguments); break;
                    case Utils.TraceMasks.Error: m_logger.Error(e.Format, e.Arguments); break;
                    case Utils.TraceMasks.StackTrace: 
                    case Utils.TraceMasks.Security: m_logger.Warning(e.Format, e.Arguments); break;
                    default: m_logger.Verbose(e.Format, e.Arguments); break;
                }
            }
        }
    }
}
