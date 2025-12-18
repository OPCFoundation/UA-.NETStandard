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
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;

namespace Quickstarts.ReferenceServer
{
    /// <summary>
    /// The program.
    /// </summary>
    public static class Program
    {
        public static async Task<int> Main(string[] args)
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
            bool showHelp = false;
            bool autoAccept = true;
            bool logConsole = true;
            bool appLog = true;
            bool fileLog = false;
            bool renewCertificate = false;
            bool shadowConfig = false;
            bool samplingGroups = false;
            bool cttMode = false;
            bool provisioningMode = false;
            char[] password = null;
            int timeout = -1;
            string reverseConnectUrlString = null;

            string usage = Utils.IsRunningOnMono()
                ? $"Usage: mono {applicationName}.exe [OPTIONS]"
                : $"Usage: dotnet {applicationName}.dll [OPTIONS]";
            var options = new Mono.Options.OptionSet
            {
                usage,
                { "h|help", "show this message and exit", h => showHelp = h != null },
                { "a|autoaccept", "auto accept certificates (for testing only)", a => autoAccept = a != null },
                { "c|console", "log to console", c => logConsole = c != null },
                { "l|log", "log app output", c => appLog = c != null },
                { "f|file", "log to file", f => fileLog = f != null },
                { "p|password=", "optional password for private key", p => password = p.ToCharArray() },
                { "r|renew", "renew application certificate", r => renewCertificate = r != null },
                { "t|timeout=", "timeout in seconds to exit application", (int t) => timeout = t * 1000 },
                { "s|shadowconfig", "create configuration in pki root", s => shadowConfig = s != null },
                {
                    "sg|samplinggroups",
                    "use the sampling group mechanism in the Reference Node Manager",
                    sg => samplingGroups = sg != null
                },
                { "ctt", "CTT mode, use to preset alarms for CTT testing.", c => cttMode = c != null },
                {
                    "provision",
                    "start server in provisioning mode with limited namespace for certificate provisioning",
                    p => provisioningMode = p != null
                },
                {
                    "rc|reverseconnect=",
                    "Connect to the specified client endpoint for reverse connect. (e.g. rc=opc.tcp://localhost:65300)",
                    url => reverseConnectUrlString = url
                }
            };

            using var telemetry = new ConsoleTelemetry();
            ILogger logger = LoggerUtils.Null.Logger;
            try
            {
                // parse command line and set options
                ConsoleUtils.ProcessCommandLine(args, options, ref showHelp, "REFSERVER");

                // log console output to logger
                if (logConsole && appLog)
                {
                    logger = telemetry.CreateLogger("Main");
                }

                // create the UA server
                var server = new UAServer<ReferenceServer>(telemetry)
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
                    LogLevel.Warning);

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
                await server.StartAsync().ConfigureAwait(false);

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

                Console.WriteLine("Server started. Press Ctrl-C to exit...");

                // wait for timeout or Ctrl-C
                var quitCTS = new CancellationTokenSource();
                ManualResetEvent quitEvent = ConsoleUtils.CtrlCHandler(quitCTS);
                bool ctrlc = quitEvent.WaitOne(timeout);

                // stop server. May have to wait for clients to disconnect.
                Console.WriteLine("Server stopped. Waiting for exit...");
                await server.StopAsync().ConfigureAwait(false);

                return (int)ExitCode.Ok;
            }
            catch (ErrorExitException eee)
            {
                Console.WriteLine($"The application exits with error: {eee.Message}");
                return (int)eee.ExitCode;
            }
        }
    }
}
