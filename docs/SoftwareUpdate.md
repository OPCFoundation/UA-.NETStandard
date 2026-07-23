# Device Integration (DI) — Software Update Facet

> **Glossary:** "DI" throughout this document means OPC UA
> [Device Integration (OPC 10000-100)](https://reference.opcfoundation.org/specs/OPC-10000-100),
> *not* .NET Dependency Injection.

This document explains how to expose the OPC 10000-100 §10.3
software-update facet on a Device Integration (DI) device, what
address-space surface it creates, and how clients drive it.

## Address-space layout

`WithSoftwareUpdate(...)` materialises one
`SoftwareUpdateType` instance per device with the following children:

```
{device}                              (ComponentState / DeviceState)
└─ SoftwareUpdate                     SoftwareUpdateType    (OPC-DI i=1)
   ├─ Loading                         {Package|Direct|Cached}LoadingType
   │                                   (default: PackageLoadingType i=137)
   ├─ PrepareForUpdate                PrepareForUpdateStateMachineType (i=213)
   │   ├─ CurrentState
   │   ├─ Prepare                     (Method)
   │   ├─ Abort                       (Method)
   │   └─ Resume                      (Method)
   ├─ Installation                    InstallationStateMachineType (i=249)
   │   ├─ CurrentState
   │   ├─ InstallSoftwarePackage      (InstallSoftwarePackageMethodType i=389)
   │   ├─ InstallFiles                (InstallFilesMethodType)
   │   ├─ Uninstall                   (Method)
   │   └─ Resume                      (Method)
   ├─ PowerCycle                      PowerCycleStateMachineType (i=285)
   └─ Confirmation                    ConfirmationStateMachineType (i=307)
       └─ Confirm                     (Method)
```

The Loading subtype is configurable: `UsePackageLoading()` (default,
file transfer + `CloseAndCommit`), `UseDirectLoading()`,
`UseCachedLoading()`. The full structure is added to the
`AsyncCustomNodeManager`'s `PredefinedNodes` via
`AddPredefinedNodeAsync`, so direct NodeId lookup, browse, subscription
wiring, and method calls all work out of the box.

## Server side — fluent surface

```csharp
using Opc.Ua.Di.Server.Builders;
using Opc.Ua.Di.Server.SoftwareUpdate;

// 1) Register a server-wide package store (commonly via
//    .NET Microsoft.Extensions.DependencyInjection).
services.AddSingleton<ISoftwarePackageStore, MemoryPackageStore>();

// 2) Inside ConfigureDevicesFor<DiNodeManager>:
device.WithSoftwareUpdate(
    ctx.GetRequiredService<ISoftwarePackageStore>(),
    su => su.UsePackageLoading());
```

`WithSoftwareUpdate` auto-registers a per-device
`MemorySoftwareFolder` keyed by the device's NodeId. Override with
`.WithSoftwareFolder(folder)` if you need a custom folder
implementation (e.g. `FileSystemSoftwareFolder` for persistence
between server restarts).

### Method-handler hooks

The library ships "succeed immediately" defaults for every method so
the sample is fully exercisable out of the box. Override individual
hooks to drive the real firmware-flashing logic:

```csharp
device.WithSoftwareUpdate(packageStore, su => su
    .OnPrepare(async (ctx, ct) =>
    {
        // Validate the staged package, lock the device, etc.
        await myDevice.StartPreparationAsync(ct);
    })
    .OnInstall(async (ctx, package, ct) =>
    {
        // Flash the firmware. Throw to fail the state machine.
        await myDevice.FlashAsync(package, ct);
    })
    .OnConfirm(async (ctx, success, ct) =>
    {
        await myDevice.CommitOrRollbackAsync(success, ct);
    })
    .OnUninstall(async (ctx, ct) =>
    {
        await myDevice.UninstallAsync(ct);
    }));
```

`ISoftwareUpdateContext` exposes the device NodeId, the server's
system context, the package store, and the per-device software
folder so handlers can persist version metadata without having to
re-resolve any of these.

### Server-side state reporting

The four state machines (`PrepareForUpdate`, `Installation`,
`PowerCycle`, `Confirmation`) are pre-seeded to their initial state
(`Idle` / `NotWaitingForConfirm` / `NotWaitingForPowerCycle`) when
the SU facet attaches. Every successful method invocation walks the
FSM through the standard Part 16 transitions and publishes
`CurrentState` / `LastTransition` updates that subscribers observe in
real time. Failures route to the Error state (Installation) or back
to Idle (PrepareForUpdate), again with proper `LastTransition`
metadata. The Installation FSM also exposes a per-operation
`PercentComplete` byte that walks 0 → 100 on success.

To plug application instrumentation into the same lifecycle without
having to subscribe through the address space, register the
state-changed hooks:

```csharp
device.WithSoftwareUpdate(packageStore, su => su
    .OnInstall(async (ctx, package, ct) => await myDevice.FlashAsync(package, ct))
    .OnInstallationStateChanged((ctx, change) =>
    {
        _logger.LogInformation(
            "Install on device {Device}: phase={Phase} progress={Pct}% message={Msg}",
            ctx.DeviceId, change.Phase, change.ProgressPercent, change.Message);
        return default;
    }));
```

Each hook fires twice per method call — `Started` before the
application callback runs, then `Completed` on success or `Failed`
(with the exception message) on failure. The hook is invoked from the
service call's async context; exceptions thrown by the hook are
logged and swallowed so instrumentation faults never abort the
underlying SU operation. Domain-keyed `SoftwareUpdatePhase` (rather
than raw Part 16 cause / transition ids) keeps application code
independent of dispatcher internals.

> **Note** — the source-generated DI FSMs ship without Part 16
> `StateTable` / `TransitionTable` / `CauseMappings` overrides, so
> `FiniteStateMachineState.SetState(...)` (and by extension
> `StateMachineBuilder.For(...).WithCause(...)`) is a no-op against
> them today. The SU wiring works around this by writing
> `CurrentState` / `LastTransition` directly via the internal
> `SoftwareUpdateStateMachineDispatcher` helper. Once the generator
> emits the Part 16 tables (tracked as a separate generator
> enhancement) the dispatcher can collapse into a thin
> `StateMachineBuilder.For(...)` adapter without touching the public
> hook surface.

## Storage abstractions

Two independent storage abstractions live under
`Opc.Ua.Di.Server.SoftwareUpdate`:

| Type | Role |
|---|---|
| `ISoftwarePackageStore` | Server-wide repository: Add/Get/List/Delete by id + binary payload. Backs the Loading file-transfer pipeline. |
| `ISoftwareFolder` | Per-device multi-version archive (Current / Previous / Future). Updated by the default Install handler. |

Default implementations:

| Implementation | Use case |
|---|---|
| `MemoryPackageStore` | In-process tests + samples. |
| `FileSystemPackageStore` | Disk-backed, composed over `IFileSystemProvider`. |
| `MemorySoftwareFolder` | Default for `WithSoftwareUpdate`. |
| `FileSystemSoftwareFolder` | Persistence across server restarts. |

## File-transfer pipeline

When `UsePackageLoading()` is selected (the default), the SU facet
materialises a `TemporaryFileTransferType` instance under
`SoftwareUpdate.Loading.FileTransfer`. The internal
`SoftwareUpdateFileTransferManager` implements the OPC 10000-5 §11.4
two-tier protocol:

1. Client calls `Loading.FileTransfer.GenerateFileForWrite(generateOptions)`.
   The server allocates a fresh handle, creates a transient
   `FileType` child (named `UploadFile_<handle>`) backed by an
   in-memory buffer, and returns the file NodeId + handle. The
   `generateOptions` argument is treated as a vendor-defined hint;
   the default manager interprets a single `String` value as the
   suggested package id.
2. Client opens the returned FileState in `Write|EraseExisting` mode
   (mode 6), streams chunks via the standard `Write` method, and
   closes it.
3. Client calls `Loading.FileTransfer.CloseAndCommit(fileHandle)`.
   The server reads the buffered payload, packages it into a
   `SoftwarePackage` (id from the `generateOptions` hint or a
   timestamped fallback), hands it to
   `ISoftwarePackageStore.AddAsync(metadata, stream)`, and removes
   the transient FileState from the address space.

Operational caps (defaults, configurable via constants on
`SoftwareUpdateFileTransferManager`):

| Cap | Value |
|---|---|
| Concurrent upload handles per FileTransfer | 8 |
| Max buffered upload size | 64 MiB |
| Supported open mode | `Write \| EraseExisting` (6); other modes are rejected with `BadNotSupported` |
| Read on a transient upload file | rejected with `BadNotSupported` |

Handles are owned by the session that allocated them; cross-session
access is rejected with `BadUserAccessDenied`.

> **Note** — `DirectLoadingType` and `CachedLoadingType` inherit
> `PackageLoadingType` and therefore expose the same FileTransfer
> slot. The wiring is shared because the upload semantics are
> identical; the difference between Package / Direct / Cached lives
> in the application's `OnInstall` handler (which decides whether to
> deploy immediately, atomically swap, or stage as fallback).

