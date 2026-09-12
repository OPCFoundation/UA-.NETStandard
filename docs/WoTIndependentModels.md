# Importing independent readable WoT models

`WotNodeSetConverterOptions.DocumentSetMode` selects the document-set import
algorithm. **`PartitionReconstruction` remains the default.**
`IndependentReadableModels` is an explicit additional capability, not a
replacement for reconstruction or a recovery strategy after a failed import.
An undefined enum value is rejected.

## Opt in

Two independently authored readable TDs or TMs can both bind `ns1` to their own
namespace and use local NodeId `i=1`. For example, models `urn:example:a` and
`urn:example:b` can each use `ns1:Root` as their BrowseName. Import them together
through the existing document-set API:

```csharp
var options = new WotNodeSetConverterOptions
{
    DocumentSetMode = WotDocumentSetMode.IndependentReadableModels
};

// modelA and modelB are parsed WotDocument instances; the set owns their handles.
using var documents = new WotDocumentSet(
    "a", [new("a", modelA), new("b", modelB)]);
WotConversionResult<UANodeSet> imported = await WotNodeSetConverter.ToNodeSetAsync(
    documents, options, nodeResolver: resolver, cancellationToken: ct);
if (!imported.Success)
{
    throw new InvalidOperationException(string.Join(Environment.NewLine, imported.Diagnostics));
}
UANodeSet combined = imported.Value!;
```

For those two namespaces, the output table is `[urn:example:a, urn:example:b]`;
Core remains implicit at index zero. The roots are `ns=1;i=1` and `ns=2;i=1`.
Changing entry order does not change their normalized identities. Namespace URIs
are deduplicated and sorted by Unicode code point, without URI spelling changes.
Generated identities still belong to the document's own model, not whichever
namespace happens to occupy index one.

For already converted inputs, use the same options with
`MergeNodeSetPartitions(documents, partitions, options, ct)`. The existing
three-argument overload remains available. Both entry points preserve their
source documents and NodeSets; the preconverted API clones inputs before
normalization. Verified partition **export** continues to prove reconstruction
of the original source table regardless of the import option.

## Identity, metadata and value handling

Normalization covers NodeIds, BrowseNames, parents, References, aliases, role
identities, MethodDeclarationIds and DataType definitions/fields. Input-local
aliases are resolved before combining Nodes; aliases with the same name but
different meanings receive deterministic, collision-free names in the output.
Annotation strings and XML extensions are not searched for apparent NodeIds.
Localized metadata, model constraints, extension payloads and existing
provenance are retained rather than reconstructed through NodeState import/export.

Repeated model declarations must agree on their metadata and RequiredModels.
Equivalent shared Nodes can be deduplicated, but conflicting facts, duplicate
root ownership and ambiguous source hrefs fail. Root admission includes effective
generated identities as well as authored `uav:id` values; equivalent non-root
context copies do not permit two documents to claim one root. RequiredModel
constraints stay with their declaring model. ServerUris tables must agree;
independent server table synthesis is not part of this mode. As in ordinary
NodeSet export/import, the header omits local server zero: its entries declare
remote server indexes `1..ServerUris.Length`. Do not insert a fake local entry.

Standard namespace-bearing values are decoded and re-encoded with the stack's
XML codec and mapping tables, including Arguments, DataValues, nested Variants,
arrays and matrices. Decoding must consume the understood value completely;
undeclared indexes, unknown attributes/fields and lossy codec results fail with
`NamespaceRebaseUnsupported` and the source href/NodeId.
Understood value identities are validated even when no namespace relocation is
necessary. The lossless import path preserves a typed null String distinctly
from an empty String, including nested typed values, and retains String content.
These checks do not change the ordinary XML decoder's compatibility behavior.

For registered structured XML values, supply the existing message-context seam:

```csharp
options.ValueEncodingContext = messageContext;
```

Its factory can contain generated codecs or the existing AOT-safe
`Opc.Ua.Encoders.Structure`, `StructureWithOptionalFields` and `Union` types.
Register the types the payload uses before importing. Without a supplied
context, the basic Argument codec is available. The converter copies the
context's namespace/server tables and respects its encoding limits; it does not
change the caller's factory or tables. Keeping the registration context's
indexes is important for nested structure field definitions.

The copied server table reserves index zero for the local server. A declared
remote URI is matched only to positive server indexes, even when its URI equals
the configured local slot's URI. If needed, a separate positive slot is added
to the copy. Thus `svr=1;ns=1;i=42` remains remote after namespace relocation;
neither values nor References are localized by a coincidentally equal URI.
An empty configured server table or an empty local slot is also supported.
The value encoding context is not a native import destination. Ordinary
`UANodeSet.Import` continues to resolve server URIs against its actual
destination context.

Opaque XML values and binary ExtensionObject bodies are **not** heuristically
rewritten. They can remain untouched when no namespace-table change is needed;
their understood outer identities are still checked for declared indexes.
Otherwise import fails rather than claiming a safe rebase. Supplying a factory
does not turn an opaque binary body into an XML-decoded value.

## Authority and bounds

If any input carries authoritative native or archival content, its declared
interpretation is retained and the existing strict partition/header rules
apply. Conflicting authoritative headers, incompatible namespace tables and
readable overlays on native ownership remain errors. Neither a header failure
nor unequal tables cause an automatic retry in another mode.

Independent readable sets share a bounded resolution context, including input
document accounting. Node counts, JSON and NodeSet byte/depth bounds and the
final merged output are checked. Preconverted input XML is size-bounded during
writing and depth-checked before cloning. Cancellation is honored by both
entry points. Reconstruction retains its existing per-document resolver budgets.
The selected mode is reported as `DocumentSetModeSelected`.

## Registry configuration

Use the existing fluent/DI surface:

```csharp
services.AddOpcUa().AddWotRegistryServer(options =>
    options.DocumentSetMode = WotDocumentSetMode.IndependentReadableModels);
```

The equivalent configuration key is
`OpcUa:WotConRegistry:Server:DocumentSetMode`. The registered
`WotNodeSetConverterOptions` receives the mode and an optional
`IServiceMessageContext` from DI. Directly constructed converters/coordinators
accept the same converter options.

The mode is part of the registry's effective input digest, so a result produced
by the other algorithm is not reused as unchanged. Source grouping and activation
closure policy are not widened: independent import does not combine otherwise
unrelated resources into one atomic closure. Codec registration changes remain
host configuration; use the existing forced refresh when changing them.

See [verified linked document sets](WoTNodeSetConversion.md#verified-linked-document-sets)
for strict reconstruction and source-preservation guarantees.
