# OPC UA REST Binding (Part 6 §G.3 "OpenAPI Mapping")

The OPC UA REST binding exposes the OPC UA service set as ASP.NET
Core **Minimal-API endpoints**, implementing the **OpenAPI Mapping**
defined in [OPC UA Part 6 §G.3](https://reference.opcfoundation.org/specs/OPC-10000-6/g-3)
(v1.05.07).

The binding ships as part of
`OPCFoundation.NetStandard.Opc.Ua.Bindings.Https` (net8+ only;
mounted on the same Kestrel host as the binary and
`application/opcua+uajson` sub-profiles).

## Contents

- [Quick start](#quick-start)
- [Routes (full coverage)](#routes-full-coverage)
- [Encoding negotiation](#encoding-negotiation)
- [Discovery](#discovery)
- [Wire format](#wire-format)
- [Authentication](#authentication)
  - [JWT claim projection (built-in)](#jwt-claim-projection-built-in)
- [Hosting modes](#hosting-modes)
- [Long-poll `/publish`](#long-poll-publish)
- [Client integration](#client-integration)
- [Client errors](#client-errors)
- [Compressed responses](#compressed-responses)
- [Related plans and follow-ups](#related-plans-and-follow-ups)

- **Server side**: ASP.NET Core Minimal-API endpoints (one `MapPost`
  per spec service) — **NativeAOT-compatible**; no MVC reflection, no
  `[UnconditionalSuppressMessage]` attributes.
- **Client side**: symmetric `IWebApiClient` in
  `OPCFoundation.NetStandard.Opc.Ua.Client` under
  `src/Opc.Ua.Client/WebApi/`.

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
`WebApiServiceRoutes` in `src/Opc.Ua.Core/Stack/WebApi/`.

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
> `ManagedSessionBuilder.UseWssOpenApiEndpoint(url, encoding)`. The bearer
> token is encoded in the selected sub-protocol name
> (`opcua+openapi+<accesstoken>`) because browser WebSocket APIs forbid
> custom HTTP request headers.

> **Security considerations for the bearer-prefix sub-protocol:**
> the WebSocket spec requires the server to echo the selected
> sub-protocol back to the client in the 101 handshake. The bearer
> token therefore appears in plain text in:
> 1. The 101 response on the wire (rejected by the client when the
>    URL scheme is not `wss://`; rejected by the server when the
>    request is not `IsHttps`).
> 2. The server's Kestrel access log (`Sec-WebSocket-Protocol` header).
> 3. Any HTTP proxy / WAF / load-balancer log on the path.

> Operators **must** redact the `Sec-WebSocket-Protocol` header from
> logs and **should** use short-lived tokens (≤ 60 s TTL) so a
> captured token expires before it can be replayed. The server also
> requires `AddWebApiBearerAuth(...)` to be registered: without a
> bearer validator the listener fail-closed rejects every
> bearer-prefix upgrade rather than echo the token back.

The persistent WSS client multiplexes requests with connection-local wire
handles while preserving caller-visible handles. See [Transports](Transports.md)
for cancellation, late-response routing and connection-lifetime behavior.

## Wire format

The body is the bare `<Service>Request` / `<Service>Response` object —
**no `{UaTypeId, UaBody}` envelope** at the HTTPS layer. Body
serialization uses the stack's existing `JsonEncoder` /
`JsonDecoder` driven by `WebApiBodyCodec`. The encoder shape matches
the spec's component schemas property-for-property; see
`WebApiEncoderConformanceTests` in
`tests/Opc.Ua.Core.Tests/Stack/WebApi/`.

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

The `WebApiHttpsStartupContributor` automatically inserts
`app.UseAuthentication()` between `UseRouting()` and `UseEndpoints()`
whenever at least one auth scheme is registered (detected via
`IOptions<AuthenticationOptions>.Schemes`), so the
`HttpContext.User` is populated before the dispatcher invokes the
sessionless identity provider. Bindings registered without any
`AddWebApi*Auth()` call skip the authentication middleware entirely
to preserve the historical anonymous request flow.

The default `ISessionlessIdentityProvider` is intentionally
conservative; register a custom implementation to map richer
claim sets (e.g. JWT role claims → OPC UA roles).

### JWT claim projection (built-in)

For services configured with `Microsoft.AspNetCore.Authentication.JwtBearer`,
the binding ships a built-in identity provider that projects the
authenticated JWT principal onto an OPC UA `IUserIdentity`:

```csharp
services.AddOpcUa()
    .AddHttpsTransport()
    .AddWebApiTransport()
    .AddWebApiBearerAuth(opt => { opt.Authority = "..."; })
    .UseJwtClaimIdentityProvider(opt =>
    {
        opt.SubjectClaim = "sub";    // default
        opt.ScopeClaim = "scope";    // default — space- or comma-separated
        opt.RolesClaim = "roles";    // default — also picks up ClaimTypes.Role
        opt.ReturnAnonymousForUnauthenticated = false; // default
        opt.TransformIdentity = (identity, ctx) => identity; // optional hook
    });
```

The provider builds a `UserIdentity` carrying the raw bearer token as
an `IssuedIdentityToken` (`Profiles.JwtUserToken`) so server-side
authenticators can re-validate the JWT downstream, and exposes a
projection of the subject / scopes / roles via the static helpers
`JwtClaimSessionlessIdentityProvider.GetSubject(IUserIdentity)`,
`GetScopes(IUserIdentity)`, and `GetRoles(IUserIdentity)` for
role-based-access lookups. Scopes accept either the single
space-separated OAuth 2.0 form or repeated `scope` claims; roles
accept comma/semicolon/space-separated values plus
`ClaimTypes.Role` mappings.

## Hosting modes

```csharp
services.AddWebApiTransport(opt =>
{
    opt.HostingMode = WebApiHostingMode.SharedWithHttpsListener; // default
    opt.DefaultEncoding = WebApiEncoding.Compact;                // spec default
});
```

- **`SharedWithHttpsListener`** (default) — Minimal-API endpoints
  mount into the existing `HttpsTransportListener` Kestrel pipeline
  via the internal `IHttpsListenerStartupContributor` hook. Single
  port for binary / `opcua+uajson` / REST.

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

## Client integration

The OPC Foundation publishes reference WebApi clients for several
ecosystems:

- TypeScript: <https://github.com/OPCFoundation/opcua-webapi-typescript>
- Python: <https://github.com/OPCFoundation/opcua-webapi-python>
- .NET: <https://github.com/OPCFoundation/opcua-webapi-dotnet>

On the .NET stack this binding's REST surface is wired into
`ManagedSession` via the fluent
`ManagedSessionBuilder.UseWebApiEndpoint(url, encoding)` shortcut, so
applications open and operate a WebApi session through the same
`ISession` / subscription / monitored-item API that the binary
transport uses:

```csharp
using Opc.Ua.Client;

await using ManagedSession session = await new ManagedSessionBuilder(telemetry)
    .UseWebApiEndpoint("https://server:4843/")
    .WithSessionName("Aot-Sample-Client")
    .WithUserIdentity(new UserIdentity())
    .StartAsync(ct)
    .ConfigureAwait(false);

ReadResponse response = await session.ReadAsync(new ReadRequest
{
    RequestHeader = new RequestHeader { TimeoutHint = 10000 },
    NodesToRead = new ArrayOf<ReadValueId>(/* … */)
}, ct).ConfigureAwait(false);
```

The companion `UseWssOpenApiEndpoint(url)` shortcut binds the same
session model to the WebSocket `opcua+openapi` sub-protocol.

## Client errors

Errors of a processed request come back in the OPC UA response
(`ResponseHeader.ServiceResult`, HTTP 200). An HTTP error status alone
does not tell whether the service ran. The WebApi server of this stack
sends 400 (undecodable body), 401 and 403 (authentication and
authorization), 404 and 405 (no matching route), 413 (request body over
the limit) and 429 (rate limiting) before it invokes the service. For
any other status the outcome is unknown: the server answers 500 when it
cannot encode the response of a service that already ran, and a proxy
or gateway can answer 502, 503 or 504 after the server executed the
request. A status from a proxy or another server implementation tells
only what that component reports. Retry a service that changes state,
such as Write or Call, only when repeating it is harmless.
`WebApiClient` (and therefore `ManagedSession` over
`UseWebApiEndpoint`) reports the HTTP error as a
`ServiceResultException`:

| HTTP | StatusCode |
| --- | --- |
| 400 | `Bad_DecodingError` (the server could not decode the body) |
| 401 | `Bad_IdentityTokenInvalid` when the client sent no credentials, `Bad_UserAccessDenied` when it did |
| 403 | `Bad_UserAccessDenied` |
| 404, 405, 501 | `Bad_ServiceUnsupported` (the route is not mapped) |
| 408, 504 | `Bad_Timeout` |
| 413 | `Bad_RequestTooLarge` |
| 415 | `Bad_DataEncodingUnsupported` |
| 429, 503 | `Bad_ServerTooBusy`, with a `Retry-After` header as `RetryAfterMs=<n>` in `AdditionalInfo` |
| 500 | `Bad_InternalError` |
| any other | `Bad_CommunicationError` |

The message names the HTTP status, the reason phrase and the route, and
the inner exception is an `HttpRequestException` (on .NET 5 and later
with its `StatusCode` set). An elapsed `RequestTimeout` or
`HttpClient.Timeout` is `Bad_RequestTimeout`. A request that fails below
HTTP (no connection, failed TLS handshake) gets the StatusCode the HTTPS
transport channel reports for the same failure, for example
`Bad_NotConnected` for a refused connection. Cancelling the caller's
token still throws `OperationCanceledException`.

## Compressed responses

OPC 10000-6 §7.4.5 lets JSON messages be compressed with gzip (IETF RFC
1952), announced by `Content-Encoding: gzip`. On the client,
`WebApiClientOptions.AcceptCompressedResponses = true` sends
`Accept-Encoding: gzip` (on a shared `HttpClient` per request, without
changing its default headers):

```csharp
using WebApiClient client = WebApiClient.Create(
    new Uri("https://server:4843/"),
    new WebApiClientOptions { AcceptCompressedResponses = true });
```

A gzip response is inflated whether it was asked for or not. The
`MaxMessageSize` limit applies to the inflated body, a corrupt gzip body
is `Bad_DecodingError`, and a content coding other than gzip or identity
is rejected with `Bad_DecodingError`. The HTTPS binary channel reads its
responses through the same code.

## Related plans and follow-ups

- **Source-generated OpenAPI document** (deferred) — the spec's
  `opc.ua.openapi.allservices.json` document and a runtime `/openapi/v1.json`
  endpoint will land in a future PR; the current binding produces
  spec-shaped JSON bodies without needing the document itself.
