# PubSub Transcoding

High-performance PubSub transcoders in `Opc.Ua.PubSub.Transcoding` bridge subscriber-side `PubSubNetworkMessage` traffic to publisher-side output without forcing applications to deserialize into their domain model first. A route can change the NetworkMessage mapping (`Uadp` or `Json`), field encoding (`Variant`, `RawData`, or `DataValue`), identifiers, fields, values, metadata, message types, and target broker topic before the message is re-encoded and sent.

Use transcoders when a deployment needs an in-process PubSub gateway: for example, a UADP UDP subscriber feeding an MQTT JSON publisher, a JSON cloud topic being normalized before it is re-published as UADP, or a relay that preserves UADP frames on an identity route but rewrites selected fields on another route. The implementation is aligned with OPC UA Part 14 NetworkMessage mappings for [UADP §7.2.4](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4), [JSON §7.2.5](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.5), PubSub connection filtering in [§6.2.7](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.7), and Security Key Service (SKS) key distribution in [§8](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/8).

## Architecture

The transcoding pipeline has four stages:

```text
received frame + decoded message
        │
        ▼
TranscodeInput(Message, SourceFrame, SourceFrameSecured)
        │
        ├─ raw-frame fast path for identity same-encoding routes
        │
        ▼
decoded PubSubNetworkMessage intermediate representation
        │
        ▼
ordered IPubSubMessageTransform pipeline
        │
        ▼
INetworkMessageProfileProjector (UADP ↔ JSON concrete record projection)
        │
        ▼
encoder + TranscodeSecurity (target re-securing where applicable)
        │
        ▼
TranscodeResult(Frames, Messages, FastPath)
```

`PubSubNetworkMessage` is the seam between the receive path and the target profile. Built-in and custom transforms operate on that decoded message tree, while `NetworkMessageProfileProjector` re-materializes it as the concrete UADP or JSON record requested by `TranscodeSpec.TargetEncoding`. `NetworkMessageTranscoder` is the structured primitive that applies transforms and projection; `PubSubTranscoder` is the frame-level primitive that also encodes output frames, applies target-side UADP security, and exposes the raw-frame fast path.

The fast path is used only when the route is an identity transcode (`TranscodeSpec.IsIdentity`), the source and target encoding match, the subscriber supplied a raw source frame, the source frame is not still message-layer secured, and the target route is not configured for message-layer security. In that case the input frame is returned as a `TranscodeResult` without rebuilding the message.

## Configuration model

A route is described by `TranscodeSpec`:

- `TargetEncoding` selects `TranscodeEncoding.Uadp` or `TranscodeEncoding.Json`.
- `Transforms` is the ordered `ArrayOf<IPubSubMessageTransform>` pipeline.
- `TargetOptions` controls format-level output choices through `TranscodeTargetOptions`.

`TranscodeTargetOptions.FieldEncoding` overrides target field encoding when set. `JsonSingleMessageMode` emits the flat JSON single-message layout for single-DataSetMessage JSON output. `PreserveMetaDataVersion` defaults to `true` so downstream readers can continue to validate their configured metadata major version.

`TranscodeContext` carries the `PubSubNetworkMessageContext`, shared metadata registry, diagnostics, clock, and telemetry context used by the transcode stages. It holds no per-message state and can be reused across messages for the same bridge or standalone transcoder.

## Transform pipeline

Every transform implements:

```csharp
ValueTask<PubSubNetworkMessage?> TransformAsync(
    PubSubNetworkMessage message,
    TranscodeContext context,
    CancellationToken cancellationToken = default);
```

Transforms run in registration order before profile projection. They should treat input records as immutable and return either the same message, a `with`-copied message, or `null` to drop the NetworkMessage.

Built-in transforms:

| Transform | Purpose |
| --------- | ------- |
| `IdRemapTransform` | Rewrites `PublisherId`, NetworkMessage `WriterGroupId`, concrete-record `DataSetClassId`, and DataSetMessage `DataSetWriterId` values from an optional source-to-target map. |
| `FieldEncodingTransform` | Re-encodes every `DataSetField` as `Variant`, `RawData`, or `DataValue` and updates UADP DataSetMessage `FieldEncoding`. |
| `FieldProjectionTransform` | Selects only named fields in caller-specified order or excludes named fields while preserving the remaining order. |
| `FieldRenameTransform` | Renames DataSetFields by an ordinal source-to-target name map. |
| `ValueTransform` | Applies `Func<string, Variant, Variant>` to every field value for scaling, unit conversion, redaction, or coercion. |
| `MessageTypeTransform` | Filters KeyFrame, DeltaFrame, Event, and KeepAlive DataSetMessages and can force the remaining messages to a replacement type. |
| `MetaDataTransform` | Rewrites `DataSetMetaDataType` carried by metadata-announcement NetworkMessages. |
| `DelegateMessageTransform` | Wraps a synchronous or asynchronous caller-supplied message transform. |

