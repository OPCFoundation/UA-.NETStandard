# Crypto offboarding — remaining roadmap

> **Status:** The pluggable crypto provider model and hardware-held private keys are **implemented**.
> This document is what is left.
> **Tracks:** [#4190](https://github.com/OPCFoundation/UA-.NETStandard/issues/4190)
> **Shipped feature documentation:** [`docs/CryptoProvider.md`](../docs/CryptoProvider.md)

The design, the surface inventory and the phase plan that produced this feature have been removed:
they described work that is now in the codebase, and `docs/CryptoProvider.md` documents the result.
What remains below is the work that was deliberately **not** done, with the reason it was not done and
what would have to be true to change that.

---

## 1. Registrable security policies

A provider can add a *key custody* mechanism today, but it cannot contribute a *security policy*. The
policy set is fixed at compile time. This is a wide refactor of the security constants and needs its own
design issue and sign-off; it must not be attached to the provider model.

The blockers, all in `Opc.Ua.Core/Security/Constants`:

1. Algorithm identifiers are closed C# enums, and dispatch is `switch (enum)` rather than polymorphic
   through the resolved provider.
2. The policy lookup tables are built by reflection over `typeof(SecurityPolicies).GetFields()`, which is
   also the last reflection in a trim/AOT assembly.
3. `SecurityPolicyInfo` cannot be constructed from another assembly.
4. `IsPlatformSupportedName`, `GetDefaultUris`, `BuildSupportedSecurityPolicies`,
   `MapSecurityPolicyToCertificateTypes` and `GetCurveFromCertificateTypeId` are hand-written rather than
   table-driven.

**Acceptance test:** light up `ECC_curve25519` and `ECC_curve448` from outside the tree with no
`#if CURVE25519`. Both are implemented in-tree but dead, because the `CURVE25519` symbol is defined in no
project or props file anywhere in the repository. Deleting that dead conditional is part of the work.

**Cheaper alternative worth costing first:** contributing missing profiles in-tree behind the existing
capability probe is far less work than opening the policy set, and may serve the actual need.

---

## 2. Symmetric, key-derivation and RNG provider seams

`ISymmetricCryptoProvider`, `IKeyDerivationProvider` and `IRandomSource` were considered and
**deliberately not added**. The measurement is the reason, not a guess: an isolated 8 KB round trip on
`net10.0`/Basic256Sha256 costs roughly 9.8 µs and 2.2 KB, nearly all of it inside AES and HMAC
(`SymmetricChannelCryptoBenchmarks` reproduces it).

The only consumer that would justify public API on that path is hardware offload, and a device round
trip per message would destroy throughput. Adding the seam with nothing implementing it would commit the
hottest code in the stack to an unused interface.

**What would change this:** a concrete requirement to substitute a *software* implementation of the
symmetric primitives — for example a validated module that must perform every operation, not only the
asymmetric ones. That is a real FIPS scenario, and if it arrives the seam should be added then, behind a
null-check fast path, with the benchmark as the gate.

---

## 3. Offboard providers block a thread

`RSA` and `ECDsa` are synchronous contracts, so a network-backed provider — a cloud KMS, a remote
signing service — blocks a thread for the duration of the call. This was accepted knowingly: the
operations are all on the cold path (channel open, session activation, certificate issuance), where a
local device costs single-digit milliseconds.

For a *remote* service it is noticeable. Fixing it means an async asymmetric path through the channel,
which is a much larger change than the provider model and was excluded from it.

---

## 4. Platform and protocol gaps

| Gap | Why |
|---|---|
| HTTPS with a device-held key does not work on Windows or macOS | SChannel and the macOS Security framework require keys registered with a platform key storage provider. It works on Linux, where the TLS layer dispatches through the managed key. UA-TCP is unaffected everywhere. |
| PubSub security policies take raw key bytes | `IPubSubSecurityPolicy` has no handle-based variant, so a PubSub key cannot stay in a device. Needs its own design. |
| No Part 7 conformance facet for hardware key custody | Cannot be solved in this repository; it needs raising with the OPC Foundation. |

---

## 5. FIPS posture that is still open

The compliance filter ships with `CryptoCompliancePolicy.Permissive` as the default, so nothing changes
on upgrade. Two questions were left for maintainers rather than decided here:

- Should the default become `WarnOnUncertified`? It is louder but harmless. Making `FipsOnly` the
  default would be a behavioural break, because it withholds the ChaCha20 and brainpool endpoints that
  are advertised today.
- Should the FIPS claim be scoped to `net8.0`+ only? `net472`/`net48` cannot make any claim while
  `BouncyCastle.Cryptography` is in the certificate path — it is not validated, and BC-FNA (CMVP #4416)
  is a separate commercial product.

Per-policy classification now lives on `SecurityPolicyInfo.IsFipsApproved`, so adding a policy forces the
answer to be stated next to the algorithms it follows from.

---

## 6. Unrelated, but found on the way

Three pre-existing defects surfaced while validating this work. None belongs in this change, and all
three are reproducible on `master` with no part of this feature present.

`STACKGEN001 'Stack generation not supported for <assembly>'` breaks BenchmarkDotNet host generation for
any project with `Opc.Ua.Gds.Common` in its closure — the Stack source generator errors for assemblies
outside its three-name allow-list. It is pre-existing and repo-wide, and was routed around here by
running the benchmark in process. It deserves its own issue.

`MonitoredNode.OnReportEvent` blocks on `report.AsTask().GetAwaiter().GetResult()`. Under thread-pool
starvation that stall is what makes `Remove_EventMonitoredItem_DropsCacheEntries` time out on a loaded
runner; the timeout is the symptom and the blocking wait is the cause. Confirmed a flake by re-running
with no code change.

`ServerFluentApiHostingTests.ConfigureApplicationBuildsSharedClientAndServerConfigurationAsync` fails
on `master` — verified by running the single test in a clean worktree at `master`, where the hosted
server never reaches `Start` within its 30-second budget. The `CryptographicException: The system
cannot find the path specified.` entries in its log are benign PEM-probe fallbacks that precede a
successful PFX import, not the cause.
