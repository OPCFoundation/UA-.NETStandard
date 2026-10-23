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
using Opc.Ua.GMS.Client;

namespace GmsSample.Client
{
    /// <summary>
    /// Runs passive typed GMS workflows over an injected, encrypted managed session.
    /// </summary>
    internal static class Program
    {
        /// <summary>
        /// Validates consent before connecting and distinguishes errors from delivered cancellation.
        /// </summary>
        private static async Task<int> Main(string[] args)
        {
            if (Array.IndexOf(args, "--help") >= 0 || Array.IndexOf(args, "-h") >= 0)
            {
                GmsClientArguments.PrintHelp();
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
                await RunAsync(GmsClientArguments.Parse(ArrayOf.Wrapped(args)), cancelled.Token).ConfigureAwait(false);
                return 0;
            }
            catch (OperationCanceledException) when (cancelled.IsCancellationRequested)
            {
                Console.WriteLine("CANCELLED: subscriptions, result handles, session and host disposed.");
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
        /// Uses the existing GMS client factory and lets its owning facade close the managed session.
        /// </summary>
        private static async Task RunAsync(GmsClientArguments options, CancellationToken cancellationToken)
        {
#if NET8_0_OR_GREATER
            bool dynamicCode = System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported;
            Console.WriteLine($"RUNTIME dynamicCodeSupported={dynamicCode}");
#endif
            TimeProvider clock = TimeProvider.System;
            using CancellationTokenSource deadline =
                TimeProviderExtensions.CreateCancellationTokenSource(clock, TimeSpan.FromSeconds(120));
            using var running = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
            CancellationToken ct = running.Token;
            HostApplicationBuilder builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            builder.Logging.EnableOpcUaRedaction();
            builder.Logging.AddConsole();
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
            builder.Logging.AddFilter("GmsSample", LogLevel.Information);
            builder.Services.AddSingleton(clock);
            builder.Services.AddOpcUa()
                .AddClient(client =>
                {
                    client.ApplicationName = "GmsClient";
                    client.ApplicationUri = "urn:localhost:OPCFoundation:GmsClient";
                    client.ProductUri = "uri:opcfoundation.org:GmsClient";
                    client.PkiRoot = options.PkiRoot;
                    client.AutoAcceptUntrustedCertificates = options.DemoTrust;
                    client.Session = new ManagedSessionOptions
                    {
                        SessionName = "GmsClient",
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
                    discovery.TransportProfileUri = Profiles.UaTcpTransport;
                })
                .AddGmsClient(new GmsClientOptions
                {
                    MaximumElements = 64,
                    MaximumReferences = 4096,
                    MaximumPayloadBytes = 128 * 1024
                });
            IHost host = builder.Build();
            var asynchronousHost = (IAsyncDisposable)host;
            await using (asynchronousHost.ConfigureAwait(false))
            {
                ILogger logger = host.Services.GetRequiredService<ITelemetryContext>()
                    .CreateLogger<GmsClientArguments>();
                if (options.DemoTrust)
                {
                    logger.LocalClientDemoTrustEnabled();
                }
                try
                {
                    await host.StartAsync(ct).ConfigureAwait(false);
                    GmsClient client = await host.Services.GetRequiredService<IGmsClientFactory>()
                        .CreateAsync(ct).ConfigureAwait(false);
                    try
                    {
                        await using (client.ConfigureAwait(false))
                        {
                            Console.WriteLine(
                                $"CONNECTED security={client.Session.ConfiguredEndpoint.Description.SecurityMode} " +
                                $"policy={client.Session.ConfiguredEndpoint.Description.SecurityPolicyUri}");
                            await GmsReader.RunAsync(client, options, ct).ConfigureAwait(false);
                        }
                    }
                    finally
                    {
                        Console.WriteLine(
                            $"SESSION_CLOSED connected={client.Session.Connected} disposed={client.Session.Disposed}");
                    }
                }
                finally
                {
                    using CancellationTokenSource cleanup =
                        TimeProviderExtensions.CreateCancellationTokenSource(clock, TimeSpan.FromSeconds(20));
                    await host.StopAsync(cleanup.Token).ConfigureAwait(false);
                }
            }
            Console.WriteLine("CLIENT_COMPLETE: owning typed facade, managed session and application host disposed.");
        }
    }

    /// <summary>
    /// Source-generated diagnostics for the explicit local trust exception.
    /// </summary>
    internal static partial class ProgramLog
    {
        /// <summary>
        /// Records that encryption and domain validation remain required.
        /// </summary>
        [LoggerMessage(EventId = GmsClientEventIds.Program, Level = LogLevel.Warning,
            Message = "LOCAL DEMO ONLY: accepting unknown server certificates; SignAndEncrypt/domain checks remain.")]
        public static partial void LocalClientDemoTrustEnabled(this ILogger logger);
    }

    /// <summary>
    /// Assigns the sample assembly's source-generated logging range.
    /// </summary>
    internal static class GmsClientEventIds
    {
        /// <summary>The application trust-policy range.</summary>
        public const int Program = 1;
    }
}
