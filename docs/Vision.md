# Machine Vision (OPC 40100-1 and OPC 40100-2)

The Vision package family provides Machine Vision control and management (Part 1),
asset management and condition monitoring (Part 2), and the Perception model for
sensors, inference, media, and coordinate frames. Applications can use the models
independently or combine them on one OPC UA server.

Part 1 and Perception contracts are in `Opc.Ua.Vision`; Part 2 contracts are in
`Opc.Ua.MachineVision`. Each has client and server packages. One
`Opc.Ua.Mcp.Vision` package exposes tools for all three models. The generated
Perception types live in `Opc.Ua.Vision.Perception` and use the wire namespace
`http://opcfoundation.org/UA/Vision/`.

The model, client, server, Machinery, and MCP packages follow the root release
version. Development builds inherit the root preview version. The optional
`Opc.Ua.Vision.OpenUsd` renderer is preview-only. Package version eligibility and
a model's specification status are separate: the Perception model is provisional.

## Contents

- [Models and provenance](#models-and-provenance)
- [Opt-in hosting](#opt-in-hosting)
- [Control and acquisition providers](#control-and-acquisition-providers)
- [MCP tools for Machine Vision](#mcp)
- [Perception model](#perception-model)
- [Packages](#packages)
- [Perception hosting](#minimal-hosted-server)
- [Perception client interfaces](#using-the-client-libraries)
- [Perception MCP tools](#mcp-tools)
- [Bin-picking sample](#sample-bin-picking)
- [Visual inspection](#visual-inspection-a-cross-companion-cell)

## Models and provenance

| Model | CLR namespace | Wire namespace | Loader |
| --- | --- | --- | --- |
| Perception model | `Opc.Ua.Vision.Perception` | `http://opcfoundation.org/UA/Vision/` | `AddOpcUaVisionPerception` |
| OPC 40100-1, specification 1.0 | `Opc.Ua.Vision.MachineVision` | `http://opcfoundation.org/UA/MachineVision` | `AddOpcUaVisionMachineVision` |
| OPC 40100-2, specification 1.00.0 | `Opc.Ua.Vision.MachineVision.AMCM` | `http://opcfoundation.org/UA/MachineVision/AMCM/` | `AddOpcUaVisionMachineVisionAMCM` |

Namespace URIs are exact identifiers: Part 1 has **no trailing slash**. Part 2 has one.
Part 1 depends on UA core only. Part 2 depends on UA core, Device Integration,
and Machinery, **not Part 1**. The Part 2 package references the DI and Machinery model producers; selecting
one released model does not load the other.

Licensed, release-pinned NodeSets and identifier tables are under
[`src/Opc.Ua.Vision/Model`](../src/Opc.Ua.Vision/Model) for Part 1 and
[`src/Opc.Ua.MachineVision/Model`](../src/Opc.Ua.MachineVision/Model) for Part 2.
Repository-compatible identifier guards are derived with
`tools/nodesets/derive-identifier-table.py`.

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
`Assets`, or `All` independently. Part 2 builders require
`Opc.Ua.MachineVision.Server` and the `AddVisionMachinery()` registration for a
standalone manager. This example assumes an application implementation of
`IMachineVisionOperationsProvider` has been registered with DI:

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
    .AddVisionMachinery()
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

For a Machinery host, use **one owner** for DI and Machinery:

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

Adding a second standalone asset manager to a DI/Machinery-owned server throws an
`InvalidOperationException` during hosting registration: both managers would claim
the same model namespaces and shared roots. This restriction concerns **node-manager
ownership**, not the number of vision systems.

### Multiple vision systems

Create each system under the same owner with a unique browse name and its own
operations provider. For example, `LineOneOperations` and `LineTwoOperations`
implement `IMachineVisionOperationsProvider` for separate devices:

```csharp
builder.Services.AddSingleton<LineOneOperations>();
builder.Services.AddSingleton<LineTwoOperations>();
builder.Services.AddOpcUa()
    .AddServer(options => options.EndpointUrls.Add("opc.tcp://localhost:62550/InspectionLines"))
    .AddMachinery(options => options.Parts = MachineryParts.BuildingBlocks)
    .AddVisionToMachinery()
    .ConfigureVisionFor<MachineryNodeManager>(async (context, ct) =>
    {
        MachineVisionSystemHandle lineOne = await context.AddMachineVisionSystemAsync(
            "LineOne", context.GetRequiredService<LineOneOperations>(), cancellationToken: ct);
        MachineVisionSystemHandle lineTwo = await context.AddMachineVisionSystemAsync(
            "LineTwo", context.GetRequiredService<LineTwoOperations>(), cancellationToken: ct);

        await context.AddMachineVisionAsset("LineOneAsset")
            .WithIdentification(new MachineryIdentificationData
            {
                Manufacturer = new LocalizedText("Example manufacturer"),
                SerialNumber = "LINE-001",
                ProductInstanceUri = "urn:example:vision:line-one"
            })
            .RepresentsSameSystem(lineOne.State)
            .BuildAsync(ct);
        await context.AddMachineVisionAsset("LineTwoAsset")
            .WithIdentification(new MachineryIdentificationData
            {
                Manufacturer = new LocalizedText("Example manufacturer"),
                SerialNumber = "LINE-002",
                ProductInstanceUri = "urn:example:vision:line-two"
            })
            .RepresentsSameSystem(lineTwo.State)
            .BuildAsync(ct);
    });
```

Each handle owns its state machine, job state, documents, conditions, and transfers.
The owner supplies their shared lifetime and address-space registration. Duplicate
system/asset browse names are rejected with `BadBrowseNameDuplicated`. Applications
can deliberately share a provider or store, but must choose document identities and
hardware scheduling appropriate to that sharing.

`ConfigureVisionOnMachinery<TNodeManager>` supports an application-owned DI manager
that loads the required models through its Machinery model-provider pipeline.
Such a custom manager follows Machinery's `DisposeMachineryResourcesAsync`
contract. The stock manager handles disposal itself.

Direct construction is also supported: call `MachineVisionSystemHandle.CreateAsync`
or construct `MachineVisionAssetBuilder` with an `IVisionBuildContext`. A directly
owned runtime is `IAsyncDisposable`; hosted contexts register its lifetime with their owner.
For direct Part 2 setup, include `VisionMachineryStandaloneModelProvider` alongside
`VisionModelProvider` in the `VisionNodeManager` provider array.
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
It runs only when an observer is installed and is not invoked by synchronous Call.
Observer failures are logged without replacing the validation result; requested
caller cancellation propagates.

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

`FileSystemMachineVisionStore` uses `IFileSystemProvider`, not ambient file
paths. It stores namespace-aware binary records, hashes logical identifiers into safe
filenames, stages replacement data, and recovers an interrupted replacement from its
previous version. It restores prepared recipes and the active configuration through the
provider when a runtime is rebuilt. The caller owns an explicitly supplied persistent
store and its provider.
Recovery removes incomplete `.pending` records before reading or replacing a
document; enumeration also removes orphaned first writes. Cleanup runs under the
store's operation gate and supports the earlier `.pending-<id>` staging format.
Incomplete writes are never returned as committed documents.

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
Application history and maintenance-alarm providers use the
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

Use the `AddVisionClient()` DI factory or `session.Vision(telemetry)`:

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
require the Perception namespace. Discovery recognizes subtypes and deduplicates graph
visits. Asset snapshots retain browse-name namespaces, NodeIds, statuses, timestamps, and
an explicit IsComplete flag when a depth/value limit is reached.

The Control, Automatic, Recipes, Configurations, Results, and Safety sub-clients are the
generated proxies, including optional child accessors and all declared method arguments.
Use their state-machine and streaming-subscription extensions for observation.
`UploadRecipeAsync`, `UploadConfigurationAsync`, and `DownloadResultAsync` reuse the
standard temporary-file client. Uploads wait for a returned asynchronous completion state
machine and surface its Error state; cancellation bounds the wait. Downloads release
metadata handles and preserve both errors if transfer and release fail.

The [client sample](../samples/Vision/MachineVisionClient) demonstrates secure discovery,
upload, preparation, execution, state waiting, results, the draft/native bridge, and assets.

## MCP

The Vision, Full, and composed profiles expose these seventeen Machine Vision tools:

| Tool | Purpose |
| --- | --- |
| `vision_machine_list` | Discover released control systems or assets. |
| `vision_machine_read_state` | Read control and automatic states; opt into status and step details. |
| `vision_machine_command` | Explicit control, simulation, synchronization, or ConfirmAll. |
| `vision_machine_read_asset` | Read bounded, namespace-qualified inventory/health values. |
| `vision_machine_recipes` | Recipe/product management and metadata handles. |
| `vision_machine_configurations` | Configuration management and metadata handles. |
| `vision_machine_results` | Result metadata and handle release. |
| `vision_machine_report_safety` | Report safety information. |
| `vision_machine_file` | Explicit transfer/FileType operations and completion-state inspection/waiting. |
| `vision_machine_recipe_object` | Per-recipe preparation and product links. |
| `vision_machine_condition` | Condition acknowledgement, confirmation, and comments. |
| `vision_machine_capabilities` | Independent model support and actual optional interfaces/method executability. |
| `vision_machine_relationships` | Paged same-entity, optical, data-transmission, and interface references. |
| `vision_machine_observe` | Bounded event/condition collection with owned-subscription cleanup. |
| `vision_machine_upload_recipe` | Register/upload a complete small recipe and wait for commit, without preparation. |
| `vision_machine_upload_configuration` | Register/upload a complete small configuration, without activation. |
| `vision_machine_download_result` | Download complete small result content and release file/metadata handles. |

Metadata pages are limited to 100 entries, asset snapshots to 1,024 values,
file chunks to 1 MiB, and serialized responses to 2 MiB of UTF-8 JSON. Oversized responses
fail explicitly; request a smaller page, narrower asset root, or file chunks.
The tools never acquire a new mode or retry mutations as a discovery side effect.
`vision_machine_command` accepts a `parametersJson` object containing a UA Variant
`Parameters` array and an optional `ExternalRecipeId` structure for a version/hash-qualified
job recipe. Recipe/configuration tools accept `identifierJson` for complete
binary identifiers, including version and hash metadata. The result tool's
`filtersJson` accepts the typed `MeasId`, `PartId`, recipe/configuration identifiers,
`ProductId`, and `JobId`; its `Components` operation exposes every result component.
These inputs use the same UA JSON representation written by `JsonEncoder`, with
a one-MiB input bound. Conflicting scalar and structured identifier selectors are rejected.
Keep mutating tool access behind the host's normal session/authentication policy.
MCP tool-catalog NativeAOT limitations are those documented by the MCP host;
the UA model encoders do not require reflection-based JSON serialization.

### Discover, inspect, and observe

Start with `vision_machine_capabilities` without a system selector to distinguish
Part 1 and Part 2 namespace support. Supply a discovered `systemNodeId` to resolve
actual management, transfer, safety, and step objects and their instance methods.
Namespace presence does not imply that every optional interface is available.

`vision_machine_read_state` returns control and automatic states by default. With
`includeDetails: true`, it also returns `DiagnosticLevel`, `SystemState`, active
configuration and safety readings, plus available step states. Missing optional
values retain their resolution status rather than becoming zero or empty measurements.

`vision_machine_relationships` takes `relationship: "SameEntity"`, `"OpticalPath"`,
`"DataTransmission"`, or `"Interface"`, and `direction: "Forward"`, `"Inverse"`,
or `"Both"`. It returns at most 100 unique references and a `nextIndex`; offsets
are limited to 10,000. Expand one hop at a time. Separate pages are live views,
not a snapshot immune to concurrent topology edits.

For example, collect retained conditions and subsequent condition notifications:

```json
{
  "notifierNodeId": "ns=4;s=Inspection",
  "mode": "Conditions",
  "refreshConditions": true,
  "durationSeconds": 5,
  "maximumNotifications": 50
}
```

The observation call defaults to 5 seconds/50 notifications and rejects requests
over 60 seconds/100 notifications. It owns and disposes its subscription; other
callers' subscriptions and the session remain open. It uses the options-based
subscription engine available on MCP managed sessions.

The response includes `completionReason`, `refreshComplete`,
`droppedNotifications`, namespace-qualified `fieldPaths`, and each event's fields.
An empty field path selects the condition's NodeId attribute. Exact
`eventIdBase64` and `conditionNodeId` selectors can be passed to
`vision_machine_condition`. Observation never acknowledges or confirms anything.
`refreshComplete` records receipt of a refresh start/end pair, not a guarantee
that no notifications were lost: also inspect the completion reason and drop count.
Caller cancellation and setup/refresh failures remain errors, not empty successful results.

### Complete small-file workflows

For a recipe of at most 1 MiB, call `vision_machine_upload_recipe` with a system
NodeId, `externalId` (or complete `identifierJson`), optional `productId`, and
`dataBase64`. The configuration variant accepts the corresponding configuration
identifier. Both reuse the typed client workflow, transfer only when the server
requests it, and wait for asynchronous commit completion. Preparation and activation
remain separate explicit operations.

`vision_machine_download_result` accepts `systemNodeId`, `resultId`, and optional
`maximumBytes` (1-1,048,576). It either returns the entire file as a UA ByteString or
fails; it never returns truncated content. File and metadata handles are released
on success, cancellation, and download failure.

Larger content continues to use `vision_machine_file` chunks. After `Commit`, use
its returned completion-state-machine NodeId with `ReadCompletion` or
`WaitForCompletion`. A null completion NodeId means the commit completed synchronously.
The wait operation is capped at 60 seconds and rejects Error, unavailable state,
or timeout. It never retries a commit.

## Implementation and conformance map

This map identifies implementation surfaces; it is **not a certification claim**.
The specification's server/client facets and Part 2 VSA bundles are selected by actual
application functionality, not merely by loading a NodeSet. No unverified OPC Foundation
profile URI is invented or added to ServerProfileArray. The draft VIS-* facets remain
the draft's separate reporting mechanism.

The handles implement `IConformanceContributor`. Their specification-named units use
the released model's namespace index to avoid collisions with generic names such as
Automatic Mode. Standalone Vision and composed Machinery hosts aggregate these units
through their server-capabilities pipeline. Part 2 units are derived from
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
  [Perception model](#perception-model), and [MCP hosting](McpServer.md)

## Perception model

The Perception model describes sensor capture, inference pipelines, feedback,
results, media, and coordinate frames. Its CLR namespace is
`Opc.Ua.Vision.Perception`; its providers and fluent hosting use the Vision
client/server modules. It can share a server and hardware providers with the
released Machine Vision models.

> **Draft specification.** The Perception wire namespace
> `http://opcfoundation.org/UA/Vision/` and its NodeIds are provisional.
> Do not confuse its result/pose types and VIS-* facets with OPC 40100.

## Packages

| Package | What it gives you | Depends on |
|---|---|---|
| `OPCFoundation.NetStandard.Opc.Ua.Vision` | Source-generated Perception and OPC 40100-1 models, node states, typed proxies, and distinct model loaders | `Opc.Ua.Core` |
| `OPCFoundation.NetStandard.Opc.Ua.Vision.Server` | `VisionNodeManager`, `IVisionBuildContext`, Perception and Part 1 providers and builders, facet derivation, standalone or Machinery-composed hosting | `Opc.Ua.Vision`, `Opc.Ua.Server`, `Opc.Ua.Machinery.Server` |
| `OPCFoundation.NetStandard.Opc.Ua.Vision.Client` | `VisionClient` discovery, `VisionSensorClient`, `VisionPipelineClient`, `VisionResultReader`, `VisionMediaClient`, `VisionFrameGraph`, `VisionFeedbackClient`, `session.Vision(...)` extension, `AddVisionClient()` DI | `Opc.Ua.Vision`, `Opc.Ua.Client` |
| `OPCFoundation.NetStandard.Opc.Ua.MachineVision` | OPC 40100-2 asset models and generated proxies in `Opc.Ua.Vision.MachineVision.AMCM` | `Opc.Ua.Core`, `Opc.Ua.Di`, `Opc.Ua.Machinery` |
| `OPCFoundation.NetStandard.Opc.Ua.MachineVision.Server` | Asset builders, information add-ins, conformance, runtime updates, and Part 2 hosting registration | `Opc.Ua.Vision.Server`, `Opc.Ua.MachineVision`, `Opc.Ua.Machinery.Server` |
| `OPCFoundation.NetStandard.Opc.Ua.MachineVision.Client` | Part 2 asset discovery, snapshots, and relationship extensions in `Opc.Ua.Vision.Client` | `Opc.Ua.Vision.Client`, `Opc.Ua.MachineVision` |
| `OPCFoundation.NetStandard.Opc.Ua.Vision.OpenUsd` | `ISceneCameraCaptureProvider` implementation that renders a `UsdGeomCamera` offscreen and reports `NoRenderingBackend` gracefully when no graphics device is available | `Opc.Ua.Types`, native OpenUSD renderer payload (optional per-RID) |
| `OPCFoundation.NetStandard.Opc.Ua.Mcp.Vision` | 39 MCP tools: Perception plus Machine Vision control/asset workflows, with the `vision` bounded profile and composition entry point | `Opc.Ua.Mcp.Core`, `Opc.Ua.Vision.Client`, `Opc.Ua.MachineVision.Client` |

```mermaid
graph TD
    Model["Opc.Ua.Vision<br/>source-generated model"]
    Server["Opc.Ua.Vision.Server<br/>VisionNodeManager + builders"]
    Client["Opc.Ua.Vision.Client<br/>typed discovery and readers"]
    Assets["Opc.Ua.MachineVision<br/>Part 2 model"]
    AssetServer["Opc.Ua.MachineVision.Server"]
    AssetClient["Opc.Ua.MachineVision.Client"]
    OpenUsd["Opc.Ua.Vision.OpenUsd<br/>scene-camera capture provider"]
    Mcp["Opc.Ua.Mcp.Vision<br/>agent tools"]
    Media["IVisionMediaProvider"]
    Inference["IVisionInferenceProvider"]
    Feedback["IVisionFeedbackSink"]

    Model --> Server
    Model --> Client
    Client --> Mcp
    Assets --> AssetServer
    Assets --> AssetClient
    Server --> AssetServer
    Client --> AssetClient
    AssetClient --> Mcp
    OpenUsd -.->|captures frames for| Media
    Media -.->|plugs into| Server
    Inference -.->|plugs into| Server
    Feedback -.->|plugs into| Server
```

`Opc.Ua.Vision.OpenUsd` deliberately sits outside that dependency chain: it
depends on `Opc.Ua.Types` alone and knows nothing about the Vision server. It
offers a camera, and a host writes the `IVisionMediaProvider` that hands the
resulting frames to a pipeline — which is what
[`BinPickingCell`](../samples/Robotics/BinPickingCell) does. A host with a real
camera writes the same interface over its own SDK and never references OpenUSD
at all.

The runtime address space is rooted under the standard Server object.
Configurators add frames, sensors and pipelines through the fluent builder;
providers plug into the sensor or pipeline nodes that the builder creates.

```mermaid
flowchart TD
    ServerObj["Server"] --> VisionRoot["Vision<br/>VisionRootType"]
    VisionRoot --> Sensors["Sensors"]
    VisionRoot --> Frames["Frames"]
    VisionRoot --> Pipelines["Pipelines"]

    Frames --> Frame["CoordinateFrameType<br/>FrameId + Transform"]
    Sensors --> Sensor["VisionSensorType<br/>ImageSensorType / Depth3DSensorType"]
    Sensor --> Calibrations["Calibrations"]
    Calibrations --> Intrinsic["IntrinsicCalibrationType"]
    Calibrations --> Extrinsic["ExtrinsicCalibrationType"]
    Sensor --> Media["Media<br/>VisionMediaManagementType"]
    Media --> Streams["StreamEndpoints"]
    Media --> Clips["ClipEndpoints"]
    Sensor -.->|MountedOn| Frame
    Sensor -.->|HasScenePrim| Scene["OpenUSD scene prim"]

    Pipelines --> Pipeline["InferencePipelineType"]
    Pipeline --> Results["Results"]
    Pipeline --> Feedback["Feedback<br/>VisionFeedbackType"]
    Pipeline -.->|Sensor| Sensor
    Pipeline -.->|Deployment| Deployment["AI deployment NodeId"]
```

The libraries multi-target `net8.0;net9.0;net10.0` and `netstandard2.0`
where applicable; the MCP tool package multi-targets `net8.0;net9.0;net10.0`.

## Two perception paths behind one contract

Every pipeline advertises exactly one of two inference locations, and a
client reads a `DetectionResultType` identically regardless of which is in
force:

- **`InferenceLocation = OnServer`** — the Server holds an
  `IVisionInferenceProvider` and computes results locally. This is the
  deterministic path: it needs no model, no network and no GPU, and it is
  the default for CI and offline validation. `RunInference`,
  `StartContinuous` and `Stop` all delegate to the provider; the Server
  publishes the resulting `DetectionResultType` / `InspectionResultType` /
  `SegmentationResultType` under the pipeline's `Results` folder and
  advertises `VIS-Inference-OnServer`.
- **`InferenceLocation = EdgeOffServer`** — the pipeline exposes a
  `VisionFeedbackType` object bound to an `IVisionFeedbackSink`; an
  off-Server agent (a vision-language model over MCP, an edge inference
  service, another Server) is expected to look at the current frame and
  call `SubmitDetections` / `SubmitInspectionResult` /
  `SubmitCorrection` / `SubmitImageReference`. The Server publishes those
  results into the address space unchanged, and advertises
  `VIS-Inference-OffServer`.

Choose `OnServer` when a deterministic algorithm answers the question
(vision-guided screwdriver alignment against a known fiducial; presence-or-
absence in a clean scene) or when reproducibility on CI matters. Choose
`EdgeOffServer` when a language model or a heavier out-of-process model is
what actually sees the world — the Server publishes what it did not
compute, and safety validation still applies (§9 refusals, class-label /
box / pose / confidence checks; see [Feedback validation](#feedback-validation)).

The two paths are exclusive per pipeline by design: mixing a running
`OnServer` provider with a `SubmitDetections` sink would let a computed
and a submitted result publish on the same pipeline out of any known
order.

```mermaid
flowchart LR
    Client["Client"]
    Server["Vision Server"]
    Provider["IVisionInferenceProvider"]
    Edge["Off-server agent"]
    Sink["IVisionFeedbackSink"]
    Results["Pipeline Results folder"]

    Client -->|RunInference| Server
    Server -->|OnServer delegates| Provider
    Provider -->|result id + result node| Results

    Edge -->|reads frame| Server
    Edge -->|SubmitDetections / SubmitInspectionResult| Server
    Server -->|EdgeOffServer delegates| Sink
    Sink -->|published result| Results

    Client -->|reads same result types| Results
```

## Minimal hosted server

The example below hosts a single simulated eye-in-hand camera, one
inference pipeline, and a two-frame tree (`world` → `flange`). It is the
smallest useful shape; the [`BinPickingCell`](../samples/Robotics/BinPickingCell)
sample is the full end-to-end version.

```csharp
using Microsoft.Extensions.Hosting;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Vision;
using Opc.Ua.Vision.Perception;
using Opc.Ua.Vision.Server;
using Opc.Ua.Vision.Server.Builders;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<MyMediaProvider>();
builder.Services.AddSingleton<MyGroundTruthProvider>();

builder.Services
    .AddOpcUa()
    .AddServer(options =>
    {
        options.ApplicationName = "VisionServer";
        options.ApplicationUri = "urn:localhost:OPCFoundation:VisionServer";
        options.AutoAcceptUntrustedCertificates = true;
        options.EndpointUrls.Add("opc.tcp://localhost:62855/VisionServer");
    })
    .AddVision(options =>
    {
        options.InstanceNamespaceUri = "urn:example:vision:instances";
    })
    .AddVisionMediaProvider<MyMediaProvider>(sensorBrowseName: "Camera01")
    .AddVisionInferenceProvider<MyGroundTruthProvider>(
        pipelineBrowseName: "Detector",
        onServer: true)
    .ConfigureVision((context, ct) =>
    {
        IVisionNodeBuilder nodes = context.Nodes;
        // Nodes is the fluent address-space builder: everything it creates
        // becomes real OPC UA nodes under Server/Vision that any client can
        // browse. The full build context is described further down.

        nodes.AddFrame("World", frame => frame
            .WithFrameId("world")
            .WithRole(VisionFrameRoleEnum.World));

        nodes.AddFrame("Flange", frame => frame
            .WithFrameId("flange")
            .WithRole(VisionFrameRoleEnum.MechanicalInterface)
            .WithParent("world"));

        nodes.AddImageSensor("Camera01", sensor => sensor
            .WithSensorId("cam-01")
            .WithModality(VisionSensorModalityEnum.Area2D)
            .WithRealityKind(VisionRealityKindEnum.Physical)
            .WithFrameId("flange")
            .WithResolution(1920u, 1080u)
            .WithPixelFormat("Mono8")
            .AddClipEndpoint("Clips", ep => ep
                .WithEndpointId("clip-01")
                .WithEndpointUri("opcua-inline://visionserver/clips")
                .WithClipFormat(VisionClipFormatEnum.Png)
                .WithResolution(1920u, 1080u)
                .WithInlineDelivery(enabled: true, maxInlineClipSize: 8_388_608u)));

        // The pipeline is bound to its inference provider by
        // AddVisionInferenceProvider<MyGroundTruthProvider>("Detector", ...)
        // above; the ConfigureVision delegate just creates the pipeline
        // node. A real cell resolves the sensor node the pipeline is
        // reading from off Server/Vision/Sensors — see the BinPickingCell
        // sample for the walk.
        nodes.AddPipeline("Detector", pipe => pipe
            .WithPipelineId("pipe-01")
            .WithSensor(NodeId.Null));

        return ValueTask.CompletedTask;
    });

using IHost app = builder.Build();
await app.RunAsync().ConfigureAwait(false);
```

`AddVision` never modifies the Server's existing NodeManagers — it adds a
standalone `VisionNodeManager` under the well-known `Server/Vision` object
(§4.2). It also composes with `AddRobotIntent` and `AddRobotics`; both
sample cells run all three side by side without any coupling in code.

The `AddVisionMediaProvider<T>` / `AddVisionInferenceProvider<T>` /
`AddVisionFeedbackSink<T>` extensions resolve the provider from the DI
container at build time and bind it to the sensor or pipeline whose
BrowseName is passed. The equivalent `UseMediaProvider(provider)` /
`UseInferenceProvider(provider, onServer)` / `UseFeedbackSink(sink)`
methods on the fluent builders let a configurator bind a provider it
holds directly.

## Hosting API

The extension methods on `IOpcUaServerBuilder` that make up the Vision
hosting surface:

| Method | Purpose |
|---|---|
| `AddVision(Action<VisionServerOptions>?)` | Registers the standalone `VisionNodeManager` and its factory; accepts an optional options delegate |
| `AddVisionMediaProvider<TProvider>(string sensorBrowseName)` | Registers a media provider type — resolved from DI — for the sensor with the given BrowseName |
| `AddVisionMediaProvider(string sensorBrowseName, IVisionMediaProvider provider)` | Registers a media provider instance for the sensor with the given BrowseName |
| `AddVisionInferenceProvider<TProvider>(string pipelineBrowseName, bool onServer)` | Registers an inference provider type — resolved from DI — for the pipeline; `onServer` controls the advertised `VIS-Inference-OnServer` / `VIS-Inference-OffServer` facet (§8.2) |
| `AddVisionInferenceProvider(string pipelineBrowseName, bool onServer, IVisionInferenceProvider provider)` | Registers an inference provider instance |
| `AddVisionFeedbackSink<TSink>(string pipelineBrowseName)` | Registers a feedback sink type — resolved from DI — for the pipeline's `Feedback` object |
| `AddVisionFeedbackSink(string pipelineBrowseName, IVisionFeedbackSink sink)` | Registers a feedback sink instance |
| `ConfigureVision(Func<IVisionBuildContext, CancellationToken, ValueTask>)` | Async configurator, run on server start against the standalone `VisionNodeManager` |
| `ConfigureVision(Action<IVisionBuildContext>)` | Sync configurator overload |
| `ConfigureVisionFor<TNodeManager>(...)` | Configurator targeting `VisionNodeManager` or the stock `MachineryNodeManager` |
| `AddVisionMachinery()` | Registers Part 2 models/builders with a standalone Vision owner |
| `AddVisionToMachinery(...)` | Loads the selected Vision models into a Machinery owner; selects both released parts by default |
| `ConfigureVisionOnMachinery<TNodeManager>(...)` | Configures an application-owned DI/Machinery manager that loads the Vision models |

The Robotics guide's `ConfigureFor<...>` pattern also applies here — any
of these hosting extensions can be called from a class-based configurator
that reads `IServiceProvider`, keeps its own logger, and does not put a
lambda in `Program.cs`.

### `VisionServerOptions`

| Property | Purpose |
|---|---|
| `InstanceNamespaceUri` | The application-owned namespace URI used for the instances the configurator materialises. Must be distinct from the OPC UA base namespace and from `http://opcfoundation.org/UA/Vision/`. Defaults to `urn:opcfoundation:UA:Vision:Instances`. |
| `SpecificationVersion` | The value the Server reports on `Vision.SpecificationVersion`. Defaults to `"0.1.0"`. |
| `AdditionalFacets` | The facets the Server declares beyond those the facet calculator derives structurally — the escape hatch for facets whose requirements are behavioural (an interop facet that the host meets by contract). |
| `MachineVisionParts` | Opt-in `Control`, `Assets`, or `All` for the released OPC 40100 models. Defaults to `None`, selecting the Perception model without released-model instances. |

## Build context

`ConfigureVision(...)` receives an `IVisionBuildContext`. Here `Nodes` is
the fluent address-space builder rooted at the well-known `Server/Vision`
object; it creates the frame, sensor, calibration, media-endpoint and
pipeline nodes that ordinary OPC UA clients browse and read.


| Member | Purpose |
|---|---|
| `Nodes` | The fluent `IVisionNodeBuilder` rooted at the well-known `Server/Vision` object |
| `Manager` | The active `AsyncCustomNodeManager`, for the rare case a configurator must fall back to raw node authoring |
| `Context` | The active `ISystemContext` |
| `Root` | The `VisionRootState` (§4.2) |
| `InstanceNamespaceIndex` | The namespace index of `VisionServerOptions.InstanceNamespaceUri` |
| `VisionNamespaceIndex` | The namespace index of `http://opcfoundation.org/UA/Vision/` |
| `CancellationToken` | The startup cancellation token |
| `GetRequiredService<T>()` | Application-scoped DI resolution |

Everything a Vision cell needs (frames, sensors, pipelines, calibrations,
media endpoints) is authored through `Nodes`. The low-level members are
present for interop with hand-written NodeManagers and for the vendor
extension patterns the Robotics guide describes.

### Without DI

A `VisionNodeManager` created by hand exposes the same fluent surface
through `ConfigureVisionAsync`:

```csharp
await manager.ConfigureVisionAsync(context =>
{
    context.Nodes.AddFrame("World", f => f
        .WithFrameId("world")
        .WithRole(VisionFrameRoleEnum.World));
});
```

Prefer it over `CreateVisionBuildContext()`. The node manager indexes the
Vision root when it creates the address space, so anything the builder
grafts on afterwards has to be registered as well before it can be
browsed or read **by its own NodeId** — which is how an ordinary client
and the MCP discovery tools navigate. `ConfigureVisionAsync` does that
registration when the delegate returns; a context obtained from
`CreateVisionBuildContext()` never does, so nodes built through it stay
reachable only by browsing forward from their parent.

## Topology builders

### Frames

```csharp
nodes.AddFrame("World", f => f
    .WithFrameId("world")
    .WithRole(VisionFrameRoleEnum.World));

nodes.AddFrame("RobotBase", f => f
    .WithFrameId("robot_base")
    .WithRole(VisionFrameRoleEnum.Base)
    .WithParent("world")
    .WithTransform(new VisionPose3DDataType
    {
        FrameId = "world",
        Position = new[] { 0.0, 0.0, 0.829 }.ToArrayOf(),
        Orientation = new[] { 0.0, 0.0, 0.0, 1.0 }.ToArrayOf(),
        Covariance = ArrayOf<double>.Empty,
    }));
```

The `Transform.FrameId` names the parent frame per the §5.12 frame-
precedence rule. When the parent is added later in the configurator,
`WithParent("world")` resolves at finalise time. Passing a `NodeId`
overload (`WithParent(NodeId)`) skips the deferred resolution when the
caller already has one.

Roles: `World`, `Base`, `MechanicalInterface`, `Tool`, `Object`, `Station`,
`Camera`, `Custom`. `Camera` is the non-ISO addition Vision introduces
for the sensor's own frame.

### Sensors

`IVisionNodeBuilder` exposes three sensor entry points:

- `AddImageSensor(browseName, configure)` for `ImageSensorType`;
- `AddDepth3DSensor(browseName, configure)` for `Depth3DSensorType`;
- `AddSensor(browseName, configure)` for the abstract `VisionSensorType`
  when a vendor subtype is materialised through a `IVisionModelProvider`.

All sensor builders share the members on `IVisionSensorBuilder<TSelf>`:
identity (`WithSensorId`, `WithManufacturer`, `WithModel`,
`WithSerialNumber`, `WithDeviceUri`), the frame binding (`WithFrameId`,
`MountedOn`, `HasScenePrim`), the reality kind (`WithRealityKind`), the
modality (`WithModality`), and the nested builders — `WithOptics(...)`,
`WithIllumination(...)`, `AddIntrinsicCalibration(...)`,
`AddExtrinsicCalibration(...)`, `AddStreamEndpoint(...)`,
`AddClipEndpoint(...)`, `UseMediaProvider(...)`.

`WithFrameId(frameId)` also adds a `MountedOn` reference to the
`CoordinateFrameType` instance with the matching `FrameId` when one has
been registered under `Vision/Frames`. This is the recommended way to
attach a sensor to a frame; `MountedOn(NodeId)` and `HasScenePrim(NodeId)`
are the fallbacks for mounts that are not vision frames.

### Pipelines

A pipeline is the Vision object that connects one sensor, an optional AI
deployment reference, and the result/feedback methods for one perception task
such as detection or inspection. Clients discover pipelines first, then run
or observe the task through that pipeline.

```csharp
nodes.AddPipeline("Detector", pipe => pipe
    .WithPipelineId("pipe-01")
    .WithSensor(cameraNodeId)
    .WithDeployment(deploymentNodeId)
    .ProducedBy(controllerNodeId)
    .UseInferenceProvider(inferenceProvider, onServer: true)
    .UseFeedbackSink(feedbackSink));
```

`WithSensor(NodeId)` points the pipeline at the sensor that supplies the
frames. `WithDeployment(NodeId)` then records which model deployment, if
any, is responsible for the task. The specification deliberately keeps
`Deployment` typed as `NodeId` so a Server never has to depend on the AI
Model Management companion; a host that implements that companion can point
at its deployment node, and a host that does not can leave the value null.
`ProducedBy(NodeId)` adds the `ProducedBy` semantic reference to a controller
or process instance.

`UseFeedbackSink(sink)` is optional for `OnServer` pipelines. `OnServer`
pipelines without a feedback sink still expose a `Feedback` object
whose `Submit*` methods return `Bad_NotSupported` — a client cannot
publish detections into a pipeline where nothing consumes them.
`EdgeOffServer` pipelines almost always want both a provider (whose
`RunInference` explains the mode with `Bad_NotSupported`) and a sink
that receives the agent's submissions.

### Providers

The provider abstractions live in `Opc.Ua.Vision.Server`:

- **`IVisionMediaProvider`** — supplies media without putting pixels on
  OPC UA. `GetStreamAsync` returns a leased URI; `GetClipAsync` returns
  a `VisionImageReferenceDataType` and, when the caller asked for it and
  the encoded bytes fit the effective inline limit, an inline
  `ByteString`. Providers implement the by-reference default (§6.4) —
  Servers keep pixel bytes off the OPC UA wire.
- **`IVisionInferenceProvider`** — binds a pipeline to whatever actually
  computes results. The Server publishes the result nodes and applies
  the spec's method conventions regardless of whether the provider runs
  a deterministic detector, a GPU inference engine, an in-process
  simulation or refuses everything with `Bad_NotSupported` (the
  `EdgeOffServer` case).
- **`IVisionFeedbackSink`** — receives `SubmitDetections`,
  `SubmitInspectionResult`, `SubmitCorrection` and
  `SubmitImageReference`. Off-Server agents publish through this path
  and the Server records what it did not compute.

Every provider is registered as a DI singleton and constructed with the
same lifetime as the server host — the framework never re-creates them
per call.

## §5.12 conventions

Vision inherits and adds a small number of numerical conventions that are
silently wrong if misread. Every client, provider and configurator in
this repository respects them, and public API points that carry pose or
image geometry document them explicitly:

- **Quaternion order is `(x, y, z, w)`.** Every `Orientation` array in
  `VisionPose3DDataType` is a unit quaternion ordered `(x, y, z, w)`. The
  frame graph checks `‖q‖ = 1` within tolerance `1e-6` and refuses a
  zero-norm quaternion with `Bad_InvalidArgument`.
- **Positions are metres.** Every `Position` array is a 3-vector in
  metres. `MinDepth`, `MaxDepth`, `Baseline`, `WorkingDistance` and every
  distance-typed member are metres.
- **The principal point is corner-datum.** `VisionIntrinsicsDataType.Cx`
  and `Cy` are measured from the top-left corner of the image (pixel
  centre `(0.5, 0.5)`). A client bridging to a library that uses
  centre-datum coordinates subtracts `0.5` from `Cx` and `Cy`.
- **An empty covariance array is the sentinel for "not reported".** A
  pose that reports no covariance uses `Covariance = ArrayOf<double>.Empty`
  — not a 6×6 zero matrix, which would misrepresent the pose as having
  been measured with perfect certainty.

The `VisionFrameGraph` composes transforms strictly per these rules:
right-handed frames, `(x, y, z, w)`-ordered quaternions, no
substitutions.

## §6.4 media gating

Vision separates the by-reference default path (a `VisionImageReference`
descriptor with URI, timestamp and digest) from the optional inline
delivery of encoded still image bytes. §6.4 fixes what a Server returns
in each state, and `VisionMediaClient` classifies the raw `StatusCode`
into a `VisionInlineClipState` enum so a caller can branch cleanly:

| State | `StatusCode` | Meaning |
|---|---|---|
| `Available` | `Good` | The encoded image fits the inline limit and is returned in `VisionInlineClipReading.Bytes` |
| `NotYetAvailable` | `Bad_NoDataAvailable` | The Server has not published a clip yet — §6.4 rule 5 requires this before the first acquisition |
| `InlineDisabled` | `Bad_NotSupported` | `InlineDeliveryEnabled = false` on the endpoint; §6.4 rule 5 requires this exact code |
| `Overflow` | `Bad_EncodingLimitsExceeded` | The last acquisition exceeded the effective inline size limit; §6.4 rule 3 requires no truncation |
| `Faulted` | Other | The endpoint reported a different error |

`GetClip` is always the safe path — it returns the by-reference
descriptor, and returns the inline bytes as well when the caller passes
`requestInline: true` and the still fits. `LatestClip` and
`LatestClipMetadata` are the "read the latest" variant of the same
contract. Crucially, `LatestClipMetadata` remains readable even when
`LatestClip` reports `Bad_NotSupported` — the metadata carries the URI,
timestamp, digest and pixel format the caller needs to walk the still
out of band, and reporting `Bad_NotSupported` on the metadata read would
be wrong.

```mermaid
stateDiagram-v2
    [*] --> NotYetAvailable: no acquisition yet
    NotYetAvailable --> Available: clip captured and fits
    Available --> Overflow: next clip exceeds limit
    Overflow --> Available: later clip fits
    Available --> InlineDisabled: inline delivery disabled
    InlineDisabled --> Available: inline delivery enabled
    Available --> Faulted: provider error
    Overflow --> Faulted: provider error
    Faulted --> Available: provider recovers
```

## Rendering without pixels

`Opc.Ua.Vision.OpenUsd` renders a `UsdGeomCamera` from a USD stage to an
encoded still, and is the reference `ISceneCameraCaptureProvider`
implementation the sample cell registers with
`services.AddOpenUsdSceneCameraCaptureProvider()`. When rendering is not
possible it reports the unavailable backend and leaves the address space
walkable instead of throwing from browse/read paths. It supports the
following behaviour explicitly:

- On a machine with the native OpenUSD renderer payload present and a
  usable graphics device, it renders normally and returns encoded PNG /
  JPEG bytes.
- On a machine with no graphics device — the normal case on CI — it
  reports `SceneCameraCaptureBackend.NoRenderingBackend` on
  `Backend.UnavailableReason` and returns `Bad_NoDataAvailable` for
  every capture. The sensor still exists in the address space and every
  browse still works; only the pixel bytes are absent.

The intent is that a client can rely on the address space always being
walkable, even when the process has no way to produce pixels. The
`--demo` client path in the sample skips its compose step gracefully
when the frame is unavailable rather than falsely reporting a rendering
bug.

## Facets supported

`VisionServerOptions.AdditionalFacets` is additive on top of the facets
the address-space calculator derives structurally. **Supported** means the
stock builder and calculator can claim the facet from the materialised address
space. **Partial** means the model surface exists, but the host must attest the
facet through `AdditionalFacets` because the calculator cannot verify the
behaviour or provider-owned result nodes. **Not supported** means the stock
server does not currently claim that facet.

| Facet | Support | Structural requirement or limitation |
|---|---|---|
| `VIS-Base` | Supported | A registered sensor contributes the base Vision server shape. |
| `VIS-Sensor-Params` | Supported | A sensor includes manufacturer, model or serial-number parameters. |
| `VIS-Optics` | Supported | A sensor has an `Optics` child. |
| `VIS-Media-Rtsp` | Supported | A stream endpoint uses `VisionStreamProtocolEnum.Rtsp`. |
| `VIS-Media-Jpeg` | Supported | A clip endpoint uses `VisionClipFormatEnum.Jpeg`. |
| `VIS-Media-Inline` | Supported | A clip endpoint has `InlineDeliveryEnabled = true`. |
| `VIS-Media-DataChannel` | Not supported | There is no stock data-channel endpoint builder or calculator rule. |
| `VIS-Endpoint-Config` | Supported | At least one stream endpoint is materialised. |
| `VIS-Calibration` | Supported | An intrinsic or extrinsic calibration is materialised. |
| `VIS-Result-Detection` | Partial | Providers and sinks can publish detection results, but result ownership is provider-side and not structurally derived. |
| `VIS-Result-Inspection` | Partial | `SubmitInspectionResult` exists, but the calculator does not infer inspection-result publication. |
| `VIS-Result-Segmentation` | Not supported | The stock conformance URI list and feedback surface do not claim a segmentation-result facet. |
| `VIS-Feedback` | Supported | A pipeline has a `Feedback` object bound to a sink. |
| `VIS-Inference-OnServer` | Supported | A pipeline was registered with `onServer: true`. |
| `VIS-Inference-OffServer` | Supported | A pipeline was registered with `onServer: false`. |
| `VIS-Simulation` | Supported | A sensor has `RealityKind = Simulated` or `Hybrid`. |
| `VIS-Learning` | Partial | Learning jobs are modelled by reference; the host owns the job and any `SamplesCollected` accounting. |
| `VIS-Interop-Scene` | Supported | A sensor carries a `HasScenePrim` reference. |
| `VIS-Interop-40100` | Opt-in, application-specific | The released models are implemented separately; a host explicitly binds shared behavior and attests its draft interop facet through `AdditionalFacets`. See [Machine Vision](Vision.md). |
| `VIS-Interop-RobotIntent` | Partial | The host must attest cross-model behaviour through `AdditionalFacets`. |

The Server publishes the composed set on
`Server.ServerCapabilities.ServerProfileArray`.

## Using the client libraries

### Registration

The client hosting extensions register a `VisionClientFactory` and the
factory function downstream services request:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.Client;
using Opc.Ua.Vision.Client;

builder.Services
    .AddOpcUa()
    .AddClient(options => { /* endpoint and application options */ })
    .AddVisionClient();
```

`AddVisionClient()` requires `AddClient(...)` to have been called first
so the shared `ManagedSession` factory is available.

Without DI, a `VisionClient` can be opened directly from any connected
`ISession`:

```csharp
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Vision.Client;

VisionClient vision = session.Vision(telemetry);
if (!vision.IsVisionNamespaceAvailable)
{
    // The Server does not implement the Vision companion.
    return;
}
```

### Discovery

```csharp
await foreach (VisionNodeEntry sensor in vision.EnumerateSensorsAsync(ct))
{
    Console.WriteLine($"{sensor.BrowseName} ({sensor.TypeDefinition})");
}

await foreach (VisionNodeEntry pipeline in vision.EnumeratePipelinesAsync(ct))
{
    Console.WriteLine($"{pipeline.BrowseName} ({pipeline.TypeDefinition})");
}
```

Both `EnumerateSensorsAsync` and `EnumeratePipelinesAsync` are subtype
aware: a Server that specialises `ImageSensorType` with a vendor
subtype, or `DetectionResultType` with a domain result subtype, is
enumerated as an instance of the closest declared Vision base type. The
lower-level `DiscoverSensorsAsync` / `DiscoverPipelinesAsync` /
`DiscoverFramesAsync` return `ArrayOf<NodeId>` for callers that already
know how to render the picker themselves.

### Reading a detection

```csharp
using Opc.Ua.Vision;
using Opc.Ua.Vision.Perception;
using Opc.Ua.Vision.Client;

NodeId pipelineNodeId = /* discovered above */;
VisionPipelineClient pipe = vision.Pipeline(pipelineNodeId);

VisionPipelineSnapshot snapshot = await pipe.ReadAsync(ct);
string runId = await pipe.RunInferenceAsync(cancellationToken: ct);

await foreach (VisionNodeEntry result in pipe.EnumerateResultsAsync(ct))
{
    VisionDetectionResultSnapshot detection = await vision
        .Result(result.NodeId)
        .ReadDetectionAsync(ct);

    Console.WriteLine(
        $"result={detection.ResultId} frame={detection.FrameId} " +
        $"count={detection.Detections.Count}");
    for (int ii = 0; ii < detection.Detections.Count; ii++)
    {
        VisionDetectionDataType d = detection.Detections[ii];
        Console.WriteLine($"  [{ii}] {d.ClassLabel} conf={d.Confidence:0.###}");
    }
}
```

`VisionPipelineClient.RunInferenceAsync(default, ct)` lets the Server
acquire the frame "now"; passing a non-default `DateTimeUtc` requests a
specific acquisition timestamp — the Server may honour it or refuse per
§8.

### Composing a pose

The Vision-side calibrations and frame names match the Robotics-side
frame ids by convention — a client can walk from the vision-side pose to
the robot-side world frame without any translation table:

```csharp
VisionFrameGraph frames = vision.Frames();

NodeId cameraFrameId = /* from EnumerateFramesAsync, matching FrameId "camera_eih" */;
NodeId worldFrameId = /* likewise, matching "world" */;

VisionDetectionResultSnapshot detection =
    await vision.Result(resultNodeId).ReadDetectionAsync(ct);

for (int ii = 0; ii < detection.Detections.Count; ii++)
{
    VisionDetectionDataType d = detection.Detections[ii];
    if (!d.HasPose)
    {
        continue;
    }

    // Compose the detection's pose (expressed in the camera frame) into
    // the world frame; the frame graph walks the parent chain and
    // multiplies transforms per §5.12.
    VisionPose3DDataType inWorld = await frames.ComposeAsync(
        d.Pose, cameraFrameId, worldFrameId, ct);

    Console.WriteLine(
        $"{d.ClassLabel}: pos=[{inWorld.Position[0]:0.###}, " +
        $"{inWorld.Position[1]:0.###}, {inWorld.Position[2]:0.###}]");
}
```

`ComposeAsync` walks up to 32 frames from each side, throws
`Bad_NoMatch` when the two frames share no common ancestor, and refuses
a non-unit quaternion within tolerance `1e-6` — none of which are
substituted with an identity transform, because a silent substitution
would make an incorrect pose look correct.

### Submitting feedback

```csharp
VisionFeedbackClient? feedback = await pipe.OpenFeedbackAsync(ct);
if (feedback is null)
{
    // The pipeline does not expose a Feedback object — nothing to submit into.
    return;
}

ArrayOf<VisionDetectionDataType> detections = new[]
{
    new VisionDetectionDataType
    {
        ClassLabel = "RedCube",
        Confidence = 0.94,
        HasBoundingBox2D = true,
        BoundingBox2D = new VisionBoundingBox2DDataType
        {
            CenterX = 812.0, CenterY = 604.0, Width = 96.0, Height = 96.0,
        },
        HasPose = true,
        Pose = new VisionPose3DDataType
        {
            FrameId = "camera_eih",
            Position = new[] { 0.031, -0.017, 0.412 }.ToArrayOf(),
            Orientation = new[] { 0.0, 0.0, 0.0, 1.0 }.ToArrayOf(),
            Covariance = ArrayOf<double>.Empty,
        },
    },
}.ToArrayOf();

await feedback.SubmitDetectionsAsync(
    VisionFeedbackPurposeEnum.Reconciliation,
    detections,
    frameReference: null,
    inlineImage: ByteString.Empty,
    cancellationToken: ct);
```

The `Purpose` values `Overlay`, `Reconciliation`, `GroundTruthLabel`
and `Trigger` are all defined; a Server refuses the ones it does not
permit with `Bad_NotSupported`. `SubmitCorrection` accepts *at most
one* of the two `corrected*` arrays non-empty — passing both is an
argument error (§9.5). `SubmitInspectionResult` requires at least one
characteristic.

> **Reporting an empty scene, and retracting a false positive.**
> `SubmitDetections` takes a `SceneIsEmpty` flag and `SubmitCorrection`
> a `RetractAll` flag. They exist because an empty observation is a real
> one: "I examined this frame and there is nothing in it" is the
> terminating condition of a bin-picking task and a valid negative
> training label, and a false positive is corrected by asserting that
> nothing replaces it. Neither statement can be made by submitting an
> array, because both *are* the empty array.
> The pairing is checked in both directions. An empty `Detections`
> without `SceneIsEmpty` is refused — the flag is what distinguishes a
> deliberate observation from a lost payload — and `SceneIsEmpty` with
> detections attached is refused because it asserts two contradictory
> things about one frame. `RetractAll` behaves the same way against the
> corrected arrays. §9.4 requires a Server to count a negative example
> in `SamplesCollected` exactly as it counts one carrying geometry —
> that counter belongs to the learning job, so see
> [Limitations](#limitations) for where this implementation draws the
> line and what it hands the host instead.

### Streaming detections

`VisionResultReader.ObserveDetectionsAsync(IStreamingSubscription, ct)`
publishes each `DetectionResultType` change as it arrives — either a new
result or a mutation of an existing one. Use it when you need to react
to detections rather than poll:

```csharp
using Opc.Ua.Client;

// A ManagedSession exposes a shared default IStreamingSubscription
// that a caller can hand to any Observe*Async method.
IStreamingSubscription streaming = session.DefaultStreaming;

VisionResultReader reader = vision.Result(resultNodeId);
await foreach (VisionDetectionResultSnapshot snapshot in
    reader.ObserveDetectionsAsync(streaming, ct))
{
    Console.WriteLine($"result {snapshot.ResultId}: {snapshot.Detections.Count} detections");
}
```

`ObserveInspectionAsync` and `ObserveSegmentationAsync` do the same for
inspection and segmentation results.

## MCP tools

The `Opc.Ua.Mcp.Vision` package contributes 22 Perception tools in the six categories
below plus 17 [Machine Vision tools](#mcp), and the bounded `vision` profile in `docs/McpServer.md`
carries them plus the four `ConnectionTools` — every Vision tool
resolves a named OPC UA session, and only the connection tools can open
one.

- Discovery — `vision_list_sensors`, `vision_list_pipelines`,
  `vision_list_frames`, `vision_list_calibrations`.
- Monitoring — `vision_read_sensor`, `vision_read_extrinsic_calibration`,
  `vision_read_pipeline`, `vision_read_detection_result`,
  `vision_read_inspection_result`, `vision_read_segmentation_result`.
- Seeing — `vision_get_frame` (returns the encoded still as an MCP
  `ImageContentBlock` with the correct MIME type, so a model actually
  sees pixels rather than a description of them), `vision_get_frame_metadata`.
- Inference — `vision_run_inference`, `vision_start_continuous_inference`,
  `vision_stop_inference`.
- Feedback — `vision_submit_detections`, `vision_submit_inspection_result`,
  `vision_submit_correction`, `vision_submit_image_reference`.
- Geometry — `vision_read_frame`, `vision_compose_pose`,
  `vision_compose_transform`.

`vision_run_inference` accepts one structured `request`. Its `pipeline`
selector is an exact BrowseName, DisplayName or NodeId; `expectedKind` can
require a Detection, Inspection or Segmentation result; and `detail=Summary`
returns a bounded typed summary together with the ResultId, result NodeId,
sensor, requested/published pipeline, model and frame provenance. Use
`detail=HandleOnly` when only the addressable result is needed, or call a
`vision_read_*_result` tool for the complete payload.

Profiles compose. A host that wires the `Vision` and `Robotics` profile
sets together — the [BinPickingClient sample](../samples/Robotics/BinPickingClient)
does this — exposes 81 tools in total, measured as
`39 Vision + 4 Connection + 42 Robotics − 4 shared Connection = 81`.
The `WithOpcUaVisionTools(McpToolProfileSet)` overload never registers
`ConnectionTools` directly; the corresponding
`WithOpcUaCoreTools(McpToolProfileSet)` overload owns and deduplicates
that registration across every OPC UA MCP package a host references.

See [`docs/McpServer.md`](McpServer.md) for the full profile table and
composition rules.

## Sample: bin-picking

[`samples/Robotics/BinPickingCell`](../samples/Robotics/BinPickingCell) is
the reference from the *OPC UA Robotics-Vision Addendum*: a
branch-stable four-axis palletizer with a parallel gripper, an
eye-in-hand camera parented to the flange, a bin of five parts, a
fixture, and the frame tree
`world → robot_base → flange → gripper_tcp` with `camera_eih` on the
flange. It hosts `Robot Intent`, the Vision companion and the
`OpenUsdScene` companion side by side, and either the on-server
deterministic detector (`--inferenceLocation OnServer`, the default)
or the off-server agent path (`--inferenceLocation EdgeOffServer`).
The selected detection identifies the workpiece and preserves its pose as
provenance. The Pick carries the selected class plus the source Location and
tool; the cell's executor resolves that workpiece at its observed world pose
rather than substituting the Location centre.

[`samples/Robotics/BinPickingClient`](../samples/Robotics/BinPickingClient)
is the paired client: `--demo` runs the whole loop without an agent,
`--mcp` exposes the composed 81-tool MCP catalogue for a language-model
agent, and `--view` opens the in-process OpenUSD viewport so a human
sees the same scene the agent sees.

The composed Robotics package also contributes `robotics_vision_pick`. It runs
one inference through the Vision client, selects one detection
deterministically, and submits either a Pick operation or a two-step Pick/Place
mission. It returns the selected detection provenance and the authoritative
operation or mission handles. Command authority remains explicit: the helper
never requests ownership, retries, waits, cancels or hides a refusal.

### Scene lighting

The scene is lit by a single `DomeLight` with `intensity = 1000`.
Do **not** reintroduce a `DistantLight` or any other bright directional or
point light for a Vision demo: at any intensity that shows geometry, a
`DistantLight` blows every surface to pure white regardless of
`displayColor`, and any agent looking at the frame sees a uniform
white blur. Under a `DomeLight` the five sample parts measure
`red (220, 37, 37)`, `green (37, 208, 49)`, `blue (37, 73, 233)` — distinct
enough for a vision-language model to reason about ("pick the red
cube"). The `Cell.usda` header records this contract explicitly.
Anyone authoring their own scene from scratch needs to know this or
their agent will see nothing they can act on.

### Feedback validation

When the cell runs in `EdgeOffServer` mode the agent sends detections
through `SubmitDetections`. The sample's feedback sink refuses malformed
submissions with `Bad_InvalidArgument` and a message the agent can act
on:

- **Unknown class label** — refused with the exact list of parts that
  do exist. `Detection 0 class 'PurplePyramid' is not a part in this cell.
  Known classes: RedCube, GreenCylinder, BlueSphere, YellowSlab, OrangeBrick.`
- **Confidence outside `[0, 1]`** — refused with the observed value.
- **Bounding box outside the image** — refused with the box coordinates
  and the image dimensions the box was measured against.
- **Zero-norm quaternion or pose with fewer than three position
  components** — refused with the detection index.

An empty detection set is accepted only when `SceneIsEmpty` is set; setting
that flag with a nonempty set is rejected. For corrections, `RetractAll` allows
both corrected arrays to be empty. Otherwise exactly one corrected array must
be nonempty, and `RetractAll` must not accompany corrected entries.

These flags let an agent report an empty scene or retract a false positive
without inventing a detection. See [Submitting feedback](#submitting-feedback)
for the payload contract and [Limitations](#limitations) for the host's
responsibility for learning counters.

## Limitations

- **The Vision specification is a draft.** The namespace URI and every
  NodeId are provisional; every ObjectType and BrowseName can change
  when the working group publishes.
- **No vendor drivers ship.** The reference `IVisionMediaProvider`
  covered here renders an OpenUSD stage offscreen. There is no GigE
  Vision, USB3 Vision, GenICam or vendor-native driver in the box; a
  host implementing `IVisionMediaProvider` for a real camera is the
  supported extension point.
- **Custom composition requires an existing owner.** `ConfigureVisionFor<TNodeManager>`
  supports the stock Vision and Machinery managers. Application-owned DI managers
  use `ConfigureVisionOnMachinery<TNodeManager>` and Machinery's existing model/lifetime
  integration instead of registering a second DI-owning node manager.
- **Learning jobs are modelled but not driven, and `SamplesCollected` is
  not counted here.** `InferencePipelineType` carries a `LearningJob`
  optional child and the facet calculator publishes `VIS-Learning` when
  one is bound, but the standalone `VisionNodeManager` does not itself
  run training — a host provides a learning-job provider through the
  extension pattern. This has one named conformance consequence: §9.4
  requires a Server to count a negative example (`SceneIsEmpty` or
  `RetractAll` carrying a `GroundTruthLabel`) in `SamplesCollected`
  exactly as it counts one carrying geometry, and `VisionNodeManager`
  does not do that counting. It cannot: `SamplesCollected` is a property
  of `LearningJobType`, which the *AI Model Management* companion
  defines, and Vision reaches it through a `NodeId` value rather than a
  Reference precisely so this model takes no dependency on the model
  that defines the job. What the Server does guarantee is that the
  negative example survives the hop intact — `SceneIsEmpty` and
  `RetractAll` are carried verbatim on
  `VisionSubmitDetectionsRequest` / `VisionSubmitCorrectionRequest`, so
  a host that binds a learning job has everything it needs to satisfy
  §9.4 on the counter it owns.
- **AOT compatibility of the OpenUSD capture provider depends on the
  native renderer payload.** The managed layer is AOT-friendly; the
  native payload ships per-RID and its presence at runtime is what
  distinguishes a rendering capture from a `NoRenderingBackend` one.

## Visual inspection: a cross-companion cell

Inspection is the part of the specification that composes with the most
other models, so it is documented here in full rather than in isolation.
The [Vision samples](../samples/Vision) build a cell where a camera
photographs a machined bracket, a model measures it, deterministic code
judges those measurements against a recipe, the verdict drives an ISA-95 job
order, and anything the machine cannot decide escalates to an operator whose
answer is captured as ground truth and counted as a learning sample. The
sample READMEs cover how to run it; what follows is why it is built this way.

### The model never decides

The central safety property is that model output is evidence, not authority. A
model may be involved in producing measured characteristics and a confidence,
but deterministic code applies the recipe tolerances and computes the verdict.
That matters because a plant that let a language model schedule production from
what it thought it saw in a photograph would have an image-shaped path straight
into job control. In this sample, image content can influence the measured
values, but it cannot become a free-form job-control instruction.

The sample also routes inference through the AI companion's deployment
`Invoke` method instead of letting the agent call a model privately in its own
process. That keeps the deployment node, model version, and usage accounting in
the address space and in the provenance trail. A private model call would make
the most important part of the loop invisible.

### Address-space composition

`VisualInspectionCell` composes four companion areas in one server process:

- Vision publishes `BracketFixtureCamera`, `FixtureImages`,
  `BracketInspectionPipeline`, inspection results, and feedback.
- AI Model Management publishes the primary deployment
  `visual-inspection-primary`, its model metadata, and the learning job whose
  `SamplesCollected` value is incremented by host code.
- ISA-95 Job Control V2 publishes the fixed job-order catalogue and the V2
  Methods the agent calls.
- Alarms & Conditions publishes the `OperatorDispositionDialog` condition.

`BracketInspectionPipeline` points at the AI deployment through `Deployment` and
at the learning job through `LearningJob`. The Vision companion deliberately
uses `NodeId` values for those bindings; it does not take a compile-time
dependency on the AI companion. This sample is the host that binds both models
and can therefore satisfy the learning-job counter semantics that standalone
Vision cannot.

### Recipe and verdict rule

The inspected part is a machined bracket with three dimensional characteristics
in millimetres:

| Characteristic | Nominal | Tolerance |
|---|---:|---:|
| `BoreDiameter` | 12.00 | ± 0.20 |
| `SlotWidth` | 8.00 | ± 0.15 |
| `EdgeOffset` | 20.00 | ± 0.25 |

For each characteristic, the rule builds an interval from the measured value and
physical uncertainty:

```text
measurement interval = actual ± uncertainty
tolerance interval   = [nominal - lowerTol, nominal + upperTol]
```

Then it classifies the characteristic:

- wholly inside the tolerance interval -> `Ok`
- wholly outside the tolerance interval -> `NotOk`
- straddling either tolerance limit -> `NotDecidable`

The part verdict is the worst characteristic verdict, with `NotOk` worse than
`NotDecidable`, and `NotDecidable` worse than `Ok`.

```mermaid
flowchart TD
    Measurement["Measured characteristic<br/>actual and uncertainty"] --> Interval["Build actual +/- uncertainty"]
    Interval --> Compare{"Compare with tolerance interval"}
    Compare -->|"wholly inside"| Ok["Ok"]
    Compare -->|"wholly outside"| NotOk["NotOk"]
    Compare -->|"straddles a limit"| NotDecidable["NotDecidable"]
    Ok --> Worst["Part takes worst characteristic verdict"]
    NotOk --> Worst
    NotDecidable --> Worst
```

### Why uncertainty is physical

The fixture images are 800 x 600 pixels at 10 px/mm. A feature edge can only land
on a pixel boundary, so a dimensional measurement carries one-pixel quantisation
uncertainty: 0.10 mm. That is the camera's pixel pitch. It is what makes
`VisionCharacteristicDataType.Uncertainty` meaningful, and it is why
`NotDecidable` arises naturally instead of being contrived.

The three fixtures exercise all branches:

| Fixture | Decisive characteristic | Interval | Verdict |
|---|---|---|---|
| `bracket-ok.png` | `BoreDiameter = 12.00` | `[11.90, 12.10]` is wholly inside `[11.80, 12.20]` | `Ok` |
| `bracket-not-ok.png` | `BoreDiameter = 12.60` | `[12.50, 12.70]` is wholly outside the bore tolerance | `NotOk` |
| `bracket-ambiguous.png` | `SlotWidth = 8.10` | `[8.00, 8.20]` straddles the 8.15 upper limit | `NotDecidable` |

The ambiguous fixture is intentionally mundane: the intended 8.15 mm slot is
81.5 pixels at 10 px/mm and cannot be drawn exactly. The raster image therefore
measures as 8.10 mm, and one pixel of uncertainty crosses the tolerance limit.
That is precisely the case the Vision `NotDecidable` value exists for.

### Inspection loop

The agent drives the process from outside the server. It discovers the Vision
pipeline, opens the media endpoint, follows the pipeline's `Deployment` to the
AI companion, discovers ISA-95 V2 endpoints, and finds the operator dialog.

```mermaid
flowchart TD
    Discover["Discover pipeline, media, deployment, jobs, dialog"] --> Capture["Get fixture PNG"]
    Capture --> Measure["Measure bracket geometry"]
    Measure --> Invoke["Call AI deployment Invoke"]
    Invoke --> Judge["Apply recipe rule"]
    Judge --> Submit["Submit inspection result to Vision Feedback"]
    Submit --> Verdict{"Verdict"}
    Verdict -->|"Ok"| CloseOk["Start, stop, and clear inspection job"]
    CloseOk --> Next["StoreAndStart inspection order"]
    Verdict -->|"NotOk"| CloseBad["Start, stop, and clear inspection job"]
    CloseBad --> Reject["StoreAndStart rework/reject order"]
    Verdict -->|"NotDecidable"| Hold["Hold for operator"]
```

The important separation is that quality outcome and job execution state are
separate facts. A defective part does not mean the inspection job failed.

| Verdict | Inspection job | Next job |
|---|---|---|
| `Ok` | complete, close | schedule next inspection |
| `NotOk` | complete, close | schedule rework/reject order |
| `NotDecidable` | hold | none until the operator answers |

Scheduling selects an order from a fixed allowlisted catalogue and calls V2
`StoreAndStart`. `InspectionJobControlProvider` accepts only
`VIS-INSP-BRACKET-001` and `VIS-REWORK-REJECT-001`; the agent never invents a
job payload.

### Escalation and ground truth

`NotDecidable` activates the human path. The design dispositions are
`AcceptAsOk`, `AcceptAsNotOk`, `Reinspect`, and `Stop`. The implementation maps
those dispositions onto the dialog response and a bounded timeout: it holds or
stops, but it does not auto-approve and does not block forever.

```mermaid
sequenceDiagram
    participant Agent as VisualInspectionAgent
    participant Vision as Vision Feedback
    participant Dialog as Operator Dialog
    participant Operator as Human Operator
    participant AI as AI Learning Job

    Agent->>Vision: SubmitInspectionResult NotDecidable
    Agent->>Dialog: Wait for disposition
    Dialog->>Operator: Request Accept, Reinspect, Reject, or Stop
    Operator-->>Dialog: Disposition
    Dialog-->>Agent: Response or timeout
    alt Accept as ground truth
        Agent->>Vision: SubmitCorrection GroundTruthLabel
        Vision->>AI: RecordLearningSampleAsync
        AI-->>Vision: Idempotent count result
    else Reinspect
        Agent->>Vision: No correction
        Agent->>Agent: Schedule inspection order
    else Stop or timeout
        Agent->>Agent: Hold or stop without approval
    end
```

The operator answer becomes a Vision §9 ground-truth correction. The cell's
feedback sink calls `AiNodeManager.RecordLearningSampleAsync` and uses a stable
sample id, so a retry does not count the same label twice. A negative example is
still a learning sample: it counts exactly once even when it carries no geometry.

This closes a limitation called out in the [Vision developer guide](Vision.md#limitations):
`SamplesCollected` belongs to the AI companion's `LearningJobType`, while Vision
only names the learning job by `NodeId`. A standalone Vision node manager cannot
increment a counter owned by another companion. A host binding Vision and AI
Model Management together can, and this cell is that host. See also the
[AI Model Management developer guide](AI.md) for the deployment
and learning-job model.

### Modes

`VisualInspectionAgent` supports three modes:

- `scripted` — deterministic analyser, scripted operator policy, and a finite
  `--cycles N`. This is the unattended path.
- `live-ai` — a real model path. It requires `--ai-endpoint`; if no endpoint is
  configured, the agent exits before creating any job and never silently falls
  back to the simulated analyser.
- `human` — a real dialog subscriber path with a bounded
  `--operator-timeout`.

The no-silent-fallback rule is part of the sample's safety story. A sample that
quietly degrades can look green while proving neither model connectivity nor the
provenance path it claims to demonstrate.

### What is deliberately not implemented

The sample does not implement retraining or model promotion. It records learning
samples honestly and increments the AI learning-job count, but it does not fake
an MLOps workflow. A simulated retraining integration that appeared to work
would mislead readers about the one part of the specification a sample cannot
honestly demonstrate. The [AI Model Management sample](../samples/AI/README.md)
takes the same line.

## See also

- [Robotics developer guide](Robotics.md) — the sibling companion
  implementation Vision composes with; the `BinPickingCell` sample is
  the cross-companion example.
- [OpenUSD guide](OpenUsd.md) — the OpenUSD connector and scene
  materialisation used to bind the Vision sample cell to a live USD
  stage.
- [MCP Server guide](McpServer.md) — the `vision` MCP profile and its
  composition with `robotics`.
- [AI Model Management developer guide](AI.md) — the
  companion that owns the model, dataset and deployment a Vision
  pipeline's `Deployment` and `LearningJob` point at, and the counter
  §9.4 asks a learning job to keep.
- [Dependency Injection](DependencyInjection.md) — the `AddOpcUa()`
  builder surface that hosts `AddVision`, `AddVisionClient`, and every
  other component.