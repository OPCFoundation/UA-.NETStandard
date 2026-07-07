# High Availability Server

This sample is a minimal Generic Host based OPC UA server that demonstrates the distributed high-availability server building blocks in `Opc.Ua.Redundancy.Server` and the active/active building blocks in `Opc.Ua.Redundancy.Server`. It registers an `AsyncCustomNodeManager`-derived node manager so the local address space participates in replication, and selects its topology with the `HA_MODE` environment variable: **active/passive** (`ap`, the default — leader election so only the active replica writes) or **active/active** (`aa` — every replica writes and converges by CRDT gossip). It drives `Server.ServiceLevel` from the replica state and publishes OPC 10000-4 §6.6 `Server.ServerRedundancy` metadata, including non-transparent discovery and manual failover.

The bundled configuration uses the default in-memory shared key/value store. That is useful for understanding the DI wiring and for single-process experimentation, but separate OS processes do not share memory. A real multi-process or multi-host deployment needs a shared backend for `ISharedKeyValueStore` such as Redis; that backend is intentionally deferred from this small sample.

## Run one instance

```powershell
dotnet run --project Applications\RedundantServer\RedundantServer.csproj -- --port 62543
```

Connect an OPC UA client to `opc.tcp://localhost:62543/RedundantServer` and browse to `Objects/High Availability`. The `Counter` variable is writable and is also incremented once per second while this server is leader. `ActiveReplica` shows the local active writer label.

The startup output shows the effective OPC UA redundancy value and the ServiceLevel subrange that clients read. For example, with `HA_NODE_ID=replica-a`:

```text
HA sample node 'replica-a' listening at opc.tcp://localhost:62543/RedundantServer; HA_MODE=ap; REDUNDANCY_MODE=Hot; ServiceLevel=255 (Healthy).
CurrentServerId: replica-a
Redundant peers: (none)
NTRS discovery capability and FindServers peer-set provider are enabled.
RequestServerStateChange is enabled for administrator-driven Maintenance/NoData failover.
```

## Run two instances

Use distinct ports and stable `HA_NODE_ID` values. `REDUNDANCY_MODE` selects the OPC UA redundancy model. `HA_REDUNDANT_PEERS` provides the peer set used for `ServerUriArray`, `RedundantServerArray`, NTRS discovery registration, and `FindServers` peer results. Each peer entry is:

```text
applicationUri|applicationName|discoveryUrl1+discoveryUrl2
```

```powershell
$env:HA_NODE_ID = "replica-a"
$env:REDUNDANCY_MODE = "hot"
$env:HA_REDUNDANT_PEERS = "urn:localhost:OPCFoundation:RedundantServer:replica-b|RedundantServer replica-b|opc.tcp://localhost:62544/RedundantServer"
dotnet run --project Applications\RedundantServer\RedundantServer.csproj -- --port 62543
```

In a second terminal:

```powershell
$env:HA_NODE_ID = "replica-b"
$env:REDUNDANCY_MODE = "hot"
$env:HA_REDUNDANT_PEERS = "urn:localhost:OPCFoundation:RedundantServer:replica-a|RedundantServer replica-a|opc.tcp://localhost:62543/RedundantServer"
dotnet run --project Applications\RedundantServer\RedundantServer.csproj -- --port 62544
```

With the default in-memory store, each process elects against its own private store, so this two-terminal setup demonstrates endpoint, node id, service-level, and redundancy metadata configuration rather than cross-process state transfer. To turn it into a real HA pair, register a shared `ISharedKeyValueStore` that both processes can reach, as shown below.

The second replica prints the configured redundancy value and the peer set:

```text
HA sample node 'replica-b' listening at opc.tcp://localhost:62544/RedundantServer; HA_MODE=ap; REDUNDANCY_MODE=Hot; ServiceLevel=255 (Healthy).
CurrentServerId: replica-b
Redundant peers: urn:localhost:OPCFoundation:RedundantServer:replica-a [opc.tcp://localhost:62543/RedundantServer]
NTRS discovery capability and FindServers peer-set provider are enabled.
RequestServerStateChange is enabled for administrator-driven Maintenance/NoData failover.
```

