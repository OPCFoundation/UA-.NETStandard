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
- Complete NetworkMessage transport over the provisional DataChannel
  Dedicated UASC, Shared UASC, QUIC, and WebTransport profiles. Each
  carrier independently negotiates UADP or JSON. Native carrier packages
  are not dependencies of this package.

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
See [the PubSub migration guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/migrate/2.0.x/pubsub.md)
to move to the fluent builder / DI surface.

## Brokerless DataChannel transport

The DataChannel adapter uses the existing Part 14 **1.05.06** application,
sources, sinks, groups, metadata, diagnostics, and message-security pipeline.
It does not serialize a second message format inside the flow.

First obtain and start a real authenticated `DataChannelContext` using the
selected carrier's connector or host. Then bind that context to PubSub:

```csharp
using Opc.Ua.DataChannels;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Transports.DataChannels;

DataChannelPubSubConfiguration.Configure(
    connection,
    DataChannelCarrierKind.DedicatedUasc,
    new DataChannelParametersDataType
    {
        ResourceUri = "urn:example:pubsub:telemetry",
        Protocol = DataChannelProfiles.UadpPayload, // or JsonPayload
        ContentType = "application/opcua+uadp",
        Direction = DataChannelDirection.OpenerToAcceptor,
        Delivery = DataChannelDeliveryMode.Ordered,
        Protection = DataChannelProtectionMode.Confidential,
        MaxMessageSize = 65536,
        MaxReceiveBytes = 262144
    });

await using var flows = new DataChannelContextPubSubFlowProvider(
    context, openLocally: true);
await using IPubSubApplication application = new PubSubApplicationBuilder(telemetry)
    .UseConfiguration(configuration) // contains connection, datasets and groups
    .UseAllStandardEncoders()
    .UseDataChannelTransport(DataChannelCarrierKind.DedicatedUasc, flows)
    .AddDataSetSource("Telemetry", source)
    .Build();
await application.StartAsync(cancellationToken);
```

Configure the peer's reader groups and sinks normally, and use
`openLocally: false` on its context-backed provider. Direction is relative
to the **flow opener**, not the Publisher or transport initiator:
`AcceptorToOpener` also supports a remotely opened publishing flow.
`PubSubConnectionBuilder.WithDataChannel(...)` provides the same nested
configuration API. A bound context needs no connection Address; a
connector-backed `IDataChannelPubSubFlowProvider` can use the standard
Address to locate the actual endpoint.

DI uses `IPubSubBuilder.AddDataChannelTransport(carrierKind, provider)` for
a caller-owned provider, or its factory overload for a DI-owned provider.
Custom providers return an actual ready `DataChannelPubSubFlow`; they
must clean up failed or cancelled establishment. An optional
`IDataChannelPubSubGrantProvider` obtains a fresh Session grant through
the model-control API. The default configured path never creates a
Session or reads an address space.

The context-backed accept provider exclusively owns `AcceptAsync` for its
context, routing by exact resource and payload protocol with bounded
pending admission. A host that also accepts other application flows
must use its shared router instead; `Opc.Ua.PubSub.Server` supplies a
server-router adapter. Disposing a transport closes only its flow, not
unrelated flows or shared Client/Server Services. After context loss a
connector must acquire a new authenticated context and fresh flow.

Several writers may share one flow. Separate resources, permissions,
queue budgets, delivery contracts, or lifetimes require separate flows.
ChannelId never replaces PublisherId, WriterGroupId, or DataSetWriterId.
The adapter validates the negotiated descriptor before enabling groups,
preserves every encoded byte, and retains received message credit until
decoding and sink processing finish. UADP chunking/reassembly remains
Part 14 processing. Opaque flows are not a PubSub encoding.
`AllowQueueExpiry=true` is rejected until a Publisher-side policy can preserve
or recover metadata/key-frame/delta dependencies. The adapter never silently
disables requested expiry or parses and rewrites NetworkMessages to compensate.
An orderly remote END drains accepted messages through reader delivery before
disconnect notifications pause the connection. Directional EOF alone does not
disconnect a still-open duplex flow; its remaining direction and control stay
available. Authorization and protocol faults still take effect immediately.

Carrier confidentiality needs neither a broker nor an SKS. Existing
UADP signing/encryption remains available when explicitly configured
with its normal key provider; JSON cannot silently satisfy a configured
UADP message-security requirement.

For live revisions, directional end, and flow diagnostics, a transport
observer can retain the concrete `DataChannelPubSubTransport` created by
the factory and use its borrowed `Channel` property after opening. The
property is null before opening and after closing; ownership remains
with the transport. Session-authorized updates still need the normal
model-control authorization.

Raw-byte diagnostics belong at `IPubSubTransport.SendAsync` and
`ReceiveAsync`, after Part 14 encoding/security/chunking. A decorator
must preserve the yielded frame lifetime and forward
`IPubSubEncodingTransport.EncodingProfileUri` and
`MaxNetworkMessageSize`; otherwise it hides negotiated capabilities.

## Target frameworks

`net48`, `net8.0`, `net9.0`, `net10.0`.

## Additional documentation

See the [main repository README](https://github.com/OPCFoundation/UA-.NETStandard)
and the dedicated
[Opc.Ua.PubSub README](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/src/Opc.Ua.PubSub/README.md)
for the full design (encodings, transports, security-key-service,
discovery).
