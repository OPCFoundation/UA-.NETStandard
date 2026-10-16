# MachineTool prognosis observations

This module implements the concrete prognosis types in
[OPC 40501-1, 8.6](https://reference.opcfoundation.org/specs/OPC-40501-1/8.6).
A prognosis is an equipment/provider forecast, not an instruction to perform
maintenance, move equipment, load a part or change a tool.

## Explicit capabilities

Select both the surface and the forecast kinds the provider actually supports:

```csharp
builder.WithPrognoses(new MachineToolPrognosisOptions
{
    Features = MachineToolFeatures.Prognoses |
        MachineToolFeatures.DynamicPrognoses,
    SupportedKinds =
    [
        MachineToolPrognosisKind.Maintenance,
        MachineToolPrognosisKind.ToolChange,
        MachineToolPrognosisKind.PartUnload
    ],
    MaximumPrognoses = 64
});
```

`WithPrognoses` adds these feature bits to the builder's current selection.
A later `WithFeatures` replaces that selection. An enabled provider supplies
`MachineToolSnapshot.Prognoses`; an explicitly empty list is valid. Selecting a
surface without its capabilities or observations fails rather than publishing
default predictions.

Defaults bound the list to 128 forecasts, affected-entity lookup to 8,192
entities, and text fields to 1,024 characters. Each forecast has a stable
application `Key`. `NamespaceUri` optionally selects a loaded companion
namespace for its QName.

## Concrete types

All ten concrete standard variants are supported:

| Kind | Required descriptive components |
| --- | --- |
| Maintenance | Activity |
| ManualActivity | Activity |
| PartLoad / PartUnload | Location, PartName |
| ProcessChangeover | Activity, Location |
| ProductionJobEnd | SourceIdentifier |
| ToolChange / ToolLoad / ToolUnload | Location |
| UtilityChange | UtilityName |

Every entry requires `PredictedTime`, supplied as a raw UtcTime `DataValue`.
Quality and acquisition time are retained; the runtime does not predict a date
from its own clock. Explicit bad-quality unavailability stays unavailable.
Usable samples without a source time or prediction, wrong scalar types,
unsupported kinds and fields not declared for the selected kind are rejected.
Overdue forecasts are not discarded merely because the provider's predicted
date is in the past.

The retained `ProductionJobEndPrognosisType`, `JobNodeId` and `PartNodeId`
declarations remain available despite their standard deprecation metadata.

## Actual affected entities

An explicit `MachineToolPrognosisTarget` can identify:

- a tool by `ToolKey`;
- a retained job by `JobKey`;
- a retained part by `JobKey`, `PartSetKey` and `PartKey`;
- an actual same-instance `NodeId` instead of those keys.

Without an explicit target, an unambiguous reported identifier is matched to
the machine's prospective inventory. A represented target supplies its actual
identifying fields when omitted from the forecast; conflicting supplied fields
are rejected. An ambiguous identifier requires explicit slot keys. The module
does not fabricate instance IDs or resolve another machine's similarly named
entity.

The integrated runtime resolves against the **prepared** equipment and
production inventories. Thus a forecast can reference a new GMS job, part or
sensor in the same observation before any sibling commits. A removed entity
cannot remain the target of a retained forecast. Remove or replace its forecast
in the same coherent update.

`ToolLoadPrognosisType` deliberately has no `ToolNodeId`; supplying an explicit
target for that type is rejected rather than adding a nonstandard component.
Tool names/identifiers still reconcile with a modeled tool when applicable.
For modern Machinery jobs, providers supply the appropriate JobOrderID,
OutputInformation SerialNumber and ItemNumber as the corresponding descriptive
fields; retained-node references are only created for actual retained entities.

An existing forecast retains its selected type, QName and optional schema.
Use a new forecast identity when those selections change. Prediction values,
descriptions and references to other represented entities can change without
reconstructing the generated forecast node.

## GMS and vendor factories

`CreatePrognosisAsync` returns
`MachineToolEquipmentNode<PrognosisState>`. For standard kinds, its actual
generated state must derive from the selected standard type. For a custom
companion kind, explicitly select `Companion`, provide `CompanionTypeId`, and
supply its typed generated factory.

This supports a true generated GMS `CalibrationPrognosisState` while retaining
its type, original QName and calibration-specific children. The generic module
updates `PredictedTime`; the GMS provider/factory owns its additional calibration
observations. A companion-only selection does not by itself claim the standard
MachineTool Prognoses Server Facet, which requires a standard prognosis subtype.

Factories operate on unpublished nodes and must not mutate a live parent.
Owned resources are drained on rejected staging, dynamic removal and shutdown.
The same resource must not be transferred repeatedly.

## Publication, lifecycle and clients

Dynamic forecasts reuse the equipment inventory transaction and the existing
node manager lifecycle. There are no duplicate subscription or event protocols.
The list is an event notifier, has an optional read-only namespace-zero
`NodeVersion` when dynamic, and reports standard model-change events through
both its own notifier and the containing machine.

Removal awaits actual final-value inclusion in server Publish responses before
deleting nodes. Cancellation or a factory failure before commit leaves all
monitoring, production, tools and forecasts at their accepted observation.
After commit, publication failures are retried through
`MachineToolRuntime.RetryPublicationAsync`, retaining the event identity.
Callbacks cannot await reentrant mutation or disposal of their own operation.

`MachineToolClient.ReadPrognosesAsync` uses generated notification/list proxies,
the shared bounded browser, and original raw `DataValue` reads. Its result
preserves actual type IDs and QNames, the predicted date's quality/source time,
and optional-member presence. It follows and releases continuation points and
cancels outstanding reads when the facade is disposed without taking ownership
of an application-owned session.

## Conformance evidence

| Conformance unit | Implemented behavior and retained tests |
| --- | --- |
| MachineTool PrognosisType | All ten standard variants, provider times, actual affected entities, original optional presence; `AllStandardPrognosesKeepProviderTimesAndTypedWireFieldsAsync`, `PrognosesReferenceActualProspectiveGmsProductionAndToolsAsync` |
| MachineTool Prognoses Dynamic List | Bounded add/reorder/remove, versions/events, final delivery, rollback, cancellation, real resource draining; prognosis lifetime and validation tests |
| MachineTool Event Prognoses | Every enabled prognosis list is subscribable; actual list/root Publish event delivery is asserted by `DynamicForecastRemovalDeliversFinalPredictionAndDrainsOwnedObservationAsync` |
| MachineTool Event Propagation, shared behavior | Root and nested event delivery use the existing parent/notifier mechanism once, verified by equipment and prognosis real-session tests |

Negative tests reject invalid times, fields, bounds, capabilities, ambiguous or
cross-instance targets, removed targets and factory failures. These mappings do
not claim independent certification or completion of unrelated MachineTool/GMS
features.
