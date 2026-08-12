# xRegistry — abstract registry base model

The **xRegistry** libraries implement the generic, registry-agnostic *abstract registry base model*
for OPC UA. They provide the substrate a concrete registry builds on: a **content-addressed
resource identity**, an **Opaque-NodeId fast path** that resolves a resource in a single `Read`, a
**model-driven registration lifecycle** with auto-bootstrap, and a **federation** model for resources
hosted by another registry.

The libraries are deliberately domain-neutral: they know nothing about what a *resource* contains.
A concrete registry supplies its own companion namespace and a fingerprinting strategy, and reuses
everything else. The PubSub Schema Registry, WoT Connectivity registry and AAS V3 registry are concrete
specializations: PubSub resources are schema documents, WoT resources are Thing Description /
Thing Model documents, and AAS resources are shell, submodel, concept-description, package and
environment documents.

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

// Use the ranged read extension rather than a plain ReadValue: a document larger than the
// session's MaxByteStringLength is fetched in slices instead of failing.
ByteString document = await session.ReadBytesAsync(fastPathNodeId, 0, ct).ConfigureAwait(false);
```

`XRegistryFastPathNodeManager` serves these nodes and can optionally **pre-publish a seed resource**
so a freshly started server resolves at least one content-addressed resource before any registration
has happened.

### Registration lifecycle and auto-bootstrap

The registry serves the model's own Methods. A registry root (`RegistryType`) is materialized from
the compiled model, and groups and resource versions are created beneath it at runtime:

1. **`RegistryType.CreateGroup(GroupId)`** returns the new group's NodeId; `GetOrCreateGroup`
   is the idempotent form and also reports `Created`.
2. **`GroupType.CreateResource(ResourceId, VersionId, RequestFileOpen)`** creates a resource
   version and returns `(ResourceNodeId, AssignedVersionId, FileHandle)`. An empty `VersionId`
   lets the server assign the next one; `GetOrCreateResource` additionally reports `Created`.
3. Because `ResourceType` **is a `FileType`**, the document is streamed with the standard
   `Write`/`Read` file Methods against the handle — there is no registry-specific transfer.
4. **`Close`** finalizes the upload. The server computes the content-id from the accumulated bytes
   through the configured `IResourceContentIdProvider`, commits the document to the
   [resource store](#resource-storage), bumps the resource's `Epoch`, and — this is the
   *auto-bootstrap* — publishes the Opaque fast-path node.
5. **`Delete(ExpectedEpoch)`** on a resource or a group removes it. The epoch is an
   optimistic-concurrency check: a caller holding a stale epoch is rejected with
   `Bad_InvalidState` rather than deleting a newer version. Passing `0` disables the check, which
   is how a caller deliberately forces the operation without having read the entity first.

Registration is idempotent by construction: re-registering identical bytes produces the same
content-id, so the existing fast-path node is reused rather than duplicated.

### File open modes

The handle returned by `CreateResource` / `GetOrCreateResource` is opened with **EraseExisting**
semantics — a newly created version starts empty. `GetOrCreateResource` returns a write handle for an
*existing* version too, so a caller can replace its document in the same call; a caller that only
wanted to look the version up closes that handle without writing, which releases it and leaves the
resource untouched.

Reopening a version with the inherited `Open` uses the standard `FileType` mode bits (OPC 10000-5 §C:
Read = 1, Write = 2, EraseExisting = 4, Append = 8):

| Mode | Behaviour |
| --- | --- |
| `Read` | Read the committed document. |
| `Write \| EraseExisting` | Replace the document wholesale. |
| `Write \| Append` | Start from the stored bytes with the cursor at the end. |
| `Write` | Start from the stored bytes with the cursor at 0 — writes replace only the range they cover and **do not** truncate the remainder. |

A mode requesting neither read nor write, both together, or `EraseExisting`/`Append` without `Write`
is rejected with `Bad_InvalidArgument`. A handle is valid only on the resource *and* the session that
opened it, and a session's handles are released when it closes.

### Resource storage

Document bytes live behind an injectable `IXRegistryResourceStore`. Because a resource is a
`ResourceType`, which *is* a `FileType`, the store mirrors the file access model: reads and writes are
**offset and length based**, so a document never has to be materialized as a whole.

```csharp
var options = new XRegistryServerOptions
{
    // Keeps the documents in the server process (the default).
    ResourceStore = new InMemoryResourceStore()
};

