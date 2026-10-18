# AutoID Devices

The AutoID packages implement
[OPC 30010 OPC UA for AutoID Devices](https://reference.opcfoundation.org/AutoID/v101/docs/)
(release 1.01, 2020-06-18): RFID readers, OCR readers, optical (barcode) readers
and verifiers, and real-time locating systems (RTLS), with their scan results,
tag access methods and diagnostics events.

| Package | What it adds |
| --- | --- |
| `Opc.Ua.AutoID` | The information model: node states, DataTypes, identifiers, encodeables and client proxies |
| `Opc.Ua.AutoID.Server` | Reader hosting on the Device Integration node manager, a provider contract for the reader hardware, the OPC 30010 methods and events, simulated readers |
| `Opc.Ua.AutoID.Client` | Reader discovery, typed scan and tag calls, device snapshots and an asynchronous stream of events |
| `Opc.Ua.Mcp.AutoID` | Nine bounded MCP tools; no tool changes a tag |

The [AutoID sample](../samples/AutoID/) runs a simulated RFID reader end to end.

## Contents

- [Model and dependencies](#model-and-dependencies)
- [What the model package contains](#what-the-model-package-contains)
- [Loading the model](#loading-the-model)
- [Scan data and scan results](#scan-data-and-scan-results)
- [Hosting readers](#hosting-readers)
- [Providers](#providers)
- [Behavior of the methods](#behavior-of-the-methods)
- [Events](#events)
- [Conformance](#conformance)
- [Client](#client)
- [MCP tools](#mcp-tools)
- [Sample](#sample)
- [Specification notes](#specification-notes)
- [Provenance](#provenance)

## Model and dependencies

| Item | Value |
| --- | --- |
| Package | `OPCFoundation.NetStandard.Opc.Ua.AutoID` |
| Model URI | `http://opcfoundation.org/UA/AutoID/` |
| Model version | 1.01, published 2020-06-18 |
| Required models | OPC UA 1.03, [Device Integration](DeviceIntegration.md) 1.01 (lower bounds) |
| Generated CLR namespace | `Opc.Ua.AutoID` |
| Source | [UA-Nodeset `latest/AutoID`](https://github.com/OPCFoundation/UA-Nodeset/tree/latest/AutoID) |

`AutoIdDeviceType` derives from the DI `DeviceType`, so the package references
`Opc.Ua.Di` and loads the DI model the stack ships, which is newer than the
required 1.01.

## What the model package contains

- The device types `AutoIdDeviceType` (abstract), `RfidReaderDeviceType`,
  `OcrReaderDeviceType`, `OpticalReaderDeviceType`,
  `OpticalVerifierDeviceType` and `RtlsDeviceType`, with their methods
  (`Scan`, `ScanStart`, `ScanStop`, `ReadTag`, `WriteTag`, `WriteTagID`,
  `KillTag`, `LockTag`, `SetTagPassword`, `GetLocation`, ...) as typed method
  states.
- The event types `AutoIdScanEventType` with one subtype per reader kind,
  `AutoIdDiagnosticsEventType`, `AutoIdAccessEventType`,
  `RfidAccessEventType`, `AutoIdLogEntryEventType` and
  `AutoIdPresenceEventType`.
- The DataTypes: the `ScanData` and `Location` unions, the abstract
  `ScanResult` with `RfidScanResult`, `OcrScanResult`, `OpticalScanResult`,
  `OpticalVerifierScanResult` and `RtlsLocationResult`, the access results
  `AccessResult` and `RfidAccessResult`, `ScanSettings`, `RfidSighting`,
  `ScanDataEpc`, the coordinate structures and six enumerations.
- `AutoIdModel` (model identity, `GetNamespaceUris()`) and the
  `AddAutoIdTypeSystem` registration overloads.

The scan event subtypes and `RfidAccessEventType` narrow the DataType of their
`ScanResult` or `AccessResult` property to a structure subtype. The generated
states expose the property with that type, for example
`RfidScanEventState.ScanResult` is a
`PropertyState<ArrayOf<RfidScanResult>>`.

## Loading the model

```csharp
using Opc.Ua;
using Opc.Ua.AutoID;

// Encodeables (DI and AutoID); the caller commits the builder.
messageContext.Factory.Builder.AddAutoIdTypeSystem().Commit();

// Type nodes for a node manager's predefined nodes.
var predefinedNodes = new NodeStateCollection();
predefinedNodes.AddAutoIdTypeSystem(systemContext);

// Typed node states for instances of the AutoID types.
nodeStateFactory.AddAutoIdTypeSystem();
```

`AddAutoIdTypeSystem` loads DI before AutoID, registers missing namespaces and
skips roots that are already present, so it can be combined with other models
that also load DI.

## Scan data and scan results

`ScanData` is a union: `SwitchField` selects the `ByteString`, `String`,
`Epc` or `Custom` arm. The scan and access results have optional fields;
set the corresponding bits in `EncodingMask` to send them.

```csharp
var result = new RfidScanResult
{
    CodeType = "EPC",
    ScanData = new ScanData
    {
        SwitchField = ScanDataFields.Epc,
        Epc = new ScanDataEpc { PC = 0x3000, UId = ByteString.From(epc) }
    },
    Timestamp = DateTimeUtc.Now,
    Sighting = [new RfidSighting { Antenna = 1, Strength = -52 }]
};
```

The NodeSet defines binary and XML encodings; JSON uses the DataType NodeIds.

## Hosting readers

`Opc.Ua.AutoID.Server` registers a Device Integration node manager that loads
the DI and AutoID models and creates one device per reader below `DeviceSet`.
It owns the Device Integration namespace, so it cannot be combined with another
DI-owning registration such as `AddOpcUaDi` or `AddMachinery`.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.AutoID.Server;

services.AddOpcUa()
    .AddServer(options => { /* application name, endpoints, PKI */ })
    .AddAutoId(options => options.MaximumScanDuration = TimeSpan.FromSeconds(10))
    .ConfigureAutoId(async context =>
    {
        AutoIdReader reader = await context.Runtime
            .Reader(new QualifiedName("DockDoor1", context.Manager.InstanceNamespaceIndex), hardware)
            .WithDeviceInfo("Dock door antenna portal")
            .WithDeviceLocationName("Dock 1, gate A")
            .WithAntennaNames([new AntennaNameIdPair { AntennaId = 1, AntennaName = "Left post" }])
            .WithIdentification(nameplate => nameplate.SerialNumber = "R100-0001")
            .BuildAsync(context.CancellationToken);
    });
```

`Reader(...)` returns a builder; nothing is registered before `BuildAsync`.
The builder materialises exactly the components the provider supports (a reader
without a `Scan` provider has no `Scan` method), registers the device, makes it
an event notifier and attaches the provider. A failure at any step is rolled
back, so the browse name can be used again, and concurrent builds of one name
admit exactly one. Disposing the runtime (the node manager does so on shutdown)
stops every reader's scans, detaches its provider and, for a provider the reader
owns (`OwnsProvider()`), disposes it.

`AutoIdServerOptions` bounds what a client can ask for:

| Option | Default | Effect |
| --- | --- | --- |
| `MaximumScanDuration` | 30 s | A synchronous `Scan` asking for more is `Bad_InvalidArgument`; a provider that does not return in time is cancelled and the call fails with `Bad_Timeout` |
| `MaximumTagReadLength` | 65536 | A longer `ReadTag` is `Bad_InvalidArgument` |
| `MaximumScanResults` | 1000 | More results fail the scan with `Bad_ResponseTooLarge` instead of being truncated |
| `PublishAccessEvents` | `true` | Whether commands that reach a tag raise access events |
| `AdditionalNamespaceUris` | none | Extra namespaces to register |

The readers keep their scan state (`Idle_0`, `Scanning_2`, `Busy_3`, `Error_1`)
in `DeviceStatus`. `AutoIdReader.SetFaultedAsync(true)` reports `Error_1`: new
scans then return `DEVICE_NOT_READY_17` and an active asynchronous scan stops.
`SetDeviceLocation` moves the `DeviceLocation` of a mobile reader.

## Providers

A reader is the OPC UA face of a hardware provider. The base contract
`IAutoIdReaderProvider` (kind, attach, detach) is enough for a device that scans
on its own (OPC 30010 6.1.1 option 3): the provider reports through the
`IAutoIdReaderSink` it receives when attached. Capabilities are added by
implementing more interfaces:

| Interface | Methods it enables |
| --- | --- |
| `IAutoIdScanProvider` | `Scan` |
| `IAutoIdAsyncScanProvider` | `ScanStart`, `ScanStop` |
| `IAutoIdRfidProvider` (`SupportedOperations`) | `ReadTag`, `WriteTag`, `WriteTagID`, `KillTag`, `LockTag`, `SetTagPassword`, each only when listed |
| `IAutoIdRtlsProvider` | `GetLocation`, `GetSupportedLocationTypes` |

The sink pushes `ReportScanAsync` (scan event and `LastScanData`),
`CompleteScanAsync` (a termination condition ended the asynchronous scan),
`ReportPresenceAsync`, `ReportLogEntryAsync` and `ReportAccessAsync`. Provider
calls are made without any runtime lock held, scans never overlap on one reader,
and the sink throws `ObjectDisposedException` after the reader was disposed.

Four simulated providers ship in the package and back the tests and the sample:
`InMemoryRfidReaderProvider` (an EPC Gen2 reader with memory banks, passwords,
locks and kill), `InMemoryScanReaderProvider` (OCR and optical),
`InMemoryOpticalVerifierProvider` and `InMemoryRtlsProvider`. Their scan cycle
defaults to one second (`CycleInterval`), so an unbounded `ScanStart` raises at
most one scan event per second; pass a `cycleInterval` to the constructor for
another pace. `InMemoryRtlsProvider` stores each object's location in the type it
was registered with. A `locationConverter` translates it into the other supported
types, for example from the local coordinate system the system was configured
with (OPC 30010 9.3.4) into WGS84:

```csharp
// A site whose local origin is at 48 N 11 E, X pointing east and Y north, in metres.
var rtls = new InMemoryRtlsProvider(locationConverter: (location, type) =>
    type == LocationTypeEnumeration.WGS84 && location.SwitchField == LocationFields.Local
        ? new Location
        {
            SwitchField = LocationFields.WGS84,
            WGS84 = new WGS84Coordinate
            {
                N_S_Hemisphere = "N",
                Latitude = 48.0 + (location.Local.Y / 111_320.0),
                E_W_Hemisphere = "E",
                Longitude = 11.0 + (location.Local.X / 74_600.0)
            }
        }
        : null);
```

Without a converter, or when it returns `null`, `GetLocation` for another type
answers `Bad_InvalidState`.

## Behavior of the methods

| Method | Result codes and semantics |
| --- | --- |
| `Scan` (6.1.3.4) | `Bad_InvalidArgument` for an infinite scan (duration 0, cycles 0, no `DataAvailable`), a negative or non-finite value, an undefined location type or a duration above the limit; `Bad_InvalidState` while another scan is active. The first result becomes `LastScanData`/`LastScanTimestamp` with one source timestamp |
| `ScanStart` (6.1.3.5) | Infinite settings allowed; `Bad_InvalidState` while a scan is active. The scan stops through `ScanStop`, when the provider completes it, when the starting session closes, or at shutdown |
| `ScanStop` (6.1.3.6) | `Bad_InvalidState` when no scan is active; any session may stop a scan |
| `ReadTag`, `WriteTag`, `WriteTagID`, `KillTag`, `LockTag`, `SetTagPassword` | A malformed identifier, empty code type, over-long read or undefined enumeration is `Bad_InvalidArgument`. Everything the tag decides (`PASSWORD_ERROR_4`, `OUT_OF_RANGE_ERROR_7`, ...) is returned in `Status` with a good call result |
| `GetDeviceLocation` (6.1.3.7) | Available when a location is configured; a location of another type is `Bad_NotSupported` |
| `GetLocation`, `GetSupportedLocationTypes` (6.6.3) | The result carries the location in the requested type. An unsupported location type is `Bad_InvalidArgument`, an unknown object `Bad_NotFound`, and a supported type the object's location is not available in `Bad_InvalidState` (6.6.3.2: not available, for example due to the configuration) |

A method the provider does not support is not instantiated; calling it fails with
the stack's status for an unpublished method.

## Events

Scan results are raised as the scan event of the reader kind
(`RfidScanEventType`, `OcrScanEventType`, `OpticalScanEventType`,
`OpticalVerifierScanEventType`, `RtlsLocationEventType`) with `DeviceName` and
the typed `ScanResult` array. `AutoIdPresenceEventType` and
`AutoIdLogEntryEventType` come from the sink. Commands that reached an
identifier (a scan with results, and each tag command except those answering
`NO_IDENTIFIER_8`, `MULTIPLE_IDENTIFIERS_9`, `NOT_SUPPORTED_BY_DEVICE_15`,
`DEVICE_NOT_READY_17`, `INVALID_CONFIGURATION_18` or `DEVICE_FAULT_20`) raise
`RfidAccessEventType` on RFID readers and `AutoIdAccessEventType` otherwise, with
`Client`, `Command` and the access results. `Client` names the client
application that originated the command (OPC 30010 6.1.3.3 and 7.10): the
application name of the client description the session was created with, else
its application URI, else `OPC UA client`. The results of an asynchronous scan
name the client that called `ScanStart`; scans the device reports on its own name
`AutoID device`.

## Conformance

The runtime contributes the OPC 30010 conformance units and server facets
(Tables 82 to 85) of the readers it hosts; a facet is advertised only when a
reader implements all its mandatory units.

| Unit | Implemented when |
| --- | --- |
| `AutoID DeviceType` | A reader exists |
| `AutoID Simple Sync Access` | A reader exists (`LastScanData` is updated by every scan) |
| `AutoID Sync Access` | A reader has a `Scan` method |
| `AutoID Async Access` | A reader exists (scan events) |
| `AutoID Async Access Control` | A reader has `ScanStart` and `ScanStop` |

The *Simple Sync*, *Base Sync* and *Base Async* facets are advertised
accordingly. `AutoID Simple Sync Access Control` (the `ScanActive` variable and
the `ScanSettings` function group) and `AutoID Device Parameters` (the
`RuntimeParameters` group) are not implemented, so the *Full AutoID Server Facet*
is never advertised. `DeviceInfo`, `DeviceLocation`, `DeviceLocationName` and
`AntennaNames` are published when configured.

## Client

`Opc.Ua.AutoID.Client` wraps the generated ObjectType proxies:

```csharp
using Opc.Ua.AutoID.Client;

AutoIdClient autoId = session.AutoId(telemetry);   // or AddAutoIdClient() in DI
await foreach (AutoIdReaderEntry reader in autoId.EnumerateReadersAsync())
{
    AutoIdDeviceSnapshot device = await autoId.ReadDeviceAsync(reader);
    AutoIdScanResponse scan = await autoId.ScanAsync(
        reader, new ScanSettings { Duration = 1000, DataAvailable = true });
}

await foreach (AutoIdScanEventTypeRecord e in
    autoId.ObserveScanEventsAsync(session.DefaultStreaming, reader, ct))
{
    // e.ScanResult holds RfidScanResult, OcrScanResult, ... instances
}
```

Discovery finds the devices whose type derives from one of the five concrete
reader types. A browse the server rejects throws `ServiceResultException` rather
than yielding an empty list, a rejected method call throws with the server's
status, and every asynchronous member honours cancellation. The status a device
reports for a tag command is returned as data. `AutoIdReadOptions` narrows
`ReadDeviceAsync`: `Default` reads everything the device publishes, `StatusOnly`
reads two variables. `Observe*Async` methods (scan, access, presence and log
entry events) take a streaming subscription the caller owns; the monitored item
lives as long as the enumeration.

## MCP tools

`AddOpcUaMcpAutoId()` and `WithOpcUaAutoIdTools(McpToolProfile.AutoId)` (profile
`autoid`) add `autoid_list_readers`, `autoid_read_device`, `autoid_scan`,
`autoid_scan_start`, `autoid_scan_stop`, `autoid_observe_scan_events`,
`autoid_read_tag`, `autoid_get_supported_location_types` and
`autoid_get_location`. No tool writes, locks or kills a tag or carries a
password. See the [MCP server guide](McpServer.md#autoid-tools).

## Sample

[`samples/AutoID`](../samples/AutoID/) contains `AutoIdServer`, a dock door with
a simulated RFID portal whose pallets enter and leave the antenna field, and
`AutoIdClient`, which discovers the reader, scans, reads and writes tag memory
and prints the scan and presence events. `Test-AutoIdSamplePair.ps1` runs both
and checks the output.

## Specification notes

- OPC 30010 6.1.3.16 prescribes the `AutoIdModelVersion` string `"1.00"` although
  the released model is 1.01; the server publishes `"1.00"`.
- The NodeSet orders the `GetLocation` arguments identifier, location type, code
  type; the specification text orders them identifier, code type, location type.
  The generated proxy and the server follow the NodeSet.
- The NodeSet declares the `Scan` method of `OpticalVerifierDeviceType` with
  `WGS84Coordinate` results where OPC 30010 6.4.3.1 names
  `OpticalVerifierScanResult`. The method cannot be served consistently, so a
  verifier does not publish `Scan`; it reports through `ScanStart` and scan
  events, and `AutoIdClient.ScanAsync` refuses a verifier.
- The `GetUnits` method of `RtlsDeviceType` declares no output argument; it is not
  published.

## Provenance

`src/Opc.Ua.AutoID/Model` holds the unmodified NodeSet and identifier table
from UA-Nodeset revision `9f6c7660b94af24d32c4248d4b13bf60adab0301`;
`provenance.json` records the source URLs and SHA-256 hashes. The published
identifier table predates model 1.01 and lacks 67 NodeSet declarations, so
`Opc.Ua.AutoID.Derived.NodeIds.csv` is generated from it with
`tools/nodesets/derive-identifier-table.py` and used as the source generator's
drift guard. NodeIds always come from the NodeSet.

The [AutoID tests](../tests/Opc.Ua.AutoID.Tests/) pin the file hashes, compare
the generated identifiers with the NodeSet and the identifier table, load the
model with DI, and round-trip the DataTypes through the binary, JSON and XML
encodings.
