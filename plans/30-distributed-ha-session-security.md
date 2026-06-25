# Plan 30 — Distributed HA Session Sharing & E2E: Security Assessment + Revised Plan

## Status

**Design / security review.** This plan covers the two remaining non-deferred follow-up items from [Plan 29](29-distributed-ha-followup.md) — **`f-session-sharing` (F-A4)** and **`f-e2e-test` (F-C)** — after a thorough security assessment against the OPC UA security model (Part 2 / Part 4), the **IEC 62443** baseline (62443-4-1 secure development lifecycle, 62443-4-2 foundational requirements FR1–FR7), and **Microsoft SDL**. The assessment was run by the security-review agent over the implemented building blocks and the planned `DistributedSessionManager`; a research pass compiled the standards baseline.

**Headline:** the originally-sketched "fast reconnect using just the AuthenticationToken" is **unsafe as written** and must be redesigned. Several findings also apply to the **already-implemented** shared store (the address-space replication has no integrity or confidentiality protection). Session sharing is **gated** on the store-hardening work below.

### Implementation progress

- **S1 `sec-threat-model` — DONE.** STRIDE threat model / DFD / trust boundaries documented in `Docs/HighAvailabilitySecurity.md`.
- **S2 `sec-store-crypto` — DONE.** `IRecordProtector` + `AesCbcHmacRecordProtector` (AES-256-CBC Encrypt-then-MAC, verify-before-decrypt, key-id, fail-closed) wired into `InMemoryNodeStateStore` (node + value records) and `SharedKeyValueSessionStore` (session entries), threaded through `DistributedAddressSpaceOptions.RecordProtectorFactory` + DI. Default `NullRecordProtector` keeps the in-memory single-process path zero-overhead. 16 unit tests (round-trip, tamper, wrong-key, wrong-key-id, malformed, fail-closed at the store layer). Closes Findings 3, 4.
- **S3 `sec-key-mgmt` — DONE (code) + deployment guidance.** Implemented: `KeyRingRecordProtector` (staged, zero-downtime key rotation — active write key + retired read keys, 5 tests); `ISingleUseNonceRegistry` / `SharedSingleUseNonceRegistry` (cross-replica single-use server nonce via store CAS, nonce stored only as its SHA-256 digest — Findings 1 & 6 hygiene, 5 tests); subkey derivation + master/subkey zeroization in the protector (Finding 7). Deployment guidance (KEK/KMS provisioning, token redaction) documented in `Docs/HighAvailabilitySecurity.md` (operator responsibilities — not enforceable in-process). Addresses Findings 5, 6, 7.
- **S4 `sec-transport-authz` — deployment guidance.** mTLS-to-store fail-closed, least-privilege per-replica credentials, TTL/eviction + bounded channels documented as operator responsibilities in `Docs/HighAvailabilitySecurity.md` (Findings 8, 10). These are properties of the chosen `ISharedKeyValueStore` backend (e.g. the deferred Redis adapter), not of the in-memory default.
- **S5 `f-session-sharing-secure` — partially landed; fast-reconnect deferred.** Analysis: the **safe default (re-auth on failover) requires no core-server change** — a client simply runs a fresh `CreateSession`+`ActivateSession` on the standby (standard behaviour). The building blocks for the **opt-in** mirrored fast-reconnect are in place and tested — `SharedKeyValueSessionStore` (encrypted) + `SharedSingleUseNonceRegistry` (replay defence). The remaining piece — a `DistributedSessionManager` (via a `StandardServer.CreateSessionManager` override or a new `ISessionManagerFactory` seam) that mirrors the token + `serverNonce` (which are internal to `SessionManager`, not on `ISession`) and runs a full `ActivateSession` on restore — is **deferred** as a focused, security-sensitive follow-up because it requires privileged access to core session internals. The token-only "fast reconnect using just the AuthenticationToken" from the original sketch is **not** implemented (it was the CRITICAL hijack, Finding 2).
- **S6 `f-e2e-test-secure` — store-layer security tests landed; network e2e deferred.** Implemented as unit tests: tamper rejection, wrong-key/wrong-key-id rejection, cleartext-at-rest negative checks (secret material never appears in the raw store), single-use nonce replay rejection (same-replica and cross-replica). A full two-server in-process network e2e with a real `ManagedSession` client is **deferred** with the `DistributedSessionManager` it would exercise.

