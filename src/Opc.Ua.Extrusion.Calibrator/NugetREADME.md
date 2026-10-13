# Opc.Ua.Extrusion.Calibrator

Information-model foundations based on OPC 40084-10: source-generated typed
node states, standard identifiers, client proxies, node-state factory registration,
and the `AddOpcUaExtrusionCalibrator` compiled loader.

## Model identity

- Wire model URI: `http://opcfoundation.org/UA/PlasticsRubber/Extrusion_v2/Calibrator/`
- Model version: `2.00`
- Model publication: `2022-05-01T00:00:00Z`
- CLR namespace: `Opc.Ua.Extrusion.Calibrator`
- Generator name/prefix: `ExtrusionCalibrator` / `Opc.Ua.Extrusion.Calibrator`

Each wire model URI has exactly one producing assembly. Existing Core, DI,
Machinery and IA model producers are reused, never regenerated in this package.
The model version is independent of the NuGet package version.

## Loading

```csharp
using Opc.Ua.Extrusion.Calibrator;

context.NamespaceUris.GetIndexOrAppend(Namespaces.ExtrusionCalibrator);
nodes.AddOpcUaExtrusionCalibrator(context);
```

`nodes` is a `NodeStateCollection` and `context` is the hosting `ISystemContext`.
This model declares no data types and has no `IEncodeableFactoryBuilder` overload.
Load dependency models and register their data types first using the existing
compiled loaders and factory extensions.
Loading model declarations alone does not implement device behavior.
Server/client runtimes are separate work; no runtime behavior or certification
is claimed here.

## Provenance

- [Pinned official NodeSet](https://raw.githubusercontent.com/OPCFoundation/UA-Nodeset/96a08945425d0295edb3607d2a38f6841ef44046/PlasticsRubber/Extrusion_v2/Calibrator/2.00/Opc.Ua.PlasticsRubber.Extrusion_v2.Calibrator.NodeSet2.xml)
- Upstream revision: `96a08945425d0295edb3607d2a38f6841ef44046`
- SHA-256: `18f872bd54680fe599919445730f1b561b829ab871b1c29d30cede1cb48bb775`

The NodeSet is the byte-for-byte upstream publication with its individual MIT
notice preserved. `Model\*.Upstream.NodeIds.csv` retains the published table;
`tools\nodesets\derive-identifier-table.py` derives the generator-compatible
identifier guard without changing node identifiers.
Where the published table and XML differ, the guard covers the XML nodes only.
Every missing or surplus table row is recorded in the provenance manifest; no
node or identifier is fabricated to reconcile the publications.

The shared common model is the independently maintained implementation in
`Opc.Ua.PlasticsRubber`, based on the MIT v1.02 model and authored v1.03 interface
changes. It is not a redistributed official v1.03 NodeSet. Full source hashes and
dependency headers are recorded in that package's `Model\companion-models.json`.

## Platforms

This package follows the stack's library target-framework matrix. Generated
registration and compiled loaders do not require reflection-based XML loading
and are intended for NativeAOT applications.
