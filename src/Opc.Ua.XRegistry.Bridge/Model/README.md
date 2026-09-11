# Experimental native xRegistry transport

This repository-local model is **experimental**, not a published OPC UA companion specification or an xRegistry
conformance claim. It does not change the existing xRegistry namespace, model IDs, clients or domain registries.
The base companion model remains version 0.6.0; the transport model is 0.2.0, transport ProtocolVersion is 2,
and the encoded envelope format remains 1. Version-1 transports remain readable but do not qualify preparation.

## Construction

```csharp
var options = new XRegistryBridgeNativeOptions
{
    NamespaceUri = "urn:example:registry:gateway",
    RootIdentifier = "Registry",
    ProjectionContext = operatorContext,
    AuthorizeCallerAsync = authorizeNativeCallerAsync,
    ContextFactory = authenticatedContextMapper
};
var factory = new XRegistryBridgeNodeManagerFactory(authoritativeEndpoint, options);
await server.NodeManagerLifecycle.AddAsync(factory, callerContext: null, cancellationToken);

NodeId root = ExpandedNodeId.ToNodeId(options.RootAddress, session.NamespaceUris);
IXRegistryEndpoint native = new XRegistryOpcUaEndpoint(session, root, options, telemetry);
XRegistryEndpointDescription description = await native.InspectAsync(operatorContext, cancellationToken);
```

`session` is a host-owned `ManagedSession`. Register the factory through the existing server factory/DI
infrastructure, or construct `XRegistryBridgeNodeManager` directly. The bridge does not own or dispose its endpoint,
session or endpoint storage. An HTTP reverse gateway passes `XRegistryHttpEndpoint` as `authoritativeEndpoint`.

The default root is `nsu=urn:opcfoundation.org:xregistry:bridge;s=XRegistryBridge`. Instance namespaces are configurable
and must be separate from the core and experimental model namespaces. Never persist a numeric namespace index.
The adapter retains the root namespace URI and resolves it, the extension and all generated proxies afresh per call.

## Inbound authorization is separate from operator credentials

`AuthorizeCallerAsync` has type
`Func<ISystemContext, bool, CancellationToken, ValueTask<bool>>`; the Boolean means mutation.
The callback receives the original native session context, before `ContextFactory` maps it to the upstream
operator profile. Inspect `ISessionSystemContext.UserIdentity` for the authenticated identity and
`GrantedRoleIds`; apply the host's configured subject and role allowlists. Certificate trust and native user
authentication remain the server host's responsibility. Do not treat arbitrary request fields or an upstream
operator role as permission for the inbound caller.

Every native request requires an authenticated session. Missing `AuthorizeCallerAsync` makes mutation access
deny-by-default, even if the mapper returns an authenticated operator with `xregistry.write`. A configured
callback is also applied to reads. The remaining visibility-scope check still requires the mapped context to
match `ProjectionContext`.

All mutations unconditionally require SignAndEncrypt. Neither an authorization callback nor options can relax
this; assigning `RequireEncryptedWrites = false` is rejected. The callback is re-evaluated for native methods,
property Writes and resource file writes/commit-on-Close, so a permission revoked after Open cannot publish
buffered content. Pinned file reads also re-check read authorization.

An authenticated read-authorized caller may stage a transport envelope for a read request. Upload Write/Close
does not grant mutation authority: the decoded request is authorized again at Commit before endpoint invocation.

## Generated model

Namespace URI: `http://opcfoundation.org/UA/xRegistry/Bridge/Experimental/`.

| ID | Declaration | Behavior |
| --- | --- | --- |
| 1000 | `RegistryBridgeType` | Optional `Bridge` component of a projected Registry |
| 1100 | `ProtocolVersion` | UInt32, currently 2 |
| 1200 | `InspectRegistry` | Returns an open read FileType containing a codec description |
| 1210 | `BeginRequest` | Returns a session-owned upload FileType and open write handle |
| 1220 | `CommitRequest` | Takes the upload NodeId, operation ID and request SHA-256; returns an open response file |
| 1230 | `AbortRequest` | Releases an owned upload or response; never rolls back a completed operation |
| 1240 | `GetOperationOutcome` | Delegates to the actual endpoint journal; 0 unknown, 1 committed, 2 rejected |
| 1250 | `PrepareRequest` | Validates a sealed upload and returns an open immutable response preview without mutation; the upload is the lease identity |
| 1260 | `CommitPreparedRequest` | Consumes the lease; null NodeId/zero handle means success using the original preview, otherwise returns a rejection response file |

States, factories, method arguments and `RegistryBridgeTypeClient` are emitted by the existing source generator.
There is no runtime NodeSet XML parsing or reflection-based request serialization.

## Transfer and operation semantics

Use `XRegistryProtocolCodec.EncodeRequest` for the complete request, including any document. Transfer it in bounded
FileType chunks. Close **only seals** the upload. Commit checks session/caller ownership, operation identity and
`ComputeRequestDigest` before invoking the endpoint exactly once for that upload. The decoder always receives the
trusted authenticated context from the host; envelope contents cannot impersonate another caller.

