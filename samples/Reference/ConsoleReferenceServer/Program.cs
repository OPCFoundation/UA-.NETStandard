/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Configuration;
using Opc.Ua.Gds.Server;
using Opc.Ua.Server;

namespace Quickstarts.ReferenceServer
{
    /// <summary>
    /// Hosts the OPC UA reference server with environment defaults and explicit command-line security opt-ins.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Prints application information, merges REFSERVER defaults with explicit arguments,
        /// and invokes the reference server command.
        /// </summary>
        /// <param name="args">Explicit command-line arguments, which take precedence over environment defaults.</param>
        /// <returns>The command invocation exit code.</returns>
        public static Task<int> Main(string[] args)
        {
            Console.WriteLine("{0} OPC UA Reference Server", Utils.IsRunningOnMono() ? "Mono" : ".NET Core");

            Console.WriteLine(
                "OPC UA library: {0} @ {1} -- {2}",
                Utils.GetAssemblyBuildNumber(),
                Utils.GetAssemblyTimestamp().ToString("G", CultureInfo.InvariantCulture),
                Utils.GetAssemblySoftwareVersion()
            );

            ParseResult parseResult = ParseArguments(args);
            return parseResult
                .InvokeAsync(new InvocationConfiguration(), CancellationToken.None);
        }

        /// <summary>
        /// Parses environment defaults and explicit command-line arguments without starting the host.
        /// Explicit command-line values take precedence over environment defaults.
        /// </summary>
        public static ParseResult ParseArguments(ArrayOf<string> arguments)
        {
            RootCommand rootCommand = CreateCommand();
            string[] args = [.. arguments];
            ParseResult explicitArguments = rootCommand.Parse(args);
            var environmentOptions = new Command("environment");
            foreach (Option option in rootCommand.Options)
            {
                if (explicitArguments.GetResult(option) is not OptionResult { Implicit: false })
                {
                    environmentOptions.Options.Add(option);
                }
            }

            return rootCommand.Parse(ConsoleUtils.MergeEnvironmentArgs(args, "REFSERVER", environmentOptions));
        }

        /// <summary>
        /// Creates the reference server command without loading configuration or starting a server.
        /// </summary>
        public static RootCommand CreateCommand()
        {
            // The application name and config file names
            string applicationName = Utils.IsRunningOnMono() ? "MonoReferenceServer" : "ConsoleReferenceServer";
            string configSectionName = Utils.IsRunningOnMono()
                ? "Quickstarts.MonoReferenceServer"
                : "Quickstarts.ReferenceServer";

            // command line options
            var autoAcceptOption = new Option<bool>("--autoaccept", "-a")
            {
                Description = "accept untrusted application certificates (isolated testing only); "
                    + "does not accept other certificate errors or enable None endpoints"
            };
            var allowNoneOption = new Option<bool>("--allow-none")
            {
                Description = "allow SecurityPolicy None endpoints (no message security, isolated testing only); "
                    + "false removes configured None endpoints; omitted preserves the loaded configuration"
            };
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
                Description = "explicitly consent to certificate provisioning with a limited namespace and "
                    + "acceptance of untrusted application certificates, even with --autoaccept=false; "
                    + "isolated commissioning only; does not enable None endpoints"
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
                allowNoneOption,
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
                bool logConsole = parseResult.GetValue(consoleOption);
                bool appLog = parseResult.GetValue(logOption);
                bool fileLog = parseResult.GetValue(fileOption);
                string? passwordStr = parseResult.GetValue(passwordOption);
                char[]? password = passwordStr?.ToCharArray();
                bool renewCertificate = parseResult.GetValue(renewOption);
                int timeoutSec = parseResult.GetValue(timeoutOption);
                int timeout = timeoutSec >= 0 ? timeoutSec * 1000 : -1;
                bool shadowConfig = parseResult.GetValue(shadowConfigOption);
                bool samplingGroups = parseResult.GetValue(samplingGroupsOption);
                bool cttMode = parseResult.GetValue(cttOption);
                bool provisioningMode = parseResult.GetValue(provisionOption);
                string? reverseConnectUrlString = parseResult.GetValue(reverseConnectOption);

                // Use CTT-specific config when CTT mode is enabled.
                // Mono uses a separate server (MonoReferenceServer) with its own config, so CTT config is not applicable there.
                if (cttMode && !Utils.IsRunningOnMono())
                {
                    configSectionName = "Ctt.ReferenceServer";
                }

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
                        Password = password
                    };

                    // load the server configuration, validate certificates
                    Console.WriteLine($"Loading configuration from {configSectionName}.");
                    await server.LoadAsync(applicationName, configSectionName).ConfigureAwait(false);

