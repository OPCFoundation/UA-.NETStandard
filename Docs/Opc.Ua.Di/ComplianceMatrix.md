# OPC UA DI (OPC 10000-100) Compliance Matrix

> **Scope**: Implementation roadmap for the `Opc.Ua.Di.Server` /
> `Opc.Ua.Di.Client` libraries. This document is the single source of
> truth for Phase 8 deliverables.

This matrix enumerates every ObjectType, Method, ReferenceType,
VariableType and DataType declared in
[`OpcUaDiModel.xml`](../../Libraries/Opc.Ua.Di/Design/OpcUaDiModel.xml)
(OPC UA DI v1.05) and maps each to one of four states:

- **In v1** — Implemented by Phase 8B–8F. Mandatory for the developer
  experience this work delivers.
- **Generated** — Already covered by the source-generated model
  library (`Opc.Ua.Di`). No additional server/client work needed.
- **Deferred** — Useful but not blocking v1. Tracked in this doc as
  a follow-up.
- **Excluded** — Out of scope for this library (e.g. Onboarding,
  CommunicationProfile).

## Foundation — `TopologyElementType` & nameplates

`TopologyElementType` is the abstract base for every node in the DI
device topology. Every concrete DI device or component inherits from
it (or its `ComponentType` subtype).

| Type | Kind | Status | Deliverable |
|------|------|--------|-------------|
| `TopologyElementType` | ObjectType | **In v1** | Abstract base; child FunctionalGroups and references attached by `IDeviceBuilder` |
| `IVendorNameplateType` | Interface | **In v1** | `IDeviceBuilder.WithIdentification(action)` populates Manufacturer, Model, SerialNumber, HardwareRevision, SoftwareRevision, DeviceRevision, DeviceManual, DeviceClass, ProductInstanceUri, ProductCode |
| `ITagNameplateType` | Interface | **In v1** | Same builder, separate group for AssetId / ComponentName / DeviceRevision |
| `IDeviceHealthType` | Interface | **In v1** | Surfaces DeviceHealth enum + the 4 NAMUR alarm references |
| `ISupportInfoType` | Interface | **Deferred** | Optional support folders (DeviceTypeImage, Documentation, ProtocolSupport, ImageSet) — can plug into FileSystemProvider in a follow-up |
| `IAssetLocationIndicationType` | Interface | **In v1** | `IDeviceBuilder.OnStartLocationIndication(handler)` + `OnStopLocationIndication(handler)` |
| `IOperationCounterType` | Interface | **In v1** | `IDeviceBuilder.WithOperationCounters(...)` populates PowerOnDuration, OperationDuration, EstimatedReturnedOperationDuration, EstimatedReturnedPowerOnDuration |

### Methods

| Method | Status | Deliverable |
|--------|--------|-------------|
| `StartLocationIndication` | **In v1** | Wired via `IDeviceBuilder.OnStartLocationIndication` |
| `StopLocationIndication` | **In v1** | Wired via `IDeviceBuilder.OnStopLocationIndication` |

## Device tree — `ComponentType` / `DeviceType` / `BlockType`

| Type | Status | Deliverable |
|------|--------|-------------|
| `ComponentType` | **In v1** | Abstract; `IDeviceBuilder` materializes via G1.CreateInstance |
| `DeviceType` | **In v1** | `IDeviceBuilder.WithDeviceType<TDeviceState>(factory)` |
| `SoftwareType` | **Deferred** | Modelled as a `ComponentType` for representing a software component on a device — punted to a follow-up |
| `BlockType` | **Deferred** | Used for device blocks (configurable functional units) — out of v1 scope |
| `ConfigurableObjectType` | **Deferred** | Generic "containers of configurable children" — follow-up |

## Topology references

| Reference | Status | Deliverable |
|-----------|--------|-------------|
| `ConnectsTo` | **In v1** | `IDeviceBuilder.ConnectsTo(other)` |
| `ConnectsToParent` | **In v1** | `IDeviceBuilder.ConnectsToParent(other)` |
| `IsOnline` | **In v1** | `IDeviceBuilder.WithOnlineComponent(...)` |
| `UpdateParent` | **In v1** | Used by software update — exposed via `ISoftwareUpdateBuilder` |
| `CanUpdate` | **In v1** | Used by software update — exposed via `ISoftwareUpdateBuilder` |

## Topology containers — `DeviceSet`, `NetworkSet`, `DeviceTopology`

