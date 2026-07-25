# Opc.Ua.XRegistry.Server

The generic server-side **xRegistry** registry node managers for OPC UA. They serve a
content-addressed resource registry in a server address space:

- **Fast path** — publishes registered resources under an Opaque NodeId whose Identifier is
  the raw content-id bytes, so a consumer resolves a resource in a single `Read`.
- **Registration** — the `CreateResource` / `Write` / `Close` lifecycle; on close the server
  computes the resource's content-derived id (via an `IResourceContentIdProvider`) and
  publishes the Opaque fast-path node at runtime.
- **Federation** — publishes a proxy for a resource hosted by a remote registry, carrying an
  `ExternalReference` and `ResourceUrl` plus the content-derived id (stable across registries).

A concrete registry (for example the PubSub Schema Registry) supplies an
`IResourceContentIdProvider` and its own companion namespace/NodeSet.

