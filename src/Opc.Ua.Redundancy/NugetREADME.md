<!--
SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
SPDX-License-Identifier: MIT
-->

# OPCFoundation.NetStandard.Opc.Ua.Redundancy

Shared replication building blocks for OPC UA redundancy, used by both `Opc.Ua.Redundancy.Server` and `Opc.Ua.Redundancy.Client` and built on the `Opc.Ua.Redundancy` seams in `Opc.Ua.Core`.

- **Eventually-consistent (AP):** `ByteStringCrdtSerializer` and `ReplicatedSharedKeyValueStore` — an `ISharedKeyValueStore` that gossips state between replicas without a leader.
- **Strongly consistent (CP):** `RaftSharedKeyValueStore` provides
  linearizable `CompareAndSwapAsync` and `WatchAsync`, and `RaftLeaderElection`
  uses native Raft elections. The DI default is a private single-node
  [`RaftCs`](https://github.com/marcschier/raft-cs) replica via
  `DefaultRaftConsensus`, not a store shared by replicas. For a networked
  cluster, configure mutual TLS with a pinned certificate per member and a
  durable write-ahead log.
- **Hybrid:** `HybridSharedKeyValueStore` serves bulk keys from the CRDT store and the strong keyspaces (single-use nonces, lease, election) from Raft, selected with `RedundancyConsistencyMode`.

Use a separately administered freshness authority for replicated state that
must not roll back. The Raft store can compare a writer grant, but does not
check its expiry in the same commit. See
[High Availability](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/HighAvailability.md)
for the supported configurations.

