# Certificate Manager Migration Plan

This document tracks the legacy `CertificateValidator` / `ICertificateValidator` /
`CertificateTypesProvider` usage that survives in the codebase as a binary-compat
bridge while the new `ICertificateManager` / `ICertificateValidatorEx` /
`ICertificateRegistry` API stabilises. It complements
[`Docs/CertificateManager.md`](CertificateManager.md), which documents the
target API.

## Current state (after the file-pragma → narrow-pragma cleanup)

* The `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` setting in
  `common.props` makes any unsuppressed CS0618 fail the build.
* All remaining legacy call sites are wrapped in narrow
  `#pragma warning disable CS0618 ... #pragma warning restore CS0618` blocks
  (101 added blocks across 41 production / test files in this branch).
* `dotnet build UA.slnx -c Debug` is clean of CS0618 errors.
* `dotnet test Tests\Opc.Ua.Core.Tests\Opc.Ua.Core.Tests.csproj -f net10.0`
  passes 6616/6616 (86 skipped).

The wrappers exist *only* so the legacy API can keep working for downstream
consumers that haven't moved off it yet. None of the wrapped sites is the
"correct" long-term answer; this plan describes how to retire each one.

## Obsolete API surface (and the recommended replacement)

The `[Obsolete(...)]` messages on the obsolete members are the source of truth.
They are reproduced below for convenience.

### 1. `Opc.Ua.CertificateValidator` (class)

* **Declared:** `Stack/Opc.Ua.Core/Security/Certificates/CertificateValidator.cs:53`
* **Obsolete message:** *"Use ICertificateManager (CertificateManagerFactory.Create)
  and ICertificateValidatorEx instead. See Docs/CertificateManager.md."*
* **Implements (still):** `ICertificateValidator`, `ICertificateValidatorEx`,
  `IDisposable`.
* **Replacement:** `CertificateManagerFactory.Create(SecurityConfiguration, ITelemetryContext, ...)`
  returning an `ICertificateManager`; cast to `ICertificateValidatorEx` for
  per-trust-list validation.

Wrap sites that still create or refer to this type:

| File | Line(s) | Notes |
| --- | --- | --- |
| `Stack/Opc.Ua.Core/Security/Certificates/CertificateValidator.cs` | 2659–2662, 2693–2696 | Public delegates `CertificateValidationEventHandler` and `CertificateUpdateEventHandler` carry a `CertificateValidator sender`. |
| `Stack/Opc.Ua.Core/Schema/ApplicationConfiguration.cs` | 57, 73, 90 | Default and copy ctors instantiate / propagate a `CertificateValidator`. |
| `Stack/Opc.Ua.Core/Security/Certificates/CertificateValidationExtensions.cs` | 85–93, 129–143 | `ValidateApplicationUri` / `ValidateDomains` extension methods special-case `CertificateValidator legacy`. |
| `Stack/Opc.Ua.Core/Security/Certificates/CertificateManager/CertificateManager.cs` | 67–69 (fields), 387, 722–781, 784–803, 805–827 | The new `CertificateManager` internally caches a per-trust-list `CertificateValidator` to keep behaviour byte-compatible. |
| `Libraries/Opc.Ua.Server/Server/StandardServer.cs` | 81, 391, 2209, 2752, 3110 | Subscribe/unsubscribe `CertificateUpdate` event; hand `Configuration.CertificateValidator` to the registration channel; `UpdateAsync(SecurityConfiguration)` on config update. |
| `Libraries/Opc.Ua.Server/Configuration/ConfigurationNodeManager.cs` | 1160 | `UpdateCertificateAsync(SecurityConfiguration, ApplicationUri)` after a GDS push. |
| `Libraries/Opc.Ua.Client/Session/Session.cs` | 291, 1161, 1545, 4192, 4823 | Construction-time check, validation, and `GetIssuersAsync` for outbound client cert chain. |
| `Tests/Opc.Ua.Core.Tests/Security/Certificates/CertificateValidatorTest.cs` | 37 sites — every test method that calls `validator.Update()` returns `CertificateValidator`. |
| `Tests/Opc.Ua.Core.Tests/Security/Certificates/CertificateValidatorAlternate.cs` | 4 sites — same shape. |
| `Tests/Opc.Ua.Core.Tests/Security/Certificates/TemporaryCertValidator.cs` | 118, 138, 141, 178 — exposes a `CertificateValidator Update()` factory used pervasively in tests. |
| `Tests/Opc.Ua.Aot.Tests/AotTestFixture.cs`, `BoilerNodeManagerAotTests.cs`, `GdsTestFixture.cs` | One site each — `CertificateValidation += (s,e) => e.Accept = true;` test wiring. |
| `Tests/Opc.Ua.Gds.Tests/GlobalDiscoveryTestClient.cs`, `GlobalDiscoveryTestServer.cs`, `ServerConfigurationPushTestClient.cs` | Each adds a `CertificateValidation` event handler and declares a `CertificateValidator_CertificateValidation(CertificateValidator, CertificateValidationEventArgs)` callback. |
| `Tests/Opc.Ua.Gds.Tests/X509TestUtils.cs` | 88 — instantiates a fresh `CertificateValidator` for chain tests. |
| `Tests/Opc.Ua.Core.Tests/Stack/Configuration/ApplicationConfigurationTests.cs` / `ApplicationConfigurationEncodingTests.cs` | Tests that assert default-ctor behaviour for `ApplicationConfiguration.CertificateValidator`. |

