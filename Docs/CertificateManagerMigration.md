# Certificate Manager Migration Plan

This document tracks the legacy `CertificateValidator` /
`ICertificateValidator` / `CertificateTypesProvider` migration that
ran across phases 1–8 on this branch.

## Phase 8 — COMPLETE

Phase 8 finished the migration by encapsulating the validation
pipeline into a new internal helper, retiring all legacy types, and
re-routing every consumer onto `CertificateManager`.

* `CertificateValidationCore` (internal sealed) now owns the
  per-trust-list validation state and chain walk previously locked
  inside the legacy `CertificateValidator` class.
* `CertificateValidationHelpers` (public static) hosts the static
  helpers (`FindDomain`, `IsECSecureForProfile`,
  `ValidateServerCertificateApplicationUri`,
  `IsSHA1SignatureAlgorithm`, `IsSignatureValid`).
* `CertificateManager.ValidateAsync`, `ValidateApplicationUri`, and
  `ValidateDomains` delegate to the per-trust-list core and are the
  single entry points for rejected-store enqueueing. The
  per-trust-list `CertificateValidator` cache and its inner
  `RejectedCertificateWriter` have been removed.
* `RejectedCertificateProcessor` now uses per-request
  `TaskCompletionSource` so `WaitForDrainAsync` blocks until the
  latest enqueued chain has been processed (matching the legacy
  behaviour). The processor disposes each chain after processing so
  the per-cert AddRef from `CertificateCollection.Add` is balanced.
* `CertificateValidationExtensions` no longer carries
  `is CertificateValidator legacy` fast-paths; the extensions
  delegate to `CertificateManager` (preferred) or fall through to
  `CertificateValidationHelpers`.
* `Tests/Opc.Ua.Gds.Tests/X509TestUtils.cs` has been migrated to a
  directory-backed `CertificateManager` + `result.IsValid`
  assertions.

### Files deleted

* `Stack/Opc.Ua.Core/Security/Certificates/CertificateValidator.cs`
  (~2700 lines, including the
  `CertificateValidationEventHandler` /
  `CertificateUpdateEventHandler` delegates and the
  `CertificateValidationEventArgs` event-args type).
* `Stack/Opc.Ua.Core/Security/Certificates/ICertificateValidator.cs`.
* `Stack/Opc.Ua.Core/Security/Certificates/CertificateValidatorObsolete.cs`.
* `Stack/Opc.Ua.Core/Security/Certificates/CertificateManager/CertificateValidatorAdapter.cs`.
* `Tests/Opc.Ua.Core.Tests/Security/Certificates/CertificateManager/CertificateValidatorAdapterTests.cs`.

### Files added

* `Stack/Opc.Ua.Core/Security/Certificates/CertificateManager/CertificateValidationCore.cs`.
* `Stack/Opc.Ua.Core/Security/Certificates/CertificateManager/CertificateValidationHelpers.cs`.
* `Stack/Opc.Ua.Core/Security/Certificates/CertificateManager/CertificateUpdateEventArgs.cs`
  (extracted from `CertificateValidator.cs` so server-side
  notification flow keeps compiling — its only fields are a
  `SecurityConfiguration` and an `ICertificateValidatorEx`).

### Pragma block count

The cert-related `#pragma warning disable CS0618` block count has
gone from **130** at the start of the migration to **0** at this
HEAD.

## Historical phase summaries

The following sections describe earlier phases and are retained for
historical reference. They are no longer actionable.

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
  per-trust-list cores pick up changes immediately via
  `ApplyValidationFlags`.
* Added `ICertificateLifecycle.FlushRejectedAsync(CancellationToken)` —
  modern replacement for `CertificateValidator.WaitForRejectedCertificatesDrainAsync`.
* `RejectedCertificateProcessor.SetMaxRejectedCertificates(int)` allows
  the cap to be retuned at runtime; `WaitForDrainAsync` uses a
  TCS-of-most-recent pattern matched by per-request TCS so multi-enqueue
  drain semantics remain correct.
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

