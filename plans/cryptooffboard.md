# Crypto offboarding — remaining roadmap

> **Status:** The pluggable crypto provider model and hardware-held private keys are **implemented**.
> This document is what is left.
> **Tracks:** [#4190](https://github.com/OPCFoundation/UA-.NETStandard/issues/4190)
> **Shipped feature documentation:** [`docs/CryptoProvider.md`](../docs/CryptoProvider.md)

Work that has been completed has been removed from this document rather than annotated as done:
`docs/CryptoProvider.md` documents the result, and the issue history records how it got there. What
remains below is only what is still outstanding, with the reason it has not been done and what would
have to be true to change that. Each section is tracked by its own issue.

| Section | Issue |
|---|---|
| 1. Platform and protocol gaps | [#4209](https://github.com/OPCFoundation/UA-.NETStandard/issues/4209) (HTTPS), [#4211](https://github.com/OPCFoundation/UA-.NETStandard/issues/4211) (Part 7 facet) |
| 2. FIPS posture that is still open | [#4212](https://github.com/OPCFoundation/UA-.NETStandard/issues/4212) |
| 3. Seen once, not explained | [#4213](https://github.com/OPCFoundation/UA-.NETStandard/issues/4213) |

---

## 1. Platform and protocol gaps

| Gap | Tracked by | Why |
|---|---|---|
| HTTPS with a device-held key does not work on Windows or macOS | [#4209](https://github.com/OPCFoundation/UA-.NETStandard/issues/4209) | SChannel and the macOS Security framework require keys registered with a platform key storage provider. It works on Linux, where the TLS layer dispatches through the managed key. UA-TCP is unaffected everywhere. |
| No Part 7 conformance facet for hardware key custody | [#4211](https://github.com/OPCFoundation/UA-.NETStandard/issues/4211) | Cannot be solved in this repository; it needs raising with the OPC Foundation. |

### PubSub key custody — the part that cannot be closed here

The PubSub work ([#4210](https://github.com/OPCFoundation/UA-.NETStandard/issues/4210)) is done, but it
produced one finding that stays true and is worth keeping in front of anyone who revisits it:

> **With a standard Security Key Service the key necessarily exists in process memory.**
> `GetSecurityKeys` (Part 14 §8.3.2) returns raw key bytes over the wire. The "key never leaves the
> device" property that #4192 achieved for client and server therefore **cannot** be achieved for
> PubSub through the SKS pull profile. That is a property of the specification, not of this stack.

A wrapped-key envelope would change what is on the wire and break interoperability with third-party key
services and publishers, so it was deliberately rejected rather than built. Closing this properly needs
a specification change, which puts it in the same category as the Part 7 facet above.

---

## 2. FIPS posture that is still open

Tracked by [#4212](https://github.com/OPCFoundation/UA-.NETStandard/issues/4212).

The compliance filter ships with `CryptoCompliancePolicy.Permissive` as the default, so nothing changes
on upgrade. Two questions are left for maintainers rather than decided in code:

- Should the default become `WarnOnUncertified`? It is louder but harmless. Making `FipsOnly` the
  default would be a behavioural break, because it withholds the ChaCha20 and brainpool endpoints that
  are advertised today.
- Should the FIPS claim be scoped to `net8.0`+ only? `net472`/`net48` cannot make any claim while
  `BouncyCastle.Cryptography` is in the certificate path — it is not validated, and BC-FNA (CMVP #4416)
  is a separate commercial product.

Per-policy classification lives on `SecurityPolicyInfo.IsFipsApproved`, so adding a policy forces the
answer to be stated next to the algorithms it follows from.

---

## 3. Seen once, not explained

A `Core.Encoders` CI job produced no test output for 57 minutes and hit the 60-minute timeout, then
passed on re-run. Tracked by [#4213](https://github.com/OPCFoundation/UA-.NETStandard/issues/4213) with
the evidence, including what was ruled out: it is not a static-initialiser deadlock from this work.
Nothing has been changed for it, because without a reproduction there is nothing honest to fix.
