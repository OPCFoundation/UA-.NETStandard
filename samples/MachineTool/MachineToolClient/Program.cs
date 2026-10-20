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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.MachineTool.Client;
using TypedClient = Opc.Ua.MachineTool.Client.MachineToolClient;

namespace MachineToolSample.Client
{
    /// <summary>
    /// Runs one bounded, passive-by-default workflow through a managed session and the typed client factory.
    /// </summary>
    internal static class Program
    {
        /// <summary>
        /// Validates local consent before allocating resources and reports cancellation separately.
        /// </summary>
        private static async Task<int> Main(string[] args)
        {
            if (Array.IndexOf(args, "--help") >= 0 || Array.IndexOf(args, "-h") >= 0)
            {
                ClientOptions.PrintHelp();
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
                ClientOptions options = ClientOptions.Parse(ArrayOf.Wrapped(args));
                if (options.CancelAfterMilliseconds.HasValue)
                {
                    cancelled.CancelAfter(options.CancelAfterMilliseconds.Value);
                }
                await RunAsync(options, cancelled.Token).ConfigureAwait(false);
                return 0;
            }
            catch (OperationCanceledException) when (cancelled.IsCancellationRequested)
            {
                Console.WriteLine("CANCELLED: local request cancelled; no implicit AbortOperation or command retry.");
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
        /// Keeps secure endpoint selection and certificate validation, and releases all owned sessions on every path.
        /// </summary>
        private static async Task RunAsync(ClientOptions options, CancellationToken cancellationToken)
        {
            TimeProvider clock = TimeProvider.System;
            using CancellationTokenSource deadline =
                TimeProviderExtensions.CreateCancellationTokenSource(clock, TimeSpan.FromSeconds(90));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
            CancellationToken ct = linked.Token;
            HostApplicationBuilder builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            builder.Logging.EnableOpcUaRedaction();
            builder.Logging.AddConsole();
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
            builder.Logging.AddFilter("MachineToolSample", LogLevel.Information);
            builder.Services.AddSingleton(clock);
            builder.Services.AddOpcUa()
                .AddClient(client =>
                {
                    client.ApplicationName = "MachineToolClient";
                    client.ApplicationUri = "urn:localhost:OPCFoundation:MachineToolClient";
                    client.ProductUri = "uri:opcfoundation.org:MachineToolClient";
                    client.PkiRoot = options.PkiRoot;
                    client.AutoAcceptUntrustedCertificates = options.DemoTrust;
                    client.Session = new ManagedSessionOptions
                    {
                        SessionName = "MachineToolClient",
                        SessionTimeout = TimeSpan.FromSeconds(30),
                        CheckDomain = true,
                        TimeProvider = clock
                    };
                })
                .AddDiscoveryAndConnect(discovery =>
                {
                    discovery.DiscoveryUrl = options.Endpoint;
                    discovery.SecurityMode = MessageSecurityMode.SignAndEncrypt;
                    discovery.SecurityPolicyUri = SecurityPolicies.Basic256Sha256;
                })
                .AddMachineToolClient();
            IHost host = builder.Build();
            await using (((IAsyncDisposable)host).ConfigureAwait(false))
            {
                ILogger logger = host.Services.GetRequiredService<ITelemetryContext>().CreateLogger<ClientOptions>();
                if (options.DemoTrust)
                {
                    logger.LocalDemoTrust();
                }
                try
                {
                    await host.StartAsync(ct).ConfigureAwait(false);
                    TypedClient client = await host.Services.GetRequiredService<MachineToolClientFactory>()
                        .CreateAsync(ct).ConfigureAwait(false);
                    ManagedSession session = client.Session as ManagedSession ??
                        throw new InvalidOperationException("The configured factory did not return a managed session.");
                    try
                    {
                        await DemoReader.RunAsync(client, options, ct).ConfigureAwait(false);
                    }
                    finally
                    {
                        try
                        {
                            using CancellationTokenSource close =
                                TimeProviderExtensions.CreateCancellationTokenSource(clock, TimeSpan.FromSeconds(10));
                            await session.CloseAsync(close.Token).ConfigureAwait(false);
                        }
                        finally
                        {
                            await client.DisposeAsync().ConfigureAwait(false);
                            await session.DisposeAsync().ConfigureAwait(false);
                        }
                        Console.WriteLine($"SESSION_CLOSED connected={session.Connected} disposed={session.Disposed}");
                    }
                }
                finally
                {
                    using CancellationTokenSource cleanup =
                        TimeProviderExtensions.CreateCancellationTokenSource(clock, TimeSpan.FromSeconds(20));
                    await host.StopAsync(cleanup.Token).ConfigureAwait(false);
                }
            }
            Console.WriteLine("CLIENT_COMPLETE: observations, session and application host disposed.");
        }
    }

    /// <summary>
    /// Source-generated client policy diagnostics.
    /// </summary>
    internal static partial class ProgramLog
    {
        /// <summary>
        /// Makes the loopback-only trust exception visible without changing message-security selection.
        /// </summary>
        [LoggerMessage(EventId = MachineToolClientEventIds.Program, Level = LogLevel.Warning,
            Message = "LOCAL SIMULATION ONLY: unknown server certificates accepted; SignAndEncrypt still required.")]
        public static partial void LocalDemoTrust(this ILogger logger);
    }
}
