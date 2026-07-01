# OPCFoundation.NetStandard.Opc.Ua.Redundancy.Client

CRDT gossip sharing for OPC UA client replica sets: a `ReplicatedClientKeyValueStore` (an `ISharedKeyValueStore`) that lets cooperating client replicas share the leader's session secrets cross-process without a central store. Use with the client redundancy coordinator in `Opc.Ua.Client`.