### 2. `Opc.Ua.ICertificateValidator` (interface)

* **Declared:** `Stack/Opc.Ua.Core/Security/Certificates/ICertificateValidator.cs:42`
* **Obsolete message:** *"Use ICertificateValidatorEx (from ICertificateManager)
  instead. See Docs/CertificateManager.md."*
* **Replacement:** `ICertificateValidatorEx` (composed inside `ICertificateManager`).

Wrap sites:

| File | Line(s) | Notes |
| --- | --- | --- |
| `Stack/Opc.Ua.Core/Security/Certificates/CertificateManager/CertificateValidatorAdapter.cs` | 44 | The class declaration `: ICertificateValidator` is the only obsolete reference; its body forwards to the new API. |
| `Tests/Opc.Ua.Core.Tests/Security/Certificates/TemporaryCertValidator.cs` | 118 | Public property of type `ICertificateValidator`. |
| `Applications/Quickstarts.Servers/ReferenceServer/ReferenceServer.cs` | 575 | `private ICertificateValidator m_userCertificateValidator;` field. |

### 3. `Opc.Ua.CertificateTypesProvider` (class)

* **Declared:** `Stack/Opc.Ua.Core/Security/Certificates/CertificateTypesProvider.cs:46`
* **Obsolete message:** *"Use ICertificateRegistry (composed in ICertificateManager)
  instead. See Docs/CertificateManager.md."*
* **Replacement:** `ICertificateRegistry` (composed in `ICertificateManager`).

Wrap sites:

| File | Line(s) | Notes |
| --- | --- | --- |
| `Stack/Opc.Ua.Core/Stack/Bindings/ITransportBindings.cs` | 125 | `ITransportListenerFactory.CreateServiceHost(...)` parameter. |
| `Stack/Opc.Ua.Core/Stack/Transport/ITransportListener.cs` | 83 | `ITransportListener.CertificateUpdate(...)` parameter. |
| `Stack/Opc.Ua.Core/Stack/Transport/TransportListenerSettings.cs` | 55 | `ServerCertificateTypesProvider` property type. |
| `Stack/Opc.Ua.Core/Stack/Tcp/TcpListenerChannel.cs` | 53 | Constructor parameter. |
| `Stack/Opc.Ua.Core/Stack/Tcp/TcpServerChannel.cs` | 56 | Constructor parameter. |
| `Stack/Opc.Ua.Core/Stack/Tcp/TcpServiceHost.cs` | 64, 143 | `CreateServiceHost` parameter + interior `SetServerCertificateInEndpointDescription` call. |
| `Stack/Opc.Ua.Core/Stack/Tcp/TcpTransportListener.cs` | 805, 1207 | `CertificateUpdate(...)` parameter and field. |
| `Stack/Opc.Ua.Core/Stack/Tcp/UaSCBinaryChannel.cs` | 78, 103 | Two constructor parameters (one public, one private). |
| `Stack/Opc.Ua.Core/Stack/Tcp/UaSCBinaryChannel.Asymmetric.cs` | 1418 | `m_serverCertificateTypesProvider` field. |
| `Stack/Opc.Ua.Bindings.Https/Stack/Https/HttpsServiceHost.cs` | 67 | `CreateServiceHost` parameter. |
| `Stack/Opc.Ua.Bindings.Https/Stack/Https/HttpsTransportListener.cs` | 539, 643 | `CertificateUpdate(...)` parameter and field. |
| `Stack/Opc.Ua.Core/Stack/Server/ServerBase.cs` | 109, 630, 786, 796, 805, 812, 846, 1467–1539 | Disposal, `OnCertificateUpdateAsync`, `SetServerCertificateInEndpointDescription`, and the bulk of `OnServerStarting` that creates the provider. |
| `Libraries/Opc.Ua.Server/Server/StandardServer.cs` | 428, 498, 500, 2249, 2857 | Server-side endpoint setup and cert chain emission. |

