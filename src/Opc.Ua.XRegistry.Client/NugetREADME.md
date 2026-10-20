# Opc.Ua.XRegistry.Client

The generic **xRegistry** registry client for OPC UA, built entirely on the **source-generated
ObjectType proxies**. It talks to a registry hosted in an OPC UA server address space:

- **Resolve** document bytes from an opaque content key through the Opaque-NodeId fast path — a
  read of the node whose Identifier is the raw content-id bytes (no Browse, no fingerprint
  recomputation). The read is range-based, so a document larger than the session's
  `MaxByteStringLength` is fetched in slices rather than failing.
- **Register** a resource through the model's own lifecycle — `CreateGroup` /
  `GetOrCreateGroup`, `CreateResource` / `GetOrCreateResource`, and the `FileType` methods
  `ResourceType` inherits — plus `Delete(ExpectedEpoch)` for optimistic concurrency. Resource
  and Version `Xid` values remain stable structural paths when their document bytes change.
- **Federation** — verify a configured `XRegistryFederationTarget` over an already authenticated
  Session and follow a proxy with `FollowExternalReferenceAsync`. The client validates scalar
  metadata declarations, origin, logical ownership and FileType/Versions capability, using the
  source and remote Sessions' own namespace/server tables. It returns the generated logical
  Resource client without opening content or creating a connection/cache engine.
  Native and managed sessions supply `ISessionBindingProvider`; custom adapters
  without a generation-bound dispatch guarantee reject with `BadNotSupported`.
  The returned client's Session retains the verified binding, rejects replacement
  instead of retargeting content, and is released by the caller when finished.

After successful document writes, Close commits the resource using the caller's
cancellation token and the Session's normal request timeout. Cleanup after
cancellation or a failed Write instead uses an independent five-second budget.
If Write and Close both fail, both exceptions are retained. Uploads are not automatically
replayed; inspect the server state before deciding whether a new request is safe.

`XRegistryClient` is an abstract base carrying the xRegistry-level API;
`GenericXRegistryClient` is the sealed implementation for any registry namespace. A concrete
registry client (for example a schema registry client) derives from `XRegistryClient` and adds
domain-specific naming and defaults; because a domain model subtypes the base types, the
generated proxy chain mirrors the OPC UA hierarchy automatically.

The xRegistry 0.5.0 NodeSet also source-generates all 19 concrete `*EventTypeRecord` decoders and
their `EventFilters.Build(...)` factories. Use them with the standard OPC UA subscription APIs;
the package intentionally adds no parallel convenience subscription API.
