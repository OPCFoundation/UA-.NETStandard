# OPC UA .NET Standard — PubSub

`OPCFoundation.NetStandard.Opc.Ua.PubSub` implements the publisher /
subscriber side-channel defined by OPC 10000-14 (Part 14). It
supports the UADP and JSON message mappings over UDP, MQTT, and
broker-less transports — independent of the standard Client / Server
session model.

## Overview

The package provides:

- The `IPubSubApplication` runtime and the connection / writer-group /
  reader-group object model, built via a fluent / DI API.
- UADP (binary) and JSON message mappings.
- UDP-, MQTT-, and broker-less transport profiles (in the companion
  `Opc.Ua.PubSub.Udp` / `Opc.Ua.PubSub.Mqtt` packages).
- Dataset filtering, AES-CTR message security with a Security Key
  Service, diagnostics, and persisted state.

## Getting started

Configure a publisher (or subscriber) through dependency injection:

```csharp
using Microsoft.Extensions.DependencyInjection;

builder.Services.AddOpcUa()
    .AddPubSub(pubsub => pubsub
        .AddPublisher()
        .AddUdpTransport()
        .ConfigureApplication(app => app
            .WithApplicationId("urn:example:publisher")
            .UseConfigurationFile("publisher.xml")));
```

The legacy 1.04 `UaPubSubApplication.Create(...)` API has been removed in 2.0.
See [the PubSub migration guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/migrate/2.0.x/pubsub.md)
to move to the fluent builder / DI surface.

## Target frameworks

`net472`, `net48`, `netstandard2.1`, `net8.0`, `net9.0`, `net10.0`.

## Additional documentation

See the [main repository README](https://github.com/OPCFoundation/UA-.NETStandard)
and the dedicated
[Opc.Ua.PubSub README](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Libraries/Opc.Ua.PubSub/README.md)
for the full design (encodings, transports, security-key-service,
discovery).
