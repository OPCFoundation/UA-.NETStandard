# Telemetry and Logging

> **When to read this:** Read this when migrating logger use (`Utils.LogX`, `Utils.Trace`, static log helpers) to the new `ITelemetryContext` model that resolves `ILogger` via `telemetry.CreateLogger<T>()`.

Observability in 2.0 is plumbed through `ITelemetryContext`. Loggers are resolved from the telemetry context via `telemetry.CreateLogger<T>()` rather than from `Utils.Trace` / `Utils.LogX`. The static logging helpers remain compilable but are `[Obsolete]`; consumers should resolve `ILogger` from `ITelemetryContext` instead.

Constructor injection across the public API is not uniform - the parameter is required positionally on most types, optional on `ApplicationInstance`, and absent on `Session` / `CustomNodeManager2`. The table below summarises the precise shape per type:

| Type | Telemetry parameter | Notes |
|---|---|---|
| `ApplicationInstance(ITelemetryContext? telemetry)` | Nullable | Also `ApplicationInstance(ApplicationConfiguration, ITelemetryContext?)`. Passing `null` falls back to a default telemetry context. |
| `ServerBase(ITelemetryContext telemetry)` | Required positional | The only public ctor. |
| `CertificateManagerFactory.Create(SecurityConfiguration, ITelemetryContext, Action<CertificateManagerOptions>?)` | Required positional (2nd parameter) | Factory entry point for `CertificateManager`. |
| `DefaultSessionFactory()` / `DefaultSessionFactory(ITelemetryContext telemetry)` | Both ctors exist | The parameterless ctor is `[Obsolete]`; use the telemetry-aware overload. |
| `ManagedSessionFactory(ITelemetryContext telemetry)` | Required positional | The only public ctor. |
| `Session` ctors | **None** | Telemetry flows in via `ApplicationConfiguration` or `ISubscriptionEngineFactory`. Do not look for a `Session(... ITelemetryContext)` overload - none exists. |
| `CustomNodeManager2(IServerInternal, ApplicationConfiguration?, bool, ILogger, params string[])` | **None directly** | Obtain a logger via `server.Telemetry.CreateLogger<T>()` and pass it to the ctor. |

```csharp
// Server side - log via the server's telemetry context
public sealed class MyNodeManager : CustomNodeManager2
{
    public MyNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        : base(server, configuration, useSamplingGroups: false,
               server.Telemetry.CreateLogger<MyNodeManager>(),
               "urn:example:my-namespace")
    {
    }
}

// Client side - construct the factory with telemetry
var factory = new ManagedSessionFactory(telemetry);
ISession session = await factory.CreateAsync(/* ... */);
```

---

## Replacing the static logger surface

The legacy static logger management (`Utils.SetLogger` /
`Utils.SetLogLevel`) is removed; ad-hoc static `Utils.LogX` /
`Utils.Trace` helpers are `[Obsolete]`. The replacements:

### From static logger management

```csharp
// OLD - No longer works (removed in 2.0)
Utils.SetLogger(myLogger);
Utils.SetLogLevel(LogLevel.Information);

// NEW - Use ITelemetryContext via DefaultTelemetry / ApplicationInstance
var telemetryContext = DefaultTelemetry.Create(builder => builder.AddConsole());
var applicationInstance = new ApplicationInstance(telemetryContext);
```

Even better, register the OPC UA fluent surface via
`Microsoft.Extensions.DependencyInjection`:

```csharp
HostApplicationBuilder builder = Host.CreateApplicationBuilder();
builder.Services
    .AddOpcUa()                              // registers ITelemetryContext
    .AddLogging(b => b.AddConsole())
    .AddMetrics();
```

