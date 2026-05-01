# Sessions, Reconnection, and Subscription Engines

This document describes how the OPC UA client stack composes the building
blocks that turn a configured endpoint into a running, fault-tolerant OPC
UA session: the raw `Session` and its reconnect helper, the
`ManagedSession` facade, the pluggable subscription engines, and how to
choose between them.

## Quick reference

| Type | Creates | Reconnect | Subscription engine | Recommended for |
|---|---|---|---|---|
| `Session` | itself (via constructor or `Session.Create...Async`) | none — caller drives via `Session.ReconnectAsync` / `SessionReconnectHandler` | `ClassicSubscriptionEngine` by default; pass `ISubscriptionEngineFactory` to opt in to V2 | callers that own session lifecycle and existing reconnect code |
| `DefaultSessionFactory` | raw `Session` | none — caller drives reconnect | configurable via `SubscriptionEngineFactory` init property | drop-in `ISessionFactory` for the legacy flow |
| `SessionReconnectHandler` | nothing (drives an existing session) | yes, against a `Session` (only) | inherited from the wrapped `Session` | legacy callers that already own a `Session` |
| `ManagedSession` | itself (via `CreateAsync`) | yes — built-in `ConnectionStateMachine` + `ReconnectPolicy` | `DefaultSubscriptionEngine` (V2) by default | new code; long-lived clients that need reconnect / failover |
| `ManagedSessionFactory` | `ManagedSession` | inherited from `ManagedSession` | inherited from `ManagedSession` | drop-in `ISessionFactory` that yields managed sessions |
| `ManagedSessionBuilder` | `ManagedSession` | inherited from `ManagedSession` | configurable via `UseSubscriptionEngine` | fluent / DI scenarios |

Everything below explains how these pieces fit together.

## 1. `Session` — the OPC UA session primitive

`Session` (`Libraries/Opc.Ua.Client/Session/Session.cs`) is the lowest-level
client object that maps directly to a UA secure-channel + session pair on
the server. It implements `ISession` and exposes the full surface of the
OPC UA service set (Read, Write, Browse, Call, AddNodes, etc.) plus
session-level concerns: keep-alive, namespace tables, the type tree, the
node cache, and a publish pipeline.

A `Session` is bound to:

- An `ApplicationConfiguration` (security policies, certificates, transport
  quotas, telemetry).
- A `ConfiguredEndpoint` (URL, security mode, security policy URI).
- An `ITransportChannel` created against that endpoint.

Lifecycle transitions on a `Session` are explicit and synchronous-looking
from the caller's perspective:

- `Session.ReconnectAsync()` re-uses the existing channel where possible
  and re-establishes the activated session.
- `ISessionFactory.RecreateAsync(...)` builds a fresh `Session` that
  inherits the previous session's identity, subscriptions, and node cache,
  swapping in a new channel.

`Session` does **not** run a background reconnect state machine. If the
keep-alive callback raises a bad status, the caller decides whether to
reconnect, recreate, or abandon the session.

### Plugging in a subscription engine

`Session` accepts an optional `ISubscriptionEngineFactory` at
construction. The factory creates an `ISubscriptionEngine` that owns the
publish loop and the live subscription set. Two implementations ship in
the box:

- **`ClassicSubscriptionEngineFactory.Instance`** (default when no
  factory is supplied) — the historical fire-and-forget publish engine
  that drives `Subscription` and `MonitoredItem` directly from
  `Session`.
- **`DefaultSubscriptionEngineFactory.Instance`** — wraps the new
  options-based `SubscriptionManager` (the V2 subscription pipeline)
  inside an `ISubscriptionEngine` adapter so even a raw `Session` can
  expose the `ISubscriptionManager` API.

The engine is selected once, at construction. It is then reused across
reconnect / recreate cycles. See section 4 for the engine architecture.

### `DefaultSessionFactory`

`DefaultSessionFactory` (`Libraries/Opc.Ua.Client/Session/DefaultSessionFactory.cs`)
is the drop-in `ISessionFactory` that builds raw `Session` instances. It
exposes a single tunable knob beyond `ITelemetryContext`:

```csharp
var factory = new DefaultSessionFactory(telemetry)
{
    SubscriptionEngineFactory = DefaultSubscriptionEngineFactory.Instance, // optional; null => classic
    ReturnDiagnostics = DiagnosticsMasks.SymbolicId,
};
ISession session = await factory.CreateAsync(...);
```

