# OPC UA GMS result server

Result-management bindings for the released OPC 40210 model (reference edition
1.0.1, NodeSet version 1.0.0). This package composes an existing generated
`GMSState` and its `GMSResultManagementState` with the Machinery/DI owner; it does
not register a second root or implement another result-transfer protocol.

The bounded result slice covers GMS result variables, characteristics and
tolerances, calculated corrections, intermediate-result events, and the shared
Machinery store/query/metadata-handle/file services. Providers are per instance.
Optional structures are configured explicitly. Correction values describe
calculated deviations; they do not issue a machine correction command.

This package's result binding alone is not a complete GMS machine builder or
an assertion of GMS Basic, Production or security-profile conformance.

## Bind an existing GMS instance

Load `GmsModelProvider` through the existing Machinery owner's
`AddMachineryModel<GmsModelProvider>()` registration. Select
`MachineryParts.BuildingBlocks | MachineryParts.Result`; the GMS provider adds
the canonical dependency closure without selecting WMTP, PADIM or ProcessValues.
The application or a higher-level companion builder supplies the already-staged
`IMachineBuilder<GMSState>`, including its required non-result configuration.

```csharp
using Opc.Ua.GMS;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.GMS.Server;
using Opc.Ua.Machinery.Server.Builders;

static async ValueTask<GmsResultPublisher> BindResultsAsync(
    IMachineBuilder<GMSState> stagedMachine,
    IGmsResultStore instanceStore,
    CancellationToken cancellationToken)
{
    IMachineHandle<GMSState> machine = await stagedMachine
        .WithGmsResults(instanceStore, out GmsResultBinding results,
            new GmsResultBindingOptions
            {
                PublishedResults = 4,
                IncludeUsedTools = true,
                EnableIntermediateEvents = true
            })
        .BuildAsync(cancellationToken);
    return results.CreatePublisher(machine);
}
```

`WithGmsResults` adopts the existing `GMSResultManagementState`; it does not add
another result-management object or another root registration. The registration
owner also owns the binding's shutdown. The returned publisher cannot attach to
another machine. The same provider cannot back two active GMS bindings.

`IGmsResultStore` belongs to the model package and is injectable. Supply a
separate provider per instance, directly or from an application-owned provider
factory. There is deliberately no fallback to an ambient global result store.
Missing providers fail even when all optional result features are disabled.

`InMemoryGmsResultStore` is a bounded single-host reference implementation.
It rejects overflow rather than silently evicting data. Applications control
retention with `RemoveAsync`; distributed deployments should implement their own
transactional provider and retention policy. Returned `GmsResult` values are
immutable snapshots; `ResultDataType`, raw samples, units and file buffers are
copied at the boundary.

## Measurements and calculated corrections

A `GmsResult` contains the unchanged Machinery `ResultDataType` envelope inside
a real `DataValue`. Result variables retain its quality and acquisition time,
including when the fixed ring rotates after a backing-store entry is removed.
Used-tool identifiers belong to the optional `GMSResultType.UsedTools` property,
not to an invented metadata structure.

Set `isIntermediate: true` and supply at least one `GmsCharacteristic` to emit a
concrete subtype of `IntermediateResultEventType`. Final results emit a different
concrete subtype of the Machinery `ResultReadyEventType`. Intermediate metadata
sets `IsPartial`. Characteristics preserve independent geometric validity,
tolerance evaluation, importance class, formula, engineering units, and each
configured limit's mandatory `ToleranceForm`. Neither evaluation nor validity is
inferred from UA data quality.

`GmsResultBindingOptions.Corrections` configures the optional correction folder.
Null omits it; an empty array creates an explicitly empty folder. Each
`GmsCorrection` requires its identifier, characteristic reference, and at least
one relative or absolute value. `PublishCorrectionAsync` updates the configured
monitoring values without adding nodes or issuing a machine-control command.
Changing an identifier's characteristic reference or optional-node shape
requires rebuilding that instance.

These APIs implement defined result behavior only. The undefined
`GMS_ProductionStatisticsType` mentioned in the Production conformance unit is
not fabricated, and this result binding does not advertise that facet.
