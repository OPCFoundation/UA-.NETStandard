# Sessions, GDS Client, and Subscriptions

> **When to read this:** Read this for the move from caller-driven `Session` reconnect to `ManagedSession`, the V2 subscription engine, the GDS-client `Task` -> `ValueTask` API modernisation, removed obsolete GDS APIs, durable subscriptions, PubSub changes, and reverse-connect tweaks.

## GDS Client API modernization

The `Opc.Ua.Gds.Client.Common` package has undergone a significant cleanup. Two breaking changes affect almost every consumer of the GDS / LDS / Server-Push client APIs.

### `Task` → `ValueTask` on GDS client interfaces

**Breaking Change**: All asynchronous methods on `IGlobalDiscoveryServerClient`, `ILocalDiscoveryServerClient`, and `IServerPushConfigurationClient` (and their concrete implementations) now return `ValueTask` / `ValueTask<T>` instead of `Task` / `Task<T>`.

**Rationale**: Many GDS operations complete synchronously when a session is already established. Returning `ValueTask` avoids the per-call `Task` allocation on those fast paths and keeps the surface consistent with the rest of the modernized client stack.

**Impact**: Pure `await` callers require **no change** — `await` works identically on `Task` and `ValueTask`. However, two patterns require a small adjustment.

| Pattern | Old (`Task`) | New (`ValueTask`) |
|---|---|---|
| `await` on the return value | works | works (no change) |
| Block synchronously via `.Result` / `.Wait()` | works | use `.AsTask().Result` / `.AsTask().Wait()` |
| Combine results with `Task.WhenAll` / `Task.WhenAny` | works | call `.AsTask()` first |
| Await the same return value more than once | works | **not supported** — call `.AsTask()` first |

> **Important**: A `ValueTask` may be awaited only once and the underlying value source must not be observed after the operation has completed. If you need to await a result more than once, fan it out across multiple consumers, or pass it to anything other than a single `await`, materialize it via `.AsTask()` first.

```csharp
// Before
Task<NodeId> registration = gds.RegisterApplicationAsync(application, ct);
NodeId id = await registration;
await Task.WhenAll(registration, otherTask);          // worked

// After
ValueTask<NodeId> registration = gds.RegisterApplicationAsync(application, ct);
NodeId id = await registration;                       // unchanged

// Multi-await / Task.WhenAll: materialize first
Task<NodeId> asTask = gds.RegisterApplicationAsync(application, ct).AsTask();
await Task.WhenAll(asTask, otherTask);
```

### Removal of obsolete GDS APIs

**Breaking Change**: All `[Obsolete]` synchronous wrappers, APM (`Begin*`/`End*`) methods, and other deprecated members have been removed from the GDS client surface.

**Affected APIs (non-exhaustive)**:

- All synchronous wrappers on `GlobalDiscoveryServerClient` (~25 methods such as `FindApplication`, `RegisterApplication`, `StartNewKeyPairRequest`, …) — use the corresponding `*Async` overload returning `ValueTask`/`ValueTask<T>`.
- All synchronous wrappers on `ServerPushConfigurationClient` (~14 methods such as `UpdateCertificate`, `ReadTrustList`, `ApplyChanges`, …) — use the `*Async` overload.
- APM (`Begin*` / `End*`) overloads on `LocalDiscoveryServerClient` (e.g. `BeginFindServers` / `EndFindServers`) — use the `*Async` overload.
- The capability identifier constants are now source-generated as `Opc.Ua.ServerCapability` (singular, e.g. `ServerCapability.GDS`, `ServerCapability.LDS`, `ServerCapability.DA`). The `[Obsolete] public const string` shims previously exposed on the value-type `ServerCapability` class (now `ServerCapabilityInfo` in `Opc.Ua.Gds.Client`) have been removed. The runtime `ServerCapabilities.csv` parsing path (which never actually loaded — the resource was not embedded) has been replaced by the generated dictionary `ServerCapability.All`. The instance enumerable previously named `ServerCapabilityCatalog` is now `Opc.Ua.Gds.Client.ServerCapabilities` and its `Find` returns `ServerCapabilityInfo`.
- `RegisteredApplication` is now a `sealed record`; the obsolete extension methods that wrapped its property access have been removed — use the record properties directly.
- `CertificateWrapper` is now `sealed` and no longer implements `IEncodeable`; remove any code that treated it as an encodeable.

