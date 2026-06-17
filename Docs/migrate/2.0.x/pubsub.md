# PubSub (Part 14)

> **When to read this:** Read this if your application uses any of the
> `Opc.Ua.PubSub.*` namespaces, the legacy `UaPubSubApplication` factory,
> the AMQP transport, the `JsonEncodingMode` enum, or any of the per-field
> data set / data set reader fields. The PubSub layer was modernised
> end-to-end in 2.0 — every consumer should review at least the
> compatibility matrix at the bottom.

The 1.5.378 implementation tracked Part 14 v1.04 with several known gaps
(orphaned chunking, missing security wiring, single-shot KeepAlive,
ignored `DataSetReader` filters, no v1.05 fields, AMQP transport stub).
The 2.0 rewrite tracks Part 14 v1.05.06 end-to-end, is AOT-clean, hosts
inside the standard `IServiceCollection` DI surface, and exposes a fluent
builder for inline configuration. The legacy public types remain
compilable but are marked `[Obsolete]` with codemod guidance.

For a full library reference see [`PubSub.md`](../../PubSub.md). This
sub-doc focuses on the **upgrade** story.

## 1. `UaPubSubApplication.Create*` and the legacy types are `[Obsolete]`

`UaPubSubApplication.Create(...)` and its overloads remain as thin
shims that defer to the new `IPubSubApplication` and emit
`[Obsolete]` warnings (`UA0030`). The shim covers the most common
"create from XML configuration file" flow. The following types are
also marked `[Obsolete]` with no in-place rewrite — migrate to the
fluent builder or the DI extensions:

| Legacy type                       | New replacement                                              |
| --------------------------------- | ------------------------------------------------------------ |
| `UaPubSubApplication`             | `IPubSubApplication` (built via `PubSubApplicationBuilder`)  |
| `IUaPubSubConnection`             | `PubSubConnection` (sealed, immutable)                       |
| `UaPubSubConnection`              | `PubSubConnection`                                           |
| `IUaPublisher` / `UaPublisher`    | `IPubSubScheduler` + `WriterGroup` (engine-driven)           |
| `UaPubSubConfigurator`            | `PubSubApplicationBuilder` (fluent) + `IPubSubConfigurationStore` |
| `IUaPubSubDataStore`              | `IPublishedDataSetSource` (per-DataSet provider model)       |

Codemod recipe:

```csharp
// Before (1.5.378)
var app = UaPubSubApplication.Create("publisher.xml");
app.Start();
// ...
app.Stop();

// After (2.0)
await using var app = await new PubSubApplicationBuilder()
    .ConfigureFromXml("publisher.xml")
    .BuildAsync();
await app.StartAsync();
// ...
await app.StopAsync();
```

