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
using Opc.Ua.CranesHoists.Client;

namespace CranesHoistsSample.Client
{
    /// <summary>
    /// Runs read-only-by-default crane workflows over an injected managed session and typed client.
    /// </summary>
    internal static class Program
    {
        /// <summary>
        /// Validates flags before connecting and distinguishes cancellation from success or timeout.
        /// </summary>
        private static async Task<int> Main(string[] args)
        {
            if (Array.IndexOf(args, "--help") >= 0 || Array.IndexOf(args, "-h") >= 0)
            {
                CraneClientOptions.PrintHelp();
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
                await RunAsync(CraneClientOptions.Parse(ArrayOf.Wrapped(args)), cancelled.Token).ConfigureAwait(false);
                return 0;
            }
            catch (OperationCanceledException) when (cancelled.IsCancellationRequested)
            {
                Console.WriteLine("CANCELLED: client resources have been disposed; no implicit write retries.");
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
        /// Connects with message security and certificate-store validation, then awaits all owned resources.
        /// </summary>
        /// <exception cref="InvalidOperationException">The injected client has no managed session.</exception>
        private static async Task RunAsync(CraneClientOptions options, CancellationToken cancellationToken)
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
            builder.Logging.AddFilter("CranesHoistsSample", LogLevel.Information);
            builder.Services.AddSingleton(clock);
            builder.Services.AddOpcUa()
                .AddClient(client =>
                {
                    client.ApplicationName = "CranesHoistsClient";
                    client.ApplicationUri = "urn:localhost:OPCFoundation:CranesHoistsClient";
                    client.ProductUri = "uri:opcfoundation.org:CranesHoistsClient";
                    client.PkiRoot = options.PkiRoot;
                    client.AutoAcceptUntrustedCertificates = options.DemoTrust;
                    client.Session = new ManagedSessionOptions
                    {
                        SessionName = "CranesHoistsClient",
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
                .AddCranesHoistsClient(new CranesHoistsClientOptions { BrowsePageSize = 1 });
            IHost host = builder.Build();
            var asynchronousHost = (IAsyncDisposable)host;
            await using (asynchronousHost.ConfigureAwait(false))
            {
                ILogger logger = host.Services.GetRequiredService<ITelemetryContext>()
                    .CreateLogger<CraneClientOptions>();
                if (options.DemoTrust)
                {
                    logger.LocalClientDemoTrustEnabled();
                }
                try
                {
                    await host.StartAsync(ct).ConfigureAwait(false);
                    CranesHoistsClient client = await host.Services.GetRequiredService<CranesHoistsClientFactory>()
                        .CreateAsync(ct).ConfigureAwait(false);
                    ManagedSession session = client.Session as ManagedSession ??
                        throw new InvalidOperationException("The client factory did not supply a managed session.");
                    try
                    {
                        logger.CraneClientConnected(options.Endpoint);
                        await CraneReader.RunAsync(client, options, ct).ConfigureAwait(false);
                    }
                    finally
                    {
                        try
                        {
                            using CancellationTokenSource close =
                                TimeProviderExtensions.CreateCancellationTokenSource(
                                    clock, TimeSpan.FromSeconds(10));
                            await session.CloseAsync(close.Token).ConfigureAwait(false);
                        }
                        finally
                        {
                            await session.DisposeAsync().ConfigureAwait(false);
                        }
                        Console.WriteLine(
                            $"SESSION_CLOSED connected={session.Connected} disposed={session.Disposed}");
                    }
                }
                finally
                {
                    using CancellationTokenSource cleanup =
                        TimeProviderExtensions.CreateCancellationTokenSource(
                            clock, TimeSpan.FromSeconds(20));
                    await host.StopAsync(cleanup.Token).ConfigureAwait(false);
                }
            }
            Console.WriteLine("CLIENT_COMPLETE: session and application host disposed.");
        }
    }

    /// <summary>
    /// Source-generated client lifecycle and trust-policy diagnostics.
    /// </summary>
    internal static partial class ProgramLog
    {
        /// <summary>
        /// Makes the explicit loopback-only certificate-trust exception visible.
        /// </summary>
        [LoggerMessage(EventId = CranesHoistsClientEventIds.Program, Level = LogLevel.Warning,
            Message = "LOCAL DEMO ONLY: accepting unknown server certificates; SignAndEncrypt remains required.")]
        public static partial void LocalClientDemoTrustEnabled(this ILogger logger);

        /// <summary>
        /// Records an established connection rather than a requested connection.
        /// </summary>
        [LoggerMessage(EventId = CranesHoistsClientEventIds.Program + 1, Level = LogLevel.Information,
            Message = "Managed crane session connected to {Endpoint}.")]
        public static partial void CraneClientConnected(this ILogger logger, string endpoint);
    }
}
