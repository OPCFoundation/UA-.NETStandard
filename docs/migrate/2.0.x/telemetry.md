# Telemetry and Logging

> **When to read this:** Read this when migrating logger use (`Utils.LogX`, `Utils.Trace`, static log helpers) to the new `ITelemetryContext` model that resolves `ILogger` via `telemetry.CreateLogger<T>()`.

Telemetry enters each API at a different point:

| Type | Telemetry parameter | Notes |
|---|---|---|
| `ApplicationInstance(ITelemetryContext? telemetry)` | Required argument, nullable value | Also `ApplicationInstance(ApplicationConfiguration, ITelemetryContext?)`. Passing `null` uses a null logger for the instance. Constructors without a telemetry parameter remain available but are `[Obsolete]`. |
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

## Contents

- [Replacing the static logger surface](#replacing-the-static-logger-surface)
  - [From static logger management](#from-static-logger-management)
  - [From static `Utils.Log*` methods](#from-static-utilslog-methods)
  - [From `Utils.Trace*` methods](#from-utilstrace-methods)
- [Telemetry constructors and related migrations](#telemetry-constructors-and-related-migrations)
  - [Removed and `[Obsolete]` `Utils` static APIs](#removed-and-obsolete-utils-static-apis)
- [ETW `EventSource` provider removal](#etw-eventsource-provider-removal)
  - [`ClientTraceFlags.EventLog` removed (source breaking)](#clienttraceflagseventlog-removed-source-breaking)
  - [Migrating `EventListener` / `dotnet-trace` consumers](#migrating-eventlistener--dotnet-trace-consumers)
- [Sensitive log data is classified](#sensitive-log-data-is-classified)
- [Migration utilities](#migration-utilities)

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

For dependency-injection hosts, register the OPC UA fluent surface via
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
[`docs/Diagnostics.md`](../../Diagnostics.md#wiring-into-microsoftextensionsdependencyinjection)
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

## Telemetry constructors and related migrations

Update these constructor and method calls when migrating telemetry:

- Replace `SystemContext()` with `SystemContext(ITelemetryContext)` or
  `SystemContext(IOperationContext, ITelemetryContext)`.
- Pass telemetry to `DirectoryCertificateStore(ITelemetryContext)` and
  `TcpTransportChannel(ITelemetryContext)`; their parameterless constructors
  have been removed.
- Where an `ICertificateStore` is required, replace the old
  `CertificateIdentifierCollection` store usage with
  `CertificateIdentifierCollectionStore(ITelemetryContext)`.
- Prefer `ApplicationConfiguration(ITelemetryContext)` to the parameterless
  constructor, which remains available.
- Pass telemetry to `ConfiguredEndpointCollection.Load(..., ITelemetryContext)`.
- Use `ServerSecurityPolicy.CalculateSecurityLevel(mode, policyUri, ILogger)`
  (or the corresponding `SecuredApplication` overload); the overload without
  a logger is obsolete.
- Replace obsolete `ServiceMessageContext()` and
  `ServiceMessageContext(ITelemetryContext)` calls with
  `ServiceMessageContext.Create(ITelemetryContext)`.

See the topic guides for related API migrations:

| Area | Current migration guidance |
|---|---|
| Types and encoding | [Built-in types](types.md), [Encoders and complex types](encoders.md) |
| Certificates and security | [Certificates](certificates.md), [Identity and secrets](identity.md) |
| Application configuration | [Configuration](configuration.md) |
| Client sessions and transports | [Sessions and subscriptions](sessions-subscriptions.md), [Async transport listeners](transport-listener-async.md) |
| Server node managers and state | [Node states](node-states.md) |
| PubSub | [PubSub](pubsub.md) |
| Telemetry and logging APIs | [Diagnostics](../../Diagnostics.md), [Source-generated logging recipe](../../DeveloperGuide.md#add-a-log-message-source-generated) |

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

While migrating explicit-mask calls, their original masks remain available to
legacy `Tracing.TraceEventHandler` subscribers, including combined categories.
Do not copy a numeric trace mask into an ordinary source-generated `EventId`:
event identifiers are classified by log level unless an explicitly named legacy
category is selected. See [Diagnostics](../../Diagnostics.md#overview).

---

## ETW `EventSource` provider removal

> **When to read this:** Read this if your application or tooling attaches an `EventListener`, `dotnet-trace`, PerfView, or any other ETW consumer to one of the stack's `System.Diagnostics.Tracing.EventSource` providers, or if it sets `ClientTraceFlags.EventLog` on a client.

The stack shipped four internal `EventSource` providers for high-performance tracing. All four are **removed** in 2.0; there is no compile-time or runtime fallback. Their events are replaced one-for-one by `[LoggerMessage]`-generated `ILogger` calls that preserve the original numeric `EventId`, `EventId.Name`, mapped level, message template, and structured fields, so the *event identity* survives even though the transport changes from ETW to `Microsoft.Extensions.Logging` / OpenTelemetry logging.

| Removed provider (ETW name) | Assembly | Replacement `ILogger` category |
|---|---|---|
| `OPC-UA-Core` | `Opc.Ua.Core` | `OPC-UA-Core` |
| `OPC-UA-Client` | `Opc.Ua.Client` | `OPC-UA-Client` |
| `OPC-UA-Server` | `Opc.Ua.Server` | `OPC-UA-Server` |
| `Opc.Ua.ChannelManager` | `Opc.Ua.Core` (client channel manager) | `Opc.Ua.ChannelManager` |

The replacement logger category is always the **exact old ETW provider name**, not the assembly's usual typed category. Migrated filters can therefore keep the same identifying string (for example, `AddFilter("OPC-UA-Client", LogLevel.Trace)`) after they move from an ETW provider subscription to `ILogger` configuration. `EventLevel` mapped to `LogLevel` on a like-for-like basis (`Verbose` → `Trace`, `Informational` → `Information`, `Warning` → `Warning`, `Error`/`Critical` → `Error`/`Critical`). See [DeveloperGuide.md — narrow exception: retained EventSource-compatibility ids](../../DeveloperGuide.md#narrow-exception-retained-eventsource-compatibility-ids) for the authoring-side rules and [Sessions.md — diagnostics surface contract](../../Sessions.md#diagnostics-surface-contract--what-tags-and-structured-log-fields-carry) for the full `Opc.Ua.ChannelManager` event table.

The `OPC-UA-Server` compatibility category remains opt-in like the retired EventSource provider. Enable that category at `Trace` to receive its records, including the `ServerCall` and `SessionState` records whose retained legacy level is `Information`. A global `Information` minimum therefore does not emit a record for every server request.

**Not retained:** the ETW provider GUID, `EventTask`/`EventKeywords` definitions, and the ETW manifest. There is no `ILogger` equivalent for these, and providers/consumers that depended on them (raw ETW session subscribers keyed by provider GUID, manifest-based decoders) must move to `Microsoft.Extensions.Logging` category/event-name filtering instead.

### `ClientTraceFlags.EventLog` removed (source breaking)

`ClientTraceFlags.EventLog` — the flag that routed `ClientBase` request/response tracing to the (now-removed) `OPC-UA-Core` `EventSource` — is **removed**. Code that references it fails to compile:

```csharp
// OLD - no longer compiles (ClientTraceFlags.EventLog removed)
client.ActivityTraceFlags = ClientTraceFlags.Log | ClientTraceFlags.EventLog;

// NEW - ClientTraceFlags.Log alone now emits the canonical OPC-UA-Core
// compatibility events (same EventId / EventName / message as the old
// EventLog flag); the separate per-call ClientBase structured logs that
// ClientTraceFlags.Log used to emit are replaced by these events
client.ActivityTraceFlags = ClientTraceFlags.Log;
```

`ClientTraceFlags.Log` now emits the canonical `OPC-UA-Core` request events (`ServiceCallStart` / `ServiceCallStop` / `ServiceCallBadStop`) through the `OPC-UA-Core` logger category, so existing filters or dashboards keyed on those event names keep matching without further changes. The separate `SendResponse` compatibility event is emitted by `TcpServerChannel` under the same category and is not controlled by `ClientTraceFlags`. There is no separate flag for the compatibility events; remove any reference to `ClientTraceFlags.EventLog` from your code.

This consolidation also adopts the legacy event levels and fields: request start/stop move from `Information` to `Trace`, failure moves from `Error` to `Warning`, and the former per-call elapsed-time field is no longer part of these log records (request duration remains available through `opc.ua.client.request.duration`). Configure the `OPC-UA-Core` category at `Trace` if you need successful request start/stop records.

### Migrating `EventListener` / `dotnet-trace` consumers

If your code or tooling previously attached to one of the four providers, replace it with the `Microsoft.Extensions.Logging` / OpenTelemetry logging equivalent:

```csharp
// OLD - ETW EventListener attached to a stack provider (removed in 2.0)
public sealed class ClientEventListener : EventListener
{
    protected override void OnEventSourceCreated(EventSource source)
    {
        if (source.Name == "OPC-UA-Client")
        {
            EnableEvents(source, EventLevel.Verbose);
        }
    }

    protected override void OnEventWritten(EventWrittenEventArgs e)
    {
        Console.WriteLine($"{e.EventName}: {string.Join(", ", e.Payload ?? [])}");
    }
}

// NEW - ILoggerProvider filtered to the same category name
builder.Services.AddLogging(b => b
    .AddFilter("OPC-UA-Client", LogLevel.Trace)
    .AddConsole());

// NEW - OpenTelemetry Logs SDK, same category
builder.Services.AddOpenTelemetry().WithLogging(l => l
    .AddProcessor(/* your exporter of choice */));
```

`dotnet-trace` / PerfView users who captured `OPC-UA-Core`, `OPC-UA-Client`, `OPC-UA-Server`, or `Opc.Ua.ChannelManager` as ETW providers should instead configure an `ILoggerProvider` (Console, OpenTelemetry OTLP, Application Insights, etc.) filtered to the matching category name, or use the [`Microsoft-Extensions-Logging` ETW provider](https://learn.microsoft.com/dotnet/core/diagnostics/logging-tracing) that `dotnet-trace` already understands for standard `ILogger` output, if ETW-shaped capture is still required.

> **Out of scope.** The OPC UA information-model `EventSourceRegistry` / `HasEventSource` reference type (`Opc.Ua.Server.Fluent`, used for Alarms & Conditions modelling) and the BCL's own `System.Buffers.ArrayPoolEventSource` are unrelated to this removal — neither is a stack-owned ETW provider, and neither is affected by this migration.

---

## Sensitive log data is classified

The stack's `[LoggerMessage]` parameters that hold network addresses,
application or user identities, file paths or exception texts carry the data
classification attributes of `Opc.Ua.Redaction` (see
[Diagnostics: Sensitive data in logs](../../Diagnostics.md#sensitive-data-in-logs-data-classification)).

- **Behavior breaking:** a plain `ILoggerFactory` logs `(null)` in place of
  classified values. `AddOpcUa().AddLogging(...)` and
  `DefaultTelemetry.Create(...)` enable the redaction-aware logger
  themselves. If you configure logging yourself (generic host,
  `services.AddLogging(...)`, your own `ILoggerFactory`), call
  `EnableOpcUaRedaction()` (or `EnableRedaction()`) on its
  `ILoggingBuilder`, e.g. `builder.Logging.EnableOpcUaRedaction()`.
- **Source breaking:** `Redact`, `RedactionWrapper<T>`,
  `IRedactionStrategy`, `RedactionStrategies` and `SimpleRedactionStrategy`
  were removed. Replace `RedactionStrategies.SetStrategy(...)` with
  `services.AddRedaction(...)` redactors per `OpcUaDataClassifications`
  value.
- Log messages that used to pass a redacted exception as a message
  parameter now pass the exception message as a `[SensitiveText]` string.

## Migration utilities

To aid migration the stack provides:

- `DefaultTelemetry.Create(...)` — convenient factory that
  returns an `ITelemetryContext` backed by the trace logger when no
  configuration is supplied.
- `Telemetry.NullLogger` / `Telemetry.NullLogger<T>` —
  no-op-in-release / debug-check-in-debug logger you can assign to
  an `m_logger` field to avoid null-reference exceptions while a
  class is being migrated. Distinct from
  `NullLogger.Instance` in `Microsoft.Extensions.Logging.Abstractions`.
- `Utils.Fallback.Logger` — an `ILogger` that mimics the
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
- [`docs/Diagnostics.md`](../../Diagnostics.md) — full
  end-state usage and extensibility guidance for `ITelemetryContext`
  (custom contexts, OpenTelemetry wiring, metrics inventory).
- [`docs/Sessions.md` — diagnostics surface contract](../../Sessions.md#diagnostics-surface-contract--what-tags-and-structured-log-fields-carry) — full `Opc.Ua.ChannelManager` compatibility event table and safe-field policy.
- [`docs/DeveloperGuide.md` — narrow exception: retained EventSource-compatibility ids](../../DeveloperGuide.md#narrow-exception-retained-eventsource-compatibility-ids) — authoring rules for the retained compatibility ids.
- [2.0 migration index](README.md) — analyzer quick-start + symptom → sub-doc table.
- [Migration Guide](../../MigrationGuide.md) — landing page across versions.
