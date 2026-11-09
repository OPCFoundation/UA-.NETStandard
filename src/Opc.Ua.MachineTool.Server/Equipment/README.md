# MachineTool equipment observations

This module implements the equipment observations in OPC 40501-1, 1.02:
[8.5](https://reference.opcfoundation.org/specs/OPC-40501-1/8.5),
[11.1](https://reference.opcfoundation.org/specs/OPC-40501-1/11.1), and
[12.8–12.10](https://reference.opcfoundation.org/specs/OPC-40501-1/12).
It does not introduce tool-changing, locking, movement, or machining commands.

## Selecting the surface

The mandatory machine has an `Equipment` object, but no optional `Tools`
inventory. Select capabilities explicitly:

```csharp
MachineToolRuntime<MachineToolState> machine = await context
    .AddMachineTool("Mill")
    .WithProvider(provider)
    .WithEquipment(new MachineToolEquipmentOptions
    {
        Features = MachineToolFeatures.Tools |
            MachineToolFeatures.DynamicTools |
            MachineToolFeatures.ToolLife,
        MaximumTools = 256,
        MaximumLifeEntriesPerTool = 4,
        MaximumElements = 1024
    })
    .WithIdentification(identity =>
    {
        identity.Manufacturer = new LocalizedText("Example manufacturer");
        identity.SerialNumber = "Mill-1";
        identity.ProductInstanceUri = "urn:example:mill-1";
    })
    .BuildAsync(cancellationToken);
```

`WithEquipment` adds its feature bits to the current selection. A subsequent
`WithFeatures` replaces the selection. `DynamicTools` and `ToolLife` require
`Tools`. The provider must supply `MachineToolSnapshot.Equipment`; an explicitly
empty inventory is valid. Missing observations fail setup or refresh rather
than advertising a working device with default values.

The default limits are 1,024 total tools/multitools, 16 life entries per tool,
4,096 combined entries, and 1,024 characters per text field. Bounds apply to the
complete inventory, not each browse page. Exceeding a bound rejects the entire
observation without truncation.

## Tools and multitools

`MachineToolEquipmentObservation.Tools` contains `MachineToolTool` records.
Each has a stable, machine-unique application `Key` and a `Kind`. A `MultiTool`
contains single `Tool` entries in its `Tools` collection; the standard does not
declare nested multitools. Empty multitools are allowed.

The key is not a generated global tool identifier:

- `Identifier` is optional and comes from the superordinate tool-management
  system. The module never derives it from the key or controller identifiers.
- `Name` is optional and supplies the recommended display name.
- `Location` is the actual standard `BaseObjectType`, with MachineTool-qualified
  `Name` (`String`) and `PlaceNumber` (`UInt16`) properties. There is no invented
  `ToolLocationType`. A location on an individual multitool constituent is
  supported when explicitly reported, though the specification discourages it.
- Every single tool requires `ControlIdentifier1`, `ControlIdentifierInterpretation`,
  and `Locked`, including `Locked/ReasonForLocking`.
- `NumberBased` management has one identifier; `GroupBased` requires
  `ControlIdentifier2`. `Other` retains the provider's custom interpretation.
- `LastUsage` is an optional raw `DataValue`, preserving its own quality and
  acquisition time. `PlannedForOperating` is an optional observation, not a command.

Absent optional fields remain absent. An existing slot retains its selected
type, parent and optional schema; use a new slot identity for a different
schema. Dynamic inventories can add, remove and reorder slots, including
multitool constituents. Ordinary `HasComponent` references are retained; the
module does not invent `NumberInList` properties for tools.

## Tool life

The `ToolLife` capability requires at least one `MachineToolToolLife` entry on
every single tool. Multiple entries are independent measurements, not values
to add together.

`Value`, `StartValue`, `WarningValue`, and `LimitValue` are `DataValue` values,
not `double` conversions. All built-in integral and floating-point Number
representations are preserved. Validation rejects arrays, nonnumeric values,
NaN, infinity, missing mandatory samples, usable samples without acquisition
times, and fractional part/usage counts. Threshold comparisons are exact even
for adjacent `UInt64` values above Double's integral precision.

| Indication | Required units |
| --- | --- |
| `Time` | Hours, UNECE `HUR` |
| `NumberOfParts`, `NumberOfUsages` | One, `C62` |
| `Feed_Distance`, `Cutting_Distance` | Millimetres, metres or kilometres (`MMT`, `MTR`, `KMT`) |
| `Length`, `Diameter` | Those distance units or micrometres (`4H`) |
| `Other` | An explicitly supplied unit definition |

No unit conversion is performed. Present start/warning/limit thresholds follow
the reported counting direction: increasing for `IsCountingUp=true`, decreasing
otherwise. Measurements are not clamped to the thresholds, and reaching a
threshold does not fabricate a lock, alarm or equipment command.

The pinned canonical NodeSet qualifies `EngineeringUnits` in the MachineTool
namespace, although the prose table in 11.1 labels it namespace zero. Both server
and client preserve the actual canonical declaration.

## Generated GMS and vendor nodes

`CreateToolAsync`, `CreateMultiToolAsync`, and `CreateCompanionToolAsync` return
narrowly typed `MachineToolEquipmentNode<TState>` results. A result can contain
an actual generated `SensorState` or `MultiSensorState`, not a copied base tool.
Its NodeId, requested QName, parent, concrete type and additional derived children
are preserved. A precreated unpublished tool already under the requested parent
can also be adopted instead of reconstructed.

Factories must return unique unpublished nodes and must not mutate a live
parent. A returned optional `IAsyncDisposable` is owned after a valid transfer
and is drained on rollback, removal or disposal. Returning an already registered
node does not transfer or dispose its existing owner's resources.

For an actual GMS `Qualified` life entry, select:

- `Name = "Qualified"` and `NamespaceUri = GmsModel.NamespaceUri`;
- `Indication = ToolLifeIndication.Time`, hours, and `IsCountingUp = false`;
- a Double-valued sample, matching the actual GMS declaration.

When needed, the binder instantiates the loaded generated declaration using the
shared node clone/rebase helpers. It retains `ToolLifeState<double>` and its
derived declaration metadata instead of creating an untyped base substitute.
Other GMS observations remain the GMS provider/factory's responsibility. A
factory-selected tool-life entry without its own provider sample is rejected.

## Transactions, lifetime and client reads

`RefreshAsync` stages monitoring, retained production, equipment and prognoses
before their coherent commit. Provider and factory callbacks are not awaited
under the runtime's commit gate. Concurrent/reentrant mutations are rejected.
Cancellation before commit preserves the accepted observation; after commit,
publication completes or is retained for `RetryPublicationAsync`.

Dynamic removal first awaits the shared namespace owner's final-value barrier.
Its boundary is inclusion in actual server `PublishResponse` construction for
active reporting subscriptions, **not** transport acknowledgement. Nodes and
their real resources are then removed through the node manager's lifecycle.
No timer or successful no-op substitutes for that barrier.

Dynamic lists have a read-only namespace-zero `NodeVersion` and publish standard
`GeneralModelChangeEvent` notifications, including pure reorders. Treat the
version as an opaque token. Failed publication retains the accepted event for
retry without inventing a new event identity.

`MachineToolClient.ReadEquipmentAsync` returns the actual node/type/QName
inventory and original `DataValue` samples. `NodeId.IsNull` and
`DataValue.IsNull` distinguish absent optional members from bad-quality values.
The workflow uses generated proxies and the shared continuation-owning browser,
applies aggregate limits, honors cancellation/disposal, and checks list versions
around its multi-request read. It does not claim that separate service reads
freeze every changing device measurement.

## Conformance evidence

| Conformance unit | Implemented behavior and retained tests |
| --- | --- |
| MachineTool Equipment ToolIdentification | Complete tools/multitools, true location layout, identifiers, read-only control observations; `ToolInventoryPreservesIdentifiersLocationAndIntegralLifeOverTcpAsync`, `DynamicMultiToolRemovalPublishesFinalSamplesAndDrainsResourcesAsync` |
| MachineTool Equipment Dynamic Tool List | Add/reorder/remove, version/events, final delivery, real resource draining and rollback; equipment lifetime tests and cross-module prognosis rollback tests |
| MachineTool Equipment ToolLife | Mandatory per-tool entries, exact Number representations, all indications/units, direction/threshold validation, raw quality/time; equipment validation tests and `ActualGmsSensorAndQualifiedLifeKeepTheirGeneratedIdentityAsync` |

These are behavioral implementation mappings, not a claim of independent CTT
certification or completion of the GMS runtime.
