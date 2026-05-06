# Certificate Manager Migration Plan

This document tracks the legacy `CertificateValidator` / `ICertificateValidator` /
`CertificateTypesProvider` usage that survives in the codebase as a binary-compat
bridge while the new `ICertificateManager` / `ICertificateValidatorEx` /
`ICertificateRegistry` API stabilises. It complements
[`Docs/CertificateManager.md`](CertificateManager.md), which documents the
target API.

## Current state (after Phases 1, 2, 3, 4, 5, 6, 7 partial migration)

* The `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` setting in
  `common.props` makes any unsuppressed CS0618 fail the build.
* Phases 1, 2, 3, 4, 5, 6, and 7 have been migrated. Phase 8 (delete the
  legacy bridge) is deferred — it requires rewriting the ~22 legacy-only
  tests in `CertificateValidatorTest.cs` that throw on `ValidateAsync`,
  subscribe to `CertificateValidation` events, or depend on legacy
  `MaxRejectedCertificates = -1` cleanup semantics.
* The remaining CS0618 wrappers count is **43** scoped pragma blocks
  (down from **130** at the start of this branch — a **87-block
  reduction**). Most remaining wrappers are inside the legacy types
  themselves (`CertificateValidator.cs`, `CertificateValidatorAdapter.cs`)
  or in tests that exercise the legacy validator's behaviour.
* `dotnet build UA.slnx -c Debug` is clean of CS0618 errors.
* `dotnet test Tests\Opc.Ua.Core.Tests\Opc.Ua.Core.Tests.csproj -f net10.0`
  passes 6619/6619. Server (920/920) and Client (2176/2176) test suites
  also green.

The remaining wrappers exist *only* so the legacy API can keep working for
downstream consumers that haven't moved off it yet, or so the legacy
test fixtures continue to validate the legacy validator's behaviour
until Phase 8 lands.

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
* `ApplicationConfiguration.CertificateValidator` is now a `[Obsolete]`
  forwarder that exposes `CertificateManager` cast as
  `ICertificateValidatorEx`. The setter only accepts an
  `ICertificateManager` (other values are no-ops).
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

## Deferred — Phase 8 prerequisites

The following are blocked on rewriting the ~22 legacy-only test cases
in `CertificateValidatorTest.cs` that:

1. Catch `ServiceResultException` from `CertificateValidator.ValidateAsync`
   (the new pipeline returns a `CertificateValidationResult` instead).
2. Subscribe to the legacy `CertificateValidation` event (the new
   pipeline exposes a per-error `AcceptError` callback on
   `ICertificateValidatorEx` and per-call
   `CertificateValidationOptions.AcceptError` instead).
3. Depend on the legacy `MaxRejectedCertificates = -1` "delete all"
   side-effect (the new processor honours the cap on the next write
   but does not actively delete existing entries).

When those tests have been rewritten, Phase 8 can delete:

* `CertificateValidator.cs` (the class) and its delegates
  `CertificateValidationEventHandler` / `CertificateUpdateEventHandler`.
* `ICertificateValidator.cs` (the legacy interface).
* `CertificateValidatorAdapter.cs` (only used to expose the modern API
  as the legacy interface).
* `CertificateValidator` legacy branches in
  `CertificateValidationExtensions.cs`.

## Reference: pragma-block inventory at HEAD

The CS0618-suppressing pragma blocks that still wrap legacy
`CertificateValidator` / `CertificateValidation` / `CertificateUpdate`
references:

| File | Wrapped sites |
| --- | --- |
| `Tests/Opc.Ua.Core.Tests/Security/Certificates/CertificateValidatorTest.cs` | 22 (legacy-only test cases — see "Deferred" above) |
| `Stack/Opc.Ua.Core/Security/Certificates/CertificateManager/CertificateManager.cs` | 8 (Phase 8 — internal per-trust-list `CertificateValidator` cache) |
| `Stack/Opc.Ua.Core/Stack/Tcp/UaSCBinaryChannel.cs` | 2 (Phase 8 — file declared in `Opc.Ua.Bindings`; pre-existing) |
| `Stack/Opc.Ua.Core/Security/Certificates/CertificateValidator.cs` | 2 (Phase 8 — the obsolete class itself) |
| `Stack/Opc.Ua.Core/Security/Certificates/CertificateValidationExtensions.cs` | 2 (Phase 8 — legacy fast-path for rejected-store writer) |
| `Tests/Opc.Ua.Core.Tests/Security/Certificates/TemporaryCertValidator.cs` | 3 (Phase 1 cleanup deferred — bridge stays for legacy tests) |
| `Stack/Opc.Ua.Core/Stack/Server/ServerBase.cs` | 1 (Phase 4 — `configuration.CertificateValidator` read at OnServerStarting) |
| `Libraries/Opc.Ua.Server/Server/StandardServer.cs` | 1 (Phase 4 — `configuration.CertificateValidator` passed to `CreateServiceHost`) |
| `Stack/Opc.Ua.Core/Security/Certificates/CertificateManager/CertificateValidatorAdapter.cs` | 1 (Phase 8 — the bridge class itself) |
| `Tests/Opc.Ua.Gds.Tests/X509TestUtils.cs` | 1 (instantiates a fresh `CertificateValidator`) |

Total: **43** wrapped sites (down from **130** at the start of the
migration — an **87-block reduction**).

