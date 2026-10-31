# OPC UA LADS model

`OPCFoundation.NetStandard.Opc.Ua.LADS` contains the OPC 30500-1 Laboratory and
Analytical Device Standard (LADS) information model, independent of a server or
client runtime. Its namespace is `http://opcfoundation.org/UA/LADS/`.

The NodeSet is model 1.0.0 (2023-11-30). It requires OPC UA 1.05.02, Device
Integration 1.04.0, Asset Management Basics 1.01.0 and Machinery 1.03.0 as lower
bounds. The package loads the versions of those models the stack ships. Loading
the model is not a claim of runtime or profile conformance.

## Included APIs

- All NodeSet declarations: 53 ObjectTypes, among them `LADSDeviceType`,
  `FunctionalUnitType`, the control, sensor, timer and cover function types,
  `ProgramManagerType`, `ResultType`, `MaintenanceTaskType` and the state
  machine types.
- The DataTypes `KeyValueType` and `SampleInfoType` and the enumeration
  `MaintenanceTaskResultEnum`.
- Generated node states, node-state and encodeable factory registration, and
  asynchronous ObjectType client proxies.
- `LadsModel` with the model identity and `GetNamespaceUris()`.
- `AddLadsTypeSystem` overloads for `NodeStateCollection`,
  `IEncodeableFactoryBuilder` and `INodeStateFactoryBuilder`.

## Loading the model

```csharp
using Opc.Ua;
using Opc.Ua.LADS;

messageContext.Factory.Builder.AddLadsTypeSystem().Commit();

var predefinedNodes = new NodeStateCollection();
predefinedNodes.AddLadsTypeSystem(systemContext);

nodeStateFactory.AddLadsTypeSystem();
```

`AddLadsTypeSystem` loads DI, IA, Machinery, AMB and LADS in that order. It
registers missing namespaces and skips roots that are already present, so it can
be combined with other models that load the same dependencies. Encodeable
registration does not commit the caller's builder.

## Provenance

`Model\Opc.Ua.LADS.NodeSet2.xml` and `Model\Opc.Ua.LADS.NodeIds.csv` are the
unmodified files from OPCFoundation/UA-Nodeset. `Model\provenance.json` pins
the source revision, URLs and SHA-256 hashes. NodeIds always come from the
NodeSet.

Device hosting (`Opc.Ua.LADS.Server`) and the typed client
(`Opc.Ua.LADS.Client`) are separate packages.

See the [LADS guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/LADS.md).