When `SubscriptionEngineFactory` is `null`, `Session` falls back to
`ClassicSubscriptionEngineFactory` — preserving the historical default.

## 2. `SessionReconnectHandler` — legacy reconnect driver

`SessionReconnectHandler` (`Libraries/Opc.Ua.Client/Session/SessionReconnectHandler.cs`)
is the original reconnect helper that ships with `Session`. It implements
the timer-driven retry loop, picks between
`Session.ReconnectAsync` and `ISessionFactory.RecreateAsync`, handles
endpoint refreshes when the server certificate changes, and cooperates
with `ReverseConnectManager`.

```csharp
using var handler = new SessionReconnectHandler(
    telemetry,
    reconnectAbort: true,
    maxReconnectPeriod: 30000);

session.KeepAlive += (_, e) =>
{
    if (e.Status != null && ServiceResult.IsNotGood(e.Status))
    {
        handler.BeginReconnect(session, 1000, OnReconnectComplete);
    }
};
```

### Supported session types

`SessionReconnectHandler` drives `Session.ReconnectAsync` and
`ISessionFactory.RecreateAsync` directly, and replaces the channel on the
underlying object. It therefore only supports the concrete `Session`
class (or types derived from it). Passing any other `ISession`
implementation — most notably `ManagedSession` — throws
`NotSupportedException`, because those facades already run their own
reconnect state machine and exposing two reconnect drivers on the same
session would race.

```csharp
// OK: raw Session
ISession raw = await new DefaultSessionFactory(telemetry).CreateAsync(...);
handler.BeginReconnect(raw, 1000, callback);

// Throws NotSupportedException
ISession managed = await new ManagedSessionFactory(telemetry).CreateAsync(...);
handler.BeginReconnect(managed, 1000, callback);
```

`SessionReconnectHandler` is **not** marked `[Obsolete]` — it is a
supported entry point for callers that already own raw `Session`
instances. New code should still prefer `ManagedSession`.

## 3. `ManagedSession` — the connection-state-machine facade

`ManagedSession` (`Libraries/Opc.Ua.Client/Session/ManagedSession.cs`) is
an `ISession` facade that wraps a raw `Session` and adds:

- A **connection state machine** (`ConnectionStateMachine`) that owns
  transitions between `Disconnected`, `Connecting`, `Connected`,
  `Reconnecting`, `Closing`, `Closed`.
- A **reconnect policy** (`ReconnectPolicy` / `ReconnectPolicyOptions`)
  with pluggable backoff strategies (`Exponential`, `Linear`,
  `Constant`), jitter, max-retry caps, and bounded delays.
- A **server-redundancy handler** (`IServerRedundancyHandler`,
  default: `DefaultServerRedundancyHandler`) that reads the server's
  `ServerRedundancy` object and can fail over to a backup endpoint.
- A **service gate** (`m_serviceLock`) that pauses caller-issued service
  calls (Read/Write/Browse/Call/...) for the duration of a reconnect or
  failover, so consumers see one transparent retry rather than a torn
  state.
- A **default V2 subscription engine** so the new options-based
  subscription API (`ISubscriptionManager`) is available on
  `ManagedSession.SubscriptionManager` out of the box.

```csharp
ManagedSession session = await ManagedSession.CreateAsync(
    configuration,
    endpoint,
    sessionFactory: new DefaultSessionFactory(telemetry),
    reconnectPolicy: new ReconnectPolicy(new ReconnectPolicyOptions
    {
        Strategy = BackoffStrategy.Exponential,
        InitialDelay = TimeSpan.FromSeconds(1),
        MaxDelay = TimeSpan.FromSeconds(30),
        MaxRetries = 0, // unlimited
        JitterFactor = 0.1
    }),
    redundancyHandler: new DefaultServerRedundancyHandler(),
    telemetry: telemetry);
```

### `ManagedSessionFactory`

`ManagedSessionFactory` (`Libraries/Opc.Ua.Client/Session/ManagedSessionFactory.cs`)
is the `ISessionFactory` that produces `ManagedSession` (cast as
`ISession` to satisfy the interface). Internally it composes a
`DefaultSessionFactory` to build the raw inner `Session`, then wraps it.
The factory is the drop-in replacement for code that currently calls
`DefaultSessionFactory.CreateAsync(...)` and wants managed reconnect for
free:

