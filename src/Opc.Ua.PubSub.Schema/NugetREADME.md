# OPC UA PubSub Schema

Runtime schema generation for OPC UA PubSub JSON message payloads. Produces **JSON Schema (draft 2020-12)** documents that describe the JSON `DataSetMessage` body for a writer from its `DataSetMetaDataType` metadata, so consumers (validators, code generators, documentation tooling) can be driven directly from the live PubSub configuration.

Part of the OPC UA .NET Standard stack (`OPCFoundation.NetStandard.Opc.Ua.PubSub.Schema`). Built on the core runtime schema subsystem (`Opc.Ua.Core.Schema`); no reflection, NativeAOT / trim safe.

## Features

- Generates JSON Schema for the Part 14 PubSub **JSON** message mapping (Part 6 reversible / non-reversible encoding rules).
- Driven from runtime `DataSetMetaDataType` — schemas reflect the actual configured fields, data types and `DataSetFieldContentMask`.
- Resolves built-in and custom (structured / enumerated) data types through the encodeable type system.
- Dependency-injection first: `services.AddOpcUa().AddPubSubSchema()` registers `IPubSubSchemaProvider`; a direct-construction path (`new PubSubSchemaProvider(...)`) is also available.

## Quick start

```csharp
services.AddOpcUa().AddPubSubSchema();
// ...
var provider = serviceProvider.GetRequiredService<IPubSubSchemaProvider>();
IUaSchema schema = provider.CreateDataSetSchema(dataSetMetaData, fieldContentMask);
string jsonSchema = schema.ToSchemaString();
```

## Documentation

See the [Schema generation guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/SchemaGeneration.md)
for concepts, registration, the schema object model and the PubSub message schemas, and the
[PubSub guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/PubSub.md).
