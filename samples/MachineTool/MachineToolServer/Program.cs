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
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.MachineTool.WMTP;
using Opc.Ua.Server.Hosting;

namespace MachineToolSample.Server
{
    /// <summary>
    /// Hosts one selected Machinery owner with bounded MachineTool and independent WMTP simulations.
    /// </summary>
    internal static class Program
    {
        /// <summary>
        /// Validates arguments before allocating resources and distinguishes cancellation from completion.
        /// </summary>
        private static async Task<int> Main(string[] args)
        {
            if (Array.IndexOf(args, "--help") >= 0 || Array.IndexOf(args, "-h") >= 0)
            {
                ServerOptions.PrintHelp();
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
                ServerOptions options = ServerOptions.Parse(ArrayOf.Wrapped(args));
                if (options.CancelAfterMilliseconds.HasValue)
                {
                    cancelled.CancelAfter(options.CancelAfterMilliseconds.Value);
                }
                await RunAsync(options, cancelled.Token).ConfigureAwait(false);
                return 0;
            }
            catch (OperationCanceledException) when (cancelled.IsCancellationRequested)
            {
                Console.WriteLine("CANCELLED: startup/acquisition stopped and owned resources drained.");
                return 2;
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
        /// Uses the existing configuration, certificate stores, post-start task and awaited host lifetime.
        /// </summary>
        private static async Task RunAsync(ServerOptions options, CancellationToken cancellationToken)
        {
            TimeProvider clock = TimeProvider.System;
            int port = options.Port == 0 ? SelectPort() : options.Port;
            string endpoint = FormattableString.Invariant($"opc.tcp://127.0.0.1:{port}/MachineToolServer");
            HostApplicationBuilder builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            builder.Logging.EnableOpcUaRedaction();
            builder.Logging.AddConsole();
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
            builder.Logging.AddFilter("MachineToolSample", LogLevel.Information);
            builder.Services.AddSingleton(options);
            builder.Services.AddSingleton(clock);
            builder.Services.AddSingleton<IWmtpAuthorization>(services =>
                new DemoAuthorization(services.GetRequiredService<ServerOptions>()));
            builder.Services.AddSingleton(services => new DemoApplication(
                services.GetRequiredService<ServerOptions>(), services.GetRequiredService<TimeProvider>(),
                services.GetRequiredService<ITelemetryContext>(), services.GetRequiredService<IWmtpAuthorization>()));
            builder.Services.AddSingleton<IServerStartupTask>(services =>
                services.GetRequiredService<DemoApplication>());
            builder.Services.AddOpcUa()
                .AddServer(server =>
                {
                    server.ApplicationName = "MachineToolServer";
                    server.ApplicationUri = "urn:localhost:OPCFoundation:MachineToolServer";
                    server.ProductUri = "uri:opcfoundation.org:MachineToolServer";
                    server.PkiRoot = options.PkiRoot;
                    server.AutoAcceptUntrustedCertificates = options.DemoTrust;
                    server.IncludeUnsecurePolicyNone = false;
                    server.EndpointUrls.Add(endpoint);
                    server.OperationTimeoutMs = 10000;
                    server.Identity.Defaults.EnableUserNamePassword = false;
                    server.Identity.Defaults.EnableX509 = false;
                    server.Identity.Defaults.EnableJwt = false;
                })
                .AddMachineTool(model => model.Parts = options.Parts)
                .ConfigureMachineTool((context, ct) =>
                    context.GetRequiredService<DemoApplication>().ConfigureAsync(context, ct));
            IHost host = builder.Build();
            await using (((IAsyncDisposable)host).ConfigureAwait(false))
            {
                DemoApplication demo = host.Services.GetRequiredService<DemoApplication>();
                ILogger logger = host.Services.GetRequiredService<ITelemetryContext>().CreateLogger<DemoApplication>();
                IHostApplicationLifetime lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
                if (options.DemoTrust)
                {
                    logger.LocalDemoTrust(options.AllowCommands);
                }
                try
                {
                    using (CancellationTokenSource timeout =
                        TimeProviderExtensions.CreateCancellationTokenSource(clock, TimeSpan.FromSeconds(30)))
                    using (var startup = CancellationTokenSource.CreateLinkedTokenSource(
                        timeout.Token, cancellationToken, lifetime.ApplicationStopping))
                    {
                        await host.StartAsync(startup.Token).ConfigureAwait(false);
                        await demo.Ready.WaitAsync(startup.Token).ConfigureAwait(false);
                    }
                    Console.WriteLine($"READY {endpoint}");
                    Console.WriteLine("SIMULATION ONLY: no CNC, Bluetooth, pairing, flash driver or physical effects.");
                    using var running = CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken, lifetime.ApplicationStopping);
                    await demo.RunAsync(running.Token).ConfigureAwait(false);
                }
                finally
                {
                    try
                    {
                        using CancellationTokenSource cleanup =
                            TimeProviderExtensions.CreateCancellationTokenSource(clock, TimeSpan.FromSeconds(20));
                        await host.StopAsync(cleanup.Token).ConfigureAwait(false);
                    }
                    finally
                    {
                        // The host must stop before providers borrowed by a partially started address space are freed.
                        await demo.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
            Console.WriteLine("STOPPED: acquisition joined; runtimes, providers and application host disposed.");
        }

        /// <summary>
        /// Selects an available loopback port; a subsequent bind race still fails explicitly at startup.
        /// </summary>
        private static int SelectPort()
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            return ((IPEndPoint)socket.LocalEndPoint!).Port;
        }
    }

    /// <summary>
    /// Source-generated policy diagnostics.
    /// </summary>
    internal static partial class ProgramLog
    {
        /// <summary>
        /// Warns about the local-only certificate and anonymous command policy.
        /// </summary>
        [LoggerMessage(EventId = MachineToolServerEventIds.Program, Level = LogLevel.Warning,
            Message = "LOCAL SIMULATION ONLY: unknown certificates accepted; anonymous demo commands: {Allowed}.")]
        public static partial void LocalDemoTrust(this ILogger logger, bool allowed);
    }
}