**Migration**:

The `ServerCapability` identifiers are source-generated from `Tools/Opc.Ua.SourceGeneration.Core/Design/ServerCapabilities.csv`; each capability emits a `public const string` field. The instance type carrying `Id` / `Description` is `ServerCapabilityInfo`, and the registry exposing `IEnumerable<ServerCapabilityInfo>` plus `Find(string?) : ServerCapabilityInfo?` is the static `ServerCapabilities` class in `Opc.Ua.Gds.Client.Common`.

```csharp
// Before
var apps = gds.FindApplication(uri);                       // sync wrapper
var caps = ServerCapability.GlobalDiscoveryServer;         // obsolete shim

// After
var apps = await gds.FindApplicationAsync(uri, ct);
string id = ServerCapability.GDS;                          // const string "GDS"
ServerCapabilityInfo? info = ServerCapabilities.Find(id);  // null if not registered
```

If you currently rely on a `[Obsolete]` member, switch to the `Async` equivalent and apply the `ValueTask` migration notes above. If a particular API has no direct replacement, the migration is described inline in the XML doc comment of the replacement member.

## ManagedSession and Automatic Reconnection

Version 2.0 introduces `ManagedSession`, a wrapper around `Session` that automatically handles connection lifecycle including reconnection and server redundancy failover.

**Key Changes**:

- **`ManagedSessionFactory`** is a **new** factory that creates `ManagedSession` instances which handle reconnection and failover automatically. Use this when you want managed-session behavior.
- **`DefaultSessionFactory`** is **unchanged** — it continues to create raw `Session` instances. Existing code that constructs `DefaultSessionFactory` directly keeps the same behavior in 2.0.
- **`SessionReconnectHandler`** is **retained** as a supported legacy entry point for callers that already manage raw `Session` instances. The type itself is not removed. Its parameterless legacy constructor remains marked `[Obsolete("Use SessionReconnectHandler(ITelemetryContext, bool, int) instead.")]` in 2.0 (the same attribute was already present in 1.5.378); pass an `ITelemetryContext` to the new ctor when adopting it. It now also requires the wrapped `ISession` to be a `Session` (or a derived type) — passing a `ManagedSession` (or any other `ISession` facade) throws `NotSupportedException`, since those facades drive their own reconnect / failover state machine. New code should still prefer `ManagedSessionFactory` / `ManagedSession.CreateAsync`.

For a deeper architectural picture of how `Session`, `ManagedSession`, `SessionReconnectHandler`, and the subscription engines fit together, see [Sessions, Reconnection, and Subscription Engines](../../Sessions.md).

**Migration**:

**If you use `DefaultSessionFactory`:**
No code changes are required — `DefaultSessionFactory` still returns raw `Session`. To opt into automatic reconnection and redundancy failover, switch to `ManagedSessionFactory`:

```csharp
// Still supported in 2.0 — DefaultSessionFactory creates raw Session:
var defaultFactory = new DefaultSessionFactory(telemetry);
ISession rawSession = await defaultFactory.CreateAsync(...);

// Opt in to managed reconnect/failover — ManagedSessionFactory creates ManagedSession:
var managedFactory = new ManagedSessionFactory(telemetry);
ISession managedSession = await managedFactory.CreateAsync(...);
```

Both factories implement `ISessionFactory`. `ManagedSessionFactory` internally uses a `DefaultSessionFactory` to create the raw `Session` and then wraps it in a `ManagedSession`; the public surface is unchanged.

**If you use `SessionReconnectHandler`:**

`SessionReconnectHandler` continues to work in 2.0 against `Session` instances. The pattern below is unchanged, but the legacy parameterless ctor remains `[Obsolete]` - prefer the `(ITelemetryContext, bool, int)` overload:

