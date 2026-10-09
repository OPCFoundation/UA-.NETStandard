# OPC UA Schema Registry Client

Native exact schema access with generated OPC 30455 structures. Depends only on
model/client modules, never server or format-provider implementations.

Use `SchemaRegistryClient.DiscoverAsync(session, telemetry)` or construct directly
(TypedSchemas is discovered lazily). With DI, call `AddSchemaRegistryClient()` and
resolve `SchemaRegistryClientFactory`. Sessions remain application-owned.

`ReadSchemaAsync` selects an exact `SchemaReferenceDataType`; `WriteSchemaAsync`
uses configured source identities. Both return typed StatusCode/Issues diagnostics.
Always check these before using Document. Service errors propagate.
`GetSchemaAsync` returns opaque ByteString octets and format/content type, never
parsing content; an ambiguous fingerprint is an error, not a guessed Version.

`OpenSnapshotAsync` opens a `DocumentKind = "schema"` Version lease using advertised
limits. Dispose it asynchronously before closing the session. No dynamic schema
registration or format inference is invented. See `samples\Registry`.
