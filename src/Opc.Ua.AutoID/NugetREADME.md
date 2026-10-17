# OPC UA for AutoID Devices model

`OPCFoundation.NetStandard.Opc.Ua.AutoID` contains the OPC 30010 AutoID Devices
information model, independent of a server or client runtime. Its wire
namespace is `http://opcfoundation.org/UA/AutoID/`.

The NodeSet is model **1.01** (2020-06-18). It requires the base namespace 1.03
and Device Integration 1.01 as lower bounds; the package loads the DI model the
stack ships. Loading the model is not a claim of runtime or profile
conformance.

## Included APIs

- All 305 NodeSet declarations: 17 ObjectTypes, among them the abstract
  `AutoIdDeviceType` with the RFID, OCR, optical, optical verifier and RTLS
  reader device types and their scan, access, diagnostics, log entry and
  presence event types; the `LocationVariableType`; and 28 DataTypes.
- The generated DataTypes, including the `ScanData` and `Location` unions, the
  structures with optional fields (`AccessResult`, `RfidAccessResult`,
  `ScanResult` and its subtypes, `ScanSettings`), `RfidSighting`,
  `ScanDataEpc`, the coordinate structures and the six enumerations.
- Generated node states, typed node-state and encodeable factory registration,
  and asynchronous ObjectType client proxies.
- `AutoIdModel` publication identity and `GetNamespaceUris()` returning an
  `ArrayOf<string>` in dependency order.
- `AddAutoIdTypeSystem` overloads for `NodeStateCollection`,
  `IEncodeableFactoryBuilder` and `INodeStateFactoryBuilder`, which load DI
  before AutoID.
- The generated `AddOpcUaAutoID` overloads for callers that already load DI.

The package references neither a server nor a client runtime project.

## Dependency-aware registration

```csharp
using Opc.Ua;
using Opc.Ua.AutoID;

ServiceMessageContext message = ServiceMessageContext.Create(telemetry);
message.Factory.Builder.AddAutoIdTypeSystem().Commit();

var context = new SystemContext(telemetry)
{
    NamespaceUris = message.NamespaceUris,
    EncodeableFactory = message.Factory
};

var predefinedNodes = new NodeStateCollection();
predefinedNodes.AddAutoIdTypeSystem(context);

var nodeFactory = new NodeStateFactory();
nodeFactory.AddAutoIdTypeSystem();
```

Repeated node loading retains already-present roots by NodeId. Encodeable
registration does not commit the caller's builder.

## Scan data

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

`ScanData` carries the identifier as a `ByteString`, a `String`, an EPC or a
`Custom` `Variant`; `SwitchField` selects the arm. Optional structure fields
are written only when their bit is set in `EncodingMask`.

## Publication provenance and limitations

`Model\Opc.Ua.AutoID.NodeSet2.xml` and `Model\Opc.Ua.AutoID.NodeIds.csv` are
the unmodified files from OPCFoundation/UA-Nodeset; `Model\provenance.json` pins
revision `9f6c7660b94af24d32c4248d4b13bf60adab0301`, source URLs and SHA-256
hashes. The published identifier table predates model 1.01 and lacks 67 of the
NodeSet declarations; `Opc.Ua.AutoID.Derived.NodeIds.csv` is a **local**
symbolic-name drift guard derived from it with
`tools\nodesets\derive-identifier-table.py`. NodeIds always come from the
NodeSet.

Server hosting of reader devices (`Opc.Ua.AutoID.Server`), the typed client
(`Opc.Ua.AutoID.Client`) and the MCP tools (`Opc.Ua.Mcp.AutoID`) are separate
packages; this model package carries no runtime and makes no conformance claim.
