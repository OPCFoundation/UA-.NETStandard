# DI Software Update Facet

This document explains how to expose the OPC 10000-100 §10.3
software-update facet on a DI device, what address-space surface it
creates, and how clients drive it.

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

// 1) Register a server-wide package store (commonly via DI).
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

## Client side

The minimal v1 `SoftwareUpdateClient` reads the device's
`SoftwareVersion` property:

```csharp
var client = new SoftwareUpdateClient(
    session, softwareUpdateNodeId, telemetry);
string version = await client.ReadSoftwareVersionAsync();
```

State-machine drivers (Prepare / Install / Confirm / Uninstall) are
invoked through the standard `Session.CallAsync` service against the
method NodeIds resolved with
`Session.TranslateBrowsePathsToNodeIdsAsync`. The sample server
(`Applications/MinimalSoftwareUpdateServer`) is the canonical
end-to-end demo of the facet.

## Walkthrough — `Applications/MinimalSoftwareUpdateServer`

```csharp
services.AddSingleton<ISoftwarePackageStore, MemoryPackageStore>();

services
    .AddOpcUa()
    .AddServer(o =>
    {
        o.ApplicationName = "MinimalSoftwareUpdateServer";
        o.EndpointUrls.Add($"opc.tcp://localhost:{port}/MinimalSoftwareUpdateServer");
    })
    .AddOpcUaDi()
    .ConfigureDevicesFor<DiNodeManager>(async ctx =>
    {
        var device = await ctx.CreateDeviceAsync(
            new QualifiedName("UpdateableDevice #1", ctx.Manager.DiNamespaceIndex));

        device.WithIdentification(id =>
        {
            id.Manufacturer = new LocalizedText("Acme Corp");
            id.Model = new LocalizedText("UpdateableDevice X1");
            id.SerialNumber = "SN-SW-1";
            id.SoftwareRevision = "1.0.0";
        });

        var packageStore = ctx.GetRequiredService<ISoftwarePackageStore>();
        await SoftwarePackageSeeder.SeedAsync(packageStore);

        device.WithSoftwareUpdate(packageStore, su => su.UsePackageLoading());
    });

await builder.Build().RunAsync();
```

A client connecting to the running server will discover the SU
subtree under `UpdateableDevice #1.SoftwareUpdate`, can browse the
state machines, and invoke `InstallSoftwarePackage`,
`PrepareForUpdate.Prepare`, `Confirmation.Confirm`, etc. The
library-supplied default handlers record the installed version in the
per-device `MemorySoftwareFolder`, so a subsequent read of the
device's `SoftwareVersion` reflects the new version.

## Implementation pointers

- Fluent surface: `Libraries/Opc.Ua.Di.Server/Builders/ISoftwareUpdateBuilder.cs`
  and `DeviceBuilderSoftwareUpdateExtensions.cs`.
- Address-space wiring: `SoftwareUpdateFacetWiring.cs` (internal).
- Storage abstractions:
  `Libraries/Opc.Ua.Di.Server/SoftwareUpdate/ISoftwarePackageStore.cs` and
  `ISoftwareFolder.cs`.
- Client helper: `Libraries/Opc.Ua.Di.Client/SoftwareUpdateClient.cs`.
- Tests:
  `Tests/Opc.Ua.Di.Tests/DeviceBuilderSoftwareUpdateTests.cs`,
  `MemorySoftwareFolderTests.cs`, `PackageStoreTests.cs`,
  `SoftwareUpdateClientTests.cs`.
- Sample: `Applications/MinimalSoftwareUpdateServer/Program.cs`.

## Spec references

- OPC 10000-100 §10.3 — Software Update (companion specification).
- OPC 10000-5 §11.4 — `TemporaryFileTransferType` and the
  `GenerateFileForRead` / `GenerateFileForWrite` / `CloseAndCommit`
  method triplet.
