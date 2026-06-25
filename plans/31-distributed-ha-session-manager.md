# Plan 31 — Secure Distributed Session Manager (S5) + two-server E2E (S6)

## Status

Implementation plan for the deferred items from [Plan 30](30-distributed-ha-session-security.md): **S5 `f-session-sharing-secure`** and **S6 `f-e2e-test-secure`**. The store-hardening building blocks (encrypted record protector, key-ring rotation, single-use nonce registry, shared session store) are already implemented, tested, and committed (`6e08302bc`). This plan adds the session-manager integration that uses them.

### Implementation progress

- **S5 — DONE.** Implemented and unit-tested:
  - `SharedSessionEntry` extended with the full reconstruction state (serverNonce, clientNonce, client cert blob, SecurityPolicyUri/Mode, endpoint, timeout, client description) + encode/decode (`s5-entry-extend`).
  - Additive `SessionManager.RestoreSessionAsync` + `SupportsSessionRestore` seam in the `ActivateSessionAsync` miss-path — default returns null so existing behaviour is unchanged (`s5-base-seam`).
  - `DistributedSessionManager` — mirrors encrypted session state on create/activate, removes on close, restores on failover with REQ-UA-7 policy/mode check + single-use nonce consume + leaf-cert reconstruction + provenance logging (`s5-distributed-mgr`).
  - `ISessionManagerFactory` seam on `StandardServer` (wired from DI by the hosted service) + `DistributedSessionManagerFactory` + `UseDistributedSessions(...)` fluent API; default `EnableFastReconnect = false` (re-auth on failover) (`s5-di-factory`).
  - Unit tests: security-decision logic (policy match, nonce single-use/replay), entry round-trip, factory + DI registration (`s5-unit-tests`).
  - Docs updated: `HighAvailability.md`, `HighAvailabilitySecurity.md` (`s5-docs`).
  - Verified: 97 Distributed tests (net10) + 92 (net48); 61 Session-category + 57 client SessionTests integration pass (no regression in the live activation path); server + hosted service build clean on net10 + net48.
- **S6 `f-e2e-test-secure` — runtime mirror integration test DONE; secured restore e2e deferred.** Added `DistributedSessionMirrorIntegrationTests`: a real, fully-started server whose `ISessionManagerFactory` builds a `DistributedSessionManager`, verifying end-to-end that a session created/activated through the real service handlers is mirrored **encrypted** to the shared store (wrong-key reader fails closed) and removed on close — closing the factory→`CreateSessionManager`→mirror runtime-wiring gap. The full **secured token-reuse restore** happy-path (a client computing a real `ActivateSession` signature over the mirrored single-use nonce, re-sent on a new channel to a standby with a shared server certificate) remains deferred: the stack's client does re-auth-on-failover and the direct-service helper only drives unsecured sessions. The restore **decision** logic (REQ-UA-7 policy match + single-use nonce/replay) is unit-tested and the base `ActivateSession` signature path is integration-tested (57 client SessionTests).

## Problem & approach

Let an OPC UA client that loses its server replica **fail over to a standby and reconnect by re-running `ActivateSession`** — without a fresh `CreateSession` — while fully preserving the OPC UA session security model. The standby validates the client-certificate signature against the **mirrored, single-use** `serverNonce`; the `AuthenticationToken` is only a lookup key, never an authenticator. This is the spec's HotAndMirrored failover (Part 4 §6.6, REQ-UA-12/13) made safe against the security-review findings (token-only hijack = Finding 2, nonce replay = Finding 1).

The **safe default stays re-authentication on failover** (a fresh `CreateSession`+`ActivateSession`, which already works today with no code change). Mirrored fast-reconnect is an explicit, audited **opt-in**.

## Design (grounded in current code)

Verified facts:

- `SessionManager.CreateSessionAsync` and `ActivateSessionAsync` are `public virtual`; `CreateSession(...)` is `protected virtual`; `StandardServer.CreateSessionManager(...)` is `protected virtual` (`SessionManager.cs:193,346,1022`; `StandardServer.cs:3959`).
- `m_sessions` (the token→session map) is **private** — a subclass cannot insert a restored session, so a small additive base seam is required (`SessionManager.cs:1172`).
- `ActivateSessionAsync` validates via `Session.ValidateBeforeActivate`, which checks the client signature against `m_serverNonce.Data` + `m_serverCertificate` (`Session.cs:579,621-663`) — so a restored session carrying the mirrored nonce + the (shared) server cert validates correctly.
- A `Nonce` can be rebuilt from raw bytes: `Nonce.CreateNonce(string securityPolicyUri, byte[] nonceData)` (`Nonce.cs:250`).
- `Session` ctor state to mirror: `authenticationToken, clientNonce, serverNonce, sessionName, clientDescription, endpointUrl, clientCertificate, clientCertificateChain, sessionTimeout` (`Session.cs:121-165`). `serverCertificate` is identical across replicas (REQ-UA-14) and comes from the server, not the entry. `SecureChannelId`/`EndpointDescription` come from the **new** failover channel (correct).

### 1. Additive base-class seam (backward-compatible)

In `SessionManager.ActivateSessionAsync`, when the locked `m_sessions` lookup misses, call a new hook before throwing `BadSessionIdInvalid`:

```csharp
protected virtual ValueTask<ISession?> RestoreSessionAsync(
    NodeId authenticationToken, OperationContext context, CancellationToken ct)
    => new((ISession?)null);   // default: no restore -> current behaviour
```

