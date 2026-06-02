# OPC UA Device Integration (DI)

> **Glossary:** In this document, **DI** refers to **OPC UA Device
> Integration** (companion specification [OPC 10000-100](https://reference.opcfoundation.org/specs/OPC-10000-100)),
> *not* to .NET Dependency Injection (covered in
> [DependencyInjection.md](DependencyInjection.md)). Wherever this
> document uses the unqualified initialism "DI" it always means the
> companion spec; .NET-DI mentions are spelled out as "Dependency
> Injection (.NET DI)" or appear with `Microsoft.Extensions.DependencyInjection`
> in context.

End-to-end developer guide for the `Opc.Ua.Di*` library trio shipped
with this repository and the `services.AddOpcUa()` hosting model that
plugs it together.

## Contents

- [Library layout](#library-layout)
- [Quick start](#quick-start)
- [Device builder](#device-builder)
- [Device sub-type extensions](#device-sub-type-extensions)
- [Hosting integration](#hosting-integration)
- [Lock service](#lock-service)
- [Software update](#software-update)
- [Client helpers](#client-helpers)
- [What is supported (OPC 10000-100)](#what-is-supported-opc-10000-100)

## Library layout

| Library | Role |
|---------|------|
| `Opc.Ua.Di` | Model assembly: source-generated NodeId tables, DataTypes, ObjectType client proxies, `AddOpcUaDi(NodeStateCollection)` extension. |
| `Opc.Ua.Di.Server` | Server: `DiNodeManager`, fluent `IDeviceBuilder`, locking service, software-update package store, hosting integration. |
| `Opc.Ua.Di.Client` | Client: `DiDeviceClient`, `DiDiscoveryClient`, `DiTopologyClient`, `DiLockClient`, `SoftwareUpdateClient`, hosting integration. |

The running example is `Applications/PumpDeviceIntegrationServer`
(companion-spec server with full simulation **and** the Device
Integration software-update facet attached to a second declarative
device).

## Quick start

### Plain Device Integration (DI) server

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Services
    .AddOpcUa()
    .AddServer(o =>
    {
        o.ApplicationName = "MyDiServer";
        o.EndpointUrls.Add("opc.tcp://localhost:48010/MyDiServer");
    })
    .AddOpcUaDi()
    .ConfigureDevicesFor<DiNodeManager>(async ctx =>
    {
        var device = await ctx.CreateDeviceAsync(
            new QualifiedName("Sensor #1", ctx.Manager.DiNamespaceIndex));
        device.WithIdentification(id =>
        {
            id.Manufacturer = new LocalizedText("Acme");
            id.SerialNumber = "SN-001";
            id.DeviceClass = "Sensor";
        });
    });

await builder.Build().RunAsync();
```

### Companion-spec server (Pumps + Device Integration)

```csharp
builder.Services
    .AddOpcUa()
    .AddServer(o => { ... })
    .AddNodeManager<Pumps.PumpNodeManagerFactory>()
    // Pump factory already loads OPC UA Device Integration via its LoadPredefinedNodesAsync
    // direct chain; do NOT call AddOpcUaDi() — that would
    // double-register the OPC UA Device Integration namespace.
    .ConfigureDevicesFor<Pumps.PumpNodeManager>(async ctx =>
    {
        var pump = await ctx.CreateDeviceAsync(
            new QualifiedName("Pump #2", ctx.Manager.DiNamespaceIndex));
        pump.WithIdentification(id =>
        {
            id.Manufacturer = new LocalizedText("Acme Pumps Inc.");
            id.SerialNumber = "SN-DI-2";
            id.DeviceClass = "Pump";
        });
    });
```

### Client

```csharp
builder.Services
    .AddOpcUa()
    .AddClient(o => { o.Configuration = ...; o.Session.Endpoint = ...; })
    .AddOpcUaDi();

public sealed class MyAppService(
    IDiDiscoveryService discovery,
    Func<NodeId, CancellationToken, ValueTask<DiDeviceClient>> deviceFactory,
    Func<CancellationToken, ValueTask<DiTopologyClient>> topologyFactory)
{
    public async Task PrintAsync(CancellationToken ct)
    {
        DiTopologyClient topology = await topologyFactory(ct);
        await foreach (TopologyEntry entry in topology.EnumerateDevicesAsync(ct))
        {
            DiDeviceClient device = await deviceFactory(entry.NodeId, ct);
            DeviceIdentification id = await device.ReadIdentificationAsync(ct);
            Console.WriteLine($"{id.Manufacturer} {id.Model} ({id.SerialNumber})");
        }
    }
}
```

## Device builder

The `IDeviceBuilder<TDevice>` fluent surface is the recommended way
to create and configure Device Integration device instances programmatically. It
lives in `Opc.Ua.Di.Server.Builders` and integrates with the broader
fluent API for node managers.

### Entry points

All entry points live on `DiNodeManager`:

```csharp
// Default DeviceState under the Device Integration DeviceSet folder (or whatever
// ResolveDefaultDeviceParent() returns).
ValueTask<IDeviceBuilder<DeviceState>> CreateDeviceAsync(
    QualifiedName browseName,
    NodeState? parent = null,
    CancellationToken ct = default);

// Typed factory — required for companion-spec subclasses (PumpType etc.)
ValueTask<IDeviceBuilder<TDevice>> CreateDeviceAsync<TDevice>(
    QualifiedName browseName,
    NodeId typeDefinitionId,
    Func<NodeState, TDevice> factory,
    NodeState? parent = null,
    CancellationToken ct = default)
    where TDevice : ComponentState;

// Wrap an existing device.
IDeviceBuilder<TDevice> Device<TDevice>(TDevice device)
    where TDevice : ComponentState;
IDeviceBuilder<TDevice> Device<TDevice>(NodeId nodeId)
    where TDevice : ComponentState;
IDeviceBuilder<TDevice> DeviceByBrowseName<TDevice>(
    QualifiedName browseName,
    NodeState? parent = null)
    where TDevice : ComponentState;
```

`CreateDeviceAsync` performs four steps:

1. Resolves the parent (default: Device Integration `DeviceSet`; subclasses override
   `ResolveDefaultDeviceParent()` — e.g. machinery managers can return
   the `Machines` folder).
2. Fails fast if a child with the same browse name already exists
   (`StatusCodes.BadBrowseNameDuplicated`).
3. Sets BrowseName/SymbolicName/DisplayName, stamps the
   `TypeDefinitionId`, and assigns the NodeId via the active
   `Context.NodeIdFactory`.
4. Calls the real `AsyncCustomNodeManager.AddPredefinedNodeAsync` so
   subscription wiring, type-tree registration, and root-notifier
   propagation all happen exactly as for nodes loaded from a NodeSet2.

### Fluent surface

```csharp
IDeviceBuilder<TDevice>
    .WithIdentification(action<DeviceIdentificationData>)
    .WithIdentificationGroup(action<IFunctionalGroupBuilder>)
    .WithConfigurationGroup(action<IFunctionalGroupBuilder>)
    .WithMaintenanceGroup(action<IFunctionalGroupBuilder>)
    .WithDiagnosticsGroup(action<IFunctionalGroupBuilder>)
    .WithStatusGroup(action<IFunctionalGroupBuilder>)
    .WithOperationalGroup(action<IFunctionalGroupBuilder>)
    .WithStatisticsGroup(action<IFunctionalGroupBuilder>)
    .WithOperationCountersGroup(action<IFunctionalGroupBuilder>)
    .WithFunctionalGroup(QualifiedName name, action<IFunctionalGroupBuilder>)
    .ConnectsTo(NodeId other)
    .ConnectsToParent(NodeId parent)
    .Configure(action<TDevice, ISystemContext>)
    .WithDeviceHealth(DeviceHealthEnumeration)   // extension; requires TDevice : DeviceState
```

#### Identification properties

`WithIdentification(action)` populates the standard Device Integration nameplate
properties on the device. The mutable `DeviceIdentificationData`
record holds:

- `Manufacturer` (`LocalizedText`, `IsNull` = unset)
- `ManufacturerUri` (`string?`)
- `Model` (`LocalizedText`, `IsNull` = unset)
- `HardwareRevision`, `SoftwareRevision`, `DeviceRevision`,
  `ProductCode`, `DeviceManual`, `DeviceClass`, `SerialNumber`,
  `ProductInstanceUri` (`string?`)
- `RevisionCounter` (`int?`)

Properties that exist as typed children on the device are updated in
place. Missing properties are created via
`NodeState.AddProperty<T, VariantBuilder>` and registered with the
manager — this lets the bare `new DeviceState(parent)` factory work
without a generated companion type.

#### Functional groups

The 8 well-known Device Integration functional groups have typed builder methods.
Arbitrary group names go through `WithFunctionalGroup(qualifiedName, action)`.
Each call is idempotent: invoking the same accessor twice reuses the
existing group. The order of operations inside the builder is:

1. Create the `FunctionalGroupState` child (via
   `TopologyElementState.AddIdentification` or `AddGroupIdentifier`).
2. Normalise BrowseName, SymbolicName, DisplayName, NodeId (via
   `Context.NodeIdFactory`) and `TypeDefinitionId`.
3. Register the group with the manager via `AddPredefinedNodeAsync`.
4. Hand the `IFunctionalGroupBuilder` to the configure delegate.

```csharp
device.WithMaintenanceGroup(fg =>
{
    fg.Organizes(device.Manufacturer!.NodeId);
    fg.Node.WithProperty("LastMaintenanceDate", DateTime.UtcNow);
});
```

#### Topology references

`ConnectsTo(NodeId)` adds a forward `Opc.Ua.Di.ReferenceTypes.ConnectsTo`
reference; `ConnectsToParent(NodeId)` adds the inverse. Useful for
declaring physical / logical topology relationships outside the
hierarchical address-space tree.

#### Device health

`WithDeviceHealth(DeviceHealthEnumeration)` is an extension method
constrained to `TDevice : DeviceState` (where the typed
`DeviceHealth` child variable exists). Throws
`StatusCodes.BadInvalidState` if the device was constructed without
the `DeviceHealth` child — typically because the factory was
`p => new DeviceState(p)` rather than the generator-produced
`CreateDeviceType` factory. Callers can pre-populate the child via
`Configure((dev, ctx) => ...)` to avoid the error.

### Subclass support

Companion-spec managers (`PumpNodeManager`, machinery managers)
inherit from `DiNodeManager` and gain the entire builder surface for
free. They typically override `ResolveDefaultDeviceParent()` to
return the companion-spec-specific container (`Machines` folder,
etc.) and supply their own typed factories to
`CreateDeviceAsync<TPumpType>(...)`.

## Device sub-type extensions

Three extension surfaces on top of `IDeviceBuilder<TDevice>` cover
optional OPC 10000-100 features:

### Software / Block / ConfigurableObject (§5.4 advanced sub-types)

`DeviceBuilderTypeExtensions` adds typed materialisation helpers for
the §5.4 sub-types that are not provided by `WithDeviceType` itself:

```csharp
SoftwareState           sw    = builder.AddSoftware(qn("Firmware"));
BlockState              blk   = builder.AddBlock(qn("InputBlock"));
ConfigurableObjectState cfg   = builder.AddConfigurableObject(qn("DriverFolder"));
```

Each helper creates a typed child under the device, stamps a
manager-assigned `NodeId` via `INodeIdFactory`, sets the
`HasComponent` reference, registers the child via
`AddPredefinedNodeAsync`, and invokes an optional `configure`
callback. The returned typed state is ready for further
configuration through the standard `NodeState` APIs.

### Lifetime indication (§10.6)

`DeviceBuilderLifetimeExtensions.AddLifetimeIndication` creates a
`LifetimeVariableState` under the device with a
`LifetimeIndicationKind` classifier covering all six §10.6 indication
sub-types:

```csharp
device.AddLifetimeIndication(
    qn("OperatingHours"),
    LifetimeIndicationKind.Time,
    startValue: 0.0);

device.AddLifetimeIndication(
    qn("PartsProduced"),
    LifetimeIndicationKind.NumberOfParts,
    startValue: 0.0);
```

The classifier enum maps directly to OPC 10000-100 ObjectType ids:

| `LifetimeIndicationKind` | OPC UA ObjectType |
|---|---|
| `Time`             | `TimeIndicationType` |
| `NumberOfParts`    | `NumberOfPartsIndicationType` |
| `NumberOfUsages`   | `NumberOfUsagesIndicationType` |
| `Length`           | `LengthIndicationType` |
| `Diameter`         | `DiameterIndicationType` |
| `SubstanceVolume`  | `SubstanceVolumeIndicationType` |

`DeviceBuilderLifetimeExtensions.ResolveIndicationTypeId(kind,
namespaceUris)` resolves the matching NodeId at runtime when
applications need to materialise the indication classifier object
themselves.

### Support info (§5.15)

`DeviceBuilderSupportInfoExtensions.WithSupportInfo` creates an
`ISupportInfoState` child on the device (idempotent — re-uses the
existing instance on subsequent calls) and yields it through a
configure callback. The interface exposes
`DocumentationFiles` / `ImageSet` / `ProtocolSupport` folder
properties; consumers populate them by attaching `FileState` or
`BaseObjectState` children through the standard `NodeState` API.
File-backed children commonly use `FileState` wired to an
`IFileSystemProvider` from `Libraries/Opc.Ua.Server/FileSystem`.

```csharp
device.WithSupportInfo(info =>
{
    info.Description = new LocalizedText("Support information for device 1");
    // attach FileState children for DocumentationFiles / ImageSet
    // / ProtocolSupport folders here, using IFileSystemProvider
    // for content backing.
});
```

## Hosting integration

`AddOpcUaDi()` and `ConfigureDevicesFor<TNodeManager>()` plug the
Device Integration (DI) library into the unified `AddOpcUa()`
`Microsoft.Extensions.DependencyInjection` hosting pattern.

### Server-side surface

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

#### When to use `AddOpcUaDi()`

Call this when you want a **plain Device Integration (DI) server**
without any companion-spec subclass. It registers the
`DiNodeManagerFactory` so the hosted service stands up a pure
`DiNodeManager`.

```csharp
services.AddOpcUa()
    .AddServer(o => { ... })
    .AddOpcUaDi();
```

**Do NOT call `AddOpcUaDi()`** alongside a companion-spec factory
that already loads OPC UA Device Integration (e.g. `PumpNodeManagerFactory` which loads
Device Integration + Machinery + Pumps). The Device Integration namespace would be registered twice
and the OPC UA server may reject the duplicate. `AddOpcUaDi()` throws
on its second invocation to surface the misuse early.

#### `ConfigureDevicesFor<TNodeManager>(action)`

Registers a post-setup configurator targeted at a specific manager
type. The runner invokes each matching delegate **after**:

- the manager's `LoadPredefinedNodesAsync` has populated
  `PredefinedNodes`;
- the manager's `CreateAddressSpaceAsync` base has wired up the type
  tree and root notifiers;
- (for `FluentNodeManagerBase` subclasses) the user's
  `Configure(builder)` + `builder.Seal()` is complete.

Configurator-type matching follows `Type.IsAssignableFrom`. A
delegate targeting `DiNodeManager` runs against every Device Integration-derived
manager (including `PumpNodeManager`); a delegate targeting
`PumpNodeManager` will NOT run against a plain `DiNodeManager`.

Multiple `ConfigureDevicesFor<T>` calls accumulate and run in
registration order. **Exception semantics are fail-fast**: any
exception thrown by a configurator aborts hosted-server startup with
a diagnostic that identifies the failing index and target type.

### Context surface

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

### Architecture

| Component | Lifetime | Where |
|-----------|----------|-------|
| `IDiPostSetupRunner` | Singleton (registered by `AddOpcUaDi`/`ConfigureDevicesFor`) | `Opc.Ua.Di.Server.Hosting` |
| `IDiPostSetupConfigurator` | Singleton (one per `ConfigureDevicesFor` call) | `Opc.Ua.Di.Server.Hosting` |
| `DiNodeManagerFactory` / `PumpNodeManagerFactory` | Singleton (.NET-DI-aware ctor injects runner) | server / app |
| `DiNodeManager` / `PumpNodeManager` | Per-server startup (factory passes runner) | app |

The runner is injected into the manager via the factory. The base
`DiNodeManager` auto-invokes it at the end of its own
`CreateAddressSpaceAsync` for every concrete subclass:

- The base `DiNodeManager.CreateAddressSpaceAsync` calls
  `base.CreateAddressSpaceAsync` → `OnAddressSpaceReadyAsync` →
  `PostSetupRunner.RunAsync(this, ct)` in that order.
- Subclasses (e.g. `PumpNodeManager`) override the
  `protected virtual ValueTask OnAddressSpaceReadyAsync(...)` hook to
  materialise instances + drive the fluent `INodeManagerBuilder`.
  The runner fires automatically once `OnAddressSpaceReadyAsync`
  returns — subclasses do not need to call `PostSetupRunner.RunAsync`
  themselves.

### Client-side surface

`Microsoft.Extensions.DependencyInjection.OpcUaClientDiBuilderExtensions`
adds a single extension method on `IOpcUaClientBuilder`:

```csharp
IOpcUaClientBuilder AddOpcUaDi();
```

This registers four .NET-DI-friendly services that wrap the lazy
`ManagedSession` accessor produced by `AddClient(...)`:

- `IDiDiscoveryService` — recursive device discovery from the
  `Objects` folder; streams `DeviceEntry` records as
  `IAsyncEnumerable<DeviceEntry>` so callers can begin processing
  before the browse completes.
- `Func<NodeId, CancellationToken, ValueTask<DiDeviceClient>>` —
  validates a device NodeId and returns a typed client.
- `Func<NodeId, CancellationToken, ValueTask<DiLockClient>>` —
  client wrapper for `LockingServicesType.InitLock` /
  `RenewLock` / `ExitLock` / `BreakLock`.
- `Func<CancellationToken, ValueTask<DiTopologyClient>>` —
  topology browser for the Device Integration well-known folders.
- `Func<NodeId, CancellationToken, ValueTask<SoftwareUpdateClient>>` —
  software-version reader for a SoftwareUpdate instance.

Requires `AddClient(...)` to be called first — the extension throws
`InvalidOperationException` if the managed-session accessor is
missing when one of the factories is resolved.

## Lock service

The lock service implements the OPC 10000-100 §10.5 locking facet.
It tracks ownership of every `TopologyElementType.Lock` instance in
the address space, enforces configurable timeouts, releases locks
automatically when the owning session closes, and exposes the four
spec-defined methods through the typed `LockingServicesState` proxy.

### Components

| Type | Purpose |
|------|---------|
| `ILockService` | Application-facing facade: `InitLock`, `RenewLock`, `ExitLock`, `BreakLock`, `GetState`. Thread-safe. |
| `LockState` | Snapshot record (Locked, LockingClient, LockingUser, RemainingLockTimeSeconds). |
| `LockStatus` | OPC 10000-100 status-code constants. |
| `DefaultLockService` | Default in-memory implementation backed by a `ConcurrentDictionary`. |
| `LockingServicesExtensions.BindToLockService` | Wires the four generated `*MethodState.OnCall` handlers on a `LockingServicesState` instance through an `ILockService`. |

Namespaces: `Opc.Ua.Di.Server.Locking`.

### Status codes

Following OPC 10000-100 §10.5:

| Constant | Value | Returned from |
|----------|-------|---------------|
| `LockStatus.Ok` | 0 | All methods (success) |
| `LockStatus.AlreadyLocked` | 1 | `InitLock` only |
| `LockStatus.CouldNotLock` | 2 | `InitLock` only |
| `LockStatus.NotLocked` | 1 | `RenewLock` / `ExitLock` / `BreakLock` |
| `LockStatus.WrongClient` | 2 | `RenewLock` / `ExitLock` |

### Default lock service

```csharp
var lockService = new DefaultLockService(
    lockDuration: TimeSpan.FromMinutes(5),   // optional, defaults to 5 min
    timeProvider: TimeProvider.System);       // optional, for tests

// Hook session-close so locks held by a disconnecting session are
// released automatically.
lockService.AttachToSessionManager(server.SessionManager);
```

Lock ownership is keyed by `ServerSystemContext.SessionId`. When the
service is invoked from a test (`SystemContext` rather than
`ServerSystemContext`), it falls back to a synthetic session id
derived from `ISystemContext.UserId` so unit tests can still
distinguish callers.

### Binding to a `LockingServicesState`

The Device Integration spec defines a `Lock` child on every `TopologyElementType`
that implements four method invocations. Wire them through your
service:

```csharp
LockingServicesState lockNode = device.Lock!;
lockNode.BindToLockService(elementId: device.NodeId, service: lockService);
```

`BindToLockService` reads `lockNode.InitLock`, `RenewLock`,
`ExitLock`, `BreakLock` and replaces their `OnCall` handlers with
delegates that route into the `ILockService`. The `elementId`
argument keys the service's internal dictionary — typically the
topology element's own NodeId so two different devices can hold
independent locks.

### End-to-end flow

1. Client calls `InitLock(context: "tag")` on a device's Lock.
2. The method's `OnCall` runs the bound `ILockService.InitLock`,
   which checks the per-device record:
   - If unlocked / expired → record `(SessionId, "tag", UserId,
     now + duration)`, return `Ok` (0).
   - If already locked by another session → return `AlreadyLocked` (1).
3. `RenewLock` (same session) extends the expiry timestamp; from a
   different session it returns `WrongClient` (2).
4. `ExitLock` removes the record (subject to ownership check).
5. `BreakLock` removes the record regardless of ownership; intended
   for administrative recovery.
6. If the owning session closes (server-emitted `SessionClosing`
   event), the `DefaultLockService` walks its records and releases
   anything that was held by that session.

### Hosting

Register the service as a singleton through standard
`Microsoft.Extensions.DependencyInjection`:

```csharp
services.AddSingleton<ILockService, DefaultLockService>();
```

Then attach the session-close hook from inside a
`ConfigureDevicesFor<TNodeManager>` configurator once the server's
`SessionManager` is available:

```csharp
.ConfigureDevicesFor<DiNodeManager>(ctx =>
{
    ILockService svc = ctx.GetRequiredService<ILockService>();
    if (svc is DefaultLockService defaultSvc)
    {
        defaultSvc.AttachToSessionManager(ctx.Manager.Server.SessionManager);
    }
    foreach (DeviceState device in ctx.Manager.PredefinedNodes
        .Values.OfType<DeviceState>())
    {
        device.Lock?.BindToLockService(device.NodeId, svc);
    }
});
```

## Software update

The software-update facet exposes a package-storage layer plus a
minimal client helper for OPC 10000-100 §10.3. The full state-machine
wiring (PrepareForUpdate / Installation / PowerCycle / Confirmation)
remains application-specific — the source generator emits typed
`*StateMachineState` proxies that applications drive directly when
needed.

### Server-side: package store

The store is an application-facing abstraction over the binary
artifacts that the Device Integration software-update facet exposes. Two
implementations ship in `Opc.Ua.Di.Server.SoftwareUpdate`:

| Type | Backing | Use case |
|------|---------|----------|
| `MemoryPackageStore` | `ConcurrentDictionary<string, byte[]>` | Unit tests; small fixtures. |
| `FileSystemPackageStore` | `Opc.Ua.Server.FileSystem.IFileSystemProvider` | Production — reuses the same provider model used by the server's `FileSystem` mount. |

#### Surface

```csharp
public interface ISoftwarePackageStore
{
    IAsyncEnumerable<SoftwarePackage> ListAsync(CancellationToken ct = default);
    ValueTask<SoftwarePackage?> GetAsync(string packageId, CancellationToken ct = default);
    ValueTask<bool> ExistsAsync(string packageId, CancellationToken ct = default);
    ValueTask<Stream> OpenReadAsync(string packageId, CancellationToken ct = default);
    ValueTask<SoftwarePackage> AddAsync(SoftwarePackage metadata, Stream payload, CancellationToken ct = default);
    ValueTask<bool> DeleteAsync(string packageId, CancellationToken ct = default);
}
```

`SoftwarePackage` is a record carrying `Id`, `Version`, `Vendor`,
`Description`, `SizeBytes`, `CreatedAt`, `Hash`. Both stores
recompute `SizeBytes` and `CreatedAt` during `AddAsync` so callers
can pass zeros / `default` in the input metadata.

#### `FileSystemPackageStore` layout

Each package is stored as a directory containing two files:

```
{root}/
    {package-id}/
        payload.bin       ← the binary firmware/installer
        metadata.json     ← the SoftwarePackage record as JSON
```

JSON serialization uses a source-generated `System.Text.Json`
context (`SoftwarePackageJsonContext`) so the store is AOT-friendly.

Package IDs must NOT contain `/` or `\` — the store validates and
throws `ArgumentException` to prevent path traversal.

#### Composing with `IFileSystemProvider`

```csharp
IFileSystemProvider fs = new PhysicalFileSystemProvider(
    rootDirectory: "/var/lib/myserver/packages",
    mountName: "Packages",
    isWritable: true);

ISoftwarePackageStore store = new FileSystemPackageStore(
    provider: fs,
    rootPath: "/SoftwarePackages");
```

The same `IFileSystemProvider` instance can also be mounted into the
server's address space via `FileSystemNodeManager` — both paths
share the on-disk layout.

### Hosting

Register the store as a singleton and seed it from a
`ConfigureDevicesFor` configurator:

```csharp
builder.Services.AddSingleton<ISoftwarePackageStore, MemoryPackageStore>();
builder.Services
    .AddOpcUa()
    .AddServer(o => { ... })
    .AddNodeManager<MyNodeManagerFactory>()
    .ConfigureDevicesFor<MyNodeManager>(async ctx =>
    {
        ISoftwarePackageStore store = ctx.GetRequiredService<ISoftwarePackageStore>();
        await store.AddAsync(
            new SoftwarePackage(
                Id: "firmware-1.0.0",
                Version: "1.0.0",
                Vendor: "Acme",
                Description: "Initial firmware",
                SizeBytes: 0,
                CreatedAt: default,
                Hash: string.Empty),
            new FileStream("/path/to/firmware.bin", FileMode.Open));
    });
```

The companion sample `Applications/PumpDeviceIntegrationServer`
demonstrates the end-to-end pattern with `SoftwarePackageSeeder`.

### Client-side software update

`SoftwareUpdateClient` exposes a minimal read-only surface:

```csharp
public sealed class SoftwareUpdateClient
{
    public SoftwareUpdateClient(ISession session, NodeId softwareUpdateNodeId, ITelemetryContext telemetry);
    public ValueTask<string> ReadSoftwareVersionAsync(CancellationToken ct = default);
}
```

Method-level invocation (Loading, Installation, ...) is performed
through the typed `*MethodStateClient` proxies emitted by the source
generator for the Device Integration model. The client integration registers the
factory `Func<NodeId, CancellationToken, ValueTask<SoftwareUpdateClient>>`
via `services.AddOpcUa().AddClient(...).AddOpcUaDi()`.

## Client helpers

Beyond `DiDeviceClient` and `DiDiscoveryClient`, the
`Opc.Ua.Di.Client` library ships three typed client wrappers that
compose the standard OPC UA `Session` surface.

| Helper | Purpose |
|--------|---------|
| `DiLockClient` | Wraps the four `LockingServicesType` methods. |
| `DiTopologyClient` | Browses `DeviceSet`, `NetworkSet`, `DeviceTopology`. |
| `SoftwareUpdateClient` | Reads the software-version property of a `SoftwareUpdateType` instance. |

All three live in `Opc.Ua.Di.Client` and are registered automatically
when applications call `services.AddOpcUa().AddClient(...).AddOpcUaDi()`
on the client-side builder.

### DiLockClient

```csharp
public sealed class DiLockClient
{
    public DiLockClient(ISession session, NodeId lockNodeId, ITelemetryContext telemetry);
    public ValueTask<int> InitLockAsync(string context, CancellationToken ct = default);
    public ValueTask<int> RenewLockAsync(CancellationToken ct = default);
    public ValueTask<int> ExitLockAsync(CancellationToken ct = default);
    public ValueTask<int> BreakLockAsync(CancellationToken ct = default);
}
```

Each method invokes the corresponding `LockingServicesType.{Method}`
generated NodeId on the server and returns the integer status code
(see the [`LockStatus` table](#status-codes) for the OPC spec values).

The constructor takes the NodeId of the `Lock` instance — typically
the `device.Lock` child, not the device itself. Resolve it via
`Session.TranslateBrowsePathsToNodeIdsAsync` or by browsing the
device's `HasComponent` references for a child named `"Lock"`.

```csharp
DiLockClient lockClient = new(session, lockNodeId, telemetry);
int status = await lockClient.InitLockAsync("client-tag");
if (status == LockStatus.Ok)
{
    try
    {
        // Do work that requires exclusive access.
    }
    finally
    {
        await lockClient.ExitLockAsync();
    }
}
```

### DiTopologyClient

```csharp
public sealed class DiTopologyClient
{
    public DiTopologyClient(ISession session, ITelemetryContext telemetry);
    public NodeId DeviceSetId { get; }
    public NodeId NetworkSetId { get; }
    public NodeId DeviceTopologyId { get; }

    public IAsyncEnumerable<TopologyEntry> EnumerateDevicesAsync(CancellationToken ct = default);
    public IAsyncEnumerable<TopologyEntry> EnumerateNetworksAsync(CancellationToken ct = default);
    public IAsyncEnumerable<TopologyEntry> EnumerateChildrenAsync(NodeId parentNodeId, CancellationToken ct = default);
}

public sealed record TopologyEntry(
    NodeId NodeId,
    string DisplayName,
    NodeId TypeDefinitionId,
    QualifiedName BrowseName);
```

`EnumerateDevicesAsync` and `EnumerateNetworksAsync` browse the
hierarchical references of `DeviceSet` and `NetworkSet` and return
the direct children that are Objects. `EnumerateChildrenAsync`
provides the same surface for arbitrary nodes — useful when walking
the `DeviceTopology` tree.

```csharp
DiTopologyClient topology = new(session, telemetry);
await foreach (TopologyEntry device in topology.EnumerateDevicesAsync())
{
    Console.WriteLine($"{device.BrowseName}: {device.DisplayName}");
}
```

### Hosting registration

When `services.AddOpcUa().AddClient(o => { ... }).AddOpcUaDi()` is
called, the following factories are registered as singletons:

```csharp
Func<NodeId, CancellationToken, ValueTask<DiDeviceClient>>
Func<NodeId, CancellationToken, ValueTask<DiLockClient>>
Func<CancellationToken, ValueTask<DiTopologyClient>>
Func<NodeId, CancellationToken, ValueTask<SoftwareUpdateClient>>
IDiDiscoveryService
```

Inject the factory you need into your application services; the
factory opens (or reuses) the lazy `ManagedSession` on first call.

```csharp
public sealed class MyMonitor(
    Func<CancellationToken, ValueTask<DiTopologyClient>> topologyFactory,
    Func<NodeId, CancellationToken, ValueTask<DiDeviceClient>> deviceFactory,
    Func<NodeId, CancellationToken, ValueTask<DiLockClient>> lockFactory)
{
    public async Task ScanAsync(CancellationToken ct)
    {
        DiTopologyClient topo = await topologyFactory(ct);
        await foreach (TopologyEntry e in topo.EnumerateDevicesAsync(ct))
        {
            DiDeviceClient dev = await deviceFactory(e.NodeId, ct);
            DeviceIdentification id = await dev.ReadIdentificationAsync(ct);
            // ...
        }
    }
}
```

## What is supported (OPC 10000-100)

References are to the OPC 10000-100 (DI v1.05) specification sections.

### Foundation — nameplates and topology elements

- `TopologyElementType` (§5.2) — abstract base for every Device Integration node.
- `IVendorNameplateType` (§5.10) — Manufacturer, Model, SerialNumber, HardwareRevision, SoftwareRevision, DeviceRevision, DeviceManual, DeviceClass, ProductInstanceUri, ProductCode. Populated by `IDeviceBuilder.WithIdentification(...)`.
- `ITagNameplateType` (§5.11) — AssetId, ComponentName, DeviceRevision. Populated by the same builder.
- `IDeviceHealthType` (§5.12) — DeviceHealth enum plus the four NAMUR alarm references.
- `IAssetLocationIndicationType` (§5.13) — `StartLocationIndication` / `StopLocationIndication` methods.
- `IOperationCounterType` (§5.14) — PowerOnDuration, OperationDuration, EstimatedReturnedOperationDuration, EstimatedReturnedPowerOnDuration.

### Device tree

- `ComponentType` (§5.3) — abstract; materialised via `IDeviceBuilder`.
- `DeviceType` (§5.4) — `IDeviceBuilder.WithDeviceType<TDeviceState>(factory)`.
- `SoftwareType`, `BlockType`, `ConfigurableObjectType` (§5.4 advanced sub-types) — `IDeviceBuilder.AddSoftware`, `AddBlock`, `AddConfigurableObject`.

### Topology references

- `ConnectsTo` (§5.6.2) — `IDeviceBuilder.ConnectsTo(other)`.
- `ConnectsToParent` (§5.6.3) — `IDeviceBuilder.ConnectsToParent(other)`.
- `IsOnline` (§5.6.4) — exposed for online-component wiring.
- `UpdateParent`, `CanUpdate` (§5.6.5–§5.6.6) — used by software update.

### Topology containers

- `DeviceSet` (§6.2) — well-known instance, auto-created by `DiNodeManager` under `ObjectsFolder`; `IDeviceBuilder` parents devices here by default.
- `NetworkSet` (§6.3) — well-known instance.
- `DeviceTopology` (§6.4) — well-known instance; topology references attach here.
- `NetworkType`, `ConnectionPointType`, `ProtocolType` (§6.5–§6.7).

### Functional groups (§5.7)

All eight well-known Device Integration functional groups are exposed through typed builder methods on `IDeviceBuilder`:

- `Identification`, `Configuration`, `Maintenance`, `Diagnostics`, `Statistics`, `Status`, `Operational`, `OperationCounters`.

Custom groups go through `WithFunctionalGroup(qualifiedName, action)`.

### Lock service (§10.5)

- `LockingServicesType` and the four method types (`InitLockMethodType`, `RenewLockMethodType`, `ExitLockMethodType`, `BreakLockMethodType`) — wired through `ILockService` and `DefaultLockService` (session ownership, configurable timeout, automatic cleanup on session close).

### Software update (§10.3)

- `SoftwareUpdateType` — orchestrates the loading + state-machine wiring.
- `SoftwareLoadingType`, `PackageLoadingType` (§10.3.4) — abstract bases.
- `DirectLoadingType`, `CachedLoadingType`, `FileSystemLoadingType` (§10.3.4) — the three loading variants.
- `SoftwareVersionType` (§10.3.6) — Manufacturer, ProductInstanceUri, SoftwareRevision, PatchIdentifiers, ReleaseDate, ChangeLog, Hash.
- `PrepareForUpdateStateMachineType` (§10.3.7), `InstallationStateMachineType` (§10.3.8), `PowerCycleStateMachineType` (§10.3.9), `ConfirmationStateMachineType` (§10.3.10) — generated proxies driven by the application.
- Storage abstraction: `ISoftwarePackageStore` with `MemoryPackageStore` and `FileSystemPackageStore` implementations.

### Support info & lifetime indication

- `ISupportInfoType` (§5.15) — `IDeviceBuilder.WithSupportInfo(configure)`. Backing folders `DocumentationFiles` / `ImageSet` / `ProtocolSupport` accept `FileState` / `BaseObjectState` children; file content commonly backed by `IFileSystemProvider`.
- `LifetimeVariableType` + 6 indication sub-types (§10.6) — `IDeviceBuilder.AddLifetimeIndication(kind, …)`. Classifier kinds: `Time`, `NumberOfParts`, `NumberOfUsages`, `Length`, `Diameter`, `SubstanceVolume`.

### NAMUR alarms (§10.2)

- `DeviceHealthDiagnosticAlarmType` (abstract) and the four concrete alarm types (`FailureAlarmType`, `CheckFunctionAlarmType`, `OffSpecAlarmType`, `MaintenanceRequiredAlarmType`) — generated proxies wired via the fluent `IAlarmBuilder<TState>` + `ActivatesAlarm` patterns from `Libraries/Opc.Ua.Server/Fluent`.

### DataTypes & VariableTypes

All Device Integration DataTypes (`DeviceHealthEnumeration`, `SoftwareClass`, `LocationIndicationType`, `SoftwareVersionFileType`, `UpdateBehavior`, `FetchResultDataType`, `TransferResultErrorDataType`, `TransferResultDataDataType`, `ParameterResultDataType`) ship as source-generated types in the `Opc.Ua.Di` model library.

### Not yet implemented

- `SoftwareFolderType` (§10.3.5) — multi-version repository.
- `TransferServicesType` (§10.4) — parameter set transfer.

## See also

- [FileSystemClient](FileSystemClient.md) — file-transfer client used by `SoftwareUpdateClient` and `FileSystemPackageStore`.
- [Source-generated NodeManagers](SourceGeneratedNodeManagers.md) — the underlying source generator and fluent API.
