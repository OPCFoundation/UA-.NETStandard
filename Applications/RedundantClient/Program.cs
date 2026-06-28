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

#nullable enable

using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Configuration;

namespace RedundantClient
{
    /// <summary>
    /// Entry point for the managed client sample.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Starts the sample.
        /// </summary>
        public static Task<int> Main(string[] args)
        {
            var serverOption = new Option<string>("--server", "-s")
            {
                Description = "Discovery URL of any server in the (optionally) redundant set.",
                DefaultValueFactory = _ => "opc.tcp://localhost:62543/RedundantServer"
            };
            var noSecurityOption = new Option<bool>("--nosecurity")
            {
                Description = "Select endpoints with MessageSecurityMode.None."
            };
            var autoAcceptOption = new Option<bool>("--autoaccept")
            {
                Description = "Automatically accept untrusted server certificates for sample runs."
            };
            var durationOption = new Option<TimeSpan>("--duration", "-d")
            {
                Description = "How long to monitor before exiting. Use 00:00:00 to run until Ctrl+C.",
                DefaultValueFactory = _ => TimeSpan.FromMinutes(2)
            };

            var rootCommand = new RootCommand(
                "OPC UA managed client sample that transparently handles server redundancy")
            {
                serverOption,
                noSecurityOption,
                autoAcceptOption,
                durationOption
            };

            rootCommand.SetAction(async (parseResult, cancellationToken) =>
            {
                await RunAsync(
                    parseResult.GetValue(serverOption)!,
                    parseResult.GetValue(noSecurityOption),
                    parseResult.GetValue(autoAcceptOption),
                    parseResult.GetValue(durationOption),
                    cancellationToken).ConfigureAwait(false);
            });

            ParseResult parseResult = rootCommand.Parse(args);
            return parseResult.InvokeAsync(new InvocationConfiguration(), CancellationToken.None);
        }

