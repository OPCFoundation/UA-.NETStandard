# OPC UA REST Binding (Part 6 §G.3 "OpenAPI Mapping")

The OPC UA REST binding exposes the OPC UA service set as an ASP.NET
Core MVC controller surface, implementing the **OpenAPI Mapping**
defined in [OPC UA Part 6 §G.3](https://reference.opcfoundation.org/specs/OPC-10000-6/g-3)
(v1.05.07).

The binding ships as
`OPCFoundation.NetStandard.Opc.Ua.Bindings.WebApi` and complements (does
not replace) the binary and `application/opcua+uajson` sub-profiles
already provided by `OPCFoundation.NetStandard.Opc.Ua.Bindings.Https`.

- **Server side**: ASP.NET Core controllers serving every spec route.
- **Client side**: symmetric `IWebApiClient` in
  `OPCFoundation.NetStandard.Opc.Ua.Client` under
  `Libraries/Opc.Ua.Client/WebApi/`.

## Quick start

```csharp
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua;
using Opc.Ua.Bindings;

services
    .AddOpcUa()
    .AddHttpsTransport()        // existing binary + opcua+uajson
    .AddWebApiTransport()      // adds REST routes alongside them
    .AddWebApiAnonymousAuth(); // or AddWebApiBearerAuth(...) etc.
```

The REST surface mounts inside the existing `HttpsTransportListener`
Kestrel pipeline, so a single port serves binary,
`application/opcua+uajson`, **and** REST traffic without configuring a
second listener.

A spec-shaped client request:

```bash
curl -X POST https://server:4843/read \
     -H 'Content-Type: application/json' \
     --data '{
       "RequestHeader": { "TimeoutHint": 10000 },
       "TimestampsToReturn": 2,
       "NodesToRead": [
         { "NodeId": "i=2258", "AttributeId": 13 }
       ]
     }'
```

returns the matching `ReadResponse` body. Add
`?encoding=verbose` to the `Content-Type` (or `Accept`) to opt into the
Verbose JSON flavour.

## Routes (full coverage)

The binding implements every service in the spec's
[`opc.ua.openapi.allservices.json`](https://github.com/OPCFoundation/UA-Nodeset/blob/latest/Schema/opc.ua.openapi.allservices.json)
document — 28 services across the Discovery, Session, View, Attribute,
Method, MonitoredItem, and Subscription service sets. NodeManagement
(Part 4 §5.8) and Query (§5.10) are **not in scope** — the spec
document deliberately omits them.

| Service set (Part 4) | Routes |
| --- | --- |
| Discovery (§5.5) | `/findservers`, `/getendpoints` |
| Session (§5.7) | `/createsession`, `/activatesession`, `/closesession`, `/cancel` |
| View (§5.9) | `/browse`, `/browsenext`, `/translate`, `/registernodes`, `/unregisternodes` |
| Attribute (§5.11) | `/read`, `/write`, `/historyread`, `/historyupdate` |
| Method (§5.12) | `/call` |
| MonitoredItem (§5.13) | `/createmonitoreditems`, `/modifymonitoreditems`, `/setmonitoringmode`, `/settriggering`, `/deletemonitoreditems` |
| Subscription (§5.14) | `/createsubscription`, `/modifysubscription`, `/setpublishingmode`, `/publish`, `/republish`, `/transfersubscriptions`, `/deletesubscriptions` |

All routes are `POST` with a JSON body holding the matching
`<Service>Request`; the response body is the matching
`<Service>Response`. The route table is the source of truth — see
`WebApiServiceRoutes` in `Stack/Opc.Ua.Core/Stack/WebApi/`.

## Encoding negotiation

OPC UA Part 6 §5.4 defines two JSON flavours: **Compact** (default,
mandatory) and **Verbose**. The REST binding selects between them via
the `encoding` media-type parameter:

| Request `Content-Type` | Server response `Content-Type` |
| --- | --- |
| `application/json` | `application/json; encoding=compact` |
| `application/json; encoding=compact` | `application/json; encoding=compact` |
| `application/json; encoding=verbose` | `application/json; encoding=verbose` |
| `application/json; encoding=verbose` + `Accept: application/json; encoding=compact` | `application/json; encoding=compact` |

The `Accept` header takes precedence for the response encoding; the
`Content-Type` header governs how the inbound body is decoded. Unknown
`encoding` parameter values fall back to **Compact**.

## Discovery

When the binding is registered, the `HttpsServiceHost` emits a
discovery-only twin per `SecurityMode.None` HTTPS-binary endpoint with:

- `TransportProfileUri = http://opcfoundation.org/UA-Profile/Transport/https-uajson-openapi` (OPC Foundation [profile/2338](https://profiles.opcfoundation.org/profile/2338); surfaced via `Profiles.HttpsOpenApiTransport`)
- `SecurityMode = None` (TLS provides transport security)
- `SecurityPolicyUri = None`
- `ServerCertificate` and `UserIdentityTokens` copied from the
  companion HTTPS-binary description so clients can pick a compatible
  identity at activate time.

Clients discover the binding through `GetEndpoints` and resolve it by
the `TransportProfileUri` — the synthetic registry-key scheme
`opc.https+webapi` (`Utils.UriSchemeOpcHttpsWebApi`) maps the profile
to the `WebApiTransportChannelFactory` while the wire-level URL stays
`https://...`. The fluent shortcut
`ManagedSessionBuilder.UseWebApiEndpoint(url)` constructs the
endpoint description with all of these fields pre-populated.

> The companion WebSocket sub-profile
> `Profiles.WssOpenApiTransport` (OPC Foundation [profile/2339](https://profiles.opcfoundation.org/profile/2339))
> is implemented as a peer to the HTTPS surface:
> `WebApiWssTransportChannel` on the client side,
> `HttpsTransportListener.AcceptWebSocketOpenApiAsync` on the server
> side. Fluent shortcut:
> `ManagedSessionBuilder.UseWssOpenApiEndpoint(url, encoding)`. Bearer
> tokens ride in the sub-protocol name
> (`opcua+openapi+<accesstoken>`) because browser WebSocket APIs forbid
> custom HTTP request headers.

## Wire format

The body is the bare `<Service>Request` / `<Service>Response` object —
**no `{UaTypeId, UaBody}` envelope** at the HTTPS layer. Body
serialization uses the stack's existing `JsonEncoder` /
`JsonDecoder` driven by `WebApiBodyCodec`. The encoder shape matches
the spec's component schemas property-for-property; see
`WebApiEncoderConformanceTests` in
`Tests/Opc.Ua.Core.Tests/Stack/WebApi/`.

## Authentication

Four auth modes are pluggable via DI:

```csharp
services.AddOpcUa()
    .AddHttpsTransport()
    .AddWebApiTransport()
    .AddWebApiAnonymousAuth()                                      // baseline
    .AddWebApiBearerAuth(opt => { opt.Authority = "..."; })        // JWT
    .AddWebApiBasicAuth(async (user, password) => { /* ... */ })   // RFC 7617
    .AddWebApiMutualTlsAuth(opt => { opt.AllowedCertificateTypes = ... });
```

- **Anonymous** — no `Authorization` header; identity =
  `AnonymousIdentityToken`.
- **Bearer JWT** — standard
  `Microsoft.AspNetCore.Authentication.JwtBearer` middleware; the JWT
  flows into the OPC UA dispatcher through
  `ISessionlessIdentityProvider`.
- **HTTP Basic** — in-package
  `BasicAuthenticationHandler`. Rejects requests over plain HTTP by
  default (`Options.RequireHttps = true`).
- **Mutual TLS** —
  `Microsoft.AspNetCore.Authentication.Certificate` against the
  client cert presented to Kestrel. Kestrel itself must be configured
  to request client certificates (the existing
  `HttpsSettings.HttpsMutualTls` flag is the supported path).

For sessionless services (Read/Write/Browse/…) the resolved
`IUserIdentity` is attached to the
`WebApiInvocationContext.Identity` and flows into the
dispatcher's role-based-access pipeline through
`ISessionlessIdentityProvider`. For session-based services the
client is responsible for calling `CreateSession` + `ActivateSession`
with credentials in the request body — the binding does not
double-authenticate.

The default `ISessionlessIdentityProvider` is intentionally
conservative; register a custom implementation to map richer
claim sets (e.g. JWT role claims → OPC UA roles).

## Hosting modes

```csharp
services.AddWebApiTransport(opt =>
{
    opt.HostingMode = WebApiHostingMode.SharedWithHttpsListener; // default
    opt.DefaultEncoding = WebApiEncoding.Compact;                // spec default
});
```

- **`SharedWithHttpsListener`** (default) — controllers mount into the
  existing `HttpsTransportListener` Kestrel pipeline via the internal
  `IHttpsListenerStartupContributor` hook. Single port for
  binary / `opcua+uajson` / REST.
- **`OwnListener`** — reserved for a follow-up PR; the API surface is
  in place (`opt.UseOwnListener(...)`) but the standalone listener
  implementation is deferred.

## Long-poll `/publish`

`PublishRequest`/`PublishResponse` flow through the same dispatcher
the binary path uses. The server side awaits the next notification
(or the request's `RequestHeader.TimeoutHint`) without blocking a
thread. Kestrel timeouts must exceed the largest expected
`TimeoutHint`:

```csharp
webHostBuilder.UseKestrel(o =>
{
    o.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(5);
    o.Limits.RequestBodyTimeout = TimeSpan.FromMinutes(5);
});
```

On the client, set `WebApiClientOptions.RequestTimeout =
Timeout.InfiniteTimeSpan` for the publish endpoint so the server-side
`TimeoutHint` governs cancellation.

## Symmetric C# client

```csharp
using Opc.Ua.Client.WebApi;

using var client = WebApiClient.Create(
    new Uri("https://server:4843/"),
    new WebApiClientOptions { Encoding = WebApiEncoding.Compact });

ReadResponse response = await client.ReadAsync(new ReadRequest
{
    RequestHeader = new RequestHeader { TimeoutHint = 10000 },
    NodesToRead = new ArrayOf<ReadValueId>(/* … */)
});
```

The client lives in `OPCFoundation.NetStandard.Opc.Ua.Client` under
`Libraries/Opc.Ua.Client/WebApi/`. It is multi-TFM (net48 /
netstandard2.1 / net8+) — the server binding is net8+ only because of
its dependency on `Microsoft.AspNetCore.OpenApi` and modern MVC
infrastructure, but the client only needs `HttpClient`.

`IWebApiClient` exposes one strongly-typed method per service plus a
generic `InvokeAsync<TRequest, TResponse>` that resolves the route
through the same table the controllers use.

## Tooling notes

- **No Swashbuckle.** If a runtime OpenAPI document is added in a
  follow-up, it will be served by
  `Microsoft.AspNetCore.OpenApi` (`app.MapOpenApi()`); Swashbuckle is
  explicitly excluded from the binding.
- **TFM constraint.** `Opc.Ua.Bindings.WebApi` targets `net8.0`,
  `net9.0`, `net10.0` only. The package mirrors
  `Opc.Ua.Bindings.Kestrel.Tcp`'s `RestrictForLegacyTfm` shell so it
  builds as an empty assembly on legacy CI matrix runs without
  pulling in net8+ APIs.
- **AOT.** The binding is *not* Native-AOT compatible because MVC
  controllers and `Microsoft.AspNetCore.OpenApi` rely on reflection
  that is not trim-safe.

## Related plans and follow-ups

- [`plans/25-wss-openapi-subprotocols.md`](../plans/25-wss-openapi-subprotocols.md)
  — WSS `opcua+openapi` / `opcua+openapi+<accesstoken>` sub-protocols
  reuse the codec / routing / dispatcher plumbing from this binding.
- **Source-generated OpenAPI document** (deferred) — the spec's
  `opc.ua.openapi.allservices.json` document and a runtime `/openapi/v1.json`
  endpoint will land in a future PR; the current binding produces
  spec-shaped JSON bodies without needing the document itself.
- **Own-listener mode** (deferred) — `WebApiTransportOptions.UseOwnListener`
  reserves the API surface; the standalone listener implementation
  ships in a follow-up.
