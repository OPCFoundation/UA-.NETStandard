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
using Opc.Ua.CranesHoists;
using Opc.Ua.Server.Hosting;

namespace CranesHoistsSample.Server
{
    /// <summary>
    /// Runs a bounded standalone crane server without connecting to physical equipment.
    /// </summary>
    internal static class Program
    {
        /// <summary>
        /// Parses options before allocating resources and returns a nonzero exit code on failure.
        /// </summary>
        private static async Task<int> Main(string[] args)
        {
            if (Array.IndexOf(args, "--help") >= 0 || Array.IndexOf(args, "-h") >= 0)
            {
                CraneServerOptions.PrintHelp();
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
                await RunAsync(CraneServerOptions.Parse(ArrayOf.Wrapped(args)), cancelled.Token).ConfigureAwait(false);
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
        /// Uses the existing DI host, certificate stores and one Machinery-backed crane owner.
        /// </summary>
        private static async Task RunAsync(CraneServerOptions options, CancellationToken cancellationToken)
        {
            TimeProvider clock = TimeProvider.System;
            int port = options.Port == 0 ? SelectLocalPort() : options.Port;
            string endpoint = FormattableString.Invariant($"opc.tcp://localhost:{port}/CranesHoistsServer");
            HostApplicationBuilder builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
            builder.Logging.AddFilter("CranesHoistsSample", LogLevel.Information);
            builder.Services.AddSingleton(options);
            builder.Services.AddSingleton(clock);
            builder.Services.AddSingleton<ICraneWriteAuthorizer>(services =>
                new DemoWriteAuthorizer(services.GetRequiredService<CraneServerOptions>()));
            builder.Services.AddSingleton(services => new CraneDemo(
                services.GetRequiredService<CraneServerOptions>(),
                services.GetRequiredService<TimeProvider>(),
                services.GetRequiredService<ICraneWriteAuthorizer>(),
                services.GetRequiredService<ITelemetryContext>()));
            builder.Services.AddSingleton<IServerStartupTask>(services => services.GetRequiredService<CraneDemo>());
            builder.Services.AddOpcUa()
                .AddServer(server =>
                {
                    server.ApplicationName = "CranesHoistsServer";
                    server.ApplicationUri = "urn:localhost:OPCFoundation:CranesHoistsServer";
                    server.ProductUri = "uri:opcfoundation.org:CranesHoistsServer";
                    server.PkiRoot = options.PkiRoot;
                    server.AutoAcceptUntrustedCertificates = options.DemoTrust;
                    server.IncludeUnsecurePolicyNone = false;
                    server.EndpointUrls.Add(endpoint);
                })
                .AddCranesHoists()
                .ConfigureCranesHoists((context, ct) =>
                    context.Machinery.GetRequiredService<CraneDemo>().ConfigureAsync(context, ct));
            IHost host = builder.Build();
            var asynchronousHost = (IAsyncDisposable)host;
            await using (asynchronousHost.ConfigureAwait(false))
            {
                ILogger logger = host.Services.GetRequiredService<ITelemetryContext>().CreateLogger<CraneDemo>();
                if (options.DemoTrust)
                {
                    logger.LocalDemoTrustEnabled(options.AllowLimitRequests);
                }
                CraneDemo demo = host.Services.GetRequiredService<CraneDemo>();
                IHostApplicationLifetime lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
                try
                {
                    using (CancellationTokenSource startup =
                        TimeProviderExtensions.CreateCancellationTokenSource(clock, TimeSpan.FromSeconds(30)))
                    using (var stopping = CancellationTokenSource.CreateLinkedTokenSource(
                        startup.Token, lifetime.ApplicationStopping, cancellationToken))
                    {
                        await host.StartAsync(stopping.Token).ConfigureAwait(false);
                        await demo.Ready.WaitAsync(stopping.Token).ConfigureAwait(false);
                    }
                    Console.WriteLine($"READY {endpoint}");
                    Console.WriteLine("SIMULATION ONLY: no equipment connection and no physical effects.");
                    using var running = CancellationTokenSource.CreateLinkedTokenSource(
                        lifetime.ApplicationStopping, cancellationToken);
                    await demo.RunAsync(running.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    logger.CraneServerCancelled();
                }
                finally
                {
                    try
                    {
                        using CancellationTokenSource cleanup =
                            TimeProviderExtensions.CreateCancellationTokenSource(
                                clock, TimeSpan.FromSeconds(20));
                        await host.StopAsync(cleanup.Token).ConfigureAwait(false);
                    }
                    finally
                    {
                        await demo.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
            Console.WriteLine("STOPPED: acquisition awaited; crane resources and host disposed.");
        }

        /// <summary>
        /// Selects a currently free loopback port; startup still fails explicitly if another process takes it.
        /// </summary>
        private static int SelectLocalPort()
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            return ((IPEndPoint)socket.LocalEndPoint!).Port;
        }
    }

    /// <summary>
    /// Source-generated diagnostics for application policy and cancellation.
    /// </summary>
    internal static partial class ProgramLog
    {
        /// <summary>
        /// Warns that the explicit local-demo trust and authorization policy is not a production policy.
        /// </summary>
        [LoggerMessage(EventId = CranesHoistsServerEventIds.Program, Level = LogLevel.Warning,
            Message = "LOCAL DEMO ONLY: accepting unknown certificates; anonymous limit requests allowed: {Allowed}.")]
        public static partial void LocalDemoTrustEnabled(this ILogger logger, bool allowed);

        /// <summary>
        /// Records delivered host cancellation before awaited cleanup.
        /// </summary>
        [LoggerMessage(EventId = CranesHoistsServerEventIds.Program + 1, Level = LogLevel.Information,
            Message = "Crane server cancellation received; draining sample resources.")]
        public static partial void CraneServerCancelled(this ILogger logger);
    }
}
