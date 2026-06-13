# Transport Profiles

This document is the practical companion to [`Profiles.md`](Profiles.md) and
describes how the OPC UA .NET Standard stack ships each of the transport
profiles defined in OPC UA Part 6 §7. It is aimed at developers who need
to host or connect to OPC UA servers over something other than the
default `opc.tcp://` transport — for example to traverse firewalls or
to integrate with web-based tooling.

## Transport profile matrix

| Profile | URL scheme | Wire format | UA Secure Conversation | Security modes |
|---|---|---|---|---|
| `uatcp-uasc-uabinary` | `opc.tcp://` | UA Binary | yes | `None`, `Sign`, `SignAndEncrypt` |
| `https-uabinary` | `opc.https://`, `https://` | UA Binary in HTTP body (`application/octet-stream`) | yes (one chunk per POST) | `None`, `Sign`, `SignAndEncrypt` |
| `https-uajson` | `opc.https://`, `https://` | UA JSON in HTTP body (`application/opcua+uajson`) | **no** — TLS only | `None` only |
| `uawss-uasc-uabinary` | `opc.wss://`, `wss://` | UA Binary in WebSocket binary frame (sub-protocol `opcua+uacp`) | yes | `None`, `Sign`, `SignAndEncrypt` |
| `uawss-uajson` | `opc.wss://`, `wss://` | UA JSON in WebSocket text frame (sub-protocol `opcua+uajson`) | **no** — TLS only | `None` only |

The two JSON profiles do not negotiate a UA SecureChannel; transport
security is provided exclusively by the surrounding TLS connection.
Servers MUST advertise these endpoints with `MessageSecurityMode.None`
and `SecurityPolicyUri = None`. JSON encoding is always the *Compact*
(reversible) flavour mandated by Part 6 §5.4.9 — the stack uses
`JsonEncoderOptions.Compact` everywhere.

## Assembly layout

* **`Opc.Ua.Core`** (this is what every Server / Client application
  references):
  - `IUaSCByteTransport` — the runtime transport boundary used by the
    UA Secure Conversation channel pipeline.
  - `TcpByteTransport`, `TcpByteTransportFactory` — TCP implementation.
  - `TcpTransportChannel` + `TcpTransportChannelFactory` — client-side
    channel for `opc.tcp://`.
  - `HttpsTransportChannel` + `HttpsTransportChannelFactory`,
    `OpcHttpsTransportChannelFactory` — polymorphic client-side channel
    that handles both `https-uabinary` and `https-uajson` based on the
    endpoint's `TransportProfileUri`.
* **`Opc.Ua.Bindings.Https`** (Kestrel-dependent; pulled in
  automatically when an HTTPS or WSS endpoint is configured):
  - `HttpsTransportListener` — Kestrel-hosted listener that dispatches
    requests on `(Upgrade, ContentType)` to the binary / JSON / WSS
    handlers.
  - `HttpsTransportListenerFactory`, `OpcHttpsTransportListenerFactory`
    — listener factories for `https://` and `opc.https://`.
  - `WssTransportListenerFactory`, `OpcWssTransportListenerFactory` —
    listener factories for `wss://` and `opc.wss://`.
  - `WebSocketClientByteTransport` + `WebSocketClientByteTransportFactory`,
    `WssTransportChannel`, `WssTransportChannelFactory`,
    `OpcWssTransportChannelFactory` — client-side WSS+uacp.
  - `WssJsonTransportChannel`, `WssJsonTransportChannelFactory` —
    client-side WSS+uajson (per-request `ClientWebSocket`).
  - `JsonRequestMapper` — shared JSON encode / decode helper used by
    both server handlers.

For both WSS variants the binding assembly is loaded automatically the
first time the channel manager sees a `wss://` or `opc.wss://`
endpoint, via the `Utils.DefaultBindings` reflection-based loader.

## Server-side configuration

