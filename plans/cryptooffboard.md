# Crypto offboarding — remaining roadmap

> **Status:** The pluggable crypto provider model and hardware-held private keys are **implemented**.
> This document is what is left.
> **Tracks:** [#4190](https://github.com/OPCFoundation/UA-.NETStandard/issues/4190)
> **Shipped feature documentation:** [`docs/CryptoProvider.md`](../docs/CryptoProvider.md)

The design, the surface inventory and the phase plan that produced this feature have been removed:
they described work that is now in the codebase, and `docs/CryptoProvider.md` documents the result.
What remains below is the work that was deliberately **not** done, with the reason it was not done and
what would have to be true to change that. Each section is tracked by its own issue.

| Section | Issue |
|---|---|
| 1. Registrable security policies | [#4206](https://github.com/OPCFoundation/UA-.NETStandard/issues/4206) |
| 2. ~~Symmetric, key-derivation and RNG provider seams~~ **done** | [#4207](https://github.com/OPCFoundation/UA-.NETStandard/issues/4207) |
| 3. ~~Offboard providers block a thread~~ **done** | [#4208](https://github.com/OPCFoundation/UA-.NETStandard/issues/4208) |
| 4. Platform and protocol gaps | [#4209](https://github.com/OPCFoundation/UA-.NETStandard/issues/4209) (HTTPS), [#4210](https://github.com/OPCFoundation/UA-.NETStandard/issues/4210) (PubSub), [#4211](https://github.com/OPCFoundation/UA-.NETStandard/issues/4211) (Part 7 facet) |
| 5. FIPS posture that is still open | [#4212](https://github.com/OPCFoundation/UA-.NETStandard/issues/4212) |

---

## 1. Registrable security policies

Tracked by [#4206](https://github.com/OPCFoundation/UA-.NETStandard/issues/4206).

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

## 2. Symmetric, key-derivation and RNG provider seams — **done**

Tracked by [#4207](https://github.com/OPCFoundation/UA-.NETStandard/issues/4207).

`ISymmetricCryptoProvider`, `IKeyDerivationProvider` and `IRandomSource` were originally left out because
the only consumer that would have justified them was hardware offload, and a device round trip per
message would destroy throughput. The consumer that does justify them has since been accepted: a
**validated software module that must perform every operation**, not only the asymmetric ones.

They are now implemented, on the terms this section set:

- Declared as optional **facets** discovered by type test, not as members of `ICryptoProvider`, so
  providers written against the shipped interface still compile.
- Behind a **null fast path**. `CryptoProviderFacets` returns `null` when no registry is configured *and*
  when resolution lands on the platform, because the platform facets run exactly the inline code. The
  default configuration therefore has no interface dispatch on the per message path.
- Resolved **once**, in `CalculateSymmetricKeySizes`, and held for the life of the channel.
- Gated by the benchmark this section named. `SymmetricChannelCryptoBenchmarks` now measures both paths:
  `EncryptSignThenDecryptVerify` is the baseline and `EncryptSignThenDecryptVerifyThroughProvider` is the
  same work through the seam.

`CryptoCompliance.GetUnservedOperationPurposes` closes the failure the seam otherwise introduces: a
provider bound to a symmetric purpose without the matching facet would be silently replaced by the
platform. Under `FipsOnly` that now refuses to start.

Documented in [`docs/CryptoProvider.md`](../docs/CryptoProvider.md#substituting-the-symmetric-primitives).

---

## 3. Offboard providers block a thread — **done**

Tracked by [#4208](https://github.com/OPCFoundation/UA-.NETStandard/issues/4208).

`RSA` and `ECDsa` are synchronous contracts, and they are .NET's rather than this stack's, so they
cannot be replaced. The way out is for an implementation of them to *also* declare an asynchronous path,
which the stack finds by type test:

- `IAsyncRsaKey` and `IAsyncEcdsaKey` are the opt-in facets.
- `CryptoUtils.SignAsync`, `RsaUtils.DecryptAsync`, `SecurityPolicies.CreateSignatureDataAsync` and
  `SecurityPolicies.DecryptAsync` take them when present.
- A software key declares neither, so every one of those returns an already-completed task and the
  ordering of everything around the call is unchanged.

The secure channel open and renew path is asynchronous end to end, as are the user identity paths and
session activation. The channel no longer serialises its state on a monitor: `ChannelGate` replaces it
and can be entered from an asynchronous path.

### What still occupies a thread, and why

Certificate, certificate request and revocation list signing, because
`X509SignatureGenerator.SignData` is called by .NET's own `CertificateRequest` and CRL builders. Service
faults and the synchronous reconnect handoff are in the same position: both are reached from call sites
that cannot become asynchronous without a contract break.

### Three defects this surfaced, all fixed

**A synchronous gate handle held across an await.** `ChannelGate.Enter()` records the acquiring thread
so an inline completion callback can re-enter — `ChannelAsyncOperation` invokes its callback both inline
and detached. That record is only sound while the holder is synchronously on that thread; once it
suspends, the thread returns to the pool and unrelated work is recognised as the holder. `EnterAsync`
therefore records no thread at all, and `Enter()` must not be held across an await.

**Detached work started inline.** The channel started its writes with `_ = WriteBuffersAsync(...)`, which
runs the prologue on the caller's stack. That prologue disclaims the inherited context — stripping the
*caller's own* right to re-enter — and when the send completed synchronously the completion then blocked
on a gate that very thread was holding. This is what stopped the handshake. Writes are now queued, so
the disclaimer only ever runs on genuinely detached work.

**`SignAsync` rejected the policy that signs nothing.** It validated the certificate before the
algorithm, so `SecurityPolicies.None` threw where the synchronous `Sign` returns null.

All three are covered by tests in `ChannelGateTests` and by the secured loopback fixture.

---

## 4. Platform and protocol gaps

| Gap | Tracked by | Why |
|---|---|---|
| HTTPS with a device-held key does not work on Windows or macOS | [#4209](https://github.com/OPCFoundation/UA-.NETStandard/issues/4209) | SChannel and the macOS Security framework require keys registered with a platform key storage provider. It works on Linux, where the TLS layer dispatches through the managed key. UA-TCP is unaffected everywhere. |
| PubSub security policies take raw key bytes | [#4210](https://github.com/OPCFoundation/UA-.NETStandard/issues/4210) | `IPubSubSecurityPolicy` has no handle-based variant, so a PubSub key cannot stay in a device. Needs its own design. |
| No Part 7 conformance facet for hardware key custody | [#4211](https://github.com/OPCFoundation/UA-.NETStandard/issues/4211) | Cannot be solved in this repository; it needs raising with the OPC Foundation. |

---

## 5. FIPS posture that is still open

Tracked by [#4212](https://github.com/OPCFoundation/UA-.NETStandard/issues/4212).

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

## 6. Pre-existing defects, found on the way and fixed here

Three defects surfaced while validating this work. All three reproduce on `master` with no part of
this feature present, and all three are fixed in this change.

**`STACKGEN001` broke every compilation the generator was merely loaded into.** The Stack source
generator switched on the compilation's assembly name and reported a hard error for anything outside a
three-name allow list, so BenchmarkDotNet's generated host project failed to build and the benchmark
had to run in process. Keying behaviour on an assembly *name* was the deeper problem: renaming a
project silently changed what was generated. Projects now opt in through `StackSourceGeneratorMode`;
absent, the generator emits nothing and no diagnostic. The benchmark runs on the default toolchain
again.

**Certificates could not be loaded from paths past `MAX_PATH`.** Files were handed to the platform
loader by path, which reaches CryptoAPI on Windows and is not long-path aware, so a certificate
deeper than 260 characters failed with `CryptographicException: The system cannot find the path
specified` even though the enumeration that produced the path had just succeeded. Reading the file
first — which the PEM branch beside it already did — removes the limit. This is what made
`ConfigureApplicationBuildsSharedClientAndServerConfigurationAsync` fail on `master`: a
`ClientAndServer` application provisions ECC certificates whose file names carry the curve
(`... [BrainpoolP256r1] [<40 hex>].pfx`), which pushed the PFX over the limit. .NET Framework cannot
open such a path at all, so that test also got a shorter PKI root.

**The `MonitoredNode2` tests starved the thread pool.** `Remove_EventMonitoredItem_DropsCacheEntries`
timed out on loaded runners and passed on re-run. The blocking wait was real but test-side: the
fixture is `[Parallelizable]`, and 27 tests blocked a thread-pool thread on
`ManualResetEventSlim.Wait` while waiting for a consumer task that needs a thread-pool thread of its
own. They now await instead, and the fixture runs in about a quarter of a second. `MonitoredNode2`'s
synchronous `OnReportEvent` and `OnMonitoredNodeChanged` turned out to have no production callers at
all, so they are marked `[Obsolete]` rather than removed.

---

## 7. Seen once, not explained

A `Core.Encoders` CI job produced no test output for 57 minutes and hit the 60-minute timeout, then
passed on re-run. Tracked by [#4213](https://github.com/OPCFoundation/UA-.NETStandard/issues/4213) with
the evidence, including what was ruled out: it is not a static-initialiser deadlock from this work.
Nothing here is a fix, because without a reproduction there is nothing honest to fix.
