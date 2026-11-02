# OPC UA AutoID server hosting

`OPCFoundation.NetStandard.Opc.Ua.AutoID.Server` hosts OPC 30010 AutoID readers
on the Device Integration node manager. The package implements the OPC UA
methods, events, device status and conformance claims. A provider connects each
reader to RFID, OCR, optical, optical verifier or RTLS hardware.

```csharp
services.AddOpcUa()
    .AddServer(options => { /* application name, endpoints, PKI */ })
    .AddAutoId()
    .ConfigureAutoId(async context =>
    {
        AutoIdReader reader = await context.Runtime
            .Reader(new QualifiedName("DockDoor1", context.Manager.InstanceNamespaceIndex), hardware)
            .WithDeviceInfo("Dock door antenna portal")
            .BuildAsync(context.CancellationToken);
    });
```

`AddAutoId` owns the Device Integration namespace and cannot be combined with
`AddOpcUaDi`, `AddMachinery` or another DI owner. To host readers next to
machines and other Machinery-based companions, compose AutoID into the
Machinery node manager instead; the readers then share its `DeviceSet`:

```csharp
services.AddOpcUa()
    .AddServer(options => { /* application name, endpoints, PKI */ })
    .AddMachinery(options => options.Parts = MachineryParts.BuildingBlocks)
    .AddAutoIdModel()
    .ConfigureAutoIdFor<MachineryNodeManager>(async (context, ct) =>
    {
        await context.Runtime
            .Reader(new QualifiedName("DockDoor1", context.Runtime.InstanceNamespaceIndex), hardware)
            .BuildAsync(ct);
    });
```

`AddOpcUaDi().AddAutoIdModel()` hosts readers on the plain DI owner without
the Machinery server package; use `ConfigureAutoIdFor<DiNodeManager>` there.
Register Machinery configuration before AutoID callbacks so machines exist when
readers attach to them. The generic
`AddDiModel<AutoIdModelProvider, TNodeManager>(configure)` helper combines model
registration with an owner-targeted callback without an integration package.

Every reader stays reachable from the DI `DeviceSet` (OPC 10000-100 4.9).
`WithParent` places it below another node; if DeviceSet cannot reach it,
the owner organizes the outermost DI component in its parent chain.
In the Machinery composition,
`AsComponentOf(machine)` makes a reader an identifiable component of a machine
(OPC 40001-1 11): it is referenced from the machine's `Components` add-in and
gets a `MachineryComponentIdentificationType` add-in that references its
nameplate. Readers are never added to the `Machines` folder.

## Providers

Implement `IAutoIdReaderProvider` and add interfaces for what the hardware can do;
the reader publishes exactly those methods.

| Interface | Methods |
| --- | --- |
| `IAutoIdScanProvider` | `Scan` |
| `IAutoIdAsyncScanProvider` | `ScanStart`, `ScanStop` |
| `IAutoIdRfidProvider` | `ReadTag`, `WriteTag`, `WriteTagID`, `KillTag`, `LockTag`, `SetTagPassword` (per `SupportedOperations`) |
| `IAutoIdRtlsProvider` | `GetLocation`, `GetSupportedLocationTypes` |

Providers report scans, completion, presence, logbook entries and device accesses
through the `IAutoIdReaderSink` they receive on attach. The package ships
simulated providers (`InMemoryRfidReaderProvider` with EPC Gen2 memory, passwords,
locks and kill; `InMemoryScanReaderProvider`; `InMemoryOpticalVerifierProvider`;
`InMemoryRtlsProvider`).

## Behavior

- A reader runs one scan at a time; a second `Scan` or `ScanStart` fails with
  `Bad_InvalidState`. An infinite synchronous scan is `Bad_InvalidArgument`.
- The scan started by a session stops when the session closes.
- A failed reader registration is rolled back and the name stays usable;
  disposal stops scans, detaches providers and disposes the providers the
  reader owns.
- Conformance units and facets are advertised only for what the hosted readers
  implement; the Full AutoID facet is not claimed.
- An optical verifier does not publish `Scan`: the NodeSet types its results
  as `WGS84Coordinate`, contradicting OPC 30010 6.4.3.1.

See the [AutoID guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/AutoID.md).
