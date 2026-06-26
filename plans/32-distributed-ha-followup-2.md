# Plan 32 — Distributed HA: follow-up work (post S5/S6)

## Status

**Planning.** The distributed-HA feature (plans [28](28-distributed-ha-node-state.md) / [29](29-distributed-ha-followup.md) / [30](30-distributed-ha-session-security.md) / [31](31-distributed-ha-session-manager.md)) is implemented and tested through **S1–S5 + the S6 mirror integration test** (3 commits on `nodestatestorage`; 98 Distributed tests pass on net10 + net48; no regression). This plan enumerates and prioritizes the remaining follow-up work and the items deliberately deferred during that effort.

## What is already done (for reference)

- Shared k/v store, node-state store, address-space synchronizer, leader election (static + lease CAS), service level, server redundancy, distributed value cache — wired via `IServerStartupTask` + `UseDistributedAddressSpace(...)`.
- Store hardening: `IRecordProtector` (AES-256-CBC Encrypt-then-MAC, fail-closed), `KeyRingRecordProtector` (rotation), `SharedSingleUseNonceRegistry` (cross-replica single-use nonce), key zeroization.
- Secure session sharing: `DistributedSessionManager` (mirror on create/activate, restore with REQ-UA-7 + single-use nonce + full ActivateSession signature; token = lookup key) via the `ISessionManagerFactory` seam + `UseDistributedSessions(...)`. Safe default = re-auth on failover.
- Real-server mirror integration test.

## Remaining work (security findings still open)

From [Plan 30](30-distributed-ha-session-security.md)'s findings, these are **not yet fully closed in code**:

- **F6 (MEDIUM)** — the session store keys entries by the **raw `AuthenticationToken`** (`SharedKeyValueSessionStore.cs:147` `"session/" + token`). On a network backend this leaks the token handle via `SCAN` / `MONITOR` / RDB / slowlog. The nonce registry already hashes its keys; the session store should too.
- **F8 (HIGH)** — replica↔store transport authentication (mutual-TLS, fail-closed in production) is **documentation-only**; it becomes enforceable only with a real network backend (Redis).
- **F9 (MEDIUM)** — cross-replica session restore is currently **logged** (token digest provenance) but does not emit a formal `AuditActivateSession` / "session restored" audit event through the server audit APIs.
- **F10 (LOW)** — no **TTL / eviction** for `session/` and consumed-`nonce/` entries; bounded only by the in-memory process lifetime today. Native with Redis.
- **F7 (MEDIUM)** — key material is zeroized, but the **restore path's** decrypted nonce/identity buffers are not explicitly zeroized after use.

## Workstreams

### A. Security-hardening completion (in-repo, no external dependencies) — recommended first

Independent, fully testable in this environment, closes the residual findings.

- **A1 — Keyspace HMAC for the session store (F6).** Key session entries by `HMAC-SHA256(keyspaceKey, token)` (or the SHA-256 digest already used by `SharedSingleUseNonceRegistry.Digest`) instead of the raw token. Keep the protector for the value. Update `SharedKeyValueSessionStore` + tests (round-trip, cross-replica visibility still works, raw key no longer contains the token).
- **A2 — Formal restore audit (F9).** Emit `AuditActivateSession` plus a distinct "session restored from shared store" audit with provenance (source token-hash, reason) through the server's audit/redaction APIs (not just `ILogger`). Wire from `DistributedSessionManager.RestoreSessionAsync`.
- **A3 — Restore-path zeroization (F7).** Zeroize decrypted `serverNonce` / identity buffers after the restored session consumes them (mirror the `EncryptedSecret` zeroization pattern).

### B. Client-side spec-compliant fast reconnect (REQ-UA-13) — makes the feature consumable

Today this stack's client does **re-auth on failover** (fresh `CreateSession`), so the server-side mirrored fast-reconnect is never exercised by it.

- **B1 — Token-reuse failover in the client.** Add an opt-in path in `ManagedSession` (and/or `Session`) so that on failover to a higher-`ServiceLevel` redundant server it opens a new SecureChannel and re-runs **`ActivateSession` reusing the existing `AuthenticationToken`** (signing over the new channel + last `serverNonce`), per OPC UA Part 4 §6.6.2.4.5.5 / REQ-UA-13, instead of `CreateSession`. Falls back to re-auth when the standby rejects the token. This is the client counterpart to the server-side `DistributedSessionManager` and unblocks workstream C.

### C. Secured two-server end-to-end test (depends on B) — completes S6