See [`PubSub.md` §Fluent builder](../../PubSub.md#fluent-builder-walkthrough)
for the in-code form. Cites [Part 14 §6.2](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2).

## 2. AMQP transport removed (breaking)

`Opc.Ua.PubSub.PublisherInterfaces.TransportProtocol.AMQP` is removed.
The 1.5.378 enum value was a stub — no working AMQP transport ever
shipped, and the [Part 14 §6.4](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.4)
profile is unused outside that experiment. Configurations that name
`http://opcfoundation.org/UA-Profile/Transport/pubsub-amqp-uadp` or
`...-amqp-json` fail validation with `PSC0010`
(`SpecClause = "6.4"`).

Replacement: switch to MQTT (`Opc.Ua.PubSub.Mqtt`,
[Part 14 §6.4.2](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.4.2))
or UDP (`Opc.Ua.PubSub.Udp`, [Part 14 §6.4.1](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.4.1)).
The codemod is purely the transport profile URI plus the addition of
`AddMqttConnection(...)` / `AddUdpConnection(...)`.

## 3. JSON encoder switched to System.Text.Json

The Newtonsoft-based encoder
(`Opc.Ua.PubSub.Encoding.JsonNetworkMessage` v1) is replaced with a
`System.Text.Json`-backed encoder under
`Libraries/Opc.Ua.PubSub/Encoding/Json/`. Behaviour changes that may
surface in callers:

- The `Newtonsoft.Json` dependency is dropped from the PubSub layer
  (it remains transitively available via `Opc.Ua.Core` for legacy
  Variant JSON).
- Numeric round-trips honour the .NET native precision instead of the
  Newtonsoft default (e.g. `double` → 17 significant digits, not 15).
- The new encoder is `Utf8JsonWriter`-backed; allocations on the hot
  path drop ~70 % vs. the Newtonsoft pipeline.
- The decoder uses `Utf8JsonReader` and validates structurally; it
  rejects trailing junk where the old decoder silently truncated.

The wire-level layout is unchanged where the spec is unambiguous; see
[`pubsub.md` §JSON SingleNetworkMessage](#18-json-singlenetworkmessage--jsonactionnetworkmessage--jsondiscoverymessage)
for new content.

## 4. `JsonEncodingMode` — 1.04 names removed

`Opc.Ua.PubSub.Encoding.Json.JsonEncodingMode.Reversible` and
`Opc.Ua.PubSub.Encoding.Json.JsonEncodingMode.NonReversible` are
removed in favour of the
[Part 6 §5.4.1](https://reference.opcfoundation.org/specs/OPC-10000-6/v1.05.06/5.4.1)
/ [Part 14 §7.2.5](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.5)
v1.05.06 names:

| Old                              | New                              |
| -------------------------------- | -------------------------------- |
| `JsonEncodingMode.Reversible`    | `JsonEncodingMode.Verbose`       |
| `JsonEncodingMode.NonReversible` | `JsonEncodingMode.Compact`       |
| _(new)_                          | `JsonEncodingMode.RawData`       |

The wire format produced by `Verbose` is byte-identical to the wire
format the old `Reversible` produced; similarly `Compact` ≡ old
`NonReversible`. The rename is a public-API change only. No
`[Obsolete]` aliases exist — consumers update enum references at
upgrade time. Background:
[#3609](https://github.com/OPCFoundation/UA-.NETStandard/issues/3609).

## 5. UADP RawData field padding

Per [Part 14 §7.2.4.5.11](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.5.11),
`String`, `ByteString`, `XmlElement`, and array fields encoded via
`DataSetFieldContentMask.RawData` are now padded to the maximum size
declared in `FieldMetaData.MaxStringLength` or
`FieldMetaData.ArrayDimensions`. The on-wire length prefix is
suppressed for padded fields; consumers receive the exact
`MaxStringLength` bytes with trailing NULs as the spec mandates.
Decoders trim the trailing NUL fill on read.

If your configuration uses RawData but does not declare
`MaxStringLength` or `ArrayDimensions`, the encoder falls back to the
legacy length-prefixed form (variable size) and the configuration
validator surfaces issue code `PSC0025`
(`SpecClause = "7.2.4.5.11"`) so the missing bound is reported at
configuration time. Closes
[#3566](https://github.com/OPCFoundation/UA-.NETStandard/issues/3566).

## 6. `DataSetFieldContentMask` — per-field timestamps and status

The encoder/decoder now honour every bit defined in the
[Part 14 §6.2.4.2](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.4.2)
`DataSetFieldContentMask`:

- `StatusCode`
- `SourceTimestamp` / `SourcePicoSeconds`
- `ServerTimestamp` / `ServerPicoSeconds`
- `RawData` (see §5)

In 1.5.378 the encoder produced bare values regardless of the mask;
consumers that explicitly opted in to timestamps now actually receive
them. To migrate consumers that previously got bare values:

```csharp
// 1.5.378 — bare value, mask ignored
DataValue dv = field.Value;

// 2.0 — mask honoured. Read the field; check IsNull on the timestamp.
DataValue dv = field.Value;
if (!dv.SourceTimestamp.IsNull)
{
    /* mask included SourceTimestamp */
}
```

If the consumer was written against 1.5.378 and is sensitive to a
suddenly-non-default `SourceTimestamp`, configure the writer with
`DataSetFieldContentMask.None` to opt back into bare-value behaviour.

## 7. `DataSetReader` honours `DataSetClassId` and `MessageReceiveTimeout`

[Part 14 §6.2.7.5](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.7.5)
defines a per-reader filter on `DataSetClassId`. In 1.5.378 the field
was deserialised but never compared at runtime — a reader bound to
class A would happily process a NetworkMessage carrying a different
class. 2.0 enforces the filter; mismatches drop the message and
increment `IPubSubDiagnostics.RejectedDataSetMessageCount`.

[Part 14 §6.2.7](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.7)
also defines `MessageReceiveTimeout`. 2.0 wires it into a
`DataSetReaderTimeoutWatcher` that transitions the reader to
`PubSubState.Error` after the configured idle window expires (default
0 = disabled, matching 1.5.378). Migration: leave the field zero to
keep 1.5.378 behaviour, or set it explicitly to opt in.

## 8. `DatagramConnectionTransport2DataType` v2 fields

The v1.05 UDP transport node introduces three new fields under
[Part 14 §6.4.1.4](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.4.1.4):

- `DiscoveryAnnounceRate` — interval at which discovery announcements
  are emitted on the discovery topic.
- `DiscoveryMaxMessageSize` — caps the discovery NetworkMessage size
  (forces chunking above the limit).
- `QosCategory` — maps to a DSCP TOS byte on the outbound socket
  (`Best-Effort`, `Voice`, `Video`, etc.).

1.5.378 ignored all three. 2.0 reads them out of the configuration
(`DatagramConnectionTransport2DataType` extension object) and applies
them at the `Opc.Ua.PubSub.Udp.UdpUaTransport` layer. Configurations
that still use the legacy `DatagramConnectionTransportDataType`
(without the `2`) keep working without behaviour change.

## 9. UADP chunking now wired at runtime

The `Opc.Ua.PubSub.Encoding.Uadp.UadpChunkingEncoder` existed in
1.5.378 but was never invoked by the transport layer. NetworkMessages
larger than `MaxNetworkMessageSize`
([Part 14 §7.2.4.6](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.6))
were silently truncated and rejected by interoperable receivers. 2.0
splits the NetworkMessage into chunks and reassembles on the receive
side. No code change is required — set `MaxNetworkMessageSize` to a
sensible value (1500 for unicast, 1472 for IPv4 multicast) and the
chunker activates.

## 10. KeepAlive emission cadence

[Part 14 §6.2.6.5](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.6.5)
specifies that a `WriterGroup` configured with `KeepAliveTime > 0`
emits a KeepAlive NetworkMessage whenever no DataSetMessage has been
sent in the last `KeepAliveTime` ms. 1.5.378 emitted at most one
KeepAlive after the first publish cycle; the watchdog never re-armed.
2.0 routes KeepAlive emission through the `IPubSubScheduler` and
re-arms after every emitted message (KeepAlive included). Set
`KeepAliveTime = 0` to keep 1.5.378 behaviour.

## 11. `UadpSecurityWrapper` is now invoked

`Opc.Ua.PubSub.Security.UadpSecurityWrapper` was orphaned in 1.5.378 —
the type compiled but the publisher never called it, so configurations
that named a `SecurityMode` other than `None` produced unsigned bytes
on the wire. 2.0 wires the wrapper into the encode / decode pipeline:

- `SecurityMode.None` continues to skip the wrapper (no behaviour
  change for unsigned configurations).
- `SecurityMode.Sign` produces an HMAC-SHA-256-only payload.
- `SecurityMode.SignAndEncrypt` produces AES-128/256-CTR + HMAC.

Configurations that already declared a security mode now actually get
that security applied; receivers must be configured with matching keys
or the unwrap fails with
`PubSubDiagnosticsLevel.High → SecurityFailureCount`.

See [`PubSub.md` §Security](../../PubSub.md#security) and the NIST SP
800-38A F.5.1 / F.5.5 KAT vectors covered by the
`Aes128CtrTransformTests` / `Aes256CtrTransformTests` suites.

## 12. `MetaDataPublisher` — retained metadata at startup

[Part 14 §6.2.6](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.6)
and [Part 14 §7.2.5.4](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.5.4)
require a publisher to make every active `DataSetMetaData` available
before the first DataSetMessage that references it. 1.5.378 emitted
metadata only when the writer ticked, leaving subscribers that joined
mid-stream unable to decode RawData payloads.

2.0 introduces `MetaDataPublisher` (registered automatically by
`PubSubApplicationBuilder`). On `StartAsync`:

- UDP transports broadcast every active metadata once via the
  configured discovery selector.
- MQTT transports publish each metadata to its `ua-metadata/...`
  topic with the `retained` flag set, so late subscribers receive it
  on connect.

Opt out by registering a no-op `IMetaDataPublisher` in DI:

```csharp
services.AddSingleton<IMetaDataPublisher, NullMetaDataPublisher>();
```

## 13. Server-side address space — `services.AddPubSubAddressSpace()`

`Opc.Ua.PubSub.Server` is new. It mounts the
[Part 14 §9](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/9)
`PublishSubscribe` Object on a hosted server and binds the standard
methods (`AddConnection`, `RemoveConnection`, `AddDataSetWriter`,
`RemoveDataSetWriter`, `AddDataSetReader`, `RemoveDataSetReader`,
`Get/SetSecurityKeys`, `Enable`, `Disable`, `AddSecurityGroup`) to the
runtime mutation methods on `IPubSubApplication`. Wire it in:

```csharp
services.AddOpcUaServer().AddPubSubAddressSpace(o =>
{
    o.AllowMutations = true;
});
```

1.5.378 had no server-side surface — Part 14 clients could not browse
or invoke methods against the publisher.

## 14. Configuration mutation methods

`IPubSubApplication` now exposes:

```csharp
ValueTask<PubSubConnection> AddConnectionAsync(
    PubSubConnectionDataType cfg, CancellationToken ct = default);
ValueTask RemoveConnectionAsync(NodeId connectionId, CancellationToken ct = default);
ValueTask<WriterGroup> AddWriterGroupAsync(...);
ValueTask<ReaderGroup> AddReaderGroupAsync(...);
ValueTask<DataSetWriter> AddDataSetWriterAsync(...);
ValueTask<DataSetReader> AddDataSetReaderAsync(...);
// + Remove* and Enable/DisableAsync per component
```

These are bound by the server-side address space (§13) and by the
fluent builder. 1.5.378 required a stop / reconfigure / start cycle.

## 15. Per-component diagnostics

[Part 14 §6.2.10](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.10)
defines a per-`PubSubGroupTypeState` /
`DataSetWriterTypeState` / `DataSetReaderTypeState` diagnostics
sub-object. 2.0 instantiates one `IPubSubDiagnostics` per component
(connection, group, writer, reader) instead of a single
application-wide counter. The Variables are exposed on the address
space when `AddPubSubAddressSpace()` is wired (§13).

Custom `IPubSubDiagnostics` consumers attach via DI:

```csharp
services.AddSingleton<IPubSubDiagnosticsFactory, MyDiagnosticsFactory>();
```

## 16. Dependency-injection integration

`services.AddOpcUa().AddPubSub(o => ...)` registers the full PubSub
runtime — connections, groups, writers, readers, scheduler, security
subsystem, metadata registry, diagnostics — into the standard
`IServiceCollection`. The previous note in
[`Docs/DependencyInjection.md`](../../DependencyInjection.md) that
"PubSub is not part of the dependency injection surface" is removed
in 2.0.

Quick-reference (see [`PubSub.md` §DI hosting](../../PubSub.md#di-hosting)):

| Extension                                | Where it lives                      |
| ---------------------------------------- | ----------------------------------- |
| `AddPubSub`                              | `Opc.Ua.PubSub`                     |
| `AddPubSubPublisher`                     | `Opc.Ua.PubSub`                     |
| `AddPubSubSubscriber`                    | `Opc.Ua.PubSub`                     |
| `AddPubSubSecurityKeyServiceClient`      | `Opc.Ua.PubSub`                     |
| `AddPubSubSecurityKeyServiceServer`      | `Opc.Ua.PubSub`                     |
| `AddUdpTransport`                        | `Opc.Ua.PubSub.Udp`                 |
| `AddMqttTransport`                       | `Opc.Ua.PubSub.Mqtt`                |
| `IOpcUaServerBuilder.AddPubSub(...)`     | `Opc.Ua.PubSub.Server`              |
| `AddPubSubAddressSpace` (server-side)    | `Opc.Ua.PubSub.Server`              |

## 17. Native AOT

Both `ConsoleReferencePublisher` and `ConsoleReferenceSubscriber`
publish AOT-clean (`PublishAot=true`, `IlcOptimizationPreference=Size`,
zero `IL2026` / `IL3050` warnings). The
`Tests/Opc.Ua.Aot.Tests/PubSubAotTests` suite exercises every code path
that touches the runtime under AOT. 1.5.378 PubSub was reflection-heavy
and could not publish AOT-clean.

## 18. JSON `SingleNetworkMessage` / `JsonActionNetworkMessage` / `JsonDiscoveryMessage`

[Part 14 §7.2.5.2](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.5.2)
adds three modes the 1.5.378 encoder did not support:

- `SingleNetworkMessage` — emit one DataSetMessage per
  NetworkMessage, suitable for MQTT topic-per-writer patterns where
  the broker handles fan-out.
- `JsonActionNetworkMessage` — request / response message used by the
  Action methods (`Action.Request`, `Action.Response`,
  [Part 14 §7.2.5.6](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.5.6)).
- `JsonDiscoveryMessage` — per-publisher DataSetMetaData /
  PublisherEndpoints discovery, in JSON form.

Consumer impact: subscribers that previously crashed on these payload
shapes now decode them. Subscribers can opt out by configuring a
`JsonNetworkMessageContentMask` that excludes `SingleNetworkMessage`.

## 19. Compatibility matrix

| Surface                                                      | 2.0 outcome                                                       |
| ------------------------------------------------------------ | ----------------------------------------------------------------- |
| `UaPubSubApplication.Create(string)` from XML config         | Compiles unchanged + `[Obsolete]` warning. Behaviour identical.   |
| `UaPubSubApplication.Start()` / `.Stop()`                    | Compiles + `[Obsolete]`. Internally delegates to `IPubSubApplication`. |
| Direct construction of `UaPubSubConnection` etc.             | Compiles + `[Obsolete]`. Migrate to the fluent builder.           |
| `JsonEncodingMode.Reversible` / `NonReversible`              | **Source break.** Rename to `Verbose` / `Compact`.                |
| `TransportProtocol.AMQP` enum value                          | **Source break.** Switch to MQTT or UDP.                          |
| `DataSetFieldContentMask.SourceTimestamp` etc.               | **Behavioural break.** Now actually emitted; consumers must read. |
| `DataSetReader.DataSetClassId` mismatch                      | **Behavioural break.** Reader now drops; previously accepted.     |
| `DataSetReader.MessageReceiveTimeout > 0`                    | **Behavioural break.** Now transitions to Error; previously inert. |
| `KeepAliveTime > 0`                                          | **Behavioural fix.** Cadence now correct per spec.                |
| `SecurityMode.Sign` / `SignAndEncrypt`                       | **Behavioural fix.** Now actually applied; previously inert.      |
| `MaxNetworkMessageSize` chunking                             | **Behavioural fix.** Now chunks; previously truncated.            |
| `DatagramConnectionTransport2DataType` v2 fields             | New. Honoured if present; ignored otherwise.                      |
| Server-side `PublishSubscribe` Object                        | New (`AddPubSubAddressSpace`). Optional.                          |
| Per-component diagnostics                                    | New. Replace single global counter with per-component instances.  |
| DI surface (`services.AddOpcUa().AddPubSub(...)`)            | New. Optional.                                                    |
| AOT                                                          | Both samples publish AOT-clean.                                   |

## See also

- [Library reference (PubSub.md)](../../PubSub.md)
- [Dependency injection](../../DependencyInjection.md)
- [Profiles](../../Profiles.md) — Datagram-v2, SKS pull/push, AES-CTR
- [Native AOT](../../NativeAoT.md)
- [What's New in 2.0](../../WhatsNewIn2.0.md#part-14-pubsub-modernization)
