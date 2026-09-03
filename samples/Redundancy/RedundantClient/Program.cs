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
using Opc.Ua.Client.Historian;
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
        /// <param name="args">The command-line arguments supplied to the sample.</param>
        /// <returns>The process exit code returned by the command-line parser.</returns>
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
            var historyOption = new Option<bool>("--history")
            {
                Description =
                    "Exercise raw, event, and processed history continuations across an active-server failover."
            };
            var historyFailoverDelayOption = new Option<TimeSpan>("--history-failover-delay")
            {
                Description =
                    "How long the history scenario pauses after opening continuations so an active replica can fail.",
                DefaultValueFactory = _ => TimeSpan.FromSeconds(15)
            };

            var rootCommand = new RootCommand(
                "OPC UA managed client sample that transparently handles server redundancy")
            {
                serverOption,
                noSecurityOption,
                autoAcceptOption,
                durationOption,
                suiteOption,
                historyOption,
                historyFailoverDelayOption
            };

            rootCommand.SetAction(async (parseResult, cancellationToken) => await RunAsync(
                    parseResult.GetValue(serverOption)!,
                    parseResult.GetValue(noSecurityOption),
                    parseResult.GetValue(autoAcceptOption),
                    parseResult.GetValue(durationOption),
                    parseResult.GetValue(suiteOption),
                    parseResult.GetValue(historyOption),
                    parseResult.GetValue(historyFailoverDelayOption),
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
            bool history,
            TimeSpan historyFailoverDelay,
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
                    if (history)
                    {
                        throw new InvalidOperationException(
                            "The --history scenario requires CLIENT_MODE=independent so one managed session can " +
                            "retain and resume its HistoryRead continuations across server failover.");
                    }
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
                    .WithReconnectPolicy(options => options with
                    {
                        Strategy = BackoffStrategy.Constant,
                        InitialDelay = TimeSpan.FromMilliseconds(500),
                        MaxDelay = TimeSpan.FromMilliseconds(500),
                        MaxRetries = 3
                    })
                    .WithServerRedundancy()
                    .WithTokenReuseFailover()
                    .ConnectAsync(ct)
                    .ConfigureAwait(false);

                await using (session.ConfigureAwait(false))
                {
                    var haMonitor = new HaMonitor();
                    void OnConnState(object? s, ConnectionStateChangedEventArgs e)
                        => haMonitor.OnConnectionStateChanged(
                            e, session.ConfiguredEndpoint?.EndpointUrl?.ToString());
                    session.ConnectionStateChanged += OnConnState;

                    await LogRedundancyInfoAsync(session, telemetry, ct).ConfigureAwait(false);
                    if (suite)
                    {
                        await RunClientSuiteAsync(session, haMonitor, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        await SubscribeToCurrentTimeAsync(session, haMonitor, ct).ConfigureAwait(false);
                    }

                    if (history)
                    {
                        await RunHistorianFailoverScenarioAsync(
                            session,
                            historyFailoverDelay,
                            ct).ConfigureAwait(false);
                        session.ConnectionStateChanged -= OnConnState;
                        return;
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

        private static async Task LogRedundancyInfoAsync(
            ISession session,
            ITelemetryContext telemetry,
            CancellationToken ct)
        {
            var handler = new DefaultServerRedundancyHandler(
                new DefaultRedundantServerEndpointResolver(telemetry));
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
                        await LogRedundancyInfoAsync(leaderSession, telemetry, cfgCt).ConfigureAwait(false);
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
                        .WithReconnectPolicy(options => options with
                        {
                            Strategy = BackoffStrategy.Constant,
                            InitialDelay = TimeSpan.FromMilliseconds(500),
                            MaxDelay = TimeSpan.FromMilliseconds(500),
                            MaxRetries = 3
                        })
                        .WithServerRedundancy()
                        .WithTokenReuseFailover()
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
            // mirrors the samples/Reference/ConsoleReferenceClient ClientSamples suite but is kept
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

        private static async Task RunHistorianFailoverScenarioAsync(
            ManagedSession session,
            TimeSpan failoverDelay,
            CancellationToken ct)
        {
            if (failoverDelay < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(failoverDelay),
                    "The history failover delay cannot be negative.");
            }

            int namespaceIndex = session.NamespaceUris.GetIndex(kHighAvailabilityNamespaceUri);
            if (namespaceIndex < 0)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNodeIdUnknown,
                    $"The server does not expose '{kHighAvailabilityNamespaceUri}'.");
            }

            var counterNodeId = new NodeId("Counter", (ushort)namespaceIndex);
            var eventNotifierId = new NodeId("HistoryEvents", (ushort)namespaceIndex);
            var client = new HistoryClient(session);
            EventFilter eventFilter = CreateHistoryEventFilter();

            DateTime startTime = DateTime.UtcNow.AddMinutes(-5);
            DateTime endTime = DateTime.UtcNow.AddSeconds(1);
            await WaitForHistoricalDepthAsync(
                client,
                counterNodeId,
                eventNotifierId,
                eventFilter,
                startTime,
                endTime,
                ct).ConfigureAwait(false);
            DateTime visibilityMarkerTime = DateTime.UtcNow.AddMinutes(-4);
            await InsertAndVerifyVisibilityMarkerAsync(
                client,
                counterNodeId,
                visibilityMarkerTime,
                "active",
                ct).ConfigureAwait(false);
            endTime = DateTime.UtcNow.AddSeconds(1);
            startTime = endTime.AddSeconds(-30);
            (
                DateTime processedStartTime,
                DateTime processedEndTime,
                double processingInterval) = await GetProcessedWindowAsync(
                    client,
                    counterNodeId,
                    startTime,
                    endTime,
                    ct).ConfigureAwait(false);
            var expectedProcessingInterval =
                TimeSpan.FromMilliseconds(processingInterval);

            IAsyncEnumerator<DataValue> raw = client.ReadRawAsync(
                counterNodeId,
                startTime,
                endTime,
                maxValuesPerNode: 1,
                timestampsToReturn: TimestampsToReturn.Both,
                cancellationToken: ct).GetAsyncEnumerator(ct);
            IAsyncEnumerator<HistoryEventFieldList> events = client.ReadEventsAsync(
                eventNotifierId,
                startTime,
                endTime,
                eventFilter,
                maxValuesPerNode: 1,
                timestampsToReturn: TimestampsToReturn.Both,
                cancellationToken: ct).GetAsyncEnumerator(ct);
            IAsyncEnumerator<DataValue> processed = client.ReadProcessedAsync(
                counterNodeId,
                ObjectIds.AggregateFunction_Average,
                processedStartTime,
                processedEndTime,
                processingInterval,
                timestampsToReturn: TimestampsToReturn.Both,
                cancellationToken: ct).GetAsyncEnumerator(ct);

            var reconnected = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            int connectionInterrupted = 0;
            void OnConnectionStateChanged(object? sender, ConnectionStateChangedEventArgs args)
            {
                _ = sender;
                if (args.NewState is ConnectionState.Reconnecting or ConnectionState.Failover)
                {
                    Interlocked.Exchange(ref connectionInterrupted, 1);
                }
                else if (args.NewState == ConnectionState.Connected &&
                    Volatile.Read(ref connectionInterrupted) != 0)
                {
                    reconnected.TrySetResult(true);
                }
            }
            session.ConnectionStateChanged += OnConnectionStateChanged;

            try
            {
                await using (raw.ConfigureAwait(false))
                await using (events.ConfigureAwait(false))
                await using (processed.ConfigureAwait(false))
                {
                    if (!await raw.MoveNextAsync().ConfigureAwait(false) ||
                        !await events.MoveNextAsync().ConfigureAwait(false) ||
                        !await processed.MoveNextAsync().ConfigureAwait(false))
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadNoData,
                            "The server did not return the first page for every historical read.");
                    }

                    int rawCount = 0;
                    int eventCount = 0;
                    int processedCount = 0;
                    DateTime lastRawTime = DateTime.MinValue;
                    DateTime lastEventTime = DateTime.MinValue;
                    DateTime lastProcessedTime = DateTime.MinValue;
                    int lastRawCounter = 0;
                    int lastEventCounter = 0;
                    var eventIds = new HashSet<ByteString>();
                    ObserveRawValue(
                        raw.Current,
                        ref lastRawTime,
                        ref lastRawCounter,
                        ref rawCount);
                    ObserveHistoryEvent(
                        events.Current,
                        eventIds,
                        ref lastEventTime,
                        ref lastEventCounter,
                        ref eventCount);
                    ObserveProcessedValue(
                        processed.Current,
                        expectedProcessingInterval,
                        ref lastProcessedTime,
                        ref processedCount);

                    Console.WriteLine(
                        "HISTORY: portable continuations ready (raw, event, processed); " +
                        "remove the active server now.");
                    await Task.Delay(failoverDelay, ct).ConfigureAwait(false);
                    if (Volatile.Read(ref connectionInterrupted) != 0)
                    {
                        await reconnected.Task
                            .WaitAsync(TimeSpan.FromSeconds(90), ct)
                            .ConfigureAwait(false);
                    }

                    while (await raw.MoveNextAsync().ConfigureAwait(false))
                    {
                        ObserveRawValue(
                            raw.Current,
                            ref lastRawTime,
                            ref lastRawCounter,
                            ref rawCount);
                    }
                    while (await events.MoveNextAsync().ConfigureAwait(false))
                    {
                        ObserveHistoryEvent(
                            events.Current,
                            eventIds,
                            ref lastEventTime,
                            ref lastEventCounter,
                            ref eventCount);
                    }
                    while (await processed.MoveNextAsync().ConfigureAwait(false))
                    {
                        ObserveProcessedValue(
                            processed.Current,
                            expectedProcessingInterval,
                            ref lastProcessedTime,
                            ref processedCount);
                    }

                    if (rawCount < 5 || eventCount < 5 || processedCount < 3)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadNoData,
                            $"Insufficient resumed history: raw={rawCount}, event={eventCount}, " +
                            $"processed={processedCount}.");
                    }

                    await WaitForVisibilityMarkerAsync(
                        client,
                        counterNodeId,
                        visibilityMarkerTime,
                        "promoted",
                        ct).ConfigureAwait(false);
                    await WaitForPromotedWriterHistoryAsync(
                        client,
                        counterNodeId,
                        eventNotifierId,
                        eventFilter,
                        lastRawCounter,
                        lastEventCounter,
                        ct).ConfigureAwait(false);

                    Console.WriteLine(
                        "HISTORY HA OK: raw={0}, event={1}, processed={2}; portable continuations resumed " +
                        "without duplicates or gaps.",
                        rawCount,
                        eventCount,
                        processedCount);
                }
            }
            finally
            {
                session.ConnectionStateChanged -= OnConnectionStateChanged;
            }
        }

        private static async Task InsertAndVerifyVisibilityMarkerAsync(
            HistoryClient client,
            NodeId counterNodeId,
            DateTime markerTime,
            string replicaRole,
            CancellationToken ct)
        {
            var marker = new DataValue(
                Variant.From(kHistoryVisibilityMarker),
                StatusCodes.Good,
                markerTime,
                markerTime);
            ArrayOf<StatusCode> statuses = await client.InsertAsync(
                counterNodeId,
                [marker],
                ct).ConfigureAwait(false);
            if (statuses.Count != 1 || StatusCode.IsBad(statuses[0]))
            {
                StatusCode status = statuses.Count == 1
                    ? statuses[0]
                    : StatusCodes.BadUnexpectedError;
                throw new ServiceResultException(
                    status,
                    "The historical visibility marker write was rejected.");
            }

            await VerifyVisibilityMarkerAsync(
                client,
                counterNodeId,
                markerTime,
                replicaRole,
                ct).ConfigureAwait(false);
        }

        private static async Task WaitForVisibilityMarkerAsync(
            HistoryClient client,
            NodeId counterNodeId,
            DateTime markerTime,
            string replicaRole,
            CancellationToken ct)
        {
            ServiceResultException? lastError = null;
            for (int attempt = 0; attempt < 60; attempt++)
            {
                try
                {
                    await VerifyVisibilityMarkerAsync(
                        client,
                        counterNodeId,
                        markerTime,
                        replicaRole,
                        ct).ConfigureAwait(false);
                    return;
                }
                catch (ServiceResultException exception)
                {
                    lastError = exception;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(500), ct).ConfigureAwait(false);
            }

            throw new ServiceResultException(
                lastError?.StatusCode ?? StatusCodes.BadNoData,
                $"The historical visibility marker was not readable from the {replicaRole} replica.");
        }

        private static async Task VerifyVisibilityMarkerAsync(
            HistoryClient client,
            NodeId counterNodeId,
            DateTime markerTime,
            string replicaRole,
            CancellationToken ct)
        {
            int count = 0;
            await foreach (DataValue value in client.ReadRawAsync(
                counterNodeId,
                markerTime.AddMilliseconds(-1),
                markerTime.AddMilliseconds(1),
                maxValuesPerNode: 1,
                cancellationToken: ct).ConfigureAwait(false))
            {
                if (!value.WrappedValue.TryGetValue(out int marker) ||
                    marker != kHistoryVisibilityMarker ||
                    value.SourceTimestamp.ToDateTime() != markerTime)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadUnexpectedError,
                        "The historical visibility marker read returned an unknown value.");
                }
                count++;
            }

            if (count != 1)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNoData,
                    $"Expected one historical visibility marker but received {count}.");
            }

            Console.WriteLine(
                "HISTORY: write/read marker {0} visible on {1} replica.",
                kHistoryVisibilityMarker,
                replicaRole);
        }

        private static async Task WaitForPromotedWriterHistoryAsync(
            HistoryClient client,
            NodeId counterNodeId,
            NodeId eventNotifierId,
            EventFilter eventFilter,
            int previousRawCounter,
            int previousEventCounter,
            CancellationToken ct)
        {
            for (int attempt = 0; attempt < 60; attempt++)
            {
                int newestRawCounter = previousRawCounter;
                int newestEventCounter = previousEventCounter;
                DateTime endTime = DateTime.UtcNow.AddSeconds(1);
                DateTime startTime = endTime.AddSeconds(-30);

                await foreach (DataValue value in client.ReadRawAsync(
                    counterNodeId,
                    startTime,
                    endTime,
                    maxValuesPerNode: 2,
                    cancellationToken: ct).ConfigureAwait(false))
                {
                    if (!StatusCode.IsGood(value.StatusCode) ||
                        !value.WrappedValue.TryGetValue(out int counter))
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadDecodingError,
                            "Promoted-writer raw history returned an unknown value.");
                    }
                    newestRawCounter = Math.Max(newestRawCounter, counter);
                }
                await foreach (HistoryEventFieldList historicalEvent in
                    client.ReadEventsAsync(
                        eventNotifierId,
                        startTime,
                        endTime,
                        eventFilter,
                        maxValuesPerNode: 2,
                        cancellationToken: ct).ConfigureAwait(false))
                {
                    if (!TryGetHistoryEventCounter(
                        historicalEvent,
                        out int counter))
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadDecodingError,
                            "Promoted-writer event history returned an unknown event.");
                    }
                    newestEventCounter = Math.Max(
                        newestEventCounter,
                        counter);
                }

                if (newestRawCounter > previousRawCounter &&
                    newestEventCounter > previousEventCounter)
                {
                    Console.WriteLine(
                        "HISTORY: promoted writer added shared raw and event history " +
                        "(counter {0}, event {1}).",
                        newestRawCounter,
                        newestEventCounter);
                    return;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(500), ct).ConfigureAwait(false);
            }

            throw new ServiceResultException(
                StatusCodes.BadNoData,
                "The promoted replica did not add new raw and event history within 30 seconds.");
        }

        private static async Task WaitForHistoricalDepthAsync(
            HistoryClient client,
            NodeId counterNodeId,
            NodeId eventNotifierId,
            EventFilter eventFilter,
            DateTime startTime,
            DateTime endTime,
            CancellationToken ct)
        {
            for (int attempt = 0; attempt < 60; attempt++)
            {
                int rawCount = await CountAtLeastAsync(
                    client.ReadRawAsync(
                        counterNodeId,
                        startTime,
                        endTime,
                        maxValuesPerNode: 1,
                        cancellationToken: ct),
                    minimum: 5,
                    ct).ConfigureAwait(false);
                int eventCount = await CountAtLeastAsync(
                    client.ReadEventsAsync(
                        eventNotifierId,
                        startTime,
                        endTime,
                        eventFilter,
                        maxValuesPerNode: 1,
                        cancellationToken: ct),
                    minimum: 5,
                    ct).ConfigureAwait(false);
                if (rawCount >= 5 && eventCount >= 5)
                {
                    return;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(500), ct).ConfigureAwait(false);
                endTime = DateTime.UtcNow.AddSeconds(1);
            }

            throw new ServiceResultException(
                StatusCodes.BadNoData,
                "The active server did not produce enough raw and event history within 30 seconds.");
        }

        private static async Task<(
            DateTime StartTime,
            DateTime EndTime,
            double ProcessingInterval)> GetProcessedWindowAsync(
                HistoryClient client,
                NodeId counterNodeId,
                DateTime startTime,
                DateTime endTime,
                CancellationToken ct)
        {
            var timestamps = new List<DateTime>();
            await foreach (DataValue value in client.ReadRawAsync(
                counterNodeId,
                startTime,
                endTime,
                maxValuesPerNode: 2,
                cancellationToken: ct).ConfigureAwait(false))
            {
                if (!StatusCode.IsGood(value.StatusCode) ||
                    !value.WrappedValue.TryGetValue(out int _))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadDecodingError,
                        "Raw history used to derive the processed window is invalid.");
                }
                timestamps.Add(value.SourceTimestamp.ToDateTime());
            }
            if (timestamps.Count < 5)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNoData,
                    "At least five raw values are required to exercise processed paging.");
            }

            DateTime processedStart = timestamps[0];
            DateTime processedEnd = timestamps[^1].AddTicks(1);
            double interval =
                (processedEnd - processedStart).TotalMilliseconds / 3;
            if (interval <= 0 || !double.IsFinite(interval))
            {
                throw new ServiceResultException(
                    StatusCodes.BadUnexpectedError,
                    "The processed history window is invalid.");
            }
            return (processedStart, processedEnd, interval);
        }

        private static async Task<int> CountAtLeastAsync<T>(
            IAsyncEnumerable<T> values,
            int minimum,
            CancellationToken ct)
        {
            int count = 0;
            await foreach (T _ in values.WithCancellation(ct).ConfigureAwait(false))
            {
                count++;
                if (count >= minimum)
                {
                    break;
                }
            }
            return count;
        }

        private static void ObserveRawValue(
            DataValue value,
            ref DateTime lastTimestamp,
            ref int lastCounter,
            ref int count)
        {
            if (!value.WrappedValue.TryGetValue(out int counter))
            {
                throw new ServiceResultException(
                    StatusCodes.BadTypeMismatch,
                    "A raw history value did not contain an Int32 counter.");
            }

            var timestamp = value.SourceTimestamp.ToDateTime();
            if (count > 0 &&
                (timestamp <= lastTimestamp || counter != lastCounter + 1))
            {
                throw new ServiceResultException(
                    StatusCodes.BadUnexpectedError,
                    $"Raw history has a duplicate or gap after counter {lastCounter} at {lastTimestamp:O}.");
            }

            lastTimestamp = timestamp;
            lastCounter = counter;
            count++;
        }

        private static void ObserveHistoryEvent(
            HistoryEventFieldList historicalEvent,
            HashSet<ByteString> eventIds,
            ref DateTime lastTimestamp,
            ref int lastCounter,
            ref int count)
        {
            if (historicalEvent.EventFields.Count != 3 ||
                !historicalEvent.EventFields[0].TryGetValue(out ByteString eventId) ||
                eventId.IsEmpty ||
                !historicalEvent.EventFields[1].TryGetValue(out DateTimeUtc eventTime) ||
                !historicalEvent.EventFields[2].TryGetValue(out LocalizedText message) ||
                string.IsNullOrWhiteSpace(message.Text))
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError,
                    "A historical event did not contain EventId, Time, and Message.");
            }
            if (!eventIds.Add(eventId))
            {
                throw new ServiceResultException(
                    StatusCodes.BadUnexpectedError,
                    "Historical event paging returned a duplicate EventId.");
            }

            if (!TryParseHistoryEventCounter(message.Text, out int counter))
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError,
                    $"Historical event message '{message.Text}' did not contain a counter.");
            }

            var timestamp = eventTime.ToDateTime();
            if (count > 0 &&
                (timestamp <= lastTimestamp || counter != lastCounter + 1))
            {
                throw new ServiceResultException(
                    StatusCodes.BadUnexpectedError,
                    $"Event history has a duplicate or gap after counter {lastCounter} at {lastTimestamp:O}.");
            }

            lastTimestamp = timestamp;
            lastCounter = counter;
            count++;
        }

        private static bool TryGetHistoryEventCounter(
            HistoryEventFieldList historicalEvent,
            out int counter)
        {
            counter = 0;
            return historicalEvent.EventFields.Count == 3 &&
                historicalEvent.EventFields[2].TryGetValue(
                    out LocalizedText message) &&
                TryParseHistoryEventCounter(message.Text, out counter);
        }

        private static bool TryParseHistoryEventCounter(
            string? message,
            out int counter)
        {
            counter = 0;
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            const string marker = "archived counter ";
            int markerIndex = message.LastIndexOf(marker, StringComparison.Ordinal);
            return markerIndex >= 0 &&
                int.TryParse(
                    message.AsSpan(markerIndex + marker.Length).TrimEnd('.'),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out counter);
        }

        private static void ObserveProcessedValue(
            DataValue value,
            TimeSpan expectedInterval,
            ref DateTime lastTimestamp,
            ref int count)
        {
            if (!StatusCode.IsGood(value.StatusCode) ||
                !value.WrappedValue.TryGetValue(out double aggregate) ||
                !double.IsFinite(aggregate))
            {
                throw new ServiceResultException(
                    StatusCodes.BadDecodingError,
                    "Processed history returned a bad status or non-numeric aggregate.");
            }
            var timestamp = value.SourceTimestamp.ToDateTime();
            if (count > 0)
            {
                TimeSpan interval = timestamp - lastTimestamp;
                if (Math.Abs(
                    (interval - expectedInterval).TotalMilliseconds) > 1)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadUnexpectedError,
                        $"Processed history interval changed from {expectedInterval} to {interval}.");
                }
            }

            lastTimestamp = timestamp;
            count++;
        }

        private static EventFilter CreateHistoryEventFilter()
        {
            var filter = new EventFilter();
            filter.AddSelectClause(
                ObjectTypeIds.BaseEventType,
                BrowseNames.EventId,
                Attributes.Value);
            filter.AddSelectClause(
                ObjectTypeIds.BaseEventType,
                BrowseNames.Time,
                Attributes.Value);
            filter.AddSelectClause(
                ObjectTypeIds.BaseEventType,
                BrowseNames.Message,
                Attributes.Value);
            return filter;
        }

        /// <summary>
        /// V2 subscription notification handler that logs data changes for the
        /// monitored CurrentTime and replicated Counter values and forwards each
        /// change to the <see cref="HaMonitor"/> for failover / data-loss analysis.
        /// </summary>
        private sealed class MonitoringHandler : Opc.Ua.Client.Subscriptions.ISubscriptionNotificationHandler
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="MonitoringHandler"/> class.
            /// </summary>
            /// <param name="monitor">The monitor that evaluates high-availability data changes.</param>
            public MonitoringHandler(HaMonitor monitor)
            {
                m_monitor = monitor;
            }

            /// <inheritdoc/>
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

            /// <inheritdoc/>
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

            /// <inheritdoc/>
            public ValueTask OnKeepAliveNotificationAsync(
                Opc.Ua.Client.Subscriptions.ISubscription subscription,
                uint sequenceNumber,
                DateTime publishTime,
                Opc.Ua.Client.Subscriptions.PublishState publishStateMask)
            {
                return default;
            }

            /// <inheritdoc/>
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
            /// <param name="e">The connection-state transition reported by the managed session.</param>
            /// <param name="endpoint">The endpoint URL associated with the connected session, if known.</param>
            public void OnConnectionStateChanged(ConnectionStateChangedEventArgs e, string? endpoint)
            {
                Console.WriteLine("Connection state: {0} -> {1}", e.PreviousState, e.NewState);
                if (e.Error != null && ServiceResult.IsBad(e.Error))
                {
                    Console.WriteLine(
                        "FAILOVER DETAIL: {0} ({1})",
                        e.Error.StatusCode,
                        e.Error.LocalizedText);
                }
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
            /// <param name="isLeader">Whether this coordinated client replica is now the leader.</param>
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
            /// <param name="name">The monitored item name associated with the value.</param>
            /// <param name="value">The data value received from the subscription.</param>
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

        private const string kHighAvailabilityNamespaceUri =
            "http://opcfoundation.org/UA/Samples/HighAvailability";

        private const int kHistoryVisibilityMarker = -4390;
    }
}