## Wire up a shared store for real HA

The single-instance defaults stay in effect until you supply a shared backend. The DI wiring below is what makes additions, removals, references, and values replicate across replicas. Replace the placeholder `RedisSharedKeyValueStore` with any `ISharedKeyValueStore` reachable by every replica (a Redis adapter is intentionally deferred from this sample):

```csharp
builder.Services
    .AddOpcUa()
    .AddServer(o =>
    {
        o.ApplicationName = "RedundantServer";
        o.ApplicationUri = applicationUri;   // unique per replica
        o.EndpointUrls.Add(endpointUrl);
    })
    .AddNodeManager<HaSampleNodeManagerFactory>()
    .UseDistributedAddressSpace(d =>
    {
        // Shared backend reachable by every replica. The default is an
        // in-process InMemorySharedKeyValueStore that is NOT shared across
        // processes, so a real deployment must override it.
        d.KeyValueStoreFactory = sp => new RedisSharedKeyValueStore(redisConnectionString);

        // Authenticated encryption + integrity protection for every record at
        // rest. The 32-byte master key comes from a Kubernetes Secret / KMS and
        // is shared by all replicas.
        d.RecordProtectorFactory = _ => new AesCbcHmacRecordProtector(recordKey);

        d.UseLeaderElection = true;   // lease-based leader = the single writer
        d.NodeId = nodeId;            // unique per replica
        d.RedundancyMode = RedundancySupport.HotAndMirrored;
    })
    .UseDistributedSessions(s =>
    {
        // Mirror encrypted session state so a client can fail over to a standby
        // and reconnect with a full ActivateSession (the authentication token is
        // only a lookup key). The default is the safe re-auth-on-failover.
        s.EnableFastReconnect = true;
    })
    .AddServerRedundancy(r =>
    {
        r.Mode = RedundancySupport.HotAndMirrored;
        r.CurrentServerId = nodeId;
        r.RedundantPeers.Add(new RedundantPeer(
            "urn:host-b:OPCFoundation:RedundantServer:replica-b",
            new ArrayOf<string>("opc.tcp://host-b:62543/RedundantServer"))
        {
            ApplicationName = new LocalizedText("RedundantServer replica-b")
        });
    })
    .AddRequestServerStateChange();
```

### Strong consistency with Raft (no external store)

The Redis placeholder above is one way to get a shared `ISharedKeyValueStore`. This sample also ships an in-package alternative: a strongly-consistent Raft cluster. Set `HA_CONSISTENCY=strong` and the active/passive path registers `UseRedundancyConsistency(RedundancyConsistencyMode.Strong)` backed by a multi-node RaftCs cluster over NanoMsg — the shared store, leader election, and single-use session nonce become linearizable and shared across every replica, with no Redis or other external dependency. Configure the cluster from the environment:

| Setting | Example | Description |
| --- | --- | --- |
| `HA_CONSISTENCY` | `strong`, `eventual` | `strong` backs the shared store with a Raft cluster; `eventual` (default) keeps today's behaviour. |
| `HA_RAFT_ID` | `1` | This replica's unique Raft node id (`1..N`). |
| `HA_RAFT_MEMBERS` | `3` | Static cluster size (odd `3`/`5` for a fault-tolerant quorum). |
| `HA_RAFT_BIND` | `tcp://0.0.0.0:6560` | Local Raft transport bind address. |
| `HA_RAFT_PEERS` | `tcp://server-b:6560,tcp://server-c:6560` | The other members' Raft transport addresses. |

See `docker-compose.yml` with the `active-passive.env` env file for a runnable 3-node Raft cluster (real cross-container active/passive HA). For Kubernetes, `UseKubernetesRaftConsensus` in `Opc.Ua.Redundancy.Kubernetes` derives the same wiring from the StatefulSet ordinal and headless-Service DNS, with a file WAL on a PersistentVolume; see `Docs/Kubernetes.md`.

## Redundancy mode and discovery settings

