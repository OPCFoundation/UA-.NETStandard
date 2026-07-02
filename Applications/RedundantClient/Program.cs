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
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Client;

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
            var replicasOption = new Option<int>("--replicas")
            {
                Description = "Run an in-process client replica set of this size (leader holds the session).",
                DefaultValueFactory = _ => 1
            };
            var suiteOption = new Option<bool>("--suite")
            {
                Description = "Run a browse/read/subscribe workload against the redundant session."
            };
            var standbyOption = new Option<ClientStandbyMode>("--standby")
            {
                Description = "Standby mode for the client replica set: Cold, Warm, or Hot.",
                DefaultValueFactory = _ => ClientStandbyMode.Cold
            };

            var rootCommand = new RootCommand(
                "OPC UA managed client sample that transparently handles server redundancy")
            {
                serverOption,
                noSecurityOption,
                autoAcceptOption,
                durationOption,
                replicasOption,
                suiteOption,
                standbyOption
            };

            rootCommand.SetAction(async (parseResult, cancellationToken) => await RunAsync(
                    parseResult.GetValue(serverOption)!,
                    parseResult.GetValue(noSecurityOption),
                    parseResult.GetValue(autoAcceptOption),
                    parseResult.GetValue(durationOption),
                    parseResult.GetValue(replicasOption),
                    parseResult.GetValue(suiteOption),
                    parseResult.GetValue(standbyOption),
                    cancellationToken).ConfigureAwait(false));

            ParseResult parseResult = rootCommand.Parse(args);
            return parseResult.InvokeAsync(new InvocationConfiguration(), CancellationToken.None);
        }

        private static async Task RunAsync(
            string serverUrl,
            bool noSecurity,
            bool autoAccept,
            TimeSpan duration,
            int replicas,
            bool suite,
            ClientStandbyMode standby,
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

                if (replicas > 1)
                {
                    await RunReplicaSetAsync(
                        configuration, endpoint, telemetry, replicas, standby, suite, duration, ct).ConfigureAwait(false);
                    return;
                }

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

            // Also monitor the replicated "Counter" value from the HA sample node
            // manager. The active replica increments it and mirrors it to the
            // standbys, so its value continues across a failover - a visible
            // demonstration of distributed address-space state replication.
            int haNamespaceIndex = session.NamespaceUris.GetIndex(
                "http://opcfoundation.org/UA/Samples/HighAvailability");
            if (haNamespaceIndex >= 0)
            {
                var replicatedCounter = new MonitoredItem(subscription.DefaultItem)
                {
                    StartNodeId = new NodeId("Counter", (ushort)haNamespaceIndex),
                    AttributeId = Attributes.Value,
                    DisplayName = "HighAvailability.Counter",
                    SamplingInterval = 1000,
                    QueueSize = 10,
                    DiscardOldest = true
                };
                subscription.AddItem(replicatedCounter);
            }

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
                MonitoredItem? source = subscription.FindItemByClientHandle(item.ClientHandle);
                Console.WriteLine(
                    "{0}={1} Status={2}",
                    source?.DisplayName ?? "Value",
                    item.Value.WrappedValue,
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

        private static async Task RunReplicaSetAsync(
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint,
            ITelemetryContext telemetry,
            int replicas,
            ClientStandbyMode standby,
            bool suite,
            TimeSpan duration,
            CancellationToken ct)
        {
            // Local, in-process demo/testing only. This runs a single-process replica set over an
            // in-memory store + lease election (one replica leads, others stand by). The in-memory
            // store cannot coordinate across processes, so it is not a real deployment: for
            // multi-process client redundancy either run independent managed clients that each fail
            // over on their own (scale/docker-compose.yml with --scale client=N), or use a
            // coordinated single-active replica set backed by a CAS-capable Raft client store
            // (AddRedundantClientSession + AddRaftClientSharedStore; see Docs/HighAvailability.md).
            using var store = new InMemorySharedKeyValueStore();
            var sessions = new List<RedundantClientSession>();
            try
            {
                for (int i = 0; i < replicas; i++)
                {
                    string nodeId = $"replica-{i + 1}";
                    // Ownership of the election transfers to the coordinator, which disposes it.
#pragma warning disable CA2000
                    var election = new SharedStoreLeaseElection(
                        store, "client-replica/leader", nodeId,
                        TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(5), TimeProvider.System);
#pragma warning restore CA2000
                    RedundantClientSession session = new RedundantClientSessionBuilder(telemetry)
                        .WithNodeId(nodeId)
                        .WithStandbyMode(standby)
                        .UseSession(token => new ValueTask<ManagedSession>(
                            new ManagedSessionBuilder(configuration, telemetry)
                                .UseEndpoint(endpoint)
                                .WithSessionName(nodeId)
                                .WithUserIdentity(new UserIdentity())
                                .ConnectAsync(token)))
                        .UseRedundancy(election, store, NullRecordProtector.Instance)
                        .Build();
                    session.RoleChanged += isLeader =>
                        Console.WriteLine("{0} is now {1}", nodeId, isLeader ? "LEADER" : "follower");
                    sessions.Add(session);
                    await session.StartAsync(ct).ConfigureAwait(false);
                }

                Console.WriteLine(
                    "Client replica set of {0} started ({1} standby); each replica exposes a transparent ISession facade.",
                    replicas,
                    standby);
                if (suite)
                {
                    await RunSuiteThroughLeaderAsync(sessions, ct).ConfigureAwait(false);
                }
                await RunForDurationAsync(duration, ct).ConfigureAwait(false);
            }
            finally
            {
                foreach (RedundantClientSession session in sessions)
                {
                    await session.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        private static async Task RunSuiteThroughLeaderAsync(
            List<RedundantClientSession> sessions,
            CancellationToken ct)
        {
            // Wait for whichever replica wins leadership, then run the suite through its
            // transparent ISession facade (follower facades stay blocked until promoted).
            var leadershipTasks = new List<Task>(sessions.Count);
            foreach (RedundantClientSession session in sessions)
            {
                leadershipTasks.Add(session.WaitForLeadershipAsync(ct));
            }
            await Task.WhenAny(leadershipTasks).ConfigureAwait(false);

            RedundantClientSession? leader = null;
            for (int i = 0; i < sessions.Count; i++)
            {
                if (leadershipTasks[i].IsCompletedSuccessfully)
                {
                    leader = sessions[i];
                    break;
                }
            }
            if (leader == null)
            {
                return;
            }

            Console.WriteLine("Running the client suite through the leader replica...");
            await RunClientSuiteAsync(leader, ct).ConfigureAwait(false);
        }

        private const string kApplicationName = "RedundantClient";
        private const string kConfigSectionName = "RedundantClient";
    }
}