### 4. `Opc.Ua.ApplicationConfiguration.CertificateValidator` (property)

* **Declared:** `Stack/Opc.Ua.Core/Stack/Configuration/ApplicationConfiguration.cs:107`
* **Obsolete message:** *"Use ApplicationConfiguration.CertificateManager
  (ICertificateManager) instead. See Docs/CertificateManager.md."*
* **Replacement:** `ApplicationConfiguration.CertificateManager` (already
  initialized eagerly in `ApplicationConfiguration` ctors and `ApplicationInstance`).

Wrap sites are spread across:

* `Libraries/Opc.Ua.Configuration/ApplicationInstance.cs:65` (Dispose path).
* `Libraries/Opc.Ua.Client/Session/Session.cs` and
  `Libraries/Opc.Ua.Client/Session/DefaultSessionFactory.cs` (validation
  fallbacks).
* `Stack/Opc.Ua.Core/Stack/Client/ClientChannelManager.cs:178, 256` (channel
  settings).
* `Stack/Opc.Ua.Core/Stack/Server/ServerBase.cs:1539` and
  `Libraries/Opc.Ua.Server/Server/StandardServer.cs:2210, 2752`.
* `Stack/Opc.Ua.Core/Stack/Tcp/TcpServiceHost.cs:144` and
  `Stack/Opc.Ua.Bindings.Https/Stack/Https/HttpsServiceHost.cs:201` (transport
  bring-up).
* All sample apps (`UAClient.cs`, `ConnectTester.cs`, `UAServer.cs`,
  `OpcUaSessionManager.cs`).
* All test fixtures (`AotTestFixture.cs`, `GdsTestFixture.cs`,
  `GlobalDiscoveryTest*.cs`, `ServerConfigurationPushTestClient.cs`,
  `Application*Tests.cs`).

### 5. `Opc.Ua.ServerBase.CertificateValidator` (property)

* **Declared:** `Stack/Opc.Ua.Core/Stack/Server/ServerBase.cs:695`
* **Obsolete message:** *"Use ServerBase.CertificateManager (ICertificateManager)
  instead. See Docs/CertificateManager.md."*
* **Replacement:** `ServerBase.CertificateManager` (existing public property of
  type `CertificateManager`).

Wrap sites: `StandardServer.cs:81, 391, 2209, 3110`,
`ReferenceServer.cs:489` (the `else` branch of `VerifyX509IdentityToken`).

### 6. `Opc.Ua.ServerBase.InstanceCertificateTypesProvider` (property)

* **Declared:** `Stack/Opc.Ua.Core/Stack/Server/ServerBase.cs:702`
* **Obsolete message:** *"Use ServerBase.CertificateManager (ICertificateRegistry)
  instead. See Docs/CertificateManager.md."*
* **Replacement:** `ServerBase.CertificateManager` (cast to / use as
  `ICertificateRegistry` plus `ICertificateValidatorEx`).

Wrap sites: `ServerBase.cs:109, 786, 796, 805, 812, 846, 1467–1538`,
`StandardServer.cs:428, 498, 500, 2249, 2857`.

### 7. Legacy `CertificateValidator` instance methods still in use

