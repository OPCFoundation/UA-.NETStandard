# OPC UA .NET Standard — PubSub UDP transport

`OPCFoundation.NetStandard.Opc.Ua.PubSub.Udp` provides the UDP transport
(unicast, multicast, and broadcast, including the Part 14 §6.4.1.4
datagram-v2 connection profile and UDP discovery) for the modern
`OPCFoundation.NetStandard.Opc.Ua.PubSub` stack.

## Getting started

Register the transport on the PubSub builder:

```csharp
using Microsoft.Extensions.DependencyInjection;

builder.Services.AddOpcUa()
    .AddPubSub(pubsub => pubsub
        .AddPublisher()
        .AddUdpTransport());
```

The UDP-UADP transport profile supports both `opc.udp://` and the DTLS-protected
unicast `opc.dtls://` scheme. DTLS still requires explicit
`AddUdpTransport().WithDtls(...)` registration, certificate configuration, and
peer validation. Accepting the DTLS scheme during configuration validation does
not enable DTLS automatically or fall back to plaintext UDP.

## Target frameworks

`net48`, `net8.0`, `net9.0`, `net10.0`.

## Additional documentation

See the [PubSub documentation](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/PubSub.md)
for transports, encodings, security, and the fluent / DI API.
