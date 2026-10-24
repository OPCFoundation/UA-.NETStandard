# OPC UA Schema Registry Client

Native exact schema access with generated OPC 30451 structures. Depends only on
model/client modules, never server or format-provider implementations.

Use `SchemaRegistryClient.DiscoverAsync(session, telemetry)` or construct directly
(TypedSchemas is discovered lazily). With DI, call `AddSchemaRegistryClient()` and
resolve `SchemaRegistryClientFactory`. Sessions remain application-owned.

`RegisterSchemaAsync` admits explicit namespace/subject identities with native
content; set an explicit ResourceUri when MakeDefault binds a logical URI.
`BeginSchemaUploadAsync` returns a Session-bound FileType/handle for opaque
ByteString writes and validated publication on Close. The caller owns that handle;
this client does not parse or alter uploaded bytes.

`ReadSchemaAsync` selects an exact `SchemaReferenceDataType`; `WriteSchemaAsync`
updates an admitted exact reference. These return typed StatusCode/Issues diagnostics.
When changing content, omit the old SchemaIdAlg/SchemaId pair from the write
reference (or supply a claim for the new content); use ExpectedEpoch to guard
against stale writes. A retained old fingerprint correctly fails validation.
Always check these before using Document. Service errors propagate.
`GetSchemaAsync` returns opaque ByteString octets and format/content type, never
parsing content; an ambiguous fingerprint is an error, not a guessed Version.

`OpenSnapshotAsync` opens a `DocumentKind = "schema"` Version lease using advertised
limits. Dispose it asynchronously before closing the session. No source identity
or format is inferred. See `samples\Registry`.