```csharp
ISession session = await new DefaultSessionFactory(telemetry).CreateAsync(...);
using var reconnectHandler = new SessionReconnectHandler(telemetry);
session.KeepAlive += (s, e) =>
{
    if (e.Status != null && ServiceResult.IsNotGood(e.Status))
    {
        reconnectHandler.BeginReconnect(session, 1000, OnReconnectComplete);
    }
};
```

`SessionReconnectHandler.BeginReconnect` only supports the legacy `Session` class (or types derived from it). Passing a `ManagedSession` throws `NotSupportedException`. If you have already migrated to `ManagedSession`, **do not** wrap it with a `SessionReconnectHandler` — `ManagedSession` already runs its own reconnect state machine. Use the `StateChanged` event to observe transitions:

```csharp
ISession session = await ManagedSession.CreateAsync(
    configuration, endpoint,
    reconnectPolicy: new ReconnectPolicy
    {
        Strategy = BackoffStrategy.Exponential,
        InitialDelay = TimeSpan.FromSeconds(1),
        MaxDelay = TimeSpan.FromSeconds(30)
    });
// Reconnection is automatic — no manual handler needed
((ManagedSession)session).StateMachine.StateChanged += (s, e) =>
{
    Console.WriteLine($"Session state: {e.NewState}");
};
```

Or, equivalently, via the factory:

```csharp
var factory = new ManagedSessionFactory(telemetry);
ISession session = await factory.CreateAsync(...);
```

### Configuring Reconnection Policy

Two related types ship side-by-side and are not interchangeable. `ReconnectPolicyOptions` is a `public sealed record` with init-only properties - the DTO consumed by dependency injection / `ManagedSessionOptions`. `ReconnectPolicy` is a `public class` (implementing `IReconnectPolicy`) - the runtime policy passed to `ManagedSession.CreateAsync` and `SessionReconnectHandler`. Construct the runtime policy from the options snapshot with `new ReconnectPolicy(options)`; `ManagedSessionBuilder.ConnectAsync` performs this conversion internally.

```csharp
var policy = new ReconnectPolicy
{
    Strategy = BackoffStrategy.Exponential,  // or Linear, Constant
    InitialDelay = TimeSpan.FromSeconds(1),
    MaxDelay = TimeSpan.FromSeconds(30),
    MaxRetries = 0,         // 0 = unlimited
    JitterFactor = 0.1      // ±10% jitter
};
```

### Server Redundancy

`ManagedSession` automatically reads server redundancy information and can failover to backup servers:

```csharp
var session = await ManagedSession.CreateAsync(
    configuration, endpoint,
    redundancyHandler: new DefaultServerRedundancyHandler());
```

### Service Call Behavior During Reconnect

When the session is reconnecting, service calls (Read, Write, Browse, etc.) automatically wait until the session is reconnected. This is transparent to the caller — no special handling needed. If reconnection fails permanently, calls will throw `ServiceResultException`.

### Fluent Builder, V2 Subscriptions, and Dependency Injection

Version 2.0 introduces a fluent builder for `ManagedSession`, exposes the new options-based subscription API on the managed session, and adds Microsoft.Extensions.DependencyInjection integration for Azure / ASP.NET Core / generic-host scenarios.

**Fluent builder:**

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
    .ConnectAsync(ct);
