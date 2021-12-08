/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Mono.Options;
using Opc.Ua;
using Opc.Ua.Configuration;
using Serilog;
using Serilog.Events;

namespace Quickstarts
{
    /// <summary>
    /// The error code why the application exited.
    /// </summary>
    public enum ExitCode : int
    {
        Ok = 0,
        ErrorNotStarted = 0x80,
        ErrorRunning = 0x81,
        ErrorException = 0x82,
        ErrorStopping = 0x83,
        ErrorInvalidCommandLine = 0x100
    };

    public class ErrorExitException : Exception
    {
        public ExitCode ExitCode { get; }

        public ErrorExitException(ExitCode exitCode) : base()
        {
            ExitCode = exitCode;
        }

        public ErrorExitException() : base()
        {
            ExitCode = ExitCode.Ok;
        }

        public ErrorExitException(string message) : base(message)
        {
            ExitCode = ExitCode.Ok;
        }

        public ErrorExitException(string message, ExitCode exitCode) : base(message)
        {
            ExitCode = exitCode;
        }

        public ErrorExitException(string message, Exception innerException) : base(message, innerException)
        {
            ExitCode = ExitCode.Ok;
        }

        public ErrorExitException(string message, Exception innerException, ExitCode exitCode) : base(message, innerException)
        {
            ExitCode = exitCode;
        }
    }

    /// <summary>
    /// A dialog which asks for user input.
    /// </summary>
    public class ApplicationMessageDlg : IApplicationMessageDlg
    {
        private string m_message = string.Empty;
        private bool m_ask = false;

        public override void Message(string text, bool ask)
        {
            m_message = text;
            m_ask = ask;
        }

        public override async Task<bool> ShowAsync()
        {
            if (m_ask)
            {
                m_message += " (y/n, default y): ";
                Console.Write(m_message);
            }
            else
            {
                Console.WriteLine(m_message);
            }
            if (m_ask)
            {
                try
                {
                    ConsoleKeyInfo result = Console.ReadKey();
                    Console.WriteLine();
                    return await Task.FromResult((result.KeyChar == 'y') || (result.KeyChar == 'Y') || (result.KeyChar == '\r')).ConfigureAwait(false);
                }
                catch
                {
                    // intentionally fall through
                }
            }
            return await Task.FromResult(true).ConfigureAwait(false);
        }
    }

    public static class ConsoleUtils
    {
        public static string ProcessCommandLine(string[] args, Mono.Options.OptionSet options, ref bool showHelp, string usage, bool noExtraArgs = true)
        {
            IList<string> extraArgs = null;
            try
            {
                extraArgs = options.Parse(args);
                if (noExtraArgs)
                {
                    foreach (string extraArg in extraArgs)
                    {
                        Console.WriteLine("Error: Unknown option: {0}", extraArg);
                        showHelp = true;
                    }
                }
            }
            catch (OptionException e)
            {
                Console.WriteLine(e.Message);
                showHelp = true;
            }

            if (showHelp)
            {
                Console.WriteLine(usage);
                Console.WriteLine();
                Console.WriteLine("Options:");
                options.WriteOptionDescriptions(Console.Out);
                throw new ErrorExitException("Invalid Commandline or help requested.", ExitCode.ErrorInvalidCommandLine);
            }
            return extraArgs?.FirstOrDefault();
        }

        public static void ConfigureLogging(ApplicationConfiguration configuration, string context, bool logConsole, LogLevel logLevel)
        {
            var loggerConfiguration = new LoggerConfiguration()
                .Enrich.FromLogContext();

            if (logConsole)
            {
                loggerConfiguration.WriteTo.Console(
                    restrictedToMinimumLevel: (LogEventLevel)logLevel
                    );
            }
#if DEBUG
            else
            {
                loggerConfiguration
                    .WriteTo.Debug(restrictedToMinimumLevel: (LogEventLevel)logLevel);
            }
#endif
            LogLevel fileLevel = LogLevel.Information;

            // test for Trace/Verbose output
            var traceMasks = configuration.TraceConfiguration.TraceMasks;
            if ((traceMasks & ~(Utils.TraceMasks.Information | Utils.TraceMasks.Error |
                Utils.TraceMasks.Security | Utils.TraceMasks.StartStop | Utils.TraceMasks.StackTrace)) != 0)
            {
                fileLevel = LogLevel.Trace;
            }

            // add file logging if configured
            var outputFilePath = configuration.TraceConfiguration.OutputFilePath;
            if (!string.IsNullOrWhiteSpace(outputFilePath))
            {
                loggerConfiguration.WriteTo.File(
                    Utils.ReplaceSpecialFolderNames(outputFilePath),
                    restrictedToMinimumLevel: (LogEventLevel)fileLevel,
                    rollOnFileSizeLimit: true);
            }

            // adjust minimum level
            if (fileLevel < LogLevel.Information || logLevel < LogLevel.Information)
            {
                loggerConfiguration.MinimumLevel.Verbose();
            }

            // create the serilog logger
            var serilogger = loggerConfiguration
                .CreateLogger();

            // create the ILogger for Opc.Ua.Core
            var logger = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Trace))
                .AddSerilog(serilogger)
                .CreateLogger(context);

            // set logger interface, disables TraceEvent
            Utils.SetLogger(logger);
        }

        public static void LogTest()
        {
            // print legacy logging output
            Utils.Trace(Utils.TraceMasks.Error, "This is an Error message: {0}", Utils.TraceMasks.Error);
            Utils.Trace(Utils.TraceMasks.Information, "This is a Information message: {0}", Utils.TraceMasks.Information);
            Utils.Trace(Utils.TraceMasks.StackTrace, "This is a StackTrace message: {0}", Utils.TraceMasks.StackTrace);
            Utils.Trace(Utils.TraceMasks.Service, "This is a Service message: {0}", Utils.TraceMasks.Service);
            Utils.Trace(Utils.TraceMasks.ServiceDetail, "This is a ServiceDetail message: {0}", Utils.TraceMasks.ServiceDetail);
            Utils.Trace(Utils.TraceMasks.Operation, "This is a Operation message: {0}", Utils.TraceMasks.Operation);
            Utils.Trace(Utils.TraceMasks.OperationDetail, "This is a OperationDetail message: {0}", Utils.TraceMasks.OperationDetail);
            Utils.Trace(Utils.TraceMasks.StartStop, "This is a StartStop message: {0}", Utils.TraceMasks.StartStop);
            Utils.Trace(Utils.TraceMasks.ExternalSystem, "This is a ExternalSystem message: {0}", Utils.TraceMasks.ExternalSystem);
            Utils.Trace(Utils.TraceMasks.Security, "This is a Security message: {0}", Utils.TraceMasks.Security);

            // print ILogger logging output
            Utils.LogTrace("This is a Trace message: {0}", LogLevel.Trace);
            Utils.LogDebug("This is a Debug message: {0}", LogLevel.Debug);
            Utils.LogInfo("This is a Info message: {0}", LogLevel.Information);
            Utils.LogWarning("This is a Warning message: {0}", LogLevel.Warning);
            Utils.LogError("This is a Error message: {0}", LogLevel.Error);
            Utils.LogCritical("This is a Critical message: {0}", LogLevel.Critical);
        }

        public static ManualResetEvent CtrlCHandler()
        {
            var quitEvent = new ManualResetEvent(false);
            try
            {
                Console.CancelKeyPress += (sender, eArgs) => {
                    quitEvent.Set();
                    eArgs.Cancel = true;
                };
            }
            catch
            {
            }
            return quitEvent;
        }
    }
}

