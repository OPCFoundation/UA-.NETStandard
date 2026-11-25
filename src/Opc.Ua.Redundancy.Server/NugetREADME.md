<!--
SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
SPDX-License-Identifier: MIT
-->

# OPC UA .NET Standard — Distributed / High-Availability Server

`OPCFoundation.NetStandard.Opc.Ua.Redundancy.Server` adds the optional distributed building blocks that let an `OPCFoundation.NetStandard.Opc.Ua.Server` server run as a redundant replica set (active/passive or active/active) while sharing its address space and — optionally — its session state across replicas.

## Overview

The core server library stays a self-contained, single-instance server. This package layers the distributed concerns on top through dependency injection so the in-memory, single-instance path is unchanged when the package is not used:

- A shared, integrity-protected key/value backend (`ISharedKeyValueStore`) that mirrors node additions, removals, references, and values across replicas.
- Leader election (`ILeaderElection`) for the shared-read / leader-write redundancy model, surfaced to clients through the standard OPC UA `ServiceLevel` and redundancy nodes.
- Opt-in mirrors for HotAndMirrored/Transparent failover: encrypted session
  state with single-use nonce validation, subscription definitions,
  retransmission queues for `Republish`, and best-effort continuation
  points. `UseReplicatedEventReporting` orders and replays shared occurrences
  before a replica advertises readiness. The application supplies event
  data and the policy for mirrored condition methods.

## Getting started

Wire the distributed address space and (optionally) shared sessions through the fluent DI surface:

```csharp
services.AddOpcUa()
    .AddServer(...)
    .UseReplicaNodeIdentity("replica-set", ["urn:example:model", "urn:example:instances"])
    .UseDistributedAddressSpace(distributed =>
    {
        distributed.KeyValueStoreFactory = _ => new InMemorySharedKeyValueStore();
    })
    .UseDistributedSessions();
```

The single-instance defaults remain in effect until a shared store is supplied, so the same server binary runs stand-alone or as part of a replica set.

Replica-set address spaces require `UseReplicaNodeIdentity` with the same ordered
namespace list and assignment mode on every replica. Shared slots start at index
2; ApplicationUri and built-in diagnostics remain local. The standard factory
derives identical wire NodeIds, and hydration preserves supplied root/child IDs.
Enable `writerAssignedIds` only for active/passive writer allocations; independent
active/active entities need stable keys or explicit IDs. Protected store contracts
and pre-merge gossip descriptors reject incompatible replicas. See
[Replica-consistent NodeIds](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/ReplicaNodeIdentity.md)
for direct construction, hybrid provisioning and legacy-state restrictions.

Distributed address-space replication follows each node manager's declared non-standard `NamespaceUris`; namespace-zero infrastructure remains local to each replica. Node managers that use a custom ownership partition and return `null` for `NamespaceUris` must implement `ILocalAddressSpaceOwnership` with a stable `PartitionId` and an `OwnsNode(NodeId)` predicate.

Managed cross-process writers require a strong store with guarded writes and a
writer election that exposes its committed grant on that store. The shared-store
lease election supports this with a freshness-protected view whose authority
implements `IDeadlineGuardedSharedKeyValueStore`. The authority must compare
the grant and its expiry in the same commit as the mutation. A separate
sequence coordinator or a leadership predicate cannot do this. Process-local
configurations do not require a distributed grant.

For a strong Raft deployment, configure `UseDistributedAddressSpace` with
`UseLeaderElection = true`, a unique `NodeId`, and the same complete
`LeaseMemberIds` on every replica. This explicitly selects the protected
publication lease over a previously registered Raft or Kubernetes election;
Raft still provides the storage consensus. Do not register another server
writer election after selecting the publication lease.

The guard covers each committed topology, value, delta, sequence and metadata
mutation. It does not roll back changes committed before the grant changed or
turn the separate publications into a multi-key transaction. An unfinished
publication reservation blocks writer takeover until reconciled.
Managed value-cache writes use the owning partition's verified writer; an
unbound registry handle cannot write topology or publication metadata.

Authoritative bootstrap and compacted snapshots require strongly consistent
state reads. CRDT payloads use non-destructive hydration and retained deltas:
missing rows do not imply deletion or an empty store. Corrupt records cannot
justify cleanup, and unfinished publication reservations prevent snapshot/log
compaction until reconciled. See the High Availability guide for these
consistency and recovery requirements.

## Target frameworks

`net48`, `net10.0`, `net11.0`.

## Additional documentation

See the [High Availability guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/HighAvailability.md) for the OPC UA redundancy mapping and the [Kubernetes deployment guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/Kubernetes.md) for running the server as a replica set.
