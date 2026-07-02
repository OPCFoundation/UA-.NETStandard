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
- Libraries `Opc.Ua.Redundancy`, `Opc.Ua.Redundancy.Server`, `Opc.Ua.Redundancy.Client`, `Opc.Ua.Redundancy.Kubernetes`; samples `Applications/RedundantServer` (a single env-configurable `docker-compose.yml` plus the `transparent` and `loaddirection` demo files) and `Applications/RedundantClient` (a managed client, an in-process client replica set, and a transparent `RedundantClientSession`); the `Docs/Kubernetes.md` deployment guide.
- Security findings F1–F7 and F9 from the security assessment are closed in code.

## Remaining work

### 1. True on-demand fault-in for very large, sparsely-accessed graphs — deferred

Hydration now uses a snapshot + bounded delta log (fast time-to-ready without transferring one key/value entry per node), but a standby still fully materializes the mirrored graph. The next step — materializing a node only when it is first browsed/read, keeping only a hot subset resident — would further cut time-to-ready and steady-state memory for very large, sparsely-accessed address spaces. It requires an asynchronous node-resolution seam through the core node-manager read/browse contract (today `PredefinedNodes.TryGetValue` is synchronous at dozens of call sites), so it is a larger, higher-risk change scoped separately. The eventual-consistency CRDT path already exchanges a snapshot plus deltas.

### 2. Monitored-item data/event queue restore on failover — deferred

`SharedKeyValueSubscriptionStore` restores subscription definitions and retransmission state, but the per-monitored-item data/event queues are not restored (`RestoreDataChangeMonitoredItemQueue`/`RestoreEventMonitoredItemQueue` run on the synchronous monitored-item creation path). After failover an item resumes sampling and delivers fresh values, but values queued on the failed replica and not yet published are lost. Restoring the queues needs the monitored-item creation path to accept an async restore (or a pre-hydrated queue) without blocking. Documented as a Note in `Docs/HighAvailability.md`.

### 3. Integration coverage for the transparent client session facade — follow-up

`RedundantClientSession` ships with unit tests for the leadership gate (block until leader), the session swap, the `BadInvalidState` guard on synchronous members before leadership, DI resolution, and the fail-closed protector — but they drive the facade with mocked sessions. What is not yet covered is an end-to-end integration test: a real `ClientReplicaCoordinator` set against a running server that promotes a follower and runs browse/read/subscribe through the facade across a forced leader change, proving the event re-wiring, remembered-property re-apply, and block-until-leader behaviour over live sessions. This belongs in an integration suite (e.g. `Tests/Opc.Ua.Sessions.Tests` or a dedicated redundancy integration project), not the current unit tests.

### 4. Optional future enhancements — not required

- **Dynamic compose replica scaling.** The consolidated `Applications/RedundantServer/docker-compose.yml` runs a fixed 2–3 replica set with static per-service peer lists. A truly arbitrary replica count would need server-side peer discovery (for example DNS `tasks.<service>` enumeration or a headless-service lookup) so `docker compose up --scale server=N` self-configures its gossip/Raft/redundant peers instead of the hard-coded lists.
- **Warm/Hot client standby in the sample.** `Applications/RedundantClient --replicas` uses `ClientStandbyMode.Cold`. A Warm/Hot demonstration (the standby keeps a connected session, or a session with sampling-only subscriptions, and enables publishing on promotion) plus running `--suite` through the promoted leader's `RedundantClientSession` would exercise those standby paths end to end.

## Delivered in this iteration

- **Snapshot + delta-log hydration (fast time-to-ready).** Records carry a single-writer monotonic sequence; the optional `INodeStateSnapshotStore` capability (implemented by `InMemoryNodeStateStore`) publishes a chunked, atomically-swapped snapshot and a bounded delta log. `AddressSpaceSynchronizer.SeedOrHydrateAsync` hydrates a standby from the snapshot (one bulk `ILocalAddressSpace.AddOrUpdateRangeAsync`, no per-node event) then replays the delta log after the snapshot sequence, with a per-key sequence guard that makes the snapshot/delta-log/live-feed apply paths idempotent. The writer publishes an initial snapshot after seeding and re-snapshots on a write-count threshold. Streamed `EnumerateAsync`/`EnumerateValuesAsync` remains the fallback. Correctness + benchmark tests added. Documented in `Docs/HighAvailability.md`.
- **Transparent client redundancy via DI (`ISession`-shaped).** `AddRedundantClientSession(...)` (in `Opc.Ua.Redundancy.Client`) wires the `ClientReplicaCoordinator` + an `IHostedService` lifecycle and exposes a single stable `RedundantClientSession : ISession`. The facade forwards the full `ISession`/service surface to the current leader session, re-wires events across leader swaps, remembers settable properties, blocks async calls until this replica is leader with a live session (sync members throw `BadInvalidState` until then), and is fail-closed on the record protector. `RedundantClientSessionBuilder` is the non-DI equivalent; `ClientReplicaSetBuilder`/`ClientReplicaCoordinator` remain the lower-level seam. Unit tests + `Docs/HighAvailability.md` example added.
- **Single env-configurable server `docker-compose.yml`.** One `Applications/RedundantServer/docker-compose.yml` chooses the topology from the environment (`active-active-eventual.env` default, `active-active-strong.env`, `active-passive.env`); the two per-mode files are folded in. `docker compose config` renders for all three modes. `docker-compose.transparent.yml`/`docker-compose.loaddirection.yml` remain as distinct demos.
- **Client compose + replication/failover demo.** A `clients`/`demo` compose profile runs 1..N `RedundantClient` replicas (`CLIENT_REPLICAS`); the README adds a step-by-step recipe to stop the active/leader replica and watch the mirrored `HighAvailability.Counter` continue.
- **`--suite` sample workload.** `Applications/RedundantClient --suite` runs a browse/read/subscribe workload against the redundant `ISession` (a NativeAOT-safe inline mirror of the `ConsoleReferenceClient` `ClientSamples` suite; the sample AOT-publishes clean).

## Notes

- CRDT active/active and Raft strong consistency were listed as "deferred" in the original plans; both are now **delivered** in-package and are therefore intentionally absent from the remaining work above.
- The original "reconnect using just the AuthenticationToken" idea shipped only in its **safe** form (token = lookup key; admission always requires a full `ActivateSession` client-signature check against a single-use mirrored nonce). No token-only reconnect remains to be done.

## References

- Shipped docs: `Docs/HighAvailability.md`, `Docs/Kubernetes.md`.
- Code: `Libraries/Opc.Ua.Redundancy`, `Libraries/Opc.Ua.Redundancy.Server`, `Libraries/Opc.Ua.Redundancy.Client`, `Libraries/Opc.Ua.Redundancy.Kubernetes`; `Applications/RedundantServer`, `Applications/RedundantClient`.
- Superseded plans consolidated here (see git history): `plans/28-distributed-ha-node-state.md`, `29-distributed-ha-followup.md`, `30-distributed-ha-session-security.md`, `31-distributed-ha-session-manager.md`, `32-distributed-ha-followup-2.md`.
