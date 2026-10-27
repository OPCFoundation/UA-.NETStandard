# Composing Device Integration companions

A server has one owner of the Device Integration (DI) namespace. Add companion
models to that owner with `AddDiModel<TProvider>`, then create their instances
through the owner's configuration callbacks. Registering another standalone
DI owner fails during service registration rather than creating a second,
unbrowsable `DeviceSet`.

## Who owns the DI address space?

Exactly one node manager serves the DI namespace. `AddOpcUaDi`, `AddMachinery`,
`AddRobotics` and `AddAutoId` each claim it with `ClaimDiAddressSpace`. The
claim is exclusive: a second claim, by the same or a different owner, fails at
registration with an exception that names the current owner.
Pumps and Scales also claim DI when used as standalone owners. Vision claims
DI only when configured with Machine Vision assets. Composition methods such
as `AddPumpsToMachinery` add a model and runtime to an existing owner instead
of claiming DI again.

## How does a companion contribute its model?

A companion implements `IDiModelProvider` and registers it with `AddDiModel<T>`.
The provider declares its namespaces and loads its predefined nodes. A plain
`DiNodeManager` loads DI first and then every registered provider in
ascending `Order`, then provider type name, with registration order breaking
remaining ties. A provider must tolerate dependencies that are already loaded.
The namespaces are added to both the node manager factory and
the node manager, which keeps the two in agreement about namespace indexes.

```csharp
services.AddOpcUa()
    .AddServer(options => { /* application name, endpoints, PKI */ })
    .AddOpcUaDi()
    .AddAutoIdModel(options => { /* reader options */ });
```

The family interfaces `IMachineryModelProvider`, `IRoboticsModelProvider`,
`IRobotIntentModelProvider` and `IVisionModelProvider` derive from
`IDiModelProvider`. A provider registered with `AddDiModel` is
merged into the family owner, deduplicated by instance, and Machinery keeps its
built-in providers first. Register a provider either with `AddDiModel` or with
the family's own `Add*Model` method, never with both.

## How does a companion attach runtime nodes?

Runtimes use the public members of `DiNodeManager`:

- `RegisterResource` hands the manager an `IAsyncDisposable` that it releases
  when the manager is disposed. Resources are released newest first, an
  exception from one resource does not stop the others, and the failures are
  thrown together as an `AggregateException`. `DisposeResourcesAsync` releases
  them explicitly; a subsequent manager disposal does not release them again.
  The Machinery-named registration and disposal extensions forward to these
  neutral members.
- `RegisterRootNotifierAsync` registers an event source and attaches existing
  event subscriptions to it.
- `TryOrganizeUnder` adds an `Organizes` reference from a folder to an
  instance and returns `false` when the folder is not in the address space.
  Resolve `Machinery.ObjectIds.Machines` through the server's namespace table
  to organize a machine; a matching BrowseName alone does not identify that folder.
- `CompanionInstanceNamespaceIndex` is the namespace index a companion uses
  for the instances it creates. A family owner overrides it to return its own
  instance namespace.

Runtime registration, including `CreateDeviceAsync`, automatically keeps DI
components reachable from `DeviceSet`.
If its parent has no hierarchical path from DeviceSet, the manager adds an
`Organizes` reference. For a composed device, it links the outermost component
in the hierarchical parent chain rather than every nested device.

Use `IsReachableFrom(root, target)` to query forward hierarchical paths through
children and explicit references owned by the manager. Inverse, non-hierarchical
and remote references do not establish reachability; cycles terminate.
`TryEnsureDeviceSetReachability(device)` applies the same placement rule to
devices registered through another path. It returns `false` when the manager
has no DeviceSet, and repeated calls do not duplicate references. Existing
predefined instances loaded during startup are not reorganized automatically.

## Where do the companion instances belong?

DeviceSet reachability and Machinery placement are separate requirements.
DI requires every `ComponentType` instance to be reachable, with only the root
of a composed device directly referenced. Machinery requires machines to appear
under `Machines`; it does not make every DI device a machine.

| Family | Placement | Basis |
| --- | --- | --- |
| DI devices, AutoID readers, Robotics devices | DeviceSet, directly or through a composed root | OPC 10000-100 1.05.0 §4.9; AutoID devices derive from DI DeviceType |
| Machinery machines, Extrusion machines, GranulateDrying machines, Machine Vision assets | Machines; also reachable from DeviceSet if the instance is a DI ComponentType subtype | OPC 40001-1 1.04.2 §9.2; the domain model determines the machine type |
| Pumps and Scales | DeviceSet; also Machines when `OrganizeIntoMachinesFolder` is enabled | DI §4.9 and the families' Machinery composition options |
| AutoID reader attached with `AsComponentOf(machine)` | The machine's Components add-in; DeviceSet reaches the reader independently if necessary | OPC 40001-1 §§8.3, 11.1–11.2 and DI §4.9 |

Readers are not added to Machines. `AsComponentOf` requires the machine's
Components add-in and supplies the component Identification add-in; see
[AutoID placement](AutoID.md#placement).

## In what order is configuration applied?

`ConfigureDevicesFor<T>` runs in the DI post-setup pipeline after model loading
and the owner's initial configuration. `ConfigureAutoIdFor<T>` uses that
pipeline. Register custom `ConfigureMachineryFor<T>` callbacks before
`ConfigureAutoIdFor<T>` so the machines exist when readers attach to them. With
`AddAutoIdToMachinery`, the pipeline is registered first automatically.

## How do AutoID readers join a Machinery server?

`AddAutoIdToMachinery` lives in the
`OPCFoundation.NetStandard.Opc.Ua.AutoID.Machinery.Server` package. The core
`Opc.Ua.AutoID.Server` package depends only on the Machinery model, so a server
that hosts readers without Machines does not load the Machinery server stack.
See the [AutoID guide](AutoID.md) for the reader runtime and the
[migration guide](MigrationGuide.md#composing-autoid-with-machinery) for the
changes from earlier previews.

Vision and Robot Intent retain their own runtime configuration contracts;
the shared provider interface does not turn their managers into `DiNodeManager`
subclasses.
