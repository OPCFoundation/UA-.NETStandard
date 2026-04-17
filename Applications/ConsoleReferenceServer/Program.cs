/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using System.CommandLine;

namespace Quickstarts.ReferenceServer
{
    /// <summary>
    /// The program.
    /// </summary>
    public static class Program
    {
        public static Task<int> Main(string[] args)
        {
            Console.WriteLine("{0} OPC UA Reference Server", Utils.IsRunningOnMono() ? "Mono" : ".NET Core");

            Console.WriteLine(
                "OPC UA library: {0} @ {1} -- {2}",
                Utils.GetAssemblyBuildNumber(),
                Utils.GetAssemblyTimestamp().ToString("G", CultureInfo.InvariantCulture),
                Utils.GetAssemblySoftwareVersion()
            );

            // The application name and config file names
            string applicationName = Utils.IsRunningOnMono() ? "MonoReferenceServer" : "ConsoleReferenceServer";
            string configSectionName = Utils.IsRunningOnMono()
                ? "Quickstarts.MonoReferenceServer"
                : "Quickstarts.ReferenceServer";

            // command line options
            var autoAcceptOption = new Option<bool>("--autoaccept", "-a") { Description = "auto accept certificates (for testing only)" };
            var consoleOption = new Option<bool>("--console", "-c") { Description = "log to console" };
            var logOption = new Option<bool>("--log", "-l") { Description = "log app output" };
            var fileOption = new Option<bool>("--file", "-f") { Description = "log to file" };
            var passwordOption = new Option<string>("--password", "-p") { Description = "optional password for private key" };
            var renewOption = new Option<bool>("--renew", "-r") { Description = "renew application certificate" };
            var timeoutOption = new Option<int>("--timeout", "-t")
            {
                Description = "timeout in seconds to exit application",
                DefaultValueFactory = _ => -1
            };
            var shadowConfigOption = new Option<bool>("--shadowconfig", "-s") { Description = "create configuration in pki root" };
            var samplingGroupsOption = new Option<bool>("--samplinggroups", "--sg")
            {
                Description = "use the sampling group mechanism in the Reference Node Manager"
            };
            var cttOption = new Option<bool>("--ctt") { Description = "CTT mode, use to preset alarms for CTT testing." };
            var provisionOption = new Option<bool>("--provision")
            {
                Description = "start server in provisioning mode with limited namespace for certificate provisioning"
            };
            var reverseConnectOption = new Option<string>("--reverseconnect", "--rc")
            {
                Description = "Connect to the specified client endpoint for reverse connect. (e.g. --rc opc.tcp://localhost:65300)"
            };

            var rootCommand = new RootCommand(
                Utils.IsRunningOnMono()
                    ? $"Usage: mono {applicationName}.exe [OPTIONS]"
                    : $"Usage: dotnet {applicationName}.dll [OPTIONS]")
            {
                autoAcceptOption,
                consoleOption,
                logOption,
                fileOption,
                passwordOption,
                renewOption,
                timeoutOption,
                shadowConfigOption,
                samplingGroupsOption,
                cttOption,
                provisionOption,
                reverseConnectOption
            };

            rootCommand.SetAction(async (parseResult, cancellationToken) =>
            {
                bool autoAccept = parseResult.GetValue(autoAcceptOption);
                bool logConsole = parseResult.GetValue(consoleOption);
                bool appLog = parseResult.GetValue(logOption);
                bool fileLog = parseResult.GetValue(fileOption);
                string passwordStr = parseResult.GetValue(passwordOption);
                char[] password = passwordStr?.ToCharArray();
                bool renewCertificate = parseResult.GetValue(renewOption);
                int timeoutSec = parseResult.GetValue(timeoutOption);
                int timeout = timeoutSec >= 0 ? timeoutSec * 1000 : -1;
                bool shadowConfig = parseResult.GetValue(shadowConfigOption);
                bool samplingGroups = parseResult.GetValue(samplingGroupsOption);
                bool cttMode = parseResult.GetValue(cttOption);
                bool provisioningMode = parseResult.GetValue(provisionOption);
                string reverseConnectUrlString = parseResult.GetValue(reverseConnectOption);

                using var telemetry = new ConsoleTelemetry();
                ILogger logger = LoggerUtils.Null.Logger;
                try
                {
                    // log console output to logger
                    if (logConsole && appLog)
                    {
                        logger = telemetry.CreateLogger("Main");
                    }
                    var sw = Stopwatch.StartNew();

                    // create the UA server
                    var server = new UAServer<ReferenceServer>(telemetry, t => new ReferenceServer(t))
                    {
                        AutoAccept = autoAccept,
                        Password = password
                    };

                    // load the server configuration, validate certificates
                    Console.WriteLine($"Loading configuration from {configSectionName}.");
                    await server.LoadAsync(applicationName, configSectionName).ConfigureAwait(false);

                    // use the shadow config to map the config to an externally accessible location
                    if (shadowConfig)
                    {
                        Console.WriteLine("Using shadow configuration.");
                        string shadowPath = Directory
                            .GetParent(
                                Path.GetDirectoryName(
                                    Utils.ReplaceSpecialFolderNames(server.Configuration.TraceConfiguration.OutputFilePath)
                                )
                            )
                            .FullName;
                        string shadowFilePath = Path.Combine(
                            shadowPath,
                            Path.GetFileName(server.Configuration.SourceFilePath)
                        );
                        if (!File.Exists(shadowFilePath))
                        {
                            Console.WriteLine("Create a copy of the config in the shadow location.");
                            File.Copy(server.Configuration.SourceFilePath, shadowFilePath, true);
                        }
                        Console.WriteLine($"Reloading configuration from shadow location {shadowFilePath}.");
                        await server
                            .LoadAsync(applicationName, Path.Combine(shadowPath, configSectionName))
                            .ConfigureAwait(false);
                    }

                    // setup the logging
                    telemetry.ConfigureLogging(
                        server.Configuration,
                        applicationName,
                        logConsole,
                        fileLog,
                        appLog,
                        LogLevel.Information);

                    // check or renew the certificate
                    Console.WriteLine("Check the certificate.");
                    await server.CheckCertificateAsync(renewCertificate).ConfigureAwait(false);

                    // Create and add the node managers
                    server.Create(Servers.Utils.NodeManagerFactories);

                    // enable provisioning mode if requested
                    if (provisioningMode)
                    {
                        Console.WriteLine("Enabling provisioning mode.");
                        Servers.Utils.EnableProvisioningMode(server.Server);
                        // Auto-accept is required in provisioning mode
                        if (!autoAccept)
                        {
                            Console.WriteLine("Auto-accept enabled for provisioning mode.");
                            autoAccept = true;
                            server.AutoAccept = autoAccept;
                        }
                    }

                    // enable the sampling groups if requested
                    if (samplingGroups)
                    {
                        Servers.Utils.UseSamplingGroupsInReferenceNodeManager(server.Server);
                    }

                    // start the server
                    Console.WriteLine("Start the server.");
                    await server.StartAsync(cancellationToken).ConfigureAwait(false);

                    // setup reverse connect if specified
                    if (!string.IsNullOrEmpty(reverseConnectUrlString))
                    {
                        try
                        {
                            Console.WriteLine($"Adding reverse connection to {reverseConnectUrlString}.");
                            var reverseConnectUrl = new Uri(reverseConnectUrlString);
                            server.Server.AddReverseConnection(reverseConnectUrl);
                        }
                        catch (UriFormatException ex)
                        {
                            logger.LogError(ex, "Invalid reverse connect URL: {Url}", reverseConnectUrlString);
                            throw new ErrorExitException(
                                $"Invalid reverse connect URL: {reverseConnectUrlString}",
                                ExitCode.ErrorInvalidCommandLine);
                        }
                    }

                    // Apply custom settings for CTT testing
                    if (cttMode)
                    {
                        Console.WriteLine("Apply settings for CTT.");
                        // start Alarms and other settings for CTT test
                        await Servers.Utils.ApplyCTTModeAsync(Console.Out, server.Server)
                            .ConfigureAwait(false);
                    }

                    Console.WriteLine($"Server started ({sw.ElapsedMilliseconds} ms). Press Ctrl-C to exit...");

                    // wait for timeout or Ctrl-C (cancellationToken is cancelled on Ctrl-C by System.CommandLine)
                    if (timeout >= 0)
                    {
                        using CancellationTokenSource timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        timeoutCts.CancelAfter(timeout);
                        try
                        {
                            await Task.Delay(Timeout.Infinite, timeoutCts.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            // expected — timeout or Ctrl-C
                        }
                    }
                    else
                    {
                        try
                        {
                            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            // expected — Ctrl-C
                        }
                    }

                    // stop server. May have to wait for clients to disconnect.
                    Console.WriteLine("Server stopped. Waiting for exit...");
                    await server.StopAsync(default).ConfigureAwait(false);

                    return (int)ExitCode.Ok;
                }
                catch (ErrorExitException eee)
                {
                    Console.WriteLine($"The application exits with error: {eee.Message}");
                    return (int)eee.ExitCode;
                }
            });

            args = ConsoleUtils.MergeEnvironmentArgs(args, "REFSERVER", rootCommand);
            ParseResult parseResult = rootCommand.Parse(args);
            return parseResult
                .InvokeAsync(new InvocationConfiguration(), CancellationToken.None);
        }
    }
}
