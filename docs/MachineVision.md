# Machine Vision (OPC 40100-1 and OPC 40100-2)

The existing `Opc.Ua.Vision`, `Opc.Ua.Vision.Server`, `Opc.Ua.Vision.Client`,
and `Opc.Ua.Mcp.Vision` packages include the released Machine Vision models.
They coexist with the provisional perception model documented in [Vision](Vision.md).
No existing draft NodeId, provider interface, or default `AddVision()` address space is replaced.

## Models and provenance

| Model | CLR namespace | Wire namespace | Loader |
| --- | --- | --- | --- |
| Provisional perception model | `Opc.Ua.Vision` | `http://opcfoundation.org/UA/Vision/` | `AddOpcUaVision` |
| OPC 40100-1, specification 1.0 | `Opc.Ua.Vision.MachineVision` | `http://opcfoundation.org/UA/MachineVision` | `AddOpcUaVisionMachineVision` |
| OPC 40100-2, specification 1.00.0 | `Opc.Ua.Vision.MachineVision.AMCM` | `http://opcfoundation.org/UA/MachineVision/AMCM/` | `AddOpcUaVisionMachineVisionAMCM` |

Namespace URIs are exact identifiers: Part 1 has **no trailing slash**. Part 2 has one.
Part 1 depends on UA core only. Part 2 depends on UA core, Device Integration,
and Machinery, **not Part 1**. Managed package references include the existing DI and
Machinery producers; enabling one model does not load the other released model.

The original, licensed NodeSets and identifier tables are retained under
[`src/Opc.Ua.Vision/Model`](../src/Opc.Ua.Vision/Model). They are not rewritten to
match the provisional model. Repository-compatible identifier guards are derived
with the existing `tools/nodesets/derive-identifier-table.py` utility.