```csharp
ISessionFactory factory = new ManagedSessionFactory(telemetry);
ISession session = await factory.CreateAsync(...); // actually a ManagedSession
```

> **Naming note.** `ManagedSessionFactory` creates `ManagedSession`
> instances. `DefaultSessionFactory` creates raw `Session` instances. The
> two factories are siblings under `ISessionFactory`; `ManagedSessionFactory`
> uses an inner `DefaultSessionFactory` only as an implementation detail
> for building the underlying transport+session pair.

### `ManagedSessionBuilder`

`ManagedSessionBuilder` (`Libraries/Opc.Ua.Client/ClientBuilder/ManagedSessionBuilder.cs`)
is the fluent / DI-friendly entry point. `Build()` returns an immutable
`ManagedSessionOptions` snapshot; `ConnectAsync()` wraps `Build()` plus
`ManagedSession.CreateAsync(...)`:

```csharp
ManagedSession session = await new ManagedSessionBuilder(configuration, telemetry)
    .UseEndpoint(endpoint)
    .WithSessionName("MyClient")
    .WithSessionTimeout(TimeSpan.FromSeconds(60))
    .WithReconnectPolicy(p => p with
    {
        Strategy = BackoffStrategy.Exponential,
        InitialDelay = TimeSpan.FromSeconds(1),
        MaxDelay = TimeSpan.FromSeconds(30)
    })
    .WithServerRedundancy()
    .UseSubscriptionEngine(DefaultSubscriptionEngineFactory.Instance) // optional; V2 by default
    .ConnectAsync(ct);
```

The builder is also the integration point for
`OpcUaClientServiceCollectionExtensions.AddOpcUaClient` — DI consumers
inject a `Func<CancellationToken, Task<ManagedSession>>` that lazily
connects on first use and caches the resulting session.

### Reconnect semantics on `ManagedSession`

When the underlying session's keep-alive fires a bad status, the
connection state machine transitions `Connected → Reconnecting` and:

1. Pauses the service gate so all in-flight and incoming service calls
   wait.
2. Drives the configured `ReconnectPolicy` to compute the next delay
   (with jitter), bounded by `MinDelay` / `MaxDelay` and capped by
   `MaxRetries` (where `0` means unlimited).
3. Calls `Session.ReconnectAsync` (channel reuse path) and on failure
   `ISessionFactory.RecreateAsync` (channel rebuild path), updating the
   inner session reference atomically.
4. If the redundancy handler is enabled and the active server is
   unreachable, it consults
   `IServerRedundancyHandler.GetNextEndpointAsync` and recreates the
   session against the failover endpoint.
5. On success, transitions `Reconnecting → Connected` and releases the
   service gate, replaying any deferred calls.
6. On exhaustion (`MaxRetries` reached), transitions to `Closed` and
   surfaces a `ServiceResultException` to outstanding callers.

Because all of this is driven internally, callers must **not** wrap a
`ManagedSession` with `SessionReconnectHandler`; doing so throws
`NotSupportedException`.

## 4. Subscription engines

The `ISubscriptionEngine` abstraction
(`Libraries/Opc.Ua.Client/Session/ISubscriptionEngine.cs`) decouples the
publish pipeline from `Session`. Each `Session` owns exactly one engine
chosen at construction; the engine manages the publish workers, ack
queues, keep-alive timers, and dispatch to subscriptions.

### `ClassicSubscriptionEngine`

The historical engine. Owns:

- The `Subscription` and `MonitoredItem` classes in
  `Libraries/Opc.Ua.Client/Subscription/Classic`.
- A fire-and-forget publish loop driven from
  `Session.BeginPublish` / `OnPublishComplete`.
- The `Subscriptions` collection on `ISession` and `ManagedSession`.

This is the default when no `SubscriptionEngineFactory` is supplied to
`Session` directly. It is also what `ManagedSession` exposes via the
classic `Subscriptions` property regardless of which engine is active —
an internal `SubscriptionBridge` forwards classic subscriptions onto the
V2 engine when one is in use, so existing classic-API code keeps
working.

### `DefaultSubscriptionEngine` (V2)

The new engine wraps the options-based `SubscriptionManager`
(`Libraries/Opc.Ua.Client/Subscription/SubscriptionManager.cs`) inside
the `ISubscriptionEngine` contract. It owns:

- The `Subscriptions.Subscription` / `Subscriptions.MonitoredItems.MonitoredItem`
  records consumed via `IOptionsMonitor<T>`.
