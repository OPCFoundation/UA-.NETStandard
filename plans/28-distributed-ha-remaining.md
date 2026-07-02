# Distributed / High-Availability Server — Remaining Work

## Status

The distributed / high-availability feature is implemented, tested, documented, and shipping on the `nodestatestorage` branch (draft PR OPCFoundation/UA-.NETStandard#3918). This file consolidates the former plans 28–32 down to the work that is **not yet delivered**. Everything else those plans described is done and is described in the shipped docs.

## Baseline (already delivered — do not re-plan)

See `Docs/HighAvailability.md` and `Docs/Kubernetes.md` for the full, current design. In summary, the following are complete:

- Provider-based shared address-space state (topology + values) behind `ISharedKeyValueStore` / `INodeStateStore`, with the address-space synchronizer, opt-in node-manager adapter, and DI/fluent wiring (`UseReplicatedAddressSpace` / `UseDistributedAddressSpace` and the `IServerStartupTask` seam). Default path (no store) is unchanged and zero-overhead.
- Leader election (static, shared-store lease CAS, and native Raft leadership), dynamic `Server.ServiceLevel` (`IServiceLevelProvider`), and `Server.ServerRedundancy` population for both transparent (`CurrentServerId`) and non-transparent (`ServerUriArray` / `RedundantServerArray`) modes.
- Distributed value cache (read/write callback participation with freshness).
- Secure session mirroring: `DistributedSessionManager` via the `ISessionManagerFactory` seam, encrypted + integrity-protected records (`IRecordProtector` / `AesCbcHmacRecordProtector` / `KeyRingRecordProtector`), cross-replica single-use nonce CAS, full `ActivateSession` signature verification on restore (the token is a lookup key only), and restore audit. Safe default is re-auth on failover; mirrored fast reconnect is opt-in.
- Client token-reuse failover (`ManagedSession` / `WithTokenReuseFailover`), network redundancy endpoint alternates, and HotAndMirrored state mirroring with deterministic EventIds (`DeterministicEventIdProvider`).
- Both consistency backends in-package over the NanoMsg transport, selectable via `UseRedundancyConsistency`: **CRDT** (eventual, active/active, leaderless) and **Raft** (`RaftCs`, linearizable strong consistency for `nonce/` / `lease/` / `election/`). Kubernetes wiring via `UseKubernetesRaftConsensus`.
- Libraries `Opc.Ua.Redundancy`, `Opc.Ua.Redundancy.Server`, `Opc.Ua.Redundancy.Client`, `Opc.Ua.Redundancy.Kubernetes`; samples `Applications/RedundantServer` (+ `docker-compose` active/active, active/passive, Raft) and `Applications/RedundantClient`; the `Docs/Kubernetes.md` deployment guide.
- Security findings F1–F7 and F9 from the security assessment are closed in code.

## Remaining work

### 1. True on-demand fault-in for very large, sparsely-accessed graphs — deferred

Hydration now uses a snapshot + bounded delta log (fast time-to-ready without transferring one key/value entry per node), but a standby still fully materializes the mirrored graph. The next step — materializing a node only when it is first browsed/read, keeping only a hot subset resident — would further cut time-to-ready and steady-state memory for very large, sparsely-accessed address spaces. It requires an asynchronous node-resolution seam through the core node-manager read/browse contract (today `PredefinedNodes.TryGetValue` is synchronous at dozens of call sites), so it is a larger, higher-risk change scoped separately. The eventual-consistency CRDT path already exchanges a snapshot plus deltas.

### 2. Monitored-item data/event queue restore on failover — deferred

`SharedKeyValueSubscriptionStore` restores subscription definitions and retransmission state, but the per-monitored-item data/event queues are not restored (`RestoreDataChangeMonitoredItemQueue`/`RestoreEventMonitoredItemQueue` run on the synchronous monitored-item creation path). After failover an item resumes sampling and delivers fresh values, but values queued on the failed replica and not yet published are lost. Restoring the queues needs the monitored-item creation path to accept an async restore (or a pre-hydrated queue) without blocking. Documented as a Note in `Docs/HighAvailability.md`.

### 3. Transparent client redundancy via DI (`ISession`-shaped, coordination hidden) — feedback follow-up

Today `Applications/RedundantClient` wires the client replica set explicitly (`ClientReplicaSetBuilder`, election, shared store, standby mode). Generalize this in `Opc.Ua.Client` / `Opc.Ua.Redundancy.Client` so a user can register client redundancy through DI (key store, shared store, election, protector) and receive a single `ISession`-shaped handle whose failover/coordination and underlying session churn are fully transparent — the user reasons only about `ISession`, not about coordinators, leaders, or the sessions being managed underneath. Source: PR #3918 review (`Applications/RedundantClient/Program.cs`).

### 4. Single env-configurable server `docker-compose` — feedback follow-up

Collapse the per-mode compose files into one `docker-compose.yml` that selects the topology from an environment variable: active/active eventual (default), active/active strong, and active/passive; and make the replica count configurable. Source: PR #3918 review (`Applications/RedundantServer/docker-compose.active-active.yml`).

### 5. Client `docker-compose` + a runnable replication demo — feedback follow-up

Add a `docker-compose.yml` for `Applications/RedundantClient` to run either a single client or a multi-replica client set against the server, and provide a way to observe replication/failover working (e.g. scale up clients to saturate one server replica and watch load direction / failover). Source: PR #3918 review (`Applications/RedundantServer/README.md`).

### 6. Run the `UAClient.cs` sample suite against the redundant `ISession` — feedback follow-up

Add an option to `Applications/RedundantClient` to run the existing `UAClient.cs` sample "suite" (browse/read/subscribe workflow) against the redundant session, so the sample exercises real client workloads over the failover-capable session. Source: PR #3918 review (`Applications/RedundantClient/Program.cs`).

## Delivered in this iteration

- **Snapshot + delta-log hydration (fast time-to-ready).** Records carry a single-writer monotonic sequence; the optional `INodeStateSnapshotStore` capability (implemented by `InMemoryNodeStateStore`) publishes a chunked, atomically-swapped snapshot and a bounded delta log. `AddressSpaceSynchronizer.SeedOrHydrateAsync` hydrates a standby from the snapshot (one bulk `ILocalAddressSpace.AddOrUpdateRangeAsync`, no per-node event) then replays the delta log after the snapshot sequence, with a per-key sequence guard that makes the snapshot/delta-log/live-feed apply paths idempotent. The writer publishes an initial snapshot after seeding and re-snapshots on a write-count threshold. Streamed `EnumerateAsync`/`EnumerateValuesAsync` remains the fallback. Correctness + benchmark tests added. Documented in `Docs/HighAvailability.md`.

## Notes

- CRDT active/active and Raft strong consistency were listed as "deferred" in the original plans; both are now **delivered** in-package and are therefore intentionally absent from the remaining work above.
- The original "reconnect using just the AuthenticationToken" idea shipped only in its **safe** form (token = lookup key; admission always requires a full `ActivateSession` client-signature check against a single-use mirrored nonce). No token-only reconnect remains to be done.

## References

- Shipped docs: `Docs/HighAvailability.md`, `Docs/Kubernetes.md`.
- Code: `Libraries/Opc.Ua.Redundancy`, `Libraries/Opc.Ua.Redundancy.Server`, `Libraries/Opc.Ua.Redundancy.Client`, `Libraries/Opc.Ua.Redundancy.Kubernetes`; `Applications/RedundantServer`, `Applications/RedundantClient`.
- Superseded plans consolidated here (see git history): `plans/28-distributed-ha-node-state.md`, `29-distributed-ha-followup.md`, `30-distributed-ha-session-security.md`, `31-distributed-ha-session-manager.md`, `32-distributed-ha-followup-2.md`.