## Cross-encoding and field encoding

`TranscodeEncoding` mirrors the two implemented NetworkMessage mappings: UADP and JSON. `TranscodeEncodingExtensions.ToTransportProfileUri()` maps the family to a canonical transport profile URI so `PubSubTranscoder` can resolve the matching encoder; `FromTransportProfileUri()` and `EncodingOf()` classify source messages.

`NetworkMessageProfileProjector` performs all four encoding combinations:

- UADP to UADP for same-profile rebuilds and field-encoding changes.
- UADP to JSON for UDP or Ethernet ingress to broker/cloud egress.
- JSON to UADP for broker ingress to datagram or Layer 2 egress.
- JSON to JSON for JSON normalization and topic republishing.

Field encoding is controlled either by `FieldEncodingTransform` in the transform pipeline or by `TranscodeTargetOptions.FieldEncoding` during target projection. RawData output depends on metadata, so keep the source metadata available in `TranscodeContext.MetaDataRegistry` and use `MetaDataTransform` when field projection or rename changes the schema seen by downstream readers.

## Identifier, field, value, and metadata rewrites

Identifier remapping changes the Part 14 addressing fields used by reader matching (`PublisherId`, `WriterGroupId`, `DataSetWriterId`, and `DataSetClassId`). Field projection can select, drop, and reorder fields by name. Field rename changes DataSetMessage field names; if the target readers are metadata-driven, rewrite or publish matching metadata as well.

Use `ValueTransform` for lightweight per-field value conversion. Prefer `Variant.TryGetValue` to inspect strongly typed values and return the original `Variant` when the field is not the expected type:

```csharp
.TransformValue((name, value) =>
{
    if (name == "Temperature" && value.TryGetValue(out double celsius))
    {
        return Variant.From(celsius * 9 / 5 + 32);
    }

    return value;
})
```

`MetaDataTransform` is invoked only for messages that carry metadata. Data messages pass through unchanged.

## Security

`TranscodeSecurity` is the frame-level security policy used by `PubSubTranscoder`. It can hold a target-side `UadpSecurityWrapper` and `UadpSecurityWrapOptions` so transcoded UADP output is re-secured with the target connection's SKS-backed security context. If no target wrapper is configured, target output is emitted without message-layer security.

OPC UA PubSub message-layer security is defined for UADP only ([§7.2.4.4](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/7.2.4.4)); JSON output cannot carry that UADP security envelope. For this reason, a secured source frame is refused when the target output would be unsecured unless `AllowInsecureCrossEncoding` is set. In the fluent API this is `.AllowInsecureCrossEncoding()`. Use that option only when the deployment intentionally relies on transport-layer protection such as DTLS, TLS, or MQTT TLS for the target hop.

A secured UADP source can be re-published as secured UADP when the target connection resolves a `UadpSecurityWrapper`. A secured UADP source to JSON, or a secured source to unsecured UADP, is dropped by default with a security diagnostic and log warning.

## In-process bridge, receive hook, and egress

`IPubSubConnection.RegisterReceivedNetworkMessageSink(IReceivedNetworkMessageSink sink)` registers an opt-in observer of decoded data NetworkMessages on a connection's receive path. The method returns an `IDisposable` registration token; disposing it removes the observer. The sink receives a `ReceivedNetworkMessage` containing the decoded plaintext `Message`, optional raw `Frame`, whether that frame was `FrameSecured`, and the source transport profile URI and connection name.

`PubSubTranscodingBridge` implements `IReceivedNetworkMessageSink`. It registers on the source connection when `Start()` is called, builds a `TranscodeInput`, invokes `ITranscoder.TranscodeAsync`, and sends non-dropped results to an `IPubSubTranscodeEgress`. `ConnectionTranscodeEgress` sends each output frame through a target `PubSubConnection` transport and applies UADP chunking when needed by the connection.

The DI bridge is started as a hosted service after the PubSub application is available. `PubSubTranscodingBridgeHostedService` resolves source and target connections by `Name` from `IPubSubApplication.Connections`, builds the shared `TranscodeContext`, resolves registered `INetworkMessageEncoder` instances, resolves target security through `IPubSubSecurityWrapperResolver`, and starts the bridge.

