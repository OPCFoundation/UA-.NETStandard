# Migration Guide

This document is the landing page for migrating your application between
versions of the OPC UA .NET Standard Stack. The detailed per-version
content lives in the [`migrate/`](migrate/) sub-folder; this page is the
index that points you at the right version folder and keeps the small
legacy migration notes inline.

## General principles

1. All API that is replaced with newer API is marked `[Obsolete]` and
   code should compile and work albeit of the warnings (which can be
   suppressed). `[Obsolete]` API will be cleaned up in the next *minor*
   version increment. We therefore recommend upgrading from minor
   version to minor version and fixing all `[Obsolete]` warnings as you
   go along.
2. API that cannot be supported anymore will be removed in a minor
   version and migration steps documented in the version sub-folder.
   We try to keep this to an absolute minimum.
3. Bugs or issues found in obsoleted API are not supported.
4. We follow semver, but do not use the major version indicator to
   denote breaking changes like (1) or (2) as we should if we followed
   related conventions. We are a small team and cannot afford to
   maintain previous major versions, therefore we try to keep cases of
   (2) to a minimum and expect you to upgrade to the next minor version
   within 6 months of release.

> **Pro TIP.** Point your favourite coding agent at this guide and let
> it do the migration work for you. The
> [`opcua-v20-migration`](../.agents/skills/opcua-v20-migration/SKILL.md)
> agent skill knows when to load which sub-doc and runs the
> migration-analyzer codefixer end-to-end.

## Per-version migration index