On a non-null return the base adds the session to `m_sessions` and continues into the **normal** full validation path (signature, lockout, identity auth, fresh nonce). Default returns null, so existing behaviour and all current tests are unchanged. A companion restore-aware initialize seam preserves the mirrored `SessionId` (instead of allocating a new one in `InitializeAsync`).

### 2. `DistributedSessionManager : SessionManager` (opt-in)

- **Mirror-write:** override `CreateSessionAsync` / `ActivateSessionAsync` → `await base` → on success write the encrypted `SharedSessionEntry` (latest `serverNonce`) to `ISharedSessionStore`. Override `CloseSessionAsync` → remove it.
- **Restore-read:** override `RestoreSessionAsync` → read the entry; **consume the mirrored `serverNonce` via `ISingleUseNonceRegistry`** (reject as replay if already consumed); enforce REQ-UA-7 cross-channel checks (same client certificate, same SecurityPolicy/Mode as mirrored); rebuild the `Nonce`; reconstruct the `Session` via the `CreateSession` factory with the mirrored state + preserved `SessionId`; emit an `AuditActivateSession` plus a distinct "session restored from shared store" audit with provenance (source token-hash, reason). The base then runs the standard `ValidateBeforeActivate` (full client-signature check) — admission still requires a valid signature.

### 3. Extend `SharedSessionEntry`

Add (all encrypted at rest by the S2 protector): `ServerNonce`, `ClientNonce`, `ClientCertificate` (+ chain), `SecurityPolicyUri`, `SecurityMode`, `EndpointUrl`, `SessionTimeout`, `ClientDescription`. Update encode/decode + tests.

### 4. DI / fluent seam

Add `ISessionManagerFactory` consulted by `StandardServer.CreateSessionManager` (resolved from DI/`ServerInternalData`; absent ⇒ the default `new SessionManager(...)`). `DistributedServerBuilderExtensions.UseDistributedSessions(...)` registers the factory + `ISharedSessionStore` + `ISingleUseNonceRegistry` + the shared `IRecordProtector`, with options (failover mode default = re-auth; opt-in mirrored reconnect; protector factory). Default OFF.

## Security invariants (must hold; covered by tests)

1. Token is a **lookup key only** — admission always requires a valid client-certificate signature (full `ValidateBeforeActivate`); a reconnect presenting only the token is rejected. (Finding 2)
2. `serverNonce` is **single-use across the replica set** (CAS registry) — a replayed/consumed nonce is rejected on every replica. (Finding 1)
3. Entries are **encrypted + integrity-protected at rest**; a tampered/forged entry is rejected fail-closed. (Findings 3, 4 — already enforced by S2)
4. REQ-UA-7 cross-channel checks (same client cert, same SecurityPolicy/Mode) enforced on restore.
5. Cross-replica restore is **audited** with provenance. (Finding 9)
6. Mirrored fast-reconnect is **opt-in**; default is re-auth on failover.

## Phased todos

| id | Summary | Deps |
|----|---------|------|
| `s5-entry-extend` | Extend `SharedSessionEntry` (+ encode/decode) with the full mirror state; unit tests incl. encrypted round-trip. | — |
| `s5-base-seam` | Additive `RestoreSessionAsync` hook + restore-aware `SessionId` preservation in `SessionManager`/`Session`; backward-compatible; tests prove default behaviour unchanged. | — |
| `s5-distributed-mgr` | `DistributedSessionManager` (mirror write/remove, restore, nonce CAS consume, REQ-UA-7 checks, audit). | `s5-base-seam`, `s5-entry-extend` |
| `s5-di-factory` | `ISessionManagerFactory` + `StandardServer.CreateSessionManager` wiring + `UseDistributedSessions` fluent API + options (default OFF / re-auth). | `s5-distributed-mgr` |
| `s5-unit-tests` | Mirror round-trip; restore happy-path (valid signature); reject token-only/no-signature; reject replayed/consumed nonce; reject tampered entry; reject mismatched client cert / policy. | `s5-distributed-mgr` |
| `s6-e2e` | Two in-process servers sharing one store + a real `ManagedSession` client: failover reconnect succeeds with a valid signature; replay/hijack/tamper rejected; secret never appears in cleartext; highest `ServiceLevel` selected. | `s5-di-factory` |
| `s5-docs` | Update `HighAvailability.md` (usage + DI), `HighAvailabilitySecurity.md` (status), `migrationguide.md` if new API needs it. | `s5-distributed-mgr` |

## Risks & mitigations

- **Touches core session auth (sensitive).** Keep base changes minimal, additive, backward-compatible; default returns null/unchanged; gate the feature behind opt-in DI; extensive positive + negative tests; run the full `Opc.Ua.Server.Tests` + client integration suites (net10 + net48), not just `Category=Distributed`.
- **Session reconstruction details** — `SessionId` preservation, diagnostics-node registration, and certificate handle ownership/disposal (per repo certificate-ownership conventions) need care.
- **Per-activate shared-store round-trip cost** — only the restore (failover) path consults the store; the normal local-session activate path is unchanged.
- **Nonce single-use vs. legitimate failover** — mirror stores the **last-issued** `serverNonce` (the one the client signs next); it is consumed exactly once (active in steady state, standby on failover), so legitimate reconnect is not falsely rejected.

## References

- Plan 30 findings + standards baseline; Plan 28/29 building blocks.
- Code: `SessionManager.cs:193,346,1022,1172`; `Session.cs:121,579,621`; `Nonce.cs:250`; `StandardServer.cs:3959`; `Distributed/{SharedSessionEntry,SharedKeyValueSessionStore,ISingleUseNonceRegistry,SharedSingleUseNonceRegistry,IRecordProtector}.cs`.
