# OPCFoundation.NetStandard.Opc.Ua.Redundancy

Shared CRDT building blocks for OPC UA redundancy: `ByteStringCrdtSerializer` and `CrdtSharedKeyValueStore` (an `ISharedKeyValueStore` that gossips state between replicas). Used by both `Opc.Ua.Redundancy.Server` and `Opc.Ua.Redundancy.Client`. Builds on the `Opc.Ua.Redundancy` seams in `Opc.Ua.Core`.
