# AutoID Devices

The `Opc.Ua.AutoID` library contains the information model of
[OPC 30010 OPC UA for AutoID Devices](https://reference.opcfoundation.org/AutoID/v101/docs/)
(release 1.01, 2020-06-18): RFID readers, OCR readers, optical (barcode) readers
and verifiers, and real-time locating systems (RTLS), with their scan results,
tag access methods and diagnostics events.

This is the model package only. It loads the model into a server address space
and gives clients the generated DataTypes, identifiers and ObjectType proxies.
It does not implement reader devices, the scan or tag access methods, or a
typed client. Those layers are proposed in the tracking issue for the AutoID
companion SDK.

## Contents

- [Model and dependencies](#model-and-dependencies)
- [What the package contains](#what-the-package-contains)
- [Loading the model](#loading-the-model)
- [Scan data and scan results](#scan-data-and-scan-results)
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

## What the package contains

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