- A worker-pool publish loop with adaptive worker count.
- The full subscription-service-set lifecycle: `CreateSubscription`,
  `ModifySubscription`, `SetPublishingMode`, `Republish`, plus
  `CreateMonitoredItems`, `ModifyMonitoredItems`, `DeleteMonitoredItems`,
  `SetMonitoringMode`, `Call`, `SetTriggering` — all routed back through
  the `Session`'s service-set client methods.
- An `ISubscriptionNotificationHandler` callback contract for
  `OnDataChangeNotificationAsync` / `OnEventDataNotificationAsync` /
  `OnKeepAliveNotificationAsync`.

`DefaultSubscriptionEngine.SubscriptionManager` exposes the
`ISubscriptionManager` API; on `ManagedSession` it surfaces directly as
`SubscriptionManager`.

```csharp
// V2 engine is the default for ManagedSession
ManagedSession session = await new ManagedSessionBuilder(configuration, telemetry)
    .UseEndpoint(endpoint)
    .ConnectAsync(ct);

var handler = new MyNotificationHandler(); // : ISubscriptionNotificationHandler
ISubscription subscription = session.AddSubscription(handler,
    new Opc.Ua.Client.Subscriptions.SubscriptionOptions
    {
        PublishingInterval = TimeSpan.FromMilliseconds(500),
        KeepAliveCount = 10,
        LifetimeCount = 100
    });

subscription.TryAddMonitoredItem(
    "ServerStatus_CurrentTime",
    VariableIds.Server_ServerStatus_CurrentTime,
    o => o with
    {
        SamplingInterval = TimeSpan.FromMilliseconds(250),
        QueueSize = 10
    },
    out IMonitoredItem _);
```

### Choosing an engine

| Scenario | Engine |
|---|---|
| Existing code using `session.AddSubscription(new Subscription(...))` and the classic event-driven API | `ClassicSubscriptionEngine` (the default for raw `Session`) |
| New code, options-based subscriptions, DI / Microsoft.Extensions.Options friendly | `DefaultSubscriptionEngine` (the default for `ManagedSession`) |
| Migrating a managed session but you still need a classic subscription you cannot rewrite | `ManagedSession` with `UseSubscriptionEngine(ClassicSubscriptionEngineFactory.Instance)`; `SubscriptionManager` then throws `InvalidOperationException` and you keep using `Subscriptions` |

The two engines can co-exist on a `ManagedSession`:
`ManagedSession.Subscriptions` (classic API) keeps working alongside
`ManagedSession.SubscriptionManager` (V2 API) when the V2 engine is
active. Internally, classic subscriptions are bridged onto the V2
publish pipeline.

## 5. Putting it all together

Pick the entry point that best matches your call site:

- **Already have a `Session` and an existing reconnect helper.** Keep
  `Session` + `SessionReconnectHandler`. They are first-class APIs in
  1.6 and not deprecated. You may opt into the V2 subscription engine
  by passing
  `SubscriptionEngineFactory = DefaultSubscriptionEngineFactory.Instance`
  to `DefaultSessionFactory`.
- **New service / ASP.NET Core / generic-host application.** Use
  `AddOpcUaClient` and resolve `Func<CancellationToken, Task<ManagedSession>>`
  from DI.
- **New code without DI.** Use `ManagedSessionBuilder.ConnectAsync(...)`.
- **Drop-in replacement for `DefaultSessionFactory` that wants managed
  reconnect.** Switch to `ManagedSessionFactory` — the public surface
  (`ISessionFactory`) is identical.
- **Need to share a single underlying `Session` across two reconnect
  drivers, or wrap a `ManagedSession` with `SessionReconnectHandler`.**
  Don't. The reconnect drivers race, and `SessionReconnectHandler` will
  throw `NotSupportedException` to enforce this.

## See also

- [Migration Guide — ManagedSession and Automatic Reconnection](MigrationGuide.md#managedsession-and-automatic-reconnection)
- [Migration Guide — Fluent Builder, V2 Subscriptions, and DI](MigrationGuide.md#fluent-builder-v2-subscriptions-and-dependency-injection)
- [TransferSubscription](TransferSubscription.md) — server-driven session-handoff support.
- [Observability](Observability.md) — telemetry plumbed through `ITelemetryContext` on every factory and session type.
- [Reverse Connect](ReverseConnect.md) — works with both `Session` and `ManagedSession`.
