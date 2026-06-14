# OPC UA .NET Standard — HTTPS / WSS binding

`OPCFoundation.NetStandard.Opc.Ua.Bindings.Https` adds the
`https://` / `opc.https://` (HTTPS-binary + HTTPS-JSON) and
`wss://` / `opc.wss://` (WSS-binary + WSS-JSON) listeners and
channels on top of `OPCFoundation.NetStandard.Opc.Ua.Core`. The
listeners are hosted by Kestrel.

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
- An `IHttpsListenerStartupContributor` extension hook that allows
  companion bindings (e.g.
  `OPCFoundation.NetStandard.Opc.Ua.Bindings.WebApi`) to mount
  additional middleware (typically routing + MVC controllers) into the
  same Kestrel host that already serves binary / `opcua+uajson` /
  WebSocket traffic.

## Getting started

```csharp
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua;

services
    .AddOpcUa()
    .AddOpcTcpTransport()
    .AddHttpsTransport()   // https:// + opc.https://
    .AddWssTransport();    // wss://   + opc.wss://
```

## Companion REST binding

Install
[`OPCFoundation.NetStandard.Opc.Ua.Bindings.WebApi`](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.Bindings.WebApi/)
and call `.AddWebApiTransport()` to expose the OPC UA service set as
an ASP.NET Core REST controller surface (Part 6 §G.3 OpenAPI Mapping)
on the same Kestrel port.

## Target frameworks

`net472`, `net48`, `netstandard2.1`, `net8.0`, `net9.0`, `net10.0`.

## Additional documentation

See the [main repository README](https://github.com/OPCFoundation/UA-.NETStandard)
and the [Transport Profiles guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/Transports.md)
for the full URI scheme matrix, the
[Reverse Connect guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/ReverseConnect.md),
and the [Profiles overview](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/Profiles.md).
