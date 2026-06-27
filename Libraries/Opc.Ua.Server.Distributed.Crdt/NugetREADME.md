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
        crdt.UseTcpGossip(IPAddress.Loopback, port: 0, tls: mutualTlsOptions);
        crdt.AddPeer(peerEndpoint);
    })
    .UseReplicatedSessions();
```

## Security

CRDT session mirroring gossips `SharedSessionEntry` records that contain session nonces and secret material. Register an `IRecordProtector` (for example an `AesCbcHmacRecordProtector` backed by a managed cluster key, or a `KeyRingRecordProtector` during key rotation) before calling `UseReplicatedSessions`; startup fails closed without one. `GossipTlsOptions` protects gossip traffic in transit, but it does not provide at-rest confidentiality or record integrity if a replica or replicated store is compromised.

Address-space CRDT replication does not require an `IRecordProtector` because mirrored node values are not session secrets. It does require authenticated gossip for networked replicas: TCP gossip must be configured with mutual TLS by default, and UDP gossip requires an explicit `AllowUnauthenticatedGossip` opt-out for isolated development/test fabrics. This protects integrity/authenticity so an adjacent host cannot inject a higher-clock LWW update and forge served node values.

## Target frameworks

Available on the stack target frameworks: `net472`, `net48`, `netstandard2.1`, `net8.0`, `net9.0`, and `net10.0`. `netstandard2.1` assets are not NativeAOT-published.

## Additional documentation

See the [High Availability guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/HighAvailability.md) for the architecture, the active/active-with-CRDT section, and the security boundary around single-use session nonces.
