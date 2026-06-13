# Plan — WebSocket `opcua+openapi` / `opcua+openapi+<accesstoken>` sub-protocols

## Status

**Proposed.** Surfaced from PR feedback on
[#3880](https://github.com/OPCFoundation/UA-.NETStandard/pull/3880)
(`Docs/Profiles.md` review comment on line 110: *"Plan this work and
add the plan to plans folder"*).

## Problem

Part 6 §7.5.2 Table 81 defines two WebSocket sub-protocols beyond the
ones currently shipping:

| Sub-protocol | Wire | Auth model |
| --- | --- | --- |
| `opcua+uacp` | UASC binary | UA UserIdentity tokens negotiated inside the channel | ✅ supported |
| `opcua+uajson` | UA-JSON in WebSocket text frames | UA UserIdentity tokens | ✅ supported (Security Mode None) |
| `opcua+openapi` | OPC UA OpenAPI requests / responses (JSON) | Authentication out of band | ❌ not supported |
| `opcua+openapi+<accesstoken>` | OPC UA OpenAPI | Bearer-style access token negotiated in the sub-protocol name | ❌ not supported |

These sub-protocols are intended for thin web clients that prefer the
OPC UA OpenAPI HTTP-shape rather than UASC framing — most useful for
browser-based front-ends backed by a UA server.

## Goals

- Implement an opt-in WebSocket handler in
  `HttpsTransportListener.AcceptWebSocketAsync` that negotiates the
  `opcua+openapi` sub-protocol when offered.
- For `opcua+openapi+<accesstoken>`, parse the trailing access-token
  segment per Part 6 §7.5.2 and feed it through the same
  `IClientIdentityProvider` / `IUserTokenAuthenticator` pipeline used
  by the other transports (see [`Docs/IdentityProviders.md`](../Docs/IdentityProviders.md)).
- Provide a corresponding client-side `IUaSCByteTransport`-shaped
  surface (or, more likely, a dedicated `IOpenApiClient` since the
  OpenAPI flow is request/response over HTTPS, not UASC-shaped).
- Discovery emission: the OpenAPI sub-profiles get their own
  `TransportProfileUri` so clients can discover them via
  `GetEndpoints`.

## Non-goals

- Re-implementing the OPC UA OpenAPI surface itself — reuse the
  existing OpenAPI schema rendering (the OPC Foundation maintains the
  authoritative OpenAPI definition).
- Authentication models beyond what the OPC UA spec defines for
  OpenAPI.

## Open questions

1. Is the OpenAPI surface the
   [OPC 10000-100 (Devices) OpenAPI](https://reference.opcfoundation.org/Devices/v200/docs/)
   one, the
   [OPC 10000-200 (PADIM)](https://reference.opcfoundation.org/PADIM/v100/docs/)
   one, or the generic OPC UA OpenAPI work that's still in draft? The
   choice affects what schemas the OpenAPI handler must emit / accept.
2. Does the stack need to bundle the OpenAPI document, or is it
   declared by the application?
3. Should `opcua+openapi+<accesstoken>` integrate with the existing
   JWT / OAuth2 / OIDC plumbing in
   [`Docs/IdentityProviders.md`](../Docs/IdentityProviders.md), or
   stay as its own bearer-token path?
4. What is the right server-side request boundary — does the OpenAPI
   handler short-circuit to the UA Server API
   (`IServerInternal.Read`, `Browse`, …) directly, or does it go
   through the existing service-call dispatcher used by the binary /
   JSON transports?

## Acceptance

- Reference server exposes an `opcua+openapi` endpoint reachable from
  any standard HTTP client (curl, Postman, browser fetch).
- Bearer-token flow round-trips an authenticated user identity into
  the server's `Session`.
- New integration tests under
  `Tests/Opc.Ua.Sessions.Tests/WssOpenApi*IntegrationTests.cs`.
- `Docs/Profiles.md` and `Docs/Transports.md` updated to mark the
  sub-profiles as supported.

## Dependencies

- An OPC UA OpenAPI schema is required (see open question #1).
- Likely coordinates with the
  [transport binding registry + DI extensions plan](24-transport-binding-registry-and-di-extensions.md)
  so the new handler can be registered cleanly.

## Out of scope (file separately if needed)

- OPC UA REST / OpenAPI work that isn't gated on `opcua+openapi*` WS
  sub-protocols (a flat HTTPS OpenAPI surface without the WebSocket
  upgrade dance).
