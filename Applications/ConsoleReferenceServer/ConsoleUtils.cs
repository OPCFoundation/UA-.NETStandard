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
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Mono.Options;
using Opc.Ua;
using Serilog;
using Serilog.Events;
using Serilog.Templates;
#if NET5_0_OR_GREATER
using Microsoft.Extensions.Configuration;
#endif

namespace Quickstarts
{
    /// <summary>
    /// Simple console based telemetry
    /// </summary>
    public sealed class ConsoleTelemetry : ITelemetryContext, IDisposable
    {
        public ConsoleTelemetry(Action<ILoggingBuilder> configure = null)
        {
            LoggerFactory = Microsoft.Extensions.Logging.LoggerFactory
                .Create(builder =>
                {
                    builder.SetMinimumLevel(LogLevel.Information);
                    configure?.Invoke(builder);
                })
                .AddSerilog(Log.Logger);

            ActivitySource = new ActivitySource("Quickstarts", "1.0.0");

            m_logger = LoggerFactory.CreateLogger("Main");

            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += Unobserved_TaskException;
        }

        /// <inheritdoc/>
        public ILoggerFactory LoggerFactory { get; internal set; }

        /// <inheritdoc/>
        public Meter CreateMeter()
        {
            return new Meter("Quickstarts", "1.0.0");
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
        /// <param name="configuration">The application configuration.</param>
        /// <param name="context">The context name for the logger. </param>
        /// <param name="logConsole">Enable logging to the console.</param>
        /// <param name="consoleLogLevel">The LogLevel to use for the console/debug.
        /// </param>
        public void ConfigureLogging(
            ApplicationConfiguration configuration,
            string context,
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
            LogLevel fileLevel = LogLevel.Information;

            // switch for Trace/Verbose output
            int traceMasks = configuration.TraceConfiguration.TraceMasks;
            if ((traceMasks &
                ~(
                    Utils.TraceMasks.Information |
                    Utils.TraceMasks.Error |
                    Utils.TraceMasks.Security |
                    Utils.TraceMasks.StartStop |
                    Utils.TraceMasks.StackTrace
                )) != 0)
            {
                fileLevel = LogLevel.Trace;
            }

            // add file logging if configured
            string outputFilePath = configuration.TraceConfiguration.OutputFilePath;
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
                args.ExceptionObject as Exception,
                "Unhandled Exception: (IsTerminating: {IsTerminating})",
                args.IsTerminating);
        }

        private void Unobserved_TaskException(
            object sender,
            UnobservedTaskExceptionEventArgs args)
        {
            m_logger.LogCritical(
                args.Exception,
                "Unobserved Task Exception (Observed: {Observed})",
                args.Observed);
        }

        private Microsoft.Extensions.Logging.ILogger m_logger;
    }

    /// <summary>
    /// The error code why the application exit.
    /// </summary>
    public enum ExitCode
    {
        Ok = 0,
        ErrorNotStarted = 0x80,
        ErrorRunning = 0x81,
        ErrorException = 0x82,
        ErrorStopping = 0x83,
        ErrorCertificate = 0x84,
        ErrorInvalidCommandLine = 0x100
    }

    /// <summary>
    /// An exception that occured and caused an exit of the application.
    /// </summary>
    [Serializable]
    public class ErrorExitException : Exception
    {
        public ExitCode ExitCode { get; }

        public ErrorExitException(ExitCode exitCode)
        {
            ExitCode = exitCode;
        }

        public ErrorExitException()
        {
            ExitCode = ExitCode.Ok;
        }

        public ErrorExitException(string message)
            : base(message)
        {
            ExitCode = ExitCode.Ok;
        }

        public ErrorExitException(string message, ExitCode exitCode)
            : base(message)
        {
            ExitCode = exitCode;
        }

        public ErrorExitException(string message, Exception innerException)
            : base(message, innerException)
        {
            ExitCode = ExitCode.Ok;
        }

        public ErrorExitException(string message, Exception innerException, ExitCode exitCode)
            : base(message, innerException)
        {
            ExitCode = exitCode;
        }
    }

    /// <summary>
    /// Helper functions shared in various console applications.
    /// </summary>
    public static class ConsoleUtils
    {
        /// <summary>
        /// Process a command line of the console sample application.
        /// </summary>
        /// <exception cref="ErrorExitException"></exception>
        public static string ProcessCommandLine(
            string[] args,
            Mono.Options.OptionSet options,
            ref bool showHelp,
            string environmentPrefix,
            bool noExtraArgs = true,
            TextWriter output = null)
        {
            output ??= Console.Out;

#if NET5_0_OR_GREATER
            // Convert environment settings to command line flags
            // because in some environments (e.g. docker cloud) it is
            // the only supported way to pass arguments.
            IConfigurationRoot config = new ConfigurationBuilder()
                .AddEnvironmentVariables(environmentPrefix + "_")
                .Build();

            List<string> argslist = [.. args];
            foreach (Option option in options)
            {
                string[] names = option.GetNames();
                string longest = names.MaxBy(s => s.Length);
                if (longest != null && longest.Length >= 3)
                {
                    string envKey = config[longest.ToUpperInvariant()];
                    if (envKey != null)
                    {
                        if (string.IsNullOrWhiteSpace(envKey) ||
                            option.OptionValueType == OptionValueType.None)
                        {
                            argslist.Add("--" + longest);
                        }
                        else
                        {
                            argslist.Add("--" + longest + "=" + envKey);
                        }
                    }
                }
            }
            args = [.. argslist];
#endif

            IList<string> extraArgs = null;
            try
            {
                extraArgs = options.Parse(args);
                if (noExtraArgs)
                {
                    foreach (string extraArg in extraArgs)
                    {
                        output.WriteLine("Error: Unknown option: {0}", extraArg);
                        showHelp = true;
                    }
                }
            }
            catch (OptionException e)
            {
                output.WriteLine(e.Message);
                showHelp = true;
            }

            if (showHelp)
            {
                options.WriteOptionDescriptions(output);
                throw new ErrorExitException(
                    "Invalid Commandline or help requested.",
                    ExitCode.ErrorInvalidCommandLine
                );
            }

            return extraArgs.FirstOrDefault();
        }

        /// <summary>
        /// Create an event which is set if a user
        /// enters the Ctrl-C key combination.
        /// </summary>
        public static ManualResetEvent CtrlCHandler(CancellationTokenSource cts)
        {
            var quitEvent = new ManualResetEvent(false);
            try
            {
                Console.CancelKeyPress += (_, eArgs) =>
                {
                    cts.Cancel();
                    quitEvent.Set();
                    eArgs.Cancel = true;
                };
            }
            catch
            {
                // intentionally left blank
            }
            return quitEvent;
        }
    }
}
