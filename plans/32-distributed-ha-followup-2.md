# Plan 32 — Distributed HA: follow-up work (post S5/S6)

## Status

**Planning.** The distributed-HA feature (plans [28](28-distributed-ha-node-state.md) / [29](29-distributed-ha-followup.md) / [30](30-distributed-ha-session-security.md) / [31](31-distributed-ha-session-manager.md)) is implemented and tested through **S1–S5 + the S6 mirror integration test** (3 commits on `nodestatestorage`; 98 Distributed tests pass on net10 + net48; no regression). This plan enumerates and prioritizes the remaining follow-up work and the items deliberately deferred during that effort.

## What is already done (for reference)

- Shared k/v store, node-state store, address-space synchronizer, leader election (static + lease CAS), service level, server redundancy, distributed value cache — wired via `IServerStartupTask` + `UseDistributedAddressSpace(...)`.
- Store hardening: `IRecordProtector` (AES-256-CBC Encrypt-then-MAC, fail-closed), `KeyRingRecordProtector` (rotation), `SharedSingleUseNonceRegistry` (cross-replica single-use nonce), key zeroization.
- Secure session sharing: `DistributedSessionManager` (mirror on create/activate, restore with REQ-UA-7 + single-use nonce + full ActivateSession signature; token = lookup key) via the `ISessionManagerFactory` seam + `UseDistributedSessions(...)`. Safe default = re-auth on failover.
- Real-server mirror integration test.

## Implementation status (this follow-up)

- **A — DONE** (commit `245b6237a`): F6 session-key hashing (`SharedKeyValueSessionStore.KeyFor`), F9 restore audit (`IAuditEventServer.ReportAuditSessionRestoredEvent` + wired in the manager), F7 analyzed (no extra plaintext copy in the manager; `Nonce.Data` zeroization is a pre-existing server-wide Core concern, tracked separately). 99 Distributed tests pass (net10 + net48).
- **FG — DONE** (commit `f6acbdc7d`): `Docs/KubernetesDeployment.md` (linked from `Docs/README.md` + `HighAvailability.md`); the `RedundantServer` sample now wires `UseDistributedSessions` + an optional `AesCbcHmacRecordProtector` from `HA_RECORD_KEY`.
- **B — DONE** (commit `652c85a1f`): opt-in client token-reuse failover (REQ-UA-13). `Session.EnableTokenReuseFailover` + extracted `ReactivateExistingSessionAsync` (shared with `UpdateSessionAsync`); `RecreateInPlaceCoreAsync` tries token-reuse against the failover server (adopting its cert) before the existing fresh-`CreateSession` fallback; `ManagedSessionBuilder.WithTokenReuseFailover()` + option. `OpenAsync` untouched. No regression (57 client SessionTests + 135 reconnect/failover integration tests pass).
- **C — DONE** (commit pending): `DistributedSessionFailoverIntegrationTests` — two secured servers sharing one store via `DistributedSessionManager`, a client with token-reuse failover fails over from A to B; the standby restores the mirrored session and re-activates with the reused token, so the client's `SessionId` is **preserved** (proving restore, not fresh `CreateSession`). Passes on net10 + net48.
- **D / E — deferred** (D blocked offline; E long-term).

All A/FG work is committed and pushed to the draft PR `OPCFoundation/UA-.NETStandard#3918`.


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

#### B1 — validated implementation design (ready to build)

Investigated and de-risked against the current client (file:line are `Libraries/Opc.Ua.Client/Session/Session.cs` unless noted):

