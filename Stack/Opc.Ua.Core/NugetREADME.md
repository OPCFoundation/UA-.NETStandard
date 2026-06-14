# OPC UA .NET Standard — Core

`OPCFoundation.NetStandard.Opc.Ua.Core` is the foundation library of the
OPC UA .NET Standard stack. It contains the wire-level UASC channel
implementation, the `opc.tcp://` raw-socket listener and channel, the
core service contracts, the encoders (binary / XML / JSON), the
certificate validation primitives, and the dependency-injection entry
point used by every other package in the stack.

## Overview

Most consumers do not reference `Opc.Ua.Core` directly — it flows in
transitively from `Opc.Ua.Server`, `Opc.Ua.Client`,
`Opc.Ua.Configuration`, or one of the binding packages. Reference it
directly when you need:

- Custom transport authors (`IUaSCByteTransport`, `ITransportListener`,
  `ITransportChannel`, `ITransportBindingRegistry`).
- Direct UASC channel use (`UaSCBinaryChannel`, `TcpServerChannel`,
  `TcpClientChannel`).
- Encoding / decoding without a full server or client
  (`BinaryEncoder`, `BinaryDecoder`, `JsonEncoder`, `XmlDecoder`).
- The fluent DI surface (`services.AddOpcUa()`).

## Getting started

```csharp
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua;

IServiceCollection services = new ServiceCollection();
services
    .AddOpcUa()
    .AddOpcTcpTransport();   // raw-socket opc.tcp listener + channel
```

## Target frameworks

`net472`, `net48`, `netstandard2.1`, `net8.0`, `net9.0`,
`net10.0`.

## Additional documentation

See the [main repository README](https://github.com/OPCFoundation/UA-.NETStandard)
and the [Docs folder](https://github.com/OPCFoundation/UA-.NETStandard/tree/master/Docs)
for the full design guide, transport profiles, certificate management,
and the migration guide from 1.5.378.
