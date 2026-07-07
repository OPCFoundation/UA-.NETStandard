# PubSub High Availability

This guide maps OPC UA Part 14 §9.1.6 PubSub redundancy to the OPC UA .NET Standard stack's distributed PubSub high-availability seams. It is the PubSub counterpart to [High Availability and OPC UA Redundancy](HighAvailability.md), which covers OPC 10000-4 §6.6 server, client, subscription, session, and network redundancy. For the base publisher/subscriber runtime, transports, encodings, discovery, diagnostics, and SKS concepts, see [Part 14 PubSub](PubSub.md).

PubSub redundancy is implemented by the opt-in `Opc.Ua.PubSub.Redundancy` library and package `OPCFoundation.NetStandard.Opc.Ua.PubSub.Redundancy`. A deployment that uses only the standard `OPCFoundation.NetStandard.Opc.Ua.PubSub` package runs with the default process-local stores and `AlwaysActiveCoordinator`, so every configured PubSub component is active.

## Overview (as per Part 14 §9.1.6)

OPC UA Part 14 §9.1.6 defines redundant PubSub sets as active/standby deployments. Redundant publishers share the same `PublisherId` and identical DataSetWriter configuration, including the same `WriterGroupId` and `DataSetWriterId`. Exactly one instance is `Active` and drives the transport; the rest are `Standby` and wait to take over. Redundant subscribers use the same pattern: one active subscriber dispatches received messages to the application, while standby subscribers keep enough state to assume ownership after failover.

Part 14 defines the redundancy mode as a deployment behavior rather than a standardized PubSub information-model node. Unlike Part 4/5 server redundancy, there is no standard `ServerRedundancy`-style PubSub node that advertises the set. PubSub redundancy is therefore expressed through deployment configuration, shared runtime state, and protocol rules for active/standby publishers and subscribers.

| Mode | Standby state | Stack behavior |
| --- | --- | --- |
| `None` | No standby state. | The default non-redundant behavior; every instance is always active. |
| `Cold` | Rebuild from shared configuration, runtime-state, and security-key stores only after failover. | Lowest standby cost; failover recreates local runtime state and may restart sequence numbers. |
| `Warm` | Configuration is loaded and the components are paused. | Faster failover than Cold because the standby already knows the configured connections, writer groups, reader groups, writers, and readers. |
| `Hot` | Warm state plus live sequence and keep-alive checkpoints. | Seamless takeover mode; a promoted standby resumes with strictly increasing sequence numbers and current keep-alive context. |