## Client side

The minimal v1 `SoftwareUpdateClient` reads the device's
`SoftwareVersion` property:

```csharp
var client = new SoftwareUpdateClient(
    session, softwareUpdateNodeId, telemetry);
string version = await client.ReadSoftwareVersionAsync();
```

### Uploading a package

`SoftwareUpdateClient.UploadPackageAsync` drives the full
GenerateForWrite → Open → Write* → Close → CloseAndCommit flow,
streaming the payload in `chunkSizeBytes`-sized chunks (default 8 KiB):

```csharp
var client = new SoftwareUpdateClient(session, suNodeId, telemetry);

// byte-array convenience overload
long bytesUploaded = await client.UploadPackageAsync(
    payload: firmwareBytes,
    suggestedPackageId: "acme-firmware-2.0.0",
    ct: ct);

// streaming overload (does not seek the stream)
using FileStream fs = File.OpenRead("firmware.bin");
await client.UploadPackageAsync(
    payload: fs,
    suggestedPackageId: "acme-firmware-2.0.0",
    chunkSizeBytes: 16 * 1024,
    ct: ct);
```

After commit the package is queryable via the server's
`ISoftwarePackageStore.GetAsync(suggestedPackageId)`. The recommended
deployment flow is: upload via `UploadPackageAsync(...)` → drive the
state machines via `PrepareAsync` / `InstallSoftwarePackageAsync` /
`ConfirmAsync` as described below.

