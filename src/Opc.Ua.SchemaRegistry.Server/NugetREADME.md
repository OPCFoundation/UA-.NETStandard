# OPC UA Schema Registry Server

Hosts the optional OPC 30451 in-server `SchemaRegistry` root. The module is opt-in and does not activate when Endpoint Registry hosting is configured.

The host provides typed schema access, exact-Version FileType downloads, `GetSchema`, and the
Opaque SchemaId lookup through the shared xRegistry fast-path manager. JSON Schema, Avro and
Arrow use the first-party schema format providers. Native snapshots pin the selected schema and
registry revisions and can traverse a schema larger than one response.
`TypedAccess.ReadDocument` also returns complete generic or named root metadata, model and
capabilities records. These shared documents are pinned by `OpenDocument` and traversed with
the same snapshot Methods. Hidden Versions are excluded from metadata, and its contents and
epoch are captured from one committed store generation. Shared record descriptors reside in
xRegistry; Schema Registry has no dependency on the Endpoint Registry assembly.

`SchemaRegistryStore` persists all exact bytes, fingerprints, Version epochs and explicit default
selections through `IRegistryStateStore`. A typed no-op retains the original bytes; an exact-Version
read never falls back to a default or another format. A failed pre-commit check changes nothing.
A projection failure after a durable commit is reported as uncertain, not as a failed no-op.
An unchanged retry remains uncertain until a later successful activation or restart; it cannot
turn a pending projection into a successful response merely because the document is equal.

`TypedSchemas.RegisterSchema` admits an explicit `NamespaceUri`, `SchemaName`, format and
Version through one durable transaction. `MakeDefault` requires an independently authored
`ResourceUri`; no URI stripping or identifier inversion is performed. The host checks symbolic
identifiers against these source identities. Applications may alternatively preconfigure subjects.

`BeginSchemaUpload` returns a Session-owned temporary FileType write handle. Bytes are staged
within finite per-Session and aggregate bounds, then validated and atomically admitted on Close.
Another Session cannot close or write the handle; Session closure discards uncommitted uploads.

Mutations require SignAndEncrypt and ConfigureAdmin or SecurityAdmin by default. Per-Version
visibility applies to typed reads, FileType content and both legacy fingerprint lookups. Hidden
Versions are excluded before ambiguity is evaluated.
Pinned schema and metadata snapshots recheck access to every included Version before returning
a part; revocation releases the snapshot without exposing retained values. Session reactivation
also discards old snapshot/file/upload handles.

The application owns a supplied state store. For single-writer restart-safe storage, supply
`FileRegistryStateStore`; the default in-memory store is owned by the node manager.

No Server or Full facet is claimed: automatic materialization, an inbound HTTP xRegistry API,
TTL mirroring and PubSub schema ownership are not implied by hosting this catalog.
