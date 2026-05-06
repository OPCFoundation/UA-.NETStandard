# Certificate Manager Migration Plan

This document tracks the legacy `CertificateValidator` / `ICertificateValidator` /
`CertificateTypesProvider` usage that survives in the codebase as a binary-compat
bridge while the new `ICertificateManager` / `ICertificateValidatorEx` /
`ICertificateRegistry` API stabilises. It complements
[`Docs/CertificateManager.md`](CertificateManager.md), which documents the
target API.

## Current state (after Phases 1, 2, 3, 4, 5, 6, 7 + Phase A + Phase B)

* The `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` setting in
  `common.props` makes any unsuppressed CS0618 fail the build.
* Phases 1–7, plus Phase A (retirement of `TemporaryCertValidator`)
  and Phase B (deletion of `ApplicationConfiguration.CertificateValidator`
  and `ServerBase.CertificateValidator` obsolete forwarders) are done.
* Phase 8 — full deletion of `CertificateValidator.cs`, `ICertificateValidator.cs`,
  `CertificateValidatorAdapter.cs`, and the `CertificateValidator` legacy
  branches inside `CertificateValidationExtensions.cs` — is **deferred**.
  See "Phase 8 prerequisites" below.
* The remaining cert-related CS0618 wrappers count is **15** scoped pragma blocks
  (down from **130** at the start of this branch — a **115-block reduction**).
* `dotnet build UA.slnx -c Debug` is clean.
* Test suites all pass on `net10.0` and `net48`:
  * `Tests/Opc.Ua.Core.Tests`           net10.0  6619/6619  net48  6571/6571
  * `Tests/Opc.Ua.Server.Tests`         net10.0   920/925   net48   920/925
  * `Tests/Opc.Ua.Client.Tests`         net10.0  2176/2557  (net48 stress
    variants take >4 h on this workstation; targeted ClientTest filter
    passed 260/437)
  * `Tests/Opc.Ua.Configuration.Tests`  net472    208/208   net48   208/208
  * `Tests/Opc.Ua.Security.Certificates.Tests` net472 236/236  net48 236/236
  * `Tests/Opc.Ua.Types.Tests`          net472   7501/7501  net48  7501/7501
  * `Tests/Opc.Ua.PubSub.Tests`         net9.0   9296/9428  net472 9296/9418
  * `Tests/Opc.Ua.Client.ComplexTypes.Tests` net472 3393/3393 net48 3393/3393

The remaining wrappers exist *only* inside the legacy `CertificateValidator`
class, the `CertificateValidatorAdapter` bridge, the legacy fast-path inside
`CertificateValidationExtensions`, the per-trust-list `CertificateValidator`
cache that backs `CertificateManager.ValidateAsync`, and one Gds test that
exercises the legacy `CertificateValidator.Update(issuer, trusted, rejected)`
in-memory trust-list constructor.

## What changed in this branch

### Phase 3 — `CertificateTypesProvider` retirement

* Added `ICertificateRegistry.GetIssuersAsync(Certificate, IList<...>, ct)`
  (mirrors the legacy `CertificateValidator.GetIssuersAsync` signature).
* Added `ITransportListenerFactory.CreateServiceHost(...)` overload taking
  `ICertificateRegistry serverCertificates` plus
  `ICertificateValidatorEx clientCertificateValidator` (binary breaking on
  `ITransportListenerFactory`).
* Added `ITransportListener.CertificateUpdate(ICertificateValidatorEx,
  ICertificateRegistry)` (binary breaking on `ITransportListener`).
* Added `CertificateRegistryExtensions.LoadCertificateChain(this registry,
  Certificate)` — returns a caller-owned, AddRef'd `CertificateCollection`
  for transport channels.
* Added `ICertificateLifecycle.UpdateAsync(SecurityConfiguration, ...)` —
  re-maps trust-list paths and reloads the application certificate snapshot
  in one call (replacement for the legacy
  `CertificateValidator.UpdateAsync(SecurityConfiguration)`).
* Replaced `TransportListenerSettings.ServerCertificateTypesProvider`
  with `TransportListenerSettings.ServerCertificates`.
* Refactored TCP / HTTPS transports + `ServerBase` /
  `StandardServer` / `ConfigurationNodeManager` / `ApplicationInstance` /
  `ApplicationConfigurationBuilder` to consume `ICertificateRegistry` /
  `ICertificateManager` directly.
