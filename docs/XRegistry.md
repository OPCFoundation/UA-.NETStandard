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

## Packages

| Package | Depends on | Contains |
| --- | --- | --- |
| `OPCFoundation.NetStandard.Opc.Ua.XRegistry` | `Opc.Ua.Core` | The source-generated base companion model (types, NodeIds, `NodeState`s and `*TypeClient` proxies), `XRegistryWellKnown`, `IResourceContentIdProvider` |
| `OPCFoundation.NetStandard.Opc.Ua.XRegistry.Client` | `Opc.Ua.XRegistry`, `Opc.Ua.Client` | `XRegistryClient` — fast-path resolve and lifecycle registration |
| `OPCFoundation.NetStandard.Opc.Ua.XRegistry.Server` | `Opc.Ua.XRegistry`, `Opc.Ua.Server` | The three node managers and `XRegistryServerOptions` |

`Opc.Ua.XRegistry` has no dependency on either SDK, so a codec or a shared contracts assembly can
reference the identity abstraction without pulling in the client or the server.

The model assembly consumes the reviewed **0.7.0 draft** dated 2026-09-12,
paired with the WoT Connectivity 1.2 draft. It preserves the existing namespace,
numeric identities and native event hierarchy, and adds origin and canonical
capability snapshot declarations. This is a draft model input, not a published
release or a claim of runtime support for every new declaration.

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

### Source-derived identifier allocation

`XRegistryIdentifier` constructs readable tokens from source identities, never from
document bytes or content digests. URI authority parsing discards query and fragment
text even when there is no slash after the authority. Readable path labels are
percent-decoded, but that lossy operation must never change the registry's exact
authority key.

A domain registry first reuses its durable exact-authority mapping. For a new
allocation, the helper compares occupied identifiers case-insensitively. It tries
the readable token, then SHA-256 suffixes of 8, 16, 32 and 64 lowercase hexadecimal
digits over the original UTF-8 identity. Each suffix reserves its space within
the 128-character limit. Full-hash collisions use positive decimal ordinals;
the default declared bound is 1,024, with explicit failure on exhaustion.
The overload accepting `ArrayOf<string>` also accepts a readable domain prefix
and a finite ordinal bound. Candidate construction alone is not a transaction:
the domain service commits mapping, allocation and structure together.

The shared projection engine supports preparing a preserving exact-Version write
reservation before a domain's durable structural commit. It uses the same file
provider and session-handle lifecycle as normal reconciliation, which adopts the
prepared entry after commit. Providers opt in through
`IXRegistryProjectedPreservingResourceFile`; no second content store is introduced.
`XRegistryResourceFileReservation.SessionClosedToken` invalidates a reservation
when its owning Session is discarded. A domain must link this token into the
existing creation transaction's cancellation token before the store's commit
point. Complete the reservation only after commit and reconciliation; otherwise
dispose it. Session cleanup after a completed store commit does not roll back the
committed generation.

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

`GetPosition` and `SetPosition` return `Bad_InvalidArgument` for unknown, closed, or foreign-session
handles (OPC 10000-20, 4.2.6 and 4.2.7), without moving a cursor or consuming the owner's handle.
Genuine handle-state errors still return `Bad_InvalidState`.

For projected logical files, explicit `Close` also requires exact session ownership: a scoped
caller cannot consume an unscoped pin, or vice versa.

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

Whole-document publication uses the optional `IXRegistryAtomicResourceStore.ReplaceAsync` capability,
implemented by both stock stores. An offset-only store supports initial uploads, but the registrar
must first remove any **uncommitted** bytes left by a failed upload and failed compensating cleanup.
If that cleanup still fails, `Close` fails without writing or publishing the retry. Replacing
**committed** content requires the atomic capability and otherwise returns `Bad_NotSupported`;
the registrar never deletes established content to emulate atomic replacement.

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
proxy is a `ResourceType` Object carrying three different facts:

| Property | Meaning |
| --- | --- |
| `OriginRegistry` | Read-only, immutable origin identity from an independently trusted binding. |
| `ExternalReference` | The remote **logical Resource** Object, never its content-digest Variable or an exact Version. |
| `ResourceUrl` | An authorized transport locator, not evidence of application or registry identity. |

The proxy retains its own configured `ResourceId`, `VersionId` and historical structural `Xid`;
the remote logical Xid is pinned separately in `XRegistryFederationTarget`. Equal bytes do not merge
different remote entities. A non-materialized proxy has no local `Versions` folder: follow its
verified remote logical Resource to browse Versions and use the generated FileType client.

#### Configuring the trusted binding

`PublishFederationProxy` requires an immutable `FederationTarget`, an authorized `RemoteEndpointUrl`
and an `IXRegistryFederationProvider`. The provider must verify the origin, logical Resource
ownership/Xid and FileType/Versions capability before publication. Missing or unsupported verification
fails with `Bad_NotSupported`; it never falls back to a content link. A disabled proxy needs none of
these optional settings.

An existing `XRegistryClient`, including `WotRegistryClient`, implements the native provider over its
already connected Session. Establish that Session using the application's endpoint authorization and
certificate trust policy, independently of proxy/document metadata. Pin the expected ApplicationUri,
registry-root NodeId and assigned logical Resource identity from trusted configuration:

