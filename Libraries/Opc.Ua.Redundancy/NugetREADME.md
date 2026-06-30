# OPCFoundation.NetStandard.Opc.Ua.Redundancy

Shared replication building blocks for OPC UA redundancy, used by both `Opc.Ua.Redundancy.Server` and `Opc.Ua.Redundancy.Client` and built on the `Opc.Ua.Redundancy` seams in `Opc.Ua.Core`.

- **Eventually-consistent (AP):** `ByteStringCrdtSerializer` and `CrdtSharedKeyValueStore` — an `ISharedKeyValueStore` that gossips state between replicas without a leader.
- **Strongly-consistent (CP):** a Raft layer behind the `IRaftConsensus` seam — `RaftSharedKeyValueStore` (linearizable `CompareAndSwapAsync` + `WatchAsync`) and `RaftLeaderElection` (native single-leader election). `InProcessRaftConsensus` is the in-process/offline backend; the external `RaftCs` engine binds via `RaftCsConsensus` for multi-pod clusters.
- **Hybrid:** `HybridSharedKeyValueStore` serves bulk keys from the CRDT store and the strong keyspaces (single-use nonces, lease, election) from Raft, selected with `RedundancyConsistencyMode`.

See `Docs/HighAvailability.md` (*Consistency modes*) for guidance.