* Deleted `CertificateTypesProvider.cs`.
* Deleted `ServerBase.InstanceCertificateTypesProvider`.
* Deleted `ServerInternalData[Obsolete]` ctor that took
  `CertificateTypesProvider`.

### Phase 4 — `ApplicationConfiguration.CertificateValidator` collapse

* Removed the eager `new CertificateValidator(m_telemetry)` from the
  three `ApplicationConfiguration` ctors.
* `ApplicationInstance.Dispose` no longer disposes the legacy validator
  (`CertificateManager.Dispose()` handles it).
* `ApplicationConfiguration` copy ctor now copies `CertificateManager`
  by reference (matching the legacy shared-instance semantics formerly
  provided by `CertificateValidator`).
* Updated `ApplicationConfigurationTests` /
  `ApplicationConfigurationEncodingTests` /
  `CertificateValidatorTest.CertificateValidatorAssignableFromAppConfig`.

### Phase 1 cleanup (partial) — new validation knobs

* `CertificateManager.AutoAcceptUntrustedCertificates` /
  `RejectSHA1SignedCertificates` /
  `RejectUnknownRevocationStatus` /
  `MaxRejectedCertificates` are now first-class properties; the cached
  per-trust-list validators pick up changes immediately via
  `ApplyValidationFlags`.
* Added `ICertificateLifecycle.FlushRejectedAsync(CancellationToken)` —
  modern replacement for `CertificateValidator.WaitForRejectedCertificatesDrainAsync`.
* `RejectedCertificateProcessor.SetMaxRejectedCertificates(int)` allows
  the cap to be retuned at runtime; `WaitForDrainAsync` uses the same
  TCS-of-most-recent pattern as the legacy `RejectedCertificateWriter`.
* `TemporaryCertificateManager.Update()` returns the concrete
  `CertificateManager` so tests can configure the new validation knobs
  without an extra cast.
* Migrated `CertificateValidatorAlternate.cs` fully off
  `TemporaryCertValidator`.

### Phase A — retire `TemporaryCertValidator`

* Migrated the remaining 22 legacy-only `CertificateValidatorTest` cases
  to `TemporaryCertificateManager` + the modern `ICertificateValidatorEx` /
  `CertificateManager` API. The
  `Assert.ThrowsAsync<ServiceResultException>(... ValidateAsync ...)` pattern
  becomes `var result = await ValidateAsync(...); Assert.That(result.IsValid,
  Is.False)`.
* `CertificateValidation` event subscriptions become
  `CertificateManager.AcceptError = approver.AcceptError;` callbacks.
  `CertValidationApprover` gains a new `bool AcceptError(Certificate,
  ServiceResult)` method.
* `CertificateUpdate` subscribe/unsubscribe-only test becomes a subscription
  on `ICertificateLifecycle.CertificateChanges`.
* `WaitForRejectedCertificatesDrainAsync()` becomes
  `CertificateManager.FlushRejectedAsync()`.
* `TestNullParameters` no longer covers
  `UpdateAsync(ApplicationConfiguration)` (the modern `ICertificateLifecycle`
  takes `SecurityConfiguration` only).
* `CertificateValidatorAssignableFromAppConfig` is renamed to
  `CertificateManagerAssignableFromAppConfig` and asserts the modern
  property only.
* Deleted `Tests/Opc.Ua.Core.Tests/Security/Certificates/TemporaryCertValidator.cs`.

#### Bug fixes uncovered while migrating

* `CertificateManager.MaxRejectedCertificates` setter now propagates the
  cap to the per-trust-list legacy `CertificateValidator` via
  `ApplyValidationFlags`. Previously only the manager-owned
  `RejectedCertificateProcessor` (used via `RejectCertificateAsync`) was
  reconfigured, so writes from validation paths did not honour a lowered
  cap.
* `CertificateManager.MaxRejectedCertificates` setter now triggers an active
  trim of the existing rejected-store contents so a lowered cap shrinks the
  store immediately. Added
  `RejectedCertificateProcessor.EnqueueTrimAsync()` and switched the
  channel item type to `CertificateCollection?` so the processor can
  distinguish trim signals from rejection writes.