| Setting | Values | Description |
| --- | --- | --- |
| `REDUNDANCY_MODE` | `none`, `cold`, `warm`, `hot`, `hotandmirrored`, `transparent` | Selects the `Server.ServerRedundancy.RedundancySupport` value. If unset, active/passive defaults to `hot` and active/active defaults to `hotandmirrored`. |
| `HA_NODE_ID` | stable replica id | Used as this replica's `ApplicationUri` suffix (unless `HA_APPLICATION_URI` overrides it) and as `CurrentServerId` for transparent redundancy. |
| `HA_REDUNDANT_PEERS` | `applicationUri|applicationName|discoveryUrl1+discoveryUrl2`, separated by comma or semicolon | Defines the peer `RedundantPeer` set. Non-transparent modes publish peer `ApplicationUri` values in `ServerUriArray`, advertise the `NTRS` server capability, and return these peers from `FindServers`. |
| `peerServerUris` | comma/semicolon-separated application URIs | Legacy shorthand for `RedundantServerArray`; prefer `HA_REDUNDANT_PEERS` when clients must resolve peers through `FindServers`. |
| `HA_MODE` | `ap`, `aa` | Chooses active/passive shared-store replication or active/active CRDT gossip. |
| `HA_PEER_DISCOVERY` | `static`, `dns`, `lds`, `k8s` | Peer-discovery mechanism for the client-facing `RedundantServerSet` (`FindServers`) and, for active/active, the gossip fabric. `static` (default) uses the configured peers; `dns` resolves `HA_SERVICE_NAME` (one record per replica) and updates both dynamically. `lds`/`k8s` are wired via `UseLdsPeerDiscovery` / `UseKubernetesPeerDiscovery`. Static configuration is the fallback until discovery finds peers. |
| `HA_SERVICE_NAME` | a DNS name | The headless-service DNS name resolved by `HA_PEER_DISCOVERY=dns` (defaults to `server`). |
| `HA_CONSISTENCY` | `strong`, `eventual` | Selects the shared-store consistency model; `strong` backs it with a Raft cluster (see *Strong consistency with Raft*). |
| `HA_FAST_RECONNECT` | `true`, `false` | Allows token-reuse reconnect for mirrored sessions. The default requires full `ActivateSession` re-authentication after failover. |
| `HA_RECORD_KEY` | base64 32-byte key | Shared record-protection key for the distributed topologies. When set, every mirrored record (session secrets, identity tokens, notifications) is encrypted + integrity-protected at rest with `AesCbcHmacRecordProtector`. Use the **same** value on every replica; provision from a Kubernetes Secret / KMS in production. |
| `HA_INSECURE` | `true`, `false` | Explicit, auditable opt-out that runs an **isolated demo** without record protection or gossip authentication (prints a warning). Secure by default: without `HA_RECORD_KEY` and without this flag, a distributed topology fails closed at startup. Never set in production. |
| `HA_BALANCING_URL` | a discovery URL | Enables GetEndpoints load direction (see below). A `GetEndpoints` request on this virtual/LB discovery URL is answered with the best replica's endpoints; empty (default) disables it. |
| `HA_APPLICATION_URI` | a URI | Overrides the per-replica `ApplicationUri` with a **shared** one. Required for transparent redundancy so every replica presents one logical server identity (`CreateSession` validates the client `serverUri` against it). |
| `HA_SUBJECT_NAME` | a certificate subject | Sets an explicit, stable certificate subject so replicas sharing a PKI store load one `ApplicationInstanceCertificate`. |
| `HA_PKI_ROOT` | a filesystem path | Points the certificate stores at a shared directory. Combined with `HA_SUBJECT_NAME`, replicas share one `ApplicationInstanceCertificate` (production provisions this from a secret rather than a shared volume). |

The sample also enables `RequestServerStateChange` with `AddRequestServerStateChange()`. An administrator client can call the standard method on `Server` to request Maintenance or NoData behavior and set `Server.EstimatedReturnTime`; the server updates `Server.ServiceLevel` into the appropriate OPC UA subrange so clients back off or fail over.

Mode-specific nodes shown by the sample:

