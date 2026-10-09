# Native Endpoint, Message and Schema registries

The OPC 30455 Endpoint Registry combines Endpoint Groups and reusable Message Groups.
Messages are metadata-only Objects, not payload files. The separate optional OPC 30451
Schema Registry owns exact schema documents and their explicit logical defaults.
Neither hosting a catalog nor reading an Endpoint connects a broker or configures PubSub.

## Modules and hosting

`Opc.Ua.XRegistry` supplies exact native values, presence-aware record mapping, shared
document descriptors and model/capabilities records. Its server module supplies CAS
state stores, metadata commits, pinned snapshots, metadata files and projection support.
Endpoint Registry and Schema Registry reuse these modules; Schema Registry does not
depend on Endpoint Registry.

Register `EndpointRegistryNodeManagerFactory` explicitly to host Endpoint/Message catalogs.
Use `EndpointRegistryServerOptions.Generic` for a generic or Message-only catalog, and
`Media` for a distinct media-only root. Loading dependency models does not host Schema
Registry. To host schemas, register `SchemaRegistryNodeManagerFactory` or call
`services.AddSchemaRegistryServer(...)` separately. Supplied stores remain application-owned.

`FileRegistryStateStore` supports single-writer staged atomic publication and restart.
Use separate directories for independent catalogs; it is not shared-folder or clustered HA.
Validation failures and stale epochs publish nothing. If durable publication succeeds but
address-space activation fails, the mutation reports `UncertainNotAllNodesAvailable`;
retry/recovery must inspect the committed generation rather than assume a failed no-op.

## Native clients

The reusable clients depend on model/client modules, not server implementations.
Applications own the connected authenticated Session and its certificate policy.

```csharp
EndpointRegistryClient endpoints =
    await EndpointRegistryClient.DiscoverAsync(session, telemetry);
RegistryReadResultDataType read =
    await endpoints.ReadDocumentAsync(new RegistryReadRequestDataType
    {
        TargetXid = "/messagegroups/factory/messages/temperature",
        DocumentKind = "metadata",
        View = 1,
        MaxItems = 100
    });
```

Check the returned native `StatusCode` and `Issues` before consuming its Document.
An OPC UA service failure throws; a successful Call can still return a bad native result.
Use generated records and `PresentFields` for writes, with the observed target epoch as
`ExpectedEpoch`. `Canonicalize` sets canonical unused fields without guessing presence.
Unknown fields and explicit null remain distinct from absence. Exact numbers do not
round through `Double`.

`RegistrySnapshotClient` opens a Session-bound lease, traverses native Structures, arrays,
Strings and ByteStrings, and closes asynchronously. It uses advertised encoding bounds,
preserves Unicode scalars and rejects incorrect leaf types. Snapshots pin committed content,
not an entitlement: revoked access is rechecked before exposing further parts.

## Schema admission and access

`RegisterSchema` supplies namespace URI, schema subject name, format, exact VersionId
and entity URI. A logical default also requires an explicit `ResourceUri`; neither source
identity nor logical URI is inferred by reversing an identifier. `BeginSchemaUpload`
provides a bounded Session-owned FileType handle; Close validates and publishes staged bytes.
Schema no-ops retain original bytes, including whitespace and numeric source forms.

Typed schema reads, exact-Version FileType downloads, `GetSchema` and Opaque fingerprint
lookup share the same store. Visibility is evaluated before legacy lookup ambiguity.
Distinct visible candidates fail explicitly instead of selecting the first match.
The first-party JSON Schema, Avro and Arrow providers preserve supported declarations.
Arrow is one binary IPC Schema message and has no inline JSON form in Message metadata.
Messages reference Arrow documents instead.

Shared root metadata, model and capabilities are also native documents. Metadata and its
epoch come from one committed generation; hidden Versions are excluded. Oversized documents
use pinned subtree/leaf snapshot reads rather than a mandatory JSON fallback.

## Federation and Part 14

`Opc.Ua.EndpointRegistry.Federation` offers explicit UA and authorized HTTPS preloads.
Configured trust, observed application/root/type/ownership and collection-qualified identity
determine acceptance, not client-authored trust flags. Group snapshots retain native records
and actual browsed UA targets. Raw metadata must agree with its independent observed epoch.
Pure Message resolution reads verified cached evidence and never initiates network access.

`Opc.Ua.EndpointRegistry.PubSub` binds to the existing PubSub configuration/address-space
view. It invalidates References before retirement and republishes associations after activation.
It surfaces read-only provider-owned entries without automatic transport configuration.
Remote observations require authenticated publisher identity and explicit authorization;
retained expiry/replay handling does not invent native targets.

## Scope and conformance

Implementation is still undergoing its final conformance pass.
`tools\registry-conformance.json` records unclaimed facets and remaining prerequisite gaps.
In particular, the inherited schema creation surface and complete schema Version metadata/
compatibility semantics must be implemented before claiming Schema Native Writable.
Automatic schema materialization, an inbound xRegistry HTTP server and the Schema Server/Full
facets remain outside the approved scope. Imported models and passing tests alone are not
conformance claims.

See [the standalone registry samples](../samples/Registry/README.md) for encrypted TCP,
durable restart, direct/DI clients and the opt-in existing PubSub client entry point.