```csharp
const string remoteNamespace = "urn:example:remote-registry:nodes";
const string applicationUri = "urn:example:remote-registry";
var binding = new XRegistryFederationTarget(
    new RegistryOriginDataType
    {
        OriginUri = string.Empty,
        ServerUri = applicationUri,
        RegistryNodeId = new ExpandedNodeId(XRegistryWellKnown.RegistryObject, remoteNamespace)
    },
    applicationUri,
    new ExpandedNodeId("pump-resource", remoteNamespace),
    "/groups/pumps/resources/pump-resource");

var remote = new GenericXRegistryClient(remoteSession, remoteNamespace, telemetry);
var options = new XRegistryServerOptions
{
    PublishFederationProxy = true,
    FederationTarget = binding,
    FederationProvider = remote,
    RemoteEndpointUrl = authorizedEndpointUrl
};
var federation = new XRegistryFederationNodeManager(server, configuration, options);
```

Use the actual assigned remote NodeIds and Xid, not identifiers derived from document bytes or an
endpoint path. Portable NodeIds carry a namespace URI and no session-local namespace/server index.
The native provider requires a signed or encrypted authenticated channel, verifies the channel's
actual application/endpoint rather than mutable configured endpoint labels, and checks the
registry root, Group ownership, logical Resource type/Xid, typed Versions folder and executable
FileType read Methods. Verification does not open a file or read content.

The alternative stable `OriginUri` form is available to explicitly configured custom providers.
It has empty `ServerUri` and null `RegistryNodeId` inside `OriginRegistry`; the native Session
provider rejects that form with `Bad_NotSupported` rather than inventing an application/root
identity. Origin URI comparisons preserve exact spelling, including case and percent escapes.

DI uses the existing `AddXRegistryServer` seam:

```csharp
services.AddSingleton<IXRegistryFederationProvider>(remote);
services.AddXRegistryServer(options =>
{
    options.PublishFederationProxy = true;
    options.FederationTarget = binding;
    options.RemoteEndpointUrl = authorizedEndpointUrl;
});
```

Direct construction remains supported. The application owns the provider's Session lifetime.
The node manager captures its configuration at construction; later mutation of the options or
of an `OriginRegistry` copy cannot replace the captured authority.

#### Following and relocating a proxy

Use the independently selected remote client and trusted binding to follow metadata obtained from
the local referencing Session:

```csharp
ResourceTypeClient logical = await remote.FollowExternalReferenceAsync(
    localSession, proxyNodeId, binding, ct);
ResourceVersionsTypeClient? versions = await logical.GetVersionsAsync(telemetry, ct);
ByteString document = await logical.ReadDocumentAsync(ct: ct);
```

The client validates the actual scalar declarations as well as their values: `OriginRegistry`
must declare `RegistryOriginDataType`, `ExternalReference` must declare `ExpandedNodeId`, and
`ResourceUrl` and the remote Group/Resource `Xid` must declare `String`. Wrong DataTypes or ranks
fail with `Bad_TypeMismatch`, even if the encoded value would otherwise look valid.

`ExternalReference.ServerIndex` resolves through the referencing Session's `ServerUris`;
zero means that referencing application. An index-only namespace resolves through that Session's
`NamespaceUris`. The resulting portable identity is then resolved against the selected remote
Session's current namespace table. Neither an old Session's indexes nor a source server's indexes
are reused as remote identity.

A default-Version change leaves the logical target, origin and local Xid unchanged. A new FileType
handle reads the new default; a handle opened before the switch retains its original exact bytes.
The independent content fast path may be removed or replaced without changing these identities or
invalidating the logical handle. `FederatedDocument`, `ContentIdProvider` and the legacy
`RemoteRegistryNamespaceUri` content-lookup hint do not gate proxy publication. The legacy
`RemoteServerIndex` hint is ignored; the current ServerArray index is derived from the pinned
ApplicationUri when the reference is read.

For an authorized endpoint relocation, establish a replacement trusted Session/client and call:

```csharp
await federation.UpdateEndpointAsync(replacementEndpointUrl, replacementClient, ct);
```

This verifies the **same** pinned origin, logical target and Xid before changing only `ResourceUrl`.
Failed or cancelled verification leaves the published binding unchanged, including cancellation
while waiting for another update or after a provider returns. An origin change requires a new
binding, not a locator update. Startup verification and locator updates are awaited and serialized;
no connection manager, automatic URL fetch, resolver or content cache is created.

These are the bounded client/provider and proxy-publication surfaces for the xRegistry federation
and WoT Connectivity federated-origin contracts. They do not implement automatic federated
dependency acquisition, durable federation-record storage or projection restore/startup orchestration.
Hosts retain their trusted configuration and obtain fresh provider verification on reconstruction.
This support alone is not a claim of Full, XREG-Federation or WOTC-Federation conformance.

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

## Related documentation

* [Source generation](../tools/Opc.Ua.SourceGeneration/readme.md) — how the companion model and its typed proxies are compiled into the assembly.
* [Node management](NodeManagement.md) — the node manager model the three managers build on.
* [PubSub (Part 14)](PubSub.md) — the PubSub Schema Registry specialization of this model.