There is no bridge-local replay cache. The same operation ID in another upload is forwarded only when the endpoint
advertises durable replay and implements `IXRegistryOperationJournalEndpoint`. A bare HTTP endpoint does not acquire
durability or idempotency by being wrapped. Unknown outcomes are not rejections and must not trigger retries.
After an ambiguous commit, use a qualified journal or reconcile/read back; do not blindly repeat an HTTP touch.

Response and inspection bodies also use FileType, so they are not limited to one OPC UA message. Close each response
handle and call AbortRequest to release its node. Session close and bounded file lifetimes reclaim abandoned transfers.
File/count/byte/chunk/entity/page limits are validated in `XRegistryBridgeNativeOptions`.

Preparation is advertised only if the endpoint implements `IXRegistryPreparedEndpoint`
and qualifies `SupportsPreparedMutations`. The client also probes the actual version-2
methods; a version label or provider flag alone is not sufficient. `AbortRequest` on
the upload aborts its prepared lease. Session close, expiry and awaited address-space
shutdown release preparations. Commit rechecks the original caller's authorization.

Any intervening registry mutation invalidates a prepared candidate. HTTP hosting can
therefore read the preview, encode its exact HTTP headers/body, and commit without a
post-publication serialization failure. Direct `CommitRequest` also allocates its
response transfer before publication when its backend supports preparation. A
successful commit returns the existing preview; rejected candidates do not mutate.

## Native projection and write-through

The shared `XRegistryProjectionEngine` and versioned strategy materialize Groups, stable Resources, Versions folders
and exact Version files. Private projection keys escape the complete collection-aware path; exposed IDs and Xids retain
their exact identities. Base group/resource creation rejects ambiguous multi-collection selection instead of choosing
one arbitrarily. The extension addresses those collections explicitly.

Core properties, string labels, independent Resource Meta/Version epochs, Model and Capabilities files are live endpoint
reads. An experimental read-only `Metadata` FileType preserves complete typed JSON for each entity. Arbitrary JSON is not
stringified into `AttributesType`. UInt32 epoch overflow or a missing authoritative default/version mapping is rejected.

Native create/delete/label methods and single-property Writes use the same authoritative endpoint path as extension
commits. Create checks membership and sends an atomic request guarded by the parent's counter; it does not publish a
local-only placeholder. Multi-property Writes are rejected before side effects: use one extension request for a combined
mutation. All writes require actual atomic, conditional and touch guarantees, not a nominal HTTP transport label.

Resource FileType Open pins an exact Version and its bytes. Writes and seeks are staged; a changed Close awaits a
conditional endpoint mutation. Clean or byte-identical Close does not touch the endpoint. Changing the default Version
does not retarget an existing handle. A rejected or uncertain upstream mutation is never acknowledged as a local success.

## Explicit limits

- One node manager exposes one configured visibility scope. `AuthorizeCallerAsync` checks inbound subjects/roles
  independently; the mapped subject/authority/authentication state must match `ProjectionContext`. Use separate instances
  for different data visibility scopes; shared cached projections are not a per-caller authorization mechanism.
- Writes always require SignAndEncrypt. The host owns certificates, trust, credentials and caller mapping.
  Native write fixtures use encrypted sessions with generated username credentials; no insecure test bypass exists.
- Without the experimental extension, the adapter is strictly read-only and requires an actual Model file or an explicit
  `BaseModel`. It reads real core nodes through `GenericXRegistryClient`; a network/access error is not legacy discovery.
- Legacy sibling Version files are enumerated as observed. Their logical default and Resource Meta are unavailable unless
  genuinely represented. No synthetic single-version history, modelsource or offered capabilities are generated.
- Base-only query shaping, custom typed domain-attribute mappings and unsupported aspect operations are explicitly rejected.
  Standard capability flags for the limited adapter do not advertise writes or unsupported query/extra APIs.
- Base epochs are UInt32. Wider counters remain representable in codec messages but cannot be projected through these
  core properties. Upstream state/model changes that become unrepresentable fail reads/refresh rather than truncate data.
- Inventories use bounded complete collection scans. They are not a distributed snapshot when an external endpoint offers
  no snapshot primitive. Relative next-page links must remain within the same collection; interrupted scans are not published.
- Version write guards use the endpoint epoch and check observed creation identity. The interface has no atomic incarnation
  CAS: an external delete/recreate race between the identity check and mutation cannot be given a stronger guarantee.
- Response allocation is pre-commit on prepared backends; nonprepared external HTTP
  commits and transport failures can still leave an indeterminate client outcome.
  A failed projection refresh after a known prepared commit is logged, and subsequent
  reads must refresh rather than serving stale Good data. The transport cannot undo
  an external HTTP commit or manufacture a durable outcome record.
- No event history, durable notification stream or native change-event emission is implemented by this slice. Current reads
  and explicit refresh are authoritative; notifications are not advertised as a replay mechanism.
- Content is bounded in memory, not streamed to disk. File handles cannot outlive their owning session or retired native node.
