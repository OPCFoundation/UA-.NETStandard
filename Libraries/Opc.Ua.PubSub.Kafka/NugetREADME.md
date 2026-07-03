# OPC UA .NET Standard — PubSub Apache Kafka transport

`OPCFoundation.NetStandard.Opc.Ua.PubSub.Kafka` provides the Apache Kafka broker
transport (OPC UA Part 14 Annex B.2, with SASL/TLS security, configurable delivery
guarantees, and both the UADP and JSON message mappings) for the modern
`OPCFoundation.NetStandard.Opc.Ua.PubSub` stack. Kafka consumer groups and idempotent
producers back the high-availability publisher/subscriber deployments.

## Getting started

Register the transport on the PubSub builder:

```csharp
using Microsoft.Extensions.DependencyInjection;

builder.Services.AddOpcUa()
    .AddPubSub(pubsub => pubsub
        .AddSubscriber()
        .AddKafkaTransport());
```

Connection addresses use `kafka://host:9092` (plain/SASL) or `kafkas://host:9093` (TLS).

## Target frameworks

`net472`, `net48`, `netstandard2.1`, `net8.0`, `net9.0`, `net10.0`.

## NativeAOT

This transport wraps the native `librdkafka` client (via Confluent.Kafka) and is therefore
**not** NativeAOT/trimming compatible. Use a JIT-compiled host for Kafka deployments; the
other PubSub transports (UDP, Ethernet, MQTT) remain AOT-compatible.

## Additional documentation

See the [PubSub Kafka documentation](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/PubSubKafka.md)
and the [PubSub documentation](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/PubSub.md)
for transports, encodings, security, high availability, and the fluent / DI API.
