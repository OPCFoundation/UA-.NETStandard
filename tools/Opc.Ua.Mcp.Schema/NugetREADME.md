# OPC UA MCP schema workflows

Embeddable `schemas` profile for runtime type discovery and bounded XSD, BSD,
UA JSON, PubSub and REST OpenAPI schema artifacts.

The module reuses the stack's definition resolvers and schema generators.
It does not compile supplied code, use Reflection.Emit, or start a UA server.
Generated artifacts use the host's configured transfer root and size limits.

## Embedding

```csharp
services.AddOpcUaMcpCore(new OpcUaMcpOptions
{
    TransferRoot = artifactDirectory,
    MaxTransferBytes = 16 * 1024 * 1024
});
services.AddOpcUaMcpSchema(new McpSchemaOptions { MaxTypes = 256 });
services.AddMcpServer().WithOpcUaSchemaTools(McpToolProfile.Schemas);
```

`WithOpcUaSchemaTools` also accepts a composed `McpToolProfileSet`. Repeated
registration does not duplicate the six schema tools or the four shared
connection tools. Connection tools are available for explicit `Session`
operations; registered/standard local schemas and REST OpenAPI do not require
a connection.

Hosts can inject `IServiceMessageContext`, `IDataTypeDefinitionResolver`
(the default is `DataTypeDefinitionRegistry`) and `IUaSchemaGenerator`
implementations. `SchemaTools` also has a direct constructor. Per-operation
generation always receives a separate finite registry, never the potentially
unlimited host resolver.

## Tools

| Tool | Purpose |
| --- | --- |
| `schema_list_types` | A live, bounded local namespace or remote HasSubtype page. |
| `schema_inspect_type` | Actual structure/enum definition, identity, ranks and remote attribute quality. |
| `schema_load_types` | Explicit client-side registration through the SDK's AOT-safe default complex-type factory. |
| `schema_generate` | Xsd, Bsd, JsonCompact or JsonVerbose; Type closure or Namespace. |
| `schema_generate_pubsub` | Payload, DataSetMessage, NetworkMessage or MetaData JSON schema. |
| `schema_generate_openapi` | Existing REST OpenAPI generator, with optional bounded component schemas. |

Scalar settings are grouped under `options`; identifiers and actual arrays
remain typed top-level arguments. For example:

```json
{
  "typeId": "i=296",
  "outputPath": "argument.schema.json",
  "options": {
    "source": "Registered",
    "format": "JsonCompact",
    "scope": "Type"
  }
}
```

Use `options.source: "Session"` plus `sessionName` for live server definitions.
`schema_load_types` takes the non-empty `typeIds` string array and optional
`sessionName`; it does not accept assembly paths, code, or a dictionary-loading
fallback. Loading affects the current client session's factory, not the server.
Per-root results can report partial client-side registration; successful roots
and dependencies remain registered.

PubSub input uses either a typed `fields` array or `metadataNodeId` with
`options.source: "Session"`. Each inline field contains `name`, `dataTypeId`
and optional `valueRank`. All three numeric `options.fieldContentMask`,
`options.messageContentMask` and `options.networkContentMask` are required.
Unused masks must be zero, unknown/unsupported bits are refused, and RawData
cannot be combined with DataValue quality bits. `MetaData` describes the SDK's
metadata-message envelope; it does not specialize its generic metadata body.
Remote metadata's built-in hints are preserved; custom namespace tables must
match the selected context. Use inline fields with context-local identifiers
when metadata uses a different namespace table. Custom PubSub type names are
limited to letters, digits and underscores, and distinct types cannot share a
schema name, because the current SDK provider otherwise aliases their references.
`serverUrl` in OpenAPI options is document metadata and is never fetched.

## Bounds and fidelity

The default and maximum ceilings are 512 definitions, 1024 fields per type,
32 dependency/array levels, 16 MiB per artifact and 30 seconds per operation.
The host may lower these limits. The shared transfer byte ceiling also applies.
Registered-factory discovery additionally caps its identifier/encoding-alias
scan at 4096 entries before activating further definitions.
Output destinations are preflighted before remote reads, must have existing
parent directories beneath `TransferRoot`, and are published atomically without
overwriting existing files. Failures and cancellation leave no partial artifact.

Namespace expansion must fit a complete bounded enumeration; a large remote
type hierarchy is an error, not a silently incomplete namespace schema.
Choose Type scope for an explicitly bounded root closure on such a server.
List offsets refer to a live enumeration, not a durable server snapshot.

The tools preserve DataType identifier namespaces independently of BrowseName
namespaces. Unknown definitions, ambiguous schema names and unsupported
representations fail explicitly. In particular, the SDK BSD generator cannot
preserve multidimensional arrays or OptionSet wire forms, and the current
generators cannot fully describe unconstrained-dimensional or subtyped-field
envelopes. Integer-backed OptionSets are not registered as signed enumerations
by the bounded loader. No fallback invents a permissive successful schema.
XML/BSD namespace imports remain references; dependency artifacts are not
silently generated or fetched.

See the repository's `docs/McpServer.md` and `docs/SchemaGeneration.md`
for configuration and the underlying schema contracts.