Servers declare endpoints via `ApplicationConfiguration` →
`ServerConfiguration` → `BaseAddresses`. Each base address yields a
single listener for its scheme.

```xml
<BaseAddresses>
  <ua:String>opc.tcp://localhost:62541/MyServer</ua:String>
  <ua:String>opc.https://localhost:62540/MyServer</ua:String>
  <ua:String>opc.wss://localhost:62543/MyServer</ua:String>
</BaseAddresses>
```

* The `opc.tcp` listener uses the lightweight raw-socket
  `TcpTransportListener` by default (no ASP.NET Core dependency). An
  opt-in alternative — `Opc.Ua.Bindings.Kestrel.Tcp` — is described
  below.
* The `opc.https` and `opc.wss` listeners both share a single
  `HttpsTransportListener` per `(host, port)` — Kestrel routes the
  inbound HTTP request to the right handler based on the
  `Upgrade` / `Content-Type` headers. Concretely:
  - `POST` with `Content-Type: application/octet-stream` →
    `https-uabinary` handler.
  - `POST` with `Content-Type: application/opcua+uajson` →
    `https-uajson` handler.
  - HTTP Upgrade with `Sec-WebSocket-Protocol: opcua+uacp` →
    full UASC WebSocket session.
  - HTTP Upgrade with `Sec-WebSocket-Protocol: opcua+uajson` →
    request/response JSON WebSocket session.

### Security configuration

The HTTPS and WSS listeners share the same TLS configuration via
`HttpsConnectionAdapterOptions`. By default the listener serves
`SecurityMode.None`; setting
`ServerConfiguration.HttpsMutualTls = true` requires the client to
present a TLS certificate which is matched against the
`ClientCertificate` field of the UASC `OpenSecureChannelRequest`.

The JSON sub-profiles (`https-uajson` and the WSS `opcua+uajson`
sub-protocol) only accept `SecurityMode.None` regardless of the
configured security policies — see Part 6 §7.4.5 / §7.5.2 for the
spec rationale.

## Client-side usage

The client API is the standard `Session` + `EndpointDescription` flow;
the transport is selected automatically from
`EndpointDescription.TransportProfileUri`:

```csharp
ITelemetryContext telemetry = NUnitTelemetryContext.Create();
var application = new ApplicationInstance(telemetry)
{
    ApplicationName = "MyClient",
    ApplicationType = ApplicationType.Client
};
ApplicationConfiguration configuration = await application
    .Build("urn:localhost:MyClient", "urn:localhost:product")
    .AsClient()
    .AddSecurityConfiguration("CN=MyClient")
    .Create()
    .ConfigureAwait(false);

// 1) Direct connect to a known opc.wss endpoint (binary UASC):
EndpointDescription wssEndpoint = CoreClientUtils.SelectEndpoint(
    application,
    "opc.wss://server.example.com:62543/MyServer",
    useSecurity: true);
ISession session = await Session.CreateAsync(
    configuration, new ConfiguredEndpoint(null, wssEndpoint), false,
    "WssSession", 60_000, null, null).ConfigureAwait(false);

// 2) HTTPS-JSON endpoint (Security Mode None):
var jsonEndpoint = new EndpointDescription
{
    EndpointUrl = "opc.https://server.example.com:62540/MyServer",
    SecurityMode = MessageSecurityMode.None,
    SecurityPolicyUri = SecurityPolicies.None,
    TransportProfileUri = Profiles.HttpsJsonTransport
};
ISession jsonSession = await Session.CreateAsync(
    configuration, new ConfiguredEndpoint(null, jsonEndpoint), false,
    "JsonSession", 60_000, null, null).ConfigureAwait(false);
```

A few notes:

* The `opc.wss://` scheme is the OPC UA alias; the stack normalises it
  to `wss://` (the IETF scheme) and falls back to
  `Utils.UaWebSocketsDefaultPort` (4843) when no explicit port is
  supplied.