Broker transports also have their own high-availability mechanisms. Kafka consumer groups (Part 14 Annex B.2, used by the stack's Kafka transport) and MQTT shared subscriptions help scale and fail over subscribers at the broker layer. Those broker mechanisms complement, but do not replace, the application-level active/standby ownership described here.

## How it maps onto the stack

The core PubSub package defines the seams that a redundant deployment replaces:

| Seam | Purpose |
| --- | --- |
| `IPubSubActivationCoordinator` | Answers whether each PubSub component is `PubSubComponentRole.Active` or `PubSubComponentRole.Standby`, and raises `RoleChanged` when the runtime should pause or resume a component. |
| `IPubSubLeaseStore` | Provides `TryAcquireAsync`, `TryRenewAsync`, and `ReleaseAsync` for single-owner component leases with monotonic fencing tokens. |
| `IPubSubRuntimeStateStore` | Persists component `PubSubState` values for Cold and Warm rebuild / resume. |
| `IPubSubSecurityKeyStore` | Persists SKS `SecurityGroup` key material so all redundant instances can use the same current and future keys. |

`IPubSubBuilder` exposes those seams through `WithActivationCoordinator`, `WithLeaseStore`, `WithRuntimeStateStore`, and `WithSecurityKeyStore`. The distributed implementations in `Opc.Ua.PubSub.Redundancy` bridge them to the reusable HA infrastructure used by the server and client redundancy packages:

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

Hot standby is the seamless mode because the standby tracks live sequence and keep-alive checkpoints. On promotion, it resumes at `last-checkpoint + margin`, so the next published sequence number is strictly greater than the last sequence number that subscribers may have accepted from the failed active. A forward gap is valid: subscribers can detect and log the missing range. A reset to a lower number is different; it collides with the de-duplication window and forces the subscriber to reset its de-duplication state.

Cold and Warm deployments may restart a writer without a current sequence checkpoint. That can create a sequence reset and therefore a subscriber de-duplication reset, producing an observable data gap. Use Hot when the deployment requires seamless publisher failover rather than only process availability.

## Shared security keys (SKS)

PubSub security depends on SecurityGroup keys supplied by SKS. `SharedStorePubSubSecurityKeyStore` implements `IPubSubSecurityKeyStore` on top of `ISharedKeyValueStore` and protects every stored SecurityGroup record with `IRecordProtector`. It persists the SecurityGroup id, security policy URI, key lifetime, current and future `PubSubSecurityKey` values, authorized caller identities, and role permissions.

The practical effect is that a promoted standby can publish, decrypt, or validate secured PubSub messages with the same current and future SecurityGroup keys as the failed active without first pulling fresh keys from SKS. If a protected record cannot be verified and decrypted, the store fails closed and does not apply it.

Networked key stores require a real authenticated-encryption protector. `NullRecordProtector` is only suitable for single-process demos and tests; do not use it with a networked shared store in production.

## Consistency: eventual vs strong

The reusable redundancy layer exposes `RedundancyConsistencyMode`:

- **Eventual.** Bulk replicated state uses the leaderless CRDT / gossip store, while linearizable keyspaces are routed to a strong store. This is the default model for high-throughput state that can tolerate convergence delay.
- **Strong.** All shared state uses a linearizable Raft-backed `ISharedKeyValueStore`. Choose this when every write must be quorum-confirmed.

PubSub leases and leader election need compare-and-swap and therefore a strong keyspace. `SharedStorePubSubLeaseStore` uses `CompareAndSwapAsync` for acquisition, renewal, release, and fencing-token advancement, so it must run on a store that provides linearizable CAS for the `lease/pubsub/` keyspace. Bulk runtime state, Cold / Warm component state, SKS record discovery, and Hot sequence / keep-alive checkpoints can tolerate eventual consistency when the failover policy accepts the resulting checkpoint lag; deployments that require no stale checkpoint reads can place those keyspaces on the strong store as well.

## Kubernetes

Use the Kubernetes high-availability package when the redundant PubSub set runs as replicas in a cluster. `Opc.Ua.Redundancy.Kubernetes` provides Kubernetes Lease-based leader election for whole-instance active/standby deployments, so one pod in a `ReplicaSet` or `Deployment` drives the PubSub transports while the other pods remain standby. For the full server/client HA deployment pattern, RBAC, probes, time synchronization, peer discovery, and secret-management guidance, see [Kubernetes High Availability Deployment](Kubernetes.md).

## Security considerations

Treat the shared store as part of the PubSub trust boundary. Authenticate and encrypt the store channel, restrict network access to the redundant set, apply quotas and retention to PubSub key prefixes, and monitor lease, checkpoint, runtime-state, and SKS write failures as availability signals.

Any shared secret or key material must be protected with `IRecordProtector` before it reaches a networked store. This is mandatory for SKS SecurityGroup records and for any future PubSub state that contains secrets. Never run production networked stores with `NullRecordProtector`; use a real protector such as an authenticated-encryption key-ring implementation and rotate its keys through the same operational controls used for OPC UA certificates and secrets.

Exactly one active publisher should drive a transport for a redundant writer. Validate that all redundant publishers use the same `PublisherId` and identical writer ids, and that standbys cannot publish while in `Standby`. A split-brain publisher set produces duplicate or conflicting sequence streams that subscribers can only handle as data loss or replay.

## See also

- [High Availability and OPC UA Redundancy](HighAvailability.md) — server, client, session, subscription, network, and shared-store HA.
- [Part 14 PubSub](PubSub.md) — base PubSub runtime, transports, encodings, SKS, diagnostics, and existing PubSub HA seams.
- [Kubernetes High Availability Deployment](Kubernetes.md) — Kubernetes Lease election and deployment guidance.
- [OPC UA Part 14 v1.05](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05/) — PubSub specification, including §7.2.5.4.1, §9.1.6, and Annex B.2.
