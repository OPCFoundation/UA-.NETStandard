# OPC UA Encoders and Decoders

## Json

### Built-in Data Types

The following table lists the OPC UA built-in data types as defined in
[OPC UA Part 6 - Table 1](https://reference.opcfoundation.org/Core/Part6/v105/docs/5.1.2),
extended with the default JSON produced by `JsonEncoder` in **Compact** and
**Verbose** mode (see `JsonEncoderOptions`).

The *Nullable* column indicates whether a `null` JSON value exists for the type.
The *Default* column specifies the default value when one is needed; the default
for all arrays is `null`.

In **Compact** mode (`IgnoreNullValues=true`, `IgnoreDefaultValues=true`) every
default value is omitted — the field is not written to the output at all.

| Id | Name | Nullable | Default | Description | Verbose | Compact |
|----|------|:--------:|---------|-------------|---------|---------|
| 1 | Boolean | No | `false` | A two-state logical value (true or false). | `false` | *(omitted)* |
| 2 | SByte | No | `0` | An integer value between −128 and 127. | `0` | *(omitted)* |
| 3 | Byte | No | `0` | An integer value between 0 and 255. | `0` | *(omitted)* |
| 4 | Int16 | No | `0` | An integer value between −32 768 and 32 767. | `0` | *(omitted)* |
| 5 | UInt16 | No | `0` | An integer value between 0 and 65 535. | `0` | *(omitted)* |
| 6 | Int32 | No | `0` | An integer value between −2 147 483 648 and 2 147 483 647. | `0` | *(omitted)* |
| 7 | UInt32 | No | `0` | An integer value between 0 and 4 294 967 295. | `0` | *(omitted)* |
| 8 | Int64 | No | `0` | An integer value between −9.2e18 and 9.2e18. | `"0"` | *(omitted)* |
| 9 | UInt64 | No | `0` | An integer value between 0 and 1.8e19. | `"0"` | *(omitted)* |
| 10 | Float | No | `0` | An IEEE single precision (32 bit) floating point value. | `0` | *(omitted)* |
| 11 | Double | No | `0` | An IEEE double precision (64 bit) floating point value. | `0` | *(omitted)* |
| 12 | String | Yes | `null` | A sequence of Unicode characters. | `null` | *(omitted)* |
| 13 | DateTime | Yes | MinValue | An instance in time. | `null` | *(omitted)* |
| 14 | Guid | Yes | All zeros | A 16-byte globally unique identifier. | `null` | *(omitted)* |
| 15 | ByteString | Yes | `null` | A sequence of octets. | `null` | *(omitted)* |
| 16 | XmlElement | Yes | `null` | A Unicode string that is an XML element. | `null` | *(omitted)* |
| 17 | NodeId | Yes | All default | An identifier for a node in the address space. | `null` | *(omitted)* |
| 18 | ExpandedNodeId | Yes | All default | A NodeId allowing a namespace URI instead of an index. | `null` | *(omitted)* |
| 19 | StatusCode | No | Good | A numeric identifier for an error or condition. | `{}` | *(omitted)* |
| 20 | QualifiedName | Yes | All default | A name qualified by a namespace. | `null` | *(omitted)* |
| 21 | LocalizedText | Yes | All default | Human readable text with an optional locale identifier. | `null` | *(omitted)* |
| 22 | ExtensionObject | Yes | All default | A structure with app-specific data that may not be recognized. | `null` | *(omitted)* |
| 23 | DataValue | Yes | All default | A data value with an associated status code and timestamps. | `null` | *(omitted)* |
| 24 | Variant | Yes | Null | A union of all of the types specified above. | `null` | *(omitted)* |
| 25 | DiagnosticInfo | Yes | No fields | Detailed error and diagnostic info associated with a StatusCode. | `null` | *(omitted)* |

### Notes

- All non-nullable types are encoded as their default value when they equal the
  default. In Compact mode, all default values are omitted.
    - **Int64 / UInt64** are encoded as JSON strings (e.g. `"0"`, `"42"`) because
      JSON numbers cannot precisely represent the full 64-bit range.
    - **StatusCode** Good is encoded as an empty JSON object `{}` in Verbose mode.
      A non-Good StatusCode includes `Code` (UInt32) and, in Verbose mode only, a
      `Symbol` string (e.g. `{"Code":2147483648,"Symbol":"Bad"}`). Compact mode
      omits the `Symbol`.
    - **DateTime** is encoded as an ISO 8601 string (e.g. `"2024-01-01T00:00:00Z"`).
      `DateTimeUtc.MinValue` is treated as null.
    - **Guid** is encoded as a string (e.g. `"72962B91-FA75-4AE6-8D28-B404DC7DAF63"`).
      `Guid.Empty` is treated as null.
- All nullable types are encoded as `null` when they equal `null`. In Compact
  mode, all default values are omitted.
    - **ByteString** is encoded as a Base64 string.
    - **XmlElement** is encoded as a string containing the outer XML. 
    - **NodeId / ExpandedNodeId** are encoded as formatted strings
      (e.g. `"nsu=http://opcfoundation.org/UA/;i=2258"`).
    - **QualifiedName** is encoded as a formatted string
      (e.g. `"nsu=http://opcfoundation.org/UA/;ServerStatus"`). 
    - **LocalizedText** is encoded as a JSON object
      (e.g. `{"Text":"Hello","Locale":"en"}`). 
    - **Variant** is encoded as a JSON object with `UaType` and `Value`
      (e.g. `{"UaType":6,"Value":42}`). In Compact mode, `Value` is omitted
      when it equals the type default.
    - **DataValue** is encoded like a Variant with additional `StatusCode`,
      `SourceTimestamp`, `SourcePicoseconds`, `ServerTimestamp`, and
      `ServerPicoseconds` fields (omitted when at their defaults).
    - **ExtensionObject** is encoded as a JSON object. Encodeable bodies are
      inlined with a `UaTypeId` field; binary/XML bodies use `UaEncoding`
      and `UaBody` fields.
    - **DiagnosticInfo** is encoded as a JSON object with optional fields
      `SymbolicId`, `NamespaceUri`, `Locale`, `LocalizedText`,
      `AdditionalInfo`, `InnerStatusCode`, and `InnerDiagnosticInfo`.

## Experimental Encodings (Avro, Arrow)

In addition to the standard Binary/JSON/XML codecs, `Opc.Ua.Types` ships two **experimental** encodings behind the same `IEncoder`/`IDecoder` abstractions and the `EncodingType` enum. Both are annotated with `[Experimental("UA_NETStandard_1")]`, so consuming `AvroEncoder`/`AvroDecoder`, `ArrowEncoder`/`ArrowDecoder`, or the `EncodingType.Avro` / `EncodingType.Arrow` members requires acknowledging diagnostic `UA_NETStandard_1`; the API and wire format may change without a major-version bump.

### Avro (`EncodingType.Avro`)

- Available on **every** target framework. On the legacy targets (`net472`, `net48`, `netstandard2.0`, `netstandard2.1`) the codec relies on the span/stream/`Encoding` polyfills in `Opc.Ua.Types/Polyfills`; on `net8.0`+ it uses the BCL fast paths so the modern targets keep their performance.
- Implements the complete built-in / `Variant` / `ExtensionObject` / `Enumeration` surface and runs in the shared Part 6 encoder round-trip test matrix alongside Binary/JSON/XML.
- Also available as a PubSub network-message encoding and transcoder target (see [`../../PubSub.md`](../../PubSub.md)).

### Arrow (`EncodingType.Arrow`)

- Columnar [Apache Arrow](https://arrow.apache.org/) representation, available on **`net8.0`+ only** (the `Apache.Arrow` dependency is not offered for the legacy targets).
- Implements the full round-trip surface used by the shared matrix, including:
  - `IEncodeable` / `ExtensionObject` values — encodeable bodies are serialized as a nested OPC UA binary body and, on decode, reconstructed into the concrete `IEncodeable` via the message context's `EncodeableFactory`. When the type id is not registered the raw binary body is preserved (matching the Binary decoder behaviour).
  - `Enumeration` `Variant`s (scalar / array / matrix), carried as `Int32` columns.
- Known limitations: writing top-level struct arrays of `Variant`/`DataValue` is currently limited to a single element, and full message-envelope decode (`ArrowDecoder.DecodeMessage<T>()`) is not implemented.

> **Protobuf:** An experimental Protobuf codec existed transiently during development and was removed before release. There is no `EncodingType.Protobuf` member or public Protobuf codec.

