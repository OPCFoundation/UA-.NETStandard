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

## Target frameworks

`net48`, `net8.0`, `net9.0`, `net10.0`.

## Additional documentation

See the [main repository README](https://github.com/OPCFoundation/UA-.NETStandard)
and the [Transport Profiles guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/Transports.md)
for the full URI scheme matrix, the
[Reverse Connect guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/ReverseConnect.md),
and the [Profiles overview](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/Profiles.md).
