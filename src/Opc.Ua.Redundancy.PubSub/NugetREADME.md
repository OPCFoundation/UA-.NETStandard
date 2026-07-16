# OPCFoundation.NetStandard.Opc.Ua.Redundancy.PubSub

Distributed High-Availability for OPC UA PubSub (OPC UA Part 14 §9.1.6).

This package backs the PubSub high-availability seams in
`OPCFoundation.NetStandard.Opc.Ua.PubSub` (`IPubSubActivationCoordinator`,
`IPubSubLeaseStore`, `IPubSubRuntimeStateStore`, `IPubSubSecurityKeyStore`) with the
distributed redundancy building blocks from `OPCFoundation.NetStandard.Opc.Ua.Redundancy`
(`ILeaderElection`, `ISharedKeyValueStore`, `IRecordProtector`), so multiple PubSub
instances run as an active/standby redundant set with automatic failover.

Supported redundancy modes (Part 14 §9.1.6): `None`, `Cold`, `Warm`, and `Hot`
(seamless takeover with sequence-number continuity per Part 14 §7.2.5.4.1).

See `docs/PubSubHighAvailability.md` for the design and usage.
