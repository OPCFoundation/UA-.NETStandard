# High Availability Server

This sample is a minimal Generic Host based OPC UA server that demonstrates the distributed high-availability server building blocks in `Opc.Ua.Server.Distributed`. It registers an `AsyncCustomNodeManager`-derived node manager so the local address space participates in active/passive replication, enables leader election so only the active replica writes sample values, drives `Server.ServiceLevel` from the leader state, and publishes `Server.ServerRedundancy` metadata in hot-redundancy mode.

The bundled configuration uses the default in-memory shared key/value store. That is useful for understanding the DI wiring and for single-process experimentation, but separate OS processes do not share memory. A real multi-process or multi-host deployment needs a shared backend for `ISharedKeyValueStore` such as Redis; that backend is intentionally deferred from this small sample.

## Run one instance

```powershell
dotnet run --project Applications\HighAvailabilityServer\HighAvailabilityServer.csproj -- --port 62543
```

Connect an OPC UA client to `opc.tcp://localhost:62543/HighAvailabilityServer` and browse to `Objects/High Availability`. The `Counter` variable is writable and is also incremented once per second while this server is leader. `ActiveReplica` shows the local active writer label.

## Run two instances

Use distinct ports and stable `HA_NODE_ID` values. The `peerServerUris` setting is a comma- or semicolon-separated list published in `Server.ServerRedundancy.RedundantServerArray`.

```powershell
$env:HA_NODE_ID = "replica-a"
dotnet run --project Applications\HighAvailabilityServer\HighAvailabilityServer.csproj -- --port 62543 --peerServerUris "urn:localhost:OPCFoundation:HighAvailabilityServer:replica-b"
```

In a second terminal:

```powershell
$env:HA_NODE_ID = "replica-b"
dotnet run --project Applications\HighAvailabilityServer\HighAvailabilityServer.csproj -- --port 62544 --peerServerUris "urn:localhost:OPCFoundation:HighAvailabilityServer:replica-a"
```

With the default in-memory store, each process elects against its own private store, so this two-terminal setup demonstrates endpoint, node id, service-level, and redundancy metadata configuration rather than cross-process state transfer. To turn it into a real HA pair, register a shared `ISharedKeyValueStore` that both processes can reach, as shown below.

## Wire up a shared store for real HA

The single-instance defaults stay in effect until you supply a shared backend. The DI wiring below is what makes additions, removals, references, and values replicate across replicas. Replace the placeholder `RedisSharedKeyValueStore` with any `ISharedKeyValueStore` reachable by every replica (a Redis adapter is intentionally deferred from this sample):

```csharp
builder.Services
    .AddOpcUa()
    .AddServer(o =>
    {
        o.ApplicationName = "HighAvailabilityServer";
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
        r.PeerServerUris.Add("urn:host-b:OPCFoundation:HighAvailabilityServer:replica-b");
    });
```

## Active/passive vs active/active

Both topologies are supported out of the box and differ only in the redundancy mode reported to clients and whether session state is mirrored — the shared store and leader election are the same in both:

- **Active/passive** — set `r.Mode = RedundancySupport.Hot` (or `Warm` / `Cold`). One replica is the active server that clients use; standbys hydrate from the shared store and take over on failover. Leader election selects the single active writer, and clients follow `Server.ServiceLevel` / `Server.ServerRedundancy` to find it.
- **Active/active** — set `r.Mode = RedundancySupport.HotAndMirrored` and enable `UseDistributedSessions(s => s.EnableFastReconnect = true)`. Every replica serves clients concurrently from the shared, mirrored state; the elected leader remains the single writer for replicated nodes while all replicas serve reads. Because session state is mirrored, a session created on one replica can be resumed on another with `ManagedSessionBuilder.WithTokenReuseFailover()` on the client, reconnecting by reusing the authentication token after a full `ActivateSession` check.
