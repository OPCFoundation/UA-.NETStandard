# Opc.Ua.XRegistry.Server

The generic server-side **xRegistry** registry node managers for OPC UA. They serve a
content-addressed resource registry in a server address space:

- **Fast path** — publishes registered resources under an Opaque NodeId whose Identifier is
  the raw content-id bytes, so a consumer resolves a resource in a single `Read`.
- **Registration** — the model's own lifecycle: `CreateGroup` / `GetOrCreateGroup` on the
  registry root, `CreateResource` / `GetOrCreateResource` on a group, and — because
  `ResourceType` is a `FileType` — the document is transferred with the inherited `Open` /
  `Read` / `Write` / `Close`. On close the server computes the resource's content-derived id
  (via an `IResourceContentIdProvider`) and publishes the Opaque fast-path node at runtime.
  `Delete(ExpectedEpoch)` gives optimistic concurrency on removal.
- **Federation** — publishes a proxy for a resource hosted by a remote registry as a real
  `ResourceType` instance carrying an `ExternalReference` and `ResourceUrl` plus the
  content-derived id (stable across registries).
- **Events** — optionally emits the xRegistry 0.5.0 native OPC UA event hierarchy for successful
  registry interactions and projection reconciliation. Event support is disabled by default and
  requires an absolute `EventSourceUrl`; generated concrete `EventState` types are reported through
  the registry/group/resource notifier chain.

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
