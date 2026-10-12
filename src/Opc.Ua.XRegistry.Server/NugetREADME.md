# Opc.Ua.XRegistry.Server

The generic server-side **xRegistry** registry node managers for OPC UA. They serve a
structurally addressed registry with an independent content fast path.

`XRegistryRegistrationNodeManager`, `XRegistryFastPathNodeManager`, and
`XRegistryFederationNodeManager` derive from `AsyncCustomNodeManager`. Host them through
`IAsyncNodeManagerFactory`; directly constructed managers use the same awaited lifecycle.
Custom subclasses override `LoadPredefinedNodesAsync`, `CreateAddressSpaceAsync`, and
`SessionClosingAsync`, awaiting their base implementations. Await `DeleteAddressSpaceAsync`
before disposal to drain active operations and release handles.

The managers provide:

- **Fast path** — publishes registered resources under an Opaque NodeId whose Identifier is
  the raw content-id bytes, so a consumer resolves a resource in a single `Read`.
- **Registration** — the model's own lifecycle: `CreateGroup` / `GetOrCreateGroup` on the
  registry root, `CreateResource` / `GetOrCreateResource` on a group, and — because
  `ResourceType` is a `FileType` — the document is transferred with the inherited `Open` /
  `Read` / `Write` / `Close`. `Xid` remains the structural registry-relative Resource or Version
  path. Only a successful Close with accepted, byte-different staged content updates Version
  `Epoch`/`ModifiedAt`; it computes a separate content key (via `IResourceContentIdProvider`) and
  publishes or reuses the reference-counted Opaque fast-path node. Empty, rejected, clean, and
  byte-identical closes are side-effect free, and each Version permits one writer.
  `Delete(ExpectedEpoch)` gives optimistic concurrency on removal.
- **Federation** — publishes a proxy for a resource hosted by a remote registry as a real
  `ResourceType` instance carrying an `ExternalReference` and `ResourceUrl` while retaining
  structural `ResourceId`, `VersionId`, and `Xid`.
- **Events** — optionally emits the xRegistry 0.5.0 native OPC UA event hierarchy for successful
  registry interactions and projection reconciliation. Event support is disabled by default and
  requires an absolute `EventSourceUrl`; generated concrete `EventState` types are reported through
  the Server/registry/group/resource notifier chain.

Version events use the corresponding version file as `SourceNode`; resource events use the committed
default-version file. Deleted events retain their former source identity and are reported through the
nearest surviving notifier. Enabling events emits every mandatory event for each supported mutation;
whole-registry creation/deletion events remain recommendations.

Version `Labels`, `Epoch`, `CreatedAt`, and `ModifiedAt` are independent per Version. Resource Meta
uses synchronized `MetaLabels`, `MetaEpoch`, `MetaCreatedAt`, and `MetaModifiedAt`; Version changes
do not mutate Resource Meta unless Versions or default-Version state also change.

Existing projection strategies and the original six-parameter `XRegistryProjectionContext`
constructor remain compatible with events disabled. Event-enabled projections provide one atomic
generation through `IXRegistryProjectionGenerationProvider`; version-aware projections opt into
`IXRegistryVersionedProjectionStrategy`.

Collection-qualified, metadata-only registries use the same `XRegistryProjectionEngine`. The
registry state owner returns an `IXRegistryCollectionProjectionSnapshot` in each committed
`XRegistryProjectionGeneration`. Groups and `MetadataResourceType` Resources are identified by
collection-qualified Xids, so `/endpoints/orders` and `/messagegroups/orders` never alias; the
collection folders are `GroupCollectionType` views, not entities. An
`IXRegistryCollectionProjectionStrategy` creates the typed nodes and routes their
`Delete(ExpectedEpoch)` to the committed-state owner. The engine publishes committed epochs
verbatim and ignores a generation older than the active one. It validates a generation and builds
its new nodes before changing the address space, and removes stale or replaced nodes before it adds
new ones. A committed deletion whose activation fails returns `Uncertain`; the next activation
retries the pending removals. `DetachAsync` removes every projected node.

Document bytes live behind an injectable `IXRegistryResourceStore`; an in-process and a
file-backed implementation ship with the package. Registry writes always require a
`SignAndEncrypt` secure channel.

A concrete registry (for example a schema registry) supplies an
`IResourceContentIdProvider` and its own companion namespace/NodeSet.

```csharp
options.EventsEnabled = true;
options.EventSourceUrl = "https://registry.example.com";
options.GroupsAttributeName = "groups";
options.ResourcesAttributeName = "resources";
options.ResourceDocumentAttributeName = "schema";
```
