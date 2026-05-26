# DI Software Update

Phase 8E/8F deliver the package-storage layer plus a minimal client
helper for the OPC 10000-100 §10.3 software-update facet. The full
state-machine wiring (PrepareForUpdate / Installation / PowerCycle /
Confirmation) remains application-specific — the source generator
emits typed `*StateMachineState` proxies that applications drive
directly when needed.

## Server-side: package store

The store is an application-facing abstraction over the binary
artifacts that the DI software-update facet exposes. Two
implementations ship in `Opc.Ua.Di.Server.SoftwareUpdate`:

| Type | Backing | Use case |
|------|---------|----------|
| `MemoryPackageStore` | `ConcurrentDictionary<string, byte[]>` | Unit tests; small fixtures. |
| `FileSystemPackageStore` | `Opc.Ua.Server.FileSystem.IFileSystemProvider` | Production — reuses the same provider model used by the server's `FileSystem` mount. |

### Surface

```csharp
public interface ISoftwarePackageStore
{
    IAsyncEnumerable<SoftwarePackage> ListAsync(CancellationToken ct = default);
    ValueTask<SoftwarePackage?> GetAsync(string packageId, CancellationToken ct = default);
    ValueTask<bool> ExistsAsync(string packageId, CancellationToken ct = default);
    ValueTask<Stream> OpenReadAsync(string packageId, CancellationToken ct = default);
    ValueTask<SoftwarePackage> AddAsync(SoftwarePackage metadata, Stream payload, CancellationToken ct = default);
    ValueTask<bool> DeleteAsync(string packageId, CancellationToken ct = default);
}
```

`SoftwarePackage` is a record carrying `Id`, `Version`, `Vendor`,
`Description`, `SizeBytes`, `CreatedAt`, `Hash`. Both stores recompute
`SizeBytes` and `CreatedAt` during `AddAsync` so callers can pass
zeros / `default` in the input metadata.

### `FileSystemPackageStore` layout

Each package is stored as a directory containing two files:

```
{root}/
    {package-id}/
        payload.bin       ← the binary firmware/installer
        metadata.json     ← the SoftwarePackage record as JSON
```

JSON serialization uses a source-generated
`System.Text.Json` context (`SoftwarePackageJsonContext`) so the
store is AOT-friendly.

Package IDs must NOT contain `/` or `\` — the store validates and
throws `ArgumentException` to prevent path traversal.

### Composing with `IFileSystemProvider`

```csharp
IFileSystemProvider fs = new PhysicalFileSystemProvider(
    rootDirectory: "/var/lib/myserver/packages",
    mountName: "Packages",
    isWritable: true);

ISoftwarePackageStore store = new FileSystemPackageStore(
    provider: fs,
    rootPath: "/SoftwarePackages");
```

The same `IFileSystemProvider` instance can also be mounted into the
server's address space via `FileSystemNodeManager` — both paths share
the on-disk layout.

## Hosting

Register the store as a singleton and seed it from a
`ConfigureDevicesFor` configurator:

```csharp
builder.Services.AddSingleton<ISoftwarePackageStore, MemoryPackageStore>();
builder.Services
    .AddOpcUa()
    .AddServer(o => { ... })
    .AddNodeManager<MyNodeManagerFactory>()
    .ConfigureDevicesForAsync<MyNodeManager>(async ctx =>
    {
        ISoftwarePackageStore store = ctx.GetRequiredService<ISoftwarePackageStore>();
        await store.AddAsync(
            new SoftwarePackage(
                Id: "firmware-1.0.0",
                Version: "1.0.0",
                Vendor: "Acme",
                Description: "Initial firmware",
                SizeBytes: 0,
                CreatedAt: default,
                Hash: string.Empty),
            new FileStream("/path/to/firmware.bin", FileMode.Open));
    });
```

The pump sample (`Applications/MinimalPumpServer`) demonstrates the
end-to-end pattern with `PumpSoftwareUpdateSeeder`.

## Client-side

`SoftwareUpdateClient` exposes a minimal read-only surface for v1:

```csharp
public sealed class SoftwareUpdateClient
{
    public SoftwareUpdateClient(ISession session, NodeId softwareUpdateNodeId, ITelemetryContext telemetry);
    public ValueTask<string> ReadSoftwareVersionAsync(CancellationToken ct = default);
}
```

Method-level invocation (Loading, Installation, ...) is performed
through the typed `*MethodStateClient` proxies emitted by the source
generator for the DI model. The client integration registers a
factory:

```csharp
Func<NodeId, CancellationToken, ValueTask<SoftwareUpdateClient>>
```

…via `services.AddOpcUa().AddClient(...).AddOpcUaDi()`.

## See also

- [Hosting](Hosting.md) — runtime registration of the store.
- [DeviceBuilder](DeviceBuilder.md) — attaching software-update
  children to a device.
- [FileSystemClient](../FileSystemClient.md) — file-transfer client
  used to read/write the package binary.

## Roadmap

- **State-machine builder** (`Docs/plans/StateMachineBuilder.md`) —
  fluent configurator for the four DI software-update state
  machines. Planned as an additive opt-in surface once the package
  store contract is stable.
- **Validation hooks** — content-hash verification on `AddAsync`,
  signature checks before `OpenReadAsync` returns the payload.