- **No shared certificate needed.** The failover client signs the activation over the certificate of the server it is *currently* connecting to (`OpenAsync` parses + validates `m_endpoint.Description.ServerCertificate` at `:1247-1278`, before `CreateSession`), and the standby's restored session validates with **its own** instance certificate (from the `serverCertificateProvider`). Only the `serverNonce` (+ client cert + policy) is mirrored, not the server cert — so replicas may have different certs for non-transparent redundancy.
- **Failover path:** `ManagedSession.HandleFailoverAsync` (`ManagedSession.cs:1091-1158`) → `Session.RecreateInPlaceAsync` → `RecreateInPlaceCoreAsync` (`:2711`). The new channel is built at `:2872`; the session id/token is then **cleared** at `:2878-2881` to force a fresh `CreateSession` in `OpenAsync` (`:2893`).
- **Safest change (gated, fallback built-in):** add `bool reuseExistingSession = false` to `OpenAsync` (`:1220`). Default `false` ⇒ every existing caller is byte-for-byte unchanged. The reuse branch skips only the `CreateSession` block (`:1281-1398`) + its response-validation (`:1385-1401`), keeps the existing `m_clientNonce`, sets `serverNonce = m_serverNonce`, and falls through to the **unchanged** activation code (`:1403-1520`) which signs over the new channel and calls `ActivateSession` reusing the base-held `AuthenticationToken`.
- In `RecreateInPlaceCoreAsync`, **before** the clear at `:2878`, when the opt-in flag is set and a valid token + `m_serverNonce` exist, `try { await OpenAsync(..., reuseExistingSession: true) }`; on **any** exception fall through to the existing clear + `OpenAsync(reuseExistingSession:false)` (the built-in re-auth fallback) — so failover always succeeds.
- **Opt-in surface:** `ManagedSessionOptions.EnableTokenReuseFailover` (`ManagedSessionOptions.cs:91-129`) + `ManagedSessionBuilder.WithTokenReuseFailover(...)` (`Fluent/ManagedSessionBuilder.cs`), plumbed to a `Session` field. Default off ⇒ no behaviour change.

**Risk:** `OpenAsync` is the central connect method; the gating (defaulted param + the create block left intact inside the gate) provably preserves the default path, and the fresh-`CreateSession` fallback preserves failover. Still warrants careful review + the full client `SessionTests` regression run.

### C. Secured two-server end-to-end test (depends on B) — completes S6

- **C1 — Full secured failover e2e.** Two in-process servers sharing one store **and a shared `ApplicationInstanceCertificate`** (REQ-UA-14), a real client performing the B1 token-reuse failover. Assert: reconnect succeeds with a valid signature; a captured/replayed activation is rejected (nonce single-use); a token-only / wrong-signature reconnect is rejected; a tampered mirrored record is rejected; session secrets never appear in cleartext; the highest `ServiceLevel` replica is selected.

### D. Redis backend (BLOCKED in this environment — needs network/package) — production backend

`StackExchange.Redis` is **not in the offline NuGet cache**; this workstream needs network access or a pre-populated cache.

- **D1 — `Opc.Ua.Redundancy.Server.Redis` package.** `RedisSharedKeyValueStore : ISharedKeyValueStore` — `TryGet/Set/Delete` → string ops; `CompareAndSwap` → atomic Lua (set-if-absent / compare-and-set); `ScanAsync` → `SCAN` by prefix; `WatchAsync` → keyspace-notification pub/sub. Add `StackExchange.Redis` (MIT) to `Directory.Packages.props`. Separate package keeps core dependency-free.
- **D2 — Transport + availability (F8, F10).** Require mutual-TLS + auth, fail-closed when absent in production; least-privilege per-replica credential; native Redis **TTL** for `session/` + `nonce/` entries; bounded watch channels / keyspace caps.
- **D3 — AOT + tests.** StackExchange.Redis reflection may not be AOT-safe → AOT tests in `Opc.Ua.Aot.Tests` exercising the Redis paths, or mark non-AOT + document. Integration tests gated on a `REDIS_URL` / testcontainers.

### E. Active/active correctness — CRDT (further deferred)

- **E1 — Conflict-free store.** A CRDT-based `ISharedKeyValueStore` / node-state store for true active/active without single-writer leader election (the current A/A relies on the lease writer). Large research/design effort; remains deferred and needs its own ADR.

### F. Transparent redundancy + subscription transfer (polish)

- **F1 — Transparent-redundancy helper + sample.** A worked path for a single virtual endpoint (k8s `Service`/LB) hiding failover, tying the shared session store (B/S5) to subscription transfer ([TransferSubscription.md](../Docs/TransferSubscription.md)). Mostly deployment + sample; no new transport.

### G. Docs / sample (polish)

- **G1 — `Docs/KubernetesDeployment.md`.** Replicaset, headless `Service`, Lease election, readiness tied to `ServiceLevel`, Redis wiring, shared cert/KEK provisioning.
- **G2 — Sample extension.** Extend `Applications/RedundantServer` to demonstrate `UseDistributedSessions` (+ Redis when D lands).

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
