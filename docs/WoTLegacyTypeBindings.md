# Existing-type bindings on legacy WoT assets

Legacy `WoTAssetFileType.CloseAndUpdate` uses the shared WoT semantic conversion
for native/type-bound documents before connecting a replacement asset provider
or changing the published asset graph. It applies the converted instance to the
existing legacy owner rather than creating another executing root or a runtime
NodeManager for that asset.

This implements the existing-type mapping required by WoT Connectivity
Sections 7.2 and 13.2 on the legacy surface. It is not a full TD/TM JSON Schema
validator or a claim of complete companion-specification conformance.

## Type and declaration authority

The default path uses `WotNodeSetDocumentConverter`, including its loaded
`AddressSpaceWotNodeResolver` context. Both a namespace-qualified ObjectType in
`@type` and a `ua:HasTypeDefinition` link go through that same type resolution.
Missing, multiple, wrong-NodeClass and conflicting type identities fail instead
of falling back to the legacy interface type.

Standard `Thing` annotations identify a TD without requiring `uav:object`.
Native admission uses `WotNodeSetConverter.RequiresNativeMappingAsync` with the
same loaded node context as conversion. A definitive type link always requires
admission, including when its target is missing. A readable type name requires
admission when its namespace is held, even if the named type does not exist.
The same admission check covers property, action and event scopes, using each
affordance's active context rather than requiring a root type annotation.
Ordinary annotations in unloaded namespaces retain unbound legacy behavior.
Classification does not validate the binding or replace the uploaded bytes.

The shared declaration merge retains the loaded declaration's QName,
ReferenceType, VariableType, DataType, ValueRank and ArrayDimensions. An
unqualified member can populate its uniquely named declaration; an explicit
qualified name is not replaced by a local-name guess. Missing mandatory
Variable declarations are emitted by the shared native conversion, not by the
legacy primitive property mapper.

The legacy adapter imports the complete produced NodeSet with the public
`UANodeSet.Import` and parent-linking APIs. Converted action argument Properties
retain their native types and ranks. Variables also receive the legacy
`HasWoTComponent` relation while retaining their native hierarchy/reference facts.
This augmentation is prepared idempotently on the detached candidate before any
provider or old-generation effects. A native root may repeat the owner's fixed
`HasInterface` reference or author the `HasWoTComponent` relation.
The existing asset NodeId and placement remain stable, and automatically assigned
property/action identities use the existing legacy conventions.
Mandatory properties with colliding local names use separate escaped namespace-URI
and local-name path segments. This avoids dependence on declaration order or
server namespace indexes. Unique local names and authored interaction identities
retain their legacy paths.
Argument Properties use the reserved `InputArguments` / `OutputArguments` children
of their owning action path, so authored `_in`/`_out` action names cannot alias them.

An authored root NodeId or qualified BrowseName must agree with that existing
owner. Native/archive identities are not silently rebased. A preserved graph
must use the legacy manager's owned asset namespace for its instance nodes;
unsupported ownership is rejected rather than partially published.

Native non-placement references retain both directions. Inverse hierarchical
references must agree with the existing management parent and `Organizes`
placement; a conflicting parent is rejected before provider effects rather
than silently discarded. Classification uses loaded ReferenceTypes and any
ReferenceType ancestry in the prepared native graph. Retirement removes only
edges owned by that native generation, preserving fixed and preexisting edges.

## Upload, discovery and restart

Uploaded UTF-8 bytes remain authoritative for downloads and persistence,
including literal spellings and unknown members. The provider-facing
`ThingDescription` is not serialized over those uploaded bytes.

For endpoint discovery, the generated description is validated and prepared
before creating its published asset owner. Its source-generated JSON model
retains unmodeled root, affordance and value-schema members, including native
envelopes, projections and interaction identities, so serialization cannot
discard those identities before admission. On restart, a persisted native
document is likewise prepared before asset creation. An invalid persisted type
binding leaves its source file intact and does not publish an unmaterialized
owner or connect a provider.

Admission reserves the prospective owner's complete fixed child hierarchy,
including the `File` node and its generated properties, methods and argument
nodes, even when the owner is not yet published. Reservation and publication
use the same asset creation and child-identity allocation code, with
`NodeState.GetInstanceHierarchy` enumerating the actual generated subtree.
Only those identities are reserved, not a string prefix; the selected native
root still reuses the owner's identity.

Failed native admission leaves an existing valid asset/provider/document in
place. Replacing a valid graph reuses the existing R43 interaction indexing and
cleanup paths; the additional native ownership/reference augmentation is retired
with its graph. Only root references actually added by that graph are owned and
removed during retirement; pre-existing owner references remain. Registry
mirroring/transaction semantics and event modes are separate contracts and are
not changed by this mapping.

## Direct and DI configuration

No hook is required for the stock converter:

```csharp
var options = new WotConnectivityServerOptions
{
    ThingDescriptionStorageFolder = storageFolder
};
options.Bindings.Add(assetProviderFactory);
var factory = new WotConnectivityNodeManagerFactory(options);
await server.NodeManagerLifecycle.AddAsync(factory, callerContext: null, ct);
```

An application can inject the existing converter interface explicitly:

```csharp
options.DocumentConverter = documentConverter;
```

For hosting, an options-supplied converter wins; otherwise the registered
`IWotDocumentConverter` is used when options are composed:

```csharp
services.AddSingleton<IWotDocumentConverter>(documentConverter);
services.AddOpcUa().AddWotConServer(options =>
{
    options.ThingDescriptionStorageFolder = storageFolder;
    options.Bindings.Add(assetProviderFactory);
});
```

A custom converter must return the complete authoritative native graph, its
selected root and resolved local affordances, or an explicit failure. It must not
return a success-shaped partial mapping. The stock converter is supplied with
the running AddressSpace context by the legacy host.

## Compatibility boundaries

Plain unbound legacy descriptions retain their previous provider/property/action
behavior, including explicit `BadConfigurationError` for unmappable properties.
Native/type-bound descriptions use stricter shared admission instead of silently
skipping an invalid native member. Native event execution and new type declarations
outside the manager's owned namespace require their corresponding provider/
ownership contract; they are not approximated by the primitive legacy mapper.

No new Method signature or model NodeId is introduced. A generated legacy
`IWoTAssetState` initializes its own interface type; callers must not assume an
unbound root is `BaseObjectType`. Binding replaces the actual initialized type
with the resolved ObjectType, while an unbound replacement restores the owner's
original initialized type.
