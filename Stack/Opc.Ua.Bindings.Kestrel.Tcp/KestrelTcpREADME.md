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
using Opc.Ua.Bindings;

// Register the factory BEFORE opening any opc.tcp listener (typically
// at application startup). All subsequent opc.tcp listeners (server
// endpoints and client-side reverse-connect hosts created by
// ReverseConnectHost.CreateListener) will run on Kestrel.
TransportBindings.Listeners.SetBinding(new KestrelTcpTransportListenerFactory());
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
