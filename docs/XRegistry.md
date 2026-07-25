# xRegistry — abstract registry base model

The **xRegistry** libraries implement the generic, registry-agnostic *abstract registry base model*
(Annex B) for OPC UA. They provide the substrate a concrete registry builds on: a **content-addressed
resource identity**, an **Opaque-NodeId fast path** that resolves a resource in a single `Read`, a
**`CreateResource`/`Write`/`Close` registration lifecycle** with auto-bootstrap, and a **federation**
model for resources hosted by another registry.

The libraries are deliberately domain-neutral: they know nothing about what a *resource* contains.
A concrete registry supplies its own companion namespace and a fingerprinting strategy, and reuses
everything else. The PubSub Schema Registry is the first such specialization, where the resources are
Avro / Arrow / JSON DataSet schema documents.

## Packages

| Package | Depends on | Contains |
| --- | --- | --- |
| `OPCFoundation.NetStandard.Opc.Ua.XRegistry` | `Opc.Ua.Core` | The source-generated base companion model (types, NodeIds, `NodeState`s and `*TypeClient` proxies), `XRegistryWellKnown`, `IResourceContentIdProvider` |
| `OPCFoundation.NetStandard.Opc.Ua.XRegistry.Client` | `Opc.Ua.XRegistry`, `Opc.Ua.Client` | `XRegistryClient` — fast-path resolve and lifecycle registration |
| `OPCFoundation.NetStandard.Opc.Ua.XRegistry.Server` | `Opc.Ua.XRegistry`, `Opc.Ua.Server` | The three node managers and `XRegistryServerOptions` |

`Opc.Ua.XRegistry` has no dependency on either SDK, so a codec or a shared contracts assembly can
reference the identity abstraction without pulling in the client or the server.

## Core concepts

### Content-derived resource identity

A resource's identity is **derived from its bytes**, not assigned by the server. Every registry
supplies an `IResourceContentIdProvider` that maps a document plus its format to a fingerprint:

```csharp
public interface IResourceContentIdProvider
{
    ByteString ComputeContentId(string format, ReadOnlySpan<byte> document);

    string? GetAlgorithm(string format);
}
```

`GetAlgorithm` names the (canonicalization, hash) pair used for a format and returns `null` for
formats the registry does not handle. Because the identity is content-derived it is **stable across
registries**: the same document registered in two different servers yields the same id, which is what
makes de-duplication and federation work.

### Opaque-NodeId fast path

A registered resource is reachable at an **Opaque `NodeId`** in the registry namespace whose
Identifier is the *raw content-id bytes*. A consumer that received the id on the wire therefore needs
no Browse and no fingerprint recomputation — one `Read` of that node's `Value` returns the document:

```csharp
var fastPathNodeId = new NodeId(contentId, registryNamespaceIndex);
DataValue value = await session.ReadValueAsync(fastPathNodeId, ct).ConfigureAwait(false);
```

`XRegistryFastPathNodeManager` serves these nodes and can optionally **pre-publish a seed resource**
so a freshly started server resolves at least one content-addressed resource before any registration
has happened.

### Registration lifecycle and auto-bootstrap

`XRegistryRegistrationNodeManager` exposes the write lifecycle on the resource-group object:

1. **`CreateResource`** returns an upload handle.
2. **`Write`** appends a chunk of the document to that handle, one or more times.
3. **`Close`** finalizes the upload. The server computes the content-id and algorithm from the
   accumulated bytes through the configured `IResourceContentIdProvider` and — this is the
   *auto-bootstrap* — creates the Opaque fast-path node **at runtime**, then returns
   `(ContentId, Algorithm)` to the caller.
4. **`Delete`** removes a registered resource by its content-id.

Registration is idempotent by construction: re-registering identical bytes produces the same
content-id, so the existing fast-path node is reused rather than duplicated.

### Federation

`XRegistryFederationNodeManager` publishes a **proxy** for a resource hosted by another registry. The
proxy carries an `ExternalReference` — an `ExpandedNodeId` whose `ServerIndex` names the remote server
through the local `ServerArray`, and whose `NamespaceUri` and `Identifier` are the remote resource
node's identity — and/or a plain `ResourceUrl`, alongside the resource's content-id. Since the
content-id is stable across registries, the same resource federated from several endpoints keeps
**one** identity and can be de-duplicated by consumers.

## Server-side usage

Configure the node managers through `XRegistryServerOptions` and add them to the server's node
manager list. The options object carries the registry namespace, the content-id provider, the
optional seed and federation resources, and the resource-exhaustion bounds:

