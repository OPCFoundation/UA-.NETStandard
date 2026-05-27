# MinimalSoftwareUpdateServer

A self-contained console OPC UA server that demonstrates the
**OPC 10000-100 (Device Integration) software-update facet** wired
end-to-end through the unified `services.AddOpcUa()` Microsoft.Extensions
hosting model.

## What it shows

- `AddOpcUaDi()` brings up a plain DI node manager (no companion-spec
  subclass required).
- `ConfigureDevicesForAsync<DiNodeManager>(...)` runs once the address
  space is initialised:
  - Creates an `UpdateableDevice #1` instance under `DeviceSet`.
  - Populates the standard DI nameplate properties via
    `WithIdentification(...)`.
  - Seeds an `ISoftwarePackageStore` (an `MemoryPackageStore`
    registered as a singleton) with two demonstration firmware
    payloads.

The store is consulted by the DI server's software-update file-transfer
pipeline; clients that browse the device's `SoftwareUpdate.Loading.Pending`
file node read the seeded bytes.

## Running

```bash
dotnet run --project Applications/MinimalSoftwareUpdateServer -c Release
```

The server binds to `opc.tcp://localhost:62543/MinimalSoftwareUpdateServer`
(override with `--port=NNNN`).

## Swap in a file-system store

Replace the singleton registration in `Program.cs`:

```csharp
builder.Services.AddSingleton<ISoftwarePackageStore>(_ =>
{
    var provider = new PhysicalFileSystemProvider(
        rootDirectory: "/var/lib/sw-update",
        mountName: "Packages",
        isWritable: true);
    return new FileSystemPackageStore(provider, rootPath: "/SoftwarePackages");
});
```

## See also

- `Docs/Opc.Ua.Di/SoftwareUpdate.md` — full developer guide for the
  package-store + state-machine surface.
- `Docs/Opc.Ua.Di/Hosting.md` — `AddOpcUaDi()` /
  `ConfigureDevicesFor` registration model.
- `Applications/MinimalPumpServer` — companion-spec server that
  exercises the OPC 40223 Pumps facet end-to-end.
