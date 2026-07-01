# Runtime Schema Generation

The `Opc.Ua.Core.Schema` library generates schemas for OPC UA data types at runtime. It works for the encodeable types emitted by the source generators and for complex types that are added dynamically by the complex-type client. Schemas are produced in every supported encoding:

- **XSD** for the XML encoding.
- **BSD** (OPC Binary, Part 6) for the binary encoding.
- **JSON Schema** (Part 6 Annex C, draft 2020-12) for the JSON encoding, in both the **compact** (reversible, BrowseName-keyed) and **verbose** flavors.

Schemas are built as strongly-typed object models in code — there are no embedded schema strings — so unused generation paths are trimmed away and the whole library is NativeAOT compatible. The XSD object model is the in-box `System.Xml.Schema.XmlSchema`, the BSD object model is the existing `Opc.Ua.Schema.Binary.TypeDictionary`, and the JSON object model is `System.Text.Json.Nodes.JsonObject`.

## Concepts

Generation is driven by a type's runtime structure definition (`StructureDefinition` or `EnumDefinition`), which already captures every field, data type, value rank and optionality. The pieces fit together as follows:

- `ISchemaProvider` is the entry point. It produces an `IUaSchema` for a requested `UaSchemaFormat` and `UaSchemaScope`.
- `IUaSchema` is the generated document. It exposes the strongly-typed object model (for example `JsonSchemaDocument.Root`, `XmlSchemaDocument.Schema`, `BinarySchemaDocument.Dictionary`) and can serialize itself with `WriteTo(Stream)`, `WriteTo(TextWriter)` or `ToSchemaString()`.
- `IDataTypeDefinitionResolver` maps a data type id to its `UaTypeDescription` (the type id, browse name and definition). The default implementation, `DataTypeDefinitionRegistry`, is an in-memory registry that generated and dynamically built types register their definitions with. The resolver is also used to follow field references and to enumerate the types of a namespace.

`UaSchemaFormat` selects the encoding (`Xsd`, `Bsd`, `JsonCompact`, `JsonVerbose`). `UaSchemaScope` selects the document granularity: `Type` produces a document for a single type and the closure of the types it depends on; `Namespace` produces a dictionary document for all types in a namespace.

## Registration

The services are registered through the standard OPC UA dependency-injection surface:

```csharp
IServiceProvider services = new ServiceCollection()
    .AddOpcUa()
    .AddSchemaGeneration()
    .Services
    .BuildServiceProvider();

ISchemaProvider provider = services.GetRequiredService<ISchemaProvider>();
```

The provider can also be constructed directly when dependency injection is not used:

```csharp
var registry = new DataTypeDefinitionRegistry();
registry.Add(new UaTypeDescription(typeId, browseName, structureDefinition, namespaceUri));

ISchemaProvider provider = new DefaultSchemaProvider(
    registry,
    new IUaSchemaGenerator[] { new JsonSchemaGenerator() });
```

## Registering data types

Schema generation needs the runtime definition of a type. A type's `StructureDefinition` / `EnumDefinition` is registered with the resolver from whichever source has it:

- **Server / browsed types** — a `DataTypeNode` obtained from a server (or the client node cache) carries its definition in `DataTypeNode.DataTypeDefinition`. Register it directly:

```csharp
var registry = serviceProvider.GetRequiredService<DataTypeDefinitionRegistry>();
registry.TryAddDataType(dataTypeNode, session.NamespaceUris);
```

- **Source-generated types** — the generated types expose their definition through the generated `DataTypeDefinitions.Create<TypeName>(namespaceUris)` factory. Wrap it in a `UaTypeDescription` and add it:

```csharp
registry.Add(new UaTypeDescription(typeId, browseName, definition, namespaceUri));
```

- **Dynamic complex types** — complex types built by the complex-type client carry a `StructureDefinition` (via `IStructureTypeInfo` / the structure-definition attribute) that can likewise be wrapped in a `UaTypeDescription` and registered.

