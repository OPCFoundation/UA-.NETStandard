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
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Serilog;
using Serilog.Events;
using Serilog.Templates;
using static Opc.Ua.Utils;

public static class Logging
{
    /// <summary>
    /// Configure the serilog logging provider.
    /// </summary>
    public static void Configure(
        string context,
        string outputFilePath,
        bool logConsole,
        LogLevel consoleLogLevel)
    {
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += Unobserved_TaskException;

        var loggerConfiguration = new LoggerConfiguration()
                .Enrich.FromLogContext();

        if (logConsole)
        {
            loggerConfiguration.WriteTo.Console(
                restrictedToMinimumLevel: (LogEventLevel)consoleLogLevel,
                formatProvider: CultureInfo.InvariantCulture
                );
        }
#if DEBUG
        else
        {
            loggerConfiguration
                .WriteTo.Debug(
                    restrictedToMinimumLevel: (LogEventLevel)consoleLogLevel,
                    formatProvider: CultureInfo.InvariantCulture
                    );
        }
#endif
        LogLevel fileLevel = LogLevel.Information;

        // add file logging if configured
        if (!string.IsNullOrWhiteSpace(outputFilePath))
        {
            loggerConfiguration.WriteTo.File(
                new ExpressionTemplate("{UtcDateTime(@t):yyyy-MM-dd HH:mm:ss.fff} [{@l:u3}] {@m}\n{@x}"),
                ReplaceSpecialFolderNames(outputFilePath),
                restrictedToMinimumLevel: (LogEventLevel)fileLevel,
                rollOnFileSizeLimit: true);
        }

        // adjust minimum level
        if (fileLevel < LogLevel.Information || consoleLogLevel < LogLevel.Information)
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
        SetLogger(logger);
    }

    private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs args)
    {
        Utils.LogCritical("Unhandled Exception: {0} IsTerminating: {1}", args.ExceptionObject, args.IsTerminating);
    }

    private static void Unobserved_TaskException(object sender, UnobservedTaskExceptionEventArgs args)
    {
        Utils.LogCritical("Unobserved Exception: {0} Observed: {1}", args.Exception, args.Observed);
    }
}
