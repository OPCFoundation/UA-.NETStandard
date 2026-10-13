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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Machinery.Server;
using Opc.Ua.MachineTool;
using Opc.Ua.Server.Hosting;

namespace CompanionSuite
{
    /// <summary>
    /// Runs the same bounded simulated workcell as a server, typed client, or real-TCP self-check.
    /// </summary>
    internal static class Program
    {
        /// <summary>
        /// Validates input before creating application resources and reports every failure through the exit code.
        /// </summary>
        private static async Task<int> Main(string[] args)
        {
            if (Array.IndexOf(args, "--help") >= 0)
            {
                SuiteOptions.PrintHelp();
                return 0;
            }
            using var cancelled = new CancellationTokenSource();
            void Cancel(object? _, ConsoleCancelEventArgs eventArgs)
            {
                eventArgs.Cancel = true;
                cancelled.Cancel();
            }
            Console.CancelKeyPress += Cancel;
            try
            {
                SuiteOptions options = SuiteOptions.Parse(ArrayOf.Wrapped(args));
                Console.WriteLine("SIMULATION ONLY: no hardware controller, Bluetooth connection or physical effects.");
                if (options.Mode == SuiteMode.Client)
                {
                    await RunClientAsync(options, options.Endpoint, cancelled.Token).ConfigureAwait(false);
                }
                else
                {
                    await RunServerAsync(options, cancelled.Token).ConfigureAwait(false);
                }
                return 0;
            }
            catch (OperationCanceledException) when (cancelled.IsCancellationRequested)
            {
                Console.WriteLine("CANCELLED: owned sessions and workcell resources have been drained.");
                return 0;
            }
            catch (Exception exception)
            {
                await Console.Error.WriteLineAsync(exception.ToString()).ConfigureAwait(false);
                return 1;
            }
            finally
            {
                Console.CancelKeyPress -= Cancel;
            }
        }

