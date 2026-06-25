# High Availability — Security & Threat Model

This document is the security design / threat model (ADR) for the distributed high-availability feature ([HighAvailability.md](HighAvailability.md)). It records the trust boundaries introduced by sharing address-space and session state across replicas, a STRIDE analysis, and the mitigations. It exists because an HA replicaset adds a **new trust boundary** — the shared store and the peer replicas — that the single-instance server did not have (Microsoft SDL "threat model every new trust boundary"; IEC 62443-4-1 secure development lifecycle).

## Assets

- **Process data integrity** — variable values and address-space topology served to clients (safety-relevant in an industrial server).
- **Session credentials** — the `AuthenticationToken` (a secret per OPC UA Part 4 §7.35), the `serverNonce` / `ClientNonce`, the client `ApplicationInstanceCertificate`, and the user identity token.
- **Availability** — the server must keep serving and fail over.

## Data-flow diagram (trust boundaries)

```
            ┌──────────── replica (server) trust boundary ────────────┐
 Client ──TLS/UA-SC──▶  OPC UA endpoint ─▶ NodeManager / SessionManager │
                       │            │                                   │
                       ▼            ▼                                   │
                AddressSpaceSynchronizer   DistributedSessionManager    │
                       │            │                                   │
            ───────────┼────────────┼─── shared-store conduit ─────────┐│  ← NEW trust boundary
                       ▼            ▼                                   ││
                 ISharedKeyValueStore  (in-memory / Redis)             ││
                       ▲            ▲                                   ││
            ───────────┼────────────┼───────────────────────────────── ┘│
                       │            │                                    │
                 peer replica  peer replica   ← NEW trust boundary (rogue replica)
            └──────────────────────────────────────────────────────────┘
```

The store conduit and peer replicas are **outside** the single replica's trust boundary. The store must be treated as an **untrusted conduit** (zero-trust between replicas).

## STRIDE analysis

| Element / flow | Threat (STRIDE) | Risk | Mitigation (todo) |
|----------------|-----------------|------|-------------------|
| Client → standby reconnect | **S**poofing / Elevation: token-only reconnect impersonates a session | CRITICAL | Full ActivateSession signature validation; token = lookup key only (`f-session-sharing-secure`) |
| serverNonce in shared record | **T**ampering / replay (reuse a captured ActivateSession) | CRITICAL | Single-use nonce, CAS-invalidated on consume; fresh nonce per activate (`f-session-sharing-secure`) |
| Node/value/session records in store | **T**ampering: rogue replica / compromised store forges values, topology, sessions applied to live graph | HIGH | AEAD + MAC on every record, verify-before-apply, fail-closed (`sec-store-crypto`) |
| Secrets at rest in store | **I**nformation disclosure: nonce / identity / token readable from Redis dump/MONITOR | HIGH | AEAD encryption at rest; envelope DEK/KEK (`sec-store-crypto`, `sec-key-mgmt`) |
| Store keys = raw token | **I**nformation disclosure via keyspace (`SCAN`/slowlog) | MEDIUM | HMAC the sensitive key part; redact tokens from logs (`sec-key-mgmt`) |
| Decrypted secrets in memory | **I**nformation disclosure (heap/dump) | MEDIUM | `CryptographicOperations.ZeroMemory` after use (`sec-key-mgmt`) |
| Replica ↔ store link | **S**poofing / **T**ampering / **I**nfo disclosure on the wire | HIGH | Mutual-TLS + auth to store, fail-closed in prod (`sec-transport-authz`) |
| Cross-replica session restore | **R**epudiation: no audit of a session appearing on a standby | MEDIUM | Emit AuditActivateSession + "restored from store" provenance (`f-session-sharing-secure`) |
| Shared store growth | **D**enial of service: unbounded sessions/nodes/watchers | LOW | TTL/eviction, bounded channels, caps (`sec-transport-authz`) |
| Encryption key | **E**levation: single static fleet key → fleet-wide compromise | HIGH | Per-session HKDF keys, rotation, KMS provisioning (`sec-key-mgmt`) |

## Security principles adopted

1. **The shared store is untrusted.** Every record is authenticated (MAC) and confidential (AEAD); unverified records are rejected fail-closed. A compromised store or rogue replica cannot forge state served to clients.
2. **The OPC UA session security model is preserved on failover.** Reconnect always performs a full ActivateSession: the standby issues a fresh `serverNonce` and verifies the client-certificate signature, the same `ClientUserId`, and the same SecurityPolicy/Mode (Part 4 §5.7.3). The `AuthenticationToken` is never an authenticator, only a lookup key.
3. **Secrets are encrypted at rest and zeroized in memory**, keyed by rotation-capable, least-privilege keys provisioned from a secret store — never a static constant.
4. **Secure by default / fail closed.** Production transport to the store must be authenticated TLS; absence fails closed.
5. **Auditable.** Cross-replica session restore emits audit events with provenance.

## Implemented mitigations (status & usage)

The store-hardening mitigations are implemented and unit-tested; the session-mirroring fast-reconnect is gated behind them and is opt-in (the safe default is re-authentication on failover, which requires no shared session state).

### Record protection — `IRecordProtector` (S2, done)

Every record the shared store persists — node payloads (`n/…`), encoded values (`v/…`), and session entries (`session/…`) — is wrapped in an authenticated-encryption envelope before it leaves the replica, and is verified-then-decrypted on the way back in. `AesCbcHmacRecordProtector` uses AES-256-CBC with HMAC-SHA256 in Encrypt-then-MAC order; the MAC is checked **before** any decryption (no padding-oracle), so a tampered or forged record is rejected fail-closed and never reaches `LoadAsBinary` / the live graph. The default `NullRecordProtector` is a pass-through for the single-process in-memory case, so that path keeps its zero-overhead behaviour.