```

`Build()` returns an immutable `ManagedSessionOptions` snapshot; `ConnectAsync()` wraps `Build()` and `ManagedSession.CreateAsync(...)` so most callers can use the builder directly.

**New subscription API on `ISession`:**

The V2 options-based subscription manager (`ISubscriptionManager`) is exposed on `ISession` through `bool TryGetSubscriptionManager(out ISubscriptionManager? manager)` — it returns `true` and the manager for V2-engine sessions (the default for `ManagedSession`) and `false` for classic-engine sessions. The classic `Subscriptions` property remains available alongside it. Use `UseSubscriptionEngine(ClassicSubscriptionEngineFactory.Instance)` on the builder if you need the legacy classic engine instead.

> **Source-breaking (2.0 preview):** the earlier throwing `ManagedSession.SubscriptionManager` property has been **removed** in favor of `ISession.TryGetSubscriptionManager`. Replace `var manager = session.SubscriptionManager;` (which threw `InvalidOperationException` on classic-engine sessions) with:
>
> ```csharp
> if (session.TryGetSubscriptionManager(out ISubscriptionManager? manager))
> {
>     // use manager (V2 engine)
> }
> ```
>
> The `session.AddSubscription(...)` fluent extensions are unchanged.

```csharp
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;

var handler = new MyNotificationHandler();   // : ISubscriptionNotificationHandler

