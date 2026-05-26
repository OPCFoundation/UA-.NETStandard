# DI Hosting Integration

The `AddOpcUaDi()` and `ConfigureDevicesFor<TNodeManager>()` extension
methods (Phase 8H) plug the DI library into the unified
`AddOpcUa()` Microsoft.Extensions DI hosting pattern that ships in
master (commit `f3928db05`).

## Server-side surface

`Microsoft.Extensions.DependencyInjection.OpcUaServerDiBuilderExtensions`
adds three methods on `IOpcUaServerBuilder`:

```csharp
IOpcUaServerBuilder AddOpcUaDi();

IOpcUaServerBuilder ConfigureDevicesFor<TNodeManager>(
    Action<IDiPostSetupContext> configure)
    where TNodeManager : DiNodeManager;

IOpcUaServerBuilder ConfigureDevicesFor<TNodeManager>(
    Func<IDiPostSetupContext, ValueTask> configure)
    where TNodeManager : DiNodeManager;
```

### When to use `AddOpcUaDi()`

Call this when you want a **plain DI server** without any companion-
spec subclass. It registers the `DiNodeManagerFactory` so the hosted
service stands up a pure `DiNodeManager`.

```csharp
services.AddOpcUa()
    .AddServer(o => { ... })
    .AddOpcUaDi();
```

**Do NOT call `AddOpcUaDi()`** alongside a companion-spec factory
that already loads DI (e.g. `PumpNodeManagerFactory` which loads
DI + Machinery + Pumps). The DI namespace would be registered twice
and the OPC UA server may reject the duplicate. `AddOpcUaDi()` throws
on its second invocation to surface the misuse early.

### `ConfigureDevicesFor<TNodeManager>(action)`

Registers a post-setup configurator targeted at a specific manager
type. The runner invokes each matching delegate **after**:

- the manager's `LoadPredefinedNodesAsync` has populated
  `PredefinedNodes`;
- the manager's `CreateAddressSpaceAsync` base has wired up the type
  tree and root notifiers;
- (for `FluentNodeManagerBase` subclasses) the user's
  `Configure(builder)` + `builder.Seal()` is complete.

Configurator-type matching follows `Type.IsAssignableFrom`. A
delegate targeting `DiNodeManager` runs against every DI-derived
manager (including `PumpNodeManager`); a delegate targeting
`PumpNodeManager` will NOT run against a plain `DiNodeManager`.

Multiple `ConfigureDevicesFor<T>` calls accumulate and run in
registration order. **Exception semantics are fail-fast**: any
exception thrown by a configurator aborts hosted-server startup with
a diagnostic that identifies the failing index and target type.

## Context surface

The configurator receives an `IDiPostSetupContext`:

```csharp
public interface IDiPostSetupContext
{
    DiNodeManager Manager { get; }
    CancellationToken CancellationToken { get; }
    T GetRequiredService<T>() where T : notnull;

    ValueTask<IDeviceBuilder<DeviceState>> CreateDeviceAsync(...);
    ValueTask<IDeviceBuilder<TDevice>> CreateDeviceAsync<TDevice>(...) where TDevice : ComponentState;
    IDeviceBuilder<TDevice> Device<TDevice>(NodeId nodeId) where TDevice : ComponentState;
    IDeviceBuilder<TDevice> DeviceByBrowseName<TDevice>(QualifiedName name, NodeState? parent = null)
        where TDevice : ComponentState;
}
```

The context **does not** expose a raw `IServiceProvider`. Use
`GetRequiredService<T>()` to resolve application services
(intentionally narrow to discourage service-locator anti-patterns
and lifetime traps).

## Architecture

| Component | Lifetime | Where |
|-----------|----------|-------|
| `IDiPostSetupRunner` | Singleton (registered by `AddOpcUaDi`/`ConfigureDevicesFor`) | `Opc.Ua.Di.Server.Hosting` |
| `IDiPostSetupConfigurator` | Singleton (one per `ConfigureDevicesFor` call) | `Opc.Ua.Di.Server.Hosting` |
| `DiNodeManagerFactory` / `PumpNodeManagerFactory` | Singleton (DI-aware ctor injects runner) | server / app |
| `DiNodeManager` / `PumpNodeManager` | Per-server startup (factory passes runner) | app |

The runner is injected into the manager via the factory. The manager
calls `runner.RunAsync(this, cancellationToken)` at the end of its
own `CreateAddressSpaceAsync` override:

- `DiNodeManager` runs the runner ONLY when its concrete runtime type
  is exactly `DiNodeManager` — subclasses must opt in by calling
  `PostSetupRunner.RunAsync(this, ct)` themselves at the appropriate
  point in their own override (typically AFTER any
  `Configure(builder)`/`builder.Seal()` work).
- `PumpNodeManager` does this in
  `CreateAddressSpaceAsync` after `Configure(builder)` + `builder.Seal()`
  so configurators see the fully wired pump.

## Client-side surface

`Microsoft.Extensions.DependencyInjection.OpcUaClientDiBuilderExtensions`
adds a single extension method on `IOpcUaClientBuilder`:

```csharp
IOpcUaClientBuilder AddOpcUaDi();
```

This registers four DI-friendly services that wrap the lazy
`ManagedSession` accessor produced by `AddClient(...)`:

- `IDiDiscoveryService` — recursive device discovery from the
  `Objects` folder.
- `Func<NodeId, CancellationToken, ValueTask<DiDeviceClient>>` —
  validates a device NodeId and returns a typed client.
- `Func<NodeId, CancellationToken, ValueTask<DiLockClient>>` —
  client wrapper for `LockingServicesType.InitLock` /
  `RenewLock` / `ExitLock` / `BreakLock`.
- `Func<CancellationToken, ValueTask<DiTopologyClient>>` —
  topology browser for the DI well-known folders.
- `Func<NodeId, CancellationToken, ValueTask<SoftwareUpdateClient>>` —
  software-version reader for a SoftwareUpdate instance.

Requires `AddClient(...)` to be called first — the extension throws
`InvalidOperationException` if the managed-session accessor is missing
when one of the factories is resolved.

## End-to-end example

`Applications/MinimalPumpServer/Program.cs` demonstrates the full
pattern: registers the pump factory, declares a second pump
declaratively via `ConfigureDevicesFor<Pumps.PumpNodeManager>(...)`,
and seeds the `ISoftwarePackageStore` from the same configurator.

## See also

- [DeviceBuilder](DeviceBuilder.md) — the fluent builder surface
  exposed through the context.
- [SoftwareUpdate](SoftwareUpdate.md) — composing the package store
  with `ConfigureDevicesFor`.
- [LockService](LockService.md) — wiring the lock service through
  DI registration.