| Object | Status | Deliverable |
|--------|--------|-------------|
| `DeviceSet` (well-known instance) | **In v1** | `DiNodeManager` auto-creates under `ObjectsFolder`; `IDeviceBuilder` parents devices here |
| `NetworkSet` (well-known instance) | **In v1** | `IDeviceTopology.AddNetwork(...)` |
| `DeviceTopology` (well-known instance) | **In v1** | Browse-only; topology references attach here |
| `NetworkType` | **In v1** | Exposes lock + protocol per-network |
| `ConnectionPointType` | **In v1** | Connection between a network and a device |
| `ProtocolType` | **In v1** | Per-network protocol metadata |

## Lock service

| Type | Status | Deliverable |
|------|--------|-------------|
| `LockingServicesType` | **In v1** | `IDeviceBuilder.WithLockService(provider)` attaches a Lock child + 4 methods |
| `InitLockMethodType` / `InitLock` | **In v1** | Wired by `DefaultLockService.InitLockAsync` |
| `RenewLockMethodType` / `RenewLock` | **In v1** | `DefaultLockService.RenewLockAsync` |
| `ExitLockMethodType` / `ExitLock` | **In v1** | `DefaultLockService.ExitLockAsync` |
| `BreakLockMethodType` / `BreakLock` | **In v1** | `DefaultLockService.BreakLockAsync` |

### `ILockService` contract

```csharp
public interface ILockService
{
    ValueTask<LockResult> InitLockAsync(LockContext ctx, string user, CancellationToken ct);
    ValueTask<LockResult> RenewLockAsync(LockContext ctx, CancellationToken ct);
    ValueTask<LockResult> ExitLockAsync(LockContext ctx, CancellationToken ct);
    ValueTask<LockResult> BreakLockAsync(LockContext ctx, CancellationToken ct);
    LockState QueryLockState(NodeId targetNode);
}
```

`DefaultLockService` v1 ships with: session ownership, configurable
timeout (default 5 min), renew, break-lock policy hook, automatic
cleanup on session close.

## Software update — `SoftwareUpdateType`

Software update is the largest single feature. Phase 8E + 8F implement
the server orchestration + client. Spec-fixed state machines are
**not** exposed in the public API; the `ISoftwareUpdateBuilder` exposes
domain operations.

| Type | Kind | Status | Deliverable |
|------|------|--------|-------------|
| `SoftwareUpdateType` | ObjectType | **In v1** | `IDeviceBuilder.WithSoftwareUpdate(configure)` returns `ISoftwareUpdateBuilder` |
| `SoftwareLoadingType` (abstract) | ObjectType | **In v1** | Internal — base for the 3 loading variants |
| `PackageLoadingType` (abstract) | ObjectType | **In v1** | Internal |
| `DirectLoadingType` | ObjectType | **In v1** | `ISoftwareUpdateBuilder.UseDirectLoading()` — client uploads bytes via the spec's FileType node |
| `CachedLoadingType` | ObjectType | **In v1** | `ISoftwareUpdateBuilder.UseCachedLoading()` — adds the `GetUpdateBehavior` method |
| `FileSystemLoadingType` | ObjectType | **In v1** | `ISoftwareUpdateBuilder.UseFileSystemLoading(provider)` — backs by `IFileSystemProvider` |
| `SoftwareVersionType` | ObjectType | **In v1** | Surfaces Manufacturer, ProductInstanceUri, SoftwareRevision, PatchIdentifiers, ReleaseDate, ChangeLog, Hash |
| `SoftwareFolderType` | ObjectType | **Deferred** | Multi-version repository — follow-up |
| `PrepareForUpdateStateMachineType` | ObjectType | **In v1** | Internal — driven by `ISoftwareUpdateBuilder.OnPrepareForUpdate(handler)` |
| `InstallationStateMachineType` | ObjectType | **In v1** | Internal — driven by `OnInstall(handler)` |
| `PowerCycleStateMachineType` | ObjectType | **In v1** | Internal — driven by `OnPowerCycleRequired(handler)` |
| `ConfirmationStateMachineType` | ObjectType | **In v1** | Internal — driven by `OnConfirm(handler)` |

### State-transition method coverage