* The WSS client requires the server to select the requested WebSocket
  sub-protocol. If the server returns anything else the connection
  fails with `BadNotConnected`.
* The WSS-JSON client opens a fresh `ClientWebSocket` per request
  (simplest correct behaviour). A pooled / persistent-WebSocket
  variant can be added without changing the public shape.
* The HTTPS client (binary or JSON) reuses a single `HttpClient` per
  channel; the encoding is selected from
  `EndpointDescription.TransportProfileUri` at request time.

## Discovery

`GetEndpoints` and `FindServers` return the `EndpointDescription` list
declared by the listener for each base address. For TCP, HTTPS-binary,
and WSS+uacp the description includes the correct `TransportProfileUri`
(`uatcp-uasc-uabinary`, `https-uabinary`, `uawss-uasc-uabinary`
respectively). The JSON sub-protocols are reachable on the same URL as
their binary counterparts and clients select them explicitly via the
`Content-Type` header (HTTPS) or the `Sec-WebSocket-Protocol` header
(WSS). Explicit discovery emission for the JSON profiles is tracked as
a follow-up; for now clients that want JSON construct the
`EndpointDescription` themselves with
`TransportProfileUri = Profiles.HttpsJsonTransport` /
`Profiles.UaWssJsonTransport`.

## Opt-in: Kestrel-hosted `opc.tcp`

By default the stack ships **two** listener implementations for
`opc.tcp://`:

| Package | Listener | When to use |
| --- | --- | --- |
| `Opc.Ua.Core` (default) | `TcpTransportListener` | Raw `Socket` + `SocketAsyncEventArgs`. No ASP.NET Core dependency. The right choice for trimmed / AOT deployments and for environments that already avoid pulling in `Microsoft.AspNetCore.App`. |
| `Opc.Ua.Bindings.Kestrel.Tcp` (opt-in) | `KestrelTcpTransportListener` | Hosts `opc.tcp://` on Kestrel via `Microsoft.AspNetCore.Connections.ConnectionHandler`. Lets a single `IHost` serve `opc.tcp`, `opc.https`, and `opc.wss` so consumers manage one HTTP-style runtime, share TLS plumbing, and share observability middleware. |

The Kestrel-TCP listener uses the same `IUaSCByteTransport` runtime
boundary as the raw-socket listener, so the UASC channel pipeline is
unchanged. It supports the full feature set the raw-socket listener
does, including:

* forward (server) and **reverse-connect** listener modes,
* per-listener TLS certificate hot-update via
  `ITransportListenerCertificateRotation`,
* discovery (`EndpointDescription` emission via the shared
  `TcpServiceHost` base).

Swap it in by registering the factory **before** opening any
`opc.tcp` listener (typically at application startup):

```csharp
TransportBindings.Listeners.SetBinding(
    new KestrelTcpTransportListenerFactory());

// All subsequent opc.tcp listeners (server endpoints AND client-side
// reverse-connect hosts created by ReverseConnectHost.CreateListener)
// will now run on Kestrel.
```

The package targets net8.0+ only (the ASP.NET Core `ConnectionContext`
API surface used to bridge `Socket`-like semantics — `LocalEndPoint`,
`RemoteEndPoint`, `ConnectionClosed` — is not consistently available
on older TFMs). Consumers on net472 / netstandard2.x continue to use
the raw-socket default.

## See also

* [`Profiles.md`](Profiles.md) — supported profiles, security policies,
  message encodings.
* [`CustomTransport.md`](CustomTransport.md) — implementing
  `IUaSCByteTransport` for a custom byte transport (named pipes, QUIC,
  in-process bridges, etc.). Includes a worked example.
* [`MigrationGuide.md`](MigrationGuide.md) — note on the breaking
  removal of `IMessageSocket` and the new `IUaSCByteTransport`
  contract.
* OPC UA Part 6 §7.4 (HTTPS) / §7.5 (WebSockets) for the wire-format
  specification.
