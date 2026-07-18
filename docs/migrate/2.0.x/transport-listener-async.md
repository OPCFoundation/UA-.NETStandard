# Transport listener API is async (issue #3923)

The `ITransportListener` contract was refactored from synchronous
`Open` / `Close` / `IDisposable` to asynchronous
`OpenAsync` / `CloseAsync` / `IAsyncDisposable`. The change cascades
through the server bootstrap (`ServerBase`, `ITransportListenerFactory`,
`StandardServer`, `LdsServer`) and through every in-tree transport
listener (raw-socket TCP, Kestrel-TCP, HTTPS, WSS).

There is **no `[Obsolete]` shim** for this change; the sync methods are
gone. Update every call site that previously invoked `listener.Open` /
`listener.Close` / `listener.Dispose`.

## What changed

### `ITransportListener`

```csharp
// Before — sync
public interface ITransportListener : IDisposable
{
    void Open(Uri baseAddress, TransportListenerSettings settings,
              ITransportListenerCallback callback);
    void Close();
    // …
}

// After — async
public interface ITransportListener : IAsyncDisposable
{
    ValueTask OpenAsync(Uri baseAddress, TransportListenerSettings settings,
                        ITransportListenerCallback callback,
                        CancellationToken ct = default);
    ValueTask CloseAsync(CancellationToken ct = default);
    // …
}
```

### `ITransportListenerCertificateRotation`

```csharp
// Before
IReadOnlyList<string> CloseChannelsForCertificate(Certificate oldCertificate);

// After
ValueTask<IReadOnlyList<string>> CloseChannelsForCertificateAsync(
    Certificate oldCertificate,
    CancellationToken ct = default);
```

### `ITransportListenerFactory`

```csharp
// Before
List<EndpointDescription> CreateServiceHost(
    ServerBase serverBase, IDictionary<string, ServiceHost> hosts,
    /* … */);

// After
ValueTask<List<EndpointDescription>> CreateServiceHostAsync(
    ServerBase serverBase, IDictionary<string, ServiceHost> hosts,
    /* … */,
    CancellationToken ct = default);
```

### `ServerBase`

* `CreateServiceHostEndpoint` → `CreateServiceHostEndpointAsync`
* `InitializeServiceHosts(out …)` → `InitializeServiceHostsAsync` returning a
  new `ServiceHostInitializationResult` struct (no `out` parameters because
  `async` methods cannot have them).
* `ServerBase.Dispose` keeps its synchronous `IDisposable` contract but
  bridges to `IAsyncDisposable.DisposeAsync` internally — server shutdown
  from a sync host process continues to work unchanged.

### `ReverseConnectHost`

```csharp
// Before
public void Open();
public void Close();

// After
public ValueTask OpenAsync(CancellationToken ct = default);
public ValueTask CloseAsync(CancellationToken ct = default);
```

`ReverseConnectManager` now exposes a fully asynchronous lifecycle:

```csharp
await manager.StartServiceAsync(configuration, ct);
await manager.StopServiceAsync(ct);
await manager.DisposeAsync();
```

The synchronous `StartService` and `Dispose` APIs remain as `[Obsolete]` compatibility wrappers. They run the async lifecycle on an off-context bridge and may block the caller thread. Replace them with `StartServiceAsync` and `DisposeAsync` (`await using`) when migrating.

The manager validates and prepares a candidate configuration before stopping a working listener. If activation fails after the old listeners stop, it recreates and reopens the previous configuration. Cancellation cleans partially initialized listeners and preserves or restores the previous service.

### `ReverseConnectManager.RegisterWaitingConnection`

```csharp
// Before (still compiles, now [Obsolete])
int id = manager.RegisterWaitingConnection(url, serverUri, handler, strategy);

// After
int id = await manager.RegisterWaitingConnectionAsync(
    url, serverUri, handler, strategy, ct);
```

The synchronous `RegisterWaitingConnection` overload is now `[Obsolete]`.
It is retained for backward compatibility, but in DI-lazy scenarios (where
the manager was configured with an initial startup and started on first
use) it must block on an off-context bridge to `EnsureStartedAsync` before
registering so the configured listeners are bound. Prefer
`RegisterWaitingConnectionAsync`, which starts the manager without blocking.
Directly constructed, manually started, or unconfigured managers keep the
previous registration-only behavior for the synchronous overload.

### Reverse-connect configuration providers

Custom subclasses that previously overrode `OnUpdateConfiguration` should migrate to `IReverseConnectConfigurationProvider`. Providers run asynchronously before any active listener is stopped and may validate, replace, or augment the effective `ReverseConnectClientConfiguration`.

```csharp
services.AddSingleton<IReverseConnectConfigurationProvider, MyProvider>();
```

The protected `OnUpdateConfiguration` hooks remain `[Obsolete]` for compatibility and are invoked outside the lifecycle gate. Existing overrides can continue to mutate or reject a candidate while they migrate to the provider model.

### Dependency-injection startup

`AddClient(...)` no longer blocks inside the singleton factory. It registers a hosted service that eagerly invokes `EnsureStartedAsync` when a .NET Generic Host starts. In a plain `ServiceCollection`, `WaitForConnectionAsync` and `RegisterWaitingConnectionAsync` start the manager lazily on first use.

## Migration steps

### Application / sample code

1. **Replace `listener.Open(...)`** with `await listener.OpenAsync(...)`.
   Mark the enclosing method `async` if it isn't already.
2. **Replace `listener.Close()`** with `await listener.CloseAsync()`.
3. **Replace `listener.Dispose()`** with `await listener.DisposeAsync()`.
4. **Replace `using var listener = ...`** with
   `await using var listener = ...`.
5. **Replace `rotator.CloseChannelsForCertificate(cert)`** with
   `await rotator.CloseChannelsForCertificateAsync(cert)`.
6. **Replace `manager.StartService(config)`** with
   `await manager.StartServiceAsync(config, ct)`.
7. **Replace `manager.RegisterWaitingConnection(...)`** with
   `await manager.RegisterWaitingConnectionAsync(..., ct)`.
8. **Replace `manager.Dispose()` / `using`** with
   `await manager.DisposeAsync()` / `await using`.

### Custom transport binding implementations

1. **Implement `OpenAsync` / `CloseAsync` / `DisposeAsync`** on your
   listener; remove the old `Open` / `Close` / `Dispose` methods.
2. **Implement `CreateServiceHostAsync`** on your binding factory if you
   subclass `TcpServiceHost` / `HttpsServiceHost` (the in-tree base
   implementations already do).
3. If your listener supports certificate rotation, implement
   `CloseChannelsForCertificateAsync` instead of the sync variant.

### Custom server subclass (`StandardServer` / `LdsServer` heir)

1. If you override `InitializeServiceHosts`, rename to
   `InitializeServiceHostsAsync` and return
   `ServiceHostInitializationResult` instead of using `out` parameters.
2. If you override `CreateServiceHostEndpoint`, rename to
   `CreateServiceHostEndpointAsync` (the body stays the same except for
   the `await listener.OpenAsync(...)` line).

## Why

The synchronous `Open` / `Close` contract forced
`KestrelTcpTransportListener`, `HttpsTransportListener`, and the
`SharedKestrelHostRegistry` to wrap `IHost.StartAsync` /
`StopAsync` in `.GetAwaiter().GetResult()` — a sync-over-async
anti-pattern that can deadlock under certain SynchronizationContext
configurations and prevents the listener from cooperating with
`CancellationToken`-driven shutdown. The async contract removes the
bridge entirely and is a prerequisite for further work on graceful
shutdown and reverse-connect cancellation.