* `Update(CertificateTrustList issuer, CertificateTrustList trusted, CertificateStoreIdentifier rejected)` —
  used by `TemporaryCertValidator.Update()` and indirectly by every test that
  configures issuer/trusted/rejected directories. Replacement:
  `CertificateManagerFactory.Create(...)` + `ICertificateManager.LoadApplicationCertificatesAsync(...)`,
  with the same options surfaced through `CertificateManagerOptions`.
* `UpdateAsync(SecurityConfiguration, CancellationToken)` — used by
  `StandardServer.OnUpdateConfigurationAsync` (`StandardServer.cs:2752`).
  Replacement: rebuild the `CertificateManager` (or expose an
  `ICertificateManager.UpdateAsync(SecurityConfiguration, CancellationToken)`
  on `ICertificateLifecycle`).
* `UpdateCertificateAsync(SecurityConfiguration, string applicationUri)` — used
  by `ConfigurationNodeManager` after a GDS push (`ConfigurationNodeManager.cs:1160`).
  Replacement: `ICertificateManager.LoadApplicationCertificatesAsync(SecurityConfiguration, applicationUri, ct)`
  followed by an explicit notification on the `ICertificateLifecycle.CertificateChanges`
  observable.
* `GetIssuersAsync(Certificate, IList<CertificateIdentifier>, CancellationToken)` — used in
  `Session.cs:4823` to assemble an outbound client certificate chain.
  Replacement: `ICertificateRegistry.GetIssuersAsync(...)` (already the contract
  that `CertificateManager` satisfies internally).
* `ValidateAsync(...)` — covered by `ICertificateValidatorEx.ValidateAsync(...)`.
* `ValidateApplicationUri` / `ValidateDomains` — already exposed as extension
  methods on `ICertificateValidatorEx` in
  `CertificateValidationExtensions.cs`; the `is CertificateValidator legacy`
  branches inside those extensions only exist to keep the legacy
  `CertificateValidation` event firing. Once event consumers migrate, those
  `is` checks (and the wrappers around them) can be deleted.
* `CertificateValidation` event (legacy `CertificateValidationEventHandler`) —
  used by GDS test fixtures and the AOT test fixtures. Replacement:
  `CertificateValidationOptions.AcceptError` per-call hook **or**
  `ICertificateManager.AcceptError` global hook (already wired in
  `CertificateManager.GetOrCreateValidator`).

## Migration ordering and risks

The migration is best done bottom-up: replace producers (transports, server
base, application configuration) before consumers (sessions, sample apps,
tests). Each step should keep the bridge alive until all callers in the same
layer move.

### Phase 0 — confirm the seam (already done)

* `ICertificateManager` is created eagerly by `ApplicationInstance` and by
  `ServerBase.OnServerStarting` (see `ServerBase.cs:1454-1462`).
* `ApplicationConfiguration.CertificateManager` property is in place
  (`ApplicationConfiguration.cs` non-Schema partial). Consumers can already
  call into the new API; what they're still doing is reading the legacy
  `CertificateValidator` field for backward-compat.

### Phase 1 — retire the test bridge

`TemporaryCertValidator` is the entry point used by
`CertificateValidatorTest`, `CertificateValidatorAlternate`, and most other
core tests.

1. Add a parallel `TemporaryCertificateManager` helper in
   `Tests/Opc.Ua.Core.Tests/Security/Certificates/` that exposes
   `ICertificateManager Manager => m_manager;` and an
   `ICertificateValidatorEx Validator => m_manager;` instead of a
   `CertificateValidator`.
2. Migrate `CertificateValidatorTest` and `CertificateValidatorAlternate` test
   methods one fixture at a time: replace
   `CertificateValidator certValidator = validator.Update();` with
   `ICertificateValidatorEx certValidator = validator.Validator;`. The body of
   each test then no longer touches an obsolete type, and the wrapper pragmas
   inside the test methods can be deleted as the tests move.
3. Once all tests use the new helper, delete `TemporaryCertValidator`.
4. **Risk:** the legacy `CertificateValidator` raises the
   `CertificateValidation` event synchronously inside the validation pipeline
   for *every* error. The new `CertificateValidationOptions.AcceptError` hook
   fires once per validation call after collecting all errors. Tests that
   subscribe to the event for trace/logging will see fewer callbacks. Convert
   them to use `CertificateValidationOptions.AcceptError`.

