# xRegistry — abstract registry base model

The **xRegistry** libraries implement the generic, registry-agnostic *abstract registry base model*
for OPC UA. They provide the substrate a concrete registry builds on: **structural xRegistry
identity**, an **Opaque-NodeId content fast path** that resolves a document in a single `Read`, a
**model-driven registration lifecycle** with auto-bootstrap, and a **federation** model for resources
hosted by another registry, and optional native OPC UA events for registry mutations.

The libraries are deliberately domain-neutral: they know nothing about what a *resource* contains.
A concrete registry supplies its own companion namespace and a fingerprinting strategy, and reuses
everything else. The PubSub Schema Registry and WoT Connectivity registry are concrete
specializations: PubSub resources are schema documents, while WoT resources are Thing Description /
Thing Model documents.

## Contents

- [Packages](#packages)
- [Core concepts](#core-concepts)
  - [Structural identity and content lookup](#structural-identity-and-content-lookup)
  - [Opaque-NodeId fast path](#opaque-nodeid-fast-path)
  - [Registration lifecycle and auto-bootstrap](#registration-lifecycle-and-auto-bootstrap)
  - [File open modes](#file-open-modes)
  - [Resource storage](#resource-storage)
  - [Transport security](#transport-security)
  - [Federation](#federation)
  - [Labels](#labels)
  - [Native xRegistry events](#native-xregistry-events)
- [Server-side usage](#server-side-usage)
  - [Async lifecycle](#async-lifecycle)
  - [Resource-exhaustion bounds](#resource-exhaustion-bounds)
- [Client-side usage](#client-side-usage)
  - [Extending for a domain registry](#extending-for-a-domain-registry)
- [Well-known identifiers](#well-known-identifiers)
- [Related documentation](#related-documentation)

The [xRegistry OPC UA / HTTP bridge](#opc-ua-http-bridge) builds on this substrate
to provide write-through gateways and durable bidirectional reconciliation.
Its experimental transaction extension is opt-in; existing native registration
and clean-FileType-Close behavior remains unchanged.

## Packages

| Package | Depends on | Contains |
| --- | --- | --- |
| `OPCFoundation.NetStandard.Opc.Ua.XRegistry` | `Opc.Ua.Core` | The source-generated base companion model (types, NodeIds, `NodeState`s and `*TypeClient` proxies), `XRegistryWellKnown`, `IResourceContentIdProvider` |
| `OPCFoundation.NetStandard.Opc.Ua.XRegistry.Client` | `Opc.Ua.XRegistry`, `Opc.Ua.Client` | `XRegistryClient` — fast-path resolve and lifecycle registration |
| `OPCFoundation.NetStandard.Opc.Ua.XRegistry.Server` | `Opc.Ua.XRegistry`, `Opc.Ua.Server` | The three node managers and `XRegistryServerOptions` |

`Opc.Ua.XRegistry` has no dependency on either SDK, so a codec or a shared contracts assembly can
reference the identity abstraction without pulling in the client or the server.

## Core concepts

### Structural identity and content lookup

`Xid` is always the stable structural path relative to the registry root: `/` for the registry,
`/groups/{group}` for a group, `/groups/{group}/resources/{resource}` for a Resource, and that
Resource path plus `/versions/{version}` for a materialized Version file. Replacing document bytes
never changes `Xid`, `ResourceId`, `VersionId`, AddressSpace placement, or event `Subject`.

Every registry may separately supply an `IResourceContentIdProvider` that maps a document plus its
format to an opaque content key:

```csharp
public interface IResourceContentIdProvider
{
    ByteString ComputeContentId(string format, ReadOnlySpan<byte> document);

    string? GetAlgorithm(string format);
}
```

`GetAlgorithm` names the (canonicalization, hash) pair used for a format and returns `null` for
formats the registry does not handle. This key is an implementation fast path, not entity identity.
It is tracked per Version and reference-counted, so equal bytes in different Versions share one
Opaque node while replacing or deleting one Version releases only that Version's reference.

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
   A non-empty `VersionId` is preserved exactly and must be 1-128 characters: the first
   character is an ASCII letter, digit, or `_`, and subsequent characters may additionally use
   `-`, `.`, `~`, `:`, or `@`. Lookup is case-sensitive, while sibling Version ids must be unique
   without regard to case. A Resource may have one contentless pending Version while an upload is
   open. Allocating it never evicts committed content; retention is applied atomically when the
   upload closes, and a close that cannot preserve active/default/desired Versions is rejected.
   An empty Version id reuses that pending Version after an abort or restart; requesting a
   different explicit Version while it remains pending is rejected. Existing manifests retain
   compatibility with longer, already-normalized legacy Version ids.
3. Because `ResourceType` **is a `FileType`**, the document is streamed with the standard
   `Write`/`Read` file Methods against the handle — there is no registry-specific transfer.
4. **`Close`** compares the staged bytes with the committed bytes captured immediately before
   `Open`. Only a successful Close with an accepted, byte-different write commits the document,
   increments that Version's `Epoch`, updates its `ModifiedAt`, and publishes or updates the
   reference-counted Opaque fast-path node. Clean, aborted, rejected, empty, and byte-identical
   closes perform no store rewrite and change no metadata.
5. **`Delete(ExpectedEpoch)`** on a resource or a group removes it. The epoch is an
   optimistic-concurrency check: a caller holding a stale epoch is rejected with
   `Bad_InvalidState` rather than deleting a newer version. Passing `0` disables the check, which
   is how a caller deliberately forces the operation without having read the entity first.

Registration commits Resource and Version structural identity before the create Method returns.
When `RequestFileOpen` is true, a later dirty `Close` is a separate mutation; it updates the Version
but does not repeat the create operation or its events.

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
is rejected with `Bad_InvalidArgument`. Each Version permits one writer; a second write open fails
with `Bad_NotWritable`, and a read open while that writer is active fails with `Bad_NotReadable`.
A handle is valid only on the resource *and* the session that opened it, and a session's handles are
released when it closes. `EraseExisting` stages an empty buffer but does not mutate the committed
file until a dirty `Close`.

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

Use the in-box `InMemoryResourceStore` or `FileSystemResourceStore` unless the
application requires another persistence backend. A custom `IXRegistryResourceStore`
must implement four operations:

| Operation | Contract |
| --- | --- |
| `ReadAsync` | Read from the requested offset, returning fewer bytes at the end. Return a null `ByteString` for an unknown key, and an empty non-null value for an empty read of an existing resource. |
| `WriteAsync` | Replace the addressed byte range without truncating the remainder. Extend the document and zero-fill any gap when writing beyond its end. |
| `GetLengthAsync` | Return the byte length, or `-1` for an unknown key. |
| `DeleteAsync` | Return whether a resource was deleted; an absent key returns `false`. |

For implementation examples, see
[`InMemoryResourceStore`](../src/Opc.Ua.XRegistry.Server/InMemoryResourceStore.cs)
and [`FileSystemResourceStore`](../src/Opc.Ua.XRegistry.Server/FileSystemResourceStore.cs).
Concurrent writes must preserve non-overlapping updates. A concurrent dictionary
alone does not make a read-modify-write sequence atomic.

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
the shared byte layer without an on-disk migration. See
[WoT Connectivity — keeping the document bytes in a shared store](WoTConnectivity.md#keeping-the-document-bytes-in-a-shared-store).

### Transport security

Registry **writes always require a `SignAndEncrypt` secure channel**. A document and its
content lookup are integrity-critical, so `CreateGroup`, `GetOrCreateGroup`,
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
`NamespaceUri` and `Identifier` locate the remote resource node — and/or a plain `ResourceUrl`.
`ResourceId`, `VersionId`, and `Xid` retain the remote structural xRegistry identity. A content
digest may still be used by the remote NodeId or a local cache, but it never replaces `Xid`.

### Labels

`RegistryType`, `GroupType` and each Version `ResourceType` expose a `Labels` Object of type
`AttributesType`. Version label Methods use that Version's `Epoch`, increment it, update Version
`ModifiedAt`, and emit `VersionUpdated` when events are enabled. Resource Meta is distinct:
`MetaEpoch`, `MetaLabels`, `MetaCreatedAt`, and `MetaModifiedAt` are synchronized across the
Resource's Version files. Meta label Methods use `MetaEpoch`, update only Resource Meta, and emit
`ResourceUpdated`. Adding or removing a Version advances Resource Meta; modifying Version bytes or
Version labels does not.

### Native xRegistry events

The xRegistry 0.5.0 model includes `XRegistryEventType` and the 19 concrete registry, model,
capabilities, group, resource and version event types. The model source generator emits the typed
`*EventState`, `*EventTypeRecord` and `EventFilters.Build(...)` surfaces directly from the NodeSet.
Applications subscribe with the standard OPC UA event APIs; there is no separate xRegistry
subscription protocol and no CloudEvents document is serialized into a Variable or Method.

Generic event publication is disabled by default. Enable it only with a stable absolute source URL
and the concrete registry's canonical collection/document attribute names:

```csharp
var options = new XRegistryServerOptions
{
    EventsEnabled = true,
    EventSourceUrl = "https://registry.example.com",
    GroupsAttributeName = "groups",
    ResourcesAttributeName = "resources",
    ResourceDocumentAttributeName = "schema"
};
```

Incomplete enabled configuration throws during node-manager construction. `SourceUrl` is the
configured registry URL, independently of the OPC UA endpoint and event `SourceNode`; `Subject` is
the changed entity xid. The generic stack leaves `CorrelationId` absent because its Method responses
do not return a corresponding correlation value.

`SourceNode` identifies the native AddressSpace source rather than the registry URL. A version event
uses that version's `ResourceType` file. A resource event uses the committed default-version file,
including the new default after a switch; consequently `ResourceCreated` and the first
`VersionCreated` share the first/default file. Registry, model, modelsource, and capabilities events
always use the registry root. A deleted event retains the removed source's former `SourceNode` and
`SourceName`, but is reported through the nearest surviving notifier so subscriptions continue to
receive it.

One successful Method mutation, dirty file `Close`, or post-startup projection reconciliation is
coalesced into one logical event batch. Events in a batch share one `Time`; duplicate
type-and-subject changes are merged; `Changed` names are ordinally sorted and de-duplicated; and
deleted/created/updated precedence is applied per subject. Initial projection is a silent baseline,
and failed, stale, idempotent, clean-close and no-op interactions emit nothing. Recursive deletion
reports version leaves before resources, groups and their surviving parent update.

The registration manager registers its registry root with the node manager's root-notifier API.
Consequently a MonitoredItem on `ObjectIds.Server` receives descendant group, Resource, and Version
events, while monitoring the registry or a narrower notifier remains supported.

Enabling XREG-Events commits the implementation to every event marked MUST by the xRegistry event
specification for each supported mutation mechanism. `RegistryCreated`, `RegistryDeleted`, and
descendant events caused by deleting an entire registry are recommendations because registry
creation/deletion is outside the core mutation API. Descendant events produced by recursive group or
resource deletion are required and are never suppressed.

The stack publishes native OPC UA events only. If an application separately serializes one of these
events as CloudEvents JSON, it maps the opaque `BaseEventType.EventId` bytes to the CloudEvents `id`
using standard Base64 encoding.

For projected domain registries, existing `IXRegistryProjectionStrategy` implementations and the
original six-parameter `XRegistryProjectionContext` constructor remain supported when events are
disabled. Event-enabled strategies additionally implement
`IXRegistryProjectionGenerationProvider`, which captures projection data and event metadata from one
immutable generation. `IXRegistryVersionedProjectionStrategy` is additive and lets a domain honor
explicit/server-assigned Version ids, materialize stable per-Version NodeIds, and separate Version
labels from Resource Meta without breaking existing strategies.

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

All three managers derive from `AsyncCustomNodeManager`. Register them through the server's
`AddNodeManager(IAsyncNodeManagerFactory)` overload; the factory's `CreateAsync` returns an
`IAsyncNodeManager`. Direct construction and `AddXRegistryServer` options/provider registrations
are supported. Startup, node publication/removal, session cleanup, and event
delivery are awaited rather than routed through a synchronous node-manager wrapper.

The registry's companion model is **compiled into the assembly** by the OPC UA model source
generator: `Opc.Ua.XRegistry.NodeSet2.xml` is a generator input (`AdditionalFiles`), so the
ObjectTypes, Methods, Variables, NodeId constants, `NodeState` classes and typed
[ObjectType proxies](../tools/Opc.Ua.SourceGeneration/readme.md) are emitted at build time. No
NodeSet2 XML is parsed at runtime — each node manager simply returns the generated model from
`LoadPredefinedNodesAsync`:

```csharp
protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
    ISystemContext context,
    CancellationToken cancellationToken = default)
{
    cancellationToken.ThrowIfCancellationRequested();
    return new ValueTask<NodeStateCollection>(
        new NodeStateCollection().AddOpcUaXRegistry(context));
}
```

A concrete registry composes its own companion model on top of the base model in dependency
order, declaring `RequiredModel` on the xRegistry namespace in its NodeSet.

### Async lifecycle

Custom subclasses override the async lifecycle hooks and await their base implementations.
Use `AddPredefinedNodeAsync` and `DeleteNodeAsync` for runtime graph mutations. In-memory lookups
such as `Find` and `FindPredefinedNode<T>` are synchronous. Hosting and custom callbacks use the
async interface without blocking on tasks.

Cancellation is observed before a registration mutation starts and during document reads. Once
a dirty Close consumes its handle, or a deletion changes the graph, the manager finishes the
commit/cleanup even if the caller subsequently cancels. Await `DeleteAddressSpaceAsync` before
disposal to drain active operations and release handles without disposing the injected store.
Failed or cancelled startup removes its partially indexed nodes and does not publish incomplete
external references. Await successful startup before invoking registration Methods; a failed
startup can be retried on the same manager.
Deletion cleanup attempts every removed subtree and stored document before reporting callback or
storage failures. Removals that could not complete are retried before an explicit deletion or during
teardown, never midway through an unrelated file operation. Retiring resources reject new file
handles and label mutations.
See [Async server support](AsyncServerSupport.md) for the async base's hosting and notification
contracts.

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
groups, a typed `Refresh` result and a dependency-ordered bulk load.

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

## OPC UA / HTTP bridge

The xRegistry connector hosts a protocol gateway or reconciles two independently
writable registries. Gateways are **write-through**: a successful response means
the authoritative backend completed the operation. Synchronization is a separate,
explicitly selected mode; it is not used to acknowledge gateway writes early.

This is experimental support for **xRegistry 1.0-rc4** and the OPC UA working
drafts, not a claim of certification or final-standard conformance.

### Packages and construction

| Package | Responsibility |
| --- | --- |
| `Opc.Ua.XRegistry` | `Protocol.IXRegistryEndpoint`, lossless addressing, caller contexts and bounded envelopes |
| `Opc.Ua.XRegistry.Http` | HTTP binding client and modern .NET endpoint hosting |
| `Opc.Ua.XRegistry.Server` | Optional atomic generation provider and storage interface |
| `Opc.Ua.XRegistry.Bridge` | Native transport, experimental transaction extension, projection and reconciliation |
| `Opc.Ua.XRegistry.Connector` | Thin .NET 10 command-line host, installed as `opcua-xregistry` |

Library packages retain the stack's target frameworks. ASP.NET hosting requires
modern .NET; the connector requires .NET 10. Direct constructors and dependency
injection use the same implementation. Native models are source-generated;
registry JSON is not OPC UA Part 6 JSON.

On net8.0 and later, the HTTP binding consumes the published
`XRegistry` NuGet package (`0.1.0-alpha`) for Core model compilation and
model-driven Document metadata header encoding/decoding. The bridge still owns
its caller-contextual HTTP transport, OPC UA-specific transaction boundary,
native projection and synchronization. The existing compatibility codec remains
active for historical or experimental model documents that are outside the
published Core model grammar. This is a staged package integration, not a
replacement of the generic server engine or evidence of conformance.

### Choose a mode

Build the tool from the repository:

```powershell
$env:CustomTestTarget = "net10.0"
dotnet build tools\Opc.Ua.XRegistry.Connector -c Release
dotnet run --project tools\Opc.Ua.XRegistry.Connector -c Release --no-build -- --help
```

For a native executable use `dotnet publish tools\Opc.Ua.XRegistry.Connector
-c Release -r win-x64 -p:XRegistryPublishAot=true`. The tool-scoped property avoids
applying `PublishAot` to the .NET Standard source-generator build dependencies.

Replace the example root NodeId with the actual registry instance advertised by
the server. Namespace-URI form avoids depending on a server's current namespace
indexes. Provision OPC UA certificate trust before connecting.

```powershell
# HTTP clients access the authoritative OPC UA registry.
opcua-xregistry http-gateway `
  --opcua opc.tcp://localhost:4840 `
  --registry-node "nsu=http://opcfoundation.org/UA/xRegistry/;s=Registry" `
  --listen https://localhost:8443 --public-root https://localhost:8443/registry `
  --config connector.json --profile production

# OPC UA clients access the authoritative HTTP registry.
opcua-xregistry opcua-gateway `
  --http-root https://registry.example/catalog `
  --listen opc.tcp://localhost:4841/xregistry `
  --config connector.json --profile production

# Reconcile two independently writable registries.
opcua-xregistry sync `
  --opcua opc.tcp://localhost:4840 `
  --registry-node "nsu=http://opcfoundation.org/UA/xRegistry/;s=Registry" `
  --http-root https://registry.example/catalog `
  --state D:\RegistryState --job production `
  --conflict-policy manual --deletes on `
  --config connector.json --profile production
```

`--once` runs one reconciliation pass. `--dry-run` computes a plan without
applying registry changes. `--deletes off` disables background deletion
propagation, but does not disable explicit authorized gateway DELETE requests.
There is deliberately no unguarded-delete switch.

Each job uses a subdirectory below `--state`. Offline `conflicts --state ... --job ...`
lists active conflicts; `resolve --state ... --job ... --conflict ID
--resolution prefer-opcua|prefer-http` records a decision for revalidation on the
next synchronization pass. It does not force an immediate remote write.

`inspect` reports the effective model and backend guarantees without changing
registry entities. `--model` supplies an explicit model for native deployments
that do not expose a Model document; it does not grant missing backend guarantees.

The default projected bridge root is
`nsu=urn:opcfoundation.org:xregistry:bridge;s=XRegistryBridge`.

### Atomicity and capability negotiation

The base native binding and HTTP binding are not semantically interchangeable:

| Operation | HTTP requirement | Base native limitation |
| --- | --- | --- |
| Conditional mutation | Missing/null epoch skips checking; **zero is a real guard** | `ExpectedEpoch=0` means unconditional |
| Epoch width | Unsigned integer; never silently narrow | Companion properties commonly use UInt32 |
| Identical update | Every successful update advances epoch | Clean/identical FileType Close does not touch |
| Nested update | Any error rejects the entire request | Several Calls or Writes are not a transaction |

The optional native transaction extension preserves the complete request,
preconditions, response parameters and document bytes. It is in a **separate
experimental namespace**, not a modification to the companion specification.
Staged requests do not mutate registry entities. Publication uses a qualified
atomic provider, and replay/outcome support is advertised only when backed by
appropriate storage.

HTTP mutations additionally require real **prepare/commit** support. The gateway
first obtains an immutable response preview, fully encodes its actual HTTP body
and headers, and only then commits the prepared operation. Disposal before commit
aborts it. Any intervening registry mutation invalidates the candidate instead of
silently rebasing it. Checking only a transport envelope's serialization would not
protect against HTTP header, URL or response-body errors.

`IXRegistryPreparedEndpoint` and `IXRegistryPreparedOperation` expose this optional
seam without exposing a lock. The native extension carries prepared operations
through bounded, session-owned leases. A plain HTTP backend does not acquire
remote preparation merely by being wrapped, and unsupported gateway writes fail
before mutation.

An unextended server receives a limited profile. Requests requiring guarantees it
cannot provide are rejected **before mutation**. Read/check/write sequences,
compensating writes and a fabricated non-atomic capability are not substitutes
for HTTP atomicity. A protocol version label alone is not proof of runtime
capabilities or hierarchy.

OPC UA logical Resources remain distinct from exact Versions. Resource deletion
uses **MetaEpoch**, while Version deletion uses that Version's epoch. Existing
native clean-Close semantics are retained rather than changed to HTTP touch
semantics.

Entity timestamps follow the pinned core rules. An omitted `createdat` retains
its existing value even on PUT; `null` resets it to the request time, and an
explicit timestamp replaces it. An absent, null or unchanged `modifiedat` uses
the request time; a different supplied value is retained. Adding or removing a
child updates its immediate parent's `modifiedat`, but editing a descendant does
not. Timestamps are not incarnation identifiers.

#### Generation-consistent inventory

Qualified endpoints advertise `SupportsGenerationGuards` and return an opaque
`Generation` with inspection and successful reads. An `ExpectedGeneration`
request compares that token against the same authoritative snapshot used for
the operation. A mismatch returns `409 concurrent_change` without applying the
request. Prepared mutations retain their separate global commit guard as well.

Native projection and synchronization inventories share a read-scope helper
that attaches the negotiated guard to every read, including continuation pages
and document reads. It verifies returned generation tokens and rechecks
inspection before accepting the scan. A descendant-only edit invalidates the
scan even when the Registry epoch and collection membership remain unchanged.
There is no automatic retry or hidden read-snapshot lease.

The transactional provider's tokens are local to its current activation and
loaded state. They expire after a publication, an observed external store
change or restart. They are not persistent cursor IDs, entity epochs or
cross-registry ordering values. A mutation response does not promise a reusable
read token; inspect again after commit.

These optional fields use codec format 1 and the existing native method
signatures. Ordinary HTTP and base-native endpoints reject explicit generation
guards rather than ignoring them. Their normal unguarded read profiles remain
available with the existing completeness checks, but are not advertised as
generation-consistent snapshots.

Qualified local providers also expose `IXRegistryPreparedSnapshot`: immutable
candidate reads tied to the preparing identity and lifetime. The native manager
prepares its actual projection and generated event batch before committing that
candidate. Reads, browsing and browse-path translation reject a transition in
progress rather than returning a mixed or uncommitted view. Local address-space
observer callbacks are staged until the authoritative commit; a rejected
candidate restores the previous projection and retains existing pinned handles.
Replayed outcomes have no candidate and cannot repeat projection or events.

#### Version incarnation guards

Writable native FileType handles pin a Version incarnation, not just its epoch
or timestamps. The shared request's `ExpectedVersionIncarnation`, response's
`VersionIncarnation`, and description's `SupportsVersionIncarnationGuards` are
optional **codec-format-1** fields. They are experimental endpoint guarantees,
not new HTTP headers or base companion attributes.

The transactional provider persists a random incarnation when creating a
Version and validates supplied guards atomically with the operation. Legacy
Versions without a stored incarnation receive guards bound to the exact loaded
state. Reads do not change durable storage; the next successful mutation
persists the guards. Until then, reopening the endpoint or loading changed
legacy state invalidates them. Writable native
`Open` requires both advertised support and a returned incarnation. `Close`
passes that pinned guard with the staged write, without a separate `createdat`
read. A replacement with identical epochs and timestamps must not be overwritten
by the old handle, whether replacement happens before `Close` or across its
dispatch boundaries.

Ordinary HTTP and base-native endpoints reject guarded requests before dispatch;
wrapping them does not grant incarnation fencing. Read-only file access can
remain available when writable `Open` cannot be qualified. This guard is independent of timestamps. Full reconnect/disposal and every
cross-mode lifecycle interaction remain separate acceptance gates; see
`XREG-NATIVE-011` through `XREG-NATIVE-016` in the ledger.

#### Prepared transfer cleanup and deadlines

Native transfers are session-owned and single-use. File publication/open failure
releases the transfer's local reservations. If an upload is aborted or expires
while its provider prepares a response, the orphaned preview and returned lease
are released rather than left consuming quota. Commit rechecks ownership after
asynchronous authorization, so an intervening abort prevents dispatch.
Once attached, an unread preview belongs to its upload; aborting or expiring the
upload revokes both. Consuming and releasing the preview separately remains valid.

Provider Prepare/Commit and disposal do not run while the shared transfer table
is synchronized. A slow provider therefore does not stall unrelated transfers.
Session-close, expiry and shutdown revoke the entire affected set before
starting cleanup; a failing disposal cannot prevent retirement of its siblings.

`XRegistryBridgeNativeOptions.PreparedOperationTimeout` bounds each provider
Prepare and Commit phase (default 30 seconds). `CleanupTimeout` bounds waiting
for detached transfer and lease cleanup (default 5 seconds). Both use the
injected `TimeProvider` and the same deadline module as synchronization.
These are not whole-gateway network or authorization deadlines.
The executable accepts the same durations at
`NativeGateway:PreparedOperationTimeout` and `NativeGateway:CleanupTimeout`
in configuration (for example `"00:00:30"` and `"00:00:05"`), or through the
equivalent `XREGISTRY_NativeGateway__...` environment-variable names.
Invalid durations fail before the host is built.

A late Prepare result is aborted. A timed-out Commit returns `BadTimeout` and
remains indeterminate until a qualified journal or readback establishes its
outcome; the commit is never retried. Its lease remains owned by the in-flight
work until that work completes. Cleanup failures are logged. A cleanup failure
after a known successful commit does not turn that authoritative success into
a fictitious rejection. Uncooperative work may outlive the caller's deadline,
but is observed and retains ownership until safe cleanup.

#### Optional transactional provider

Applications upgrading a native server can inject their own `IXRegistryEndpoint`
or use `XRegistryTransactionalEndpoint`. All writable native surfaces in that
deployment must use the same provider.

```csharp
using Opc.Ua.XRegistry.Protocol;
using Opc.Ua.XRegistry.Server.Protocol;

services.AddSingleton<IXRegistryTransactionStore>(
    _ => new FileXRegistryTransactionStore(registryStateDirectory));
services.AddXRegistryTransactions(new XRegistryTransactionalOptions
{
    RegistryId = "plant-registry",
    Model = modelDocument.RootElement,
    PublicRoot = new Uri("https://registry.example/catalog")
});
```

The built-in provider atomically publishes metadata, document bytes and operation
outcomes in a generation. It prepares the response before publication. The
process-local store is suitable for tests and transient hosting, but does not
advertise durable replay. File storage claims one local writer and uses staged
writes, file flushes, directory durability barriers and atomic replacement.
It is not an active/active or eventually-consistent shared-storage protocol.
An initialization marker prevents a missing previously committed data file from
being interpreted as a fresh empty registry. Restore data and its marker together;
do not remove recovery artifacts to bypass a storage failure.
`InitialMetadata` supplies required root attributes that have no model default.
Root defaults are present before the first write; persisted roots take precedence
over initialization options. Prepared candidates have separate count and aggregate
byte quotas, released on commit or abort.

The provider supports the full standard-model overlay separately from the
original `modelsource`, conditional `ifvalues`, typed reference targets,
strict/extended object names, local `ximportresources`, Group constraints and
`matchversions`. Model and dependent entity changes can share one atomic root
request. Existing data is validated against the resulting model before commit.

`$include` and `$includes` use an injected `IXRegistryModelDocumentResolver`;
the supplied catalog resolver is offline and allow-listed. Resolution is bounded
by depth, document count and UTF-8 size, and is persisted rather than repeated
on ordinary reads. Relative references use `ModelSourceUri`. No registry
credentials are forwarded to document URLs.

Version ordering supports manual, creation-time, modification-time and SemVer 2.0
precedence, with sticky defaults and retention. Cross-reference Resources retain
their imported model-type identity, expose the target's Versions at the source
path, and remain read-only until explicitly converted back into a normal
Resource. Dangling or transitive references do not manufacture Versions or epochs.
External document URIs are retained and returned as HTTP 303 redirects without
fetching content; native metadata preserves the URI instead of returning an empty file.

`IXRegistryDocumentValidator` supplies domain-specific format/compatibility checks.
The default validator advertises **JSON/1.0** and **XML/1.0 syntax** and the explicit
`identical` document compatibility mode. It does not claim JSON Schema, XSD,
Avro or Protobuf validation. Unsupported checks remain explicitly unvalidated
with a reason, or reject under `strictvalidation`; a performed check that fails
always rejects the whole request.

Filters use typed dot paths, repeated-parameter OR and comma-separated AND,
wildcards and exact-width numeric comparisons. Sorts support scalar paths,
including Resource Meta without forcing it into the response. `ignore`,
`setdefaultversionid`, discovery, inline and document/binary views retain their
binding-specific semantics. Top-level collection pagination uses authenticated
cursors bound to the caller, path, view, parameters, generation and expiration.
Pagination links preserve the aggregate `count`; HTTP `Expires` survives the
shared/native response envelope. Inlined collections are never partially paginated.
`doc` selects metadata without automatically including document bytes, omits
duplicate default-Version fields, and uses JSON pointers only for included targets.
`collections` does implicitly inline descendant collections. Unknown extension flags are
not an advertisement of implemented behavior.

HTTP validators and range service are not required by the pinned binding.
The bridge does not invent ETags; unsupported `If-*` preconditions reject rather
than becoming unconditional writes. A Range request can receive the complete
200 representation, not a fabricated partial response. Incoming gzip/deflate
payloads are decoded within the configured uncompressed limit. Pagination
`count` and `Expires` are preserved; unrelated extension Link parameters are not
advertised. Persistent short links are opt-in and use the same guarded
operation path, not redirects that replay writes.

#### Persistent short links

Set `XRegistryTransactionalOptions.ShortLinksEnabled` and explicitly initialize
the catalog before exposing the provider:

```csharp
var endpoint = new XRegistryTransactionalEndpoint(new XRegistryTransactionalOptions
{
    RegistryId = "plant-registry",
    Model = modelDocument.RootElement,
    PublicRoot = new Uri("https://registry.example/catalog"),
    ShortLinksEnabled = true,
    ShortLinkPrefix = "/_s",
    MaxShortLinks = 8192
}, transactionStore);
await endpoint.InitializeShortLinksAsync(trustedOperator, cancellationToken);
```

`IXRegistryShortLinkMaintenance` is also available through `AddXRegistryTransactions`.
Initialization is explicit, idempotent and requires write authorization with no
outstanding preparation. GET never persists a migration. Initialization upgrades
the **transactional provider** state from format 1 to format 2; old readers must
not open this format. This is separate from the synchronization-state version.

Aliases are allocated and retired in the authoritative transaction. Their
monotonic IDs are never reused. Restart, backup/recovery and disable/re-enable
preserve live identities; disabling suppresses `shortself` serialization but
keeps existing routes. The stored public root and alias prefix cannot silently
change. Logical Resources, Meta and exact Versions have different aliases;
switching the default Version does not retarget the logical Resource identity.
Referenced Version aliases are retired when their target incarnation changes.

The optional `IXRegistryAddressResolver` resolves the canonical model path under
the caller lease before HTTP body decoding. `XRegistryRequest.AddressPath` retains
the presented alias for authoritative revalidation and replay digests. Both the
original and canonical paths are authorized. `$details`, raw bodies, metadata,
flags and document views retain their canonical semantics; writes are dispatched
once, not redirected. Alias resolution is not a substitute for epoch,
incarnation or generation guards.
Explicit resolver rejections retain their status. Malformed upstream alias
responses and transport failures are backend errors (HTTP 502), not caller
syntax errors; private upstream diagnostics are not returned in the body.

For a **known HTTP deployment** whose aliases are immutable and never reused,
`XRegistryHttpOptions.ShortLinkPrefix` (or
`Profiles.<profile>.Http.ShortLinkPrefix` in the connector) enables model-aware
alias resolution through `doc` reads. This is an explicit deployment
qualification: the standard alone permits alias reuse after deletion. Unknown
HTTP alias schemes are not guessed and redirects are not followed. Ordinary
HTTP still does not acquire remote preparation or replay support.

#### Model-driven native attributes

`XRegistryBridgeNativeOptions.AttributeMappings` binds literal logical attribute
paths to namespace-URI-qualified native Property paths. `ModelPath` contains
collection types without instance IDs; `Scope` distinguishes Registry, Group,
logical Resource, Meta and exact Version.

```csharp
var mapping = new XRegistryNativeAttributeMapping(
    "/schemagroups",
    XRegistryNativeAttributeScope.Group,
    ["cycles"],
    [new("urn:plant:properties", "Diagnostics"), new("urn:plant:properties", "Cycles")])
{
    NativeType = BuiltInType.Int32,
    Writable = true
};
var options = new XRegistryBridgeNativeOptions { AttributeMappings = [mapping] };
```

Typed mappings preserve supported scalar/array types, timestamps, literal strings,
order and exact numeric bounds. Values that would round, overflow or change rank
are rejected. `CanonicalString` uses model-canonical scalars or JSON for
objects/maps/arrays; it does **not** apply HTTP header escaping or interpret a
string-valued `"null"` as deletion. Nested object/map leaves can also map to
separate typed Properties. Active conditional definitions are resolved from
discriminators rather than profile order; inactive properties report no data.
Actual native discriminator values take precedence over model defaults before
dependent fields are validated. Ordinary label decoding excludes explicitly
mapped keys, so an optional mapped String with `BadNoData` stays absent.

`StructureType` and its namespace-URI `StructureTypeId` opt into an already
registered native activator implementing `IStructure`. Its field names must
match the logical object model, and field values must use supported scalar or
array types. Unknown fields, unions, incompatible nested structures and
ambiguous optional-null representations are rejected, not defaulted or dropped.
Register such types in the stack before use; no reflection-based construction
or automatic type downloading is added. These activators are configured in code,
not by loading an assembly name from connector JSON.

Labels remain strings unless an explicit profile assigns a particular native
key to a different logical attribute. Duplicate logical/native paths and
label/attribute collisions are rejected. Qualified projected writes use the
existing preparation and authorization path; a mapping cannot grant HTTP
atomicity to an unextended server. Conversion and the mapped-property quota are
checked before publication. `MaxMappedProperties` bounds a complete projection.
Leaf mappings must cover the complete present compound value; adding an unmapped
member or replacing a container with an unrepresentable empty value rejects
before publication. Base-native read profiles must cover every declared member
of an object, including conditional members. Open-ended maps and wildcard
objects need a whole-value mapping for a faithful base read; a finite selection
of keys does not establish that other keys are absent. Use a whole canonical or
registered mapping when empty/null container presence must be distinguished.
Resource Meta uses its own `metaattributes`, not the default Version's rules.

The connector accepts `NativeGateway:AttributeMappings` with `ModelPath`, `Scope`,
`AttributePath`, `BrowsePath` entries (`NamespaceUri`, `Name`), `Encoding`,
`NativeType` and `Writable`. Profiles and bounds are validated before hosting.
`BasePropertyNamespaceUris` explicitly supports known domain layouts that place
inherited properties in their own namespace; ambiguous matches still fail.
The existing WoT projection is supported this way without changing its domain
implementation. Where that projection lacks root Epoch/SpecVersion, exact mapped
Versions remain readable but a complete root representation and HTTP mutations
remain unsupported rather than receiving fabricated metadata.

#### Content storage and acknowledged maintenance

Register `IXRegistryDocumentStore` or set `DocumentStore` to opt into immutable
SHA-256 blob storage. `FileXRegistryDocumentStore` streams bounded content,
verifies lengths and hashes, and publishes blobs before a metadata-generation
CAS. Repeated documents are deduplicated. Metadata snapshots retain references,
and document reads materialize only the requested content within configured
bounds. A missing or corrupt committed blob is a recovery error, never an empty
document. The legacy inline store remains available when no document store is configured.

```csharp
services.AddSingleton<IXRegistryDocumentStore>(
    _ => new FileXRegistryDocumentStore(privateDocumentDirectory));
```

`IXRegistryJournalMaintenance.RetireOutcomesAsync` explicitly acknowledges response
bodies owned by a caller. It retains permanent operation-ID barriers and the
original committed/rejected classification; retired IDs return `410 operation_retired`
instead of being executed again. It requires no outstanding preparations.
`XRegistrySyncStateManager.CompactAsync` similarly retires only explicitly
acknowledged terminal intents at an exact state generation. Pending work, active
conflicts, baselines, tombstones and the monotonic operation sequence remain.
Neither mechanism uses a TTL. Collect unreferenced document blobs only during
exclusive offline maintenance, retaining references from authoritative **and
recovery** generations; never collect against a partial inventory or while
preparations may still own blobs.

The connector exposes offline state administration without opening either upstream:

```powershell
opcua-xregistry state-status --state D:\registry-state --job plant
opcua-xregistry state-backup --state D:\registry-state --job plant --snapshot D:\registry-backup
opcua-xregistry state-restore --state D:\restored-registry-state --job plant --snapshot D:\registry-backup
opcua-xregistry state-compact --state D:\registry-state --job plant --expected-generation 42 --acknowledge operation-id
```

`--snapshot` names a private storage **directory**, not a JSON file. Backup and
restore use the same durable publication and corruption checks as synchronization.
Both require a pristine destination and advance its generation; neither overwrites
existing recovery evidence, resets corrupt state, drops pending intents, or contacts
an upstream. Status reads do not create missing state. Keep the source and backup
until a restored job has been independently verified.

### Native change notifications

The bridge uses the existing xRegistry event coalescer, generated event states
and notifier hierarchy. A locally controlled mutation constructs its event
batch before commit and reports it only after authoritative success. Failed,
aborted and replayed operations do not publish another batch. External HTTP
changes are reported after a complete verified refresh, not as a durable change
log. Inbound event subscription and delivery retain original-caller authorization;
cached permissions are invalidated between batches.

An event carries `CorrelationId` only when the upstream returns that same value
for the interaction. Client operation IDs are never substituted. A backend that
changes its prepared correlation after commit causes the event batch to be
discarded and notification health to degrade, not a false mutation rejection.

`EnableChangeEvents` enables this behavior by default. `EventSource` can set
the stable source URI; otherwise the upstream public root is used, with the
instance namespace as a fallback. Delivery failures are observable through
`IsNotificationDegraded` and telemetry without rewriting a known commit as a
rejection. Optional `Changed` details are omitted when a metadata inventory
cannot determine all document changes.

The companion's UInt32 epoch limit does not narrow the extended metadata or
file-write guard. A wider epoch makes the corresponding scalar property return
`BadOutOfRange`; full JSON metadata and conditional writes retain the original
integer. Native event emission is marked degraded for an unrepresentable epoch
rather than reporting a truncated or invented value. An inaccessible referenced
Resource has a stable logical node, no invented Version children, and `BadNoData`
for unavailable scalar properties. Polling remains available.

`XRegistryOpcUaEndpoint` also implements `IXRegistryChangeFeed` using the existing
ManagedSession V2 subscription manager. Hints are coalesced into one bounded
slot and **always require a full inventory**; their path is informational.
Disposing the iterator releases its monitored item and subscription. A model
or connection/namespace change ends the old stream explicitly, requiring
re-inspection and subscription recreation. Hosts retain periodic polling and
must not infer absence from hints or a quiet stream.

```csharp
await foreach (XRegistryChangeHint hint in nativeEndpoint.WatchAsync(caller, cancellationToken))
{
    if (hint.RequiresFullInventory)
    {
        await synchronizer.RunOnceAsync(cancellationToken: cancellationToken);
    }
}
```

The HTTP binding has no invented watch route, remote prepare method or event
replay guarantee.

### Synchronization safety

Reconciliation compares each side with a confirmed baseline, not with the other
side's numeric epoch. Backend epochs, timestamps, links and correlation IDs are
not replicated as if they were user content. A root epoch is not a recursive
change watermark; child inventories must be read.

`--conflict-policy manual` is the default and holds conflicting entities while
allowing unrelated entities to proceed. `prefer-opcua` and `prefer-http` choose
a side, but do not bypass destination preconditions or use clock-based
last-writer-wins.

Automatic deletion requires confirmed prior synchronization, a complete inventory
showing source absence, stable registry/scope identities, and still-current
destination guards. Timeouts, authentication failures, incomplete scans, model
changes and root disappearance are not deletion signals. Subtree deletion must
not remove concurrently edited descendants.

Ordinary endpoints can safely authorize empty-group deletion using their epoch
guard. Resource/subtree and exact-Version deletion requires a qualified prepared
destination: the bridge verifies affected descendants and Resource Meta/default
state after preparation, and the global generation guard protects the final commit.
Without that guarantee those operations are held, because the ordinary HTTP
contract cannot atomically combine those guards. Compatible, independently
verifiable model extensions are reconciled first, with a durable model intent
and registry-epoch guard. Incompatible model migrations and unresolved model
includes require explicit qualification rather than an implicit data rewrite.
Prepared Resource closures carry matching attributes, ancestry, defaults and
ordering/retention dependencies together; independently changed siblings and
unexpected preview effects abort the candidate.

`XRegistryVersionCorrespondence` records exact canonical/OPC UA/HTTP Version
addresses within one Resource. Explicit mappings can be supplied through
`Sync:VersionCorrespondences` (`CanonicalPath`, `OpcUaPath`, `HttpPath`).
For qualified prepared destinations that assign IDs, the bridge validates the
assigned preview and persists correspondence before commit. Version/default/
ancestor IDs and model-typed references are translated; opaque domain strings
are not. A lost creation response remains pending without a confirmed outcome,
even if the current content matches.
See the [synchronization profile](../src/Opc.Ua.XRegistry.Bridge/Sync/README.md)
for the exact supported mutation matrix.

The state provider persists intent before a write and verified outcome before
advancing a baseline. After a lost response, the bridge consults outcome support
or guarded read-back; it does not blindly repeat PUT or server-assigned version
creation. Ambiguous operations remain pending. Corruption, unsupported state
formats, uncertain durability and exhausted quotas fail closed. Do not delete
state to clear a conflict: doing so discards the evidence that makes deletion
propagation safe. Tombstones have no arbitrary time-based expiry.

Native events are optional invalidation hints. HTTP polling is the common
denominator; the core binding does not specify a watch endpoint. Optional
`xregcorrelationid` is not assumed to be caller-controlled or sufficient for
echo suppression.

### Credentials and deployment

The connector uses an explicit operator credential profile by default.
Inbound authentication and authorization are independent of upstream credentials.
Embedding hosts can supply per-caller endpoint/identity resolution with isolated
sessions instead. `IXRegistryEndpointResolver` returns an `IXRegistryEndpointLease`
bound to the full caller/session/role scope. One HTTP lease spans inspection,
wire preparation and commit. `XRegistryScopedEndpoint` supplies the same retained
prepare/candidate/journal lifetime for native gateways and synchronization.
Lease revocation and live authorization are checked before dispatch and publication;
there is no shared model/token cache in these adapters. Never forward arbitrary
inbound credential headers upstream.

```csharp
var resolver = new XRegistryEndpointResolver(AcquireCallerEndpointAsync);
app.MapXRegistry("/registry", resolver, routeOptions);
// Or, for an existing native/server DI composition:
services.AddXRegistryCallerEndpoints(resolver);
```

The acquisition callback owns credential-profile selection and returns a lease
with its asynchronous release callback, optional revocation token and live
authorization check. Native address spaces still have one explicit projection
visibility scope; use separate manager instances for different data visibility.

There are no password or access-token command-line options. Secret references
resolve through `ISecretRegistry`; the executable can map names to environment
variables with its read-only `Environment` store. Custom hosts can substitute
other secret stores and identity/token providers. Environment-variable **names**,
not secret values, belong in configuration.

For example, `connector.json` can contain:

```json
{
  "PkiRoot": "D:\\RegistryState\\pki",
  "Secrets": {
    "upstream-http": "REGISTRY_HTTP_TOKEN",
    "upstream-ua": "REGISTRY_UA_PASSWORD",
    "inbound-http": "REGISTRY_BRIDGE_TOKEN"
  },
  "Profiles": {
    "production": {
      "Http": {
        "BearerSecret": "upstream-http",
        "IsQualifiedBinding": true
      },
      "OpcUa": {
        "Identity": {
          "EnableAnonymous": false,
          "UserName": {
            "UserName": "registry-operator",
            "SecretName": "upstream-ua",
            "SecretStoreType": "Environment"
          }
        }
      }
    }
  },
  "HttpServer": {
    "BearerSecret": "inbound-http",
    "AllowAnonymousReads": false
  },
  "NativeGateway": {
    "AllowedSubjects": ["CN=AuthorizedRegistryClient"]
  }
}
```

`Http:IsQualifiedBinding` is an explicit deployment attestation, not automatic
trust in a version string. Set it only for an HTTP registry qualified to provide
the binding's atomic failure, epoch and successful-update semantics. The adapter
also checks advertised capabilities. Leave it false for inspection of an unknown
server.

The executable's native gateway advertises X.509 user authentication and
SignAndEncrypt. Provision its Users trust list and explicitly allow the identities'
display names in `NativeGateway:AllowedSubjects`. The default projected root is
`nsu=urn:opcfoundation.org:xregistry:bridge;s=XRegistryBridge`. Embedding applications
can provide other authenticators, context mappings and isolated visibility scopes
through the existing server hosting API.

Optional `NativeGateway:Users` entries contain `UserName`, `PasswordSecret`,
`SecretStoreType` (default `Environment`) and `Enabled` (default true). They use
the existing username authenticator, and each native operation rechecks the
secret reference: rotating/removing the secret or disabling a user revokes the
old identity. No password belongs in JSON configuration.
`NativeGateway:Issuers` entries use the existing JWT/JWKS infrastructure and
require `IssuerUri`, `Audience` and a credential-free HTTPS `JwksUri`; signed JWT
expiry is checked on every operation. X.509 users are revalidated through the
certificate manager's Users trust list. All token types still require the
separate native subject allowlist and SignAndEncrypt.

HTTP gateway callers use the separately configured inbound bearer secret over
HTTPS. That secret is never forwarded to the upstream registry. Its
`/_bridge/ready` endpoint reports the shared runner's latest bounded authoritative
inspection and the availability of atomic writes. Diagnostic logs go to stderr; command status,
inspection and reconciliation records use JSON on stdout.

OPC UA connections select SignAndEncrypt and do not automatically trust unknown
certificates. Certificate lifecycle uses the stack's certificate configuration,
manager and stores. HTTP operator bearer credentials require HTTPS.
`--allow-loopback-http` is only for uncredentialed local development HTTP, not
remote plaintext deployment.

Use a private, persistent local state directory, an explicit public HTTP root,
bounded request limits, and one writer per job. Do not trust `Host` or forwarding
headers to select an upstream registry or to construct public links.

`XRegistryBridgeRunner` owns the reusable all-mode cadence, health/last-success
snapshot and bounded change-hint scheduling. The CLI uses it for HTTP gateway
health, native projection refresh and synchronization. `AddXRegistryBridgeRunner`
supports embedding; the host retains ownership of sessions, listeners and stores.
Each hint requests a complete repair, periodic scans remain enabled, and shutdown
observes late subscription cleanup without disposing in-use resources.
Gateway modes never resolve or run an injected synchronization job. A timed-out
health or projection operation retains the pass gate until its actual completion;
new passes cannot overlap it. Embedding hosts stop the runner and await
`WaitForPendingOperationsAsync` before disposing their transports and stores.
The CLI bounds its shutdown wait and defers resource disposal, with a diagnostic,
if a provider continues after cancellation.
Status includes the time since the last successful full pass and optional native
open-handle, memory and spool reservations. Counts are diagnostic, not admission
tokens or a distributed quota.

Information-level audit records identify caller/upstream/job, action, target and
known or unconfirmed outcome through source-generated telemetry and the existing
redaction wrappers. Native transaction Calls also use the server's audit-event
API when auditing is enabled. Document bodies and credentials are never audit
arguments. Configure the stack redaction strategy and logging filters for the
deployment; redaction wrappers do not enable redaction by themselves.

Native buffers spill to individually owned temporary files above
`NativeGateway:MemoryBufferThreshold` (256 KiB by default). The CLI configures
`NativeGateway:SpoolDirectory` beneath its local application-data directory
unless explicitly overridden. `NativeGateway:MaxSpoolBytes` bounds aggregate
disk reservations (1 GiB by default). Library hosts can leave `SpoolDirectory`
null for bounded in-memory operation. A failed spool write invalidates its
handle; partial bytes cannot be published on Close.

Synchronization state format 2 retains model baselines, Version correspondence
and assigned-creation evidence. The reader accepts format 1 without deleting or
reinitializing it; subsequent writes use format 2. Older readers must not open
format 2. A changed legacy registry/model scope remains held until explicitly
revalidated. Back up the state, initialization/recovery markers and document
directory as one consistent maintenance set before migration.
`ExportSnapshotAsync` validates and exports an offline synchronization snapshot.
`RestoreIntoPristineAsync` validates supported schema/checksum/job identity and
restores only into a proven pristine destination. It never overwrites live or
corrupt state; retain the old directory and recovery evidence until the restored
job has been verified.

See [Identity Providers](IdentityProviders.md),
[Certificate Manager](CertificateManager.md), and
[Dependency Injection](DependencyInjection.md) for the shared infrastructure.

### Source baseline

The implementation baseline is pinned rather than floating:

* `xregistry/spec@a1544396d63b74cdf1de5da6a269d02b88696802`:
  `core/spec.md`, `core/http.md`, `core/model.md`, `core/events.md`,
  `pagination/spec.md`, and `workingdrafts/bindings/opcua.md`.
* `OPCF-Members/spec-drafts@9d3fdeb77259dedd257ee2f3f522cf7cc16f676e`:
  `source/core-specs/xregistry/spec.md` (authorized access required).

The public OPC UA working draft points to
`marcschier/opcua-drafts@ff22f224400fc8be813bf0abcbfc3cde52bc7ed3`,
`core-specs/xregistry/OPC-UA-xRegistry.md`, rather than the selected OPCF revision.
This implementation selects the supplied OPCF companion for native type
definitions; it does not claim the drafts establish an unambiguous precedence
rule. Companion model version, xRegistry `specversion`, resource `versionid`,
and the experimental envelope version are different identifiers and must not
be interchanged. The pinned pagination document identifies itself as `0.1-wip`.

### Conformance and acceptance ledger

The versioned machine-readable
[acceptance ledger](../tests/Opc.Ua.XRegistry.Tests/Conformance/xregistry-acceptance-ledger.json)
records 146 stable requirement IDs across the nine original acceptance areas,
the original scope decisions/exclusions, and all twelve remaining-work gap areas.
It is an inventory of the **qualified profile and unfinished acceptance**, not
a certification result or a claim that the original full scope is delivered.

Each requirement resolves its source file and section through an immutable
source manifest, then records its normative level, gateway/synchronization
modes, capability rule, expected outcomes, qualification, remaining work and
exact test references. Public sources have both their selected Git revision and
downloaded-byte SHA-256. Local approved-plan and reviewed-evidence references
use artifact SHA-256 revisions. The selected companion entry records authorized
file metadata and the reviewed synthesis, not redistributed members-only text.
The older public companion reference is retained separately.

The historical `remaining-plan` entry preserves its recorded digest, but its
original snapshot is unavailable and is not claimed as currently verified
bytes. The archived qualified-profile plan and approved alias/mapping expansion
have separate verified artifact hashes. `planProvenance` records that neither
archive substitutes for the missing historical revision.

| Status | Meaning |
| --- | --- |
| `implemented` | Behavior exists within the stated qualification; not a current test-run claim. |
| `partial` | Some behavior exists, but the listed remaining acceptance is unfinished. |
| `missing` | Required positive implementation or assurance is outstanding. |
| `provider-required` | A qualified domain/resolution/validation implementation must still be supplied. |
| `upstream-impossible` | The named unextended upstream lacks the primitive; its capability rule denies unsafe use. |
| `excluded` | An explicit approved non-goal, not completed mandatory functionality. |

`MUST`/`SHOULD`/`optional` classify the referenced specification rule;
`policy` identifies local acceptance or deployment choices. These are
paraphrases, not normative quotations. A specification-optional capability
promised by the approved plan can still be unfinished acceptance. Model,
query/pagination, native lifecycle, dependency-aware sync, storage, caller leases
and all-mode runtime features have implementation evidence, but their full release
matrix and ledger promotion remain separate gates. The incarnation-guard implementation and
its exact provider/codec/native/HTTP test references do not alone establish
complete lifecycle or snapshot safety. Native-base write
denial does not turn missing mandatory positive HTTP semantics into an
implemented feature.

Evidence kinds distinguish positive behavior, safety invariants,
unsupported-operation denials and structural checks. **Ledger integrity tests
are not protocol conformance tests.** They protect pins, IDs, route/action
inventory, evidence shape and status logic; they do not prove every linked
existing test executes on every TFM. Cross-project test references do not add
HTTP/Bridge/Tools dependencies to the portable provider test project.

The separate
[core provider oracles](../tests/Opc.Ua.XRegistry.Tests/Conformance/core-provider-oracles.json)
are independently authored literal requests, response expectations and
authoritative-state probes. Eight tests exercise zero versus absent/null epoch,
ignored create epochs, identical/empty touches with an injected clock, immediate
parent membership counters, failed nested-write rollback, omitted versus empty
content, documentless resources and missing resources. They call the real
transactional provider directly, without a client/server codec roundtrip.
They pin provider-level behavior, not unexercised HTTP wire behavior.

The literal counter sequences explicitly select the provider's zero-initialized,
once-per-request increment policy. The core specification requires an increase,
not an initial zero or a unit increment. Exact timestamps, bytes, fields and
membership outcomes come from fixture literals, never recorded endpoint output.

Execution evidence belongs in NUnit/TRX and coverage results, not in a static
ledger success flag. Maintainer design sign-off remains `blocked-external` and
is not supplied by passing structural or provider tests. To execute the new
tests from the repository root, use the existing target-selection mechanism,
serially:

```powershell
$env:CustomTestTarget = "net10.0"
dotnet test tests\Opc.Ua.XRegistry.Tests\Opc.Ua.XRegistry.Tests.csproj `
  -c Release --no-restore -m:1 `
  --filter "FullyQualifiedName~Opc.Ua.XRegistry.Tests.Conformance" --logger trx
```

Repeat with `CustomTestTarget=net48` and `CustomTestTarget=netstandard2.1` for the
legacy and mixed-library consumer configurations. Full solution/release gates
remain separate. Extend existing requirement IDs when implementing remaining
work: add an independent positive oracle, retain denial/rollback coverage,
update the qualified status and evidence, and intentionally update inventory
checks when adding new requirements. Do not mark full acceptance complete from
test counts, structural checks or unsupported-operation tests.

### Dependency injection and embedding

The xRegistry binding services compose with `services.AddOpcUa()` rather than
creating a second stack lifetime. `AddXRegistryTransactions` registers the
optional atomic provider over an injected `IXRegistryTransactionStore`.
`AddXRegistryCallerEndpoints` registers a caller-specific resolver and scoped
endpoint adapter; its leases retain authorization and resource ownership through
preparation and commit. Register `IXRegistryDocumentStore` for immutable blob
storage rather than embedding all document bytes in each persisted generation.

The same transactional registration exposes `IXRegistryAddressResolver` and
`IXRegistryShortLinkMaintenance`. Short links are disabled by default. With
`ShortLinksEnabled`, explicitly await `InitializeShortLinksAsync` using an
authorized writer before opening listeners. This initializes the persistent
alias catalog; resolving services or reading a registry never migrates storage.
The catalog belongs to the provider's transactional format 2, not the independent
synchronization-state format.

Configure `XRegistryBridgeNativeOptions.AttributeMappings` for both native
discovery and projection. URI-qualified paths map declared logical attributes
to typed Properties or explicitly canonical String Properties. Pass the same
options to the endpoint and node-manager factory; registered structures use an
explicit `IEncodeableType` activator, not reflection-based assembly discovery.
Mappings do not grant write permissions or upstream transaction guarantees.

`AddXRegistryBridgeRunner` exposes the same health, repair cadence and
synchronization orchestration used by the connector executable. The embedding
host still owns its listeners, `ManagedSession` instances and stores. There is
no implicit network connection or background registry writer during service
registration. Direct constructors remain available, and an endpoint resolver
can be passed directly to `MapXRegistry`.
After stopping scheduled execution, await the runner's
`WaitForPendingOperationsAsync` before disposing those resources. An operation
that outlives its deadline remains owned and blocks an overlapping pass.

Do not bind one cached operator projection to callers with different data
visibility; use separately authorized native manager instances for those scopes.

## Related documentation

* [Source generation](../tools/Opc.Ua.SourceGeneration/readme.md) — how the companion model and its typed proxies are compiled into the assembly.
* [Node management](NodeManagement.md) — the node manager model the three managers build on.
* [PubSub (Part 14)](PubSub.md) — the PubSub Schema Registry specialization of this model.
