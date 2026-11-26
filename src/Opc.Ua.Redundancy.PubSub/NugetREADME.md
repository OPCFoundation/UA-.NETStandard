<!--
SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
SPDX-License-Identifier: MIT
-->

# OPCFoundation.NetStandard.Opc.Ua.Redundancy.PubSub

Distributed High-Availability for OPC UA PubSub (OPC UA Part 14 §9.1.6).

This package backs the PubSub high-availability seams in
`OPCFoundation.NetStandard.Opc.Ua.PubSub` (`IPubSubActivationCoordinator`,
`IPubSubLeaseStore`, `IPubSubRuntimeStateStore`, `IPubSubSecurityKeyStore`) with the
distributed redundancy building blocks from `OPCFoundation.NetStandard.Opc.Ua.Redundancy`
(`ILeaderElection`, `ISharedKeyValueStore`, `IRecordProtector`), so multiple PubSub
instances run as an active/standby redundant set with automatic failover.

Supported redundancy modes (Part 14 §9.1.6): `None`, `Cold`, `Warm`, and `Hot`.
Hot standby adds DataSetWriter sequence checkpoints (Part 14 §7.2.5.4.1).

Secured UADP publishers also need fenced activation and durable nonce reservations.
Use lease-store election with an enrolled `FreshnessProtectedKeyValueStore` backed
by a durable, linearizable authority to protect leases and reservations against
rollback of shared payload records.

See `docs/PubSubHighAvailability.md` for the design and usage.
