# PubSub High Availability

This guide explains how the OPC UA .NET Standard stack implements
distributed PubSub high availability under OPC UA Part 14 §9.1.6. It
complements [High Availability and OPC UA Redundancy](HighAvailability.md),
which covers OPC 10000-4 §6.6 server, client, subscription, session, and
network redundancy. For the base publisher/subscriber runtime, transports,
encodings, discovery, diagnostics, and Security Key Service (SKS), see
[Part 14 PubSub](PubSub.md).

PubSub redundancy is implemented by the opt-in `Opc.Ua.Redundancy.PubSub` library and package `OPCFoundation.NetStandard.Opc.Ua.Redundancy.PubSub`. A deployment that uses only the standard `OPCFoundation.NetStandard.Opc.Ua.PubSub` package runs with the default process-local stores and `AlwaysActiveCoordinator`, so every configured PubSub component is active.

## Contents

- [PubSub High Availability](#pubsub-high-availability)
  - [Overview (as per Part 14 §9.1.6)](#overview-as-per-part-14-916)
  - [How it maps onto the stack](#how-it-maps-onto-the-stack)
  - [Sequence-number continuity (Hot standby)](#sequence-number-continuity-hot-standby)
  - [Shared security keys (SKS)](#shared-security-keys-sks)
  - [Consistency: eventual vs strong](#consistency-eventual-vs-strong)
  - [Kubernetes](#kubernetes)
  - [Security considerations](#security-considerations)
  - [See also](#see-also)

## Overview (as per Part 14 §9.1.6)

OPC UA Part 14 §9.1.6 defines redundant PubSub sets as active/standby deployments. Redundant publishers share the same `PublisherId` and identical DataSetWriter configuration, including the same `WriterGroupId` and `DataSetWriterId`. Exactly one instance is `Active` and drives the transport; the rest are `Standby` and wait to take over. Redundant subscribers use the same pattern: one active subscriber dispatches received messages to the application, while standby subscribers keep enough state to assume ownership after failover.

Part 14 defines the redundancy mode as a deployment behavior rather than a standardized PubSub information-model node. Unlike Part 4/5 server redundancy, there is no standard `ServerRedundancy`-style PubSub node that advertises the set. PubSub redundancy is therefore expressed through deployment configuration, shared runtime state, and protocol rules for active/standby publishers and subscribers.

| Mode | Standby state | Stack behavior |
| --- | --- | --- |
| `None` | No standby state. | The default non-redundant behavior; every instance is always active. |
| `Cold` | Rebuild from shared configuration, runtime-state, and security-key stores only after failover. | Lowest standby cost; failover recreates local runtime state and may restart sequence numbers. |
| `Warm` | Configuration is loaded and the components are paused. | Faster failover than Cold because the standby already knows the configured connections, writer groups, reader groups, writers, and readers. |
| `Hot` | Warm state plus shared writer sequence checkpoints. | A promoted standby restores writer sequence checkpoints with a fixed offset; continuity depends on checkpoint freshness and sequence-number wrap handling. |

Broker transports also have their own high-availability mechanisms. Kafka consumer groups (Part 14 Annex B.2, used by the stack's Kafka transport) and MQTT shared subscriptions help scale and fail over subscribers at the broker layer. Those broker mechanisms complement, but do not replace, the application-level active/standby ownership described here.

## How it maps onto the stack

The core PubSub package defines the seams that a redundant deployment replaces:

| Seam | Purpose |
| --- | --- |
| `IPubSubActivationCoordinator` | Answers whether each PubSub component is `PubSubComponentRole.Active` or `PubSubComponentRole.Standby`, and raises `RoleChanged` when the runtime should pause or resume a component. |
| `IPubSubLeaseStore` | Provides `TryAcquireAsync`, `TryRenewAsync`, and `ReleaseAsync` for single-owner component leases with monotonic fencing tokens. |
| `IPubSubRuntimeStateStore` | Persists component `PubSubState` values for Cold and Warm rebuild / resume. |
| `IPubSubSecurityKeyStore` | Persists SKS `SecurityGroup` key material so all redundant instances can use the same current and future keys. |

`IPubSubBuilder` exposes those seams through `WithActivationCoordinator`, `WithLeaseStore`, `WithRuntimeStateStore`, and `WithSecurityKeyStore`. The distributed implementations in `Opc.Ua.Redundancy.PubSub` bridge them to the reusable HA infrastructure used by the server and client redundancy packages:

| Distributed building block | PubSub bridge |
| --- | --- |
| `ILeaderElection` | `LeaderElectionActivationCoordinator` maps leadership to whole-instance PubSub `Active` / `Standby` roles. |
| `ISharedKeyValueStore.CompareAndSwapAsync` | `SharedStorePubSubLeaseStore` implements fenced leases for `LeaseActivationCoordinator`. |
| `ISharedKeyValueStore` | `SharedStorePubSubRuntimeStateStore` stores component runtime state and the PubSub keyspace includes Hot sequence / keep-alive checkpoints. |
| `IRecordProtector` + `ISharedKeyValueStore` | `SharedStorePubSubSecurityKeyStore` encrypts and authenticates shared SKS records before writing them to the store. |

There are two election paths:

- **Whole-instance active/standby.** `LeaderElectionActivationCoordinator` wraps an `ILeaderElection`. When the local replica is leader, every known PubSub component is `Active`; when it is a follower, every component is `Standby`. Use this when the deployment elects one active PubSub process at a time, for example with Raft leadership or the Kubernetes Lease election from `Opc.Ua.Redundancy.Kubernetes`.
- **Per-component leases.** `SharedStorePubSubLeaseStore` backs the existing `LeaseActivationCoordinator`. Each connection, writer group, or reader group gets its own lease key. The active owner renews the lease; standbys retry acquisition and take over when renewal stops. Compare-and-swap plus a monotonic fencing token prevents a paused or partitioned owner from resuming with stale ownership.

The low-level wiring is intentionally the same shape as the base PubSub builder:

```csharp
services.AddOpcUa()
    .AddPubSub()
    .WithRuntimeStateStore(runtimeStateStore)
    .WithSecurityKeyStore(securityKeyStore)
    .WithLeaseStore(leaseStore)
    .WithActivationCoordinator(new LeaseActivationCoordinator(leaseStore, telemetry, ownerId));
```

The intended high-level service-collection API is `AddPubSubRedundancy(Action<PubSubRedundancyOptions>?)`. It composes the same seams from `PubSubRedundancyOptions`, resolving `ISharedKeyValueStore`, `ILeaderElection` when `PubSubRedundancyElection.LeaderElection` is selected, and `IRecordProtector` for the shared SKS store.

## Sequence-number continuity (Hot standby)

OPC UA Part 14 §7.2.5.4.1 defines the subscriber de-duplication key as `(PublisherId, DataSetWriterId, SequenceNumber)`. For a redundant publisher, the shared `PublisherId` and identical writer configuration mean the promoted standby must continue the same logical sequence stream.

Hot standby restores each writer from its stored sequence checkpoint with a fixed offset of 1,000, advancing the local counter only if the calculated value is higher. This does not guarantee continuity beyond every message accepted from the failed publisher: checkpoint age, publication rate, store lag and sequence-number wrap affect the result. A strongly consistent store prevents stale reads of committed checkpoints, but cannot recover sequence advances that were never checkpointed. Validate failover and subscriber duplicate handling for the deployment; Hot mode alone does not guarantee lossless or gap-free delivery.

Cold and Warm deployments may restart a writer without a current sequence checkpoint. That can create a sequence reset and require subscriber de-duplication state to be reset, producing an observable data gap. Hot adds checkpoint-based restoration, but deployments requiring sequence continuity must account for the limitations above. Normal sequence-number wrap must be distinguished from a restart; a lower numeric value alone does not establish a reset.

## Shared security keys (SKS)

PubSub security depends on SecurityGroup keys supplied by SKS. `SharedStorePubSubSecurityKeyStore` implements `IPubSubSecurityKeyStore` on top of `ISharedKeyValueStore` and protects every stored SecurityGroup record with `IRecordProtector`. It persists the SecurityGroup id, security policy URI, key lifetime, current and future `PubSubSecurityKey` values, authorized caller identities, and role permissions.

The practical effect is that a promoted standby can publish, decrypt, or validate secured PubSub messages with the same current and future SecurityGroup keys as the failed active without first pulling fresh keys from SKS. If a protected record cannot be verified and decrypted, the store fails closed and does not apply it.

Networked key stores require a real authenticated-encryption protector. `NullRecordProtector` is only suitable for single-process demos and tests; do not use it with a networked shared store in production.

## Consistency: eventual vs strong

The reusable redundancy layer exposes `RedundancyConsistencyMode`:

- **Eventual.** Bulk replicated state uses the leaderless CRDT / gossip store, while linearizable keyspaces are routed to a strong store. This is the default model for high-throughput state that can tolerate convergence delay.
- **Strong.** All shared state uses a linearizable Raft-backed `ISharedKeyValueStore`. Choose this when every write must be quorum-confirmed.

PubSub leases and leader election need compare-and-swap and therefore a strong keyspace. `SharedStorePubSubLeaseStore` uses `CompareAndSwapAsync` for acquisition, renewal, release, and fencing-token advancement, so it must run on a store that provides linearizable CAS for the `lease/pubsub/` keyspace. Bulk runtime state, Cold / Warm component state, SKS record discovery, and Hot writer sequence checkpoints can use eventual consistency when the failover policy accepts the resulting state lag. Deployments that require no stale checkpoint reads can place those keyspaces on the strong store as well, but that alone does not establish [sequence-number continuity](#sequence-number-continuity-hot-standby).

## Kubernetes

Use the Kubernetes high-availability package when a redundant PubSub set runs
as replicas in a cluster. `Opc.Ua.Redundancy.Kubernetes` provides
Kubernetes-Lease leader election for whole-instance active/standby
deployments. One pod drives the PubSub transports; the others remain on
standby.

For the full server/client deployment pattern, including role-based access
control (RBAC), probes, time synchronization, peer discovery, and secret
management, see [Kubernetes High Availability Deployment](Kubernetes.md).

## Security considerations

Treat the shared store as part of the PubSub trust boundary:

- Authenticate and encrypt the store connection.
- Restrict network access to the redundant set.
- Apply quotas and retention to PubSub key prefixes.
- Monitor lease, checkpoint, runtime-state, and SKS write failures as
  availability signals.

Any shared secret or key material must be protected with `IRecordProtector` before it reaches a networked store. This is mandatory for SKS SecurityGroup records and for any future PubSub state that contains secrets. Never run production networked stores with `NullRecordProtector`; use a real protector such as an authenticated-encryption key-ring implementation and rotate its keys through the same operational controls used for OPC UA certificates and secrets.

Exactly one active publisher should drive a transport for a redundant writer. Validate that all redundant publishers use the same `PublisherId` and identical writer ids, and that standbys cannot publish while in `Standby`. A split-brain publisher set produces duplicate or conflicting sequence streams that subscribers can only handle as data loss or replay.

## See also

- [High Availability and OPC UA Redundancy](HighAvailability.md) — server, client, session, subscription, network, and shared-store HA.
- [Part 14 PubSub](PubSub.md) — base PubSub runtime, transports, encodings, SKS, diagnostics, and existing PubSub HA seams.
- [Kubernetes High Availability Deployment](Kubernetes.md) — Kubernetes Lease election and deployment guidance.
- [OPC UA Part 14 v1.05](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05/) — PubSub specification, including §7.2.5.4.1, §9.1.6, and Annex B.2.
