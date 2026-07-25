# OPC UA ISA-95 (OPC-10030 / OPC-10031-4)

End-to-end developer guide for the `Opc.Ua.ISA95*` library trio: the OPC-10030 ISA-95 Common Model and the OPC-10031-4 Job Control V1 and V2 companion models.

## Contents

- [Library layout](#library-layout)
- [Models and namespaces](#models-and-namespaces)
- [Normative NodeSet repairs](#normative-nodeset-repairs)
- [Quick start — server](#quick-start--server)
- [Quick start — client](#quick-start--client)
- [Server hosting model](#server-hosting-model)
- [Common-model builder reference](#common-model-builder-reference)
- [Job Control (V1 and V2)](#job-control-v1-and-v2)
- [GeoSpatialLocationType provider seam](#geospatiallocationtype-provider-seam)
- [Client](#client)
- [In-memory limitations and HA guidance](#in-memory-limitations-and-ha-guidance)
- [Provenance](#provenance)
- [Conformance matrix](#conformance-matrix)
- [Validation commands and acceptance](#validation-commands-and-acceptance)

## Library layout

| Library | Role |
|---------|------|
| `Opc.Ua.ISA95` | Model assembly: source-generated NodeId tables, DataTypes, and ObjectType client proxies for the Common Model and both Job Control versions. Shared type contract for the client and server packages. |
| `Opc.Ua.ISA95.Server` | Server: `Isa95NodeManager` (one multi-namespace `FluentNodeManagerBase`), the typed common-model builder, Job Control provider abstractions plus an in-memory implementation, the `GeoSpatialLocationType` provider seam, and `AddIsa95Server` hosting integration. |
| `Opc.Ua.ISA95.Client` | Client: `Isa95Client` discovery entry point, direct V1/V2 Job Control clients, typed V2 status-event streaming, and `AddIsa95Client` hosting integration. |

The running example is [`samples/MinimalIsa95Server`](../samples/MinimalIsa95Server), a minimal server hosting all three namespaces with a seeded demo job order.

## Models and namespaces

| Model | Spec | ModelUri | Version | Publication date | C# namespace |
|-------|------|----------|---------|-------------------|---------------|
| Common Model | OPC-10030 | `http://www.OPCFoundation.org/UA/2013/01/ISA95` | 1.00 | 2013-11-06 | `Opc.Ua.ISA95` |
| Job Control V1 | OPC-10031-4 | `http://opcfoundation.org/UA/ISA95-JOBCONTROL` | 1.0.0 | 2021-03-31 | `Opc.Ua.ISA95.JobControl.V1` |
| Job Control V2 | OPC-10031-4 | `http://opcfoundation.org/UA/ISA95-JOBCONTROL_V2/` | 2.0.0 | 2024-01-31 | `Opc.Ua.ISA95.JobControl.V2` |

All three NodeSet2 XML documents live under [`src/Opc.Ua.ISA95/Design`](../src/Opc.Ua.ISA95/Design) and are compiled by the repository's model source generator (`Opc.Ua.SourceGeneration`, `ModelSourceGeneratorVersion=v105`).

Each NodeSet2 XML file has a hand-maintained identifier CSV sidecar (`*.NodeIds.csv`) that pins every symbolic name to its numeric NodeId and NodeClass.

The V1 and V2 folders additionally carry an `*.Upstream.NodeIds.csv` copy of the identifiers as published by the OPC Foundation, kept side by side with the derived sidecar so the two can be diffed; the Common Model has no upstream copy because its sidecar also encodes the two normative repairs described below.

`Tests/Opc.Ua.ISA95.Tests/ModelAssetTests.cs` validates, from the embedded NodeSet2/CSV assets, that: the `Model` element's `ModelUri`/`Version` and total node count match the values above; every CSV row has a matching NodeId of the correct NodeClass in the NodeSet and vice versa (no orphaned or missing rows); the two normative repairs are present with their links; the V1 methods and the V2 mandatory/optional methods carry the modelling rule published by the source spec; and the V2 status-event type and state-machine causes match the published shape.

## Normative NodeSet repairs

The published OPC-10030 v1.00 NodeSet2 XML has two normative gaps against the specification text itself, both corrected transparently in [`src/Opc.Ua.ISA95/Design/Common/Opc.ISA95.NodeSet2.xml`](../src/Opc.Ua.ISA95/Design/Common/Opc.ISA95.NodeSet2.xml) with an inline XML comment at each repair site.

**`DefinedByMaterialClass` (assigned `ns=1;i=5300`).** OPC-10030 §9.6.2 and Table 76 require this ReferenceType so that `MaterialDefinitionType` can declare its `<MaterialClass>` reference, but the published NodeSet omits the ReferenceType declaration entirely, leaving that declaration orphaned (referencing a ReferenceType that does not exist in the NodeSet). NodeId 5300 is the unused identifier immediately preceding the published `DefinedByMaterialDefinition` (5301), so assigning it here does not collide with any published identifier.

**`AssembledFromSublot` (assigned `ns=1;i=5333`).** OPC-10030 §9.6.8 and Table 77 require this ReferenceType so that `MaterialLotType` can declare its `<AssemblySublot>` reference, but the published NodeSet omits it in the same way. Identifier 5333 follows the published model's highest assigned identifier, so it is also collision-free.

Both repairs restore the ReferenceType declaration (with the correct `HasSubtype` supertype, `InverseName`, and a `Documentation` link to the corresponding OPC-10030 clause) and wire the previously orphaned forward/inverse references on the affected instance declarations (`MaterialDefinitionType` at `ns=1;i=5219`; `MaterialLotType` at `ns=1;i=5232` and `ns=1;i=5259`) so the model round-trips without dangling ReferenceType references.

`ModelAssetTests.CommonModelContainsNormativeMaterialReferenceRepairs` asserts both references exist with the expected source/target NodeIds; run it (see [Validation commands and acceptance](#validation-commands-and-acceptance)) to reproduce this claim yourself.

## Quick start — server

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Opc.Ua.ISA95;
using Opc.Ua.ISA95.Server.Builders;
using Opc.Ua.ISA95.Server.Providers;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddOpcUa()
    .AddServer(options =>
    {
        options.ApplicationName = "MyIsa95Server";
        options.ApplicationUri = "urn:localhost:OPCFoundation:MyIsa95Server";
        options.EndpointUrls.Add("opc.tcp://localhost:62545/MyIsa95Server");
    })
    .AddIsa95Server()
    .ConfigureModel(async (model, ct) =>
    {
        EquipmentClassState reactorClass =
            await model.CreateEquipmentClassAsync(model.Root, "ReactorClass", ct)
                .ConfigureAwait(false);
        EquipmentState reactor =
            await model.CreateEquipmentAsync(model.Root, "Reactor-1", ct)
                .ConfigureAwait(false);
        model.DefinedByEquipmentClass(reactor, reactorClass);
    });

await builder.Build().RunAsync();
```

`AddIsa95Server` registers the default in-memory Job Control provider (V1 and V2) only when no Job Control provider facet is already registered, plus the `Isa95NodeManagerFactory` and `Isa95ServerOptions`.

`ConfigureModel` runs after the ISA-95 root folder is created but before the server accepts connections; it is the extension point for populating OPC-10030 common-model instances.

See [`samples/MinimalIsa95Server/Program.cs`](../samples/MinimalIsa95Server/Program.cs) for a complete example that also creates Personnel, PhysicalAsset, Material (class/definition/lot/sublot), and a provider-backed `GeoSpatialLocationType`.

## Quick start — client

```csharp
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.ISA95.Client;

Isa95Client client = await isa95ClientFactory(ct);

Isa95JobControlDiscovery discovery =
    await client.DiscoverJobControlAsync(ct).ConfigureAwait(false);
Isa95JobControlEndpoint v2 = discovery.V2Endpoints[0];

Isa95JobControlV2Client jobControl = client.CreateJobControlV2Client(
    v2.NodeId,
    discovery.V2Endpoints[1].NodeId,
    discovery.V2Endpoints[2].NodeId);

ulong status = await jobControl.StoreAndStartAsync(
    new Opc.Ua.ISA95.JobControl.V2.ISA95JobOrderDataType
    {
        JobOrderID = "Job-1",
        Priority = 1
    },
    ct: ct).ConfigureAwait(false);
```

`isa95ClientFactory` above is the `Func<CancellationToken, Task<Isa95Client>>` registered by `AddIsa95Client`; resolve it via constructor injection, or construct `Isa95Client` directly from an existing `ISession`/`ManagedSession` and an `ITelemetryContext`.

## Server hosting model

`services.AddOpcUa().AddServer(...).AddIsa95Server(...).ConfigureModel(...)` is the full registration surface:

- `AddIsa95Server(configure, configureJobControl)` registers `Isa95ServerOptions`, the default `InMemoryIsa95JobControlProvider` (unless overridden — see below), and adds `Isa95NodeManagerFactory` to the server's node-manager pipeline. It returns `IIsa95ServerBuilder`.
- `IIsa95ServerBuilder.ConfigureModel(Func<IIsa95ModelBuilder, CancellationToken, ValueTask>)` registers a callback invoked once, after the ISA-95 root folder and Job Control endpoints exist, with a typed `IIsa95ModelBuilder` scoped to that node manager.
- `Isa95NodeManager` (`Opc.Ua.ISA95.Server.Isa95NodeManager`) is a single `FluentNodeManagerBase` that owns all three namespaces at once (Common Model, Job Control V1, Job Control V2) — there is one node manager instance, not one per namespace.
- The `GeoSpatialLocationType` variable exposed by the common model is provider-backed: `IIsa95GeoSpatialLocationProvider` supplies the current value and, optionally, a push-update stream; `Isa95GeoSpatialLocationBinder.Bind` wires an instance's async read hook and background update loop to a provider without ever blocking the stack on synchronous I/O.

**Custom providers.** Register a cohesive custom Job Control provider set **before** calling `AddIsa95Server`. If any Job Control facet is already registered, `AddIsa95Server` does not backfill the other facets from the in-memory provider, preventing unrelated stores from being combined; adding a partial custom provider after the default registration is rejected when the provider aggregate resolves. A complete distributed provider commonly implements `IIsa95JobOrderReceiverV1/V2`, `IIsa95JobResponseProviderV1/V2`, `IIsa95JobResponseReceiverV1/V2`, `IIsa95JobStatusSourceV2`, `IIsa95JobExecutionController`, `IIsa95JobOrderCatalog`, and `IIsa95JobOrderCatalogChangeSource`. `IIsa95GeoSpatialLocationProvider` remains independently replaceable through `AddIsa95GeoSpatialLocationProvider`.

## Common-model builder reference

`IIsa95ModelBuilder` (implemented by `Isa95ModelBuilder`) exposes typed `Create*Async` factory methods for every OPC-10030 primary object and class type, concrete property factories, and relationship helpers constrained to the normative source and target node classes:

- Creation: `CreatePersonnelClassAsync`, `CreatePersonAsync`, `CreateEquipmentClassAsync`, `CreateEquipmentAsync`, `CreatePhysicalAssetClassAsync`, `CreatePhysicalAssetAsync`, `CreateMaterialClassAsync`, `CreateMaterialDefinitionAsync`, `CreateMaterialLotAsync`, `CreateMaterialSublotAsync`, `CreateEquipmentCapabilityTestSpecificationAsync`, `CreatePhysicalAssetCapabilityTestSpecificationAsync`, `CreateQualificationTestSpecificationAsync`, `CreateMaterialTestSpecificationAsync`, `CreateTestResultAsync`, `AddClassPropertyAsync`, `AddPropertyAsync`.
- Classification/aggregation references: `DefinedByPersonnelClass`, `DefinedByEquipmentClass`, `DefinedByPhysicalAssetClass`, `DefinedByMaterialClass`, `DefinedByMaterialDefinition`, `MadeUpOfEquipment`, `MadeUpOfPhysicalAsset`, `MadeUpOfMaterialSublot`, `AssembledFromClass`, `AssembledFromDefinition`, `AssembledFromLot`, `AssembledFromSublot`.
- Testing references: `TestedByEquipmentTest`, `TestedByPhysicalAssetTest`, `TestedByQualificationTest`, `TestedByMaterialTest`, `HasTestResult`, `ResultsForSpecification`.
- Other: `LocatedIn` (a Variable/VariableType instance to `GeoSpatialLocationType`), bidirectional Equipment/PhysicalAsset `ImplementedBy` overloads, `Relate` (escape hatch for any `NodeId` reference type), `BindGeoSpatialLocation` (wraps `Isa95GeoSpatialLocationBinder.Bind`), `RegisterAsync`/`RemoveAsync` (address-space add/remove through the owning node manager).

`AddClassPropertyAsync` and the owner-specific `AddPropertyAsync` overloads always instantiate the concrete OPC-10030 property type for the supplied owner; the abstract `ISA95ClassPropertyType` and `ISA95PropertyType` are never instantiated. Material sublots use the standard `MaterialLotPropertyType`, as defined by the published model.

`model.Root` is the ISA-95 root `FolderState` created by the node manager (organized under the standard `ObjectsFolder`); every `Create*Async` call defaults to parenting under a supplied parent node, typically `model.Root` or a previously created instance.

## Job Control (V1 and V2)

The default `InMemoryIsa95JobControlProvider` implements every V1 and V2 provider interface over a **single shared, version-neutral store** keyed by job-order ID (`Isa95JobCanonicalState`), so a job order created through one Job Control version is visible and controllable through the other.

| Canonical state | V2 term | V1 term |
|---|---|---|
| `NotAllowedToStart` | NotAllowedToStart | Waiting |
| `AllowedToStart` | AllowedToStart | Ready |
| `Running` | Running | Running |
| `Held` | Interrupted / Held | Held |
| `Suspended` | Interrupted / Suspended | Suspended |
| `Completed` (terminal) | Ended / Completed | Completed |
| `Aborted` (terminal) | Aborted | Aborted |
| `Closed` (terminal) | Ended / Closed | Closed |
| `Loaded` (V1 response state) | Running projection | Loaded |
| `Error` (V1 response state) | Aborted projection | Error |

The neutral store preserves every standard V1/V2 job-order and job-response field, including work masters, personnel/equipment/physical-asset/material requirements and actuals, nested parameters/properties, engineering units, and V2 optional-field `EncodingMask` bits. Same-version round trips are lossless; cross-version views project only the fields representable by the target specification.

V1 behavior remains distinct where the specifications differ: `RequestJobResponse` requires exactly one selector (job-order ID or state), V1 `Stop` removes the stored order, and V1-only `Loaded`/`Error` response states round-trip without being collapsed.

V2 interrupted and ended sub-states use the standard two-entry state path: a top-level `Interrupted`/`Ended` entry with an empty browse path, followed by state number `1` or `2` under the `HasSubStateMachine` path. No private composite state numbers are emitted.

**V2 job-order operations** (`Isa95JobOrderOperationV2`, applied through `IIsa95JobOrderReceiverV2.ReceiveJobOrderAsync`):

| Operation | Effect |
|---|---|
| `Store` | Creates a new order → `NotAllowedToStart`. |
| `StoreAndStart` | Creates a new order → `AllowedToStart`. |
| `Start` | `NotAllowedToStart` → `AllowedToStart`. |
| `Update` | Updates a stored, not-yet-started order in place. |
| `Stop` | `Running`/`Held`/`Suspended` → `Completed` (ends it). |
| `Pause` | `Running` → `Suspended` (interrupts it). |
| `Resume` | `Held`/`Suspended` → `Running`. |
| `Abort` | Any non-terminal state → `Aborted`. |
| `RevokeStart` | `AllowedToStart` → `NotAllowedToStart`. |
| `Cancel` | Removes a `NotAllowedToStart`/`AllowedToStart` order from the store. |
| `Clear` | Removes a terminal (`Completed`/`Aborted`/`Closed`) order from the store. |

**Execution-system transitions** (`Isa95JobExecutionTransition`, applied through `IIsa95JobExecutionController.TransitionAsync`) model the automatic, non-client-driven state changes of the OPC-10031-4 V2 execution-system state machine: `BeginExecution` (`AllowedToStart` → `Running`), `Hold` (`Running` → `Held`), `Complete` (`Running`/`Held`/`Suspended` → `Completed`), `Close` (`Completed` → `Closed`). A hosted service (or any application component) drives these once the execution system — not the OPC UA client — decides to start, hold, complete, or close a job order; `samples/MinimalIsa95Server/DemoJobSeeder.cs` calls `StoreAndStart` followed by `BeginExecution` to demonstrate the automatic `AllowedToStart` → `Running` transition.

**Status events.** `IIsa95JobStatusSourceV2.SubscribeAsync` publishes one `Isa95JobStatusNotificationV2` per committed state change to every independent subscriber. Because the standard `ISA95JobOrderStatusEventType` is abstract, `Isa95NodeManager` defines a concrete server-owned subtype and reports that subtype while preserving the standard event fields and type hierarchy. Each notification rewrites `JobResponse.JobState` to the current event state, and the latest non-empty V2 `Comment` array is retained as audit context.

**Catalog changes.** A successful V2 `Update` is a standard self-transition and emits the required status event with the modified order; it also emits an `Updated` catalog change so `JobOrderList` projections refresh. Cancel/Clear/V1-Stop removals emit a `Removed` catalog change without fabricating a life-cycle transition. The node manager consumes `IIsa95JobOrderCatalogChangeSource` so V1/V2 lists stay current even when a shared provider is changed outside the local method handler.

**Return status.** Every Job Control V1/V2 method returns a `UInt64 ReturnStatus` bitmap as defined by Annex B.2 of the two companion specifications; `Isa95JobReturnStatus` names the bits used by this implementation (`Success` = bit 0, `UnknownJobOrderId` = bit 1, `InvalidCommand` = bit 2 (V1), `InvalidStatus` = bit 3, `UnableToAccept` = bit 4, `InvalidRequest` = bit 32) and more than one bit can be set at once.

**Business failures are `Uncertain`, not `Bad`.** When an operation cannot be applied for a business reason (unknown job order, invalid state transition, and similar), the in-memory provider returns `ServiceResult(StatusCodes.Uncertain, ...)` together with the relevant `ReturnStatus` bits, rather than a `Bad_*` status code; the OPC UA `ReturnStatus` output argument is the authoritative outcome channel, and `Uncertain` signals "the call was serviced but the requested business operation did not succeed" as opposed to a protocol-level failure.

**Bounding.** `Isa95JobControlProviderOptions` bounds the in-memory engine: `MaxJobOrders` (default 1024) and `MaxJobResponses` (default 1024) cap concurrent storage and reject new entries beyond the bound; `ResponseRetention` (default `TimeSpan.Zero`, meaning disabled) purges responses older than the configured age using the injected `TimeProvider` before each new response is received or a query is served.

## GeoSpatialLocationType provider seam

OPC-10030's `GeoSpatialLocationType` models a location as a single value (in this implementation, a human-readable string literal such as Well-Known-Text or an address), not the structured GPOS/RSL coordinate model.

`IIsa95GeoSpatialLocationProvider` is intentionally a narrow seam: `GetCurrentAsync` returns an `Isa95GeoSpatialLocation` snapshot (value, `StatusCode`, source timestamp), and `SubscribeAsync` optionally returns an `IAsyncEnumerable` of subsequent updates (`null` when the provider only supports polling).

**RSL/GPOS are explicitly out of scope at runtime.** This repository does not implement the OPC UA Part 210 (Relative Spatial Location, RSL) or Part 211 (Global Positioning, GPOS) information models. The legacy OPC-10030 `GeoSpatialLocationType` string seam is deliberately kept generic so that a future adapter — translating `GlobalPositionType`/`GlobalLocationType` structured data from a Part 211 GPOS source into the string literal (or vice versa) — could be layered on top of `IIsa95GeoSpatialLocationProvider` without any change to the ISA-95 NodeSet or the binder. No such adapter exists today.

## Client

`Isa95Client` (constructed directly from an `ISession`/`ManagedSession` plus `ITelemetryContext`, or resolved through `AddIsa95Client`) is the discovery entry point:

- `DiscoverCommonObjectsAsync(rootNodeId, recursive, ct)` walks the address space below a root using `Session.ManagedBrowseAsync`, which transparently follows `BrowseNext` continuation points, and returns every OPC-10030 primary object or vendor-defined subtype it finds (`Isa95CommonObjectKind`: PersonnelClass, Person, EquipmentClass, Equipment, PhysicalAssetClass, PhysicalAsset, MaterialClass, MaterialDefinition, MaterialLot, MaterialSublot).
- `DiscoverJobControlAsync(rootNodeId, ct)` (and the `ObjectsFolder`-rooted overload) recognizes the standard V1/V2 endpoint ObjectTypes and vendor-defined subtypes, returning every match in `Isa95JobControlDiscovery.V1Endpoints`/`V2Endpoints` so callers can explicitly handle a missing or ambiguous facet instead of an implicit "first match wins" choice.
- `CreatePersonClient`/`CreateEquipmentClient`/`CreatePhysicalAssetClient`/`CreateMaterialLotClient` return generated `*TypeClient` proxies for direct property access.
- `CreateJobControlV1Client`/`CreateJobControlV2Client` return the direct Job Control clients described below.

`Isa95JobControlV1Client` and `Isa95JobControlV2Client` wrap the three generated endpoint-object proxies (Job Order Receiver, Job Response Provider, Job Response Receiver) behind flat, per-operation async methods (`StoreAsync`, `StoreAndStartAsync`, `StartAsync`, `UpdateAsync`, `StopAsync`, `PauseAsync`, `ResumeAsync`, `AbortAsync`, `RevokeStartAsync`, `CancelAsync`, `ClearAsync` for V2; `ReceiveJobOrderAsync` with an explicit command for V1) plus response query/receive methods. Their public constructors idempotently register all Common/V1/V2 encodeables with the supplied session, so direct standalone use is wire-safe; each still exposes the underlying generated proxy for advanced scenarios.

**Typed V2 status-event streaming.** `Isa95JobControlV2Client.SubscribeJobOrderStatusEventsAsync(streaming, notifierId, registry, options, ct)` builds the generated `ISA95JobOrderStatusEventTypeRecord` event filter, registers the V2 event-record decoders, and decodes each `EventNotification` from an `IStreamingSubscription` (typically `session.DefaultStreaming`) into a strongly-typed record — no manual `EventFieldList` unpacking required:

```csharp
IStreamingSubscription streaming = session.DefaultStreaming;
await foreach (ISA95JobOrderStatusEventTypeRecord record in
    jobControl.SubscribeJobOrderStatusEventsAsync(streaming, jobControl.JobResponseProviderId, ct: ct))
{
    // record.JobOrderID, record.State, ...
}
```

`AddIsa95Client` (on `IOpcUaBuilder` or `IOpcUaClientBuilder`) registers `ITelemetryContext`, `Func<ISession, Isa95Client>`, `Func<ManagedSession, Isa95Client>`, and — when `Isa95ClientOptions.LazyConnect` is true (the default) — a `Func<CancellationToken, Task<Isa95Client>>` that lazily acquires the `ManagedSession` registered by `AddClient` on first use and caches the resulting client.

## In-memory limitations and HA guidance

`InMemoryIsa95JobControlProvider` is a single-process, non-durable engine: all job orders, responses, and subscriptions live in process memory guarded by a `System.Threading.Lock`, and are lost on process restart or crash. It is appropriate for development, demos, and single-instance deployments, not for production high-availability topologies.

For a highly-available or horizontally distributed server, implement the provider interfaces (`IIsa95JobOrderReceiverV1/V2`, `IIsa95JobResponseProviderV1/V2`, `IIsa95JobResponseReceiverV1/V2`, `IIsa95JobStatusSourceV2`, `IIsa95JobExecutionController`, `IIsa95JobOrderCatalog`, `IIsa95JobOrderCatalogChangeSource`) against one shared, durable backing store (for example a database or distributed cache reachable by every server instance) and register that cohesive set before calling `AddIsa95Server`. This mirrors the provider-model guidance in [Historical Access](HistoricalAccess.md) and the general redundancy guidance in [High Availability and OPC UA Redundancy](HighAvailability.md): state that must survive failover belongs behind a shared provider, not in server-local memory.

`IIsa95JobStatusSourceV2.SubscribeAsync` and `IIsa95JobOrderCatalogChangeSource.SubscribeCatalogChangesAsync` each provide independent, post-subscription delivery streams. A durable/distributed implementation must preserve the exactly-once-per-committed-mutation contract (or document stronger replay/checkpoint semantics) so event and `JobOrderList` projections remain coherent across server instances.

## Provenance

The Common Model and both Job Control NodeSet2/CSV asset sets in this repository were derived from the following upstream source revisions:

| Asset | Upstream source commit |
|---|---|
| Common Model (`Design/Common`) | `2a11da3...` |
| Job Control V1 (`Design/JobControl/V1`) | `90d4ebe...` |
| Job Control V2 (`Design/JobControl/V2`) | `e0b5d80...` |

The derived `*.NodeIds.csv` sidecars used by the source generator coexist, for V1 and V2, with an unmodified `*.Upstream.NodeIds.csv` copy of the identifiers as published upstream, so the two can be diffed directly; the Common Model sidecar has no separate upstream copy because it already carries the two normative repairs documented above.

`ModelAssetTests.CanonicalNodeSetHasExpectedIdentity` pins each NodeSet2's `ModelUri`, `Version`, and total node count (Common: 390, Job Control V1: 91, Job Control V2: 258) so accidental upstream drift or local edits are caught by CI.

## Conformance matrix

Status key: ✅ Implemented and tested · 📄 Static NodeSet structure only (no runtime provider/server/client behavior exercises it yet) · 🔲 Optional per the published modelling rule.

This matrix distinguishes **static NodeSet structure** (what the NodeSet2 XML declares, validated by asset-level tests against the embedded XML/CSV) from **runtime-tested facets** (behavior exercised through the server, provider, or client at runtime).

| Area | Static structure | Runtime | Source | Tests |
|---|---|---|---|---|
| Common Model primary types and concrete property types (Person/Equipment/PhysicalAsset/Material class+instance hierarchy) | ✅ | ✅ | [`Isa95ModelBuilder`](../src/Opc.Ua.ISA95.Server/Builders/Isa95ModelBuilder.cs) | `ModelAssetTests`; `Isa95ModelBuilderTests`; `Isa95EndToEndTests` |
| `DefinedByMaterialClass` / `AssembledFromSublot` repairs | ✅ | ✅ | [`Opc.ISA95.NodeSet2.xml`](../src/Opc.Ua.ISA95/Design/Common/Opc.ISA95.NodeSet2.xml) | `ModelAssetTests.CommonModelContainsNormativeMaterialReferenceRepairs` |
| `GeoSpatialLocationType` (as a string-provider seam) | ✅ | ✅ | [`Isa95GeoSpatialLocationBinder`](../src/Opc.Ua.ISA95.Server/Builders/Isa95GeoSpatialLocationBinder.cs) | `Isa95GeoSpatialLocationTests`; `Isa95EndToEndTests` |
| GPOS/RSL (Part 210/211) coordinate model | N/A — not implemented | N/A | — | — |
| Job Control V1 `ReceiveJobOrder`/`RequestJobResponse`/`ReceiveJobResponse` (all mandatory, modelling rule `i=78`) | ✅ | ✅ | [`InMemoryIsa95JobControlProvider`](../src/Opc.Ua.ISA95.Server/Providers/InMemoryIsa95JobControlProvider.cs) | `ModelAssetTests.JobControlV1MethodsAreMandatory`; `InMemoryIsa95JobControlProviderV1Tests` |
| Job Control V2 Job Order Receiver operations (Store/StoreAndStart/Start/Update/Stop/Pause/Resume/Abort/RevokeStart/Cancel/Clear — 🔲 optional, modelling rule `i=80`) | ✅ | ✅ | same | `ModelAssetTests.JobControlV2MethodRulesMatchPublishedModel`; `InMemoryIsa95JobControlProviderV2Tests` |
| Job Control V2 response methods (`RequestJobResponseByJobOrderID`, `RequestJobResponseByJobOrderState`, `ReceiveJobResponse` — mandatory, modelling rule `i=78`) | ✅ | ✅ | same | same |
| Job Control V2 state-machine causes and standard state paths (`HasCause`, `HasSubStateMachine`, scoped sub-state numbers) | ✅ | ✅ | same | `ModelAssetTests.JobControlV2StateMachineHasPublishedCauses`; `Isa95V2StateMachineTests` |
| Job Control V1/V2 lossless fields, V1 selectors/Stop/Loaded/Error, and V2 comments | ✅ | ✅ | [`Isa95JobControlConversions`](../src/Opc.Ua.ISA95.Server/Providers/Internal/Isa95JobControlConversions.cs) | `Isa95JobControlConversionsTests`; `InMemoryIsa95JobControlProviderV1Tests`; `InMemoryIsa95JobControlProviderV2Tests` |
| Job Control V2 abstract status-event shape, concrete server subtype, and live delivery | ✅ | ✅ | [`Isa95NodeManager`](../src/Opc.Ua.ISA95.Server/Isa95NodeManager.cs) | `ModelAssetTests.JobControlV2StatusEventHasRequiredShape`; `Isa95JobControlClientTests`; `Isa95EndToEndTests` |
| Catalog mutation projection (`Updated`/`Removed`) | — | ✅ | [`IIsa95JobOrderCatalogChangeSource`](../src/Opc.Ua.ISA95.Server/Providers/IIsa95JobOrderCatalogChangeSource.cs) | `InMemoryIsa95JobControlProviderV2Tests`; `Isa95EndToEndTests` |
| `ReturnStatus` Annex B bitmap / `Uncertain` business-failure result | — | ✅ | [`Isa95JobReturnStatus`](../src/Opc.Ua.ISA95.Server/Providers/Isa95JobReturnStatus.cs) | `InMemoryIsa95JobControlProviderV1Tests`; `InMemoryIsa95JobControlProviderV2Tests` |
| Subtype-aware client discovery (common objects + Job Control endpoints), continuation-safe via `ManagedBrowseAsync` | — | ✅ | [`Isa95Client`](../src/Opc.Ua.ISA95.Client/Isa95Client.cs) | `Isa95JobControlClientTests`; `Isa95EndToEndTests` |
| Typed V2 status-event client streaming | — | ✅ | [`Isa95JobControlV2Client`](../src/Opc.Ua.ISA95.Client/Isa95JobControlV2Client.cs) | `Isa95JobControlClientTests`; `Isa95EndToEndTests` |
| Cohesive server/client DI wiring (`AddIsa95Server`, `AddIsa95Client`) | — | ✅ | [`OpcUaIsa95ServerBuilderExtensions`](../src/Opc.Ua.ISA95.Server/Hosting/OpcUaIsa95ServerBuilderExtensions.cs), [`OpcUaIsa95ClientBuilderExtensions`](../src/Opc.Ua.ISA95.Client/Hosting/OpcUaIsa95ClientBuilderExtensions.cs) | `Isa95ServerBuilderTests`; `Isa95JobControlProviderOptionsTests` |
| Persistent NativeAOT roots | — | ✅ | [`Isa95AotTests`](../tests/Opc.Ua.Aot.Tests/Isa95AotTests.cs) | NativeAOT publish + source-generated test execution |
| Durable/HA Job Control provider | ❌ not shipped (in-memory only; see [In-memory limitations and HA guidance](#in-memory-limitations-and-ha-guidance)) | — | — | — |

## Validation commands and acceptance

The implementation was validated on Windows with the .NET 10 SDK:

- `Opc.Ua.ISA95`, `Opc.Ua.ISA95.Client`, and `Opc.Ua.ISA95.Server` build for every configured library TFM with zero diagnostics.
- `Opc.Ua.ISA95.Tests` passes **137/137** on both `net10.0` and `net48`.
- Source-generator regression suites pass: model generator **81/81**, stack generator **90/90**, and core generator **3718 passed / 8 skipped**.
- Cobertura line coverage is **88.08%** (`Opc.Ua.ISA95`), **90.04%** (`Opc.Ua.ISA95.Client`), and **91.19%** (`Opc.Ua.ISA95.Server`), above the required 80% for every new assembly.
- `MinimalIsa95Server` publishes as self-contained `win-x64` NativeAOT with zero ISA-95 diagnostics. The published executable was started and a typed managed client successfully invoked V2 `Store` and decoded the resulting concrete status-event subtype.
- `Opc.Ua.Aot.Tests` now references the model/client/server packages; its source-generated ISA-95 NativeAOT smoke test publishes and passes in the native executable.
- OPC UA KB `validate_nodeset` reported zero errors for the exact three upstream revisions. The inherited official warning baseline is 292 warnings for Common, 52 for V1, and 178 for V2; the local Common asset adds the two documented normative repairs and is covered by XML/CSV parity tests.
- Live interoperability against the NativeAOT executable confirmed all three namespaces, the representative Common Model graph, the WKT geospatial value, all V1/V2 facet objects, every V2 method node, and correct negative status codes. The generic MCP client could not marshal custom ExtensionObjects/`LocalizedText[]` or create event monitored items, so positive structured method/event behavior was closed by the typed end-to-end client test instead.

The V2 Job Order Receiver StateMachine describes metadata for structured job-order values rather than one live receiver state. As required by OPC-10031-4 §6.2.1.2, the inherited mandatory `CurrentState` therefore returns a Bad StatusCode; clients use each order's structured state in `JobOrderList` and status events instead.

Run the ISA-95 asset, unit, and end-to-end suite with:

```powershell
$env:CustomTestTarget = "net10.0"
dotnet test Tests\Opc.Ua.ISA95.Tests\Opc.Ua.ISA95.Tests.csproj -c Release
```

To additionally exercise the sample server end to end, run it and connect with any OPC UA client (or the [`ConsoleReferenceClient`](../samples/ConsoleReferenceClient/README.md)):

```powershell
dotnet run --project samples\MinimalIsa95Server\MinimalIsa95Server.csproj
```

To rebuild the source-generated types and re-validate the NodeSet2/CSV identifier sidecars against the source generator itself, build the `Opc.Ua.ISA95` project (part of `UA.slnx`):

```powershell
dotnet build src\Opc.Ua.ISA95\Opc.Ua.ISA95.csproj
```

The KB `check_compliance` operation is intended for exported implementation NodeSets, not for comparing a companion specification's own source NodeSet against itself; doing the latter reports false “missing mandatory node” findings. Runtime conformance is therefore tracked by the matrix above and the live-address-space interoperability tests rather than treating those self-comparison findings as implementation defects.