### Phase 2 — retire legacy event handlers in test fixtures

Apply the same idea to the GDS / AOT test fixtures
(`GlobalDiscoveryTestClient.cs`, `GlobalDiscoveryTestServer.cs`,
`ServerConfigurationPushTestClient.cs`, `AotTestFixture.cs`,
`BoilerNodeManagerAotTests.cs`, `GdsTestFixture.cs`).

* Replace the `if (config.CertificateValidator is CertificateValidator legacy)
  { legacy.CertificateValidation += ... }` pattern with
  `config.CertificateManager.AcceptError = (cert, err) => true;`
  (or per-call `CertificateValidationOptions.AcceptError`).
* Drop the per-fixture `CertificateValidator_CertificateValidation` callbacks
  (`GlobalDiscoveryTestClient.cs:343`, `GlobalDiscoveryTestServer.cs:212`,
  `ServerConfigurationPushTestClient.cs:198`).

**Risk:** the per-event callback receives the legacy
`CertificateValidationEventArgs` with `Accept`, `AcceptAll`, and per-error
context. The new `AcceptError(Certificate, ServiceResult)` callback returns a
single `bool`. Make sure tests don't rely on the `AcceptAll` short-circuit
behaviour (none currently do, but check any call sites that set both
`Accept = true` *and* `AcceptAll = true`).

### Phase 3 — retire `CertificateTypesProvider` from server bring-up

The server transport bring-up still passes
`CertificateTypesProvider instanceCertificateTypesProvider` through:
`ITransportListenerFactory.CreateServiceHost` →
`TcpServiceHost.CreateServiceHost` / `HttpsServiceHost.CreateServiceHost` →
`ServerBase.CreateServiceHostEndpoint` → `TransportListenerSettings` →
`UaSCUaBinaryChannel`.

1. Add an overload of `ITransportListenerFactory.CreateServiceHost` and
   `ITransportListener.CertificateUpdate` that takes
   `ICertificateRegistry` (or the full `ICertificateManager`) instead of
   `CertificateTypesProvider`. Keep the legacy overload as a default
   interface method that forwards to the new one for one release.
2. Update the in-repo transports (`TcpServiceHost`, `HttpsServiceHost`,
   `TcpTransportListener`, `HttpsTransportListener`, the channel hierarchy)
   to consume `ICertificateRegistry` directly. The signatures
   `UaSCUaBinaryChannel(string, BufferManager, ChannelQuotas, CertificateTypesProvider, ...)`
   become `(..., ICertificateRegistry serverCertificates, ...)`.
3. Update `ServerBase.OnServerStarting` and
   `ServerBase.CreateServiceHostEndpoint` to populate the new fields. The
   `OnCertificateUpdateAsync` handler already gets an
   `ICertificateValidatorEx` from `CertificateUpdateEventArgs`; mirror the
   same for the certificate registry.
4. Remove `InstanceCertificateTypesProvider` and the bridge inside
   `ServerBase.OnServerStarting` (the wrap currently extends from
   `ServerBase.cs:1467–1538`).
5. Delete `CertificateTypesProvider`.

**Risk:** `CertificateTypesProvider` knows how to load the *issuer chain* for
each application certificate (`LoadCertificateChainAsync`,
`LoadCertificateChainRaw`). The new `ICertificateRegistry` must offer the same
operations (it already exposes `GetIssuersAsync`); validate that
`ServerBase.OnCertificateUpdateAsync` and
`StandardServer.CreateSession`'s `SendCertificateChain` path still produce the
exact same DER blobs. Add tests that compare the byte arrays before/after the
refactor.

### Phase 4 — retire `ApplicationConfiguration.CertificateValidator`

Once tests, bindings, and the server base no longer touch
`CertificateValidator`, the property on `ApplicationConfiguration` can be
collapsed to:

1. A read path that delegates to `CertificateManager`. Today's
   `ApplicationConfiguration.CertificateValidator` returns
   `ICertificateValidatorEx`, so change it (and its setter) to forward to
   `CertificateManager` rather than holding its own field.
2. Remove the `CertificateValidator = new CertificateValidator(m_telemetry)`
   initialization in the three ctors of
   `Stack/Opc.Ua.Core/Schema/ApplicationConfiguration.cs:57, 73, 90`.
