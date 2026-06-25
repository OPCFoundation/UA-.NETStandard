# High Availability Server

This sample is a minimal Generic Host based OPC UA server that demonstrates the distributed high-availability server building blocks in `Opc.Ua.Server.Distributed`. It registers a `CustomNodeManager2`-derived node manager so the local address space participates in active/passive replication, enables leader election so only the active replica writes sample values, drives `Server.ServiceLevel` from the leader state, and publishes `Server.ServerRedundancy` metadata in hot-redundancy mode.

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

With the default in-memory store, each process elects against its own private store, so this two-terminal setup demonstrates endpoint, node id, service-level, and redundancy metadata configuration rather than cross-process state transfer. To turn it into a real HA pair, register `UseDistributedAddressSpace(d => d.KeyValueStoreFactory = ...)` with a shared store implementation available to both processes.
