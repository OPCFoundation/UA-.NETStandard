# OPC UA Schema Registry Server

Hosts the optional OPC 30451 in-server `SchemaRegistry` root. The module is opt-in and does not activate when Endpoint Registry hosting is configured.

The host provides typed schema access, exact-Version FileType downloads, `GetSchema`, and the
Opaque SchemaId lookup through the shared xRegistry fast-path manager. JSON Schema, Avro and
Arrow use the first-party schema format providers. Native snapshots pin the selected schema and
registry revisions and can traverse a schema larger than one response.

`SchemaRegistryStore` persists all exact bytes, fingerprints, Version epochs and explicit default
selections through `IRegistryStateStore`. A typed no-op retains the original bytes; an exact-Version
read never falls back to a default or another format. A failed pre-commit check changes nothing.
A projection failure after a durable commit is reported as uncertain, not as a failed no-op.

The application configures each Group's `NamespaceUri` and each Resource's `SchemaName`. The host
checks their one-way symbolic identifiers rather than trying to recover a subject from an identifier
or fingerprint. Native writes select these provider-owned subjects. Dynamic source-identity
registration and the raw upload lifecycle are not yet provided by this module.

Mutations require SignAndEncrypt and ConfigureAdmin or SecurityAdmin by default. Per-Version
visibility applies to typed reads, FileType content and both legacy fingerprint lookups. Hidden
Versions are excluded before ambiguity is evaluated.

The application owns a supplied state store. For single-writer restart-safe storage, supply
`FileRegistryStateStore`; the default in-memory store is owned by the node manager.

No Server or Full facet is claimed: automatic materialization, an inbound HTTP xRegistry API,
TTL mirroring and PubSub schema ownership are not implied by hosting this catalog.