| Part | Upstream release commit | NodeSet version and publication | SHA-256 |
| --- | --- | --- | --- |
| 1 | [`ba1b53eedd87da6ee9c79ef76e85f9f1d7bff0d0`](https://github.com/OPCFoundation/UA-Nodeset/tree/ba1b53eedd87da6ee9c79ef76e85f9f1d7bff0d0/MachineVision) | `1.0.0`, `2019-07-11T10:18:27Z` | `81B77250E5DE8D4FCCEDA7E05FD32BF199299CAAC32D15DF8F1FE25ED907C053` |
| 2 | [`5cbfb6506684ffba82610ea50a95927389f46b8e`](https://github.com/OPCFoundation/UA-Nodeset/tree/5cbfb6506684ffba82610ea50a95927389f46b8e/MachineVision/AMCM) | `1.00.0`, `2024-05-17T00:00:00Z` | `C70ED87915AB9A9F72F471C1B852A16EF7596921C0937A93FE2C3F73A6C0EC1F` |

The document publication date for Part 1 is 2019-08-01, distinct from its NodeSet date.
Part 2's required floors are DI 1.04.0 and Machinery 1.03.0; the implementation
reuses the repository's DI 1.05.0 and Machinery 1.04.1 models and their dependency closure.
Generated states, method signatures, structures, proxies, and server fluent accessors
come from those model producers rather than hand-maintained copies.

## Opt-in hosting

`VisionServerOptions.MachineVisionParts` defaults to `None`. Select `Control`,
`Assets`, or `All` independently. This example assumes an application implementation
of `IMachineVisionOperationsProvider` has been registered with DI:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.Vision;
using Opc.Ua.Vision.Server;

builder.Services.AddSingleton<IMachineVisionOperationsProvider, CameraOperations>();
builder.Services.AddOpcUa()
    .AddServer(options => options.EndpointUrls.Add("opc.tcp://localhost:62550/MachineVision"))
    .AddVision(options => options.MachineVisionParts = MachineVisionParts.All)
    .ConfigureVision(async (context, ct) =>
    {
        MachineVisionSystemHandle system = await context.AddMachineVisionSystemAsync(
            "Inspection", context.GetRequiredService<IMachineVisionOperationsProvider>(),
            cancellationToken: ct);

        MachineVisionAssetBuilder asset = context.AddMachineVisionAsset("InspectionAsset")
            .WithIdentification(new MachineryIdentificationData
            {
                Manufacturer = new LocalizedText("Example manufacturer"),
                SerialNumber = "SYSTEM-001",
                ProductInstanceUri = "urn:example:vision:system-001"
            })
            .RepresentsSameSystem(system.State);

        asset.AddItem(MachineVisionItemKind.ImageSensor, "Camera")
            .WithIdentification(new MachineryIdentificationData
            {
                Manufacturer = new LocalizedText("Example manufacturer"),
                SerialNumber = "CAMERA-001"
            })
            .WithHealth(health => health.Set("Temperature", Variant.From(22.5)));
        await asset.BuildAsync(ct);
    });
```

For an existing Machinery host, use **one owner** for DI and Machinery:

```csharp
builder.Services.AddOpcUa()
    .AddServer(options => options.EndpointUrls.Add("opc.tcp://localhost:62550/MachineVision"))
    .AddMachinery(options => options.Parts = MachineryParts.BuildingBlocks)
    .AddVisionToMachinery() // Selects both released parts by default.
    .ConfigureVisionFor<MachineryNodeManager>(async (context, ct) =>
    {
        await context.AddMachineVisionSystemAsync(
            "Inspection", context.GetRequiredService<IMachineVisionOperationsProvider>(),
            cancellationToken: ct);
    });
```

Do not add a second standalone Vision manager with assets to a DI/Machinery-owned server.
`ConfigureVisionOnMachinery<TNodeManager>` supports an application-owned DI manager
that loads the required models through its Machinery model-provider pipeline.
Such a custom manager follows Machinery's existing `DisposeMachineryResourcesAsync`
contract. The stock manager handles disposal itself.

Direct construction is also supported: call `MachineVisionSystemHandle.CreateAsync`
or construct `MachineVisionAssetBuilder` with an `IVisionBuildContext`. A directly
owned runtime is `IAsyncDisposable`; hosted contexts register its lifetime with their owner.
Factories validate model availability and instance names. Application instance namespaces
must remain distinct from the three model namespaces.

## Control and acquisition providers

The runtime owns OPC UA state, method validation, document metadata, transfer handles,
events, and conditions. `IMachineVisionOperationsProvider` owns hardware behavior and
opaque payload interpretation:

| Provider member | Responsibility |
| --- | --- |
| `ControlAsync` | Apply Halt, Reset, mode selection, Stop, Abort, and simulation changes before the state transition is committed. |
| `PrepareRecipeAsync` | Prepare or unprepare committed content; completion means the operation has finished. |
| `ActivateConfigurationAsync` | Apply a committed configuration before it becomes active. |
| `ValidateDocumentAsync` | Accept or reject uploaded bytes before the store commit. |
| `StartJobAsync` | Validate and accept the proposed job context. |
| `ExecuteAsync` | Yield acquisition notifications and optional partial/final results; ending the stream completes the job. |
| `ExecuteStepAsync` | Perform one externally synchronized step and report whether another step is required. |

All callbacks are asynchronous. Commands for one system are serialized; its execution
stream can run concurrently with control calls. Providers must honor cancellation and
must not re-enter a serialized command from inside that command. Stop, Abort, Halt,
Reset, and runtime disposal cancel the stream. Results from superseded executions are
not published into a new job.

`MachineVisionOperationResult` keeps **service status and domain error separate**.
Domain zero means success; negative values are application errors; positive values are
reserved by the specification. A successful UA Call response can therefore still contain
a failed domain result. Generated proxies preserve that result; workflow helpers use
`MachineVisionSystemClient.ThrowIfDomainError`.

A new system starts in `Preoperational`. `SelectModeAutomatic` enters `Initialized`;
provider readiness or a prepared recipe permits `Ready`. `InitiallyReady` and
`SetReadyAsync` support providers that do not expose recipe management. Reset clears
provider readiness and prepared-recipe flags; it does not erase stored recipe content.
No call implicitly selects automatic mode, prepares a recipe, or activates a configuration.

`MachineVisionSystemOptions.Features` selects optional families; its default for a
newly created native system is `All`. Control remains available with `None`.
The families are Recipes, Configurations, Results, Folders, FileTransfer, Steps,
Simulation, Safety, and Diagnostics. Namespace selection and per-system functionality
selection are separate decisions.

### Step models, events, and conditions

The seven parent state numbers are 1 Preoperational, 2 Halted, 3 Error,
5 Initialized, 6 Ready, 7 SingleExecution, and 8 ContinuousExecution.
`SetStepSequenceAsync(number, true)` enables a sequence for that parent.
`Sync` executes a waiting step. `SynchronizeStepAsync` provides the equivalent
application-side trigger using `WaitToStepAuto`. Both use the same provider, index,
and state/event implementation. An initialized sequence must finish before automatic
readiness; an execution sequence must finish before its acquisition stream begins.
An in-progress sequence or running job cannot be reconfigured.

Use `ReportErrorAsync` for application errors. Acknowledgement and confirmation are
independent of resolving the physical error: an active error stays retained after
acknowledgement/confirmation. Resolving the final blocking error returns an errored
system to Preoperational. Persistent errors also require acknowledgement and confirmation
after their physical cause is remedied. ConfirmAll applies the same recovery decision
as individual Confirm calls, including physically resolved conditions awaiting confirmation.
Reset may leave Error for Preoperational but cannot re-enable production while a blocking
condition remains unresolved.
`ReportWarningAsync` provides the same acknowledgement/optional-confirmation lifecycle
without interrupting production. Diagnostic, information, warning, error, and persistent-error
events retain their distinct severity bands; a method diagnostic identifies the failed method
as its SourceNode without exposing argument payloads.

State transitions, readiness, job starts, acquisitions, results, recipe preparation,
step entry/exit, errors, and safety changes use the model's event types. Failed bound
methods, including argument validation rejected before the handler, produce a warning
diagnostic. Argument payloads are not copied into diagnostic text. Diagnostic information
obeys the selected level; `ReportInformationAsync` and `SetSystemStateAsync` provide
application-side reporting.

The shared `MethodState.OnCallValidationFailedAsync` callback observes asynchronous
validation refusals without changing their service result or per-argument errors.
It is not invoked by legacy synchronous Call; default method behavior is unchanged
when no observer is installed.

`ReadyEventType` signals the end of a single or continuous execution, including
Stop and Abort, and carries that job's identifier and the automatic state machine's
SourceNode. Initial readiness is a state transition, not a completed-job event.

Safety reporting is **not a safety controller or safety certification**. It must not
replace an independently certified safety circuit.

## Recipes, configurations, results, and files

All management methods are available through the generated sub-clients and the server
runtime: recipe registration/preparation/removal, product links and preparation,
configuration registration/activation/removal, result retrieval/components/filtering,
and metadata-handle release. Optional folders expose the same store-backed records.
Explicit internal recipe identifiers take precedence over external identifiers.
Version and hash restrictions are preserved; result states remain extensible Int32 values.

Preparation and activation record durable intent before invoking the hardware provider.
A storage refusal therefore does not change the hardware's accepted state. Provider
refusals restore the previous store record before returning; published active/readiness
values change only after success. If that rollback also fails, production is blocked
and the failure reports both causes. Repair the store and recreate the runtime rather
than treating Reset as a persistence repair. On restart, saved intent is reapplied
through the provider before it becomes available to clients.

Registering metadata and transferring content are separate operations. AddRecipe and
AddConfiguration report whether transfer is required. A temporary writer is already
open when GenerateFileForWrite returns. CloseAndCommit validates and publishes content;
ordinary Close discards an uncommitted temporary writer. Result transfers are read-only.
The direct recipe FileType uses commit-on-close. These are **OPC 40100-1 protocols**,
not replacements with the different OPC 40001-101 result protocol.

File bindings reuse the stack's session-bound file-handle implementation, with independent
reader positions, one writer, bounded files, and owned cleanup. The default per-system
limits are 64 metadata handles, a five-minute metadata retention bound, eight temporary
transfers, 16 MiB per transferred file, and a two-minute transfer inactivity timeout.
Client metadata timeouts are retention hints, not request deadlines. Pagination accepts
zero MaxResults for all remaining results; a nonzero MaxResults requires a StartIndex
that is a multiple of that page size.

### Storage

`IMachineVisionStore` is injectable per system. The default `InMemoryMachineVisionStore`
clones metadata/content and limits storage to 1,024 documents and 64 MiB of opaque bytes.
Capacity exhaustion is explicit; records are not silently evicted.

`FileSystemMachineVisionStore` uses an existing `IFileSystemProvider`, not ambient file
paths. It stores namespace-aware binary records, hashes logical identifiers into safe
filenames, stages replacement data, and recovers an interrupted replacement from its
previous version. It restores prepared recipes and the active configuration through the
provider when a runtime is rebuilt. The caller owns an explicitly supplied persistent
store and its provider.

The reference persistent store requires an **exclusive root on one host**. It is not a
distributed database, consensus implementation, or multi-writer HA store. An application
needing those guarantees supplies its own `IMachineVisionStore` rather than sharing this
root across servers.

## Part 2 assets and live monitoring

`MachineVisionAssetBuilder` covers all 25 component categories:
computing devices, displays, physical interfaces, image sensors, frame grabbers,
lenses, lens controllers, optical filters, other optical equipment, way encoders,
trigger sensors, lamps, lighting controllers, pattern generators, calibration targets,
acquisition backgrounds, surrounding environment, housings, motion devices, power
supplies, climate controllers, licenses, software components, network devices, and cables.

Every item has IVisionInfo identification. Unused category folders are omitted; a
materialized category must satisfy its MandatoryPlaceholder. Manufacturer and serial
number are required; system assets also require an absolute ProductInstanceUri.
`WithComponents` exposes the same items through Machinery's Components add-in.
Assets are discoverable through Machinery's Machines folder.

`WithMaintenance`, `WithHealth`, `Child`, `Set`, and `Value` reach declared, typed optional
members, including warranty, service/calibration dates, firmware, maintenance records,
counters, remaining lifetime, temperature and engineering units, DI DeviceHealth,
DeviceHealthAlarms, and the Part 2 SEMI E10 state structure. Undeclared names or incompatible
Variant types are rejected instead of creating arbitrary vendor properties.

```csharp
await assetHandle.UpdateAsync(asset => asset
    .WithMaintenance(maintenance => maintenance
        .Set("CalibrationNeeded", Variant.From(true)))
    .WithHealth(health => health.Set("Temperature", Variant.From(24.0))), ct);
```

Updates register newly materialized optional members and notify normal OPC UA data
subscriptions. The callback edits an isolated snapshot: an invalid value or a thrown
callback leaves the published values and children unchanged. Existing node identities
and types cannot be replaced by a telemetry update. Retain the asset handle, not a
snapshot builder captured inside an update callback. Hosted contexts own disposal;
directly owned asset handles must be disposed asynchronously.
Application history and maintenance-alarm providers remain the existing
stack's [Historical Access](HistoricalAccess.md) and alarm/DI integration, not a second
vision-specific historian. Populate only measurements and maintenance facts your provider
actually knows.

`AddImageHandlingAspect` creates receiver, transmitter, or transceiver aspects.
`HasOpticalPathTo` and `TransmitsDataTo` describe topology. Existing generated/vendor
components can be included without replacing their TypeDefinition. Already parented or
registered components are linked rather than reparented or deleted with a new asset;
explicit fluent value configuration on those components is live.
Part 2 defines no control methods.

## Shared identity, not duplicate execution

Part 1 VisionSystemType and Part 2 VisionSystemAssetType remain distinct objects.
`RepresentsSameSystem` adds the required `RepresentsSameEntityAs` relationship when
they describe the same physical system. Use the same reference for deliberately associated
draft sensors/components; matching names alone do not establish equivalence.

The [server sample](../samples/Vision/MachineVisionServer) routes native commands and
the draft inference pipeline through **one deterministic engine**. One acquisition
publishes both result representations with the same result identity. It does not run
an inspection twice to fill two models.

Part 1's SEMI E10 enum/description and Part 2's Id/Priority structures are different
wire types. Map application facts deliberately; do not cast between them or equate
their numeric values.

## Typed clients

Use the existing `AddVisionClient()` DI factory or `session.Vision(telemetry)`:

```csharp
VisionClient vision = session.Vision(telemetry);
MachineVisionClient native = vision.MachineVision();
await foreach (VisionNodeEntry entry in native.EnumerateSystemsAsync(cancellationToken: ct))
{
    MachineVisionSystemClient system = native.System(entry.NodeId);
    VisionStateMachineTypeClient control = await system.ControlAsync(ct);
    FiniteStateSnapshot state = await control.GetCurrentFiniteStateAsync(ct);
}
```

Control and asset capability checks are independent; released-model discovery does not
require the provisional namespace. Discovery recognizes subtypes and deduplicates graph
visits. Asset snapshots retain browse-name namespaces, NodeIds, statuses, timestamps, and
an explicit IsComplete flag when a depth/value limit is reached.

The Control, Automatic, Recipes, Configurations, Results, and Safety sub-clients are the
generated proxies, including optional child accessors and all declared method arguments.
Use their existing state-machine and streaming-subscription extensions for observation.
`UploadRecipeAsync`, `UploadConfigurationAsync`, and `DownloadResultAsync` reuse the
standard temporary-file client. Uploads wait for a returned asynchronous completion state
machine and surface its Error state; cancellation bounds the wait. Downloads release
metadata handles and preserve both errors if transfer and release fail.

The [client sample](../samples/Vision/MachineVisionClient) demonstrates secure discovery,
upload, preparation, execution, state waiting, results, the draft/native bridge, and assets.

## MCP

The Vision, Full, and composed profiles add these eleven tools without renaming existing
draft tools:

| Tool | Purpose |
| --- | --- |
| `vision_machine_list` | Discover released control systems or assets. |
| `vision_machine_read_state` | Read control and automatic states. |
| `vision_machine_command` | Explicit control, simulation, synchronization, or ConfirmAll. |
| `vision_machine_read_asset` | Read bounded, namespace-qualified inventory/health values. |
| `vision_machine_recipes` | Recipe/product management and metadata handles. |
| `vision_machine_configurations` | Configuration management and metadata handles. |
| `vision_machine_results` | Result metadata and handle release. |
| `vision_machine_report_safety` | Report safety information. |
| `vision_machine_file` | Explicit transfer and FileType operations. |
| `vision_machine_recipe_object` | Per-recipe preparation and product links. |
| `vision_machine_condition` | Condition acknowledgement, confirmation, and comments. |

Metadata pages are limited to 100 entries, asset snapshots to 1,024 values,
file chunks to 1 MiB, and serialized responses to 2 MiB of UTF-8 JSON. Oversized responses
fail explicitly; request a smaller page, narrower asset root, or file chunks.
The tools never acquire a new mode or retry mutations as a discovery side effect.
`vision_machine_command` accepts a `parametersJson` object containing a UA Variant
`Parameters` array. Recipe/configuration tools accept `identifierJson` for complete
binary identifiers, including version and hash metadata. The result tool's
`filtersJson` accepts the typed `MeasId`, `PartId`, recipe/configuration identifiers,
`ProductId`, and `JobId`; its `Components` operation exposes every result component.
These inputs use the same UA JSON representation written by `JsonEncoder`, with
a one-MiB input bound. Conflicting scalar and structured identifier selectors are rejected.
Keep mutating tool access behind the host's normal session/authentication policy.
MCP tool-catalog NativeAOT limitations are those documented by the existing MCP host;
the UA model encoders do not require reflection-based JSON serialization.

## Implementation and conformance map

This map identifies implementation surfaces; it is **not a certification claim**.
The specification's server/client facets and Part 2 VSA bundles are selected by actual
application functionality, not merely by loading a NodeSet. No unverified OPC Foundation
profile URI is invented or added to ServerProfileArray. The draft VIS-* facets remain
the draft's separate reporting mechanism.

The handles implement `IConformanceContributor`. Their specification-named units use
the released model's namespace index to avoid collisions with generic names such as
Automatic Mode. Standalone Vision and composed Machinery hosts aggregate these units
through their existing server-capabilities pipeline. Part 2 units are derived from
published instances, optional add-ins, and optical/data references; empty or rolled-back
assets contribute nothing. Provider-dependent processing-time claims are not inferred,
and the model's cable category does not acquire invented cable-specific units absent
from Part 2 table 74.

| Requirement family | Implementation | Representative regression evidence |
| --- | --- | --- |
| Released model identity and defaults | `MachineVisionModels`, model inputs, `VisionServerOptions` | `MachineVisionModelTests` |
| Control, job identity, results | `MachineVisionSystemHandle`, state tables, operations provider | `AutomaticModeRunsOneJobPublishesResultAndReturnsReadyAsync` |
| Initialization and synchronization | Steps partial and all seven slots | `InitializedStepSequenceDelaysReadyUntilSyncAsync`, `ApplicationSynchronizationUsesAutomaticStepTransitionAsync` |
| Reset and selected optional surfaces | Control/options | `ResetClearsProviderReadinessAsync`, `DisabledDiagnosticsCannotMaterializeSystemStateAsync` |
| Error payload, acknowledgement, confirmation | Events partial and core condition retention provider | `ReportedErrorPreservesApplicationDetailsAsync`, `AcknowledgementUsesReportedEventIdAndRetainsActiveErrorAsync`, `PersistentErrorPublishesCallableConfirmationAsync` |
| Failed-call diagnostics | Bound methods and MethodState validation observer | `MissingControlArgumentsPublishFailureDiagnosticAsync` |
| Recipe preparation and file commit/abort | Management, Documents, Transfers, `IFileTypeBinder` | `RecipeTransferCommitsContentThenPreparationEnablesReadyAsync`, `ClosingAnUncommittedRecipeTransferDiscardsItsBytesAsync` |
| Result version filters | Management and generated result proxy | `ResultFilterHonorsExternalRecipeVersionAsync` |
| Session-owned readers/writer | Shared FileType binding | `SharedFileBindingIsolatesReadersAndCommitsOneWriterAsync` |
| Bounded and persistent storage | Memory and file-system stores | `MemoryStoreClonesMetadataAndRefusesCapacityOverflowWithoutEvictionAsync`, `FileSystemStoreSurvivesRestartAndRecoversAnInterruptedReplacementAsync` |
| Part 2 categories, identity, and health | Asset and declared-member builders | `AssetBuilderPublishesEveryComponentCategoryAndLiveHealthAsync` |
| Component sharing and root identity | Reference-based adoption and root-name reservation | `AssetsCanShareAComponentWithoutReparentingOrReplacingItAsync`, `TwoStagedAssetsCannotPublishTheSameRootNameAsync` |
| Machinery, both models, and TCP workflows | Hosting adapter and typed clients | `MachineVisionRuntimeTests.Integration.cs` |
| MCP registration and bounds | `VisionMachineTools`, profile extensions | `VisionMachineToolsTests`, `McpToolProfileCompositionTests` |

The source generator also has regressions for recursive optional declarations, a
ResultState child/type name collision, and narrowed proxy method signatures. Those
fixes preserve the upstream models rather than modifying their declarations to compile.

## References

- [OPC 40100-1](https://reference.opcfoundation.org/specs/OPC-40100-1)
- [OPC 40100-2](https://reference.opcfoundation.org/specs/OPC-40100-2)
- [RepresentsSameEntityAs](https://reference.opcfoundation.org/specs/OPC-10000-23/4.8)
- [Machinery hosting](Machinery.md), [model dependencies](ModelDependencies.md),
  [Vision](Vision.md), and [MCP hosting](McpServer.md)