`AddOpcUa()` registers a `ServiceProviderTelemetryContext` that
resolves the host's `ILoggerFactory` from DI on first use. See
[`Docs/Diagnostics.md`](../../Diagnostics.md#wiring-into-microsoftextensionsdependencyinjection)
for the full DI guidance.

### From static `Utils.Log*` methods

```csharp
// OLD - Obsolete
Utils.LogInformation("Connection established to {0}", endpoint);
Utils.LogError(exception, "Failed to connect to {0}", endpoint);

// NEW - Context-based logging
var logger = telemetryContext.CreateLogger<MyClass>();
logger.LogInformation("Connection established to {Endpoint}", endpoint);
logger.LogError(exception, "Failed to connect to {Endpoint}", endpoint);
```

### From `Utils.Trace*` methods

```csharp
// OLD - Obsolete
Utils.Trace("Processing request {0}", id);
Utils.Trace(TraceMasks.Information, "Request processed successfully");

// NEW - Structured logging with context
var logger = telemetryContext.CreateLogger<MyClass>();
logger.LogInformation("Processing request {RequestId}", id);
logger.LogInformation("Request processed successfully");
```

---

## Non-exhaustive summary of breaking changes

The telemetry-context rollout touched many core types. The list
below covers replaced constructors, modified factory methods, and
`[Obsolete]` shims grouped by layer. Most affected APIs are
internal to the stack; the most common external impact is that
**applications must create an `ApplicationInstance` with an
explicit telemetry context**.

### Core infrastructure constructors

- `ServiceMessageContext()` &rarr; `ServiceMessageContext(ITelemetryContext)`
  (parameter-less constructor removed).
- `EncodeableFactory()` &rarr; `EncodeableFactory(ITelemetryContext)`
  (parameter-less constructor removed).
- `ApplicationConfiguration` gains
  `ApplicationConfiguration(ITelemetryContext)`.
- `ContentFilter.Evaluate(...)` moved to a new `FilterEvaluator`
  class that requires telemetry context.

### Interface additions

- `IServiceMessageContext` gains
  `ITelemetryContext Telemetry { get; }`.
- `ITransportBindingFactory<T>.Create(...)` now takes
  `ITelemetryContext`.

### System context

- `SystemContext()` &rarr; `SystemContext(ITelemetryContext)` and
  `SystemContext(IOperationContext, ITelemetryContext)`
  (parameter-less constructor removed).

### Certificate and security

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

### Transport layer

- `TcpTransportChannel`, `TcpTransportListener`,
  `HttpsTransportListener`: parameter-less constructors removed;
  factory `Create(...)` methods now take an `ITelemetryContext`.
- `TransportBindings.GetChannel(uriScheme, telemetry)` /
  `GetListener(uriScheme, telemetry)`: telemetry parameter added.

### Client library

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

### Configuration library

- `ApplicationInstance(ITelemetryContext)` and
  `ApplicationInstance(ITelemetryContext, ApplicationConfiguration)`;
  non-telemetry constructors `[Obsolete]`.
- `ConfiguredEndpointCollection.Load(..., ITelemetryContext)`.
- `ConfiguredEndpoint.UpdateFromServerAsync(ITelemetryContext, ...)`.

### Server library

- `DataChangeMonitoredItemQueue`, `EventMonitoredItemQueue`,
  `MonitoredItemQueueFactory`: constructors now take
  `ITelemetryContext`.

### PubSub library

- `UaPubSubApplication.Create(IUaPubSubDataStore, ITelemetryContext)`.
- `UaPubSubConfigurator(ITelemetryContext)`.
- `UdpClientBroadcast(..., ITelemetryContext)` (internal).
- `UdpClientUnicast(..., ITelemetryContext)` (internal).

### Method signatures

- `UserIdentityToken.GetOrCreateCertificate(ITelemetryContext)`
  &mdash; certificate creation is no longer implicit on the
  `Certificate` getter; call this method explicitly when needed.
- `ServerSecurityPolicy.CalculateSecurityLevel(..., ILogger)`
  &mdash; the non-logger overload is `[Obsolete]`.

### Removed and `[Obsolete]` `Utils` static APIs

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

---

## Migration utilities

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

---

**See also**

- Related: [packages.md](packages.md), [configuration.md](configuration.md).
- [`Docs/Diagnostics.md`](../../Diagnostics.md) &mdash; full
  end-state usage and extensibility guidance for `ITelemetryContext`
  (custom contexts, OpenTelemetry wiring, metrics inventory).
- [2.0 migration index](README.md) &mdash; analyzer quick-start + symptom → sub-doc table.
- [Migration Guide](../../MigrationGuide.md) &mdash; landing page across versions.

