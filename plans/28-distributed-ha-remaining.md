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
- Libraries `Opc.Ua.Redundancy`, `Opc.Ua.Redundancy.Server`, `Opc.Ua.Redundancy.Client`, `Opc.Ua.Redundancy.K8s`; samples `Applications/RedundantServer` (+ `docker-compose` active/active, active/passive, Raft) and `Applications/RedundantClient`; the `Docs/Kubernetes.md` deployment guide.
- Security findings F1–F7 and F9 from the security assessment are closed in code.

## Remaining work

### 1. Large-address-space hydration optimization — deferred

Full address-space materialization on startup and on failover promotion can be slow for very large address spaces. A **snapshot + delta** (or lazy-materialization) hydration path is deferred. This is a known limitation, not yet optimized; the current path fully materializes.

## Delivered in this iteration

- **Async `ISubscriptionStore` definition-persistence contract.** `StoreSubscriptions`/`RestoreSubscriptions`/`OnSubscriptionRestoreComplete` are now `StoreSubscriptionsAsync`/`RestoreSubscriptionsAsync`/`OnSubscriptionRestoreCompleteAsync` (`ValueTask`-returning, `CancellationToken`). Subscription definitions can now be persisted to an async network backend without a sync-over-async wrapper; `SharedKeyValueSubscriptionStore` awaits its shared-store writes directly instead of fire-and-forget. The per-monitored-item queue-restore hooks stay synchronous (synchronous monitored-item creation path). Documented in `Docs/migrate/2.0.x/sessions-subscriptions.md` and `Docs/HighAvailability.md`.
- **Transparent-redundancy worked sample.** `Applications/RedundantServer/docker-compose.transparent.yml` runs two `REDUNDANCY_MODE=transparent` replicas that share one `ApplicationUri` and one `ApplicationInstanceCertificate` (seeded into a shared PKI volume) and mirror session + address-space state by CRDT gossip, behind an `nginx` TCP load balancer (`nginx.transparent.conf`) that publishes a single virtual endpoint. `Program.cs` gained `HA_APPLICATION_URI` / `HA_SUBJECT_NAME` / `HA_PKI_ROOT` for the shared identity; each replica advertises the load-balancer host (bind-to-any for DNS hosts), so discovery and `CreateSession` echo the single endpoint and a mirrored session resumes on the survivor after a replica fails. Documented in `Applications/RedundantServer/README.md` and `Docs/HighAvailability.md`.

## Notes

- CRDT active/active and Raft strong consistency were listed as "deferred" in the original plans; both are now **delivered** in-package and are therefore intentionally absent from the remaining work above.
- The original "reconnect using just the AuthenticationToken" idea shipped only in its **safe** form (token = lookup key; admission always requires a full `ActivateSession` client-signature check against a single-use mirrored nonce). No token-only reconnect remains to be done.

## References

- Shipped docs: `Docs/HighAvailability.md`, `Docs/Kubernetes.md`.
- Code: `Libraries/Opc.Ua.Redundancy`, `Libraries/Opc.Ua.Redundancy.Server`, `Libraries/Opc.Ua.Redundancy.Client`, `Libraries/Opc.Ua.Redundancy.K8s`; `Applications/RedundantServer`, `Applications/RedundantClient`.
- Superseded plans consolidated here (see git history): `plans/28-distributed-ha-node-state.md`, `29-distributed-ha-followup.md`, `30-distributed-ha-session-security.md`, `31-distributed-ha-session-manager.md`, `32-distributed-ha-followup-2.md`.