- **C1 — Full secured failover e2e.** Two in-process servers sharing one store **and a shared `ApplicationInstanceCertificate`** (REQ-UA-14), a real client performing the B1 token-reuse failover. Assert: reconnect succeeds with a valid signature; a captured/replayed activation is rejected (nonce single-use); a token-only / wrong-signature reconnect is rejected; a tampered mirrored record is rejected; session secrets never appear in cleartext; the highest `ServiceLevel` replica is selected.

### D. Redis backend (BLOCKED in this environment — needs network/package) — production backend

`StackExchange.Redis` is **not in the offline NuGet cache**; this workstream needs network access or a pre-populated cache.

- **D1 — `Opc.Ua.Server.Distributed.Redis` package.** `RedisSharedKeyValueStore : ISharedKeyValueStore` — `TryGet/Set/Delete` → string ops; `CompareAndSwap` → atomic Lua (set-if-absent / compare-and-set); `ScanAsync` → `SCAN` by prefix; `WatchAsync` → keyspace-notification pub/sub. Add `StackExchange.Redis` (MIT) to `Directory.Packages.props`. Separate package keeps core dependency-free.
- **D2 — Transport + availability (F8, F10).** Require mutual-TLS + auth, fail-closed when absent in production; least-privilege per-replica credential; native Redis **TTL** for `session/` + `nonce/` entries; bounded watch channels / keyspace caps.
- **D3 — AOT + tests.** StackExchange.Redis reflection may not be AOT-safe → AOT tests in `Opc.Ua.Aot.Tests` exercising the Redis paths, or mark non-AOT + document. Integration tests gated on a `REDIS_URL` / testcontainers.

### E. Active/active correctness — CRDT (further deferred)

- **E1 — Conflict-free store.** A CRDT-based `ISharedKeyValueStore` / node-state store for true active/active without single-writer leader election (the current A/A relies on the lease writer). Large research/design effort; remains deferred and needs its own ADR.

### F. Transparent redundancy + subscription transfer (polish)

- **F1 — Transparent-redundancy helper + sample.** A worked path for a single virtual endpoint (k8s `Service`/LB) hiding failover, tying the shared session store (B/S5) to subscription transfer ([TransferSubscription.md](../Docs/TransferSubscription.md)). Mostly deployment + sample; no new transport.

### G. Docs / sample (polish)

- **G1 — `Docs/KubernetesDeployment.md`.** Replicaset, headless `Service`, Lease election, readiness tied to `ServiceLevel`, Redis wiring, shared cert/KEK provisioning.
- **G2 — Sample extension.** Extend `Applications/HighAvailabilityServer` to demonstrate `UseDistributedSessions` (+ Redis when D lands).

## Recommended order & dependencies

1. **A (A1, A2, A3)** — in-repo security completion; quick, no external deps. Closes F6/F7/F9.
2. **B (B1)** — client token-reuse failover; makes the feature usable; unblocks C.
3. **C (C1)** — full secured e2e; depends on B.
4. **D (D1–D3)** — Redis; **blocked offline** (do when network/cache available). Closes F8/F10.
5. **F, G** — transparent-redundancy sample + k8s docs.
6. **E** — CRDT; long-term, separate ADR.

Dependencies: C → B; D2 (F8/F10) → D1; G2-Redis → D1.

## Open decisions (need requester input)

1. **Priority / scope for this iteration** — which workstream(s) to pursue now? (A is the recommended immediate start; D is environment-blocked.)
2. **Client fast-reconnect (B1)** — opt-in on `ManagedSession` only, or also low-level `Session`? Default behaviour stays re-auth-on-failover.
3. **Keyspace HMAC key (A1)** — derive from the same deployment KEK as the record protector, or a separate keyspace key?
4. **Redis environment** — is network access / a pre-populated NuGet cache available for D, or should D stay deferred?
5. **CRDT (E)** — confirm it remains out of scope for now.

## References

- Plans 28–31; security findings in [Plan 30](30-distributed-ha-session-security.md).
- `Libraries/Opc.Ua.Server/Distributed/SharedKeyValueSessionStore.cs:147` (raw-token key, F6); `SharedSingleUseNonceRegistry.cs` (`Digest` keyspace-hash pattern).
- `Libraries/Opc.Ua.Client/Session/ManagedSession.cs`, `SessionReconnectHandler.cs`, `DefaultServerRedundancyHandler` (client failover, B1).
- `Docs/HighAvailability.md` / `HighAvailabilitySecurity.md` (status + deployment guidance).