                    // use the shadow config to map the config to an externally accessible location
                    if (shadowConfig)
                    {
                        Console.WriteLine("Using shadow configuration.");
                        // --shadowconfig requires TraceConfiguration.OutputFilePath and SourceFilePath to be set;
                        // null values would have thrown NullReferenceException historically — preserve that behavior.
                        string shadowPath = Directory
                            .GetParent(
                                Path.GetDirectoryName(
                                    Utils.ReplaceSpecialFolderNames(server.Configuration.TraceConfiguration!.OutputFilePath))!)!
                            .FullName;
                        string shadowFilePath = Path.Combine(
                            shadowPath,
                            Path.GetFileName(server.Configuration.SourceFilePath)!
                        );
                        if (!File.Exists(shadowFilePath))
                        {
                            Console.WriteLine("Create a copy of the config in the shadow location.");
                            File.Copy(server.Configuration.SourceFilePath!, shadowFilePath, true);
                        }
                        Console.WriteLine($"Reloading configuration from shadow location {shadowFilePath}.");
                        await server
                            .LoadAsync(applicationName, Path.Combine(shadowPath, configSectionName))
                            .ConfigureAwait(false);
                    }

                    server.AutoAccept = ConfigureHost(
                        parseResult,
                        new ApplicationConfigurationBuilder((ApplicationInstance)server.Application),
                        Console.Out);

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

                    // Add async node managers
                    foreach (IAsyncNodeManagerFactory asyncFactory in Servers.Utils.AsyncNodeManagerFactories)
                    {
                        server.Server!.AddNodeManager(asyncFactory);
                    }

                    // Add GDS node manager if configured
                    GlobalDiscoveryServerConfiguration? gdsConfig = server.Configuration
                        .ParseExtension<GlobalDiscoveryServerConfiguration>();
                    if (gdsConfig != null)
                    {
                        Console.WriteLine("GDS configuration found. Adding GDS node manager.");
                        server.Server!.AddNodeManager(new GdsNodeManagerFactory(gdsConfig));
                    }

                    // enable provisioning mode if requested
                    if (provisioningMode)
                    {
                        Console.WriteLine("Enabling provisioning mode.");
                        Servers.Utils.EnableProvisioningMode(server.Server!);
                    }

                    // enable the sampling groups if requested
                    if (samplingGroups)
                    {
                        Servers.Utils.UseSamplingGroupsInReferenceNodeManager(server.Server!);
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
                            server.Server!.AddReverseConnection(reverseConnectUrl);
                        }
                        catch (UriFormatException ex)
                        {
                            logger.InvalidReverseConnectUrl(ex, reverseConnectUrlString);
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
                        await Servers.Utils.ApplyCTTModeAsync(Console.Out, server.Server!)
                            .ConfigureAwait(false);
                    }

                    Console.WriteLine($"Server started ({sw.ElapsedMilliseconds} ms). Press Ctrl-C to exit...");

                    // wait for timeout or Ctrl-C (cancellationToken is cancelled on Ctrl-C by System.CommandLine)
                    if (timeout >= 0)
                    {
                        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
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

            return rootCommand;
        }

        /// <summary>
        /// Applies explicit host options after the final configuration load, before certificate checks or startup.
        /// </summary>
        /// <returns>
        /// Whether the host may accept untrusted application certificates.
        /// </returns>
        public static bool ConfigureHost(
            ParseResult parseResult,
            ApplicationConfigurationBuilder builder,
            TextWriter output)
        {
            _ = parseResult ?? throw new ArgumentNullException(nameof(parseResult));
            _ = builder ?? throw new ArgumentNullException(nameof(builder));
            _ = output ?? throw new ArgumentNullException(nameof(output));

            if (parseResult.GetValue<bool>("--allow-none"))
            {
                output.WriteLine(
                    "WARNING: --allow-none enables SecurityPolicy None endpoints with no message security. "
                    + "Use only in an isolated test environment.");
                builder.AddUnsecurePolicyNone();
            }
            else if (parseResult.GetResult("--allow-none") is OptionResult { Implicit: false })
            {
                ServerConfiguration configuration = builder.ApplicationConfiguration.ServerConfiguration!;
                configuration.SecurityPolicies = configuration.SecurityPolicies.ToList()
                    .FindAll(policy => policy.SecurityMode != MessageSecurityMode.None
                        && policy.SecurityPolicyUri != SecurityPolicies.None)
                    .ToArrayOf();
            }

            bool provisioningMode = parseResult.GetValue<bool>("--provision");
            bool autoAccept = parseResult.GetValue<bool>("--autoaccept");
            if (provisioningMode)
            {
                output.WriteLine(
                    "WARNING: --provision enables a limited namespace for certificate provisioning and accepts "
                    + "untrusted application certificates, even with --autoaccept=false. "
                    + "Use only for isolated commissioning; this does not enable None endpoints "
                    + "or bypass other certificate errors.");
            }
            else if (autoAccept)
            {
                output.WriteLine(
                    "WARNING: --autoaccept accepts untrusted application certificates (BadCertificateUntrusted only). "
                    + "Use only in an isolated test environment; other certificate errors remain rejected.");
            }

            return provisioningMode || autoAccept;
        }
    }

    /// <summary>
    /// Source-generated diagnostics for reference server command-line configuration.
    /// </summary>
    internal static partial class ProgramLog
    {
        /// <summary>
        /// Reports a malformed reverse-connect URL together with its parsing exception.
        /// </summary>
        [LoggerMessage(EventId = ConsoleReferenceServerEventIds.Program + 0, Level = LogLevel.Error,
            Message = "Invalid reverse connect URL: {Url}")]
        public static partial void InvalidReverseConnectUrl(this ILogger logger, UriFormatException ex, string url);
    }

}
