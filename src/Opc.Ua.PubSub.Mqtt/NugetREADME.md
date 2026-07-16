# OPC UA .NET Standard — PubSub MQTT transport

`OPCFoundation.NetStandard.Opc.Ua.PubSub.Mqtt` provides the MQTT broker
transport (MQTT 3.1.1 and 5.0, with TLS, retained metadata, and both the
UADP and JSON message mappings) for the modern
`OPCFoundation.NetStandard.Opc.Ua.PubSub` stack.

## Getting started

Register the transport on the PubSub builder:

```csharp
using Microsoft.Extensions.DependencyInjection;

builder.Services.AddOpcUa()
    .AddPubSub(pubsub => pubsub
        .AddSubscriber()
        .AddMqttTransport());
```

## Target frameworks

`net472`, `net48`, `netstandard2.1`, `net8.0`, `net9.0`, `net10.0`.

## Additional documentation

See the [PubSub documentation](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/PubSub.md)
for transports, encodings, security, and the fluent / DI API.