// Or back them with files so they outlive the process and a shared volume can serve a cluster.
options.ResourceStore = new FileSystemResourceStore("/var/lib/xregistry");
```

`FileSystemResourceStore` is built on the `IFileSystem` abstraction, so a deployment can
substitute its own — and a test can run it against a `VirtualFileSystem` without touching disk:

```csharp
using var fileSystem = new VirtualFileSystem();
using var store = new FileSystemResourceStore("resources", fileSystem);
```

The server pieces are also wired for dependency injection, with direct construction still supported:

```csharp
services
    .AddXRegistryContentIdProvider<MyContentIdProvider>()
    .AddXRegistryFileSystemResourceStore("/var/lib/xregistry")
    .AddXRegistryServer(options => options.RequireEncryptionForReads = true);
```

`XRegistryServerOptions` is sealed. The three node managers are deliberately **not** sealed:
subclassing them is the server-side extension seam a domain registry uses to serve its own companion
model on top of the base one, mirroring how a domain client derives from `XRegistryClient`.

#### Implementing a store

A store has four operations. The example below is a complete, if naive, implementation that keeps each
document in a dictionary — enough to show what each contract clause means:

```csharp
public sealed class MyResourceStore : IXRegistryResourceStore
{
    public ValueTask<ByteString> ReadAsync(
        string resourceKey, long offset, int count, CancellationToken ct = default)
    {
        // Argument faults throw; an unknown key is a *null* ByteString so the caller can tell
        // "no such resource" from "resource is empty".
        if (!m_documents.TryGetValue(resourceKey, out byte[]? document))
        {
            return new ValueTask<ByteString>(default(ByteString));
        }

        // Return fewer bytes than asked for at the end of the document; never throw for that.
        if (offset >= document.Length || count == 0)
        {
            return new ValueTask<ByteString>(ByteString.From([]));
        }
        int take = (int)Math.Min(count, document.Length - offset);
        return new ValueTask<ByteString>(
            ByteString.From(document.AsSpan((int)offset, take).ToArray()));
    }

    public ValueTask WriteAsync(
        string resourceKey, long offset, ByteString data, CancellationToken ct = default)
    {
        // Random access: the chunk may land anywhere. Writing past the end grows the document and
        // the gap reads back as zeros; writing at 0 does *not* truncate what follows.
        m_documents.TryGetValue(resourceKey, out byte[]? existing);
        existing ??= [];
        var merged = new byte[Math.Max(existing.Length, offset + data.Length)];
        existing.CopyTo(merged.AsSpan());
        data.Span.CopyTo(merged.AsSpan((int)offset));
        m_documents[resourceKey] = merged;
        return default;
    }

    public ValueTask<long> GetLengthAsync(string resourceKey, CancellationToken ct = default)
    {
        // -1 signals an unknown key rather than throwing.
        return new ValueTask<long>(
            m_documents.TryGetValue(resourceKey, out byte[]? d) ? d.Length : -1);
    }

    public ValueTask<bool> DeleteAsync(string resourceKey, CancellationToken ct = default)
    {
        // Deleting an absent key is a no-op, not a fault.
        return new ValueTask<bool>(m_documents.Remove(resourceKey));
    }

