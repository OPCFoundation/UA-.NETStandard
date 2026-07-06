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
using System.Collections.Generic;
using System.CommandLine;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Redundancy;
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
            var suiteOption = new Option<bool>("--suite")
            {
                Description = "Run a browse/read/subscribe workload against the redundant session."
            };

            var rootCommand = new RootCommand(
                "OPC UA managed client sample that transparently handles server redundancy")
            {
                serverOption,
                noSecurityOption,
                autoAcceptOption,
                durationOption,
                suiteOption
            };

            rootCommand.SetAction(async (parseResult, cancellationToken) => await RunAsync(
                    parseResult.GetValue(serverOption)!,
                    parseResult.GetValue(noSecurityOption),
                    parseResult.GetValue(autoAcceptOption),
                    parseResult.GetValue(durationOption),
                    parseResult.GetValue(suiteOption),
                    cancellationToken).ConfigureAwait(false));

            ParseResult parseResult = rootCommand.Parse(args);
            return parseResult.InvokeAsync(new InvocationConfiguration(), CancellationToken.None);
        }

        private static async Task RunAsync(
            string serverUrl,
            bool noSecurity,
            bool autoAccept,
            TimeSpan duration,
            bool suite,
            CancellationToken ct)
        {
            ITelemetryContext telemetry = DefaultTelemetry.Create(builder => builder.SetMinimumLevel(LogLevel.Information));
            using var telemetryDisposable = telemetry as IDisposable;

            var application = new ApplicationInstance(telemetry)
            {
                ApplicationName = kApplicationName,
                ApplicationType = ApplicationType.Client,
                ConfigSectionName = kConfigSectionName,
                CertificatePasswordProvider = new CertificatePasswordProvider([])
            };

            await using (application.ConfigureAwait(false))
            {
                // Resolve the configuration next to the application binaries (the
                // file is copied to the output directory) so the sample runs from
                // any working directory, e.g. `dotnet run --project ...` invoked
                // from the repository root.
                string configFilePath = System.IO.Path.Combine(
                    AppContext.BaseDirectory, kConfigSectionName + ".Config.xml");
                ApplicationConfiguration configuration = await application
                    .LoadApplicationConfigurationAsync(configFilePath, silent: false, ct: ct)
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

                // Create a normal managed session and opt it into server redundancy
                // handling — that's it. WithServerRedundancy() lets the session discover
                // the redundant set (if any) from the connected server and fail over
                // transparently; against a server that is not configured for redundancy it
                // simply behaves as a resilient reconnecting session. The caller does not
                // need to know the server topology before connecting.
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
                    if (suite)
                    {
                        await RunClientSuiteAsync(session, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        await SubscribeToCurrentTimeAsync(session, ct).ConfigureAwait(false);
                    }

                    Console.WriteLine("Monitoring ServerStatus.CurrentTime and the replicated HighAvailability.Counter. Press Ctrl+C to stop.");
                    await RunForDurationAsync(duration, ct).ConfigureAwait(false);

                    session.ConnectionStateChanged -= OnConnectionStateChanged;
                }
            }
        }

        private static async Task LogRedundancyInfoAsync(ISession session, CancellationToken ct)
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

        private static async Task RunClientSuiteAsync<TSession>(TSession session, CancellationToken ct)
            where TSession : ISession
        {
            // A compact browse / read / subscribe workload against the redundant session. It
            // mirrors the Applications/ConsoleReferenceClient ClientSamples suite but is kept
            // inline so this sample stays self-contained and NativeAOT-publishable.
            Console.WriteLine("Suite: browsing the Objects folder...");
            var browseDescription = new BrowseDescription
            {
                NodeId = ObjectIds.ObjectsFolder,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true,
                ResultMask = (uint)BrowseResultMask.All
            };
            BrowseDescription[] nodesToBrowse = [browseDescription];
            BrowseResponse browseResponse = await session
                .BrowseAsync(null, null, 0u, nodesToBrowse, ct)
                .ConfigureAwait(false);
            if (browseResponse.Results.Count > 0)
            {
                var references = browseResponse.Results[0].References;
                for (int ii = 0; ii < references.Count; ii++)
                {
                    Console.WriteLine("  {0} ({1})", references[ii].DisplayName, references[ii].NodeClass);
                }
            }

            Console.WriteLine("Suite: reading server status nodes...");
            ReadValueId[] nodesToRead =
            [
                new ReadValueId { NodeId = VariableIds.Server_ServerStatus_State, AttributeId = Attributes.Value },
                new ReadValueId { NodeId = VariableIds.Server_NamespaceArray, AttributeId = Attributes.Value },
                new ReadValueId { NodeId = VariableIds.Server_ServerStatus_CurrentTime, AttributeId = Attributes.Value }
            ];
            ReadResponse readResponse = await session
                .ReadAsync(null, 0, TimestampsToReturn.Both, nodesToRead, ct)
                .ConfigureAwait(false);
            for (int ii = 0; ii < readResponse.Results.Count; ii++)
            {
                Console.WriteLine("  {0} = {1}", nodesToRead[ii].NodeId, readResponse.Results[ii].WrappedValue);
            }

            Console.WriteLine("Suite: subscribing to data changes...");
            await SubscribeToCurrentTimeAsync(session, ct).ConfigureAwait(false);
        }

        private static async Task SubscribeToCurrentTimeAsync<TSession>(TSession session, CancellationToken ct)
            where TSession : ISession
        {
            // The managed session uses the V2 subscription engine, which delivers
            // notifications through an ISubscriptionNotificationHandler registered
            // with the session's subscription manager. The classic
            // Subscription.FastDataChangeCallback delegate is NOT invoked by the V2
            // engine, so register a handler here to log the data changes.
            if (!session.TryGetSubscriptionManager(
                out Opc.Ua.Client.Subscriptions.ISubscriptionManager? manager))
            {
                Console.WriteLine(
                    "Session does not expose the V2 subscription manager; cannot monitor.");
                return;
            }

            Opc.Ua.Client.Subscriptions.ISubscription subscription = manager.Add(
                new MonitoringHandler(),
                new OptionsMonitor<Opc.Ua.Client.Subscriptions.SubscriptionOptions>(
                    new Opc.Ua.Client.Subscriptions.SubscriptionOptions
                    {
                        PublishingInterval = TimeSpan.FromSeconds(1),
                        PublishingEnabled = true,
                        KeepAliveCount = 10,
                        LifetimeCount = 100
                    }));

            subscription.TryAddMonitoredItem(
                "ServerStatus.CurrentTime",
                VariableIds.Server_ServerStatus_CurrentTime,
                o => o with
                {
                    SamplingInterval = TimeSpan.FromSeconds(1),
                    QueueSize = 10,
                    DiscardOldest = true
                },
                out _);

            // Also monitor the replicated "Counter" value from the HA sample node
            // manager. The active replica increments it and mirrors it to the
            // standbys, so its value continues across a failover - a visible
            // demonstration of distributed address-space state replication.
            int haNamespaceIndex = session.NamespaceUris.GetIndex(
                "http://opcfoundation.org/UA/Samples/HighAvailability");
            if (haNamespaceIndex >= 0)
            {
                subscription.TryAddMonitoredItem(
                    "HighAvailability.Counter",
                    new NodeId("Counter", (ushort)haNamespaceIndex),
                    o => o with
                    {
                        SamplingInterval = TimeSpan.FromSeconds(1),
                        QueueSize = 10,
                        DiscardOldest = true
                    },
                    out _);
            }

            // The V2 engine creates the subscription and its monitored items on the
            // server asynchronously; wait briefly so monitoring is active by the
            // time this method returns.
            for (int i = 0; i < 100 && !subscription.Created; i++)
            {
                await Task.Delay(50, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// V2 subscription notification handler that logs data changes for the
        /// monitored CurrentTime and replicated Counter values.
        /// </summary>
        private sealed class MonitoringHandler : Opc.Ua.Client.Subscriptions.ISubscriptionNotificationHandler
        {
            public ValueTask OnDataChangeNotificationAsync(
                Opc.Ua.Client.Subscriptions.ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                ReadOnlyMemory<Opc.Ua.Client.Subscriptions.DataValueChange> notification,
                Opc.Ua.Client.Subscriptions.PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
            {
                ReadOnlySpan<Opc.Ua.Client.Subscriptions.DataValueChange> changes = notification.Span;
                for (int ii = 0; ii < changes.Length; ii++)
                {
                    Opc.Ua.Client.Subscriptions.DataValueChange change = changes[ii];
                    Console.WriteLine(
                        "{0}={1} Status={2}",
                        change.MonitoredItem?.Name ?? "Value",
                        change.Value.WrappedValue,
                        change.Value.StatusCode);
                }

                return default;
            }

            public ValueTask OnEventDataNotificationAsync(
                Opc.Ua.Client.Subscriptions.ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                ReadOnlyMemory<Opc.Ua.Client.Subscriptions.EventNotification> notification,
                Opc.Ua.Client.Subscriptions.PublishState publishStateMask,
                IReadOnlyList<string> stringTable)
            {
                return default;
            }

            public ValueTask OnKeepAliveNotificationAsync(
                Opc.Ua.Client.Subscriptions.ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                Opc.Ua.Client.Subscriptions.PublishState publishStateMask)
            {
                return default;
            }

            public ValueTask OnSubscriptionStateChangedAsync(
                Opc.Ua.Client.Subscriptions.ISubscription subscription,
                Opc.Ua.Client.Subscriptions.SubscriptionState state,
                Opc.Ua.Client.Subscriptions.PublishState publishStateMask,
                CancellationToken ct = default)
            {
                return default;
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
