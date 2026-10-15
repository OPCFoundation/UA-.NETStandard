# OPC UA MachineTool models

This package contains the released OPC 40501-1 MachineTool 1.02.0 and OPC 40505
Wireless Machine Tool Peripherals (WMTP) 1.0.0 information models. It supplies
generated identifiers, typed node states, encodeable structures, ObjectType
client proxies, and factories without a server or client runtime dependency.

The wire namespaces remain independent:

| Model | CLR namespace | Wire namespace |
| --- | --- | --- |
| MachineTool | `Opc.Ua.MachineTool` | `http://opcfoundation.org/UA/MachineTool/` |
| WMTP | `Opc.Ua.MachineTool.WMTP` | `http://opcfoundation.org/UA/WMTP/` |

Both models reuse the existing DI, Machinery and related dependency packages.
The MachineTool model retains its mandatory deprecated Production declarations;
the newer Machinery JobManagement model does not replace them.

Model loading does not advertise conformance or implement equipment operations.
WMTP is not a MachineTool subtype and does not require a MachineTool instance or
a Bluetooth transport.

## Independent model loading

Use `MachineToolParts.MachineTool`, `MachineToolParts.Wmtp` or
`MachineToolParts.All` explicitly. The default is MachineTool only, so a consumer
such as GMS does not opt into WMTP merely by referencing this assembly.
`MachineToolParts.None` is a no-op; undefined flags are rejected.

```csharp
using Opc.Ua.MachineTool;

MachineToolParts parts = MachineToolParts.Wmtp;
ArrayOf<string> namespaces = MachineToolModels.GetNamespaceUris(parts);
messageContext.Factory.Builder.AddMachineToolTypeSystem(parts).Commit();
nodeStateFactory.AddMachineToolTypeSystem(parts);
predefinedNodes.AddMachineToolTypeSystem(systemContext, parts);
```

The closure includes DI and all transitive companion dependencies, excluding
the implicit OPC UA core namespace at index zero. Repeated loading into the
same predefined-node collection retains existing nodes without duplicates.
Factory registration remains explicit and does not commit the encodeable
builder on the caller's behalf.

Advanced callers can use the generated `AddOpcUaMachineTool` and
`AddOpcUaMachineToolWMTP` overloads directly on node collections, encodeable
builders and node-state builders when they manage dependency loading
themselves. The latter extensions live in `Opc.Ua.MachineTool.WMTP`.
Generated instance factories and ObjectType proxies remain available for typed
runtime reuse. Server-only fluent accessors are deliberately not emitted by this
runtime-independent model assembly.

## Model provenance

In the source repository, the original XML, published identifier tables,
documentation tables and WMTP profile metadata are retained under `Model`,
with their original filenames.
They are pinned to OPCFoundation/UA-Nodeset commit
`96a08945425d0295edb3607d2a38f6841ef44046`. Only line endings are normalized.
`Model\provenance.json` records immutable source URLs, hashes and model metadata.

The separate `*.Derived.NodeIds.csv` tables are generator drift guards, not
NodeId sources. They are derived with the repository's
`tools\nodesets\derive-identifier-table.py`; the authoritative numeric identifiers
come from the unmodified NodeSets. No dependency models are regenerated here.

- [OPC 40501-1](https://reference.opcfoundation.org/specs/OPC-40501-1)
- [OPC 40505](https://reference.opcfoundation.org/specs/OPC-40505)