Once registered, fields that reference other registered types are resolved automatically and included in the generated document.

## Generating a schema

Once a type's definition is registered with the resolver, a schema can be produced from its type id:

```csharp
if (provider.TryGetSchema(typeId, UaSchemaFormat.JsonCompact, UaSchemaScope.Type, out IUaSchema? schema))
{
    string json = schema.ToSchemaString();
}
```

The convenience extension methods read more naturally and make a type "expose" its schema:

```csharp
IUaSchema xsd = provider.GetXmlSchema(type);
IUaSchema bsd = provider.GetBinarySchema(type);
IUaSchema jsonCompact = provider.GetJsonSchema(type);
IUaSchema jsonVerbose = provider.GetJsonSchema(type, verbose: true);

// Resolve by type id and produce JSON in one call.
provider.TryGetJsonSchema(typeId, out IUaSchema? schema);
```

## Working with the object model

Because the schema is an object model, callers can inspect or post-process it before serializing. For JSON:

```csharp
var document = (JsonSchemaDocument)provider.GetJsonSchema(type);
JsonObject root = document.Root;            // the draft 2020-12 schema
document.WriteTo(stream);                    // UTF-8, indented
```

## JSON encoding notes (Part 6)

The JSON schemas follow the Part 6 JSON encoding faithfully, matching what the stack's `JsonEncoder` produces:

- `Int64` and `UInt64` are encoded as JSON strings (to avoid precision loss), so they are typed as `string`.
- `Float`/`Double` accept the special string values `Infinity`, `-Infinity` and `NaN`, so they are typed as `["number", "string"]`.
- `ByteString` is a base64 `string`; `DateTime` is a `date-time` string; `Guid` is a `uuid` string.
- The standard structured built-ins (`NodeId`, `Variant`, `ExtensionObject`, `DataValue`, ...) are described once per document in the `$defs` section and referenced.
- Compact enums are integers (with the allowed values listed via `oneOf`); verbose enums are the `Name_Value` strings.

## PubSub schemas

The `Opc.Ua.PubSub.Schema` library generates JSON Schemas for the PubSub JSON message formats. It is registered with `services.AddOpcUa().AddPubSubSchema()` and exposes `IPubSubSchemaProvider`:

- `CreateDataSetSchema(metaData, fieldContentMask, verbose)` — the per-DataSet payload object, one property per `FieldMetaData`. The field value shape follows the same Part 6 JSON rules as the core library, and `DataSetFieldContentMask` controls whether each field is the raw value or a `DataValue` object (with the mask-selected `StatusCode` / `SourceTimestamp` / ... members).
- `CreateDataSetMessageSchema(metaData, messageContentMask, fieldContentMask, verbose)` — a single DataSetMessage whose header fields are gated by `JsonDataSetMessageContentMask` and whose `Payload` is the DataSet schema above.
- `CreateNetworkMessageSchema(metaData, networkContentMask, messageContentMask, fieldContentMask, verbose)` — the `ua-data` NetworkMessage envelope, gated by `JsonNetworkMessageContentMask`; `Messages` is an array of DataSetMessage schemas, or a single object when `SingleDataSetMessage` is set.
- `CreateMetaDataMessageSchema(metaData, verbose)` — the `ua-metadata` message.

The provider reuses the core `ISchemaProvider` to resolve complex (structured/enum) field data types, embedding them into the document `$defs` section.

## Trimming and NativeAOT

The library opts into `IsAotCompatible` and avoids reflection-based serialization. XSD is written with `System.Xml.Schema.XmlSchema`, BSD with a direct `System.Xml.XmlWriter`, and JSON with `System.Text.Json.Nodes` / `Utf8JsonWriter`. Schema generation is a configuration-time activity, not a hot path; documents are built lazily and can be cached by the caller. Because the generation logic lives in its own assembly, it is trimmed away entirely when an application does not generate schemas.