## Fluent `AddTranscodingBridge` walkthrough

Register the bridge from the `AddPubSub` callback with `IPubSubBuilder.AddTranscodingBridge(Action<PubSubTranscoderBuilder>)`. Connection names must match `IPubSubApplication.Connections`; the configuration file or inline configuration must define both the source subscriber connection and the target publisher connection.

```csharp
using Opc.Ua;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Transcoding;

services.AddOpcUa()
    .AddPubSub(pubsub =>
    {
        pubsub.UseConfigurationFile("pubsub.xml"); // defines "udp-in" and "mqtt-out"
        pubsub.AddTranscodingBridge(bridge => bridge
            .From("udp-in")
            .To("mqtt-out", TranscodeEncoding.Json)
            .RemapIds(publisherId: PublisherId.FromUInt16(42))
            .RenameField("Temp", "Temperature")
            .SelectFields("Temperature", "Pressure")
            .TransformValue((name, value) =>
            {
                if (name == "Temperature" && value.TryGetValue(out double celsius))
                {
                    return Variant.From(celsius * 9 / 5 + 32);
                }

                return value;
            })
            .DropKeepAlive()
            .AllowInsecureCrossEncoding() // required when secured UADP is transcoded to JSON
            .ToTopic("plant/line1/telemetry"));
    });
```

Common fluent methods:

| Method | Effect |
| ------ | ------ |
| `From(string)` | Selects the source connection by name. |
| `To(string, TranscodeEncoding)` | Selects the target connection by name and the output encoding. |
| `AddTransform(IPubSubMessageTransform)` | Appends a custom transform. |
| `RemapIds(...)` | Adds `IdRemapTransform`. |
| `RenameField(string, string)` | Adds or extends one `FieldRenameTransform`. |
| `SelectFields(params string[])` / `ExcludeFields(params string[])` | Adds field projection. |
| `TransformValue(Func<string, Variant, Variant>)` | Adds per-field value transformation. |
| `TransformMetaData(Func<DataSetMetaDataType, DataSetMetaDataType>)` | Adds metadata rewriting. |
| `FilterMessageTypes(Func<PubSubDataSetMessageType, bool>, PubSubDataSetMessageType?)` | Filters and optionally relabels DataSetMessage types. |
| `DropKeepAlive()` | Drops KeepAlive DataSetMessages. |
| `WithFieldEncoding(PubSubFieldEncoding)` | Sets target field encoding. |
| `AsJsonSingleMessage(bool)` | Enables JSON single-message layout for single DataSetMessage output. |
| `PreserveMetaDataVersion(bool)` | Controls whether source metadata versions are preserved. |
| `AllowInsecureCrossEncoding(bool)` | Allows intentional security downgrades. |
| `ToTopic(string)` / `WithTopicSelector(Func<ReceivedNetworkMessage, string?>)` | Sets fixed or per-message MQTT topic selection. |
| `BuildSpec()` | Builds the standalone `TranscodeSpec` represented by the fluent route. |

## Standalone primitives

The bridge and DI extensions are optional. Tests, samples, or custom gateways can instantiate the primitives directly:

```csharp
var spec = new PubSubTranscoderBuilder()
    .To("unused-target-name", TranscodeEncoding.Uadp)
    .WithFieldEncoding(PubSubFieldEncoding.DataValue)
    .BuildSpec();

var structured = new NetworkMessageTranscoder(spec);
ArrayOf<PubSubNetworkMessage> projected = await structured.TranscodeAsync(
    sourceMessage,
    context,
    cancellationToken);
```

Use `NetworkMessageTranscoder` when input and output are already decoded `PubSubNetworkMessage` records. Use `PubSubTranscoder` when you need encoded frames, target encoder selection, raw-frame passthrough, and UADP re-securing. Built-in transforms can also be composed manually in a `TranscodeSpec` without using `PubSubTranscoderBuilder`.

## Limitations

Transcoding is an in-process bridge, not a persisted message broker. The bridge observes received data NetworkMessages inline on the source receive loop, so custom transforms and topic selectors must be fast, non-blocking, and exception-safe. Message-layer security can be produced only for UADP targets; JSON targets require an explicit `AllowInsecureCrossEncoding` policy when the source was message-layer secured. The raw-frame fast path is intentionally narrow and applies only to unsecured, same-encoding, identity routes with an available source frame. Metadata-sensitive routes remain responsible for keeping target reader metadata aligned with field projection, rename, and RawData choices.
