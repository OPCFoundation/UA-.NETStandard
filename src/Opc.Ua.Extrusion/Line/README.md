# Opc.Ua.Extrusion.Line

This part is included in the single `Opc.Ua.Extrusion` model package.
It is not a separate NuGet package.

Information-model foundations based on OPC 40084-2: source-generated typed
node states, data types, standard identifiers, client proxies, encodeable factory
registration, and the `AddOpcUaExtrusionLine` compiled loader.

## Model identity

- Wire model URI: `http://opcfoundation.org/UA/PlasticsRubber/Extrusion_v2/ExtrusionLine/`
- Model version: `2.00`
- Model publication: `2022-05-01T00:00:00Z`
- CLR namespace: `Opc.Ua.Extrusion.Line`
- Generator name/prefix: `ExtrusionLine` / `Opc.Ua.Extrusion.Line`

Each wire model URI has exactly one producing assembly. Existing Core, DI,
Machinery and IA model producers are reused, never regenerated in this package.
The model version is independent of the NuGet package version.

## Loading

```csharp
using Opc.Ua.Extrusion.Line;

context.NamespaceUris.GetIndexOrAppend(Namespaces.ExtrusionLine);
nodes.AddOpcUaExtrusionLine(context);
messageContext.Factory.Builder.AddOpcUaExtrusionLine().Commit();
```

`nodes` is a `NodeStateCollection`, `context` is the hosting `ISystemContext`, and
`messageContext` is a `ServiceMessageContext`. Load dependency models first using
their existing compiled loaders and register their data types explicitly as well.
The part-specific factory extension registers only this model's data types.
Loading model declarations alone does not implement device behavior.
Use `Opc.Ua.Extrusion.Server` and `Opc.Ua.Extrusion.Client` for runtime behavior.
No formal certification is claimed.

## Provenance

- [Pinned official NodeSet](https://raw.githubusercontent.com/OPCFoundation/UA-Nodeset/96a08945425d0295edb3607d2a38f6841ef44046/PlasticsRubber/Extrusion_v2/ExtrusionLine/2.00/Opc.Ua.PlasticsRubber.Extrusion_v2.ExtrusionLine.NodeSet2.xml)
- Upstream revision: `96a08945425d0295edb3607d2a38f6841ef44046`
- SHA-256: `21dcf568e4a819343c88741bb33ad89323b0e21cdc7935d70d19e44d5f0d9319`

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
