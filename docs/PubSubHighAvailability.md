<!--
SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
SPDX-License-Identifier: MIT
-->

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
  - [Secured publisher nonce reservations](#secured-publisher-nonce-reservations)
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
| `IPubSubRuntimeStateStore` | Shares group and child `PubSubState` values in Warm and Hot. Cold keeps state in process. |
| `IPubSubSecurityKeyStore` | Persists SKS `SecurityGroup` key material so all redundant instances can use the same current and future keys. |

`IPubSubBuilder` exposes those seams through `WithActivationCoordinator`, `WithLeaseStore`, `WithRuntimeStateStore`, and `WithSecurityKeyStore`. The distributed implementations in `Opc.Ua.Redundancy.PubSub` bridge them to the reusable HA infrastructure used by the server and client redundancy packages:

| Distributed building block | PubSub bridge |
| --- | --- |
| `ILeaderElection` | `LeaderElectionActivationCoordinator` maps leadership to whole-instance PubSub `Active` / `Standby` roles. |
| `FreshnessProtectedKeyValueStore` | `SharedStorePubSubLeaseStore` implements fenced leases for `LeaseActivationCoordinator`. |
| `FreshnessProtectedKeyValueStore` and an activation fence | `SharedStorePubSubRuntimeStateStore` mirrors active group and child state; `SharedStorePubSubWriterCheckpointStore` commits Hot writer sequences. |
| `IRecordProtector` + `ISharedKeyValueStore` | `SharedStorePubSubSecurityKeyStore` encrypts and authenticates shared SKS records before writing them to the store. |

There are two election paths:

- **Whole-instance active/standby.** `LeaderElectionActivationCoordinator` wraps an `ILeaderElection`. When the local replica is leader, every known PubSub component is `Active`; when it is a follower, every component is `Standby`. Use this for Cold when the deployment elects one active process, for example with Kubernetes Lease election. The built-in adapter doesn't expose a fence. External Warm or Hot stores require a custom `IPubSubActivationFenceProvider` that reports the currently confirmed, durable activation fence, or per-component lease election.
- **Per-component leases.** `SharedStorePubSubLeaseStore` backs `LeaseActivationCoordinator`. Writer and reader groups have separate lease keys. The active owner renews its exact grant; standbys retry acquisition and take over when renewal stops. Each lease-store instance has a new incarnation, so restarting with the same configured owner ID does not resume a live grant. Use the same lease duration on all replicas and keep their clocks synchronized. Connection-level runtime state has no group lease; it stays local.

For external per-component leases and external Warm or Hot stores, register the payload backend and its
`IRecordProtector`, then register a separate durable, linearizable authority. Enroll the freshness epoch
before starting replicas, and restrict authority writes separately from payload-store access.

```csharp
services.AddSharedRecordFreshness(_ => authorityStore, deploymentId, enrolledEpoch);
services.AddPubSubRedundancy(options =>
{
    options.Election = PubSubRedundancyElection.LeaseStore;
    options.Mode = PubSubRedundancyMode.Hot;
    options.OwnerId = replicaId;
});
```

`AddPubSubRedundancy` supplies the fence provider for lease-store election. Directly configured
external state and checkpoint stores also require callers to pass the active fence. Explicit
in-memory stores remain available for single-process development.
When upgrading an external backend, stop replicas that use the old lease format before starting
the new version. Keep the enrolled epoch and lease records. A live authenticated legacy grant
cannot be renewed by the new version and must expire before takeover. The next acquisition writes
the new format and increments the retained fence; a released legacy grant can be taken over
immediately. The freshness view doesn't import or trust former unprotected records.
On shutdown, stop the coordinator before disposing the lease store so it can release its grants.
If release doesn't complete, the replacement waits for the grant to expire.

## Sequence-number continuity (Hot standby)

OPC UA Part 14 §7.2.5.4.1 defines the subscriber de-duplication key as `(PublisherId, DataSetWriterId, SequenceNumber)`. For a redundant publisher, the shared `PublisherId` and identical writer configuration mean the promoted standby must continue the same logical sequence stream.

On promotion, Hot standby restores each writer to its stored sequence checkpoint + 1,000, advancing the local counter only if that value is higher. The writer checkpoints with the same activation fence it held across publication. Sequence advances after the last checkpoint and wrap can still produce duplicates or gaps.

Cold and Warm may restart sequences, requiring subscribers to reset de-duplication state. Distinguish a restart from normal sequence-number wrap.

## Secured publisher nonce reservations

The security header's 32-bit nonce SequenceNumber is separate from the Hot standby DataSetWriter checkpoint.
Subscribers retain nonce reuse history across publishers for each active
token. The history grows in bounded segments without dropping older
segments; plan key rotation for the aggregate message rate of every
publisher sharing a token.
In every redundancy mode, secured UADP writers, including chunked messages, reserve nonce ranges before sending.
A promoted or restarted writer skips unused numbers in the previous range; resuming does not reset subscriber
replay checks. When the current SecurityTokenId runs out of numbers, publication is withheld until a new
key and SecurityTokenId are available. Scopes sharing a token also share its sequence budget.

Select `PubSubRedundancyElection.LeaseStore` for secured publication with `AddPubSubRedundancy`.
Whole-instance `ILeaderElection` does not expose a fencing token, so it cannot authorize secured publication
without a custom `IPubSubActivationFenceProvider`. With direct construction, use
`PubSubApplicationBuilder.WithSecuredRedundancy` to supply the coordinator, the reservation store, and
the current lease fence source.

Use the same durable, linearizable authority for leases and reservations on every replica. The standard
distributed lease registration requires an enrolled `FreshnessProtectedKeyValueStore`, which protects both
records from rollback of shared payloads. A custom store must preserve equivalent monotonic state across
restarts. Keep the same component identity across replicas
and include PublisherId and distinct WriterGroupId fields on secured data messages so that separate writer
leases do not contend for one wire replay scope. Discovery and Action output uses a separate connection
control lease.
Start with fresh keys and token IDs when enabling reservations on an existing publisher or after loss of
the durable authority; the stack cannot recover the nonce sequences sent before that point.

## Shared security keys (SKS)

PubSub security depends on SecurityGroup keys supplied by SKS. `SharedStorePubSubSecurityKeyStore` implements `IPubSubSecurityKeyStore` on top of `ISharedKeyValueStore` and protects every stored SecurityGroup record with `IRecordProtector`. It persists the SecurityGroup id, security policy URI, key lifetime, current and future `PubSubSecurityKey` values, authorized caller identities, and role permissions.

The practical effect is that a promoted standby can publish, decrypt, or validate secured PubSub messages with the same current and future SecurityGroup keys as the failed active without first pulling fresh keys from SKS. If a protected record cannot be verified and decrypted, the store fails closed and does not apply it.

Networked key stores require a real authenticated-encryption protector. `NullRecordProtector` is only suitable for single-process demos and tests; do not use it with a networked shared store in production.

## Consistency: eventual vs strong

The reusable redundancy layer exposes `RedundancyConsistencyMode`:

- **Eventual.** Bulk replicated state uses the leaderless CRDT / gossip store, while linearizable keyspaces are routed to a strong store. This is the default model for high-throughput state that can tolerate convergence delay.
- **Strong.** All shared state uses a linearizable Raft-backed `ISharedKeyValueStore`. Choose this when every write must be quorum-confirmed.

External PubSub leases, Warm/Hot runtime state, and Hot checkpoints require authenticated records
committed against the enrolled freshness authority. Raw linearizable CAS alone is insufficient. The
authority retains record generations, and release retains the last lease fence. Don't delete these
records to reset ownership. A replica stops using an unavailable or invalid record instead of
recreating it. A committed newer fence prevents older or unfenced writes to the same state or
checkpoint, but does not recover [sequence numbers never checkpointed](#sequence-number-continuity-hot-standby).

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
