# OPC UA Geometric Measurement Systems server

Provider-backed systems and results for OPC 40210 reference edition 1.0.1,
using the normative released model 1.0.0. Native GMS types compose with
MachineTool and Machinery services on one DI/Machinery owner, without a second
root or duplicate protocol.

## Native systems

Use `AddGms` / `ConfigureGms` for standalone hosting, or `AddGmsToMachinery` /
`ConfigureGmsFor<TNodeManager>` with an existing compatible owner. Its
MachineTool/Result dependency closure does not enable WMTP.

Every system requires explicit identification, `IGmsProvider` and
`IGmsResultStore`. Inside an awaited configuration callback:

```csharp
using Opc.Ua;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.GMS.Server;

var provider = new InMemoryGmsProvider(new GmsSystemSnapshot());
var store = new InMemoryGmsResultStore(context.Context.AsMessageContext());
GmsHandle system = await context.AddGms("InspectionCell")
    .WithProvider(provider)
    .WithIdentification(identity =>
    {
        identity.Manufacturer = new LocalizedText("Example manufacturer");
        identity.SerialNumber = "CMM-001";
        identity.ProductInstanceUri = "urn:example:inspection:CMM-001";
    })
    .WithResults(store)
    .BuildAsync(cancellationToken);
```

Keep the handle for acquisition. Initial observations are unavailable, not
fabricated measurements. Options select native sensors/multi-sensors,
workspaces, loading/tool monitoring, accessories/additional sensors,
calibration, production and warnings.

Qualification is remaining Double hours with Qualified/Time/IsCountingUp=false.
SensorWarningAlarmType uses an additional environmental sensor's actual
measurement, not primary probe qualification. Separate inherited alerts can
represent other application conditions.

`RefreshAsync` shares MachineTool's prepare/commit/publication transaction.
Rejected acquisitions do not replace accepted indexes. `RetryPublicationAsync`
retries retained publication, not equipment effects.

`system.Results` provides result variables, characteristics/tolerances,
corrections, intermediate events and shared store/query/metadata/file
workflows. Optional behavior is explicit; the default system binding does not
enable file transfer. Correction values are observations, not commands.

## Advanced result binding

Load `GmsModelProvider` through the existing Machinery owner's
`AddMachineryModel<GmsModelProvider>()` registration. Select
`MachineryParts.BuildingBlocks | MachineryParts.Result`; the GMS provider adds
the canonical dependency closure without selecting WMTP, PADIM or ProcessValues.
The application or a higher-level companion builder supplies the already-staged
`IMachineBuilder<GMSState>`, including its required non-result configuration.

```csharp
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
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

The advanced result binding alone is not a full GMS system. The undefined
`GMS_ProductionStatisticsType` mentioned in the Production conformance unit is
not fabricated, and the complete GMS Production facet is withheld. Defined
GMS jobs/parts and inherited statistics remain implemented. GMS adds no
measurement-start, calibration-execution, GMS job-start or correction-control
protocol.

See the [GMS guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/GMS.md)
for complete system interfaces, lifetimes and the upstream conformance exception.