ISubscription subscription = session.AddSubscription(handler,
    new SubscriptionOptions
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

The `SubscriptionOptions` and `MonitoredItemOptions` records used by this API live in `Opc.Ua.Client.Subscriptions` and `Opc.Ua.Client.Subscriptions.MonitoredItems`. They are distinct from the classic types of the same names in the `Opc.Ua.Client` namespace; use namespace aliases (or fully-qualified names) when both are visible in the same file. Both records ship in the same assembly (`Opc.Ua.Client.dll`), so a using-alias is sufficient - `extern alias` is **not** required:

```csharp
using ClassicSubscriptionOptions = Opc.Ua.Client.SubscriptionOptions;
using V2SubscriptionOptions      = Opc.Ua.Client.Subscriptions.SubscriptionOptions;
```

The classic `ManagedSession.Subscriptions` collection (V1 `Subscription` objects) remains supported. Mixing classic subscriptions with the V2 manager on the same session is allowed for the time being, but this will change in future releases; classic subscriptions still receive notifications via the internal `SubscriptionBridge` when the V2 engine is active.

**Opt-in V2 notification pooling (`WithPoolNotifications`):**

The V2 subscription engine supports activator-level pooling of decoded
notification payload instances (`DataChangeNotification`,
`MonitoredItemNotification`, `EventNotificationList`, `EventFieldList`) to
reduce GC pressure on high-throughput publish loops. Pooling is **opt-in**
and disabled by default. Enable it on the builder, in
`ManagedSessionOptions`, or directly on the V2 manager:

```csharp
ManagedSession session = await new ManagedSessionBuilder(configuration, telemetry)
    .UseEndpoint(endpoint)
    .WithPoolNotifications()        // opt in
    .ConnectAsync(ct);
```

When pooling is enabled, the V2 dispatcher walks each decoded notification
after the handler `await` completes and calls
`IPooledEncodeable.Reuse()` on every payload item, returning instances to
their static activator pools. The recorded benchmarks show ~315× fewer
allocations per `MonitoredItemNotification` and a corresponding drop in
gen-0 GC pressure
(see [`Docs/Benchmarks.md` → *Pooled encodeable*](../../Benchmarks.md#pooled-encodeable)).

**Handler contract change (only when `WithPoolNotifications` is enabled):**
Handlers must **not** retain references to notification objects past the
`await` of the dispatch call. The pool may re-rent those instances to the
next publish immediately after `Reuse()` runs. Handlers that need to keep
values must **copy** them out of the dispatched struct before returning.
The `DataValueChange` / `EventNotification` projection structs are
designed not to surface pooled instances directly — copy-by-value of the
struct itself is safe and is the recommended pattern. See
[`Docs/Sessions.md`](../../Sessions.md#v2-notification-pooling-opt-in) for full
detail and a code example.

```csharp
// UNSAFE - captures a pooled instance across await
handler.OnDataChange = async (notif, ct) =>
{
    log.Add(notif);     // notif may be re-rented on the next publish
    await Task.Yield();
};

// SAFE - value-copy the projection struct before suspending
handler.OnDataChange = async (notif, ct) =>
{
    var snapshot = notif;
    log.Add(snapshot);
    await Task.Yield();
};
```

This affects only the V2 engine; the classic subscription engine is
unaffected. There is no breaking change to `IEncodeable`,
`IDecoder`, `IServiceMessageContext`, or
`ISubscriptionNotificationHandler` — pooling is opt-in via the new
`IPooledEncodeable` sub-interface, which only the source-generated
publish-payload types implement today.

**Dependency Injection:**

`services.AddOpcUa().AddClient(...)` registers a `ManagedSession` factory delegate that lazily connects on first use:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.Client;

services.AddOpcUa().AddClient(opt =>
{
    opt.Configuration = applicationConfiguration;
    opt.Session = new ManagedSessionOptions
    {
        Endpoint = endpoint,
        ReconnectPolicy = new ReconnectPolicyOptions
        {
            Strategy = BackoffStrategy.Exponential
        }
    };
});

// Resolve and connect on first use:
var sessionFactory = serviceProvider
    .GetRequiredService<Func<CancellationToken, Task<ManagedSession>>>();
ManagedSession session = await sessionFactory(ct);
```

The factory caches the connected session — subsequent awaits return the same instance. The registered delegate type is `Func<CancellationToken, Task<ManagedSession>>` (the OPC UA client APIs use `Task` here, not `ValueTask`), so resolving it from dependency injection and `await`-ing the result returns the connected `ManagedSession`. The dependency injection registration also exposes `ITelemetryContext`, `ISessionFactory` (a `DefaultSessionFactory` configured with the V2 engine), `ManagedSessionFactory`, and the top-level `OpcUaClientOptions`.

This iteration uses single-instance options (no named/keyed registrations); the underlying V2 manager consumes options via `IOptionsMonitor<T>` unfiltered. For one-off use, the `AddSubscription`/`TryAddMonitoredItem` extensions adapt plain options snapshots into the required `IOptionsMonitor<T>` automatically. Named-options dependency injection is deferred to a future iteration.

## Subscriptions and Transports

### Durable subscriptions and reshaped Subscription tree

**Source-breaking.** Durable subscription support reshapes the subscription tree on both the client and the server. On the client side, the new public surface in `Libraries/Opc.Ua.Client/Subscription/` includes `ISubscription`, `ISubscriptionManager`, `SubscriptionOptions`, and `MonitoredItemOptions` - these are the V2 options-based shapes; the classic `Opc.Ua.Client.Subscription` continues to ship alongside them. On the server side, the new public surface in `Libraries/Opc.Ua.Server/Subscription/...` includes `DataChangeMonitoredItemQueue`, `EventMonitoredItemQueue`, `IDataChangeMonitoredItemQueue`, `IMonitoredItemQueueFactory`, `ISubscriptionStore`, `IStoredSubscription`, `StoredSubscription`, and `StoredMonitoredItem`.

Consumers adopting the new shape may need to add a `using Opc.Ua.Client.Subscriptions;` import alongside the existing `using Opc.Ua.Client;`. Because the V2 records share their type names with the classic records, namespace aliases are required when both are visible in the same file - see [Fluent Builder, V2 Subscriptions, and Dependency Injection](#fluent-builder-v2-subscriptions-and-dependency-injection) for the canonical alias snippet.

### PubSub

**Not source-breaking.** No public top-level types in `Opc.Ua.PubSub` were removed or renamed in 2.0. Changes are limited to internal modernization, AOT preparation, and diagnostics improvements. `Newtonsoft.Json` remains a direct `<PackageReference>` of `Libraries/Opc.Ua.PubSub/Opc.Ua.PubSub.csproj`, so PubSub consumers keep receiving it transitively (see [Newtonsoft.Json - what really changed](#newtonsoftjson---what-really-changed)).

### Reverse connect

**Not source-breaking.** `ReverseConnectManager`, `ReverseConnectProperty`, and `ReverseConnectServer` retain the same public shape in 2.0. The previously published `ReverseConnectClientCollection` wrapper has been removed; this is already covered by the broader [Configuration collection types removed](#configuration-collection-types-removed) guidance.

### `IMessageSocket` abstraction removed

The runtime transport boundary moved from `IMessageSocket` to the new public `IUaSCByteTransport` (`Opc.Ua.Bindings`). This let us share one UASC pipeline across raw TCP and WebSocket connections and let JSON profiles bypass UASC entirely. As part of this change the entire `IMessageSocket` family was **removed** from the public API surface — this is a breaking change versus 1.5.378.

| Removed type | Replacement |
|---|---|
| `IMessageSocket`, `IMessageSocketAsyncEventArgs`, `IMessageSink`, `IMessageSocketChannel` | `IUaSCByteTransport` (chunk-level send / receive); typical consumers use `ITransportChannel` instead |
| `IMessageSocketFactory` | `IUaSCByteTransportFactory` |
| `MessageSocketExtensions` (e.g. `BeginConnect`) | `IUaSCByteTransport.ConnectAsync` |
| `TcpMessageSocket`, `TcpMessageSocketFactory`, `TcpMessageSocketAsyncEventArgs` | `TcpByteTransport` (sealed; `TcpByteTransportFactory` for client-side construction). The public `TcpTransportChannel` / `TcpTransportChannelFactory` shapes are unchanged. |
| `UaSCUaBinaryTransportChannel.Socket` (`IMessageSocket?`) | `UaSCUaBinaryTransportChannel.Transport` (`IUaSCByteTransport?`) |
| `UaSCUaBinaryClientChannel(..., IMessageSocketFactory, ...)` ctor | `UaSCUaBinaryClientChannel(..., IUaSCByteTransportFactory, ...)` ctor |
| `ITcpChannelListener.ReconnectToExistingChannel(IMessageSocket, ...)` | `ITcpChannelListener.ReconnectToExistingChannel(IUaSCByteTransport, ...)` |

**If you previously implemented a custom `IMessageSocket`** (rare in practice — almost no consumer subclasses `TcpMessageSocket`): the recommended migration path is to implement [`IUaSCByteTransport`](../../../Stack/Opc.Ua.Core/Stack/Tcp/IUaSCByteTransport.cs) directly. See [`Docs/Transports.md`](../../Transports.md) § "Implementing a custom byte transport" for the contract, an implementation checklist, and a worked example (the public [`InProcessTransport`](../../../Stack/Opc.Ua.Core/Stack/Tcp/InProcessTransport.cs) reference implementation that consumes only the public surface). The new abstraction is chunk-oriented (one Send / Receive per UASC `MessageChunk`) and exposes only `ValueTask`-based async; it is intentionally narrower than the old SAEA-based `IMessageSocket` and most legacy implementations collapse to ~150 lines.

### Transport binding registry — `TransportBindings` static API removed

**Source-breaking.** The process-wide `TransportBindings` static class and the `Utils.DefaultBindings` reflection-based assembly auto-load helper have been removed. The new `ITransportBindingRegistry` interface (in `Opc.Ua.Bindings`) is the only public registry surface; it resolves out of the host's `IServiceProvider` so two hosts (e.g. parallel test fixtures or multi-tenant applications) can install different factories without racing on shared global state.

| Removed                                                       | Replacement                                                                                                  |
| ------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------ |
| `TransportBindings.Channels` (static)                         | `ITransportBindingRegistry` (resolved from DI or constructed via `DefaultTransportBindingRegistry.WithDefaultTcp()`) |
| `TransportBindings.Listeners` (static)                        | Same `ITransportBindingRegistry` — listeners and channels share one keyed registry per scheme               |
| `TransportBindings.Channels.SetBinding(...)`                  | `ITransportBindingRegistry.RegisterChannelFactory(...)` or the DI extension `AddCustomTransport<,>()`        |
| `TransportBindings.Listeners.SetBinding(...)`                 | `ITransportBindingRegistry.RegisterListenerFactory(...)` or the DI extension `AddKestrelOpcTcpTransport()`   |
| `TransportBindings.Channels.GetBinding(scheme, telemetry)`    | `ITransportBindingRegistry.GetChannelFactory(scheme)`                                                        |
| `TransportBindings.Listeners.GetBinding(scheme, telemetry)`   | `ITransportBindingRegistry.GetListenerFactory(scheme)`                                                       |
| `TransportBindings.AddBindings(assembly)` (reflection)        | Explicit DI registration via `AddOpcTcpTransport()` / `AddHttpsTransport()` / `AddWssTransport()` / `AddKestrelOpcTcpTransport()` / `AddCustomTransport<,>()` |
| `Utils.DefaultBindings` dictionary                            | Removed; no replacement needed                                                                               |
| `ITransportBindings<T>` interface                             | Removed (folded into `ITransportBindingRegistry` per-facet methods)                                           |
| `TransportBindingsBase` (reflection helper)                   | Removed                                                                                                       |

**`Microsoft.Extensions.DependencyInjection` consumers (recommended path):**

```csharp
// Before (1.5.378):
//   TransportBindings.Listeners.SetBinding(new KestrelTcpTransportListenerFactory());

// After (2.0):
services
    .AddOpcUa()
    .AddOpcTcpTransport()              // raw-socket opc.tcp default
    .AddHttpsTransport()               // HTTPS + HTTPS-JSON
    .AddWssTransport()                 // WSS + WSS-JSON
    .AddKestrelOpcTcpTransport();      // override opc.tcp with Kestrel (last-writer-wins)
```

> `AddHttpsTransport()`, `AddWssTransport()`, and `AddKestrelOpcTcpTransport()`
> all ship in the `OPCFoundation.NetStandard.Opc.Ua.Bindings.Https`
> package. The Kestrel-hosted `opc.tcp://` listener
> (`AddKestrelOpcTcpTransport()`) is available on `net8.0`+ only; on the
> .NET Framework / netstandard targets keep the default raw-socket
> `opc.tcp` listener that ships in `Opc.Ua.Core`.

Every `Add*Transport()` extension installs an `ITransportBindingConfigurator` instance into the `IServiceCollection`. The `DefaultTransportBindingRegistry` singleton runs every registered configurator in registration order at first resolution time, so the **last** registration for a given URI scheme wins — exactly the same semantics `SetBinding` had, but scoped per `IServiceProvider`.

**Non-DI consumers:**

```csharp
// Before (1.5.378):
//   TransportBindings.Listeners.SetBinding(new KestrelTcpTransportListenerFactory());
//   var server = new MyServer();
//   server.Start(config); // pre-2.0 sync path

// After (2.0):
DefaultTransportBindingRegistry registry = DefaultTransportBindingRegistry.WithDefaultTcp();
registry.RegisterListenerFactory(new KestrelTcpTransportListenerFactory());
var server = new MyServer(telemetry);
server.TransportBindings = registry;   // public setter; rejected after StartAsync
await server.StartAsync(config, ct);
```

`ServerBase` exposes a new constructor overload (`ServerBase(ITelemetryContext, ITransportBindingRegistry?)`) and a publicly-settable `TransportBindings` property (with a started-server guard). Non-DI callers that pass nothing get a `DefaultTransportBindingRegistry.WithDefaultTcp()` on first use — exactly the raw-socket TCP listener the 1.5.378 stack defaulted to.

**Custom transport authors:**

```csharp
// Before (1.5.378):
//   TransportBindings.Listeners.SetBinding(new MyCustomListenerFactory());
//   TransportBindings.Channels.SetBinding(new MyCustomChannelFactory());

// After (2.0):
services
    .AddOpcUa()
    .AddCustomTransport<MyCustomListenerFactory, MyCustomChannelFactory>();
```

The DI extension resolves both factory types out of the container (so they may have constructor-injected dependencies), and both are registered into the registry under the `UriScheme` exposed by `MyCustomListenerFactory`.

---

**See also**

- Related: [certificates.md](certificates.md), [identity.md](identity.md), [node-states.md](node-states.md).
- [2.0 migration index](README.md) — analyzer quick-start + symptom → sub-doc table.
- [Migration Guide](../../MigrationGuide.md) — landing page across versions.

