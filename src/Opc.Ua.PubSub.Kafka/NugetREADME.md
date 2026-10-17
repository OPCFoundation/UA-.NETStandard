# OPC UA .NET Standard — PubSub Apache Kafka transport

`OPCFoundation.NetStandard.Opc.Ua.PubSub.Kafka` provides the Apache Kafka broker transport (OPC UA Part 14 Annex B.2, with SASL/TLS security, configurable delivery guarantees, and both the UADP and JSON message mappings) for the modern `OPCFoundation.NetStandard.Opc.Ua.PubSub` stack. Kafka consumer groups and idempotent producers back the high-availability publisher/subscriber deployments.

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

`net48`, `net8.0`, `net9.0`, `net10.0`.

## NativeAOT

The transport defaults to the pure-managed [Dekaf](https://github.com/thomhurst/Dekaf) client on every supported target framework. This repository asserts and validates **NativeAOT / trimming compatibility on `net10.0`** for that default backend.

JIT-compiled hosts can opt into `Confluent.Kafka`:

```csharp
pubsub.AddKafkaTransport()
    .WithConfluentKafkaClient();
```

The Confluent backend uses native `librdkafka` and is not NativeAOT or trimming compatible.

NativeAOT support for other PubSub transports depends on the target runtime and
selected backend. See the
[PubSub Native AOT guidance](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/PubSub.md#native-aot)
and the [Ethernet backend notes](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/src/Opc.Ua.PubSub.Eth/NugetREADME.md#frame-backends);
targeting a framework does not itself establish NativeAOT compatibility.

## Additional documentation

See the [Apache Kafka transport documentation](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/PubSub.md#apache-kafka)
in the [PubSub documentation](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/PubSub.md)
for transports, encodings, security, high availability, and the fluent / DI API.
