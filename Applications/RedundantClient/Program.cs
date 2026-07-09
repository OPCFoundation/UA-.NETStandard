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
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Crdt;
using Crdt.Transport;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.Redundancy;
using Opc.Ua.Configuration;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Client;
using Raft;
using Raft.Configuration;
using Raft.Storage;
using Raft.Transport.NanoMsg;

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

                // Wait for a reachable endpoint rather than failing if the server set is not
                // up yet - the client and server containers start independently (no compose
                // depends_on across the HA matrix), so the client tolerates a lagging server.
                EndpointDescription selectedEndpoint = await SelectEndpointWithRetryAsync(
                        configuration, serverUrl, useSecurity: !noSecurity, telemetry, ct)
                    .ConfigureAwait(false);
                var endpoint = new ConfiguredEndpoint(
                    null,
                    selectedEndpoint,
                    EndpointConfiguration.Create(configuration));

                // A coordinated client replica set (CLIENT_MODE=eventual|strong) elects one
                // active client that holds the session and shares its session secrets; the
                // others stand by and take over on active-client loss. The default
                // (CLIENT_MODE=independent) is a plain managed client that fails over on its own.
                string clientMode = (Environment.GetEnvironmentVariable("CLIENT_MODE") ?? "independent")
                    .Trim().ToLowerInvariant();
                if (clientMode is "eventual" or "strong")
                {
                    await RunCoordinatedClientAsync(
                            clientMode, configuration, endpoint, telemetry, serverUrl, suite, duration, ct)
                        .ConfigureAwait(false);
                    return;
                }

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
                    var haMonitor = new HaMonitor();
                    void OnConnState(object? s, ConnectionStateChangedEventArgs e)
                        => haMonitor.OnConnectionStateChanged(
                            e, session.ConfiguredEndpoint?.EndpointUrl?.ToString());
                    session.ConnectionStateChanged += OnConnState;

                    await LogRedundancyInfoAsync(session, ct).ConfigureAwait(false);
                    if (suite)
                    {
                        await RunClientSuiteAsync(session, haMonitor, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        await SubscribeToCurrentTimeAsync(session, haMonitor, ct).ConfigureAwait(false);
                    }

                    Console.WriteLine(
                        "Monitoring ServerStatus.CurrentTime and the replicated HighAvailability.Counter / " +
                        "ActiveReplica. Failover and data-loss events are logged as they happen. " +
                        "Press Ctrl+C to stop.");
                    await RunForDurationAsync(duration, ct).ConfigureAwait(false);

                    session.ConnectionStateChanged -= OnConnState;
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

        private static async Task RunCoordinatedClientAsync(
            string mode,
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint,
            ITelemetryContext telemetry,
            string serverUrl,
            bool suite,
            TimeSpan duration,
            CancellationToken ct)
        {
            string nodeId = Environment.GetEnvironmentVariable("CLIENT_NODE_ID") ?? Dns.GetHostName();
            bool strong = string.Equals(mode, "strong", StringComparison.Ordinal);
            var haMonitor = new HaMonitor();

            // Elect the active client with a real Raft quorum among the client replicas - the
            // same building blocks the server uses, mirrored on the client side. A coordinated
            // set shares the leader's protected session secrets through a networked store, so it
            // fails closed without a record protector (see CreateClientRecordProtector).
            DefaultRaftConsensus consensus = BuildClientRaftCluster();
            AesCbcHmacRecordProtector protector = CreateClientRecordProtector();
            ISharedKeyValueStore store = CreateClientSharedStore(strong, nodeId, consensus);
            // Ownership of the election transfers to the RedundantClientSession's coordinator,
            // which disposes it in DisposeAsync (see ClientReplicaCoordinator).
#pragma warning disable CA2000
            var election = new RaftLeaderElection(consensus, telemetry.CreateLogger<RaftLeaderElection>());
#pragma warning restore CA2000

            try
            {
                RedundantClientSession session = new RedundantClientSessionBuilder(telemetry)
                    .WithNodeId(nodeId)
                    .WithStandbyMode(ClientStandbyMode.Cold)
                    .UseSession(token => ConnectLeaderSessionAsync(configuration, endpoint, telemetry, token))
                    .ConfigureLeader(async (leaderSession, fastActivated, cfgCt) =>
                    {
                        Console.WriteLine(
                            "ACTIVE CLIENT: replica '{0}' is now the active client; establishing monitoring.",
                            nodeId);
                        await LogRedundancyInfoAsync(leaderSession, cfgCt).ConfigureAwait(false);
                        if (suite)
                        {
                            await RunClientSuiteAsync(leaderSession, haMonitor, cfgCt).ConfigureAwait(false);
                        }
                        else
                        {
                            await SubscribeToCurrentTimeAsync(leaderSession, haMonitor, cfgCt)
                                .ConfigureAwait(false);
                        }
                    })
                    .UseRedundancy(election, store, protector)
                    .Build();

                await using (session.ConfigureAwait(false))
                {
                    session.RoleChanged += haMonitor.OnRoleChanged;
                    await session.StartAsync(ct).ConfigureAwait(false);
                    Console.WriteLine(
                        "Coordinated client replica '{0}' started ({1} shared store) against {2}. Exactly one " +
                        "replica is active; on active-client loss a standby is promoted and resumes monitoring. " +
                        "Failover and data-loss events are logged as they happen. Press Ctrl+C to stop.",
                        nodeId, mode, serverUrl);
                    await RunForDurationAsync(duration, ct).ConfigureAwait(false);
                    session.RoleChanged -= haMonitor.OnRoleChanged;
                }
            }
            finally
            {
                // The coordinator disposes the election on session dispose; dispose the shared
                // store, the record protector, and the Raft consensus here (the stores hold the
                // consensus with ownsConsensus:false).
                if (store is IAsyncDisposable disposableStore)
                {
                    await disposableStore.DisposeAsync().ConfigureAwait(false);
                }
                protector.Dispose();
                await consensus.DisposeAsync().ConfigureAwait(false);
            }
        }

        private static async ValueTask<ManagedSession> ConnectLeaderSessionAsync(
            ApplicationConfiguration configuration,
            ConfiguredEndpoint endpoint,
            ITelemetryContext telemetry,
            CancellationToken ct)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    return await new ManagedSessionBuilder(configuration, telemetry)
                        .UseEndpoint(endpoint)
                        .WithSessionName(kApplicationName)
                        .WithUserIdentity(new UserIdentity())
                        .WithServerRedundancy()
                        .ConnectAsync(ct)
                        .ConfigureAwait(false);
                }
                catch (Exception ex) when (attempt < 30 && !ct.IsCancellationRequested)
                {
                    Console.WriteLine(
                        "Active client connect attempt {0} failed ({1}); retrying...", attempt, ex.Message);
                    await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
                }
            }
        }

        private static ISharedKeyValueStore CreateClientSharedStore(
            bool strong, string nodeId, IRaftConsensus consensus)
        {
            // CA2000: ownership of the stores created here transfers to the returned store (the
            // Hybrid owns its inner stores via ownsStores:true) and to the caller, which disposes
            // it. The shared Raft consensus is NOT owned by these stores (ownsConsensus:false); the
            // caller disposes it after the store.
#pragma warning disable CA2000
            var raftStore = new RaftSharedKeyValueStore(consensus, ownsConsensus: false);
            if (strong)
            {
                return raftStore;
            }

            (IPAddress address, int port, List<IPEndPoint> peers) = ReadClientGossip();
            var gossip = new TcpGossipTransport(new TcpGossipTransportOptions { Address = address, Port = port });
            gossip.AddPeers(peers);
            var replicated = new ReplicatedSharedKeyValueStore(
                ReplicaIdFromNodeId(nodeId), gossip, TimeProvider.System, CrdtReaderOptions.Default);
            return new HybridSharedKeyValueStore(replicated, raftStore, default, ownsStores: true);
#pragma warning restore CA2000
        }

        private static AesCbcHmacRecordProtector CreateClientRecordProtector()
        {
            // A coordinated client set mirrors the leader's session secrets through a networked
            // store, so ClientReplicaCoordinator fails closed on a NullRecordProtector. Use the
            // shared CLIENT_RECORD_KEY when supplied; otherwise, for an explicit isolated demo
            // (CLIENT_INSECURE=true), derive a well-known NON-SECRET demo key so all replicas
            // agree - never do this in production.
            string? recordKeyBase64 = Environment.GetEnvironmentVariable("CLIENT_RECORD_KEY");
            if (!string.IsNullOrWhiteSpace(recordKeyBase64))
            {
                return new AesCbcHmacRecordProtector(Convert.FromBase64String(recordKeyBase64));
            }

            bool insecure = bool.TryParse(
                Environment.GetEnvironmentVariable("CLIENT_INSECURE"), out bool value) &&
                value;
            if (insecure)
            {
                Console.Error.WriteLine(
                    "[HA][WARNING] CLIENT_INSECURE=true: protecting mirrored client session secrets with a " +
                    "well-known, NON-SECRET demo key derived from a constant. Use only for an isolated demo; " +
                    "set CLIENT_RECORD_KEY to a shared base64 32-byte key in production.");
                byte[] demoKey = SHA256.HashData(
                    Encoding.UTF8.GetBytes("OPCFoundation/RedundantClient/insecure-demo/record-key"));
                return new AesCbcHmacRecordProtector(demoKey);
            }

            throw new InvalidOperationException(
                "A coordinated client set mirrors session secrets through a networked store and requires a " +
                "record protector. Set CLIENT_RECORD_KEY to a shared base64 32-byte key (the same value on " +
                "every client replica) to encrypt mirrored secrets, or set CLIENT_INSECURE=true to run this " +
                "isolated demo without a real key.");
        }

        private static DefaultRaftConsensus BuildClientRaftCluster()
        {
            ulong raftId = ulong.TryParse(
                Environment.GetEnvironmentVariable("CLIENT_RAFT_ID"), out ulong id) ? id : 1;
            List<string> peers = ReadEnvList("CLIENT_RAFT_PEERS");
            int members = int.TryParse(
                Environment.GetEnvironmentVariable("CLIENT_RAFT_MEMBERS"), out int m) ? m : peers.Count + 1;
            string bind = Environment.GetEnvironmentVariable("CLIENT_RAFT_BIND") ?? "tcp://0.0.0.0:6561";

            var memberIds = new List<ulong>(members);
            for (int i = 1; i <= members; i++)
            {
                memberIds.Add((ulong)i);
            }

            var transportOptions = new NanoMsgBusTransportOptions { BindAddress = bind };
            foreach (string peer in peers)
            {
                transportOptions.Peers.Add(peer);
            }

            // The DefaultRaftConsensus adapter owns the node (which disposes the transport);
            // MemoryStorage is volatile, so a restarted replica re-syncs from the leader.
#pragma warning disable CA2000
            var transport = new NanoMsgBusTransport(transportOptions);
            var storage = new MemoryStorage(new ConfState(memberIds));
            return DefaultRaftConsensus.CreateCluster(
                raftId,
                transport,
                storage,
                new RaftNodeOptions { TickInterval = TimeSpan.FromMilliseconds(50) },
                config =>
                {
                    config.ElectionTick = 10;
                    config.PreVote = true;
                    config.CheckQuorum = true;
                },
                TimeSpan.FromSeconds(30));
#pragma warning restore CA2000
        }

        private static (IPAddress Address, int Port, List<IPEndPoint> Peers) ReadClientGossip()
        {
            int port = int.TryParse(
                Environment.GetEnvironmentVariable("CLIENT_GOSSIP_PORT"), out int p) ? p : 4841;
            var peers = new List<IPEndPoint>();
            foreach (string peer in ReadEnvList("CLIENT_GOSSIP_PEERS"))
            {
                peers.Add(ParseGossipEndpoint(peer));
            }
            return (IPAddress.Any, port, peers);
        }

        private static IPEndPoint ParseGossipEndpoint(string hostPort)
        {
            int separator = hostPort.LastIndexOf(':');
            if (separator <= 0 || separator == hostPort.Length - 1)
            {
                throw new FormatException($"Invalid gossip endpoint '{hostPort}'; expected host:port.");
            }

            string host = hostPort[..separator];
            int port = int.Parse(hostPort[(separator + 1)..], CultureInfo.InvariantCulture);
            IPAddress address = IPAddress.TryParse(host, out IPAddress? ip)
                ? ip
                : Dns.GetHostAddresses(host)[0];
            return new IPEndPoint(address, port);
        }

        private static ReplicaId ReplicaIdFromNodeId(string nodeId)
        {
            // Derive a stable replica identity from the node id so it survives restarts.
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(nodeId));
            return new ReplicaId(new Guid(hash.AsSpan(0, 16).ToArray()));
        }

        private static List<string> ReadEnvList(string key)
        {
            var items = new List<string>();
            string? value = Environment.GetEnvironmentVariable(key);
            if (string.IsNullOrWhiteSpace(value))
            {
                return items;
            }

            items.AddRange(value.Split(
                [',', ';'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

            return items;
        }

        private static async Task<EndpointDescription> SelectEndpointWithRetryAsync(
            ApplicationConfiguration configuration,
            string serverUrl,
            bool useSecurity,
            ITelemetryContext telemetry,
            CancellationToken ct)
        {
            const int maxAttempts = 60;
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    EndpointDescription? selected = await CoreClientUtils
                        .SelectEndpointAsync(configuration, serverUrl, useSecurity, telemetry, ct)
                        .ConfigureAwait(false);
                    if (selected != null)
                    {
                        return selected;
                    }
                }
                catch (Exception ex) when (attempt < maxAttempts && !ct.IsCancellationRequested)
                {
                    Console.WriteLine(
                        "Waiting for server '{0}' (attempt {1}/{2}): {3}",
                        serverUrl, attempt, maxAttempts, ex.Message);
                }

                if (attempt >= maxAttempts)
                {
                    throw new InvalidOperationException($"No endpoint could be selected for '{serverUrl}'.");
                }

                await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
            }
        }

        private static async Task RunClientSuiteAsync<TSession>(
            TSession session, HaMonitor monitor, CancellationToken ct)
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
                ArrayOf<ReferenceDescription> references = browseResponse.Results[0].References;
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
            await SubscribeToCurrentTimeAsync(session, monitor, ct).ConfigureAwait(false);
        }

        private static async Task SubscribeToCurrentTimeAsync<TSession>(
            TSession session, HaMonitor monitor, CancellationToken ct)
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
                new MonitoringHandler(monitor),
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

                // Monitor which replica is currently serving this session. In
                // active/active every replica writes its own node id here, so the
                // value the client sees identifies the connected replica; when it
                // changes, the session has failed over to a different server.
                subscription.TryAddMonitoredItem(
                    "HighAvailability.ActiveReplica",
                    new NodeId("ActiveReplica", (ushort)haNamespaceIndex),
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
        /// monitored CurrentTime and replicated Counter values and forwards each
        /// change to the <see cref="HaMonitor"/> for failover / data-loss analysis.
        /// </summary>
        private sealed class MonitoringHandler : Opc.Ua.Client.Subscriptions.ISubscriptionNotificationHandler
        {
            public MonitoringHandler(HaMonitor monitor)
            {
                m_monitor = monitor;
            }

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
                    string name = change.MonitoredItem?.Name ?? "Value";
                    Console.WriteLine(
                        "{0}={1} Status={2}",
                        name,
                        change.Value.WrappedValue,
                        change.Value.StatusCode);
                    m_monitor.Observe(name, change.Value);
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

            private readonly HaMonitor m_monitor;
        }

        /// <summary>
        /// Tracks the monitored high-availability values across reconnects and
        /// failovers and logs the failover and data-loss events they reveal:
        /// gaps in <c>ServerStatus.CurrentTime</c> (missed updates), regressions or
        /// divergence of the replicated <c>Counter</c> (state that did not carry
        /// over), continuity of the <c>Counter</c> (no data loss), and changes of
        /// <c>ActiveReplica</c> (the replica now serving the session).
        /// </summary>
        private sealed class HaMonitor
        {
            /// <summary>
            /// Records a connection-state transition and, on (re)connect, arms the
            /// failover context so the next data-change assessment is framed as a
            /// failover.
            /// </summary>
            public void OnConnectionStateChanged(ConnectionStateChangedEventArgs e, string? endpoint)
            {
                Console.WriteLine("Connection state: {0} -> {1}", e.PreviousState, e.NewState);
                if (e.NewState is ConnectionState.Reconnecting or ConnectionState.Failover)
                {
                    Console.WriteLine("FAILOVER: connection lost, selecting a healthy replica...");
                }
                else if (e.NewState == ConnectionState.Connected &&
                    e.PreviousState != ConnectionState.Connected)
                {
                    lock (m_lock)
                    {
                        m_failoverContext = true;
                    }
                    Console.WriteLine("CONNECTED: session (re)connected to {0}.", endpoint ?? "(unknown)");
                }
            }

            /// <summary>
            /// Records a coordinated-client role change. On promotion it arms the failover
            /// context so the next data-change assessment is framed as a (client-side) failover;
            /// on demotion it notes that a peer client took over.
            /// </summary>
            public void OnRoleChanged(bool isLeader)
            {
                if (isLeader)
                {
                    lock (m_lock)
                    {
                        m_failoverContext = true;
                    }
                }
                else
                {
                    Console.WriteLine("STANDBY: this replica is no longer the active client; a peer took over.");
                }
            }

            /// <summary>
            /// Dispatches a monitored value to the matching per-item analysis.
            /// </summary>
            public void Observe(string name, DataValue value)
            {
                switch (name)
                {
                    case "ServerStatus.CurrentTime":
                        if (value.WrappedValue.TryGetValue(out DateTimeUtc serverTime))
                        {
                            OnCurrentTime(serverTime.ToDateTime());
                        }
                        break;
                    case "HighAvailability.Counter":
                        if (value.WrappedValue.TryGetValue(out int counter))
                        {
                            OnCounter(counter);
                        }
                        break;
                    case "HighAvailability.ActiveReplica":
                        if (value.WrappedValue.TryGetValue(out string? replica) && replica != null)
                        {
                            OnActiveReplica(replica);
                        }
                        break;
                }
            }

            private void OnCurrentTime(DateTime serverTime)
            {
                DateTime? last;
                bool failover;
                lock (m_lock)
                {
                    last = m_lastServerTime;
                    m_lastServerTime = serverTime;
                    failover = m_failoverContext;
                }
                if (last.HasValue)
                {
                    TimeSpan gap = serverTime - last.Value;
                    if (gap > kExpectedInterval * 3)
                    {
                        int missed = Math.Max(0, (int)(gap.TotalSeconds / kExpectedInterval.TotalSeconds) - 1);
                        Console.WriteLine(
                            "DATA LOSS: CurrentTime jumped {0:0.0}s ({1} update(s) missed{2}).",
                            gap.TotalSeconds,
                            missed,
                            failover ? " during failover" : string.Empty);
                    }
                }
            }

            private void OnCounter(int value)
            {
                int? last;
                bool failover;
                lock (m_lock)
                {
                    last = m_lastCounter;
                    m_lastCounter = value;
                    failover = m_failoverContext;
                    m_failoverContext = false;
                }
                if (!last.HasValue)
                {
                    return;
                }
                if (value < last.Value)
                {
                    Console.WriteLine(
                        "DATA LOSS: Counter regressed {0} -> {1} ({2} increment(s) lost){3}.",
                        last.Value,
                        value,
                        last.Value - value,
                        failover ? " across failover" : string.Empty);
                }
                else if (failover && value > last.Value + kCounterJumpSlack)
                {
                    Console.WriteLine(
                        "DATA LOSS: Counter jumped {0} -> {1} across failover " +
                        "(replica values diverged; state did not carry over).",
                        last.Value,
                        value);
                }
                else if (failover)
                {
                    Console.WriteLine(
                        "HA OK: Counter continued {0} -> {1} across failover (no data loss).",
                        last.Value,
                        value);
                }
            }

            private void OnActiveReplica(string replica)
            {
                string? previous;
                lock (m_lock)
                {
                    previous = m_lastReplica;
                    m_lastReplica = replica;
                }
                if (previous == null)
                {
                    Console.WriteLine("Connected replica: '{0}'.", replica);
                }
                else if (!string.Equals(previous, replica, StringComparison.Ordinal))
                {
                    Console.WriteLine("FAILOVER: now served by replica '{0}' (was '{1}').", replica, previous);
                }
            }

            private static readonly TimeSpan kExpectedInterval = TimeSpan.FromSeconds(1);
            private const int kCounterJumpSlack = 5;
            private readonly Lock m_lock = new();
            private DateTime? m_lastServerTime;
            private int? m_lastCounter;
            private string? m_lastReplica;
            private bool m_failoverContext;
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
