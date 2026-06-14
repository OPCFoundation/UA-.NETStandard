# OPC UA .NET Standard — Kestrel `opc.tcp` listener (opt-in)

## Overview

`OPCFoundation.NetStandard.Opc.Ua.Bindings.Kestrel.Tcp` is an
optional binding that hosts the `opc.tcp://` listener on a Kestrel
`IHost` instead of raw `Socket` + `SocketAsyncEventArgs`.

The default `TcpTransportListener` (shipped in
`OPCFoundation.NetStandard.Opc.Ua.Core`) stays available and remains
the right choice for trimmed / AOT deployments or environments that
explicitly avoid `Microsoft.AspNetCore.App`. This package is for
consumers who want a **single Kestrel runtime** serving every OPC UA
transport (`opc.tcp`, `opc.https`, `opc.wss`) — shared TLS plumbing,
shared observability middleware, shared `IHost` lifetime.

## Feature parity with the raw-socket listener

| Capability | `TcpTransportListener` (default) | `KestrelTcpTransportListener` (this package) |
| --- | :---: | :---: |
| Forward server endpoints | ✅ | ✅ |
| Reverse-connect listener mode | ✅ | ✅ |
| TLS certificate hot-update (`ITransportListenerCertificateRotation`) | ✅ | ✅ |
| Discovery (`EndpointDescription` emission) | ✅ | ✅ |
| ASP.NET Core dependency | none | `Microsoft.AspNetCore.App` |

The package uses the same `IUaSCByteTransport` runtime boundary as the
raw-socket listener, so the UASC channel pipeline above the transport
is unchanged.

## Getting started

```csharp
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua;
using Opc.Ua.Bindings;

// DI-aware consumers (Microsoft.Extensions.DependencyInjection):
services
    .AddOpcUa()
    .AddOpcTcpTransport()           // raw-socket opc.tcp default
    .AddKestrelOpcTcpTransport();   // overrides with Kestrel (last-writer-wins)

// Standalone non-DI consumers:
DefaultTransportBindingRegistry registry =
    DefaultTransportBindingRegistry.WithDefaultTcp();
registry.RegisterListenerFactory(new KestrelTcpTransportListenerFactory());
// Forward the registry to:
//   - ServerBase via the new ctor overload or the `TransportBindings`
//     setter (server-side forward and reverse-connect outbound),
//   - ReverseConnectManager.TransportBindings (client-side reverse-
//     connect listener; every AddEndpoint(Uri,...) call constructs a
//     ReverseConnectHost that picks up the right factory for the
//     scheme).
```

## Target frameworks

`net8.0`, `net9.0`, `net10.0`. The package is not provided on
`net472` / `net48` / `netstandard2.x` because the ASP.NET Core
`ConnectionContext` surface used to bridge `Socket`-like semantics
(`LocalEndPoint`, `RemoteEndPoint`, `ConnectionClosed`) is not
consistently available on those TFMs. Consumers on older runtimes
should keep using the default raw-socket listener.

## Additional documentation

See the [Transport Profiles guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/Transports.md)
and the [Reverse Connect guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/ReverseConnect.md)
in the main repository for end-to-end usage, configuration, and
discovery details.