| From | To | Where to read |
| --- | --- | --- |
| `1.5.378` | `2.0.x` | [`migrate/2.0.x/`](migrate/2.0.x/README.md) — landing page + 12 thematic sub-docs (telemetry, packages, source-generation, types, encoders, node-states, identity, certificates, configuration, sessions-subscriptions, alarms-model-change, timeprovider). |
| `1.05.377` | `1.05.378` | [§ inline below](#migrating-from-105377-to-105378) — small enough to keep on this page. |
| `1.04` | `1.05` | [§ inline below](#migrating-from-104-to-105) — small enough to keep on this page. |

Looking for the broader narrative (non-prescriptive overview of what
changed in a release)? See
[What's New in 2.0](WhatsNewIn2.0.md).

## Migrating from 1.05.377 to 1.05.378

### Asynchronous as default

The server now supports `AsyncNodeManagers`; see
[Server Async (TAP) Support](AsyncServerSupport.md). The client APIs are
async by default and all synchronous and APM-based API has been
deprecated. To migrate, update your code to use the `Async` version of
every API where possible. Not recommended but for expedience you can
call the `Async` version synchronously with
`GetAwaiter().GetResult()`.

### Observability

[Diagnostics](Diagnostics.md) is now plumbed through
`ITelemetryContext` in preparation for better dependency-injection
support. The legacy static `Utils.SetLogger` / `Utils.Trace*` model
has been removed in 2.0; the replacements below cover the most
common migration patterns.

#### From static logger management

```csharp
// OLD - No longer works (removed in 2.0)
Utils.SetLogger(myLogger);
Utils.SetLogLevel(LogLevel.Information);

// NEW - Use ITelemetryContext via DefaultTelemetry / ApplicationInstance
var telemetryContext = DefaultTelemetry.Create(builder => builder.AddConsole());
var applicationInstance = new ApplicationInstance(telemetryContext);
```

#### From static `Utils.Log*` methods

```csharp
// OLD - Obsolete
Utils.LogInformation("Connection established to {0}", endpoint);
Utils.LogError(exception, "Failed to connect to {0}", endpoint);

// NEW - Context-based logging
var logger = telemetryContext.CreateLogger<MyClass>();
logger.LogInformation("Connection established to {Endpoint}", endpoint);
logger.LogError(exception, "Failed to connect to {Endpoint}", endpoint);
```

#### From `Utils.Trace*` methods

```csharp
// OLD - Obsolete
Utils.Trace("Processing request {0}", id);
Utils.Trace(TraceMasks.Information, "Request processed successfully");

// NEW - Structured logging with context
var logger = telemetryContext.CreateLogger<MyClass>();
logger.LogInformation("Processing request {RequestId}", id);
logger.LogInformation("Request processed successfully");
```

#### Non-exhaustive summary of breaking changes

The telemetry-context rollout touched many core types. The full list
&mdash; including replaced constructors, modified factory methods,
and `[Obsolete]` shims &mdash; is below. Most affected APIs are
internal to the stack; the most common external impact is that
**applications must create an `ApplicationInstance` with an explicit
telemetry context**.

##### Core infrastructure constructors

- `ServiceMessageContext()` &rarr; `ServiceMessageContext(ITelemetryContext)`
  (parameter-less constructor removed).
- `EncodeableFactory()` &rarr; `EncodeableFactory(ITelemetryContext)`
  (parameter-less constructor removed).
- `ApplicationConfiguration` gains
  `ApplicationConfiguration(ITelemetryContext)`.
- `ContentFilter.Evaluate(...)` moved to a new `FilterEvaluator`
  class that requires telemetry context.

##### Interface additions

- `IServiceMessageContext` gains
  `ITelemetryContext Telemetry { get; }`.
- `ITransportBindingFactory<T>.Create(...)` now takes
  `ITelemetryContext`.

##### System context

- `SystemContext()` &rarr; `SystemContext(ITelemetryContext)` and
  `SystemContext(IOperationContext, ITelemetryContext)`
  (parameter-less constructor removed).

##### Certificate and security

- `CertificateValidator` gains
  `CertificateValidator(ITelemetryContext)`.
- `CertificateStoreIdentifier.CreateStore(string, ITelemetryContext)`
  and `OpenStore(ITelemetryContext)` &mdash; custom stores must
  implement the new signature.
- `CertificateIdentifier.OpenStore(ITelemetryContext)` &mdash; same
  custom-store impact.
- `CertificateIdentifierCollection` no longer implements
  `ICertificateStore`; use `CertificateIdentifierCollectionStore`
  for the read-only store behaviour.
- `DirectoryCertificateStore` and `X509CertificateStore` lose their
  parameter-less constructors; pass an `ITelemetryContext`.

##### Transport layer

- `TcpTransportChannel`, `TcpTransportListener`,
  `HttpsTransportListener`: parameter-less constructors removed;
  factory `Create(...)` methods now take an `ITelemetryContext`.
- `TransportBindings.GetChannel(uriScheme, telemetry)` /
  `GetListener(uriScheme, telemetry)`: telemetry parameter added.

##### Client library

- `SessionClient`: new constructor
  `(ITransportChannel, ITelemetryContext)`.
- `Subscription(ITelemetryContext)` (otherwise attached on
  `AddSubscription`).
- `Browser(ISession, ITelemetryContext)` (otherwise set via the new
  `Telemetry` property).
- `ReverseConnectManager(ITelemetryContext)`; default constructor
  `[Obsolete]`.
- `DefaultSessionFactory(ITelemetryContext)` and the **new**
  `ManagedSessionFactory(ITelemetryContext)` (which wraps raw
  sessions in reconnect-aware `ManagedSession`s).
- `TraceableSessionFactory(ITelemetryContext)` and
  `TraceableSession(ISession, ITelemetryContext)`.
- `ComplexTypeSystem(..., ITelemetryContext)`.
- `SessionReconnectHandler(ITelemetryContext, bool, int)`.

##### Configuration library

- `ApplicationInstance(ITelemetryContext)` and
  `ApplicationInstance(ITelemetryContext, ApplicationConfiguration)`;
  non-telemetry constructors `[Obsolete]`.
- `ConfiguredEndpointCollection.Load(..., ITelemetryContext)`.
- `ConfiguredEndpoint.UpdateFromServerAsync(ITelemetryContext, ...)`.

##### Server library

- `DataChangeMonitoredItemQueue`, `EventMonitoredItemQueue`,
  `MonitoredItemQueueFactory`: constructors now take
  `ITelemetryContext`.

##### PubSub library

- `UaPubSubApplication.Create(IUaPubSubDataStore, ITelemetryContext)`.
- `UaPubSubConfigurator(ITelemetryContext)`.
- `UdpClientBroadcast(..., ITelemetryContext)` (internal).
- `UdpClientUnicast(..., ITelemetryContext)` (internal).

##### Method signatures

- `UserIdentityToken.GetOrCreateCertificate(ITelemetryContext)`
  &mdash; certificate creation is no longer implicit on the
  `Certificate` getter; call this method explicitly when needed.
- `ServerSecurityPolicy.CalculateSecurityLevel(..., ILogger)`
  &mdash; the non-logger overload is `[Obsolete]`.

##### Removed and `[Obsolete]` `Utils` static APIs

Removed entirely:

- `Utils.SetLogger(ILogger)`
- `Utils.SetLogLevel(LogLevel)`

Marked `[Obsolete]` and slated for removal:

- `Utils.SetTraceOutput`, `Utils.SetTraceMask`, `Utils.SetTraceLog`,
  `Utils.TraceMask`, `Utils.Tracing`, `Utils.UseTraceEvent`.
- All `Utils.Trace*` overloads (basic, exception, `TraceMask`-based).
- All `Utils.LogDebug` / `LogTrace` / `LogInformation` / `LogWarning`
  / `LogError` / `LogCritical` overloads (including the
  `EventId`-bearing variants).
- All `Utils.Log(LogLevel, ...)` and `Utils.Log(int traceMask, ...)`
  generic overloads.

Replace each with the equivalent `ILogger.LogXxx` call on a logger
obtained from `ITelemetryContext.CreateLogger<T>()`.

#### Migration utilities

To aid migration the stack provides:

- `DefaultTelemetry.Create(...)` &mdash; convenient factory that
  returns an `ITelemetryContext` backed by the trace logger when no
  configuration is supplied.
- `Telemetry.NullLogger` / `Telemetry.NullLogger<T>` &mdash;
  no-op-in-release / debug-check-in-debug logger you can assign to
  an `m_logger` field to avoid null-reference exceptions while a
  class is being migrated. Distinct from
  `NullLogger.Instance` in `Microsoft.Extensions.Logging.Abstractions`.
- `Utils.Fallback.Logger` &mdash; an `ILogger` that mimics the
  legacy static `ILogger`. Use as a strictly temporary placeholder
  in places where no telemetry context can yet be plumbed through.
  The `Fallback` class is marked `Experimental` and may be removed
  in a future release; treat any usage as a TODO.

In debug builds, any code path that resolves a null telemetry
context triggers a `Debug.Fail`, surfacing the missing wiring at
test time.


## Migrating from 1.04 to 1.05

A few features are still missing to fully comply with 1.05, but
certification for v1.04 is still possible with the 1.05 release.

## Support

For additional migration support:

- Review sample applications in the repository.
- Check unit tests for usage patterns.
- Use the
  [`OPCFoundation.NetStandard.Opc.Ua.MigrationAnalyzer`](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.MigrationAnalyzer)
  package — analyzer rules `UA0001`-`UA0020` map to the patterns in
  [`migrate/2.0.x/types.md`](migrate/2.0.x/types.md) and apply most
  edits via a code-fixer.
- Open an issue on
  [OPCFoundation/UA-.NETStandard](https://github.com/OPCFoundation/UA-.NETStandard/issues).