        private static async Task RunAsync(
            string serverUrl,
            bool noSecurity,
            bool autoAccept,
            TimeSpan duration,
            CancellationToken ct)
        {
            ITelemetryContext telemetry = DefaultTelemetry.Create(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Information);
            });
            using IDisposable? telemetryDisposable = telemetry as IDisposable;

            var application = new ApplicationInstance(telemetry)
            {
                ApplicationName = kApplicationName,
                ApplicationType = ApplicationType.Client,
                ConfigSectionName = kConfigSectionName,
                CertificatePasswordProvider = new CertificatePasswordProvider([])
            };

            await using (application.ConfigureAwait(false))
            {
                ApplicationConfiguration configuration = await application
                    .LoadApplicationConfigurationAsync(silent: false, ct: ct)
                    .ConfigureAwait(false);
                if (autoAccept)
                {
                    configuration.CertificateManager.AcceptError = (_, _) => true;
                }

                bool haveCertificate = await application
                    .CheckApplicationInstanceCertificatesAsync(silent: false, ct: ct)
                    .ConfigureAwait(false);
                if (!haveCertificate)
                {
                    throw new InvalidOperationException("Application instance certificate invalid.");
                }

                EndpointDescription selectedEndpoint = await CoreClientUtils
                    .SelectEndpointAsync(configuration, serverUrl, useSecurity: !noSecurity, telemetry, ct)
                    .ConfigureAwait(false)
                    ?? throw new InvalidOperationException(
                        $"No endpoint could be selected for '{serverUrl}'.");
                var endpoint = new ConfiguredEndpoint(
                    null,
                    selectedEndpoint,
                    EndpointConfiguration.Create(configuration));

                Console.WriteLine("Connecting managed client to {0}", serverUrl);

                // A single ManagedSession is the managed client. WithServerRedundancy() lets it
                // discover the redundant set (if any) from the connected server and fail over
                // transparently; against a server that is not configured for redundancy it simply
                // behaves as a resilient reconnecting session. The caller does not need to know the
                // server topology before connecting.
                ManagedSession session = await new ManagedSessionBuilder(configuration, telemetry)
                    .UseEndpoint(endpoint)
                    .WithSessionName(kApplicationName)
                    .WithUserIdentity(new UserIdentity())
                    .WithServerRedundancy()
                    .ConnectAsync(ct)
                    .ConfigureAwait(false);

                await using (session.ConfigureAwait(false))
                {
                    session.ConnectionStateChanged += OnConnectionStateChanged;

                    await LogRedundancyInfoAsync(session, ct).ConfigureAwait(false);
                    await SubscribeToCurrentTimeAsync(session, ct).ConfigureAwait(false);

                    Console.WriteLine("Monitoring ServerStatus.CurrentTime. Press Ctrl+C to stop.");
                    await RunForDurationAsync(duration, ct).ConfigureAwait(false);

                    session.ConnectionStateChanged -= OnConnectionStateChanged;
                }
            }
        }

        private static async Task LogRedundancyInfoAsync(ManagedSession session, CancellationToken ct)
        {
            var handler = new DefaultServerRedundancyHandler();
            ServerRedundancyInfo info = await handler
                .FetchRedundancyInfoAsync(session, ct)
                .ConfigureAwait(false);
            if (info.Mode == RedundancySupport.None)
            {
                Console.WriteLine(
                    "Server is not configured for redundancy (RedundancySupport=None); " +
                    "running as a single resilient session.");
                return;
            }

            Console.WriteLine(
                "Server reports RedundancySupport={0}, ServiceLevel={1} ({2}), CurrentServerId={3}.",
                info.Mode,
                info.ServiceLevel,
                info.ServiceLevelSubrange,
                info.CurrentServerId);
            for (int ii = 0; ii < info.RedundantServers.Count; ii++)
            {
                RedundantServer server = info.RedundantServers[ii];
                Console.WriteLine(
                    "Peer {0}: uri={1}, state={2}, serviceLevel={3}, endpoint={4}",
                    ii + 1,
                    server.ServerUri,
                    server.ServerState,
                    server.ServiceLevel,
                    server.Endpoint?.EndpointUrl?.ToString() ?? "(unresolved)");
            }
        }

        private static async Task SubscribeToCurrentTimeAsync(ManagedSession session, CancellationToken ct)
        {
            // Ownership of the subscription transfers to the session via AddSubscription;
            // the session disposes its subscriptions when it is disposed.
#pragma warning disable CA2000
            var subscription = new Subscription(session.DefaultSubscription)
            {
                DisplayName = "RedundantClient CurrentTime",
                PublishingEnabled = true,
                PublishingInterval = 1000,
                KeepAliveCount = 10,
                LifetimeCount = 0,
                MinLifetimeInterval = 10_000,
                FastDataChangeCallback = OnDataChange
            };
            session.AddSubscription(subscription);
#pragma warning restore CA2000
            await subscription.CreateAsync(ct).ConfigureAwait(false);

            var currentTime = new MonitoredItem(subscription.DefaultItem)
            {
                StartNodeId = VariableIds.Server_ServerStatus_CurrentTime,
                AttributeId = Attributes.Value,
                DisplayName = "ServerStatus.CurrentTime",
                SamplingInterval = 1000,
                QueueSize = 10,
                DiscardOldest = true
            };
            subscription.AddItem(currentTime);
            await subscription.ApplyChangesAsync(ct).ConfigureAwait(false);
        }

        private static void OnDataChange(
            Subscription subscription,
            DataChangeNotification notification,
            ArrayOf<string> stringTable)
        {
            for (int ii = 0; ii < notification.MonitoredItems.Count; ii++)
            {
                MonitoredItemNotification item = notification.MonitoredItems[ii];
                Console.WriteLine(
                    "CurrentTime={0:o} Status={1}",
                    item.Value.GetValue(DateTime.MinValue),
                    item.Value.StatusCode);
            }
        }

        private static void OnConnectionStateChanged(object? sender, ConnectionStateChangedEventArgs e)
        {
            Console.WriteLine("Connection state: {0} -> {1}", e.PreviousState, e.NewState);
        }

        private static async Task RunForDurationAsync(TimeSpan duration, CancellationToken ct)
        {
            try
            {
                await Task.Delay(
                    duration <= TimeSpan.Zero ? Timeout.InfiniteTimeSpan : duration,
                    ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Ctrl+C or the run duration elapsed; exit cleanly.
            }
        }

        private const string kApplicationName = "RedundantClient";
        private const string kConfigSectionName = "RedundantClient";
    }
}
