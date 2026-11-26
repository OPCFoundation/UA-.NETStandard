// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Crdt;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Server.Hosting;
using RedundancySample;
using RedundantServer;

try
{
    await RunAsync(args).ConfigureAwait(false);
    return 0;
}
catch (Exception exception) when (exception is InvalidOperationException or FormatException or ServiceResultException)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

static async Task RunAsync(string[] args)
{
    HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

    builder.Logging.ClearProviders();
    builder.Logging.EnableOpcUaRedaction();
    builder.Logging.AddConsole();

    int port = int.TryParse(builder.Configuration["port"], out int p) ? p : 62543;
    string nodeId = builder.Configuration["HA_NODE_ID"] ?? Guid.NewGuid().ToString("N");
    string? Read(string key) => builder.Configuration[key];
    bool allowInsecure = SampleFreshness.IsEnabled(Read("HA_INSECURE"));
    bool localDemo = SampleFreshness.IsEnabled(Read("HA_LOCAL_DEMO"));
    string host = builder.Configuration["HA_HOST"] ?? "localhost";
    if (allowInsecure || localDemo)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            host = "127.0.0.1";
        }
        if (!IPAddress.TryParse(host, out IPAddress? hostAddress) || !IPAddress.IsLoopback(hostAddress))
        {
            throw new InvalidOperationException("HA_INSECURE or HA_LOCAL_DEMO requires a numeric loopback UA listener.");
        }
        host = hostAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            ? $"[{hostAddress}]"
            : hostAddress.ToString();
    }
    string endpointUrl = $"opc.tcp://{host}:{port}/RedundantServer";
    string applicationUri = builder.Configuration["HA_APPLICATION_URI"]
        ?? $"urn:localhost:OPCFoundation:RedundantServer:{nodeId}";
    string? subjectName = builder.Configuration["HA_SUBJECT_NAME"];
    string? pkiRoot = builder.Configuration["HA_PKI_ROOT"];

    // Select the redundancy topology: "ap" (active/passive, default — a single
    // elected writer) or "aa" (active/active — every replica writes and converges
    // by CRDT gossip).
    string haMode = (Read("HA_MODE") ?? "ap").Trim().ToLowerInvariant();
    bool activeActive = haMode switch
    {
        "aa" or "activeactive" or "active-active" => true,
        "ap" or "activepassive" or "active-passive" => false,
        _ => throw new FormatException("HA_MODE must be ap or aa.")
    };
    string consistency = (Read("HA_CONSISTENCY") ?? "eventual").Trim().ToLowerInvariant();
    bool useStrongConsistency = consistency switch
    {
        "strong" => true,
        "eventual" => false,
        _ => throw new FormatException("HA_CONSISTENCY must be eventual or strong.")
    };
    RedundancySupport redundancyMode = ParseRedundancyMode(
        Read("REDUNDANCY_MODE"),
        defaultMode: activeActive || useStrongConsistency ? RedundancySupport.Hot : RedundancySupport.None);
    ArrayOf<RedundantPeer> redundantPeers = ReadRedundantPeers(builder.Configuration);
    bool enableFastReconnect = SampleFreshness.IsEnabled(Read("HA_FAST_RECONNECT"));
    bool enableDistributedHistorian = SampleFreshness.IsEnabled(Read("HA_HISTORIAN"));
    bool externalRaft = useStrongConsistency && !localDemo;
    if (enableDistributedHistorian)
    {
        throw new InvalidOperationException(
            "HA_HISTORIAN is not supported by this sample: the historian needs separate protected store integration.");
    }
    if (redundancyMode == RedundancySupport.Transparent)
    {
        throw new InvalidOperationException("Transparent redundancy is not supported by this sample.");
    }
    if ((useStrongConsistency && (activeActive || redundancyMode == RedundancySupport.None)) ||
        (activeActive && redundancyMode is RedundancySupport.None or RedundancySupport.HotAndMirrored))
    {
        throw new InvalidOperationException(
            "Strong consistency requires active/passive redundancy. Active/active supports only " +
            "eventual address-space replication without session mirroring.");
    }
    if (enableFastReconnect && !externalRaft)
    {
        throw new InvalidOperationException("Fast reconnect requires external strong Raft.");
    }
    if (allowInsecure && externalRaft)
    {
        throw new InvalidOperationException("HA_INSECURE cannot be used with external Raft.");
    }
    if (localDemo && useStrongConsistency &&
        (Read("HA_RAFT_ID") != null || Read("HA_RAFT_MEMBERS") != null ||
            Read("HA_RAFT_BIND") != null || Read("HA_RAFT_PEERS") != null || !redundantPeers.IsEmpty))
    {
        throw new InvalidOperationException("HA_LOCAL_DEMO strong mode uses one in-process Raft member and no peers.");
    }
    if (!activeActive && !useStrongConsistency &&
        (redundancyMode != RedundancySupport.None || !redundantPeers.IsEmpty))
    {
        throw new InvalidOperationException("Active/passive redundancy requires HA_CONSISTENCY=strong.");
    }
    if (redundancyMode == RedundancySupport.HotAndMirrored && !externalRaft)
    {
        throw new InvalidOperationException("HotAndMirrored mode requires an external freshness-protected Raft store.");
    }
    if (allowInsecure)
    {
        Console.Error.WriteLine("[HA][WARNING] HA_INSECURE=true: unsecured UA and permissive trust on loopback only.");
    }

    if (!string.IsNullOrWhiteSpace(Read("HA_BALANCING_URL")))
    {
        throw new InvalidOperationException("The legacy HA_BALANCING_URL deployment is not supported by this sample.");
    }

    builder.Services.AddSingleton(new HaSampleReplicaInfo(nodeId));
    if (!string.Equals(Read("HA_PEER_DISCOVERY") ?? "static", "static", StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("This sample requires static cluster membership; DNS scaling is unsupported.");
    }
    if (!externalRaft && SampleFreshness.IsEnabled(Read("HA_ENROLL_ONLY")))
    {
        throw new InvalidOperationException("HA_ENROLL_ONLY requires the external strong topology.");
    }

    var raft = externalRaft ? SampleCluster.ReadRaft(Read, "HA") : default;
    IPEndPoint? gossipBind = activeActive
        ? new IPEndPoint(
            IPAddress.Parse(Read("HA_GOSSIP_BIND") ?? (allowInsecure || localDemo ? "127.0.0.1" : "0.0.0.0")),
            int.Parse(Read("HA_GOSSIP_PORT") ?? "4840", CultureInfo.InvariantCulture))
        : null;
    var gossipPeers = gossipBind != null && !allowInsecure
        ? SampleCluster.ReadGossip(Read, "HA", gossipBind)
        : [];
    var localGossipPeers = gossipBind != null && allowInsecure
        ? SampleCluster.ReadList(Read("HA_GOSSIP_PEERS")).Select(peer => SampleCluster.ParseEndpoint(peer)).ToList()
        : [];
    if (gossipBind != null &&
        (gossipBind.Port == 0 || ((allowInsecure || localDemo) && (!IPAddress.IsLoopback(gossipBind.Address) ||
            localGossipPeers.Any(peer => !IPAddress.IsLoopback(peer.Address)) ||
            gossipPeers.Any(peer => !IPAddress.IsLoopback(peer.Address.Address))))))
    {
        throw new InvalidOperationException("Isolated gossip requires loopback addresses and a nonzero port.");
    }
    using AesCbcHmacRecordProtector? protector = externalRaft
        ? await SampleFreshness.AddAsync(builder.Services, Read, "HA").ConfigureAwait(false)
        : null;
    if (useStrongConsistency && localDemo)
    {
        builder.Services.AddSingleton<IRecordProtector>(NullRecordProtector.Instance);
    }
    if (externalRaft || gossipPeers.Count > 0)
    {
        _ = SampleCluster.Require(Read, "HA_NODE_ID");
    }
    using SampleCluster? cluster = externalRaft || gossipPeers.Count > 0
        ? await SampleCluster.LoadAsync(Read, "HA",
            externalRaft ? raft.Peers!.Select(peer => peer.Pin).ToArray() : gossipPeers.Select(peer => peer.Pin).ToArray())
            .ConfigureAwait(false)
        : null;

    IOpcUaServerBuilder ua = builder.Services
        .AddOpcUa()
        .AddServer(o =>
        {
            o.ApplicationName = "RedundantServer";
            o.ApplicationUri = applicationUri;
            o.ProductUri = "uri:opcfoundation.org:RedundantServer";
            o.AutoAcceptUntrustedCertificates = allowInsecure;
            o.AnonymousAccess = allowInsecure ? AnonymousAccess.Unrestricted : AnonymousAccess.ReadOnly;
            o.IncludeUnsecurePolicyNone = allowInsecure;
            o.EndpointUrls.Add(endpointUrl);
            if (!string.IsNullOrWhiteSpace(subjectName))
            {
                o.SubjectName = subjectName;
            }
            o.PkiRoot = string.IsNullOrWhiteSpace(pkiRoot) ? DefaultPkiRootForNode(nodeId) : pkiRoot;
        })
        .AddNodeManager<HaSampleNodeManagerFactory>();

    if (activeActive)
    {
        // The sample node manager gates writes on leadership; in active/active every
        // replica is a writer, so make every replica a leader.
        builder.Services.AddSingleton<ILeaderElection>(_ => new StaticLeaderElection(true));

        ReplicaId replicaId = ReplicaIdFromNodeId(nodeId);
        ua.UseReplicatedAddressSpace(options =>
        {
            options.ReplicaId = replicaId;
            options.AllowUnauthenticatedGossip = allowInsecure;
            if (gossipPeers.Count > 0)
            {
                options.UseTcpGossip(gossipBind!.Address, gossipBind.Port,
                    tls: cluster!.CreateGossipTls(gossipPeers.Select(peer => peer.Pin)));
                foreach ((IPEndPoint peer, _) in gossipPeers)
                {
                    options.AddPeer(peer);
                }
            }
            else if (localGossipPeers.Count > 0)
            {
                options.UseTcpGossip(gossipBind!.Address, gossipBind.Port);
                foreach (IPEndPoint peer in localGossipPeers)
                {
                    options.AddPeer(peer);
                }
            }
        });
        builder.Services.AddSingleton<IServiceLevelProvider>(sp =>
            new LeaderServiceLevelProvider(
                sp.GetRequiredService<ILeaderElection>(),
                redundancyMode));
        builder.Services.AddSingleton<IServerStartupTask>(sp =>
            new ServiceLevelStartupTask(sp.GetRequiredService<IServiceLevelProvider>()));
    }
    else if (redundancyMode == RedundancySupport.None)
    {
        builder.Services.AddSingleton<ILeaderElection>(_ => new StaticLeaderElection(true));
        ua.AddServerServiceLevel(new ConstantServiceLevelProvider());
    }
    else
    {
        ua.UseRedundancyConsistency(options =>
        {
            options.Mode = RedundancyConsistencyMode.Strong;
            options.UseRaftLeaderElection = !externalRaft;
            options.RaftConsensusFactory = _ => externalRaft
                ? cluster!.CreateRaft(raft.Id, raft.Bind!, raft.Peers!)
                : DefaultRaftConsensus.CreateSingleNode();
        });

        ua.UseDistributedAddressSpace(d =>
        {
            d.UseLeaderElection = externalRaft;
            if (externalRaft)
            {
                d.NodeId = raft.Id.ToString(CultureInfo.InvariantCulture);
                d.LeaseMemberIds = new ArrayOf<string>(raft.Peers!
                    .Select(peer => peer.Id.ToString(CultureInfo.InvariantCulture))
                    .Append(d.NodeId).ToArray());
            }
            d.RedundancyMode = redundancyMode;
        });
        if (externalRaft)
        {
            ua.UseDistributedSessions(options => options.EnableFastReconnect = enableFastReconnect);
        }
    }

    if (redundancyMode != RedundancySupport.None)
    {
        ua.UseReplicaNodeIdentity(
            builder.Configuration["HA_REPLICA_SET"] ?? "opcfoundation-ha-sample",
            [HaSampleNodeManagerFactory.NamespaceUri],
            writerAssignedIds: !activeActive);
    }

    ua.AddServerRedundancy(r =>
    {
        r.Mode = redundancyMode;
        r.CurrentServerId = nodeId;
        foreach (RedundantPeer peer in redundantPeers)
        {
            r.RedundantPeers.Add(peer);
        }
    })
    .AddRequestServerStateChange();

    builder.Services.AddSingleton<IServerStartupTask>(
        static _ => new HaSampleSimulationStartupTask());
    IHost application = builder.Build();
    await using var applicationLifetime = ((IAsyncDisposable)application).ConfigureAwait(false);
    if (externalRaft && await SampleFreshness.VerifyOrEnrollAsync(application.Services, Read, "HA")
        .ConfigureAwait(false))
    {
        return;
    }
    if (useStrongConsistency)
    {
        ILeaderElection election = application.Services.GetRequiredService<ILeaderElection>();
        bool hasWriterAuthority = externalRaft
            ? election is SharedStoreLeaseElection
            : election is RaftLeaderElection;
        if (application.Services.GetRequiredService<ISharedKeyValueStore>() is not RaftSharedKeyValueStore raftStore ||
            raftStore.IsProcessLocal("sample/readiness") != localDemo ||
            !hasWriterAuthority)
        {
            throw new InvalidOperationException("Strong mode requires a Raft shared store and a matching writer authority.");
        }
        // A successful linearizable read proves the quorum can commit, not just that transport startup returned.
        await raftStore.TryGetAsync("sample/readiness").ConfigureAwait(false);
        Console.WriteLine("Raft is ready ({0}).", externalRaft ? "pinned TLS cluster" : "one in-process member");
    }

    Console.WriteLine(
        "HA sample node '{0}' configured for {1}; HA_MODE={2}; REDUNDANCY_MODE={3}.",
        nodeId,
        endpointUrl,
        haMode,
        redundancyMode);
    Console.WriteLine("CurrentServerId: {0}", nodeId);
    Console.WriteLine("Redundant peers: {0}", FormatPeers(redundantPeers));
    if (redundancyMode is RedundancySupport.Cold or
        RedundancySupport.Warm or
        RedundancySupport.Hot or
        RedundancySupport.HotAndMirrored)
    {
        Console.WriteLine("NTRS discovery capability and FindServers peer-set provider are enabled.");
    }
    Console.WriteLine("RequestServerStateChange is enabled for administrator-driven Maintenance/NoData failover.");

    await application.StartAsync().ConfigureAwait(false);
    await application.WaitForShutdownAsync().ConfigureAwait(false);
}