- `none` — leaves redundancy metadata in its single-server defaults.
- `cold`, `warm`, `hot`, `hotandmirrored` — publish `ServerUriArray` from `HA_REDUNDANT_PEERS`, retain `RedundantServerArray`, advertise `NTRS`, and return the peer `ApplicationDescription` values from `FindServers`.
- `transparent` — publishes `CurrentServerId` and `RedundantServerArray` for the transparent set.

`Server.ServiceLevel` is driven by the sub-range-aware provider: leaders report Healthy, warm standby reports Degraded, hot/hot-and-mirrored replicas report Healthy, and cold standby reports NoData. The console prints the selected mode, server id, peer set, and initial service-level subrange at startup.

### GetEndpoints load direction

Setting `HA_BALANCING_URL` registers `UseServerLoadDirection(...)`: every replica publishes its health `ServiceLevel`, a load weight, and its endpoints to the shared store, and a `GetEndpoints` request that arrives on the balancing URL is answered with the best replica's endpoints — the active writer in active/passive, or the least-loaded healthy replica in active/active. Plain discovery on a replica's own URL is unaffected, and this complements (never replaces) the standard client-driven `Server.ServiceLevel` / `RedundantServerArray` selection. In a real deployment a load balancer / Kubernetes `Service` fronts the replicas at the balancing URL; the sample sets `StrongEligibility` when `HA_CONSISTENCY=strong` so the eligibility keyspaces are linearizable. It requires a shared store across replicas (use `HA_CONSISTENCY=strong` / the Raft compose). See `Docs/HighAvailability.md` for the design, conformance, and security notes.

## Transparent redundancy (single virtual endpoint)

The load direction above is one endpoint model; the other is **transparent redundancy**, where every replica presents *one logical server* behind *one virtual endpoint*. Unlike the non-transparent modes (the client reads `RedundantServerArray` and selects a replica), a transparent client sees a single endpoint and never chooses a replica — a load balancer routes it and mirrored session state lets it continue across a replica failure.

To present one logical server, all replicas must share:

- **One `ApplicationUri`** (`HA_APPLICATION_URI`). `CreateSession` validates the client-supplied `serverUri` against the server's `ApplicationUri`, so replicas that disagree would reject sessions established via discovery on a peer.
- **One `ApplicationInstanceCertificate`** (`HA_SUBJECT_NAME` + `HA_PKI_ROOT` pointing at a shared store). Under SecurityMode None the certificate is not exchanged, but secured deployments must present the same certificate from every replica.
- **Mirrored session and address-space state** (`HA_MODE=aa`, `REDUNDANCY_MODE=transparent`) so a session created on one replica can be resumed on another.

Each replica advertises the *virtual* endpoint URL (`HA_HOST` = the load-balancer host). Because that host is a DNS name, the listener binds to all interfaces in the replica's own container while returning `opc.tcp://<lb>:62543/...` to clients, so discovery and `CreateSession` echo the single endpoint the client actually uses (`ServerBase.FilterByEndpointUrl` matches the client host to the advertised base address). A client therefore connects to one URL, and on a replica failure the load balancer routes the reconnect to the survivor, where the mirrored session resumes with a token-reuse reconnect (the full `ActivateSession` signature re-check still applies).

`Transparent/docker-compose.yml` runs this end to end: two `REDUNDANCY_MODE=transparent` replicas sharing one `ApplicationUri` and one certificate (seeded into a shared PKI volume by the first replica, then reused by the second) behind an `nginx` TCP load balancer that publishes the single virtual endpoint `opc.tcp://localhost:62543/RedundantServer`, plus a client that connects only to that endpoint. In production the shared certificate is provisioned from a Kubernetes Secret / KMS to every replica rather than self-generated (see `Docs/Kubernetes.md`), which also removes the first-start certificate race.

## Active/passive vs active/active

The sample selects its redundancy topology with the `HA_MODE` environment variable — `ap` (active/passive, the default) or `aa` (active/active) — and references both `Opc.Ua.Redundancy.Server` and `Opc.Ua.Redundancy.Server`:

