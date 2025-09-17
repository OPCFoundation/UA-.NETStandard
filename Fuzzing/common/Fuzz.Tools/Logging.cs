/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Templates;

namespace Opc.Ua.Fuzzing
{
    /// <summary>
    /// Simple console based telemetry
    /// </summary>
    public sealed class Logging : ITelemetryContext, IDisposable
    {
        public Logging(Action<ILoggingBuilder> configure = null)
        {
            LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory
                .Create(builder =>
                {
                    builder.SetMinimumLevel(LogLevel.Information);
                    configure?.Invoke(builder);
                })
                .AddSerilog(Log.Logger);

            ActivitySource = new ActivitySource("Fuzzing", "1.0.0");

            m_logger = LoggerFactory.CreateLogger("Main");

            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += Unobserved_TaskException;
        }

        /// <inheritdoc/>
        public ILoggerFactory LoggerFactory { get; internal set; }

        /// <inheritdoc/>
        public Meter CreateMeter()
        {
            return new Meter("Fuzzing", "1.0.0");
        }

        /// <inheritdoc/>
        public ActivitySource ActivitySource { get; }

        /// <inheritdoc/>
        public void Dispose()
        {
            CreateMeter().Dispose();
            ActivitySource.Dispose();
            LoggerFactory.Dispose();

            AppDomain.CurrentDomain.UnhandledException -= CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException -= Unobserved_TaskException;
        }

        /// <summary>
        /// Configure the logging providers.
        /// </summary>
        /// <remarks>
        /// Replaces the Opc.Ua.Core default ILogger with a
        /// Microsoft.Extension.Logger with a Serilog file, debug and console logger.
        /// The debug logger is only enabled for debug builds.
        /// The console logger is enabled by the logConsole flag at the consoleLogLevel.
        /// The file logger uses the setting in the ApplicationConfiguration.
        /// The Trace logLevel is chosen if required by the Tracemasks.
        /// </remarks>
        /// <param name="context">The context name for the logger. </param>
        /// <param name="outputFilePath">The output file.</param>
        /// <param name="logConsole">Enable logging to the console.</param>
        /// <param name="consoleLogLevel">The LogLevel to use for the console/debug.<
        /// /param>
        public void Configure(
            string context,
            string outputFilePath,
            bool logConsole,
            LogLevel consoleLogLevel)
        {
            LoggerConfiguration loggerConfiguration = new LoggerConfiguration().Enrich
                .FromLogContext();

            if (logConsole)
            {
                loggerConfiguration.WriteTo.Console(
                    restrictedToMinimumLevel: (LogEventLevel)consoleLogLevel,
                    formatProvider: CultureInfo.InvariantCulture);
            }
#if DEBUG
            else
            {
                loggerConfiguration.WriteTo.Debug(
                    restrictedToMinimumLevel: (LogEventLevel)consoleLogLevel,
                    formatProvider: CultureInfo.InvariantCulture);
            }
#endif
            const LogLevel fileLevel = LogLevel.Information;

            // add file logging if configured
            if (!string.IsNullOrWhiteSpace(outputFilePath))
            {
                loggerConfiguration.WriteTo.File(
                    new ExpressionTemplate(
                        "{UtcDateTime(@t):yyyy-MM-dd HH:mm:ss.fff} [{@l:u3}] {@m}\n{@x}"),
                    Utils.ReplaceSpecialFolderNames(outputFilePath),
                    restrictedToMinimumLevel: (LogEventLevel)fileLevel,
                    rollOnFileSizeLimit: true
                );
            }

            // adjust minimum level
            if (fileLevel < LogLevel.Information || consoleLogLevel < LogLevel.Information)
            {
                loggerConfiguration.MinimumLevel.Verbose();
            }

            // create the serilog logger
            Serilog.Core.Logger serilogger = loggerConfiguration.CreateLogger();

            // create the ILogger for Opc.Ua.Core
            LoggerFactory = LoggerFactory.AddSerilog(serilogger);
            m_logger = LoggerFactory.CreateLogger("Main");
        }

        private void CurrentDomain_UnhandledException(
            object sender,
            UnhandledExceptionEventArgs args)
        {
            m_logger.LogCritical(
                "Unhandled Exception: {ExceptionObject} IsTerminating: {IsTerminating}",
                args.ExceptionObject,
                args.IsTerminating);
        }

        private void Unobserved_TaskException(
            object sender,
            UnobservedTaskExceptionEventArgs args)
        {
            m_logger.LogCritical(args.Exception,
                "Unobserved Exception: Observed: {Observed}",
                args.Observed);
        }

        private Microsoft.Extensions.Logging.ILogger m_logger;
    }
}