### Typed Part 16 state-machine surface

`SoftwareUpdateClient` exposes typed accessors for the four child
state machines (`PrepareForUpdate`, `Installation`, `Confirmation`,
`PowerCycle`), each composed over the source-generated
`*StateMachineTypeClient` proxies and the generic Part 16 extensions
(`GetCurrentFiniteStateAsync`, `ObserveFiniteTransitionsAsync`,
`WaitForStateAsync`). The proxies are resolved lazily on first use
via `SoftwareUpdateTypeClient.GetXxxAsync(telemetry, ct)` and cached
for the lifetime of the client so the browse-path-translate
round-trip happens at most once per state machine.

```csharp
var client = new SoftwareUpdateClient(session, suNodeId, telemetry);

// Snapshot the current state of any of the four FSMs.
FiniteStateSnapshot? install = await client.GetInstallationStateAsync(ct);
FiniteStateSnapshot? prep    = await client.GetPrepareForUpdateStateAsync(ct);

// Drive cause methods (resolved against the typed proxy — no manual
// TranslateBrowsePath / method-NodeId look-up required).
await client.PrepareAsync(ct);
await client.InstallSoftwarePackageAsync(
    manufacturerUri: "urn:acme",
    softwareRevision: "2.0.0",
    patchIdentifiers: ArrayOf.Empty<string>(),
    hash: default,
    ct: ct);
await client.ConfirmAsync(ct);

// Stream transitions via a streaming subscription.
await foreach (FiniteStateSnapshot snap in client
    .ObserveInstallationTransitionsAsync(streaming, options: null, ct))
{
    _logger.LogInformation("Installation moved to {State}", snap.CurrentState);
}
```

When the server does not expose the optional child the `Get*StateAsync`
overloads return `null`; the cause-method wrappers
(`PrepareAsync`, `InstallSoftwarePackageAsync`, `ConfirmAsync`, etc.)
throw `ServiceResultException(BadNotFound)` so calling code never
needs to redo browse-path resolution. See
[`StateMachines.md`](StateMachines.md) for the generic state-machine
API the typed wrappers build on.

