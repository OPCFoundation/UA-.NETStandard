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

`ReverseConnectManager.StartService` / `StopService` / `Dispose` retain
their public synchronous shape; internally they bridge to async
`OpenHostsAsync` / `CloseHostsAsync` (snapshot-under-lock,
await-outside-lock) so the listener-layer work is fully async without
breaking existing call sites in samples, fluent builders, and tests.

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