    private readonly Dictionary<string, byte[]> m_documents = [];
}
```

Two rules make a store substitutable:

* **Error reporting.** Argument faults throw — `ArgumentException` for a null or empty key,
  `ArgumentOutOfRangeException` for a negative offset or count. Everything a caller is expected to
  handle is a return value instead: a null `ByteString`, a `-1` length, a `false` delete. Genuine
  infrastructure failures (an unreachable share, a permission fault) should throw a
  `ServiceResultException` with an appropriate status code, which the node manager surfaces as the
  Method's result rather than faulting the server.
* **Concurrency.** Implementations must be safe for concurrent calls.

The contract is exercised by `XRegistryResourceStoreContractTests`; deriving a fixture from it is the
quickest way to validate a new implementation.

`Opc.Ua.WotCon.Server` is a worked example: `WotBlobResourceStore` implements this interface over
the `{root}/{digest}.bin` layout the WoT registry has always written, so a domain registry can adopt
the shared byte layer without an on-disk migration. The AAS registry follows the same projection
model through `AasRegistryProjection`, while adding the AAS-specific source identities and discovery
methods described in [OPC UA for Asset Administration Shell V3](Aas.md#6-aas-registry). See
[WoT Connectivity — keeping the document bytes in a shared store](WoTConnectivity.md#keeping-the-document-bytes-in-a-shared-store).

### Transport security

Registry **writes always require a `SignAndEncrypt` secure channel**. A document and its
content-derived identity are integrity-critical, so `CreateGroup`, `GetOrCreateGroup`,
`CreateResource`, `GetOrCreateResource`, `Delete`, `AddAttribute`, `RemoveAttribute`, opening a file
for writing, `Write` and `Close` are all rejected with `BadSecurityModeInsufficient` on a channel that
is merely signed or unprotected. This is not configurable.

Reads are permitted on any secure channel by default, because a registry is usually a public
catalogue. Set `RequireEncryptionForReads` when the documents themselves are confidential:

```csharp
var options = new XRegistryServerOptions
{
    RequireEncryptionForReads = true
};
```

An in-process call carries no channel at all — the server's own bootstrap, or a test — and is always
allowed.

### Federation

`XRegistryFederationNodeManager` publishes a **proxy** for a resource hosted by another registry. The
proxy is itself a `ResourceType` instance, so a generic xRegistry client drives it through exactly the
same generated proxy as a locally hosted resource. It carries an `ExternalReference` — an
`ExpandedNodeId` whose `ServerIndex` names the remote server through the local `ServerArray`, and whose
`NamespaceUri` and `Identifier` are the remote resource node's identity — and/or a plain `ResourceUrl`,
with the content-id in `Xid`. Since the content-id is stable across registries, the same resource
federated from several endpoints keeps **one** identity and can be de-duplicated by consumers.

### Labels

`RegistryType`, `GroupType` and `ResourceType` each expose a `Labels` Object of type `AttributesType`
with `AddAttribute(Key, Value, ExpectedEpoch)` and `RemoveAttribute(Key, ExpectedEpoch)`. Labels are
published as addressable `String` Properties in the registry namespace, so a client can read them with
a plain Read. Both mutations take the owning node's epoch and advance it on success, which makes a
concurrent update visible instead of silently lost; `0` disables the check.

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
| `MaxConcurrentUploads` | 64 | `CreateResource` / file `Open` | `BadTooManyOperations` |
| `MaxResourceBytes` | 16 MiB | file `Write` | `BadRequestTooLarge` |
| `MaxRegisteredResources` | 4096 | `CreateResource` | `BadTooManyOperations` |

## Client-side usage

The client layer is built entirely on the **source-generated ObjectType proxies** — no hand-rolled
service calls. `XRegistryClient` is an **abstract** base carrying the xRegistry-level API;
`GenericXRegistryClient` is the sealed implementation for callers that only need the base model,
and a domain registry client derives from the same base:

```text
abstract XRegistryClient
   ├── sealed GenericXRegistryClient   // any registry namespace
   ├── SchemaRegistryClient  (domain)
   └── WotRegistryClient     (domain)
```

Resolving a resource from an id received on the wire is a single call. It reads through
`ReadBytesAsync`, so a document larger than the session's `MaxByteStringLength` is fetched with
range-based reads rather than failing. It returns a **null** `ByteString` — check `IsNull` — when no
fast-path node is registered, so the caller can fall back to a Browse or a registry-specific download:

```csharp
var client = new GenericXRegistryClient(session, "http://example.org/UA/MyRegistry/", telemetry);

ByteString document = await client.ResolveResourceAsync(contentId, ct: ct).ConfigureAwait(false);
if (document.IsNull)
{
    // Not registered on this server — fall back.
}
```

Registering a document drives the model's own lifecycle: the group's `CreateResource` creates the
version and opens it for writing, and the document is streamed through the `FileType` methods that
`ResourceType` inherits.

```csharp
ResourceRegistrationResult registered = await client.RegisterResourceAsync(
    groupNodeId,
    resourceId: "urn:my:resource",
    document: documentBytes,
    ct: ct).ConfigureAwait(false);

NodeId resourceNodeId = registered.ResourceNodeId;
string assignedVersionId = registered.AssignedVersionId;
```

Groups, idempotent registration and deletion are covered by the same convenience layer. Delete takes
the node's `ExpectedEpoch`, so a caller working from a stale read is rejected rather than clobbering a
concurrent change:

```csharp
// The registry root sits at a well-known identifier in the registry namespace, so there is no
// need to Browse for it.
GroupRegistrationResult group = await client
    .GetOrCreateGroupAsync(client.RegistryNodeId, "schemas", ct)
    .ConfigureAwait(false);

// Only streams the document when it actually created the version.
ResourceRegistrationResult resource = await client.GetOrRegisterResourceAsync(
    group.GroupNodeId, "urn:my:resource", documentBytes, ct: ct).ConfigureAwait(false);

if (resource.Created)
{
    // The version is new on this server.
}