        /// <summary>
        /// Starts one actual Machinery owner with the required model union and awaits UA readiness.
        /// </summary>
        private static async Task RunServerAsync(SuiteOptions options, CancellationToken cancellationToken)
        {
            var endpoint = new UriBuilder(options.Endpoint) { Port = options.EndpointPort };
            if (endpoint.Port == 0)
            {
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
                endpoint.Port = ((IPEndPoint)socket.LocalEndPoint!).Port;
            }
            string url = endpoint.Uri.AbsoluteUri;
            HostApplicationBuilder builder = Host.CreateApplicationBuilder();
            ConfigureLogging(builder);
            builder.Services.AddSingleton(options);
            builder.Services.AddSingleton(TimeProvider.System);
            builder.Services.AddSingleton(services => new Workcell(
                services.GetRequiredService<SuiteOptions>(),
                services.GetRequiredService<TimeProvider>(),
                services.GetRequiredService<ITelemetryContext>()));
            builder.Services.AddSingleton<IServerStartupTask>(services => services.GetRequiredService<Workcell>());
            builder.Services.AddOpcUa().AddServer(server =>
            {
                server.ApplicationName = "CompanionSuiteServer";
                server.ApplicationUri = "urn:localhost:OPCFoundation:CompanionSuiteServer";
                server.PkiRoot = Path.Combine(options.PkiRoot, "server");
                server.AutoAcceptUntrustedCertificates = options.DemoTrust;
                server.IncludeUnsecurePolicyNone = false;
                server.EndpointUrls.Clear();
                server.EndpointUrls.Add(url);
            }).AddMachinery(machinery =>
            {
                machinery.Parts = MachineryParts.BuildingBlocks | MachineryParts.Jobs |
                    MachineryParts.Result | MachineryParts.ProcessValues;
                machinery.InstanceNamespaceUri = Workcell.InstanceUri;
            }).AddMachineToolToMachinery(machine => machine.Parts = MachineToolParts.All)
                .AddGmsToMachinery().AddCranesHoistsToMachinery().AddVisionToMachinery()
                .ConfigureMachineToolFor<MachineryNodeManager>((context, ct) =>
                    context.GetRequiredService<Workcell>().BuildMachinesAsync(context, ct))
                .ConfigureGmsFor<MachineryNodeManager>((context, ct) =>
                    context.GetRequiredService<Workcell>().BuildGmsAsync(context, ct))
                .ConfigureCranesHoistsFor<MachineryNodeManager>((context, ct) =>
                    context.Machinery.GetRequiredService<Workcell>().BuildCranesAsync(context, ct))
                .ConfigureVisionFor<MachineryNodeManager>((context, ct) =>
                    context.GetRequiredService<Workcell>().BuildVisionAsync(context, ct));
            IHost host = builder.Build();
            await using var asynchronousHost = ((IAsyncDisposable)host).ConfigureAwait(false);
            Workcell workcell = host.Services.GetRequiredService<Workcell>();
            IHostApplicationLifetime lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
            var failures = new List<Exception>();
            try
            {
                if (host.Services.GetServices<OpcUaServerNodeManagerRegistration>().Count() != 1)
                {
                    throw new InvalidOperationException("The workcell must have exactly one namespace owner.");
                }
                if (options.DemoTrust)
                {
                    host.Services.GetRequiredService<ITelemetryContext>().CreateLogger<Workcell>()
                        .DemoTrustEnabled();
                }
                using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45)))
                using (var startup = CancellationTokenSource.CreateLinkedTokenSource(
                    deadline.Token, cancellationToken, lifetime.ApplicationStopping))
                {
                    await host.StartAsync(startup.Token).ConfigureAwait(false);
                    await workcell.Ready.WaitAsync(startup.Token).ConfigureAwait(false);
                }
                Console.WriteLine($"READY {url} owner=MachineryNodeManager owners=1 instances={options.Instances}");
                using var running = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, lifetime.ApplicationStopping);
                if (options.Mode == SuiteMode.SelfCheck)
                {
                    await RunClientAsync(options, url, running.Token).ConfigureAwait(false);
                }
                else
                {
                    await Task.Delay(TimeSpan.FromSeconds(options.Seconds), running.Token).ConfigureAwait(false);
                }
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
            finally
            {
                try
                {
                    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                    await host.StopAsync(cleanup.Token).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
                try
                {
                    await workcell.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }
            if (failures.Count != 0)
            {
                if (failures.Count == 1 && failures[0] is OperationCanceledException &&
                    cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }
                throw new AggregateException("Workcell execution or cleanup failed.", failures);
            }
            Console.WriteLine("STOPPED: all native runtimes and provider resources disposed.");
        }

        /// <summary>
        /// Discovers an encrypted endpoint and shares one managed session across all generated family clients.
        /// </summary>
        private static async Task RunClientAsync(
            SuiteOptions options, string endpoint, CancellationToken cancellationToken)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder();
            ConfigureLogging(builder);
            builder.Services.AddOpcUa().AddClient(client =>
            {
                client.ApplicationName = "CompanionSuiteClient";
                client.ApplicationUri = "urn:localhost:OPCFoundation:CompanionSuiteClient";
                client.PkiRoot = Path.Combine(options.PkiRoot, "client");
                client.AutoAcceptUntrustedCertificates = options.DemoTrust;
                client.Session = new ManagedSessionOptions
                {
                    SessionName = "CompanionSuiteClient",
                    CheckDomain = true,
                    SessionTimeout = TimeSpan.FromSeconds(30)
                };
            }).AddDiscoveryAndConnect(discovery =>
            {
                discovery.DiscoveryUrl = endpoint;
                discovery.SecurityMode = MessageSecurityMode.SignAndEncrypt;
                discovery.SecurityPolicyUri = SecurityPolicies.Basic256Sha256;
            });
            IHost host = builder.Build();
            await using var asynchronousHost = ((IAsyncDisposable)host).ConfigureAwait(false);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token, cancellationToken);
            try
            {
                await host.StartAsync(linked.Token).ConfigureAwait(false);
                ManagedSession session = await host.Services
                    .GetRequiredService<Func<CancellationToken, Task<ManagedSession>>>()(linked.Token)
                    .ConfigureAwait(false);
                await using (session.ConfigureAwait(false))
                {
                    try
                    {
                        await WorkcellReader.ReadAsync(session, host.Services.GetRequiredService<ITelemetryContext>(),
                            options, linked.Token).ConfigureAwait(false);
                    }
                    finally
                    {
                        using var close = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                        await session.CloseAsync(close.Token).ConfigureAwait(false);
                    }
                }
                Console.WriteLine($"CLIENT_COMPLETE connected={session.Connected} disposed={session.Disposed}");
            }
            finally
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                await host.StopAsync(cleanup.Token).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Keeps framework diagnostics concise without hiding failures in the application.
        /// </summary>
        private static void ConfigureLogging(HostApplicationBuilder builder)
        {
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
        }
    }

    /// <summary>
    /// Source-generated application-policy diagnostics.
    /// </summary>
    internal static partial class ProgramLog
    {
        /// <summary>
        /// Warns about the explicit certificate-trust exception, without disabling message security.
        /// </summary>
        [LoggerMessage(EventId = CompanionSuiteEventIds.Program, Level = LogLevel.Warning,
            Message = "LOCAL DEMO ONLY: accepting untrusted certificates; SignAndEncrypt is still required.")]
        public static partial void DemoTrustEnabled(this ILogger logger);
    }

    /// <summary>
    /// Identifiers reserved for this sample's generated log messages.
    /// </summary>
    internal static class CompanionSuiteEventIds
    {
        /// <summary>The application policy diagnostic range.</summary>
        internal const int Program = 1;
    }
}