## Hosted-server walkthrough

Attach the software-update facet to a `ComponentType`-derived device
created through the Device Integration builder:

```csharp
services.AddSingleton<ISoftwarePackageStore, MemoryPackageStore>();

services
    .AddOpcUa()
    .AddServer(o =>
    {
        o.ApplicationName = "DeviceIntegrationServer";
        o.EndpointUrls.Add($"opc.tcp://localhost:{port}/DeviceIntegrationServer");
    })
    .AddOpcUaDi()
    .ConfigureDevicesFor<DiNodeManager>(async ctx =>
    {
        IDeviceBuilder<DeviceState> device = await ctx.CreateDeviceAsync(
            new QualifiedName("Controller #1", ctx.Manager.DiNamespaceIndex));

        device.WithIdentification(id =>
        {
            id.Manufacturer = new LocalizedText("Acme Controls");
            id.Model = new LocalizedText("Controller 2000");
            id.SerialNumber = "SN-DI-1";
            id.SoftwareRevision = "2.5.3";
        });

        var packageStore = ctx.GetRequiredService<ISoftwarePackageStore>();
        device.WithSoftwareUpdate(packageStore, su => su.UsePackageLoading());
    });

await builder.Build().RunAsync();
```

A client connecting to the running server will discover the SU
subtree under `Controller #1.SoftwareUpdate`, can browse the state
machines, and invoke `InstallSoftwarePackage`,
`PrepareForUpdate.Prepare`, `Confirmation.Confirm`, etc. The
library-supplied default handlers record the installed version in the
per-device `MemorySoftwareFolder`, so a subsequent read of the
device's `SoftwareVersion` reflects the new version.

## Implementation pointers

- Fluent surface: `src/Opc.Ua.Di.Server/Builders/ISoftwareUpdateBuilder.cs`
  and `DeviceBuilderSoftwareUpdateExtensions.cs`.
- Address-space wiring: `SoftwareUpdateFacetWiring.cs` (internal).
- State-machine dispatcher: `SoftwareUpdateStateMachineDispatcher.cs`
  (internal — direct `CurrentState` / `LastTransition` writes until
  the model source generator emits Part 16 tables for `*StateMachineState`).
- File-transfer pipeline:
  `src/Opc.Ua.Di.Server/Builders/SoftwareUpdateFileTransferManager.cs`
  (internal — `GenerateFileForWrite` / `CloseAndCommit` + per-handle
  transient `FileType` materialisation).
- Storage abstractions:
  `src/Opc.Ua.Di.Server/SoftwareUpdate/ISoftwarePackageStore.cs` and
  `ISoftwareFolder.cs`.
- Client helpers: `src/Opc.Ua.Di.Client/SoftwareUpdateClient.cs`
  (read-only discovery),
  `src/Opc.Ua.Di.Client/SoftwareUpdateClient.StateMachine.cs`
  (typed Part 16 partial), and
  `src/Opc.Ua.Di.Client/SoftwareUpdateClient.Upload.cs`
  (`UploadPackageAsync` driving the FileTransfer pipeline).
- Tests:
  `tests/Opc.Ua.Di.Tests/DeviceBuilderSoftwareUpdateTests.cs`,
  `DeviceBuilderSoftwareUpdateStateChangeTests.cs`,
  `SoftwareUpdateFileTransferTests.cs` (server side),
  `SoftwareUpdateClientStateMachineTests.cs`,
  `SoftwareUpdateClientUploadTests.cs`,
  `SoftwareUpdateClientUploadIntegrationTests.cs` (client + bridge
  end-to-end against `DiInProcessSessionBridge`),
  `MemorySoftwareFolderTests.cs`, `PackageStoreTests.cs`,
  `SoftwareUpdateClientTests.cs`.

## Spec references

- OPC 10000-100 §10.3 — Software Update (companion specification).
- OPC 10000-5 §11.4 — `TemporaryFileTransferType` and the
  `GenerateFileForRead` / `GenerateFileForWrite` / `CloseAndCommit`
  method triplet.
