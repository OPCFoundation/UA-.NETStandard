# Opc.Ua.GranulateDrying

Information-model foundations based on OPC 40082-6: source-generated typed
node states, data types, standard identifiers, client proxies, encodeable factory
registration, and the `AddOpcUaGranulateDrying` compiled loader.

## Model identity

- Wire model URI: `http://opcfoundation.org/UA/PlasticsRubber/GDD/`
- Model version: `1.0.0`
- Model publication: `2026-10-01T00:00:00Z`
- CLR namespace: `Opc.Ua.GranulateDrying`
- Generator name/prefix: `GranulateDrying` / `Opc.Ua.GranulateDrying`

Each wire model URI has exactly one producing assembly. Existing Core, DI,
Machinery and IA model producers are reused, never regenerated in this package.
The model version is independent of the NuGet package version.

## Loading

```csharp
using Opc.Ua.GranulateDrying;

context.NamespaceUris.GetIndexOrAppend(Namespaces.GranulateDrying);
nodes.AddOpcUaGranulateDrying(context);
messageContext.Factory.Builder.AddOpcUaGranulateDrying().Commit();
```

`nodes` is a `NodeStateCollection`, `context` is the hosting `ISystemContext`, and
`messageContext` is a `ServiceMessageContext`. Load dependency models first using
their existing compiled loaders and register their data types explicitly as well.
This package's factory extension registers only its own data types.
Loading model declarations alone does not implement device behavior.
Server/client runtimes are separate work; no runtime behavior or certification
is claimed here.

## Model validation

`Opc.Ua.PlasticsRubber.Tests` checks all 4,304 published numeric nodes against the
compiled loader, including nested placeholder declarations. OptionalPlaceholder
and MandatoryPlaceholder branches remain available in type declarations;
concrete instances do not automatically receive placeholder templates.
Device runtimes must create their actual named children.

## Provenance

- [Pinned official NodeSet](https://raw.githubusercontent.com/OPCFoundation/UA-Nodeset/96a08945425d0295edb3607d2a38f6841ef44046/PlasticsRubber/GDD/Opc.Ua.PlasticsRubber.GDD.NodeSet2.xml)
- Upstream revision: `96a08945425d0295edb3607d2a38f6841ef44046`
- SHA-256: `9ca7395872123ec4a63821927625689011eefef791c023c55c67a6091b68dbd4`

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
