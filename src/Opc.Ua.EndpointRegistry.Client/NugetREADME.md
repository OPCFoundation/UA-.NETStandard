# OPC UA Endpoint Registry Client

Session-bound native access to the OPC 30455 Endpoint Registry and its separate
Media root. Depends only on the model and OPC UA client libraries, not server or
format-provider implementations.

Use `EndpointRegistryClient.DiscoverAsync(session, telemetry, media: false)` or
construct directly when the well-known nodes are known. Register
`AddEndpointRegistryClient()` with DI and obtain `EndpointRegistryClientFactory`.
The caller owns the connected session. Generated codecs are registered automatically.

`Canonicalize` accepts application-built generated records. Supply
`ISchemaFormatProvider` implementations only for inline schemas; schema references
need no provider. Read, write, changes and resolution return generated result
structures: **check StatusCode and Issues**, not just the OPC UA Call service status.
Service failures propagate as exceptions.

`OpenSnapshotAsync` reads advertised bounds and returns an asynchronous lease from
`RegistrySnapshotClient`. Dispose the lease before disposing the session.
No JSON, payload parsing, broker connection or automatic PubSub configuration occurs.
See `samples\Registry` for an authenticated, durable TCP workflow.
