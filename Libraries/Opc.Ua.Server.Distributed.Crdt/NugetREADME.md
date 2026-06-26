# OPC UA .NET Standard — Active/Active Distributed Server (CRDT)

`OPCFoundation.NetStandard.Opc.Ua.Server.Distributed.Crdt` adds **active/active (multi-writer)** replication to `OPCFoundation.NetStandard.Opc.Ua.Server.Distributed`, built on conflict-free replicated data types (the [`Crdt`](https://www.nuget.org/packages/Crdt) and [`Crdt.Transport`](https://www.nuget.org/packages/Crdt.Transport) packages).

## Overview

Where the base distributed package replicates a shared address space with a single leader writer (active/passive), this package lets **every replica accept writes**. Node additions, removals, references, and values are modelled as CRDTs and gossiped between replicas, so concurrent edits converge without coordination. The active/passive path remains the default; CRDT active/active is opt-in.

## Getting started

```csharp
services.AddOpcUa()
    .AddServer(...)
    .AddNodeManager<MyNodeManagerFactory>()
    .UseReplicatedAddressSpace(crdt =>
    {
        crdt.ReplicaId = Crdt.ReplicaId.New();
        crdt.UseTcpGossip(IPAddress.Loopback, port: 0);
        crdt.AddPeer(peerEndpoint);
    })
    .UseReplicatedSessions();
```

## Target frameworks

`net8.0`, `net9.0`, `net10.0` (the gossip transport ships net8.0+). .NET Framework and netstandard consumers use the active/passive features in the base distributed package.

## Additional documentation

See the [High Availability guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/HighAvailability.md) for the architecture, the active/active-with-CRDT section, and the security boundary around single-use session nonces.
