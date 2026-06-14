# OPC UA .NET Standard — PubSub

`OPCFoundation.NetStandard.Opc.Ua.PubSub` implements the publisher /
subscriber side-channel defined by OPC 10000-14 (Part 14). It
supports the UADP and JSON message mappings over UDP, MQTT, and
broker-less transports — independent of the standard Client / Server
session model.

## Overview

The package provides:

- `UaPubSubApplication` and the connection / writer-group / reader-
  group object model.
- UADP (binary) and JSON message mappings.
- UDP-, MQTT-, and broker-less transport profiles.
- Dataset filter, security-key-service plumbing, and persisted state.

## Getting started

Build a publisher / subscriber application from a
`PubSubConfigurationDataType` (XML, JSON, or fluent-built) and start
it:

```csharp
using Opc.Ua.PubSub;

var pubSubConfig = UaPubSubConfigurationHelper.LoadConfiguration("publisher.xml");
using var pubSubApplication = UaPubSubApplication.Create(pubSubConfig);
pubSubApplication.Start();
```

## Target frameworks

`net48`, `netstandard2.0`, `netstandard2.1`, `net8.0`, `net9.0`,
`net10.0`.

## Additional documentation

See the [main repository README](https://github.com/OPCFoundation/UA-.NETStandard)
and the dedicated
[Opc.Ua.PubSub README](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Libraries/Opc.Ua.PubSub/README.md)
for the full design (encodings, transports, security-key-service,
discovery).