**Net result this pass:** the HIGH/CRITICAL findings against the **already-merged** shared store (Findings 1, 3, 4, 6, 7) are closed in code with 85 passing Distributed unit tests (net10 + net48 clean). The session-sharing fast-reconnect (Finding 2's safe redesign) and its network e2e are scoped, de-risked, and deferred as a dedicated follow-up; the safe re-auth-on-failover default works today.



## Standards baseline (citations)

OPC UA (10000-4 / 10000-2 / 10000-5):

- **REQ-UA-1** — the `AuthenticationToken` is associated with a specific SecureChannel; the server *may* accept requests only on the same channel (Part 4 §5.7.2.1).
- **REQ-UA-3 / REQ-UA-5** — the `serverNonce` is single-use, 32–128 bytes, used by the client to prove certificate possession; a *fresh* nonce is returned after every ActivateSession and "once used, a serverNonce cannot be used again" (Part 4 §5.7.2.2, §5.7.3.1).
- **REQ-UA-6 / REQ-UA-7** — ActivateSession requires the client to **sign (ServerCert ‖ ServerNonce)** (or the channel-bound form, §6.1.8 Tables 101/102) with its client-certificate key; on a **new SecureChannel (failover)** the server **shall verify** the client certificate is the same, the `UserIdentityToken` carries the same `ClientUserId`, and the SecurityPolicy/Mode match the original (Part 4 §5.7.3.1).
- **REQ-UA-9** — the `SessionAuthenticationToken` is explicitly **secret**, a random ≥32-byte ByteString, "kept secret" and "always exchanged over a SecureChannel with encryption enabled" (Part 4 §7.35).
- **REQ-UA-10** — session hijacking is countered by binding a SecureChannel security context to each Session; hijacking "would first require compromising the security context" (Part 2 §4.3.9/§5.1.9).
- **REQ-UA-12 / REQ-UA-13** — HotAndMirrored failover **minimally mirrors Sessions, Subscriptions, registered Nodes, ContinuationPoints, sequence numbers, and sent Notifications**; on failover the client "simply creates a new SecureChannel on an alternate Server and then calls ActivateSession" (Part 4 §6.6.2.4.4 Table 106, §6.6.2.4.5.5). This is the spec's own definition of the minimum shared state for reconnect-without-CreateSession.
- **REQ-UA-14** — transparent redundancy requires **identical certificates and security settings** across replicas (shared ApplicationInstanceCertificate + key) (Part 4 §6.6.2.3.2).

IEC 62443 (4-2 FRs / 4-1 SDL):

- **FR1 / CR1.x** Identification & Authentication; **FR2 / CR2.x** Use Control; **FR3 / CR3.1, CR3.4** System & Data Integrity (incl. malformed-input and integrity verification); **FR4 / CR4.1, CR4.3** Data Confidentiality + use of cryptography; **FR5 / CR5.x** Restricted Data Flow (zone/conduit trust boundaries, 62443-3-3); **FR7 / CR7.x** Resource Availability.
- **62443-4-1** secure development lifecycle: threat modeling of new trust boundaries, security verification/testing before release.

Microsoft SDL:

- **REQ-SDL-1** authenticated encryption (AES-GCM / Encrypt-then-MAC) for secrets at rest; **REQ-SDL-2** AEAD nonce uniqueness (NIST SP 800-38D); **REQ-SDL-3** CSPRNG; **REQ-SDL-8** envelope encryption (DEK/KEK); **REQ-SDL-9/10** per-consumer keys + rotation; **REQ-SDL-11** log access to secret stores; **REQ-SDL-12** STRIDE threat model every new trust boundary; **REQ-SDL-13** fuzz/static/secret-scan verification.

## Security findings (from the security-review agent)

| # | Severity | Finding | Standard | Affects |
|---|----------|---------|----------|---------|
| 1 | **CRITICAL** | Persisting/sharing the **server nonce** without strict single-use invalidation enables **ActivateSession replay / signature reuse** (a captured Sign-mode ActivateSession replays on the standby). | FR1/FR3; UA §5.7.3.1 | `SharedSessionEntry.SecretMaterial`, planned restore |
| 2 | **CRITICAL** | **Token-only reconnect** bypasses the SecureChannel + client-certificate signature binding → **session hijack / auth bypass** (anyone with the token resumes the session on a standby). | FR1/FR2; UA §5.7.3.1, Part 2 §5.1.9 | planned `DistributedSessionManager`, test asserting "reconnect using just the token" |
| 3 | **HIGH** (CRITICAL with Redis) | Session secrets stored **at rest unencrypted** — `SecretMaterial` is written verbatim; "caller-encrypted" is an **unenforced comment**. | FR4/CR4.1; SDL-1 | `SharedKeyValueSessionStore.Encode/Decode`, `InMemorySharedKeyValueStore` |
| 4 | **HIGH** | **No integrity (MAC) on any shared record** — a compromised store / rogue replica can inject forged **sessions, node topology, and variable values** that all replicas push into the live, client-served address space; untrusted-data deserialization on hydrate (`LoadAsBinary`). | FR3/CR3.1,CR3.4; SDL untrusted deserialization | `SharedKeyValueSessionStore`, `InMemoryNodeStateStore`, `NodeStateSerializer`, `AddressSpaceSynchronizer` (**implemented code**) |
| 5 | **HIGH** | **Key management**: a single static shared deployment secret → fleet-wide blast radius, no rotation, no crypto-agility envelope (no alg/key-id/IV). | FR4/CR4.3,CR1.5; SDL-8/9/10 | planned encryption |
| 6 | **MEDIUM** | **AuthenticationToken used verbatim as the store key** → credential/handle exposure in Redis `SCAN`/`MONITOR`/RDB/AOF/slowlog and diagnostics. | FR4/FR5 | `SharedKeyValueSessionStore` keys |
| 7 | **MEDIUM** | **No zeroization** of decrypted nonce/identity `ByteString`/`byte[]` (repo standard is `CryptoUtils.ZeroMemory`). | FR4 | session restore path |
| 8 | **HIGH** | **No replica↔store / replica↔replica authentication**; transport security (Redis TLS+auth) is documentation-only; rogue-replica trust boundary undefined; not secure-by-default. | FR5; 62443-3-3 zones/conduits; SDL secure-by-default | `ISharedKeyValueStore` contract, deployment |
| 9 | **MEDIUM** | **No audit trail** for cross-replica session restore/materialization (a security-relevant auth event). | FR6/CR2.8,CR2.11; UA AuditActivateSession | planned restore path |
| 10 | **LOW** | Unbounded shared-state growth + watcher channels + no TTL/eviction of `session/` entries (secret-bearing keyset grows). | FR7 | `InMemorySharedKeyValueStore`, session entries |

**Gating recommendation:** Findings 1, 2, 4 interlock and together permit **unauthenticated session takeover and live-data forgery** on a standby. They must be resolved before `f-session-sharing` ships.

## Reconciliation (review ↔ spec)

The review's "never persist the server nonce" and the spec's "the standby needs the last serverNonce to verify the ActivateSession signature" (REQ-UA-3/6, REQ-UA-12) are reconciled by:

- The session state (incl. last `serverNonce`, `ClientNonce`, client certificate, `ClientUserId`, SecurityPolicy/Mode, sequence numbers) **is** replicated — this is exactly the spec's minimum mirrored state (REQ-UA-12).
- BUT it is **encrypted (AEAD) + integrity-protected (MAC)** at rest, the nonce is **strictly single-use** (atomically invalidated/rotated via CAS so no two replicas accept the same nonce — closes replay/split-brain), and reconnect runs a **full ActivateSession signature verification** against a freshly-issued nonce with the restored client certificate (REQ-UA-6/7) — the token is a **lookup key only**, never an authenticator.
- The documented "re-auth on failover" mode (full CreateSession+ActivateSession) is the **safe default**; mirrored-session fast reconnect is an explicit, audited, risk-accepted opt-in.

## Revised design

### Store hardening (applies to the already-implemented shared store)

1. **Authenticated encryption + integrity on every record.** Wrap all records the store persists — session entries, node payloads (`n/…`), and values (`v/…`) — in an AEAD envelope `{ alg, key-id, IV, ciphertext, tag }` (AES-GCM or AES-CBC Encrypt-then-MAC; reuse the stack's `EncryptedSecret` / `CryptoUtils`). Verify the MAC and decrypt **before** `LoadAsBinary` / apply; **reject** unverified records (fail-closed). This closes Findings 3 & 4 and treats the store as an untrusted conduit (zero-trust between replicas, FR3/FR5).
2. **Bound + validate inbound topology** before applying to the live graph: allowlist NodeId namespaces, bound payload sizes, validate node class (defense for `NodeStateSerializer.Deserialize` untrusted input).
3. **Key management** (Finding 5 / SDL-8/9/10): envelope DEK/KEK; per-session (HKDF) keys to limit blast radius; key-id for staged rotation with overlapping versions; provision the KEK from a secret store / KMS, never a static constant.
4. **Keyspace hygiene** (Finding 6): key entries by `HMAC-SHA256(token, key)` not the raw token; ensure tokens are excluded from logs/metrics/redaction; document the keyspace as sensitive.
5. **Zeroization** (Finding 7): hold decrypted secrets in pooled buffers and `CryptographicOperations.ZeroMemory` immediately after use.
6. **Transport & authz** (Finding 8): require authenticated, mutually-TLS transport to the backend as **non-optional in production** (fail closed if absent); least-privilege per-replica credentials; define and document the rogue-replica threat (STRIDE, SDL-12).
7. **Availability** (Finding 10): TTL/eviction for `session/` entries, bounded/backpressured watch channels, fleet-wide caps.

### `f-session-sharing` (secure)

- A `DistributedSessionManager` (subclass of `SessionManager`, injected via a new **`ISessionManagerFactory` seam**) persists the spec-minimum mirrored session record (REQ-UA-12) **encrypted** on Create/Activate, and on a reconnect whose token is unknown locally restores the record and runs the **standard ActivateSession** path: issue a fresh `serverNonce`, verify the client signature against the **restored client certificate**, verify same `ClientUserId` + SecurityPolicy/Mode (REQ-UA-6/7), and **invalidate the consumed nonce** in the store (CAS).
- Token is a lookup key only; never admits a session without a valid client signature (closes Finding 2).
- Emit `AuditActivateSession` plus a distinct "session restored from shared store" audit with provenance (source replica, token-hash, reason); alert on restores that fail signature re-validation (Finding 9 / FR6).
- Default to **re-auth on failover**; mirrored fast reconnect is opt-in + audited.

### `f-e2e-test` (with security validation)

Two in-process server instances sharing one store + a real `ManagedSession` client. Functional: write replicates active→standby; failover selects the highest `ServiceLevel`; subscriptions transfer; reconnect succeeds. **Security tests (must pass):**

- A captured/replayed ActivateSession against the standby is **rejected** (nonce single-use).
- A reconnect presenting only the token (no valid client signature) is **rejected** (no hijack).
- A record with a tampered MAC / wrong key is **rejected** (integrity).
- Session secrets never appear **in cleartext** in the shared store (confidentiality).
- A consumed nonce cannot be reused on either replica (CAS invalidation).

## Phased todos

| Phase | Todo id | Summary | Addresses |
|-------|---------|---------|-----------|
| S1 | `sec-threat-model` | STRIDE threat model + DFD of the shared-store trust boundary; document rogue-replica + store-compromise threats; record as ADR. | SDL-12; 62443-4-1; Findings 4,8 |
| S2 | `sec-store-crypto` | AEAD + MAC envelope on every store record (session/node/value) with verify-before-apply (fail-closed); bound/validate inbound topology before `LoadAsBinary`. | Findings 3,4; FR3/FR4 |
| S3 | `sec-key-mgmt` | DEK/KEK envelope, per-session HKDF keys, key-id + rotation, KMS/secret-store provisioning; keyspace HMAC; token redaction; zeroization. | Findings 5,6,7; SDL-8/9/10 |
| S4 | `sec-transport-authz` | Require authenticated mTLS transport to the store (fail-closed in prod); least-privilege replica creds; availability caps/TTL. | Findings 8,10; FR5/FR7 |
| S5 | `f-session-sharing-secure` | `ISessionManagerFactory` seam + `DistributedSessionManager`: encrypted spec-minimum mirroring, full ActivateSession signature validation, single-use nonce CAS, restore audit. Default re-auth-on-failover. | Findings 1,2,9; REQ-UA-3/5/6/7/12/13 |
| S6 | `f-e2e-test-secure` | Two-server e2e + the security tests above (replay/hijack/tamper/cleartext/nonce-reuse rejection). | Findings 1–4; 62443-4-1 verification |

Dependencies: S2→S1; S3→S2; S4→S1; S5→{S2,S3}; S6→{S4,S5}.

## Open decisions

1. **Default failover mode** — ship **re-auth on failover** as default (safe, spec-compliant via fresh CreateSession+ActivateSession) and gate mirrored fast-reconnect behind explicit, audited opt-in? (Recommended.)
2. **KEK provisioning** — k8s Secret + CSI driver vs external KMS; rotation cadence.
3. **Nonce single-use across replicas** — CAS on the store entry vs a dedicated consumed-nonce set with TTL (REQ-UA-4 optional duplicate-nonce check).
4. **Transparent redundancy** — requires a shared ApplicationInstanceCertificate/key (REQ-UA-14); is that in scope or documentation-only?
5. **Scope of store hardening now** — S2 (integrity/encryption) affects the **already-merged** address-space replication; apply it before any production use even independent of session sharing.

## References

- Security findings: security-review agent assessment (this session).
- Standards baseline: research compilation (OPC 10000-4 §5.7/§6.6/§7.35, 10000-2 §4.3.9/§5.1, 10000-5 §6.3; IEC 62443-4-1/-4-2; Microsoft SDL crypto/secrets/threat-modeling pages).
- Code: `Libraries/Opc.Ua.Server/Distributed/{SharedSessionEntry,ISharedSessionStore,SharedKeyValueSessionStore,ISharedKeyValueStore,InMemorySharedKeyValueStore,InMemoryNodeStateStore,NodeStateSerializer,AddressSpaceSynchronizer}.cs`; `Session/SessionManager.cs` (nonce/token at :103,:246-255,:268-314,:407), `Session/Session.cs` (`ValidateBeforeActivate`).
- Plan 28 building blocks; Plan 29 F-A4/F-C.
