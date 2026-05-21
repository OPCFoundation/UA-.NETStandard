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
using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Configuration;
using Opc.Ua.Lds.Server;
using Serilog;
using Serilog.Events;

namespace Opc.Ua.Lds.Server.Console
{
    /// <summary>
    /// Console host for the OPC UA Local Discovery Server (LDS / LDS-ME).
    /// </summary>
    public static class Program
    {
        private const string ApplicationName = "ConsoleLdsServer";
        private const string ConfigSectionName = "Lds.Server";

        public static Task<int> Main(string[] args)
        {
            System.Console.WriteLine(".NET OPC UA Local Discovery Server");
            System.Console.WriteLine(
                "OPC UA library: {0} @ {1} -- {2}",
                Utils.GetAssemblyBuildNumber(),
                Utils.GetAssemblyTimestamp().ToString("G", CultureInfo.InvariantCulture),
                Utils.GetAssemblySoftwareVersion());

            var consoleOption = new Option<bool>("--console", "-c") { Description = "log to console" };
            var autoAcceptOption = new Option<bool>("--autoaccept", "-a") { Description = "auto accept untrusted certificates (testing only)" };
            var noMdnsOption = new Option<bool>("--no-mdns") { Description = "disable mDNS multicast (LDS-ME) advertisement" };
            var loopbackMdnsOption = new Option<bool>("--mdns-loopback") { Description = "restrict mDNS to the loopback interface (testing)" };
            var timeoutOption = new Option<int>("--timeout", "-t")
            {
                Description = "timeout in seconds before exiting (-1 = run until Ctrl+C)",
                DefaultValueFactory = _ => -1
            };

            var rootCommand = new RootCommand($"Usage: dotnet {ApplicationName}.dll [OPTIONS]")
            {
                consoleOption,
                autoAcceptOption,
                noMdnsOption,
                loopbackMdnsOption,
                timeoutOption
            };

            rootCommand.SetAction(async (parseResult, cancellationToken) =>
            {
                bool logConsole = parseResult.GetValue(consoleOption);
                bool autoAccept = parseResult.GetValue(autoAcceptOption);
                bool noMdns = parseResult.GetValue(noMdnsOption);
                bool loopbackMdns = parseResult.GetValue(loopbackMdnsOption);
                int timeoutSec = parseResult.GetValue(timeoutOption);
                int timeoutMs = timeoutSec >= 0 ? timeoutSec * 1000 : -1;

                if (logConsole)
                {
                    Log.Logger = new LoggerConfiguration()
                        .MinimumLevel.Information()
                        .WriteTo.Console(restrictedToMinimumLevel: LogEventLevel.Information,
                            formatProvider: CultureInfo.InvariantCulture)
                        .CreateLogger();
                }

                ITelemetryContext telemetry = DefaultTelemetry.Create(builder =>
                {
                    builder.SetMinimumLevel(LogLevel.Information);
                    if (logConsole)
                    {
                        builder.AddSerilog(Log.Logger);
                    }
                });
                Microsoft.Extensions.Logging.ILogger logger = telemetry.LoggerFactory.CreateLogger("Main");

                var sw = Stopwatch.StartNew();
                var application = new ApplicationInstance(telemetry)
                {
                    ApplicationName = ApplicationName,
                    ApplicationType = ApplicationType.DiscoveryServer,
                    ConfigSectionName = ConfigSectionName
                };

                try
                {
                    System.Console.WriteLine($"Loading configuration from {ConfigSectionName}.Config.xml.");
                    await application
                        .LoadApplicationConfigurationAsync(silent: false, ct: cancellationToken)
                        .ConfigureAwait(false);

                    System.Console.WriteLine("Checking the application certificate.");
                    bool ok = await application
                        .CheckApplicationInstanceCertificatesAsync(silent: false, ct: cancellationToken)
                        .ConfigureAwait(false);
                    if (!ok)
                    {
                        System.Console.Error.WriteLine("Application instance certificate invalid.");
                        return 1;
                    }

                    if (autoAccept)
                    {
                        application.ApplicationConfiguration.SecurityConfiguration
                            .AutoAcceptUntrustedCertificates = true;
                    }

                    var server = new LdsServer(telemetry);
                    if (!noMdns)
                    {
                        server.MulticastFactory = lds => new MulticastDiscovery(
                            lds.Store,
                            loopbackOnly: loopbackMdns,
                            logger: telemetry.LoggerFactory.CreateLogger<MulticastDiscovery>());
                    }
                    server.Store.StartPruneTimer();

                    System.Console.WriteLine("Starting the LDS.");
                    await application.StartAsync(server, cancellationToken).ConfigureAwait(false);

                    foreach (EndpointDescription ep in server.GetEndpoints())
                    {
                        System.Console.WriteLine("  Endpoint: {0}", ep.EndpointUrl);
                    }

                    System.Console.WriteLine($"LDS started ({sw.ElapsedMilliseconds} ms). Press Ctrl-C to exit...");

                    try
                    {
                        if (timeoutMs >= 0)
                        {
                            using var timeoutCts =
                                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                            timeoutCts.CancelAfter(timeoutMs);
                            await Task.Delay(Timeout.Infinite, timeoutCts.Token).ConfigureAwait(false);
                        }
                        else
                        {
                            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // expected — Ctrl-C or timeout.
                    }

                    System.Console.WriteLine("Stopping the LDS.");
                    await application.StopAsync(default).ConfigureAwait(false);
                    return 0;
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "Fatal error starting LDS.");
                    System.Console.Error.WriteLine(ex);
                    return 2;
                }
                finally
                {
                    (telemetry as IDisposable)?.Dispose();
                }
            });

            return rootCommand.Parse(args).InvokeAsync();
        }
    }
}
