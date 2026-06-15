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
| [`https-uajson-openapi`](https://profiles.opcfoundation.org/profile/2338) | `opc.https://`, `https://` | OpenAPI Mapping (Part 6 §G.3): per-service `POST /<service>` with body = `<Service>Request` JSON (`application/json; encoding=compact\|verbose`) | **no** — TLS only | `None` only |
| `uawss-uasc-uabinary` | `opc.wss://`, `wss://` | UA Binary in WebSocket binary frame (sub-protocol `opcua+uacp`) | yes | `None`, `Sign`, `SignAndEncrypt` |
| `uawss-uajson` | `opc.wss://`, `wss://` | UA JSON in WebSocket text frame (sub-protocol `opcua+uajson`) | **no** — TLS only | `None` only |
| [`wss-uajson-openapi`](https://profiles.opcfoundation.org/profile/2339) | `opc.wss://`, `wss://` | OpenAPI Mapping over WebSocket text frame (sub-protocol `opcua+openapi` / `opcua+openapi+<accesstoken>`) | **no** — TLS only | `None` only |

The JSON / OpenAPI profiles do not negotiate a UA SecureChannel; transport
security is provided exclusively by the surrounding TLS connection.
Servers MUST advertise these endpoints with `MessageSecurityMode.None`
and `SecurityPolicyUri = None`. The `https-uajson` and WSS JSON
profiles use the Compact (reversible) flavour mandated by
Part 6 §5.4.9 (`JsonEncoderOptions.Compact`). The OpenAPI profiles
select between Compact (default, mandatory) and Verbose via
the `application/json; encoding=compact|verbose` media-type parameter
on `Content-Type` / `Accept` — see [`WebApi.md`](WebApi.md) for the
full mapping table.

The OPC UA Profiles surface exposes the two OpenAPI profile URIs as
`Profiles.HttpsOpenApiTransport` (profile/2338) and
`Profiles.WssOpenApiTransport` (profile/2339); the `HttpsServiceHost`
emits the HTTPS twin as a discovery-only `EndpointDescription`
alongside each `SecurityMode.None` HTTPS-binary endpoint so discovery
clients see the OpenAPI route without hard-coding the URL.
`Profiles.HttpsWebApiTransport` is an `[Obsolete]` alias retained for
binary compatibility — new code should reference
`Profiles.HttpsOpenApiTransport`.

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

For both WSS variants the HTTPS / WSS factories live in
`Opc.Ua.Bindings.Https`; consumers register them via the
`AddHttpsTransport()` / `AddWssTransport()` DI extensions on
`IOpcUaBuilder`, or by constructing a `DefaultTransportBindingRegistry`
and calling `RegisterListenerFactory` / `RegisterChannelFactory`
directly for non-DI hosts.

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

Swap it in by registering the factory through the DI container
**before** opening any `opc.tcp` listener (typically at application
startup):

```csharp
// Standalone non-DI consumers:
DefaultTransportBindingRegistry registry =
    DefaultTransportBindingRegistry.WithDefaultTcp();
registry.RegisterListenerFactory(new KestrelTcpTransportListenerFactory());
// Pass to ServerBase (via ctor / TransportBindings setter) before StartAsync.

// Microsoft.Extensions.DependencyInjection consumers:
services
    .AddOpcUa()
    .AddOpcTcpTransport()           // raw-socket opc.tcp default
    .AddKestrelOpcTcpTransport();   // last-writer-wins: overrides with Kestrel
```

`AddKestrelOpcTcpTransport()` installs an `ITransportBindingConfigurator`
that runs after `AddOpcTcpTransport()`, so the registry resolves the
Kestrel listener factory for `opc.tcp://`. The same pattern applies to
`AddHttpsTransport()` / `AddWssTransport()` (HTTPS / WSS) and any custom
binding registered via `AddCustomTransport<TListener, TChannel>()`.

The package targets net8.0+ only (the ASP.NET Core `ConnectionContext`
API surface used to bridge `Socket`-like semantics — `LocalEndPoint`,
`RemoteEndPoint`, `ConnectionClosed` — is not consistently available
on older TFMs). Consumers on net472 / netstandard2.x continue to use
the raw-socket default.

## Implementing a custom byte transport

The OPC UA Secure Conversation (UASC) binary channel pipeline talks to
the wire through a narrow byte-level abstraction —
[`IUaSCByteTransport`](../Stack/Opc.Ua.Core/Stack/Tcp/IUaSCByteTransport.cs).
It is the public extension point for plugging in custom transports
beyond the built-in TCP and WebSocket implementations.

### When you need this

Use cases that are good fits for a custom byte transport:

- **Named pipes / Unix domain sockets** for fast loopback IPC.
- **QUIC / HTTP/3** to replace the TCP transport's congestion behaviour.
- **In-process bridges** for unit tests or co-located server/client pairs
  that want to skip the network stack entirely.
- **Tunnels** (SSH, custom L4) that wrap the UASC chunks in another
  framing.

Use cases that should NOT use this:

- A new sub-protocol that changes UASC framing — that lives inside the
  channel implementation, not the transport.
- HTTPS-binary or HTTPS-JSON variants — those are handled in
  `Opc.Ua.Bindings.Https` outside the UASC pipeline.

### Contract summary

```csharp
public interface IUaSCByteTransport
{
    string Implementation { get; }                  // diagnostic id
    TransportChannelFeatures Features { get; }      // optional capabilities
    EndPoint? LocalEndpoint { get; }                // may be null
    EndPoint? RemoteEndpoint { get; }               // may be null

    ValueTask ConnectAsync(Uri url, CancellationToken ct);
    ValueTask SendChunkAsync(ReadOnlyMemory<byte> chunk, CancellationToken ct);
    ValueTask SendChunkAsync(BufferCollection buffers, CancellationToken ct);
    ValueTask<ArraySegment<byte>> ReceiveChunkAsync(CancellationToken ct);
    void Close();
}

public interface IUaSCByteTransportFactory
{
    string Implementation { get; }
    IUaSCByteTransport Create(
        BufferManager bufferManager,
        int receiveBufferSize,
        ITelemetryContext telemetry);
}
```

Each Send / Receive operates on **exactly one** complete UASC
`MessageChunk` (Part 6 §6.7.2). The channel pipeline above the transport
owns chunk-level framing — your job is to move chunk bytes across the
wire.

### Implementation checklist

- [ ] Buffer ownership. Buffers returned by `ReceiveChunkAsync` are
      rented from the supplied `BufferManager`; the caller (the UASC
      channel) returns them via `BufferManager.ReturnBuffer` once the
      chunk has been processed. Do **not** pool or reuse the same array
      across receives.
- [ ] Idempotent `Close`. The channel may dispose a transport from
      multiple paths (normal shutdown, fatal error, channel-state race);
      every implementation MUST treat `Close()` as a no-op when already
      closed.
- [ ] Cancellation. `ReceiveChunkAsync` should observe the cancellation
      token while awaiting peer data — long-lived sessions rely on this
      to tear down cleanly.
- [ ] Error mapping. Map transport-layer errors to
      [`ServiceResultException`](../Stack/Opc.Ua.Core/Types/Result/ServiceResultException.cs)
      with the matching `StatusCodes.BadXxx` (`BadConnectionClosed`,
      `BadTcpMessageTypeInvalid`, `BadTcpMessageTooLarge`, …) so the
      channel can route them through normal UA fault paths.
- [ ] Vectored Send. The `BufferCollection` overload is called by the
      channel for chunks that span multiple buffers (typical for the
      asymmetric handshake). If your transport does not support
      vectored writes, concatenate the segments into a single buffer
      before sending (this is what `WebSocketByteTransportBase` does).
- [ ] Client vs server. Client transports implement `ConnectAsync` to
      dial outbound; server transports built from an
      already-accepted connection should throw `NotSupportedException`
      from `ConnectAsync` (this is the contract enforced by
      `UaSCUaBinaryClientChannel`).

### Wiring the transport into the channel pipeline

#### Client side

Implement [`IUaSCByteTransportFactory`](../Stack/Opc.Ua.Core/Stack/Tcp/IUaSCByteTransport.cs)
and hand the factory to a subclass of
`UaSCUaBinaryTransportChannel`:

```csharp
internal sealed class MyTransportFactory : IUaSCByteTransportFactory
{
    public string Implementation => "UA-MY";
    public IUaSCByteTransport Create(BufferManager bm, int rxSize, ITelemetryContext tel)
        => new MyByteTransport(bm, rxSize, tel);
}

public sealed class MyTransportChannel : UaSCUaBinaryTransportChannel
{
    public MyTransportChannel(ITelemetryContext telemetry)
        : base(new MyTransportFactory(), telemetry)
    {
    }
}

public sealed class MyTransportChannelFactory : ITransportChannelFactory
{
    public string UriScheme => "opc.my";
    public ITransportChannel Create(ITelemetryContext telemetry)
        => new MyTransportChannel(telemetry);
}
```

If your transport needs channel-level state (TLS validator, client cert,
custom credentials) plumbed in, override
`UaSCUaBinaryTransportChannel.OnSettingsSaved(TransportChannelSettings,
ChannelQuotas)` — that hook fires after the channel binds settings but
before it tries to connect. The WSS implementation uses this pattern;
see `WssTransportChannel.OnSettingsSaved` for a reference.

#### Server side

Implement [`ITransportListener`](../Stack/Opc.Ua.Core/Stack/Transport/ITransportListener.cs)
plus an `ITransportListenerFactory`. For each accepted connection,
construct your transport directly (not through the
`IUaSCByteTransportFactory`, which is client-side only), then hand it to
a `TcpServerChannel` via `Attach(channelId, transport)`. The WSS server
path in `HttpsTransportListener.AcceptWebSocketAsync` is the canonical
template.

#### Registering by URL scheme

Install your custom listener and channel factories into the host's
`ITransportBindingRegistry`. The simplest path uses the DI extension:

```csharp
services
    .AddOpcUa()
    .AddCustomTransport<MyCustomListenerFactory, MyCustomChannelFactory>();
```

Both factories are resolved from the `IServiceProvider` (so they may
have constructor-injected dependencies) and installed under the URI
scheme reported by `MyCustomListenerFactory.UriScheme`. Non-DI consumers
construct a `DefaultTransportBindingRegistry` and call
`RegisterListenerFactory` / `RegisterChannelFactory` directly, then hand
the registry to `ServerBase` (via `TransportBindings`) or to
`ReverseConnectManager.TransportBindings`.

### Worked example

[`InProcessTransport`](../Stack/Opc.Ua.Core/Stack/Tcp/InProcessTransport.cs)
is the public reference implementation that ships in `Opc.Ua.Core`. It
uses only public API: two paired transports communicate over a pair of
in-memory `System.Threading.Channels.Channel<byte[]>` (one per
direction). Use it directly for unit tests or co-located client/server
pairs that want to skip the network stack entirely, or as a
copy-paste starting point for a custom transport.

```csharp
using Opc.Ua.Bindings;

var buffers = new BufferManager("inproc", 8192, telemetry);
(InProcessTransport client, InProcessTransport server) =
    InProcessTransport.CreatePair(buffers, receiveBufferSize: 8192, telemetry);

await client.SendChunkAsync(payload, ct).ConfigureAwait(false);
ArraySegment<byte> received = await server.ReceiveChunkAsync(ct).ConfigureAwait(false);
```

Note that `InProcessTransport.ConnectAsync` throws
`NotSupportedException` — the pair is created up front via
`CreatePair`. A real network transport would implement `ConnectAsync`
to dial the wire.

## See also

* [`Profiles.md`](Profiles.md) — supported profiles, security policies,
  message encodings.
* [`MigrationGuide.md`](MigrationGuide.md) — note on the breaking
  removal of `IMessageSocket` and the new `IUaSCByteTransport`
  contract.
* OPC UA Part 6 §7.4 (HTTPS) / §7.5 (WebSockets) for the wire-format
  specification.