3. Update the copy-constructor and serializer behaviour. The current copy
   ctor copies `CertificateValidator` by reference; the new one should copy
   `CertificateManager` instead. Tests
   `ApplicationConfigurationTests.CopyConstructorCopiesCertificateValidator`
   and `ApplicationConfigurationTests.CertificateValidatorGetSet` move with
   the change.
4. Delete the property; everything wrapped in `CS0618` for it goes away.

**Risk:** binary compatibility. The `[Obsolete]` attribute alone is not a
breaking change; *removing* the property is. Keep this as a major-version
change.

### Phase 5 — retire `ServerBase.CertificateValidator`

Replace direct uses of `ServerBase.CertificateValidator` with
`ServerBase.CertificateManager` (typed as `ICertificateValidatorEx` /
`ICertificateManager`). Sites:

* `StandardServer.cs:81` — wire the certificate-update notification through
  `CertificateManager.CertificateChanges` (`ICertificateLifecycle`)
  instead of subscribing to the legacy `CertificateValidation`/`CertificateUpdate`
  events.
* `StandardServer.cs:391` — replace
  `await CertificateValidator.ValidateAsync(...)` with
  `await CertificateManager.ValidateAsync(clientCertificateChain, TrustListIdentifier.Peers, ct)`.
* `StandardServer.cs:465` — replace `CertificateValidator.ValidateDomains(...)`
  with the extension on `ICertificateValidatorEx` (already supported).
* `StandardServer.cs:2209` — copy `CertificateManager` instead of
  `CertificateValidator` when building the registration channel
  configuration.
* `StandardServer.cs:3110` — delete the
  `CertificateValidator is CertificateValidator legacyCertValidator` event
  wiring; the modern subscription is the
  `m_certManagerSubscription = CertificateManager.CertificateChanges.Subscribe(...)`
  line right beneath it.
* `ReferenceServer.cs:489` — the `else` branch is the
  "no user validator configured" fall-through; redirect it to
  `CertificateManager.ValidateAsync(token.Certificate, TrustListIdentifier.Users, ct)`.

### Phase 6 — retire `Session`-side fallbacks and `ClientChannelManager` settings

* `Session.cs:291` — the `null` check on
  `ApplicationConfiguration.CertificateValidator` becomes redundant once
  `CertificateManager` is mandatory.
* `Session.cs:1161, 1545, 4192` — drop the
  `(ICertificateValidatorEx?)m_configuration.CertificateManager ?? m_configuration.CertificateValidator`
  fallback. `ICertificateManager` already implements `ICertificateValidatorEx`.
