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

## Target frameworks

`net472`, `net48`, `netstandard2.1`, `net8.0`, `net9.0`, `net10.0`.

## Additional documentation

See the [PubSub documentation](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/PubSub.md)
for transports, encodings, security, and the fluent / DI API.