| Method | Status |
|--------|--------|
| `Prepare` / `Abort` / `Resume` (PrepareForUpdate) | **In v1** — wired internally |
| `InstallSoftwarePackage` / `InstallFiles` / `Uninstall` / `Resume` (Installation) | **In v1** |
| `Confirm` (Confirmation) | **In v1** |
| `GetUpdateBehavior` (CachedLoading + FileSystemLoading) | **In v1** |
| `ValidateFiles` (FileSystemLoading) | **In v1 (optional)** |
| `Clear` (SoftwareVersion) | **In v1 (optional)** |
| `Add` / `Delete` (SoftwareFolder) | **Deferred** |

### `ISoftwareUpdateBuilder` contract (Phase 8E)

```csharp
public interface ISoftwareUpdateBuilder
{
    ISoftwareUpdateBuilder UseDirectLoading();
    ISoftwareUpdateBuilder UseCachedLoading();
    ISoftwareUpdateBuilder UseFileSystemLoading(IFileSystemProvider provider);

    ISoftwareUpdateBuilder OnPackageUploaded(
        Func<PackageUploadedContext, CancellationToken, ValueTask> handler);
    ISoftwareUpdateBuilder OnPrepareForUpdate(
        Func<PrepareContext, CancellationToken, ValueTask<ServiceResult>> handler);
    ISoftwareUpdateBuilder OnInstall(
        Func<InstallContext, CancellationToken, ValueTask<ServiceResult>> handler);
    ISoftwareUpdateBuilder OnPowerCycleRequired(
        Func<PowerCycleContext, CancellationToken, ValueTask> handler);
    ISoftwareUpdateBuilder OnConfirm(
        Func<ConfirmContext, CancellationToken, ValueTask<ServiceResult>> handler);
    ISoftwareUpdateBuilder OnFailure(
        Func<FailureContext, CancellationToken, ValueTask> handler);

    ISoftwareUpdateBuilder WithSoftwareVersion(SoftwareVersionInfo info);
}
```

The 4 underlying state machines are managed by an internal
`SoftwareUpdateController` that operates on the generated
`FiniteStateMachineState` subclasses directly (no fluent state-machine
builder dependency — see `Docs/plans/StateMachineBuilder.md` for the
deferred generalization).

## Transfer services (TransferServices)

| Type | Status | Notes |
|------|--------|-------|
| `TransferServicesType` | **Deferred** | Used to up/download large data blobs (sets of parameters) — separate from software update |
| `TransferToDevice` / `TransferFromDevice` / `FetchTransferResultData` | **Deferred** | Follow-up |

## NAMUR alarms (DeviceHealth)

| Type | Status | Deliverable |
|------|--------|-------------|
| `DeviceHealthDiagnosticAlarmType` (abstract) | **Generated** | Already in source-gen output |
| `FailureAlarmType` | **Generated** | Generated; wired via existing G2 + G7 (Phase 7) |
| `CheckFunctionAlarmType` | **Generated** | Same |
| `OffSpecAlarmType` | **Generated** | Same |
| `MaintenanceRequiredAlarmType` | **Generated** | Same |

These don't require additional library work; consumers already use
the Phase 7 G2 `IAlarmBuilder<TState>` + G7 `ActivatesAlarm` patterns
to wire them.

## Lifetime indication

| Type | Status | Deliverable |
|------|--------|-------------|
| `LifetimeVariableType` | **Deferred** | Optional analog lifetime indicator — follow-up |
| `BaseLifetimeIndicationType` + 7 subtypes | **Deferred** | TimeIndication, NumberOfPartsIndication, NumberOfUsagesIndication, LengthIndication, DiameterIndication, SubstanceVolumeIndication — all follow-up |

## FunctionalGroups

`FunctionalGroupType` is the central organising pattern in DI. The
spec defines 8 well-known groups; v1 supports all 8 via typed builders.

| Group | Status | Builder method |
|-------|--------|---------------|
| `Identification` | **In v1** | `IDeviceBuilder.WithIdentificationGroup(g => ...)` |
| `Configuration` | **In v1** | `IDeviceBuilder.WithConfigurationGroup(g => ...)` |
| `Maintenance` | **In v1** | `IDeviceBuilder.WithMaintenanceGroup(g => ...)` |
| `Diagnostics` | **In v1** | `IDeviceBuilder.WithDiagnosticsGroup(g => ...)` |
| `Statistics` | **In v1** | `IDeviceBuilder.WithStatisticsGroup(g => ...)` |
| `Status` | **In v1** | `IDeviceBuilder.WithStatusGroup(g => ...)` |
| `Operational` | **In v1** | `IDeviceBuilder.WithOperationalGroup(g => ...)` |
| `OperationCounters` | **In v1** | `IDeviceBuilder.WithOperationCountersGroup(g => ...)` |

