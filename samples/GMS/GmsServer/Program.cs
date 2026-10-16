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
using Opc.Ua.Machinery.Server;
using Opc.Ua.Server.Hosting;

namespace GmsSample.Server
{
    /// <summary>
    /// Hosts native GMS instances through the public fluent DI path with a bounded simulation lifetime.
    /// </summary>
    internal static class Program
    {
        /// <summary>
        /// Parses options before resource allocation and awaits cleanup on errors or cancellation.
        /// </summary>
        private static async Task<int> Main(string[] args)
        {
            if (Array.IndexOf(args, "--help") >= 0 || Array.IndexOf(args, "-h") >= 0)
            {
                GmsServerOptions.PrintHelp();
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
                await RunAsync(GmsServerOptions.Parse(ArrayOf.Wrapped(args)), cancelled.Token).ConfigureAwait(false);
                return 0;
            }
            catch (OperationCanceledException) when (cancelled.IsCancellationRequested)
            {
                Console.WriteLine("CANCELLED: GMS instances and host disposed.");
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
        /// Keeps trust administration separate from encrypted transport and per-instance provider ownership.
        /// </summary>
        private static async Task RunAsync(GmsServerOptions options, CancellationToken cancellationToken)
        {
#if NET8_0_OR_GREATER
            bool dynamicCode = System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported;
            Console.WriteLine($"RUNTIME dynamicCodeSupported={dynamicCode}");
#endif
            TimeProvider clock = TimeProvider.System;
            int port = options.Port == 0 ? SelectLocalPort() : options.Port;
            string endpoint = FormattableString.Invariant($"opc.tcp://127.0.0.1:{port}/GmsServer");
            HostApplicationBuilder builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
            builder.Logging.AddFilter("GmsSample", LogLevel.Information);
            builder.Services.AddSingleton(options);
            builder.Services.AddSingleton(clock);
            builder.Services.AddSingleton(services => new GmsSimulation(
                services.GetRequiredService<GmsServerOptions>(),
                services.GetRequiredService<TimeProvider>(),
                services.GetRequiredService<ITelemetryContext>()));
            builder.Services.AddSingleton<IServerStartupTask>(services =>
                services.GetRequiredService<GmsSimulation>());
            builder.Services.AddOpcUa()
                .AddServer(server =>
                {
                    server.ApplicationName = "GmsServer";
                    server.ApplicationUri = "urn:localhost:OPCFoundation:GmsServer";
                    server.ProductUri = "uri:opcfoundation.org:GmsServer";
                    server.PkiRoot = options.PkiRoot;
                    server.AutoAcceptUntrustedCertificates = options.DemoTrust;
                    server.IncludeUnsecurePolicyNone = false;
                    server.EndpointUrls.Add(endpoint);
                })
                .AddGms(owner => owner.InstanceNamespaceUri = "urn:opcfoundation.org:gms:sample")
                .ConfigureGms((context, ct) =>
                    context.GetRequiredService<GmsSimulation>().ConfigureAsync(context, ct));
            builder.Services.PostConfigure<MachineryServerOptions>(owner =>
            {
                owner.MaxPinnedResultHandles = 1;
                owner.MaxConcurrentResultTransfers = 1;
                owner.PinnedResultHandleTimeout = TimeSpan.FromMinutes(3);
                owner.ResultTransferTimeout = TimeSpan.FromMinutes(3);
            });
            IHost host = builder.Build();
            var asynchronousHost = (IAsyncDisposable)host;
            await using (asynchronousHost.ConfigureAwait(false))
            {
                ILogger logger = host.Services.GetRequiredService<ITelemetryContext>().CreateLogger<GmsSimulation>();
                GmsSimulation simulation = host.Services.GetRequiredService<GmsSimulation>();
                IHostApplicationLifetime lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
                if (options.DemoTrust)
                {
                    logger.LocalDemoTrustEnabled();
                }
                try
                {
                    using (CancellationTokenSource startup =
                        TimeProviderExtensions.CreateCancellationTokenSource(clock, TimeSpan.FromSeconds(30)))
                    using (var starting = CancellationTokenSource.CreateLinkedTokenSource(
                        startup.Token, lifetime.ApplicationStopping, cancellationToken))
                    {
                        await host.StartAsync(starting.Token).ConfigureAwait(false);
                        await simulation.Ready.WaitAsync(starting.Token).ConfigureAwait(false);
                    }
                    Console.WriteLine($"READY {endpoint}");
                    Console.WriteLine("SIMULATION ONLY: no physical equipment, acquisition commands or job control.");
                    using var running = CancellationTokenSource.CreateLinkedTokenSource(
                        lifetime.ApplicationStopping, cancellationToken);
                    await simulation.RunAsync(running.Token).ConfigureAwait(false);
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
                        await simulation.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
            Console.WriteLine("STOPPED: simulation awaited; GMS resources and host disposed.");
        }

        /// <summary>
        /// Chooses a currently free local port; a subsequent bind race still fails startup explicitly.
        /// </summary>
        private static int SelectLocalPort()
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            return ((IPEndPoint)socket.LocalEndPoint!).Port;
        }
    }

    /// <summary>
    /// Source-generated security-policy diagnostics.
    /// </summary>
    internal static partial class ProgramLog
    {
        /// <summary>
        /// Makes the explicit loopback-only trust exception visible.
        /// </summary>
        [LoggerMessage(EventId = GmsServerEventIds.Program, Level = LogLevel.Warning,
            Message = "LOCAL SIMULATION: accepting unknown client certificates on 127.0.0.1; encryption is required.")]
        public static partial void LocalDemoTrustEnabled(this ILogger logger);
    }

    /// <summary>
    /// Reserves disjoint source-generated logging ranges for this sample assembly.
    /// </summary>
    internal static class GmsServerEventIds
    {
        /// <summary>The application security-policy range.</summary>
        public const int Program = 1;

        /// <summary>The simulation lifecycle range.</summary>
        public const int GmsSimulation = 100;
    }
}
