# Part 14 PubSub

> **OPC UA Part 14 PubSub.** This document
> describes the v1.05.06 PubSub library shipped under the
> `Opc.Ua.PubSub.*` namespaces. It assumes the reader already
> understands the OPC UA PubSub model
> ([Part 14 §4](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/4))
> and focuses on **how to use the library**.

## Table of contents

- [At a glance](#at-a-glance)
- [Architecture](#architecture)
- [Core abstractions](#core-abstractions)
- [Fluent builder walkthrough](#fluent-builder-walkthrough)
- [Dependency injection / hosting](#dependency-injection--hosting)
- [Transports](#transports)
- [Encodings](#encodings)
- [Discovery](#discovery)
- [Security](#security)
- [Security Key Service (SKS)](#security-key-service-sks)
- [Server-side address space](#server-side-address-space)
- [Binding PubSub to an external OPC UA server (client-session adapters)](#binding-pubsub-to-an-external-opc-ua-server-client-session-adapters)
- [High availability state providers](#high-availability-state-providers)
- [Diagnostics](#diagnostics)
- [Native AOT](#native-aot)
- [Spec coverage](#spec-coverage)
- [Test coverage](#test-coverage)
- [Cross-references](#cross-references)

## At a glance

- Targets **OPC UA Part 14 v1.05.06** conformance for the implemented UDP,
  MQTT, UADP, JSON, discovery, Action, SKS, and address-space surfaces.
- Five library packages
  ([NuGet](https://www.nuget.org/packages?q=OPCFoundation.NetStandard.Opc.Ua.PubSub)):
  `Opc.Ua.PubSub`, `Opc.Ua.PubSub.Udp`, `Opc.Ua.PubSub.Mqtt`,
  `Opc.Ua.PubSub.Server`, `Opc.Ua.PubSub.Adapter`.
- Multi-TFM: `netstandard2.1`, `net48`, `net472`, `net8.0` (LTS), `net9.0`, `net10.0` (LTS).
- Native AOT clean — both reference samples publish with zero
  `IL2026` / `IL3050` warnings.
- Transports: **UDP** (uni/multi/broadcast), **DTLS over UDP** (`opc.dtls://`, unicast UADP), and **MQTT** (3.1.1 + 5.0).
- Encodings: **UADP** ([§7.2.4](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4))
  and **JSON** ([§7.2.5](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.5))
  with `Verbose` / `Compact` / `RawData` modes.
- Security: AES-128-CTR / AES-256-CTR + HMAC-SHA-256 with replay-window
  enforcement ([§7.2.4.4.3](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.4.3),
  [§8](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/8));
  pull/push **SKS** client + in-memory SKS server.
- Fluent `PubSubApplicationBuilder` and full DI surface
  (`services.AddOpcUa().AddPubSub(...)` etc.).
- Server-side: mounts the standard `PublishSubscribe` Object via
  `services.AddServer(...).AddPubSub()`.
- Per-component diagnostics (`IPubSubDiagnostics`) on every connection,
  group, writer, reader.
- Runtime configuration mutation via
  `IPubSubApplication.AddConnectionAsync` / `AddWriterGroupAsync` / etc.

## Architecture

The library is laid out as five sibling assemblies — the abstractions
and runtime live in `Opc.Ua.PubSub`, the transports plug in via
`IPubSubTransportFactory`, `Opc.Ua.PubSub.Server` optionally exposes the
runtime through an in-process standard OPC UA address space, and
`Opc.Ua.PubSub.Adapter` optionally bridges configured DataSets and
Actions to an external OPC UA server over a managed client session.

```text
┌──────────────────────────────────┐      ┌──────────────────────────────────┐      ┌──────────────────────┐
│ Optional in-process server       │      │ Optional external-server adapter │      │ External OPC UA      │
│ Opc.Ua.PubSub.Server             │      │ Opc.Ua.PubSub.Adapter            │◀────▶│ server endpoint      │
│ PublishSubscribe Object ·        │      │ Sources · sinks · Action handler │      │ Read / Write / Call  │
│ methods · diagnostics binding    │      │ over ManagedSession              │      └──────────────────────┘
└──────────────────────────────────┘      └──────────────────────────────────┘
                 │ IPubSubApplication                      │ Sources / sinks / Action handler
                 ▼                                          ▼
┌────────────────────────────────────────────────────────────────────┐
│                            Opc.Ua.PubSub                           │
│                                                                    │
│  Application/  PubSubApplication · PubSubApplicationBuilder        │
│                IPubSubApplication · MetaDataPublisher              │
│  Configuration/ PubSubConfigurationSnapshot · validator · XML      │
│  Connections/  IPubSubConnection · UaPubSubConnection              │
│  Groups/       WriterGroup · ReaderGroup                           │
│  DataSets/     Published / Subscribed / Source / Sink              │
│  Encoding/     UADP, JSON encoders/decoders, Discovery, Action     │
│  MetaData/     IDataSetMetaDataRegistry                            │
│  Scheduling/   IPubSubScheduler · PubSubSchedule                   │
│  Security/     UadpSecurityWrapper · KeyRing · NonceLayout · KAT   │
│  Security/Sks/ ISecurityKeyService · OpcUaSecurityKeyServiceClient │
│                InMemoryPubSubKeyServiceServer · PullSecurityKey    │
│  StateMachine/ PubSubStateMachine                                  │
│  Transports/   IPubSubTransportFactory · IPubSubTransport          │
│  DependencyInjection/ AddPubSub · AddPubSubSecurityKeyService*     │
└────────────────────────────────────────────────────────────────────┘
        ▲                                ▲                       ▲
        │ IPubSubTransportFactory        │                       │
        │                                │                       │
┌─────────────────┐  ┌──────────────────────┐  ┌────────────────────┐
│ Opc.Ua.PubSub.  │  │ Opc.Ua.PubSub.Mqtt   │  │ third-party plugin │
│      Udp        │  │ MQTTnet 4 / 5        │  │ (custom transport) │
└─────────────────┘  └──────────────────────┘  └────────────────────┘
```

The **state machine** (`PubSubStateMachine`,
[Part 14 §6.2.1](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.1))
is the spine: every primitive (application, connection, group,
writer, reader) owns an instance, parents cascade enable / disable into
their children, and the sub-tree refuses to start unless its
configuration validates clean
(`PubSubConfigurationValidator`, [Part 14 §6.2.5](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.5)).

## Core abstractions

### `IPubSubApplication`

The runtime root. Holds the connections, the metadata registry, the
diagnostics aggregator and the state machine.
([Part 14 §9.1.2](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/9.1.2)).

```csharp
public interface IPubSubApplication : IAsyncDisposable
{
    string ApplicationId { get; }
    IReadOnlyList<IPubSubConnection> Connections { get; }
    IDataSetMetaDataRegistry MetaDataRegistry { get; }
    PubSubStateMachine State { get; }
    IPubSubDiagnostics Diagnostics { get; }
    ConfigurationVersionDataType ConfigurationVersion { get; }

    event EventHandler<PubSubConfigurationChangedEventArgs>? ConfigurationChanged;

    ValueTask StartAsync(CancellationToken cancellationToken = default);
    ValueTask StopAsync(CancellationToken cancellationToken = default);

    PubSubConfigurationDataType GetConfiguration();
    ValueTask<IList<StatusCode>> ReplaceConfigurationAsync(
        PubSubConfigurationDataType configuration,
        CancellationToken cancellationToken = default);
    ValueTask<NodeId> AddConnectionAsync(
        PubSubConnectionDataType configuration,
        CancellationToken cancellationToken = default);
    ValueTask<NodeId> AddWriterGroupAsync(
        NodeId connectionId, WriterGroupDataType configuration,
        CancellationToken cancellationToken = default);
    ValueTask<NodeId> AddReaderGroupAsync(
        NodeId connectionId, ReaderGroupDataType configuration,
        CancellationToken cancellationToken = default);
    ValueTask<NodeId> AddDataSetWriterAsync(
        NodeId writerGroupId, DataSetWriterDataType configuration,
        CancellationToken cancellationToken = default);
    ValueTask<NodeId> AddDataSetReaderAsync(
        NodeId readerGroupId, DataSetReaderDataType configuration,
        CancellationToken cancellationToken = default);
    ValueTask<NodeId> AddPublishedDataSetAsync(
        PublishedDataSetDataType configuration,
        CancellationToken cancellationToken = default);
    ValueTask RemoveConnectionAsync(NodeId connectionId,
        CancellationToken cancellationToken = default);
    // ... RemoveGroupAsync / RemoveDataSetWriterAsync / RemoveDataSetReaderAsync
    // ... RemovePublishedDataSetAsync
}
```

The mutation methods implement the
[Part 14 §9.1.6](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/9.1.6)
runtime configuration model — every method is the runtime counterpart of
a `PublishSubscribe` Object Method and raises
`ConfigurationChanged` so the optional address-space layer can mirror
the change.

### `PubSubConnection` / `WriterGroup` / `ReaderGroup`

`IPubSubConnection` owns one `IPubSubTransport` plus 0..N
`WriterGroup` and 0..N `ReaderGroup` children
([Part 14 §6.2.6](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.6)).
Groups own writers / readers and drive the publishing / receive
schedule via `IPubSubScheduler` ([§6.4.1](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.4.1)).
When a `WriterGroup` has `KeepAliveTime > 0`, the scheduler emits a
KeepAlive NetworkMessage whenever the group has not sent a
DataSetMessage during that interval.

### `DataSetWriter` / `DataSetReader`

`DataSetWriter` projects a published DataSet into a NetworkMessage
stream
([§6.2.6.1](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.6.1)).
`DataSetReader` consumes one and writes to its target sink
([§6.2.7](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.7)).
Filters honoured: `PublisherId`, `WriterGroupId`, `DataSetWriterId`,
`DataSetClassId`, `MessageReceiveTimeout`.
`DataSetClassId` mismatches are rejected before the message reaches the
sink. `MessageReceiveTimeout > 0` moves the reader to `PubSubState.Error`
when no matching message arrives within the configured idle window.

### `IDataSetMetaDataRegistry`

Pub/sub-shared registry keyed by
`(PublisherId, WriterGroupId, DataSetWriterId, DataSetClassId,
MajorVersion)`
([§6.2.2.4](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.2.4)).
The publisher-side `MetaDataPublisher` ([§6.2.2.5](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.2.5))
emits a retained `JsonMetaDataMessage` / `UadpDiscoveryResponseMessage`
on the well-known `ua-metadata` topic at startup and after each
configuration version bump; subscribers cache it before the first
KeyFrame arrives.

Subscribers can also **actively** request discovery information with
`IPubSubApplication.RequestDiscoveryAsync(...)` (Part 14 §7.2.4.6): it sends a
`UadpDiscoveryRequestMessage` for `DataSetMetaData`,
`DataSetWriterConfiguration`, or `PublisherEndpoints` and collects the publisher
responses within a timeout into a typed `PubSubDiscoveryResult`. Publisher
connections answer inbound discovery requests for all three types.

### `IPubSubSecurityPolicy` / `IPubSubSecurityKeyProvider`

`IPubSubSecurityPolicy` describes a Part 14 §8 cipher bundle (signing
length, encrypting length, nonce length, `Sign` / `Encrypt` /
`Decrypt` primitives). Three policies ship in the box: `None`,
`AES-128-CTR`, `AES-256-CTR`. `IPubSubSecurityKeyProvider` is the
per-`SecurityGroupId` source of `PubSubSecurityKey`s the wrapper
uses; `StaticSecurityKeyProvider` keeps a fixed ring,
`PullSecurityKeyProvider` calls an SKS endpoint
([§8.4](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/8.4)).

### `IPubSubKeyServiceServer`

Bound by the in-memory SKS implementation. Exposes the standard
`GetSecurityKeys` Method on a `SecurityGroupType` Object so a
`PullSecurityKeyProvider` from a remote subscriber can call it.

## Fluent builder walkthrough

The fluent `PubSubApplicationBuilder` mirrors the DI-flavoured
`AddPubSub(...)` extensions but works without an
`IServiceCollection`. Use it from samples, tests, or any caller that
does not own a generic host. Every `With*` / `Add*` / `Use*` method
returns the builder; `Build()` materialises the
`IPubSubApplication`.

### Publisher — UDP / UADP

```csharp
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Transports;

ITelemetryContext telemetry = DefaultTelemetry.Create(b => b.AddConsole());

var pb = new PubSubApplicationBuilder(telemetry)
    .WithApplicationId("urn:opcfoundation:Sample:Publisher")
    .UseAllStandardEncoders()
    .AddTransportFactory(new UdpPubSubTransportFactory(telemetry))
    .AddDataSetSource("Boiler", new SampleBoilerDataSetSource())
    .AddUdpConnection("urn:Connection-1",
        publisherId: PublisherId.FromUInt16(1),
        endpointUrl: "opc.udp://239.0.0.1:4840")
    .AddWriterGroup("WG-1", writerGroupId: 100,
        period: TimeSpan.FromMilliseconds(1000),
        keepAliveTime: TimeSpan.FromSeconds(10))
    .AddDataSetWriter("Writer-1", dataSetWriterId: 1, dataSetName: "Boiler",
        contentMask: UadpDataSetMessageContentMask.Status
                   | UadpDataSetMessageContentMask.SequenceNumber);

await using IPubSubApplication application = await pb.BuildAndStartAsync();
```

The `Add*` extension methods in
`PubSubApplicationBuilderExtensions` translate transport / writer
configuration into Part 14
`PubSubConnectionDataType` / `WriterGroupDataType` /
`DataSetWriterDataType` instances and append them to the inline
configuration the builder will hand off to the runtime.

### Subscriber — MQTT / JSON

```csharp
using Microsoft.Extensions.Logging;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.DataSets;
using Opc.Ua.PubSub.Encoding.Json;
using Opc.Ua.PubSub.Transports;

ITelemetryContext telemetry = DefaultTelemetry.Create(b => b.AddConsole());

var pb = new PubSubApplicationBuilder(telemetry)
    .WithApplicationId("urn:opcfoundation:Sample:Subscriber")
    .UseAllStandardEncoders()
    .AddTransportFactory(new MqttPubSubTransportFactory(telemetry))
    .AddDataSetSink("Boiler", new ConsoleSink())
    .AddMqttConnection("urn:Connection-1",
        endpointUrl: "mqtt://localhost:1883",
        topicFilter: "Quickstarts/Reference/+")
    .AddReaderGroup("RG-1", readerGroupId: 200)
    .AddDataSetReader("Reader-1", dataSetReaderId: 1, dataSetName: "Boiler",
        publisherId: PublisherId.FromUInt16(1),
        writerGroupId: 100, dataSetWriterId: 1,
        encoding: JsonEncodingMode.Compact)
    .WriteToTargetVariables(); // map to address-space variables

await using IPubSubApplication application = await pb.BuildAndStartAsync();
```

### XML configuration mode

Both the publisher and subscriber accept a Part 14 v1.05.06
configuration file via `UseConfigurationFile(path)`; the file is
loaded by `XmlPubSubConfigurationStore`, validated, and watched for
hot-reload changes:

```csharp
var pb = new PubSubApplicationBuilder(telemetry)
    .WithApplicationId("urn:opcfoundation:Sample:Publisher")
    .UseAllStandardEncoders()
    .AddTransportFactory(new UdpPubSubTransportFactory(telemetry))
    .UseConfigurationFile("publisher.xml");

await using IPubSubApplication application = await pb.BuildAndStartAsync();
```

The XML schema is the OPC UA-defined `PubSubConfigurationDataType`
binary-encoded inside an
`UABinaryFileDataType` envelope — the same format the
`PublishSubscribe.PubSubConfiguration` File Object emits / accepts.

### Inline `PubSubConfigurationDataType`

For tests and samples that want to spell out the configuration
imperatively, hand a fully-populated
`PubSubConfigurationDataType` to `UseConfiguration(...)`:

```csharp
var pb = new PubSubApplicationBuilder(telemetry)
    .WithApplicationId("urn:opcfoundation:Sample:Publisher")
    .UseAllStandardEncoders()
    .AddTransportFactory(new UdpPubSubTransportFactory(telemetry))
    .UseConfiguration(PublisherConfigurationBuilder.Build(/*...*/));
await using IPubSubApplication application = await pb.BuildAndStartAsync();
```

## Dependency injection / hosting

The DI surface plugs the PubSub runtime into the
`Microsoft.Extensions.DependencyInjection` container exactly the same
way the rest of the stack does — see
[Dependency Injection](DependencyInjection.md).

```csharp
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddOpcUa()
    .AddPubSub(pubsub =>
    {
        pubsub.AddPublisher()
            .AddUdpTransport()
            .AddMqttTransport()
            .AddSecurityKeyProvider(SampleSecurity.CreateKeyProvider())
            .AddDataSetSource("Simple", new MyDataSetSource())
            .ConfigureApplication(app => app
                .WithApplicationId("urn:opcfoundation:ConsoleReferencePubSub:Publisher")
                .UseConfigurationFile("publisher.xml"));
    });

IHost host = builder.Build();
await host.RunAsync();
```

The `AddPubSub(Action<IPubSubBuilder>)` overload hands a fluent
`IPubSubBuilder` to the callback. It removes the need to pre-register a
hand-rolled `IPubSubApplication` factory: `ConfigureApplication` runs the
supplied callbacks against the `PubSubApplicationBuilder` after the
builder has auto-added every registered `IPubSubTransportFactory`,
security key provider, dataset source and sink. A default
`IPubSubApplication` is still registered, so the direct
`AddPubSub(Action<PubSubApplicationOptions>?)` / `AddPubSub(IConfiguration)`
overloads keep working unchanged.

DI extension methods provided by `Opc.Ua.PubSub`:

| Extension                                  | Description                                                        |
| ------------------------------------------ | ------------------------------------------------------------------ |
| `AddPubSub(Action<IPubSubBuilder>)`        | Fluent composition root. Exposes `AddPublisher` / `AddSubscriber`, `ConfigureApplication`, `AddSecurityKeyProvider`, `AddDataSetSource`, `AddSubscribedDataSetSink`, `UseConfiguration` / `UseConfigurationFile`, `Configure`, plus the transport extensions. |
| `AddPubSub(Action<PubSubApplicationOptions>?)` | Registers the `IPubSubApplication`, its hosted-service driver, all standard encoders/decoders, the scheduler, the diagnostics aggregator and the security policies. |
| `AddPubSub(IConfiguration)`                | Same, binding `PubSubApplicationOptions` from the `OpcUa:PubSub` section. |
| `AddPubSubPublisher` / `AddPubSubSubscriber` | Convenience aliases. Both register the full surface; "publisher" / "subscriber" only changes the `Role` field on the options bag. |
| `AddPubSubSecurityKeyServiceClient(Action<PullSecurityKeyProviderOptions>?)` | Configures the per-group `PullSecurityKeyProvider` so subscribers can pull keys from a remote SKS. |
| `AddPubSubSecurityKeyServiceServer(Action<InMemoryPubSubKeyServiceServer>?)` | Registers an in-process SKS with optional initial groups. |

Transport-specific extensions
(`Opc.Ua.PubSub.Udp` / `.Mqtt`) supply the matching
`IPubSubTransportFactory` and now hang off `IPubSubBuilder` — a transport
only makes sense together with the PubSub feature:

- `IPubSubBuilder.AddUdpTransport(Action<UdpTransportOptions>?)` — UDP
  unicast / multicast / broadcast.
- `IPubSubBuilder.AddMqttTransport(Action<MqttConnectionOptions>?)` —
  MQTT 3.1.1 + 5.0 via MQTTnet.

> The legacy `IOpcUaBuilder.AddUdpTransport` / `AddMqttTransport`
> overloads remain as `[Obsolete]` forwarders for source compatibility;
> move them into the `AddPubSub(pubsub => …)` callback.

Server-side address space — see
[Server-side address space](#server-side-address-space):

- `IOpcUaServerBuilder.AddPubSub(Action<PubSubServerOptions>?)` adds
  the `PublishSubscribe` Object onto the hosted server (returns
  `IPubSubServerBuilder` for chaining).

## Transports

### UDP / UADP

Implemented in `Opc.Ua.PubSub.Udp`. Wire profile
[`PubSub UDP UADP`](http://opcfoundation.org/UA-Profile/Transport/pubsub-udp-uadp).
Supports unicast, IPv4 multicast, IPv6 multicast and limited
broadcast. The transport honours the
`DatagramConnectionTransport2DataType` v2 fields
([Part 14 §6.4.2](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.4.2)):

| Field                      | Meaning                                                              |
| -------------------------- | -------------------------------------------------------------------- |
| `DiscoveryAnnounceRate`    | Number of NetworkMessages between unsolicited discovery announcements. |
| `DiscoveryMaxMessageSize`  | Hard cap on the size of a discovery NetworkMessage.                  |
| `QosCategory`              | Maps to the IPv4/IPv6 DSCP TOS byte applied to outgoing datagrams.   |
| `MessageRepeatCount`       | How many times the publisher re-sends the same NetworkMessage.       |
| `MessageRepeatDelay`       | Delay between repeats; receivers deduplicate using `SequenceNumber`. |


### DTLS / UADP (`opc.dtls://`)

`Opc.Ua.PubSub.Udp` also implements the Part 14 §7.3.2.4 DTLS transport for
unicast UADP PubSub endpoints. Use `opc.dtls://host:4843` (default port 4843).
Multicast and broadcast DTLS endpoints are rejected fail-closed.

Register DTLS with the UDP transport fluent/DI extension:

```csharp
services.AddOpcUa()
    .AddPubSub(pubsub => pubsub
        .AddPublisher()
        .AddSubscriber()
        .AddUdpTransport()
        .WithDtls(options =>
        {
            // Register one or more local ECC certificates (with private keys). The handshake
            // selects the certificate whose ECDsa named curve matches the negotiated profile
            // certificate curve, similar to how secure channels register an application
            // certificate per certificate type.
            options.LocalCertificates.Add(nistP256EccCertificate);
            options.LocalCertificates.Add(nistP384EccCertificate);

            // Optional: express a preferred profile. This is only a preference, not a hard pin.
            options.PreferredProfileName = "ECC_nistP256_AesGcm";

            // Optional: disable profiles at configuration time even when the runtime supports them.
            options.DisabledProfiles.Add("ECC_brainpoolP256r1_ChaChaPoly");

            options.PeerCertificateValidator = certificateValidator;
        }));
```

The cipher suite/profile is selected at runtime from the enabled and runtime-supported set: the
endpoint and `PreferredProfileName` only express a preference, while `DisabledProfiles` removes
profiles from the candidate set even when the runtime supports them. Selection fails closed with a
`NotSupportedException` when every supported profile is disabled or no profile is available on the
current BCL/runtime.

A publisher and subscriber use the normal PubSub connection model; only the
network address changes to `opc.dtls://` and the configured DTLS profile must be
supported by the current BCL/runtime:

```csharp
var publisher = new PubSubConnectionDataType
{
    Name = "dtls-publisher",
    Address = new ExtensionObject(new NetworkAddressUrlDataType
    {
        Url = "opc.dtls://127.0.0.1:4843"
    }),
    WriterGroups = [writerGroup]
};

var subscriber = new PubSubConnectionDataType
{
    Name = "dtls-subscriber",
    Address = new ExtensionObject(new NetworkAddressUrlDataType
    {
        Url = "opc.dtls://127.0.0.1:4843"
    }),
    ReaderGroups = [readerGroup]
};
```

DTLS uses .NET BCL cryptography only. Unsupported primitives are never
substituted or downgraded: the profile is not registered and `Resolve(...)` /
transport open throws a clear `NotSupportedException`.

| Profile family | net8/net9/net10 status | netstandard2.1 status | net48 status |
| -------------- | ---------------------- | --------------------- | ------------ |
| NIST P-256/P-384 + AES-128/256-GCM | Implemented when `AesGcm` and the named curve are available. | No AEAD profile registered. | None. |
| NIST P-256/P-384 + ChaCha20-Poly1305 | Implemented when `ChaCha20Poly1305.IsSupported` and the named curve are available. | Not registered. | None. |
| NIST P-256/P-384 integrity-only (`TLS_SHA256_SHA256` / `TLS_SHA384_SHA384`) | Implemented. | Compiles; profiles are not registered because raw ECDHE is unavailable below net8. | None. |
| Brainpool P256r1/P384r1 + AES-GCM / ChaCha20 / integrity-only | Implemented only on platforms where the BCL can create the Brainpool curve OID. | Not registered. | None. |
| Curve25519 / Curve448 mandatory profiles | Unsupported: .NET BCL has no portable X25519/X448 API; fail-closed. | Unsupported. | Unsupported. |

Peer authentication reuses the injected stack `CertificateValidator` /
certificate stores. Certificates must be ECC/ECDSA and match the selected profile
hash strength. DTLS records enforce sequence-number protection and anti-replay
per RFC 9147.
### MQTT (3.1.1 / 5.0)

Implemented in `Opc.Ua.PubSub.Mqtt` on top of MQTTnet. Wire profiles
[`PubSub MQTT UADP`](http://opcfoundation.org/UA-Profile/Transport/pubsub-mqtt-uadp)
and
[`PubSub MQTT JSON`](http://opcfoundation.org/UA-Profile/Transport/pubsub-mqtt-json).
TFM matrix:

| Target                          | MQTTnet major |
| ------------------------------- | ------------- |
| `netstandard2.1`, `net48`, `net472` | v4 |
| `net8.0`, `net9.0`, `net10.0`   | v5 |

Highlights:

- Part 14 §7.3.4.7 topic layout with the spec default `opcua` prefix. Data,
  metadata, status, connection, application-information, and endpoint
  announcements are published on the standard `data`, `metadata`, `status`,
  `connection`, `application`, and `endpoints` topic segments. KeepAlive uses a
  data NetworkMessage with no DataSetMessages; there is no `keepalive` topic.
- MQTT Last-Will status presence is configured through `WillTopic`, `WillQos`,
  and `WillRetain` so subscribers see publisher disconnects on the status topic.
- `BrokerTransportQualityOfService` / `RequestedDeliveryGuarantee` maps to MQTT
  QoS 0/1/2; per-writer settings override the connection default.
- `BrokerWriterGroupTransportDataType.QueueName` and
  `BrokerDataSetWriterTransportDataType.QueueName` override the generated topic
  for a group or writer when a broker-specific queue name is required.
- `mqtt://`, `mqtts://`, and secure WebSocket `wss://` endpoint schemes are
  accepted. `AuthenticationProfileUri` selects MQTT 5 enhanced authentication.
- Retained messages are used for metadata and discovery-on-startup
  ([Part 14 §6.2.2.5](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.2.5)).
- `JsonNetworkMessageContentMask.SingleNetworkMessage` lifts the JSON
  array wrapper so each MQTT publish carries exactly one
  `JsonNetworkMessage`
  ([§7.2.5.3](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.5.3)).
- TLS, Anonymous, Username/Password, X.509-cert authentication.
- Reconnect with exponential back-off honoured at the connection
  state-machine level (no message loss on a re-subscribe at QoS ≥ 1).

```csharp
writer.WithTransportSettings(
    new BrokerDataSetWriterTransportDataType
    {
        QueueName = "opcua/json/data/Line1/Writer1",
        RequestedDeliveryGuarantee = BrokerTransportQualityOfService.AtLeastOnce
    });

var options = new MqttConnectionOptions
{
    Endpoint = "wss://broker.example.com/mqtt",
    AuthenticationProfileUri = "http://opcfoundation.org/UA-Profile/Transport/pubsub-mqtt-json",
    WillTopic = "opcua/json/status/Line1"
};
```

### DTLS transport status

The `opc.dtls://` transport URI is parsed for Part 14 §7.3.2.4 unicast endpoints
and wired through the UDP transport factory when `.WithDtls(...)` is registered.
The runtime profile registry is fail-closed: Curve25519 / Curve448 profiles are
not registered because the portable .NET BCL does not expose RFC 7748 ECDH APIs,
and optional NIST / Brainpool profiles are registered only when the required BCL
cipher, HKDF, and ECDH curve probes succeed. The DTLS 1.3 handshake and record
protection are still pending, so opening a registered DTLS endpoint throws a
clear TODO(S3) error instead of sending unprotected PubSub payloads.

## Encodings

### UADP — `Opc.Ua.PubSub.Encoding.Uadp`

Implements [Part 14 §7.2.4](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4)
in full:

- All `UadpNetworkMessageContentMask` flags (`PublisherId`,
  `GroupHeader`, `WriterGroupId`, `GroupVersion`,
  `NetworkMessageNumber`, `SequenceNumber`, `PayloadHeader`,
  `Timestamp`, `PicoSeconds`, `DataSetClassId`, `Promoted*`,
  `ReplyTo`).
- All `UadpDataSetMessageContentMask` flags including
  `Status`, `MajorVersion`, `MinorVersion`, `SequenceNumber`,
  `Timestamp`, `PicoSeconds`.
- `Variant`, `RawData`, `DataValue` per-field encoding
  ([§7.2.4.5.4](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.5.4)).
- KeyFrame / DeltaFrame / Event / KeepAlive `MessageType`s.
- Discovery NetworkMessages
  ([§7.2.4.7](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.7)) —
  Request / Response / DataSetMessage variants.
- **Chunking** ([§7.2.4.6](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.6))
  splits NetworkMessages whose encoded length exceeds the
  configured `MaxNetworkMessageSize` into ChunkData /
  ChunkData-Final fragments at the byte level; the receive side
  reassembles via `UadpReassembler`.
- **RawData padding**
  ([§7.2.4.5.11](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.5.11))
  pads strings, byte-strings, XML elements and arrays to the
  declared `MaxStringLength` / `ArrayDimensions`; the on-wire length
  prefix is suppressed; decoders trim the trailing NUL fill on read.

### JSON — `Opc.Ua.PubSub.Encoding.Json`

Implements [Part 14 §7.2.5](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.5)
on top of `System.Text.Json`. The encoder is allocation-friendly
(no Newtonsoft.Json dependency) and supports the v1.05.06 modes:

| Mode      | Spec                                                  | Wire shape                    |
| --------- | ----------------------------------------------------- | ----------------------------- |
| `Verbose` | [§7.2.5.4](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.5.4) | Field is a Variant envelope.   |
| `Compact` | [§7.2.5.4](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.5.4) | Bare value; metadata required. |
| `RawData` | [§7.2.5.4](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.5.4) | Bare bytes-as-base64 / numeric.|

Additional v1.05.06 flavours:

- `JsonActionNetworkMessage`
  ([§7.2.5.6](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.5.6)) —
  side-channel Actions using the spec `MessageType` strings
  `ua-action-request`, `ua-action-response`, `ua-action-metadata`, and
  `ua-action-responder`.
- `JsonDiscoveryMessage`
  ([§7.2.5.7](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.5.7)) —
  application, endpoint, status, connection, and metadata discovery messages using
  `ua-application`, `ua-endpoints`, `ua-status`, `ua-connection`, and
  `ua-metadata`.
- `SingleNetworkMessage` mode flips the JSON array wrapper off, so
  each MQTT publish maps 1:1 to a single `JsonNetworkMessage`.

## Discovery

Discovery implements the Part 14 §7.2.4.6, §7.2.5.7, and §7.3.4.7 surfaces for
subscribers that need to find publishers and bind to metadata at runtime.

- UDP uses the standard discovery multicast address `opc.udp://224.0.2.14:4840`
  when no deployment-specific discovery address is configured.
- `DatagramConnectionTransport2DataType.DiscoveryAnnounceRate` enables periodic
  unsolicited announcements. The runtime also announces after configuration
  version changes so subscribers can refresh cached metadata.
- Publishers respond to probes for `DataSetMetaData`,
  `DataSetWriterConfiguration`, `PublisherEndpoints`, `PubSubConnection`,
  `ApplicationInformation`, and WriterGroup-by-id filters.
- Probe traffic reduction is built in: probe requests use jittered retry/backoff,
  duplicate probes are suppressed, and identical responses are throttled.
- MQTT publishes retained discovery messages on the standard status, connection,
  application, endpoint, and metadata topics.

```csharp
PubSubDiscoveryResult result = await application.RequestDiscoveryAsync(
    new PubSubDiscoveryRequest
    {
        DiscoveryType = UadpDiscoveryType.DataSetMetaData,
        DataSetWriterIds = [1, 2]
    },
    timeout: TimeSpan.FromSeconds(5));
```

## Security

Implemented in `Opc.Ua.PubSub.Security`. Implements
[Part 14 §7.2.4.4.3](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.4.3)
(send / receive flow) and
[Annex A.2.1.6 / A.2.2.5](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/A.2.1.6)
(byte layout).

### `UadpSecurityWrapper`

Wraps an unsecured outer-prefix + inner-payload pair into the
`[prefix || SecurityHeader || ciphertext || signature]` frame. On
receive verifies the signature, replay-checks the
`SecurityTokenId` and `MessageNonce`, and decrypts. Three modes:

```csharp
public enum UadpSecurityWrapOptions
{
    SignOnly,
    EncryptOnly,
    SignAndEncrypt   // default
}
```

### Cipher policies

- `PubSubNonePolicy` — no signing, no encryption.
- `PubSubAes128CtrPolicy` — AES-128-CTR encryption + HMAC-SHA-256 signing
  (NIST SP 800-38A F.5.1 KAT verified by
  `Tests/Opc.Ua.PubSub.Tests/Security/Internal/AesCtrTransformTests`).
- `PubSubAes256CtrPolicy` — AES-256-CTR + HMAC-SHA-256.

Lookup uses
`PubSubSecurityPolicyRegistry.Find(policyUri)` — the URIs match
[Part 7 §6.4](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/8).

### Key ring

`PubSubSecurityKeyRing` keeps a current key plus a sliding window of
past + future keys per `SecurityGroupId`. Replay protection is
enforced via `SecurityTokenWindow` ([§8.2](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/8.2));
nonce reuse is detected by `RandomNonceProvider` /
`AesCtrNonceLayout` ([§A.2.1.6](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/A.2.1.6)).

## Security Key Service (SKS)

`Opc.Ua.PubSub.Security.Sks` implements both sides of
[Part 14 §8.4](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/8.4)
for PubSub symmetric group-key distribution. This is intentionally
separate from the OPC 10000-12 KeyCredential services used by GDS and
resource-server credential push: SKS rotates and serves
`PubSubSecurityKey` material for SecurityGroups, while KeyCredential
issues or pushes application credentials. Server hosting may bridge SKS
security events into the normal server audit pipeline, but the core
PubSub SKS abstractions avoid a dependency on GDS/server
KeyCredential components.

### Pull (client)

```csharp
builder.Services.AddOpcUa()
    .AddPubSub(...)
    .AddPubSubSecurityKeyServiceClient(opt =>
    {
        opt.SecurityKeyServiceUri = "opc.tcp://sks.example.com:4840";
        opt.SecurityGroupId = "Group-1";
        opt.PollInterval = TimeSpan.FromSeconds(30);
    });
```

The `PullSecurityKeyProvider` opens a managed session against the SKS
endpoint, calls `GetSecurityKeys` per
the configured poll interval, and feeds each rotated key into the
ring. Failure modes: `OpcUaSksException` carries the SKS-side
StatusCode; the consumer falls back to the cached future keys until
the next poll succeeds.

### Push targets and in-memory server

The push model (Part 14 §8.3/§8.4) is available alongside the pull client.
Register a `PushSecurityKeyProvider` for each SecurityGroup that should accept
remote `SetSecurityKeys` calls, and expose `PubSubKeyPushTargets` through the
server address space when hosting a server-side PubSub configuration.

```csharp
builder.Services.AddOpcUa()
    .AddPubSub(pubsub => pubsub.AddSubscriber())
    .AddPubSubSecurityKeyPushTarget("Group-1");

builder.Services.AddOpcUa()
    .AddServer(opt => opt.ApplicationName = "PubSubSubscriber")
        .AddPubSub()
        .WithSecurityKeyPushTarget("Group-1");
```

`InMemoryPubSubKeyServiceServer` exposes the `SecurityGroupType` Method handlers
(`GetSecurityKeys`, `SetSecurityKeys`, `GetSecurityGroup`, `AddSecurityGroup`,
`RemoveSecurityGroup`, `InvalidateKeys`, and `ForceKeyRotation`) and rotates keys
on its own timer. Use it for tests, single-process scenarios, and any deployment
where a dedicated GDS-hosted SKS is overkill.

```csharp
builder.Services.AddOpcUa()
    .AddPubSubSecurityKeyServiceServer(server =>
    {
        server.AddSecurityGroup(
            new SksSecurityGroup("Group-1", PubSubSecurityPolicyUri.Aes128Ctr));
    });
```

Remote SKS administration honours the server `RolePermissions`: callers must be
authorized for the target SecurityGroup Methods before `GetSecurityGroup`,
`SetSecurityKeys`, `InvalidateKeys`, or `ForceKeyRotation` succeeds.

## Actions (request/response)

OPC UA Part 14 **Actions** add a request/response interaction over PubSub (the
PubSub analogue of a Client/Server `Call`). A *requester* publishes an action
request to an *action target*; one or more *responders* execute it and publish a
correlated action response.

The stack implements Actions over both encodings and transports:

- **Messages** — JSON (`ua-action-request`, `ua-action-response`,
  `ua-action-metadata`, or `ua-action-responder` NetworkMessages carrying the
  generated `Opc.Ua.JsonActionRequestMessage` / `JsonActionResponseMessage` /
  `JsonActionMetaDataMessage`) and UADP (`UadpActionRequestMessage` /
  `UadpActionResponseMessage` via `UadpActionCoder`, ExtendedFlags2 action
  discriminator). UADP action payloads flow through the normal UADP message
  security (Aes-CTR + SKS); JSON confidentiality is the MQTT TLS transport.
- **Published actions** — `PublishedActionDataType` /
  `PublishedActionMethodDataType` (RequestDataSetMetaData + ActionTargets [+
  ActionMethods]) are modelled as an `IPublishedDataSetSource`; add them with
  `builder.AddPublishedAction(...)`.
- **Runtime** — `IPubSubApplication.InvokeActionAsync(PubSubActionRequest,
  timeout)` (requester, awaits the correlated `PubSubActionResponse` by
  RequestId + CorrelationData) and `RegisterActionHandler(target, handler)` /
  fluent `AddActionResponder(...)` (responder, with the `ActionState`
  Idle→Executing→Done lifecycle).
- **Server method binding** — `Opc.Ua.PubSub.Server`'s `ServerMethodActionHandler`
  binds an action to a real OPC UA method via `ActionMethodDataType`
  (ObjectId/MethodId), invoked through `IMasterNodeManager.CallAsync`; register
  with `WithActionMethodHandlers(dataSetWriterId, publishedActionMethod, ...)`.

```csharp
var target = new PubSubActionTarget { DataSetWriterId = 1, ActionTargetId = 1 };

// Responder: echo handler bound to an action target.
builder.AddActionResponder(target, (invocation, ct) =>
    new ValueTask<PubSubActionHandlerResult>(
        new PubSubActionHandlerResult { OutputFields = invocation.InputFields }));

// Requester: invoke and await the correlated response.
PubSubActionResponse response = await app.InvokeActionAsync(
    new PubSubActionRequest { Target = target, InputFields = inputFields },
    timeout: TimeSpan.FromSeconds(5));
```

Cites [Part 14 §7.2.5.6](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.5.6)
(Action NetworkMessage) and the Annex B Action data types.

## Server-side address space

`Opc.Ua.PubSub.Server` mounts the standard `PublishSubscribe` Object
([Part 14 §9.1](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/9.1))
onto a hosted OPC UA server. Wiring is one chain:

```csharp
builder.Services.AddOpcUa()
    .AddServer(opt => opt.ApplicationName = "RefServerWithPubSub")
        .AddPubSub();          // <-- PublishSubscribe Object + methods + diagnostics
builder.Services.AddOpcUa()
    .AddPubSub(opt => opt.ConfigurationFilePath = "pubsub.xml");
```

What the server side adds:

1. A `PubSubNodeManager` that materialises the Part 14 §9.1 Information Model as
   browsable address-space nodes:
   - `PublishSubscribe` Object instance with `Status` / `State`,
     `ConfigurationVersion`, `PubSubConfiguration`, and
     `PubSubKeyPushTargetFolder`.
   - One Object per `PubSubConnection`, `WriterGroup`, `ReaderGroup`,
     `DataSetWriter`, `DataSetReader`, `PublishedDataSet`, and DataSet folder.
   - Per-instance `Status` / `State` and `ConfigurationVersion` Variables so a
     client can observe the same runtime state the scheduler uses.
2. Method bindings ([§9.1.5](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/9.1.5)):
   `AddConnection`, `RemoveConnection`, per-instance `AddWriterGroup`,
   `AddReaderGroup`, `AddDataSetWriter`, `AddDataSetReader`, `Remove*`,
   `Enable`, and `Disable`, plus `AddPublishedDataItems`,
   `AddPublishedEvents`, `AddPublishedDataItemsTemplate`, `AddVariables`,
   `RemoveVariables`, `AddDataSetFolder`, and `RemoveDataSetFolder`.
3. `PubSubConfigurationType` File import/export: clients can open/read the
   current `PubSubConfigurationDataType` file or write a replacement file; the
   server applies it through `ReplaceConfigurationAsync` and returns per-item
   status codes.
4. SKS Method bindings: `AddSecurityGroup`, `RemoveSecurityGroup`,
   `GetSecurityKeys`, `SetSecurityKeys`, `GetSecurityGroup`, `InvalidateKeys`,
   and `ForceKeyRotation`, protected by the server `RolePermissions`.
5. Per-component diagnostics
   ([§9.1.11](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/9.1.11)):
   `IPubSubDiagnostics` for the application, every connection, every
   group, every writer / reader. Counters surfaced as Variables under
   each Object: `TotalInformation`, `TotalError`, `Reset`, plus the
   spec live counters (`SentNetworkMessages`,
   `ReceivedNetworkMessages`, `FailedTransmissions`, `EncryptionErrors`,
   `DecryptionErrors`, `Reset`, etc.).

Example client-side use through the standard Methods:

```csharp
// Browse PublishSubscribe, then Call its AddWriterGroup Method.
ArrayOf<Variant> outputArguments = await session.CallAsync(
    publishSubscribeNodeId,
    addWriterGroupMethodId,
    connectionNodeId,
    writerGroupConfiguration);

// Export/import the active configuration through PubSubConfigurationType.
await fileTransfer.ReadAsync(pubSubConfigurationFileNodeId, destinationStream);
await fileTransfer.WriteAsync(pubSubConfigurationFileNodeId, replacementStream);
```

The `IPubSubServerBuilder` returned by `AddPubSub()` lets you
register optional companion features
(`WithSecurityKeyPushTarget`, `WithSecurityKeyServiceServer`, etc.).
See `Libraries/Opc.Ua.PubSub.Server/Hosting/IPubSubServerBuilder.cs`.

## Binding PubSub to an external OPC UA server (client-session adapters)

`Opc.Ua.PubSub.Adapter` connects the Part 14 PubSub runtime to an external OPC UA server by using `Opc.Ua.Client.ManagedSession`. It is a client-session binding package: the PubSub process remains a publisher, subscriber, or Action responder, while the source variables, target variables, or methods live in another OPC UA server.

Use this package when you need to bridge an existing server into PubSub without hosting that server in the same process. Use `Opc.Ua.PubSub.Server` for the in-process server address-space integration that exposes the standard Part 14 `PublishSubscribe` Object and binds to node managers directly.

### Package and namespaces

| Item | Value |
| ---- | ----- |
| Assembly | `Opc.Ua.PubSub.Adapter` |
| NuGet package | `OPCFoundation.NetStandard.Opc.Ua.PubSub.Adapter` |
| Main namespaces | `Opc.Ua.PubSub.Adapter`, `Opc.Ua.PubSub.Adapter.Session`, `Opc.Ua.PubSub.Adapter.Actions`, `Opc.Ua.PubSub.Adapter.DependencyInjection` |
| DI entry points | `AddServerAsPublisher`, `AddServerAsSubscriber`, `AddServerAsActionResponder` on `IPubSubBuilder` |

The adapter implements Part 14 DataSet and Action seams rather than a new transport. You still register UDP, MQTT, encoders, security key providers, and the PubSub configuration through the normal `AddPubSub` builder.

### Architecture

The DI extensions create one `IServerSession` per adapter registration. `ServerSession` wraps a lazily connected `ManagedSession`; `Read`, `Write`, `Call`, and client data-change Subscriptions all go through that managed session. `ManagedSession` owns keep-alive and reconnect behavior, so adapter components do not expose reconnect handlers or custom retry APIs.

| Direction | Configuration source | Adapter seam | Managed session service |
| --------- | -------------------- | ------------ | ----------------------- |
| External server → PubSub | `PublishedDataSetDataType` with `PublishedDataItemsDataType` | `ServerPublishedDataSetSource : IPublishedDataSetSource` | `Read` or client `Subscription` data changes |
| PubSub → external server | `DataSetReaderDataType.SubscribedDataSet` as `TargetVariablesDataType` | `ServerSubscribedDataSetSink` and `ServerTargetVariableWriter : ITargetVariableWriter` | `Write` |
| PubSub Action → external server method | `PubSubActionTarget` plus `ActionMethodMap` | `ServerActionHandler : IPubSubActionHandler` | `Call` |

The PubSub configuration must be supplied before an `AddServerAs*` extension runs. The extensions enumerate configured PublishedDataSets, DataSetWriters, DataSetReaders, TargetVariables, and action targets during application composition and then register the appropriate sources, sinks, or handlers.

```csharp
builder.Services.AddOpcUa()
    .AddPubSub(pubsub => pubsub
        .AddPublisher()
        .AddUdpTransport()
        .UseConfigurationFile("publisher.xml")
        .AddServerAsPublisher(options =>
        {
            options.Connection.EndpointUrl = "opc.tcp://localhost:4840";
        }));
```

### Connection options

`ServerConnectionOptions` describes the client session to the external OPC UA server.

| Option | Type | Default | Notes |
| ------ | ---- | ------- | ----- |
| `EndpointUrl` | `string` | empty | Required endpoint or discovery URL, for example `opc.tcp://localhost:4840`. The session selects an advertised endpoint whose URL scheme matches this URI. |
| `SecurityMode` | `MessageSecurityMode` | `SignAndEncrypt` | Requested client/server message security mode. |
| `SecurityPolicyUri` | `string?` | `null` | Requested security policy URI. When `null`, the adapter chooses the highest-security endpoint advertised for the requested `SecurityMode`. |
| `UserIdentity` | `IUserIdentity?` | `null` | Explicit user identity. Takes precedence over `UserName` and `Password`. |
| `UserName` | `string?` | `null` | User name for username/password activation. Empty means anonymous unless `UserIdentity` is supplied. |
| `Password` | `string?` | `null` | Password used with `UserName`. |
| `SessionName` | `string` | `Opc.Ua.PubSub.Adapter` | Session name reported to the server. |
| `SessionTimeout` | `uint` | `60000` | Requested session timeout in milliseconds. |
| `ApplicationConfiguration` | `ApplicationConfiguration?` | `null` | Client application configuration used to create the session. Supply a configuration with a valid application instance certificate for secured connections. |
| `ApplicationName` | `string` | `Opc.Ua.PubSub.Adapter` | Used only when the adapter builds a minimal client configuration automatically. |

For secured connections, provide an `ApplicationConfiguration` that uses the stack certificate manager and normal trusted issuer, trusted peer, and rejected certificate stores. The automatic fallback configuration is useful for simple hosting scenarios, but production deployments should manage the client application certificate and trust lists explicitly.

### Configuration and hot reload

Adapter options are bound through the standard options pattern and are designed to support hot reload by rewiring only the writers, readers, or responders whose options changed while leaving unchanged components running. The configuration source is pluggable, so a change-feed-backed configuration service such as etcd can drive live reconfiguration. Full hot reload remains a planned follow-up and extension point; it is not fully implemented yet.

### Publisher adapter

`AddServerAsPublisher` registers an `IPublishedDataSetSource` for each configured PublishedDataSet that has a name. The PublishedDataSet must use `PublishedDataItemsDataType`; each `PublishedVariableDataType` becomes a `ReadValueId` using `PublishedVariable`, `AttributeId` (defaulting to `Attributes.Value`), and `IndexRange`.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Opc.Ua;
using Opc.Ua.PubSub.Adapter;
using Opc.Ua.PubSub.Adapter.Session;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
ApplicationConfiguration clientConfiguration = await LoadClientConfigurationAsync();

builder.Services.AddOpcUa()
    .AddPubSub(pubsub => pubsub
        .AddPublisher()
        .AddUdpTransport()
        .UseConfigurationFile("publisher.xml")
        .AddServerAsPublisher(options =>
        {
            options.Connection = new ServerConnectionOptions
            {
                EndpointUrl = "opc.tcp://localhost:4840",
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                ApplicationConfiguration = clientConfiguration,
                SessionName = "PubSub external publisher"
            };
            options.ReadMode = ReadMode.Cyclic;
        }));

await builder.Build().RunAsync();
```

#### Read modes

| Mode | Behavior | Trade-offs |
| ---- | -------- | ---------- |
| `ReadMode.Cyclic` | The publish cycle issues a `Read` service call for the current PublishedDataSet variables. | Simple and predictable; every cycle requests fresh values. Network and server load scale with the publish cadence and field count. |
| `ReadMode.Subscription` | The adapter creates client Subscriptions, adds monitored items for the referenced PublishedDataSet variables, maintains a latest-value cache from data-change notifications, primes that cache with one initial `Read`, and samples the cache during publish cycles. | Lower publish-path latency and server-driven updates. More lifecycle state: Subscriptions and monitored items must be created, applied, primed, and kept alive by the managed session. |

Cyclic mode is the default and is a good fit when the publish interval is modest, field counts are small, or the external server should only be sampled at the PubSub cadence. Subscription mode is a better fit when values change independently of the publish cadence, lower latency matters, or the external server can serve monitored items more efficiently than repeated Read calls.

#### Subscription affinity

`SubscriptionAffinity` controls how subscription-mode monitored items are grouped.

| Affinity | Behavior | Guidance |
| -------- | -------- | -------- |
| `WriterGroup` | One client Subscription per WriterGroup. The subscription publishing interval is the WriterGroup publishing interval, or 1000 ms when the WriterGroup interval is not set. This is the default. | Prefer this for most deployments because it aligns the client/server sampling group with the Part 14 WriterGroup cadence and reduces subscription count. |
| `DataSetWriter` | One client Subscription per DataSetWriter, using the owning WriterGroup publishing interval. | Use this when writers need isolation, when a server applies per-subscription limits or diagnostics that should map to one writer, or when you want to contain noisy datasets. |

For each affinity group, the coordinator de-duplicates monitored items by node and attribute, uses `PublishedVariableDataType.SamplingIntervalHint` when set, otherwise uses the group publishing interval, applies the monitored items server-side, and then primes the cache with a one-shot `Read`. Until a value is primed or a data change arrives, the cache returns `UncertainInitialValue` for that field.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Opc.Ua;
using Opc.Ua.PubSub.Adapter;
using Opc.Ua.PubSub.Adapter.Session;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
ApplicationConfiguration clientConfiguration = await LoadClientConfigurationAsync();

builder.Services.AddOpcUa()
    .AddPubSub(pubsub => pubsub
        .AddPublisher()
        .AddMqttTransport()
        .UseConfigurationFile("publisher.xml")
        .AddServerAsPublisher(options =>
        {
            options.Connection.EndpointUrl = "opc.tcp://localhost:4840";
            options.Connection.SecurityMode = MessageSecurityMode.SignAndEncrypt;
            options.Connection.SecurityPolicyUri = SecurityPolicies.Basic256Sha256;
            options.Connection.ApplicationConfiguration = clientConfiguration;
            options.ReadMode = ReadMode.Subscription;
            options.Affinity = SubscriptionAffinity.WriterGroup;
        }));

await builder.Build().RunAsync();
```

### Subscriber adapter

`AddServerAsSubscriber` registers a sink for every configured DataSetReader whose `SubscribedDataSet` is `TargetVariablesDataType`. The normal PubSub subscriber resolves incoming DataSet fields to `FieldTargetDataType` entries; the adapter writes each resolved field to the configured external node, attribute, and write index range.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Opc.Ua;
using Opc.Ua.PubSub.Adapter.Session;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
ApplicationConfiguration clientConfiguration = await LoadClientConfigurationAsync();

builder.Services.AddOpcUa()
    .AddPubSub(pubsub => pubsub
        .AddSubscriber()
        .AddUdpTransport()
        .UseConfigurationFile("subscriber.xml")
        .AddServerAsSubscriber(options =>
        {
            options.Connection = new ServerConnectionOptions
            {
                EndpointUrl = "opc.tcp://localhost:4840",
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                ApplicationConfiguration = clientConfiguration,
                SessionName = "PubSub external subscriber"
            };
        }));

await builder.Build().RunAsync();
```

The writer is fail-soft for service and transport faults: it logs the failure and returns a Bad status for that field so the receive loop can continue. Cancellation still propagates.

### Action responder adapter

`AddServerAsActionResponder` maps inbound PubSub Actions to external OPC UA Method Calls. `Targets` lists the `PubSubActionTarget` values that should be handled. `MethodMap` resolves each target to an external object and method, either by `(DataSetWriterId, ActionTargetId)` or by `ActionName`. Action input fields are converted to method input arguments in order. Method output arguments are converted back to Action response fields using the configured output field names; positions without names become `Output0`, `Output1`, and so on.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Opc.Ua;
using Opc.Ua.PubSub.Adapter.Actions;
using Opc.Ua.PubSub.Adapter.Session;
using Opc.Ua.PubSub.Application;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
ApplicationConfiguration clientConfiguration = await LoadClientConfigurationAsync();

var target = new PubSubActionTarget
{
    DataSetWriterId = 1001,
    ActionTargetId = 1,
    ActionName = "ResetMachine"
};

builder.Services.AddOpcUa()
    .AddPubSub(pubsub => pubsub
        .AddSubscriber()
        .AddMqttTransport()
        .UseConfigurationFile("actions.xml")
        .AddServerAsActionResponder(options =>
        {
            options.Connection.EndpointUrl = "opc.tcp://localhost:4840";
            options.Connection.SecurityMode = MessageSecurityMode.SignAndEncrypt;
            options.Connection.SecurityPolicyUri = SecurityPolicies.Basic256Sha256;
            options.Connection.ApplicationConfiguration = clientConfiguration;
            options.Targets.Add(target);
            options.MethodMap.Add(
                dataSetWriterId: 1001,
                actionTargetId: 1,
                objectId: new NodeId("ns=2;s=Machine1"),
                methodId: new NodeId("ns=2;s=Machine1.Reset"),
                outputFieldNames: new[] { "Accepted" }.ToArrayOf());
            options.AllowUnsecured = false;
        }));

await builder.Build().RunAsync();
```

Action responders honor the Part 14 security posture of the Action exchange. `AllowUnsecured` defaults to `false`; keep it false unless the deployment explicitly accepts unsecured Action requests and responses. With the default, the responder fails closed for unsecured action paths.

### Metadata behavior

Publisher metadata is configuration-first and server-fallback. The adapter builds the field set, order, and names from the configured `PublishedDataSetDataType`, its `PublishedDataItemsDataType.PublishedData`, and any declared `DataSetMetaDataType`. If a field does not declare type information, `DataSetMetaDataBuilder` reads `DataType`, `ValueRank`, and `ArrayDimensions` from the external server. If the fallback read fails, the field remains conservative: `BaseDataType`, `Variant`, scalar.

This behavior keeps Part 14 metadata stable when the configuration is complete and still lets a bridge infer missing type details from the source server during startup or the first publish sample.

A failed fallback read is **not** cached permanently. The builder retries resolution on each publish cycle (`ResolveAsync`) until the server read succeeds, and exposes `RefreshAsync` to force a fresh resolution on demand (for example from a model-change subscription or a scheduled refresh). When a (re)resolution changes the enriched metadata, the source raises `IMetaDataChangeNotifier.MetaDataChanged`; the owning `PublishedDataSet` then rebuilds and re-emits a DataSetMetaData message so subscribers observe the corrected field types without a restart.

### Browse-path node mapping

Any node id in the mapping configuration — published variables (read), target variables (write), and Action object/method ids (call) — may be expressed as a relative **browse path** instead of a concrete `NodeId`. A browse path is carried as a sentinel `NodeId` whose namespace-zero string identifier starts with `/` (hierarchical) or `.` (aggregates), for example `/2:Demo/2:CurrentTime`. Use `NodeBrowsePath.ToNodeId("/2:Demo/2:CurrentTime")`, or the `ActionMethodMap.Add(actionName, objectBrowsePath, methodBrowsePath)` overload. The adapter resolves browse paths against the server with `TranslateBrowsePathsToNodeIds` the first time the node is used and caches the result, so mappings can be authored without knowing the server-assigned identifiers in advance. Each segment is parsed with `QualifiedName.Parse`, so `2:Name` selects the target namespace; named reference types are not supported in this shorthand (supply a concrete `NodeId` for those).

### Lifecycle and resilience

The adapter registrations add `ServerAdapterRuntime` and `ServerAdapterHostedService`. The runtime owns sessions and subscription coordinators. On host start, subscription-mode publisher coordinators connect the session, create the client Subscriptions, add monitored items, apply changes, and prime caches. Cyclic publishers, subscribers, and action responders connect lazily on first service call. On host shutdown, coordinators and sessions are disposed.

`ManagedSession` handles keep-alive and reconnect for the underlying client session. Adapter read, write, and call components are fail-soft for ordinary service or transport faults: publisher fields become Bad-quality values, subscriber writes return Bad field status, and action failures return Bad action status. Cancellation and disposal still propagate normally. Recoverable, fail-soft faults are logged at `Information` level (not as warnings) so a transient outage does not spam the log.

### Observability

The adapter publishes metrics through a single `System.Diagnostics.Metrics.Meter` named `Opc.Ua.PubSub.Adapter` (`AdapterMetrics`, registered as a singleton). Counters cover read, write, method-call, and metadata-resolution activity with a success/failure split (`opcua.pubsub.adapter.reads` / `.read.failures`, `.writes` / `.write.failures`, `.calls` / `.call.failures`, `.metadata.resolutions` / `.metadata.failures`). Subscribe with the OpenTelemetry metrics SDK (or any `MeterListener`) to observe bridge health alongside the leveled logs.

### Security notes

The external client session uses the same stack security configuration model as other OPC UA clients. Use the certificate manager and trust stores described in [Certificates](Certificates.md) for application instance certificates, issuers, trusted peers, and rejected certificates. Prefer `MessageSecurityMode.SignAndEncrypt` with a SHA-2 security policy such as `SecurityPolicies.Basic256Sha256` or stronger policies supported by the server.

For Actions, leave `ServerActionResponderOptions.AllowUnsecured` at its default `false` unless an application-specific risk assessment requires otherwise. That gate is intentionally fail-closed.

### Sample

See `Applications\ConsoleReferencePubSub` (the `external` mode) for a complete host that wires PubSub configuration, transport registration, external session options, publisher/subscriber binding, and Action-to-Call mapping in one process.

### See also

- [Dependency Injection](DependencyInjection.md)
- [Sessions, Reconnection, and Subscription Engines](Sessions.md)
- [Certificates](Certificates.md)
- [OPC UA Part 14](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/)

## High availability state providers

Part 14 deployments that run multiple server instances should externalize the
state that otherwise lives in one process. The PubSub DI surface provides
replaceable provider contracts with in-memory defaults:

- `IPubSubConfigurationStore` persists the `PubSubConfigurationDataType` and
  per-`PublishedDataSet` `ConfigurationVersion`.
- `IPubSubIdAllocator` allocates reserved ids and configuration-file handles.
- `IPubSubRuntimeStateStore` stores component `PubSubState` values for
  connections, groups, writers, and readers.
- `IPubSubSecurityKeyStore` stores SKS SecurityGroup key material and token ids.

Use the fluent builder to inject external stores:

```csharp
services.AddOpcUa()
    .AddPubSub()
    .WithConfigurationStore(configurationStore)
    .WithIdAllocator(idAllocator)
    .WithRuntimeStateStore(runtimeStateStore)
    .WithSecurityKeyStore(securityKeyStore);
```

The default registrations preserve the existing process-local behavior. A
distributed provider must make allocation atomic and persist configuration
before a peer rebuilds its address space. Runtime mutations save the updated
configuration and per-dataset `ConfigurationVersion`, SKS key changes are
mirrored to the security-key store, and component run-state transitions are
mirrored to the runtime-state store.

## Diagnostics

`IPubSubDiagnostics` is the per-component counter sink. Every
connection / group / writer / reader has its own instance; the
application aggregates them. Counters available:

| Counter                       | Notes                                                                          |
| ----------------------------- | ------------------------------------------------------------------------------ |
| `TotalInformation`            | Live-state counter ([§9.1.11.5](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/9.1.11.5)). |
| `TotalError`                  | Live-state counter.                                                             |
| `Reset`                       | Resets the counters under the component.                                        |
| `SentNetworkMessages`         | Per-component send counter.                                                     |
| `ReceivedNetworkMessages`     | Per-component receive counter.                                                  |
| `FailedTransmissions`         | Per-component transmission errors.                                              |
| `EncryptionErrors`            | Per-component encryption / signing failures.                                    |
| `DecryptionErrors`            | Per-component decryption / signature-verification failures.                     |

Call `IPubSubDiagnostics.Read(PubSubDiagnosticsCounterKind)` at any
time. The server-side address-space layer auto-publishes the same
counters as Variables.

`PubSubDiagnosticsLevel` (`Off` / `Low` / `High`) controls how
detailed the counters become; configure via
`PubSubApplicationOptions.DiagnosticsLevel` or
`pb.WithDiagnosticsLevel(...)` on the builder.

## Native AOT

PubSub is AOT-clean across all four assemblies.

- **No reflection-based serialization.** Source-generated
  `IEncodeable` types (Part 14 datatypes) plus hand-written
  `System.Text.Json` JSON encoders / decoders.
- **No dynamic emit.** All transport / encoder / decoder factories
  are concrete singletons resolved through DI; no
  `Activator.CreateInstance` / `Type.GetType` paths.
- **No `Newtonsoft.Json`.** The PubSub JSON encoder lives entirely on
  `System.Text.Json` (which is AOT-friendly).
- **Trimmer-clean.** `PubSubAotTests` in
  [`Tests/Opc.Ua.Aot.Tests/PubSubAotTests.cs`](../Tests/Opc.Ua.Aot.Tests/PubSubAotTests.cs)
  exercise UADP encode/decode, JSON encode/decode, key-ring rotation,
  scheduler tick dispatch, and metadata-registry lookup inside an
  AOT-published binary.
- **Reference sample.** The combined reference application publishes AOT-clean
  with zero `IL2026` / `IL3050` warnings:
  - [`Applications/ConsoleReferencePubSub`](../Applications/ConsoleReferencePubSub/README.md) (`publisher` / `subscriber` / `external` modes)

## Spec coverage

The library implements every clause of Part 14 v1.05.06 the
reference servers / publishers / subscribers exercise. The table
below maps Part 14 sections to the type / file that implements them.

| Spec §       | What                                                | Library type / file                                                            |
| ------------ | --------------------------------------------------- | ------------------------------------------------------------------------------ |
| §4           | PubSub model                                        | `Opc.Ua.PubSub` namespace                                                       |
| §5.2.3       | ConfigurationVersion                                | `Configuration/ConfigurationVersionUtils.cs`                                   |
| §5.2.5       | DataSetMetaData                                     | `MetaData/IDataSetMetaDataRegistry.cs`, `MetaData/DataSetMetaDataRegistry.cs`  |
| §6.2.1       | State machine                                       | `StateMachine/PubSubStateMachine.cs`                                           |
| §6.2.2.4     | Metadata registration                               | `MetaData/DataSetMetaDataRegistry.cs`                                          |
| §6.2.2.5     | Metadata publishing                                 | `Application/MetaDataPublisher.cs`                                             |
| §6.2.5       | Configuration validation                            | `Configuration/PubSubConfigurationValidator.cs`                                |
| §6.2.6       | Connection / Group model                            | `Connections/UaPubSubConnection.cs`, `Groups/WriterGroup.cs`, `Groups/ReaderGroup.cs` |
| §6.2.7       | DataSetReader                                       | `DataSets/DataSetReader.cs`                                                     |
| §6.4.1       | Periodic publishing                                 | `Scheduling/IPubSubScheduler.cs`, `Scheduling/PubSubScheduler.cs`              |
| §6.4.2       | Datagram-v2 fields                                  | `Transports/Udp/UdpDatagramTransport.cs`                                       |
| §7.2.4       | UADP NetworkMessage                                 | `Encoding/Uadp/UadpEncoder.cs`, `Encoding/Uadp/UadpDecoder.cs`                 |
| §7.2.4.4.3   | Security wrapping                                   | `Security/UadpSecurityWrapper.cs`                                              |
| §7.2.4.5.4   | DataSet field encoding                              | `Encoding/PubSubFieldEncoding.cs`                                              |
| §7.2.4.5.11  | RawData padding                                     | `Encoding/Uadp/UadpFieldEncoder.cs`                                            |
| §7.2.4.6     | Chunking                                            | `Encoding/Uadp/UadpChunker.cs`, `Encoding/Uadp/UadpReassembler.cs`             |
| §7.2.4.7     | UADP Discovery                                      | `Encoding/Uadp/UadpDiscovery*.cs`                                              |
| §7.2.5       | JSON NetworkMessage                                 | `Encoding/Json/JsonEncoder.cs`, `Encoding/Json/JsonDecoder.cs`                 |
| §7.2.5.6     | Action NetworkMessage                               | `Encoding/Json/JsonActionNetworkMessage.cs`                                    |
| §7.2.5.7     | JSON Discovery                                      | `Encoding/Json/JsonDiscoveryMessage.cs`, `Encoding/Json/JsonMetaDataMessage.cs`|
| §8.1         | Cipher policy abstractions                          | `Security/IPubSubSecurityPolicy.cs`                                            |
| §8.2         | Replay window                                       | `Security/SecurityTokenWindow.cs`, `Security/ISecurityTokenWindow.cs`          |
| §8.4         | SKS                                                 | `Security/Sks/OpcUaSecurityKeyServiceClient.cs`, `Security/Sks/InMemoryPubSubKeyServiceServer.cs` |
| §A.2.1.6     | AES-CTR nonce layout                                | `Security/AesCtrNonceLayout.cs`                                                |
| §A.2.2.5     | Sign-only frame layout                              | `Security/UadpSecurityWrapper.cs`                                              |
| §9.1         | PublishSubscribe Object                             | `Opc.Ua.PubSub.Server/Internal/PubSubNodeManager.cs`                           |
| §9.1.2       | Application bootstrap                               | `Application/PubSubApplication.cs`                                             |
| §9.1.5       | Configuration methods                               | `Opc.Ua.PubSub.Server/Internal/PubSubMethodHandlers.cs`                        |
| §9.1.6       | Runtime mutation                                    | `IPubSubApplication.cs` (mutation surface)                                      |
| §9.1.11      | Diagnostics                                         | `Diagnostics/IPubSubDiagnostics.cs`, `Diagnostics/PubSubDiagnostics.cs`        |

## Cross-references

- [Migration sub-doc — `migrate/2.0.x/pubsub.md`](migrate/2.0.x/pubsub.md)
- [External server adapter](#binding-pubsub-to-an-external-opc-ua-server-client-session-adapters)
- [Dependency Injection](DependencyInjection.md)
- [Native AOT Testing](NativeAoT.md)
- [Profiles and Facets](Profiles.md#pubsub-transports)
- [Certificate Manager](CertificateManager.md)
- [Sessions](Sessions.md) — Part 4 service set used by the SKS client.
- [Reference PubSub sample (`Applications/ConsoleReferencePubSub/README.md`)](../Applications/ConsoleReferencePubSub/README.md)

