# OPC UA Cranes and Hoists model

`OPCFoundation.NetStandard.Opc.Ua.CranesHoists` provides the generated
OPC-40020-1 1.00 Cranes and Hoists information model. This package is preview-only.
Its wire namespace is `http://opcfoundation.org/UA/CranesHoists/`.

## Included model API

- Typed states and client proxies for `CraneMotionDeviceSystemType`,
  `CraneMotionDeviceType`, `CraneAxisType`, `CraneMotorType`, `BrakeType` and
  `ProtectiveFunctionType`.
- The four published enum DataTypes and the `ProtectsAxis` ReferenceType.
- Namespace-qualified identifiers, browse names, generated instance factories
  and `AddOpcUaCranesHoists` model/factory registration overloads.
- Real CLR inheritance from the existing Robotics and DI models, with
  Machinery identification nodes; dependency models are not regenerated.

Load the dependencies before the crane model using an application's
`ISystemContext`:

```csharp
using Opc.Ua;
using Opc.Ua.CranesHoists;
using Opc.Ua.Di;
using Opc.Ua.IA;
using Opc.Ua.Machinery;
using Opc.Ua.Robotics;

var nodes = new NodeStateCollection();
nodes.AddOpcUaDi(context);
nodes.AddOpcUaIA(context);
nodes.AddOpcUaMachinery(context);
nodes.AddOpcUaRobotics(context);
nodes.AddOpcUaCranesHoists(context);

CraneMotionDeviceSystemState system =
    context.CreateInstanceOfCraneMotionDeviceSystemType();
```

Register `AddOpcUaCranesHoists` on an `IEncodeableFactoryBuilder` or
`INodeStateFactoryBuilder` where the host assembles its factories. Namespace
indexes are resolved through the context's namespace table, not fixed to the
indexes used in the published XML.

## Present scope

This is the shared model/contracts package, not a crane controller or a safety
system. Server/client runtime, instance builders, equipment providers, live
snapshots, authorized speed-limit configuration and MCP workflows are separate
implementation work. Loading the model does not advertise a conformance profile
or prove runtime support for a conformance unit. No crane-motion commands or
safety bypass operations are introduced.

The official XML and documentation CSV are pinned without semantic edits.
`Model\provenance.json` records source hashes and dependencies; the local
identifier CSV is a derived drift guard, not an upstream file.