Custom groups are still supported via the generic G6 `AddObject` +
`Organizes` references.

## DataTypes & VariableTypes

All DI DataTypes (`DeviceHealthEnumeration`, `SoftwareClass`,
`LocationIndicationType`, `SoftwareVersionFileType`, `UpdateBehavior`,
`FetchResultDataType`, `TransferResultErrorDataType`,
`TransferResultDataDataType`, `ParameterResultDataType`) are
**Generated** — already present in the model library.

`UIElementType` (VariableType for user interface element data) is
**Generated** but client/server wiring is **Deferred** (renderer-side
concern).

## Onboarding (separate companion in `OpcUaOnboardingModel.xml`)

| Status | Note |
|--------|------|
| **Excluded** | Onboarding is a separate companion concern (tickets, identities, managed applications, certificate authorities). Will ship as `Opc.Ua.Onboarding.Server` / `.Client` in a separate phase. Generated model types remain available via the `Opc.Ua.Di` library so future consumers can build on them. |

## PackageMetadata (separate companion in `OpcUaDiPackageMetadataModel.xml`)

| Status | Note |
|--------|------|
| **Generated only** | DataTypes (`PackageMetadata`, `UpdateTarget`, `FileDescriptor`, etc.) are present in the model library. v1 software update accepts a `PackageMetadata` parameter in `OnPackageUploaded` but does not implement metadata-driven dispatch (the application handler can read the metadata and decide). |

---

## Phase-to-deliverable summary

| Phase | Files produced | Tests |
|-------|---------------|-------|
| **8B** Device builder, FunctionalGroups, Topology | `Builders/IDeviceBuilder.cs`, `Builders/DeviceBuilder.cs`, `Builders/IFunctionalGroupBuilder.cs`, `Topology/IDeviceTopology.cs`, `Topology/DeviceTopology.cs` | ~15 |
| **8C** Lock service, StartLocationIndication | `Lock/ILockService.cs`, `Lock/DefaultLockService.cs`, `Lock/LockContext.cs`, `Lock/LockState.cs`, `Lock/LockResult.cs`, extensions on `IDeviceBuilder` | ~12 |
| **8D** Client typed helpers | `DiDeviceClient.cs` (expanded), `TopologyClient.cs`, `LockClient.cs` | ~15 |
| **8E** Software update server | `SoftwareUpdate/ISoftwareUpdateBuilder.cs`, `SoftwareUpdate/SoftwareUpdateController.cs`, `SoftwareUpdate/ISoftwarePackageStore.cs`, `SoftwareUpdate/FileSystemPackageStore.cs`, `SoftwareUpdate/SoftwareUpdateFileAdapter.cs`, `SoftwareUpdate/MemoryFileSystemProvider.cs` (test helper) | ~15 |
| **8F** Software update client + pump integration | `SoftwareUpdateClient.cs`, `Applications/MinimalPumpServer/SoftwareUpdate/*.cs` (segregated partial) | ~10 |
| **8G** Documentation | `Docs/Opc.Ua.Di/*.md` (5 guides) | — |

**Total**: ~67 new tests across the 5 implementation phases (8B–8F).

## Items deferred to follow-up phases

- State-machine fluent configurator (see `Docs/plans/StateMachineBuilder.md`)
- `SoftwareFolderType`, `SoftwareVersionType` repositories (multi-version)
- `BlockType`, `ConfigurableObjectType` (configurable functional units)
- `BaseLifetimeIndicationType` + subtypes (analog lifetime indicators)
- `ISupportInfoType` (DeviceTypeImage, Documentation, ProtocolSupport, ImageSet folders)
- `TransferServicesType` (parameter blob transfer)
- `UIElementType` rendering
- Onboarding companion (`Opc.Ua.Onboarding.*`)

## References

- [OPC 10000-100 — Devices](https://reference.opcfoundation.org/specs/OPC-10000/100) — base DI specification.
- [OPC 10000-100 §8.5](https://reference.opcfoundation.org/specs/OPC-10000-100/v105/docs/8.5) — software update state diagrams.
- [`OpcUaDiModel.xml`](../../Libraries/Opc.Ua.Di/Design/OpcUaDiModel.xml) — source-of-truth ModelDesign XML for this library.
- [`CompanionSpecLibraries.md`](../CompanionSpecLibraries.md) — generic
  pattern for packaging a companion specification.
