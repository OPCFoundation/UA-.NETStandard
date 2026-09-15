# xRegistry OPC UA / HTTP bridge

The xRegistry connector hosts a protocol gateway or reconciles two independently
writable registries. Gateways are **write-through**: a successful response means
the authoritative backend completed the operation. Synchronization is a separate,
explicitly selected mode; it is not used to acknowledge gateway writes early.

This is experimental support for **xRegistry 1.0-rc4** and the OPC UA working
drafts, not a claim of certification or final-standard conformance.

## Packages and construction

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

## Choose a mode

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

## Atomicity and capability negotiation

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

### Generation-consistent inventory

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

### Version incarnation guards

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

### Prepared transfer cleanup and deadlines

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

### Optional transactional provider

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

### Persistent short links

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

### Model-driven native attributes

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

### Content storage and acknowledged maintenance

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

## Native change notifications

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

## Synchronization safety

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

## Credentials and deployment

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

## Source baseline

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

## Conformance and acceptance ledger

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