```csharp
var options = new XRegistryServerOptions
{
    RegistryNamespaceUri = "http://example.org/UA/MyRegistry/",
    ContentIdProvider = new MyContentIdProvider(),

    // Optional: pre-publish one resource on the fast path at start-up.
    PublishSeedResource = true,
    SeedDocument = seedBytes,
    SeedFormat = "avro",
};

var registration = new XRegistryRegistrationNodeManager(server, configuration, options);
var fastPath = new XRegistryFastPathNodeManager(server, configuration, options);
var federation = new XRegistryFederationNodeManager(server, configuration, options);
```

The registry's companion model is **compiled into the assembly** by the OPC UA model source
generator: `Opc.Ua.XRegistry.NodeSet2.xml` is a generator input (`AdditionalFiles`), so the
ObjectTypes, Methods, Variables, NodeId constants, `NodeState` classes and typed
[ObjectType proxies](../tools/Opc.Ua.SourceGeneration/readme.md) are emitted at build time. No
NodeSet2 XML is parsed at runtime — each node manager simply returns the generated model from
`LoadPredefinedNodes`:

```csharp
protected override NodeStateCollection LoadPredefinedNodes(ISystemContext context)
{
    return new NodeStateCollection().AddOpcUaXRegistry(context);
}
```

A concrete registry composes its own companion model on top of the base model in dependency
order, declaring `RequiredModel` on the xRegistry namespace in its NodeSet.

> **Note:** the model occupies NodeIds 63000-63999 in the registry namespace. The instance
> identifiers in `XRegistryWellKnown` live above that range so a materialized instance can never
> collide with a model node.

### Resource-exhaustion bounds

The registration Methods are remotely callable, so `XRegistryServerOptions` bounds every unbounded
dimension. Exceeding a bound fails the call rather than the server:

| Option | Default | Enforced on | Status code |
| --- | --- | --- | --- |
| `MaxConcurrentUploads` | 64 | `CreateResource` | `BadTooManyOperations` |
| `MaxResourceBytes` | 16 MiB | `Write` | `BadRequestTooLarge` |
| `MaxRegisteredResources` | 4096 | `Close` | `BadTooManyOperations` |

## Client-side usage

`XRegistryClient` binds to a connected session and the registry's companion namespace, and resolves
the namespace index up front (throwing `BadNodeIdUnknown` when the server does not expose it).

Resolving a resource from an id received on the wire is a single call. It returns a **null**
`ByteString` — check `IsNull` — when no fast-path node is registered, so the caller can fall back to a
Browse or a registry-specific download:

```csharp
var client = new XRegistryClient(session, "http://example.org/UA/MyRegistry/");

ByteString document = await client.ResolveResourceAsync(contentId, ct).ConfigureAwait(false);
if (document.IsNull)
{
    // Not registered on this server — fall back.
}
```

Registering a document drives the whole lifecycle and returns the server-computed identity. The
group and Method NodeIds are obtained by Browsing the registry's well-known object; a concrete
registry typically wraps this in a domain-specific helper:

```csharp
(ByteString contentId, string? algorithm) = await client.RegisterResourceAsync(
    resourceGroupObjectId,
    createResourceMethodId,
    writeMethodId,
    closeMethodId,
    documentBytes,
    format: "avro",
    chunkSize: 4096,
    ct).ConfigureAwait(false);
```

## Well-known identifiers

`XRegistryWellKnown` carries the base companion namespace URI and the provisional NodeIds a generic
registry materializes. A concrete registry reuses the same numeric identifiers inside **its own**
namespace, so the client-side lookup logic is shared.

> **Note:** the NodeIds are *provisional*. Final identifiers are assigned by the OPC Foundation.

| Member | Value | Meaning |
| --- | --- | --- |
| `XRegistryNamespaceUri` | `http://opcfoundation.org/UA/xRegistry/` | Abstract base companion namespace |
| `ResourceGroupObject` | 63001 | The registration resource-group object |
| `CreateResourceMethod` | 63002 | Obtain an upload handle |
| `WriteMethod` | 63003 | Append document bytes |
| `CloseMethod` | 63004 | Finalize, fingerprint and publish |
| `DeleteMethod` | 63005 | Remove a registered resource |
| `FederationProxyObject` | 64001 | Federated resource proxy |
| `FederationExternalReferenceProperty` | 64002 | Proxy's `ExternalReference` |
| `FederationResourceUrlProperty` | 64003 | Proxy's `ResourceUrl` |
| `FederationContentIdProperty` | 64004 | Proxy's content-id |

## Related documentation

* [Source generation](../tools/Opc.Ua.SourceGeneration/readme.md) — how the companion model and its typed proxies are compiled into the assembly.
* [Node management](NodeManagement.md) — the node manager model the three managers build on.
* [PubSub (Part 14)](PubSub.md) — the PubSub Schema Registry specialization of this model.
