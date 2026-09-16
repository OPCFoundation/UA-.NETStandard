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
The existing asset NodeId and placement remain stable, and automatically assigned
property/action identities use the existing legacy conventions.

An authored root NodeId or qualified BrowseName must agree with that existing
owner. Native/archive identities are not silently rebased. A preserved graph
must use the legacy manager's owned asset namespace for its instance nodes;
unsupported ownership is rejected rather than partially published.

## Upload, discovery and restart

Uploaded UTF-8 bytes remain authoritative for downloads and persistence,
including literal spellings and unknown members. The provider-facing
`ThingDescription` is not serialized over those uploaded bytes.

For endpoint discovery, the generated description is validated and prepared
before creating its published asset owner. On restart, a persisted native
document is likewise prepared before asset creation. An invalid persisted type
binding leaves its source file intact and does not publish an unmaterialized
owner or connect a provider.

Failed native admission leaves an existing valid asset/provider/document in
place. Replacing a valid graph reuses the existing R43 interaction indexing and
cleanup paths; the additional native ownership/reference augmentation is retired
with its graph. Registry mirroring/transaction semantics and event modes are
separate contracts and are not changed by this mapping.

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