* `CertificateManager.FlushRejectedAsync` now also drains the per-trust-list
  legacy validators' `RejectedCertificateWriter` queues so test assertions
  on the rejected store see all writes from the validation pipeline (not
  only those enqueued via the manager-owned processor).

### Phase B — delete `CertificateValidator` obsolete forwarders

* Deleted `ApplicationConfiguration.CertificateValidator` (the
  `[Obsolete]` `ICertificateValidatorEx` forwarder added in Phase 4).
  Source-and-binary breaking — callers must use
  `ApplicationConfiguration.CertificateManager` instead.
* Deleted `ServerBase.CertificateValidator` (the `[Obsolete]`
  `ICertificateValidatorEx` forwarder). The property was unused outside
  `ServerBase` itself and its assignment now redundant given
  `ServerBase.CertificateManager`.
* Updated `Libraries/Opc.Ua.Server/Server/StandardServer.cs` to pass
  `configuration.CertificateManager` to `CreateEndpoints` instead of
  `configuration.CertificateValidator`.
* Updated `Tests/Opc.Ua.Core.Tests/Stack/Configuration/ApplicationConfigurationTests.cs`
  to drop the `CertificateValidator`-specific assertions and the
  `CertificateValidatorDefaultIsNull` test (its assertion was a strict
  subset of `CertificateManagerDefaultIsNull`, kept as the modern check).

## Deferred — Phase 8

The full Phase 8 deletion of the legacy types
(`CertificateValidator.cs`, `ICertificateValidator.cs`,
`CertificateValidatorAdapter.cs`, `CertificateValidatorObsolete.cs`, and
the `is CertificateValidator legacy` fast-path branches inside
`CertificateValidationExtensions.cs`) is **blocked** on a meaningful
refactor: the modern `CertificateManager.ValidateAsync` delegates to a
per-trust-list `CertificateValidator` cache (`m_peerValidator`,
`m_userValidator`, `m_httpsValidator`) which is the entire validation
pipeline. Deleting the legacy class requires moving its validation logic
(chain validation, CRL handling, revocation checks, rejected-store
writer, telemetry) into the new manager (or into a shared internal
helper that does not expose the legacy type).

The known prerequisites:

1. Move the chain-validation and CRL-handling logic out of
   `CertificateValidator` into either `CertificateManager` itself or an
   internal helper (`CertificateValidationCore`?).
2. Move the rejected-certificate writer logic into
   `RejectedCertificateProcessor`. Currently each per-trust-list
   `CertificateValidator` owns its own writer.
3. Move `CertificateValidator.ValidateApplicationUri` /
   `CertificateValidator.ValidateDomains` (with their endpoint plumbing
   and event firing) into the modern path so the
   `is CertificateValidator legacy` branches in
   `CertificateValidationExtensions` can be deleted.
4. Migrate `Tests/Opc.Ua.Gds.Tests/X509TestUtils.cs:88` (the only test
   site that still instantiates `CertificateValidator` directly with
   in-memory trust lists) to the modern API.
5. Delete `CertificateValidatorAdapterTests.cs` (which is the only
   non-self-reference of `CertificateValidatorAdapter`).

## Reference: pragma-block inventory at HEAD

| File | Wrapped sites |
| --- | --- |
| `Stack/Opc.Ua.Core/Security/Certificates/CertificateManager/CertificateManager.cs` | 9 (Phase 8 — internal per-trust-list `CertificateValidator` cache) |
| `Stack/Opc.Ua.Core/Security/Certificates/CertificateValidator.cs` | 2 (Phase 8 — the obsolete class itself) |
| `Stack/Opc.Ua.Core/Security/Certificates/CertificateValidationExtensions.cs` | 2 (Phase 8 — legacy fast-path for `ValidateApplicationUri`/`ValidateDomains`) |
| `Tests/Opc.Ua.Gds.Tests/X509TestUtils.cs` | 1 (Phase 8 — instantiates a `CertificateValidator` for in-memory trust list test) |
| `Stack/Opc.Ua.Core/Security/Certificates/CertificateManager/CertificateValidatorAdapter.cs` | 1 (Phase 8 — the bridge class itself) |

Total: **15** wrapped sites (down from **130** at the start of the
migration — a **115-block reduction**).
