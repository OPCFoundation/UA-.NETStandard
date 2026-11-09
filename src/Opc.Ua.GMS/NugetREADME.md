# OPC UA Geometric Measurement Systems model

`OPCFoundation.NetStandard.Opc.Ua.GMS` contains the OPC 40210 GMS information
model, independent of a server or client runtime. Its wire namespace is
`http://opcfoundation.org/UA/GMS/`.

The released NodeSet is model **1.0.0**; the reference specification is edition
**1.0.1**. Loading the model is not a claim of runtime or profile conformance.

## Included APIs

- All 273 canonical declarations, with 19 ObjectTypes, three VariableTypes,
  four enumerations and the abstract/concrete workspace structures.
- Generated node states, typed node-state and encodeable factory registration,
  and asynchronous ObjectType client proxies.
- `GmsModel` publication identity and `GetNamespaceUris()` returning an
  `ArrayOf<string>` in dependency order.
- `AddGmsTypeSystem` overloads for `NodeStateCollection`,
  `IEncodeableFactoryBuilder` and `INodeStateFactoryBuilder`.
- The generated `AddOpcUaGMS` overloads for callers that already manage
  dependency loading.

`GMSState` derives from the existing `MachineToolState`;
`GMSResultManagementState` and `GMSResultState` derive from the existing
Machinery Result states. Dependency declarations are not regenerated in this
assembly. The package references neither a server nor a client runtime project.

## Dependency-aware registration

```csharp
using Opc.Ua;
using Opc.Ua.GMS;

ServiceMessageContext message = ServiceMessageContext.Create(telemetry);
message.Factory.Builder.AddGmsTypeSystem().Commit();

var context = new SystemContext(telemetry)
{
    NamespaceUris = message.NamespaceUris,
    EncodeableFactory = message.Factory
};

var predefinedNodes = new NodeStateCollection();
predefinedNodes.AddGmsTypeSystem(context);

var nodeFactory = new NodeStateFactory();
nodeFactory.AddGmsTypeSystem();

GMSState unregisteredGraph = context.CreateInstanceOfGMSType();
```

Registration loads DI, IA, Machinery, ISA95 JobControl V2, Machinery Jobs,
MachineTool and Machinery Result before GMS. The selected MachineTool model is
1.02.0 and retains its older Production declarations, including the required
ActiveProgram, while also providing the newer job-management dependency.
Repeated node loading retains already-present roots by NodeId. Encodeable
registration does not commit the caller's builder.

The MachineTool assembly also contains WMTP. GMS registration explicitly selects
**MachineTool only**: it neither loads WMTP/PADIM/ProcessValues/IRDI namespaces
nor registers their unrelated factories.

Generated instance factories return an unregistered graph with mandatory
members; optional features remain absent until explicitly added. Supply the
application's instance identity factory and complete the owning node manager's
creation/registration lifecycle before publishing an instance. Loading types
does not configure providers, execute measurements or enable result transfer.

## Generated proxy access

```csharp
var gms = new GMSTypeClient(session, machineNodeId, telemetry);
var production = await gms.GetProductionAsync(telemetry, cancellationToken);
GMSResultManagementTypeClient? results =
    await gms.GetResultManagementAsync(telemetry, cancellationToken);
```

The proxies preserve the DI-qualified Identification, MachineTool-qualified
Equipment/Monitoring/Notification/Production and GMS-qualified ResultManagement
browse paths. Result methods retain their inherited Machinery Result declaration
identities and typed outputs. Callers must distinguish service status from the
result-domain error code and release any result handles they acquire.

## Publication provenance and limitations

The three official files retain their original lowercase names, including
`opc.ua.gms.nodesids.csv`, and their content is changed only to repository CRLF
line endings. `Model\provenance.json` pins revision
`96a08945425d0295edb3607d2a38f6841ef44046`, source URLs and normalized SHA-256
hashes. `Opc.Ua.GMS.Derived.NodeIds.csv` is a **local** symbolic-name drift guard
produced by `tools\nodesets\derive-identifier-table.py`, not an upstream file.

Known publication differences remain explicit:

- The official CSV additionally lists abstract Workspace encoding/dictionary
  IDs 5002, 5003, 5004, 6061 and 6062, which are absent from the XML. The derivation
  tool reports those five surplus rows with exit code 1 while emitting all
  **273** canonical XML identities. No missing XML nodes or encodings are
  invented.
- Canonical XML identities are `SensorWarningAlarmType` i=1010,
  `GMSResultType` i=2004 and `GMSResultManagementType` i=1008 despite inconsistent
  prose.
- The XML and section 8.3 Table 17 give `Qualified/IsCountingUp=true`.
  The section 12.2 QualifiedToolLife CU instead requires remaining-time
  semantics with `Indication=Time` and `IsCountingUp=false`. A runtime selecting
  that CU must instantiate/configure the optional Qualified entry accordingly;
  this model package preserves the publication and does not claim that CU.
- Section 12.3.2.7 names `GMS_ProductionStatisticsType`, which is absent from
  both the CU definitions and the normative XML. Inherited production statistics
  and defined GMS jobs/parts remain available. No new type or NodeId is invented,
  and a complete GMS Production facet is not advertised.

Server hosting, equipment providers, measurement/production publishing,
result-store binding, higher-level client workflows and MCP tools are separate
runtime work, not functionality supplied by this model package.
