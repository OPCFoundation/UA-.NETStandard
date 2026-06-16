# OPC UA .NET Standard — HTTPS / WSS binding

`OPCFoundation.NetStandard.Opc.Ua.Bindings.Https` adds the
`https://` / `opc.https://` (HTTPS-binary + HTTPS-JSON) and
`wss://` / `opc.wss://` (WSS-binary + WSS-JSON) listeners and
channels on top of `OPCFoundation.NetStandard.Opc.Ua.Core`. The
listeners are hosted by Kestrel. On `net8.0`+ it also provides an
opt-in Kestrel-hosted `opc.tcp://` listener so a single `IHost` can
serve every OPC UA transport.

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
- `KestrelTcpTransportListener` (`net8.0`+, opt-in) — hosts the
  `opc.tcp://` listener on Kestrel via
  `Microsoft.AspNetCore.Connections.ConnectionHandler` instead of the
  raw-socket `TcpTransportListener` (which ships in `Opc.Ua.Core` and
  stays the default). Registered with `AddKestrelOpcTcpTransport()`; it
  has full feature parity with the raw-socket listener (forward
  endpoints, reverse-connect, TLS certificate hot-update, discovery).

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

## Target frameworks

`net472`, `net48`, `netstandard2.1`, `net8.0`, `net9.0`, `net10.0`.
The opt-in Kestrel-hosted `opc.tcp://` listener
(`AddKestrelOpcTcpTransport()`) is available on `net8.0`+ only — the
ASP.NET Core `ConnectionContext` surface it relies on
(`LocalEndPoint` / `RemoteEndPoint` / `ConnectionClosed`) is not
available on the .NET Framework / netstandard targets, where the
default raw-socket `opc.tcp` listener remains the right choice.

## Additional documentation

See the [main repository README](https://github.com/OPCFoundation/UA-.NETStandard)
and the [Transport Profiles guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/Transports.md)
for the full URI scheme matrix, the
[Reverse Connect guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/ReverseConnect.md),
and the [Profiles overview](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/Profiles.md).