await client.DeleteResourceAsync(resource.ResourceNodeId, expectedEpoch, ct).ConfigureAwait(false);
await client.DeleteGroupAsync(group.GroupNodeId, groupEpoch, ct).ConfigureAwait(false);
```

Both results are `readonly record struct`s, so they carry named members instead of positional tuple
elements and still deconstruct when that reads better:

```csharp
(NodeId nodeId, string versionId, bool created) = resource;
```

The typed proxies are also available directly, which is what a domain client builds on:
```csharp
GroupTypeClient group = client.GetGroup(groupNodeId);
(NodeId nodeId, string versionId, uint fileHandle) =
    await group.GetOrCreateResourceAsync("urn:my:resource", string.Empty, true, ct)
        .ConfigureAwait(false);

ResourceTypeClient resource = client.GetResource(nodeId);
await resource.WriteDocumentAsync(fileHandle, documentBytes, ct: ct).ConfigureAwait(false);
```

### Extending for a domain registry

A domain model subtypes the xRegistry base types — for example a schema registry declares
`SchemaFileType : ResourceType` — so the generator emits a proxy chain that mirrors the OPC UA
hierarchy (`SchemaFileTypeClient : ResourceTypeClient : FileTypeClient`). Two things follow:

* A **domain client** derives from `XRegistryClient` and inherits the whole lifecycle. Helpers
  written as extension methods over a base proxy (such as `WriteDocumentAsync` on
  `ResourceTypeClient`) are directly callable on the domain proxy, with no inheritance in the
  client layer.
* A **generic client** still drives a domain registry, because a domain instance *is* an instance
  of the base type. Discovery must be subtype-aware (browse with `includeSubtypes`, test with
  `IsTypeOf`) rather than comparing TypeDefinition NodeIds for equality.

#### Registry roots that are not well-known

`XRegistryWellKnown.RegistryObject` (`65000`) is *provisional*, and a domain registry generally
declares its own root instead — the WoT Connectivity registry publishes `WoTRegistry` as a
`HasComponent` child of the `Server` object, which a client discovers by Browse. Pass the resolved
NodeId to the constructor so the root is a construction-time input that cannot subsequently drift:

```csharp
public sealed class WotRegistryClient : XRegistryClient
{
    public WotRegistryClient(ISession session, NodeId registryObjectId, ITelemetryContext telemetry)
        : base(session, Namespaces.WotCon, registryObjectId, telemetry)
    {
        Proxy = new WoTRegistryTypeClient(session, registryObjectId, telemetry);
    }
}
```

`RegistryNodeId` then reports that root, and every inherited lifecycle method targets it. Passing a
null NodeId selects the well-known root, so the overload stays equivalent to the namespace-only
constructor. `GenericXRegistryClient` exposes the same overload, so a caller can drive a domain
registry without deriving a client at all:

```csharp
var registry = new GenericXRegistryClient(
    session, Namespaces.WotCon, wotRegistryNodeId, telemetry);
```

`Opc.Ua.WotCon.Client` is the worked example: `WotRegistryClient` derives from `XRegistryClient`,
inherits `Session`, `RegistryNodeId` and the group/resource lifecycle, and adds only WoT-specific
surface — the `ForServerAsync` Browse resolution, the reserved Thing Description / Thing Model
groups, a typed `Refresh` result and a dependency-ordered bulk load. `Opc.Ua.Aas.Client` follows
the same pattern: `AasRegistryClient` derives from `XRegistryClient`, resolves the well-known
`AASRegistry` object and adds `LookupShellsByAssetLink`, `GetSubmodel` and typed AAS group/resource
clients.

## Well-known identifiers

`XRegistryWellKnown` carries the base companion namespace URI and the provisional NodeIds a generic
registry materializes. A concrete registry reuses the same numeric identifiers inside **its own**
namespace, so the client-side lookup logic is shared.

> **Note:** the NodeIds are *provisional*. Final identifiers are assigned by the OPC Foundation.

| Member | Value | Meaning |
| --- | --- | --- |
| `XRegistryNamespaceUri` | `http://opcfoundation.org/UA/xRegistry/` | Abstract base companion namespace |
| `RegistryObject` | 65000 | The registry root, a `RegistryType` instance |
| `FederationProxyObject` | 66001 | Federated resource proxy, a `ResourceType` instance |
| `FirstDynamicInstance` | 100000 | Start of the range allocated to runtime groups and resources |

Everything else — the ObjectTypes, their Methods and their Variables — comes from the compiled model
via the generated `ObjectTypeIds`, `MethodIds` and `VariableIds` classes. The model occupies
63000–63999, so the instance identifiers above can never collide with it.

## Related documentation

* [Source generation](../tools/Opc.Ua.SourceGeneration/readme.md) — how the companion model and its typed proxies are compiled into the assembly.
* [Node management](NodeManagement.md) — the node manager model the three managers build on.
* [PubSub (Part 14)](PubSub.md) — the PubSub Schema Registry specialization of this model.

