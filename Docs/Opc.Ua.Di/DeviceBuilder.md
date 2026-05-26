# DI Device Builder

The `IDeviceBuilder<TDevice>` fluent surface (Phase 8B) is the
recommended way to create and configure OPC 10000-100 DI device
instances programmatically. It lives in
`Opc.Ua.Di.Server.Builders` and integrates with the existing
Phase 7 fluent API for node managers.

## Entry points

All entry points live on `DiNodeManager`:

```csharp
// Default DeviceState under the DI DeviceSet folder (or whatever
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

// Wrap an existing device (loaded from a NodeSet2 XML).
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

1. Resolves the parent (default: DI `DeviceSet`; subclasses override
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

## Fluent surface

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

### Identification properties

`WithIdentification(action)` populates the standard DI nameplate
properties on the device. The mutable `DeviceIdentificationData`
record holds:

- `Manufacturer` (LocalizedText, IsNull = unset)
- `ManufacturerUri` (string?)
- `Model` (LocalizedText, IsNull = unset)
- `HardwareRevision`, `SoftwareRevision`, `DeviceRevision`,
  `ProductCode`, `DeviceManual`, `DeviceClass`, `SerialNumber`,
  `ProductInstanceUri` (string?)
- `RevisionCounter` (int?)

Properties that exist as typed children on the device are updated in
place. Missing properties are created via
`NodeState.AddProperty<T, VariantBuilder>` and registered with the
manager — this lets the bare `new DeviceState(parent)` factory work
without a generated companion type.

### Functional groups

The 8 well-known DI functional groups have typed builder methods.
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

### Topology references

`ConnectsTo(NodeId)` adds a forward `Opc.Ua.Di.ReferenceTypes.ConnectsTo`
reference; `ConnectsToParent(NodeId)` adds the inverse. Useful for
declaring physical / logical topology relationships outside the
hierarchical address-space tree.

### Device health

`WithDeviceHealth(DeviceHealthEnumeration)` is an extension method
constrained to `TDevice : DeviceState` (where the typed
`DeviceHealth` child variable exists). Throws
`StatusCodes.BadInvalidState` if the device was constructed without
the `DeviceHealth` child — typically because the factory was
`p => new DeviceState(p)` rather than the generator-produced
`CreateDeviceType` factory. Callers can pre-populate the child via
`Configure((dev, ctx) => ...)` to avoid the error.

## Subclass support

Companion-spec managers (`PumpNodeManager`, machinery managers) inherit
from `DiNodeManager` and gain the entire builder surface for free.
They typically override `ResolveDefaultDeviceParent()` to return the
companion-spec-specific container (`Machines` folder, etc.) and supply
their own typed factories to `CreateDeviceAsync<TPumpType>(...)`.

## Hosting integration

See [Hosting.md](Hosting.md) for the
`ConfigureDevicesFor<TNodeManager>(action)` integration with the
`AddOpcUa()` dependency-injection pipeline.
