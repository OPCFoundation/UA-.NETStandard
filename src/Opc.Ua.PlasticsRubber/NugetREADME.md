# Opc.Ua.PlasticsRubber

Information-model foundations based on OPC 40083: source-generated typed
node states, data types, standard identifiers, client proxies, encodeable factory
registration, and the `AddOpcUaPlasticsRubber` compiled loader.

## Model identity

- Wire model URI: `http://opcfoundation.org/UA/PlasticsRubber/GeneralTypes/`
- Model version: `1.03`
- Model publication: `2021-05-10T12:00:00Z`
- CLR namespace: `Opc.Ua.PlasticsRubber`
- Generator name/prefix: `PlasticsRubber` / `Opc.Ua.PlasticsRubber`

Each wire model URI has exactly one producing assembly. Existing Core, DI,
Machinery and IA model producers are reused, never regenerated in this package.
The model version is independent of the NuGet package version.

## Loading

```csharp
using Opc.Ua.PlasticsRubber;

context.NamespaceUris.GetIndexOrAppend(Namespaces.PlasticsRubber);
nodes.AddOpcUaPlasticsRubber(context);
messageContext.Factory.Builder.AddOpcUaPlasticsRubber().Commit();
```

`nodes` is a `NodeStateCollection`, `context` is the hosting `ISystemContext`, and
`messageContext` is a `ServiceMessageContext`. Load dependency models first using
their existing compiled loaders and register their data types explicitly as well.
This package's factory extension registers only its own data types.
Loading model declarations alone does not implement device behavior.
Server/client runtimes are separate work; no runtime behavior or certification
is claimed here.

## Provenance

This is an independently maintained implementation **based on** the OPC 40083
v1.03 interface, built by modifying the explicitly MIT-licensed v1.02 model.
It is **not** the official v1.03 NodeSet. No redistribution licence has been
verified for that artifact, and neither it nor its prose, schema blobs, or
identifier table is included or used by the authoring script.

- [Pinned MIT v1.02 basis](https://raw.githubusercontent.com/OPCFoundation/UA-Nodeset/96a08945425d0295edb3607d2a38f6841ef44046/PlasticsRubber/GeneralTypes/1.02/Opc.Ua.PlasticsRubber.GeneralTypes.NodeSet2.xml)
- Basis SHA-256: `5fe4469bf64ebab5a828d2f888dca85e985cf2d48286029d37565c09e4ac9ba8`
- [Published interface specification](https://reference.opcfoundation.org/specs/OPC-40083/v1.03)
- [Specification agreement](https://reference.opcfoundation.org/specs/OPC-40083/v1.03/agreement-of-use)

`Model\author-interface-model.py` applies the independently authored interface
changes to the unchanged licensed basis in `Model\Basis`: thirteen added nodes,
nine modified nodes, and no removals. It adds the classified active-error
structure and encoding objects, monitored-parameter alarm/status declarations,
and drive position properties, and corrects the reassigned node 6711 reference.
Both BinarySchema and XMLSchema dictionaries are regenerated from the resulting
DataType definitions, including inherited fields. The original basis licence is
preserved; it is not a grant for the official v1.03 publication.

The `1.03` version and `2021-05-10T12:00:00Z` publication metadata identify the
implemented wire contract required by dependent models, not this implementation's
origin. The identifier guard is derived independently from this authored model,
using the repository's symbolic-name derivation; it is not an official table.
`Model\companion-models.json` records provenance and hashes for all fourteen models.
`Model\NOTICE.txt` preserves the original copyright and licence notices and is
included in each package as `licenses\CompanionModels.NOTICE.txt`.

This work is based on the specification only. **No certification, compliance,
or conformance claim is made.** The specification agreement is not represented
as an MIT licence for the specification or the official v1.03 artifact.

## Rebuild and validation

From the repository root:

```powershell
python src\Opc.Ua.PlasticsRubber\Model\author-interface-model.py
python src\Opc.Ua.PlasticsRubber\Model\author-interface-model.py --check
python tests\Opc.Ua.PlasticsRubber.Tests\validate-model-inputs.py
dotnet test tests\Opc.Ua.PlasticsRubber.Tests\Opc.Ua.PlasticsRubber.Tests.csproj `
    -f net10.0 -p:CustomTargetFrameworks=net10.0 -p:CustomTestTarget=net10.0 `
    -m:1 -p:UseSharedCompilation=false
```

The input validator checks all fourteen model hashes, complete XML-backed identifier
guards and the authored common model's persisted structural/encoding fingerprint.
The fingerprint was verified against a separately held v1.03 reference through a
read-only comparison. That comparison excludes display/description prose and
tool-specific extensions, but retains every node, field, reference, value, model
header, dictionary wire-field order and XML occurrence rule. It does not retain
or redistribute the reference XML. The default check needs repository files only.
Negative checks prove that changed references, ranks and wire-field order are detected.

The NUnit project exercises the generated metadata, compiled loaders, type clients,
factory registrations and populated binary/JSON/XML structure round trips. These
are implementation tests, not a certification or a complete device-behavior suite.

## Platforms

This package follows the stack's library target-framework matrix. Generated
registration and compiled loaders do not require reflection-based XML loading
and are intended for NativeAOT applications.