- **Active/passive (`HA_MODE=ap`)** — `UseDistributedAddressSpace` + `UseDistributedSessions` with leader election. One replica is the active writer; standbys hydrate from the shared store and take over on failover. Redundancy is reported as `RedundancySupport.Hot`, and clients follow `Server.ServiceLevel` / `Server.ServerRedundancy` to find the active replica.
- **Active/active (`HA_MODE=aa`)** — `UseActiveActiveRedundancy` (which wires `UseReplicatedAddressSpace` + `UseReplicatedSessions` from one set of gossip options; the individual methods remain for advanced setups). Every replica accepts writes and converges without a leader; redundancy is reported as `RedundancySupport.HotAndMirrored`. Replicas gossip over TCP: set `HA_GOSSIP_PORT` (default `4840`; session entries gossip on `port + 1`) and `HA_GOSSIP_PEERS` (a comma/semicolon list of `host:port` for the other replicas' address-space gossip). A session created on one replica can be resumed on another with `ManagedSessionBuilder.WithTokenReuseFailover()` on the client.

Set `HA_PEER_DISCOVERY=dns` (with `HA_SERVICE_NAME`) to discover peers dynamically instead of a static `HA_GOSSIP_PEERS` list: the sample registers `UseDnsPeerDiscovery`, which keeps the client-facing `RedundantServerSet` (`FindServers`) and the active/active gossip fabric current as replicas scale up and down. Static configuration (`AddServerRedundancy` / `HA_REDUNDANT_PEERS`) is the fallback. `HA_PEER_DISCOVERY=lds` (`UseLdsPeerDiscovery`) and Kubernetes EndpointSlice discovery (`UseKubernetesPeerDiscovery`) are also available — see [Docs/HighAvailability.md → Dynamic peer discovery](../../Docs/HighAvailability.md#dynamic-peer-discovery-beyond-66-opt-in).

### Run two active/passive replicas

Use `HA_MODE=ap` (or omit it) for the leader-elected active/passive setup. A real pair needs a shared `ISharedKeyValueStore`; with the default in-memory store this remains a wiring demonstration.

```powershell
# replica A
$env:HA_NODE_ID = "replica-a"
$env:HA_MODE = "ap"
$env:REDUNDANCY_MODE = "hot"
$env:HA_REDUNDANT_PEERS = "urn:localhost:OPCFoundation:RedundantServer:replica-b|RedundantServer replica-b|opc.tcp://localhost:62544/RedundantServer"
dotnet run --project Applications\RedundantServer\RedundantServer.csproj -- --port 62543
```

In a second terminal:

```powershell
# replica B
$env:HA_NODE_ID = "replica-b"
$env:HA_MODE = "ap"
$env:REDUNDANCY_MODE = "hot"
$env:HA_REDUNDANT_PEERS = "urn:localhost:OPCFoundation:RedundantServer:replica-a|RedundantServer replica-a|opc.tcp://localhost:62543/RedundantServer"
dotnet run --project Applications\RedundantServer\RedundantServer.csproj -- --port 62544
```

The active replica writes `Counter`, the standby mirrors state through the shared store, and clients use `Server.ServiceLevel`, `Server.ServerRedundancy.RedundancySupport=Hot`, and `FindServers` peer discovery to select or fail over to the active endpoint.

### Run two active/active replicas

```powershell
# replica A
$env:HA_NODE_ID = "replica-a"
$env:HA_MODE = "aa"
$env:HA_GOSSIP_PORT = "4840"
$env:HA_GOSSIP_PEERS = "127.0.0.1:4842"
dotnet run --project Applications\RedundantServer\RedundantServer.csproj -- --port 62543
```

In a second terminal:

```powershell
# replica B
$env:HA_NODE_ID = "replica-b"
$env:HA_MODE = "aa"
$env:HA_GOSSIP_PORT = "4842"
$env:HA_GOSSIP_PEERS = "127.0.0.1:4840"
dotnet run --project Applications\RedundantServer\RedundantServer.csproj -- --port 62544
```

Both replicas now accept writes to `Counter` and converge: a write on either endpoint propagates to the other by gossip, and the per-second increment runs on every replica, with CRDT last-writer-wins resolving the concurrent updates. Unlike the active/passive store-backed setup, active/active needs no shared `ISharedKeyValueStore` between processes — the gossip transport carries the state.

## Docker Compose

One compose file — `docker-compose.yml` — runs the sample as a 3-replica set; the topology is chosen from the environment (the build context is the repository root). Two extra files cover the transparent and load-direction demos.

```powershell
# Active/active, eventual consistency (CRDT gossip, no shared store) — the default.
docker compose --env-file Applications\RedundantServer\active-active-eventual.env `
  -f Applications\RedundantServer\docker-compose.yml up --build

# Active/active, strong consistency (3-node RaftCs, shared linearizable store).
docker compose --env-file Applications\RedundantServer\active-active-strong.env `
  -f Applications\RedundantServer\docker-compose.yml up --build

# Active/passive, strong consistency (3-node RaftCs; one active writer, two hot standbys).
docker compose --env-file Applications\RedundantServer\active-passive.env `
  -f Applications\RedundantServer\docker-compose.yml up --build

# GetEndpoints load direction over the Raft cluster (sets HA_BALANCING_URL on every replica).
docker compose -f Applications\RedundantServer\LoadDirection\docker-compose.yml up --build

# Transparent redundancy: replicas as ONE logical server behind an nginx load balancer
# on a single virtual endpoint (opc.tcp://localhost:62543/RedundantServer).
docker compose -f Applications\RedundantServer\Transparent\docker-compose.yml up --build
```

Each replica exposes its OPC UA endpoint on the host (`opc.tcp://localhost:62543/RedundantServer`, `:62544`, `:62545`). Set `HA_HOST` to the reachable hostname (the compose file uses the container/service name) so peers and clients connect across the container network. Three replicas run in every topology (Raft needs a 3-node quorum; active/active eventual works with 2 or 3 — remove `server-c` for a 2-node run, so the replica count is configurable). The Raft topologies are real cross-container HA deployments whose RaftCs cluster is the shared, linearizable store; active/active eventual converges leaderlessly over CRDT gossip with no shared store.

### Scale active/active eventual replicas

`Scale/docker-compose.yml` has a single `server` service for active/active eventual (CRDT gossip) runs. Docker Compose can scale it to any replica count because the sample discovers peers from the `server` DNS alias instead of a fixed `HA_GOSSIP_PEERS` list:

```powershell
docker compose -f Applications\RedundantServer\Scale\docker-compose.yml up --build --scale server=5
```

The scale file sets `HA_PEER_DISCOVERY=dns` and `HA_SERVICE_NAME=server`; every replica resolves the service alias, removes its own container IP, and seeds gossip with the remaining task IPs on `HA_GOSSIP_PORT` (session gossip uses the next port). No host ports are published, so clients should run inside the same compose network or attach to it. This dynamic scaling path is gossip-only. Dynamic Raft scaling is intentionally unsupported because Raft needs stable replica identities and an odd quorum; use the Kubernetes StatefulSet deployment with `UseKubernetesRaftConsensus` for strong consistency.

The same file has a `clients` profile that runs **independent managed clients** in their own containers — each opts into `WithServerRedundancy()` and fails over on its own, so it scales to any count alongside the servers (no shared client store needed):

```powershell
docker compose -f Applications\RedundantServer\Scale\docker-compose.yml --profile clients up --build --scale server=3 --scale client=2
```

For a *coordinated* single-active client replica set (exactly one client active, the rest hot/warm/cold standbys), use `AddRedundantClientSession` + a CAS-capable `AddRaftClientSharedStore` (a fixed Raft quorum, like the server's active/passive) rather than independent clients — see [HighAvailability.md](../../Docs/HighAvailability.md). Each client replica is its own process; there is no in-process replica set.

For a **client-centric** compose that scales the client and server independently and logs failover / HA / **data loss** on both sides, use [`Applications/RedundantClient/docker-compose.yml`](../RedundantClient/docker-compose.yml) (eventual, shows data loss) and its `Strong/docker-compose.yml` (Raft, no data loss). See the [RedundantClient README](../RedundantClient/README.md#run-with-docker-compose-scale-the-client-and-server-independently-see-failover--data-loss).

### Watch replication and failover

The `clients` (and `demo`) profile adds one or more `RedundantClient` instances — one process per replica; set `CLIENT_REPLICAS` to scale the number of client containers. To see replication and failover end to end:

```powershell
# 1. Start the server set (active/passive here) plus a client that prints the replicated Counter.
docker compose --env-file Applications\RedundantServer\active-passive.env `
  -f Applications\RedundantServer\docker-compose.yml --profile clients up --build

# 2. In another shell, stop the active/leader replica and watch the client keep going:
docker compose -f Applications\RedundantServer\docker-compose.yml stop server-a
```

The client logs its reconnect/redirect to a surviving replica across the failover. Bring the replica back with `docker compose ... start server-a`; the Raft cluster re-admits it. Set `CLIENT_REPLICAS=3` to run three independent client processes, each of which fails over on its own (for a coordinated single-active client set, use `AddRedundantClientSession` over a Raft client store as above). On the strong-consistency Raft topology the `HighAvailability.Counter` **continues** across the failover: the sample wires it through the distributed value cache (see [Sharing values across replicas](#sharing-values-across-replicas)), so the promoted replica resumes from the last value the former leader shared.

For the broader design, see [HighAvailability.md](..\..\Docs\HighAvailability.md). For an environment-driven replica-set deployment, see [Kubernetes.md](..\..\Docs\Kubernetes.md).

## Sharing values across replicas

OPC 10000-4 §6.6 requires identical NodeIds and address spaces across a redundant set but does not standardize how live process *values* are shared. `Opc.Ua.Redundancy.Server` adds an opt-in extension for this: a variable's read/write callbacks can participate in a distributed value cache (`IDistributedValueCache`) so the last value is cached in the shared store and served — within a freshness bound — from any replica.

`UseDistributedAddressSpace` registers `IDistributedValueCache` in dependency injection (backed by the distributed node-state store; a consumer that constructs the store directly can build a `DistributedValueCache` itself). The sample injects it into `HaSampleNodeManager` and wires the `Counter`:

```csharp
// Opt the Counter's read/write callbacks into the distributed value cache.
counter.EnableDistributedValueParticipation(
    valueCache,
    maxAge: TimeSpan.FromSeconds(10),
    liveRead: _ => new ValueTask<DataValue>(ReadLocalCounter()));

// The active replica writes each new value through to the shared store...
await valueCache.CacheAsync(counter.NodeId,
    new DataValue(Variant.From(value), StatusCodes.Good, DateTimeUtc.Now));

// ...and a replica that has just been promoted seeds from the last shared value,
// so the Counter continues instead of restarting.
(_, DataValue cached) = await valueCache.TryGetAsync(counter.NodeId, maxAge);
```

On the strong-consistency (Raft) topology this makes the `Counter` genuinely shared: standby replicas serve the active replica's value, and after a failover the promoted replica resumes from the last value the former leader wrote (verified end-to-end). Monitored items keep reading through the normal pipeline, so a client that monitors a standby observes the shared value only through the participating read path — consistent with OPC UA monitored-item semantics.

Value sharing uses the shared store, which is protected: supply `HA_RECORD_KEY` (secure by default) or set `HA_INSECURE=true` for an isolated demo — see the settings table above.

## Notes

- **Active/active eventual convergence over TCP/UDP gossip.** State propagation between active/active replicas requires each CRDT snapshot to be sent as a `Crdt.Transport` `FrameCodec` frame (`MessageType.State`); the TCP/UDP gossip transports `Decode` on send and would otherwise throw `System.IO.InvalidDataException: Frame length does not match the encoded body length`. The stack's `FramingGossipTransport` decorator handles this framing for `UseReplicatedAddressSpace`/`UseReplicatedSessions`, so cross-process `Counter` and session state converge over real gossip (regression-tested with two real `TcpGossipTransport`s). The in-memory transport is a transparent passthrough, so single-process runs behaved correctly regardless.
- **Value continuity across failover.** The `Counter` is shared and continues across failover through the distributed value cache: on the strong-consistency (Raft) topology it rides the linearizable Raft store, and on active/active eventual it rides the address-space CRDT gossip. Monitored items read through the normal pipeline, so a monitored item on a standby observes the shared value only through the participating read path (as documented in `DistributedValueParticipation`).
