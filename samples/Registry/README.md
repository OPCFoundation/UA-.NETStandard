# Native OPC 30455 registry samples

These standalone executables demonstrate the native registry stack over actual
OPC UA TCP. They do not modify the reference client/server defaults. No broker
connection, payload parsing, automatic PubSub configuration, or network schema
fetch is performed.

## Run

From the repository root, using Debug and .NET 10:

```powershell
dotnet run --project samples\Registry\RegistryServer -f net10.0 -- --help
dotnet run --project samples\Registry\RegistryClient -f net10.0 -- --help
```

Supply your own **development-only** credentials. Neither executable contains
a default password. The NUnit fixture alone uses `sysadmin`/`demo`.

```powershell
$env:REGISTRY_PASSWORD = '<your local development password>'
dotnet run --project samples\Registry\RegistryServer -f net10.0 -- `
  --user registry-admin --schema --auto-accept `
  --state .\registry-data\state --pki .\registry-data\server-pki
```

In another terminal with the same password environment variable:

```powershell
dotnet run --project samples\Registry\RegistryClient -f net10.0 -- `
  --endpoint opc.tcp://localhost:62555/Registry --user registry-admin `
  --schema --auto-accept --once --pki .\registry-data\client-pki
```

`--password-env <name>` selects a different environment variable. Certificates
are generated in the selected local PKI directory. **Auto-accept is opt-in and
unsafe for production**: normally exchange and trust certificates before use.
The server advertises encrypted endpoints only, validates the supplied username
and password, and maps that identity to ConfigureAdmin through the host's role
configuration. A production deployment must replace the sample verifier with
its identity/secret infrastructure and enforce its trust policy.

## Bounded execution and restart

`RegistryClient` always performs one workflow, bounded by `--timeout` (60 seconds
by default). `--once` is accepted explicitly for automation.

`RegistryServer --once --seconds 30` opens the listener, reports readiness, and
stops after the selected interval. `--self-test --seconds 60` starts the listener,
runs the authenticated native TCP demo, then stops. Use `--auto-accept` only in
an isolated development fixture, including self-tests.

Stop the server with Ctrl+C, restart it with exactly the same state/PKI options,
and run the client with `--verify-only`. That mode performs no writes and verifies
the durable native documents and exact schema Version survived the restart.
Client writes use the reserved sample IDs `sample`, `temperature`, `base`, and
`derived`; run against a dedicated catalog, not a shared production registry.
Repeated demonstrations reuse existing Endpoint/Group documents and guard typed
changes with the observed epoch, rather than replacing server-owned metadata.

The server owns and asynchronously disposes three independent single-writer
`FileRegistryStateStore` directories: `endpoints`, `media`, and (with `--schema`)
`schemas`. They must not be shared by concurrently running servers. Supplied
stores are application-owned and released **after** server shutdown; no store
lock is leaked on normal restart. Local-file staged atomic state protects process
interruption, not arbitrary power loss or clustered/shared-folder operation.

## Native workflow

* Discover the generic well-known root and its `TypedAccess`, separately discover
  Media, and verify they are not aliases.
* Create a generated MQTT Endpoint and a reusable Message Group with native
  records (`PresentFields`, `ArrayOf<T>`); canonicalize without format packages.
* Read generated protocol options and a Message schema reference, without parsing
  either transport payloads or JSON metadata.
* Apply a typed field update guarded by the Group's epoch.
* Traverse a 48 KiB String (including a supplementary Unicode scalar) through
  a pinned `RegistrySnapshotClient` lease. The server advertises 8 KiB response
  bounds, so the String genuinely spans multiple responses.
* Resolve a derived Message's local base metadata and inherited content type.
* With `--schema`, register an unconfigured namespace/subject and an exact native
  JSON Schema boolean document, explicitly bind its logical ResourceUri/default,
  update it through exact `WriteSchema`, obtain opaque octets with `GetSchema`,
  and read its binary fingerprint through a native schema snapshot.

The schema example uses generated `TypedSchemas.RegisterSchema` with explicit
namespace and subject identities (`RegistryDemo.SchemaNamespace`, `SchemaName`);
the host has no preconfigured namespace/subject dictionaries. `ResourceUri` and
`EntityUri` are authored independently, not inferred by URI stripping.
The client also exposes `BeginSchemaUploadAsync` for Session-bound opaque uploads,
which are validated and published by closing the returned FileType handle.
Generated model contracts remain authoritative. The reusable clients do not
reference Server or Formats; providers are only necessary when an application
asks the Endpoint catalog to canonicalize an **inline** schema.

Native operations have two error layers: OPC UA service failures throw, while
successful Calls can return a bad typed `StatusCode` with `Issues`. The client
libraries preserve these results, and the demo explicitly checks both layers.
Snapshot leases are closed asynchronously before the session is disposed.

## DI and direct construction

```csharp
services.AddOpcUa();
services.AddEndpointRegistryClient().AddSchemaRegistryClient();

EndpointRegistryClientFactory factory =
    provider.GetRequiredService<EndpointRegistryClientFactory>();
EndpointRegistryClient client = await factory.DiscoverAsync(session);
EndpointRegistryClient media = new(session, telemetry, media: true);
SchemaRegistryClient schemas = new(session, telemetry);
```

Factories are registered idempotently; clients never own the connected session.
Library discovery uses namespace URIs and browse paths, not fixed session indices.
Selecting Media or an absent optional Schema root never falls back to another root.

## Optional PubSub entry point

The existing PubSub executable accepts this **registry-only** prefix:

```powershell
dotnet run --project samples\PubSub\ConsoleReferencePubSubClient -- `
  --registry-client --endpoint opc.tcp://localhost:62555/Registry `
  --user registry-admin --auto-accept --once
```

An explicit endpoint is required. All remaining arguments go to `RegistryClient`
(`--registry-client --help` prints its options). This path does not initialize
any PubSub application or use the separately developed EndpointRegistry.PubSub
binding APIs. Publisher/subscriber/external defaults are unchanged.

## Validation

```powershell
dotnet build src\Opc.Ua.EndpointRegistry.Client\Opc.Ua.EndpointRegistry.Client.csproj -nologo -v:q
dotnet build src\Opc.Ua.SchemaRegistry.Client\Opc.Ua.SchemaRegistry.Client.csproj -nologo -v:q
dotnet build samples\Registry\RegistryServer\RegistryServer.csproj -nologo -v:q
dotnet build samples\Registry\RegistryClient\RegistryClient.csproj -nologo -v:q
dotnet test tests\Opc.Ua.Registry.Samples.Tests\Opc.Ua.Registry.Samples.Tests.csproj -f net10.0 -p:CustomTestTarget=net10.0
dotnet test tests\Opc.Ua.Registry.Samples.Tests\Opc.Ua.Registry.Samples.Tests.csproj -f net48 -p:CustomTestTarget=net48
```

Tests use the executable hosting/client entry points over SignAndEncrypt TCP,
assert typed values and multi-part snapshots, restart the file-backed server,
exercise direct and DI clients, and verify stale-epoch diagnostics and absent
Schema behavior. Test state and certificates are unique to each fixture.
