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

### 1. Async `ISubscriptionStore` for async persistence backends

The core subscription **definition**-persistence contract (`ISubscriptionStore`) is synchronous, so subscription definitions can only be persisted to a synchronously-completing backend (in-memory, CRDT). Persisting definitions to an async network backend would require an **async `ISubscriptionStore`** variant. (The runtime retransmission mirror already has its async seam; this is only the definition-persistence contract.)

### 2. Transparent-redundancy worked deployment sample

The server side of transparent redundancy is implemented (`RedundancySupport.Transparent` → `CurrentServerId` + `TransparentRedundancyType`, HotAndMirrored/Transparent CRDT state mirroring, `DeterministicEventIdProvider`) and the shared-certificate operational guidance is documented. What remains is a **worked single-virtual-endpoint sample**: a Kubernetes `Service` / load balancer fronting replicas that share one ApplicationInstanceCertificate, tying the shared session store to subscription transfer end-to-end. Extend `Applications/RedundantServer` with a `docker-compose.transparent.yml` + shared-cert/KEK provisioning, and document the single-endpoint client experience.

### 3. Large-address-space hydration optimization — deferred

Full address-space materialization on startup and on failover promotion can be slow for very large address spaces. A **snapshot + delta** (or lazy-materialization) hydration path is deferred. This is a known limitation, not yet optimized; the current path fully materializes.

## Notes

- CRDT active/active and Raft strong consistency were listed as "deferred" in the original plans; both are now **delivered** in-package and are therefore intentionally absent from the remaining work above.
- The original "reconnect using just the AuthenticationToken" idea shipped only in its **safe** form (token = lookup key; admission always requires a full `ActivateSession` client-signature check against a single-use mirrored nonce). No token-only reconnect remains to be done.

## References

- Shipped docs: `Docs/HighAvailability.md`, `Docs/Kubernetes.md`.
- Code: `Libraries/Opc.Ua.Redundancy`, `Libraries/Opc.Ua.Redundancy.Server`, `Libraries/Opc.Ua.Redundancy.Client`, `Libraries/Opc.Ua.Redundancy.K8s`; `Applications/RedundantServer`, `Applications/RedundantClient`.
- Superseded plans consolidated here (see git history): `plans/28-distributed-ha-node-state.md`, `29-distributed-ha-followup.md`, `30-distributed-ha-session-security.md`, `31-distributed-ha-session-manager.md`, `32-distributed-ha-followup-2.md`.