Wire it through DI:

```csharp
services.AddOpcUaServer(...)
    .UseDistributedAddressSpace(o =>
    {
        // 32-byte master key provisioned from a secret store / KMS — never a constant.
        o.RecordProtectorFactory = sp => new AesCbcHmacRecordProtector(masterKey, keyId: 2);
    });
```

This closes Findings 3 and 4 (no integrity/encryption on shared records) and treats the store as an untrusted conduit.

### Key rotation — `KeyRingRecordProtector` (S3, done)

`KeyRingRecordProtector` writes new records under a single *active* key while still verifying reads against any number of *retired* keys. An operator can roll out a new key fleet-wide, let records re-write under it over time, and only then drop the old key — no flag-day re-encryption. Each key carries a `keyId`, so a record is only ever decrypted by the key version that produced it.

```csharp
o.RecordProtectorFactory = sp => new KeyRingRecordProtector(
    active:  new AesCbcHmacRecordProtector(newKey, keyId: 3),
    retired: new AesCbcHmacRecordProtector(oldKey, keyId: 2));
```

### Single-use server nonce — `ISingleUseNonceRegistry` (S3, done)

`SharedSingleUseNonceRegistry` records each consumed `serverNonce` as a compare-and-swap marker in the shared store, so a nonce can be consumed **exactly once across the whole replica set**. This is the cross-replica enforcement of OPC UA Part 4 §5.7.3.1's single-use requirement and the replay defence (Finding 1) that a mirrored fast-reconnect needs: a Sign-mode `ActivateSession` captured against one replica is rejected when replayed against a standby. The nonce is never stored — the key is its SHA-256 digest, keeping the secret-bearing keyspace one-way (Finding 6 hygiene).

### Secret zeroization (S3, done for key material)

`AesCbcHmacRecordProtector` derives distinct AES and MAC subkeys from the master key, zeroizes the master immediately after derivation, and zeroizes both subkeys on `Dispose` (`CryptographicOperations.ZeroMemory` on net8+, `Array.Clear` on the down-level frameworks).

### Session sharing — `DistributedSessionManager` (S5, done; fast reconnect opt-in)

`DistributedSessionManager` (a `SessionManager` subclass, wired via `UseDistributedSessions(...)` and the additive `ISessionManagerFactory` seam on `StandardServer`) mirrors the encrypted session record — including the last `serverNonce` — to the shared session store on `CreateSession` / `ActivateSession`, and removes it on close. On a failover reconnect to a standby, the base `SessionManager` calls the additive `RestoreSessionAsync` hook, which:

1. enforces the same SecurityPolicy/Mode as the original session (REQ-UA-7);
2. **consumes the mirrored `serverNonce` exactly once across the replica set** via `ISingleUseNonceRegistry` — a replayed or already-consumed nonce is rejected (Finding 1);
3. reconstructs the session and lets the **standard** activation path run the full client-certificate signature validation against that nonce (REQ-UA-6/7).

The `AuthenticationToken` is therefore only a lookup key — it never admits a session without a valid client signature, closing the token-only hijack (Finding 2). The safe default is `EnableFastReconnect = false` (re-authentication on failover, no shared session state). Restores are logged with a one-way token digest for provenance (Finding 9 / Finding 6 hygiene).

A residual, intentional trade-off: an attacker who can open a SecureChannel to a standby and replays a token can cause that session's mirrored nonce to be consumed, degrading a legitimate client's fast reconnect to a full re-authentication (the secure default) — it never grants access. The full two-server reconnect end-to-end test is tracked as follow-up; the security-decision logic (policy match + single-use nonce) is unit-tested.

## Deployment guidance (operator responsibilities)

These mitigations are **not** enforced by the in-process default and must be supplied by the deployment. They are mandatory before any production / networked-store use.

- **Key provisioning (S3 / Finding 5).** Provision the `AesCbcHmacRecordProtector` master key (the KEK) from a secret store or KMS — a Kubernetes Secret mounted via the CSI Secrets Store driver, or an external KMS — never a compiled-in constant. Give each replica the **same** key (required so any replica can read shared records) but the **least** privilege needed. Rotate with the key-ring above.
- **Authenticated, encrypted store transport (S4 / Finding 8).** When the `ISharedKeyValueStore` is a network backend (e.g. Redis), require mutual-TLS with authentication on the conduit and **fail closed** if it is unavailable. Use a least-privilege, per-replica credential. The store conduit is a 62443 zone boundary; do not run it in the clear.
- **Availability caps (S4 / Finding 10).** Apply a TTL / eviction policy to `session/` and consumed-`nonce/` entries, bound the watch channels, and cap the shared keyspace so a faulty or hostile replica cannot exhaust it.
- **Token redaction (Finding 6).** Ensure `AuthenticationToken` values are excluded from logs, metrics, and traces; treat the shared keyspace as sensitive.
- **Transparent redundancy (REQ-UA-14).** Spec-transparent redundancy requires an identical `ApplicationInstanceCertificate` and private key across replicas. Provision the shared key material through the certificate/secret store; this is a deployment decision (see [HighAvailability.md](HighAvailability.md)).



See [plans/30-distributed-ha-session-security.md](../plans/30-distributed-ha-session-security.md) for the full finding list, severities, standard citations (OPC UA Part 2/4/5, IEC 62443 FR1–FR7, Microsoft SDL), the phased remediation todos, and open decisions (default failover mode, KEK provisioning, transparent-redundancy shared certificate).
