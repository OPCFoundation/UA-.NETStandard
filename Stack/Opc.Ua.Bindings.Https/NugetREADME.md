# OPC UA .NET Standard — HTTPS / WSS / REST binding

`OPCFoundation.NetStandard.Opc.Ua.Bindings.Https` adds the
`https://` / `opc.https://` (HTTPS-binary + HTTPS-JSON) and
`wss://` / `opc.wss://` (WSS-binary + WSS-JSON) listeners and
channels on top of `OPCFoundation.NetStandard.Opc.Ua.Core`. The
listeners are hosted by Kestrel. On `net8.0`+ it also provides an
opt-in Kestrel-hosted `opc.tcp://` listener so a single `IHost` can
serve every OPC UA transport, and the **OPC UA REST binding** (Part 6
§G.3 OpenAPI Mapping) as ASP.NET Core Minimal-API endpoints mounted
into the same Kestrel pipeline.

## Overview

The package contains:

- `HttpsTransportListener` — single Kestrel-hosted listener serving
  HTTPS-binary, HTTPS-JSON, WSS-binary, and WSS-JSON on the same port,
  with content-type and `Sec-WebSocket-Protocol` negotiation.
- `WssTransportChannel` / `WssJsonTransportChannel` — client-side
  WebSocket channels for the two sub-protocols.
- `HttpsTransportChannel` / `OpcHttpsTransportChannel` — client-side
  HTTPS-binary channels.
- WSS reverse-connect for both server-side outbound and client-side
  listener flows.
- The `AddHttpsTransport()` / `AddWssTransport()` DI extensions on
  `IOpcUaBuilder` for fluent registration into the
  `ITransportBindingRegistry`.
- An `IHttpsListenerStartupContributor` extension hook for companion
  middleware that wants to mount on the shared Kestrel host.
- `KestrelTcpTransportListener` (`net8.0`+, opt-in) — hosts the
  `opc.tcp://` listener on Kestrel via
  `Microsoft.AspNetCore.Connections.ConnectionHandler` instead of the
  raw-socket `TcpTransportListener` (which ships in `Opc.Ua.Core` and
  stays the default). Registered with `AddKestrelOpcTcpTransport()`; it
  has full feature parity with the raw-socket listener (forward
  endpoints, reverse-connect, TLS certificate hot-update, discovery).
- `WebApiHttpsStartupContributor` + `MapWebApiEndpoints()` (`net8.0`+,
  opt-in) — Minimal-API endpoints implementing the **OPC UA REST
  binding** (Part 6 §G.3 OpenAPI Mapping). Registered with
  `AddWebApiTransport()`; NativeAOT-compatible, no MVC reflection.

## Getting started

```csharp
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua;

services
    .AddOpcUa()
    .AddOpcTcpTransport()
    .AddHttpsTransport()   // https:// + opc.https://
    .AddWssTransport();    // wss://   + opc.wss://

// Optional (net8.0+): host opc.tcp:// on Kestrel too, so a single
// IHost serves opc.tcp, opc.https, and opc.wss. Call AFTER
// AddOpcTcpTransport(); last-writer-wins per URI scheme.
services
    .AddOpcUa()
    .AddOpcTcpTransport()
    .AddKestrelOpcTcpTransport();
```

## OPC UA REST binding (Part 6 §G.3 OpenAPI Mapping)

On `net8.0`+ the package also exposes every OPC UA service as a
`POST /<service>` Minimal-API endpoint, implementing the [OPC UA
OpenAPI Mapping](https://reference.opcfoundation.org/Core/Part6/v105/docs/G.3)
(profile/2338). Bodies use the standard OPC UA JSON encoding from
Part 6 §5.4; both the **Compact** flavour (default; mandatory per
§5.4.9) and **Verbose** are negotiated via the
`application/json; encoding=compact|verbose` media-type parameter on
`Content-Type` and `Accept`.

The REST surface is **additive**: it mounts into the same Kestrel
pipeline as the binary + `opcua+uajson` bindings so a single port
serves them all simultaneously. Discovery (`GetEndpoints`) emits an
OpenAPI twin per `SecurityMode.None` HTTPS endpoint with
`TransportProfileUri = Profiles.HttpsOpenApiTransport`.

```csharp
services
    .AddOpcUa()
    .AddHttpsTransport()
    .AddWebApiTransport()             // adds the REST endpoints
    .AddWebApiAnonymousAuth();        // or AddWebApiBearerAuth(...) /
                                      // AddWebApiBasicAuth(...) /
                                      // AddWebApiMutualTlsAuth()
```

The companion WSS sub-profile `opcua+openapi` (profile/2339) is
provided by `WebApiWssTransportChannel` (client) and
`HttpsTransportListener.AcceptWebSocketOpenApiAsync` (server).

The binding is `<IsAotCompatible>true</IsAotCompatible>` on net10 —
the endpoint surface uses Minimal-API `MapPost` calls bound to
explicit `RequestDelegate` thunks so the trimmer can see every
generic instantiation at compile time. No
`[UnconditionalSuppressMessage]` attributes.

## Target frameworks

`net472`, `net48`, `netstandard2.1`, `net8.0`, `net9.0`, `net10.0`.
The opt-in Kestrel-hosted `opc.tcp://` listener
(`AddKestrelOpcTcpTransport()`) and the REST binding
(`AddWebApiTransport()`) are available on `net8.0`+ only — the
ASP.NET Core `ConnectionContext` and Minimal-API surfaces they rely
on are not available on the .NET Framework / netstandard targets,
where the default raw-socket `opc.tcp` listener remains the right
choice and REST is unavailable.

## Additional documentation

See the [main repository README](https://github.com/OPCFoundation/UA-.NETStandard)
and the [Transport Profiles guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/Transports.md)
for the full URI scheme matrix, the
[REST binding guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/WebApi.md)
for the full REST mapping table / authentication models / hosting
modes, the
[Reverse Connect guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/ReverseConnect.md),
and the [Profiles overview](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/Profiles.md).