static RedundancySupport ParseRedundancyMode(string? value, RedundancySupport defaultMode)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return defaultMode;
    }

    return value.Trim().ToLowerInvariant() switch
    {
        "none" => RedundancySupport.None,
        "cold" => RedundancySupport.Cold,
        "warm" => RedundancySupport.Warm,
        "hot" => RedundancySupport.Hot,
        "hotandmirrored" or "hot-and-mirrored" or "mirrored" => RedundancySupport.HotAndMirrored,
        "transparent" => RedundancySupport.Transparent,
        _ => throw new FormatException(
            $"Invalid REDUNDANCY_MODE '{value}'. Expected none, cold, warm, hot, hotandmirrored, or transparent.")
    };
}

static ArrayOf<RedundantPeer> ReadRedundantPeers(IConfiguration configuration)
{
    var peers = new List<RedundantPeer>();
    foreach (string entry in ReadList(configuration, "HA_REDUNDANT_PEERS"))
    {
        string[] fields = entry.Split('|', StringSplitOptions.TrimEntries);
        if (fields.Length is < 1 or > 3 || string.IsNullOrWhiteSpace(fields[0]))
        {
            throw new FormatException(
                $"Invalid HA_REDUNDANT_PEERS entry '{entry}'; expected applicationUri|applicationName|discoveryUrl+...");
        }

        ArrayOf<string> discoveryUrls = fields.Length >= 3
            ? new ArrayOf<string>(fields[2].Split(
                ['+'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            : [];
        peers.Add(new RedundantPeer(fields[0], discoveryUrls)
        {
            ApplicationName = fields.Length >= 2 && !string.IsNullOrWhiteSpace(fields[1])
                ? new LocalizedText(fields[1])
                : new LocalizedText(fields[0])
        });
    }
    foreach (string peerServerUri in ReadList(configuration, "peerServerUris"))
    {
        peers.Add(new RedundantPeer(peerServerUri, [])
        {
            ApplicationName = new LocalizedText(peerServerUri)
        });
    }

    return new ArrayOf<RedundantPeer>(peers.ToArray());
}

static string FormatPeers(ArrayOf<RedundantPeer> peers)
{
    if (peers.IsNull || peers.Count == 0)
    {
        return "(none)";
    }

    var formattedPeers = new List<string>(peers.Count);
    foreach (RedundantPeer peer in peers)
    {
        formattedPeers.Add($"{peer.ApplicationUri} [{string.Join(", ", peer.DiscoveryUrls)}]");
    }

    return string.Join("; ", formattedPeers);
}

static IEnumerable<string> ReadList(IConfiguration configuration, string key)
{
    string? value = configuration[key];
    if (string.IsNullOrWhiteSpace(value))
    {
        yield break;
    }

    foreach (string item in value.Split(
        [',', ';'],
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        yield return item;
    }
}

/// <summary>
/// Returns a per-node PKI store root so replicas started on ONE host each get
/// their own certificate store. Scoped by node id under the OS temp folder.
/// </summary>
static string DefaultPkiRootForNode(string nodeId)
{
    var sanitized = new System.Text.StringBuilder(nodeId.Length);
    foreach (char c in nodeId)
    {
        sanitized.Append(Array.IndexOf(System.IO.Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
    }

    return System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        "OPC Foundation",
        "RedundantServer",
        sanitized.ToString(),
        "pki");
}

static ReplicaId ReplicaIdFromNodeId(string nodeId)
{
    // Derive a stable replica identity from the node id so it survives restarts.
    byte[] hash = System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(nodeId));
    return new ReplicaId(new Guid(hash.AsSpan(0, 16).ToArray()));
}