* `Session.cs:4823` — replace `CertificateValidator.GetIssuersAsync` with
  `CertificateManager.GetIssuersAsync` (or the registry's equivalent).
* `ClientChannelManager.cs:177, 256` — `TransportChannelSettings.CertificateValidator`
  should be set from `configuration.CertificateManager` directly. Either keep
  the property typed as `ICertificateValidatorEx` (and delete the obsolete
  fallback) or rename it to `CertificateManager`.
* `DefaultSessionFactory.cs:252` — same fallback pattern; same fix.

### Phase 7 — sample applications

Once the libraries are clean, fix the samples:

* `UAClient.cs`, `ConnectTester.cs`, `UAServer.cs`, `OpcUaSessionManager.cs` —
  swap `m_configuration.CertificateValidator.AcceptError = ...` for
  `m_configuration.CertificateManager.AcceptError = ...`.
* `ReferenceServer.cs` — change
  `private ICertificateValidator m_userCertificateValidator` to
  `private ICertificateValidatorEx m_userCertificateValidator` and update its
  initialization (a `CertificateManager` for the Users trust-list works).

### Phase 8 — delete the bridge

Once nothing outside the obsolete types references them, delete:

* `CertificateValidator` (the class) and its delegates
  `CertificateValidationEventHandler`, `CertificateUpdateEventHandler`.
* `ICertificateValidator` (the legacy interface).
* `CertificateTypesProvider`.
* `CertificateValidatorAdapter` (only exists to expose the modern API as the
  legacy interface).
* The `is CertificateValidator legacy` branches inside
  `CertificateValidationExtensions.cs` (the modern fall-through is the only
  path that remains).

The CS0618 wrappers in this tree all disappear in the same change.

## Cross-cutting risks to watch

1. **Event-vs-callback semantics.** The legacy
   `CertificateValidator.CertificateValidation` event lets a handler set
   `e.Accept = true` and `e.AcceptAll = true` to make the rest of the chain
   pass without further callbacks. The new `AcceptError(Certificate,
   ServiceResult)` returns a single `bool` *per error*. Don't blindly forward
   the legacy callback; review each handler's intent.
2. **Synchronous Dispose on legacy validator.** `ApplicationInstance.cs:65`
   does `(ApplicationConfiguration?.CertificateValidator as IDisposable)?.Dispose()`.
   The new `CertificateManager` is itself `IDisposable` and is disposed two
   lines below. After Phase 4 the redundant cast goes away.
3. **Rejected-store writes.** The legacy validator has its own rejected-store
   writer (and the `is CertificateValidator legacy` branches inside
   `ValidateApplicationUri` / `ValidateDomains` exist *only* to keep that
   writer running). The new `RejectedCertificateProcessor` lives inside
   `CertificateManager`; verify before ripping out the legacy code that
   trace/log/store outputs match.
4. **Copy semantics.** `ApplicationConfiguration` copy constructor currently
   copies the legacy `CertificateValidator` by reference (intentional — both
   sides share the same trust list and event subscribers). Make sure the
   replacement copies `CertificateManager` with the same shared-instance
   semantics or document the change in `Docs/CertificateManager.md`.
5. **`CreateSessionAsync` send-chain.** `StandardServer.cs:498-504` selects
   between full chain and leaf-only based on
   `InstanceCertificateTypesProvider.SendCertificateChain`. The new path
   should read `CertificateManager.SendCertificateChain`. Add an integration
   test that asserts the on-wire `serverCertificate` is byte-identical for
   both modes before/after.
6. **`OnUpdateConfigurationAsync`.** `StandardServer.cs:2752`
   `cfgUpdateValidator.UpdateAsync(SecurityConfiguration, ct)` is called
   while holding `m_semaphoreSlim`. Make sure the replacement on
   `CertificateManager` honours the same locking / cancellation contract;
   running the existing
   `ConfigurationNodeManagerTest.UpdateCertificateAsync` exercises this path.

## Suggested sequencing

| Order | Step | Owners (suggested) |
| --- | --- | --- |
| 1 | Phase 1 — `TemporaryCertValidator` → `TemporaryCertificateManager`. | Test maintainers. |
| 2 | Phase 2 — remove legacy `CertificateValidation` event handlers from test fixtures. | Test maintainers. |
| 3 | Phase 3 — drive `CertificateTypesProvider` out of transports / server base. | Stack core. |
| 4 | Phase 4 — drop `ApplicationConfiguration.CertificateValidator`. | Stack core (binary-compat call). |
| 5 | Phase 5 — retire `ServerBase.CertificateValidator`. | Server team. |
| 6 | Phase 6 — clean up `Session` and `ClientChannelManager`. | Client team. |
| 7 | Phase 7 — sample apps. | Anyone. |
| 8 | Phase 8 — delete the bridge. | Stack core. |

Each phase ends with: build clean, full test suite green, no new
`#pragma warning disable CS0618` introduced.

## Reference: file inventory after the narrow-pragma cleanup

The 41 production / test files that previously carried a file-level
`#pragma warning disable CS0618` now use scoped `disable/restore` pairs only
around legacy call sites. The complete list (with current site counts after
this pass) is:

| File | Wrapped sites |
| --- | --- |
| `Applications/ConsoleReferenceClient/ConnectTester.cs` | 1 |
| `Applications/ConsoleReferenceClient/UAClient.cs` | 2 |
| `Applications/ConsoleReferenceServer/UAServer.cs` | 1 |
| `Applications/McpServer/OpcUaSessionManager.cs` | 2 |
| `Applications/Quickstarts.Servers/ReferenceServer/ReferenceServer.cs` | 2 |
| `Libraries/Opc.Ua.Client/Session/DefaultSessionFactory.cs` | 1 |
| `Libraries/Opc.Ua.Client/Session/Session.cs` | 5 |
| `Libraries/Opc.Ua.Configuration/ApplicationConfigurationBuilder.cs` | 0 (file-level pragma simply removed; no obsolete references survive) |
| `Libraries/Opc.Ua.Configuration/ApplicationInstance.cs` | 1 |
| `Libraries/Opc.Ua.Server/Configuration/ConfigurationNodeManager.cs` | 1 |
| `Libraries/Opc.Ua.Server/Server/StandardServer.cs` | 9 |
| `Stack/Opc.Ua.Bindings.Https/Stack/Https/HttpsServiceHost.cs` | 2 |
| `Stack/Opc.Ua.Bindings.Https/Stack/Https/HttpsTransportListener.cs` | 2 |
| `Stack/Opc.Ua.Core/Schema/ApplicationConfiguration.cs` | 3 |
| `Stack/Opc.Ua.Core/Security/Certificates/CertificateManager/CertificateManager.cs` | 5 |
| `Stack/Opc.Ua.Core/Security/Certificates/CertificateManager/CertificateValidatorAdapter.cs` | 1 |
| `Stack/Opc.Ua.Core/Security/Certificates/CertificateValidationExtensions.cs` | 2 |
| `Stack/Opc.Ua.Core/Security/Certificates/CertificateValidator.cs` | 2 |
| `Stack/Opc.Ua.Core/Stack/Bindings/ITransportBindings.cs` | 1 |
| `Stack/Opc.Ua.Core/Stack/Client/ClientChannelManager.cs` | 2 |
| `Stack/Opc.Ua.Core/Stack/Configuration/ApplicationConfiguration.cs` | 0 (file-level pragma removed; no obsolete references survive) |
| `Stack/Opc.Ua.Core/Stack/Server/ServerBase.cs` | 6 |
| `Stack/Opc.Ua.Core/Stack/Tcp/TcpListenerChannel.cs` | 1 |
| `Stack/Opc.Ua.Core/Stack/Tcp/TcpServerChannel.cs` | 1 |
| `Stack/Opc.Ua.Core/Stack/Tcp/TcpServiceHost.cs` | 2 |
| `Stack/Opc.Ua.Core/Stack/Tcp/TcpTransportListener.cs` | 2 |
| `Stack/Opc.Ua.Core/Stack/Tcp/UaSCBinaryChannel.Asymmetric.cs` | 1 |
| `Stack/Opc.Ua.Core/Stack/Tcp/UaSCBinaryChannel.cs` | 2 |
| `Stack/Opc.Ua.Core/Stack/Transport/ITransportListener.cs` | 1 |
| `Stack/Opc.Ua.Core/Stack/Transport/TransportListenerSettings.cs` | 1 |
| `Tests/Opc.Ua.Aot.Tests/AotTestFixture.cs` | 1 |
| `Tests/Opc.Ua.Aot.Tests/BoilerNodeManagerAotTests.cs` | 1 |
| `Tests/Opc.Ua.Aot.Tests/GdsTestFixture.cs` | 1 |
| `Tests/Opc.Ua.Core.Tests/Security/Certificates/CertificateValidatorAlternate.cs` | 4 |
| `Tests/Opc.Ua.Core.Tests/Security/Certificates/CertificateValidatorTest.cs` | 37 |
| `Tests/Opc.Ua.Core.Tests/Security/Certificates/TemporaryCertValidator.cs` | 3 |
| `Tests/Opc.Ua.Core.Tests/Stack/Configuration/ApplicationConfigurationEncodingTests.cs` | 1 |
| `Tests/Opc.Ua.Core.Tests/Stack/Configuration/ApplicationConfigurationTests.cs` | 2 |
| `Tests/Opc.Ua.Gds.Tests/GlobalDiscoveryTestClient.cs` | 2 |
| `Tests/Opc.Ua.Gds.Tests/GlobalDiscoveryTestServer.cs` | 2 |
| `Tests/Opc.Ua.Gds.Tests/ServerConfigurationPushTestClient.cs` | 2 |
| `Tests/Opc.Ua.Gds.Tests/X509TestUtils.cs` | 1 |

Total wrapped sites: **101**. (Some files contain more than one obsolete
reference per pragma block; a "site" here counts pragma blocks, not
individual references.)
