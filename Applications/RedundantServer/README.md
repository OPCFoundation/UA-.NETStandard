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

## Redundancy mode and discovery settings

| Setting | Values | Description |
| --- | --- | --- |
| `REDUNDANCY_MODE` | `none`, `cold`, `warm`, `hot`, `hotandmirrored`, `transparent` | Selects the `Server.ServerRedundancy.RedundancySupport` value. If unset, active/passive defaults to `hot` and active/active defaults to `hotandmirrored`. |
| `HA_NODE_ID` | stable replica id | Used as this replica's `ApplicationUri` suffix and as `CurrentServerId` for transparent redundancy. |
| `HA_REDUNDANT_PEERS` | `applicationUri|applicationName|discoveryUrl1+discoveryUrl2`, separated by comma or semicolon | Defines the peer `RedundantPeer` set. Non-transparent modes publish peer `ApplicationUri` values in `ServerUriArray`, advertise the `NTRS` server capability, and return these peers from `FindServers`. |
| `peerServerUris` | comma/semicolon-separated application URIs | Legacy shorthand for `RedundantServerArray`; prefer `HA_REDUNDANT_PEERS` when clients must resolve peers through `FindServers`. |
| `HA_MODE` | `ap`, `aa` | Chooses active/passive shared-store replication or active/active CRDT gossip. |
| `HA_FAST_RECONNECT` | `true`, `false` | Allows token-reuse reconnect for mirrored sessions. The default requires full `ActivateSession` re-authentication after failover. |

The sample also enables `RequestServerStateChange` with `AddRequestServerStateChange()`. An administrator client can call the standard method on `Server` to request Maintenance or NoData behavior and set `Server.EstimatedReturnTime`; the server updates `Server.ServiceLevel` into the appropriate OPC UA subrange so clients back off or fail over.

Mode-specific nodes shown by the sample:

- `none` — leaves redundancy metadata in its single-server defaults.
- `cold`, `warm`, `hot`, `hotandmirrored` — publish `ServerUriArray` from `HA_REDUNDANT_PEERS`, retain `RedundantServerArray`, advertise `NTRS`, and return the peer `ApplicationDescription` values from `FindServers`.
- `transparent` — publishes `CurrentServerId` and `RedundantServerArray` for the transparent set.

`Server.ServiceLevel` is driven by the sub-range-aware provider: leaders report Healthy, warm standby reports Degraded, hot/hot-and-mirrored replicas report Healthy, and cold standby reports NoData. The console prints the selected mode, server id, peer set, and initial service-level subrange at startup.

## Active/passive vs active/active

The sample selects its redundancy topology with the `HA_MODE` environment variable — `ap` (active/passive, the default) or `aa` (active/active) — and references both `Opc.Ua.Redundancy.Server` and `Opc.Ua.Redundancy.Server`:

- **Active/passive (`HA_MODE=ap`)** — `UseDistributedAddressSpace` + `UseDistributedSessions` with leader election. One replica is the active writer; standbys hydrate from the shared store and take over on failover. Redundancy is reported as `RedundancySupport.Hot`, and clients follow `Server.ServiceLevel` / `Server.ServerRedundancy` to find the active replica.
- **Active/active (`HA_MODE=aa`)** — `UseReplicatedAddressSpace` + `UseReplicatedSessions` (CRDT gossip). Every replica accepts writes and converges without a leader; redundancy is reported as `RedundancySupport.HotAndMirrored`. Replicas gossip over TCP: set `HA_GOSSIP_PORT` (default `4840`; session entries gossip on `port + 1`) and `HA_GOSSIP_PEERS` (a comma/semicolon list of `host:port` for the other replicas' address-space gossip). A session created on one replica can be resumed on another with `ManagedSessionBuilder.WithTokenReuseFailover()` on the client.

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

Two compose files run the sample as containers (the build context is the repository root):

```powershell
# Active/active: two replicas converge by CRDT gossip (no shared store), plus a client.
docker compose -f Applications\RedundantServer\docker-compose.active-active.yml up --build

# Active/passive: leader-election wiring demonstration, plus a client.
docker compose -f Applications\RedundantServer\docker-compose.active-passive.yml up --build
```

Each replica exposes its OPC UA endpoint on the host (`opc.tcp://localhost:62543/RedundantServer` and `opc.tcp://localhost:62544/RedundantServer`), and the bundled `RedundantClient` connects to one replica and follows the redundant set. Set `HA_HOST` to the reachable hostname (the compose files use the container/service name) so peers and clients can connect across the container network. The active/passive compose is a wiring demonstration only, because the default in-memory store is not shared across containers; see "Wire up a shared store for real HA" above for a real deployment.

For the broader design, see [HighAvailability.md](..\..\Docs\HighAvailability.md). For an environment-driven replica-set deployment, see [HighAvailabilityKubernetes.md](..\..\Docs\HighAvailabilityKubernetes.md).
