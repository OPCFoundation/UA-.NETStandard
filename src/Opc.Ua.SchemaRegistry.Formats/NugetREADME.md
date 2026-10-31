# Opc.Ua.SchemaRegistry.Formats

Schema-document adapters between retained bytes and native Schema Registry DataTypes.
Format parsing is a server/provider responsibility; native clients do not parse schema JSON.
The model and clients do not automatically load these adapters or host a Schema Registry.

Providers preserve admitted values and reject unsupported semantics explicitly. Format support
is advertised only after the corresponding provider and native access are configured.

`AddSchemaRegistryFormats()` registers JSON Schema 2020-12, Avro 1.11 and Arrow IPC providers.
They can also be constructed directly. All parsing and serialization is schema-only; no payload
codec, CodeDom, reflection-based JSON serializer or external format library is used.

The Arrow provider uses `ArrowIpcSchemaContentDataType`, retaining ordered binary metadata,
physical child fields, dictionary identity, optional names, message metadata and endianness.
It accepts one Schema message and hashes the exact input bytes. It never consults an extension
registry or substitutes a high-level dictionary representation for repeated metadata.

The providers enforce finite traversal bounds. Arrow limits an input/output document to 16 MiB,
traversal to 100,000 vector entries/fields, and copied string/metadata bytes during parsing to
64 MiB; nesting is limited to 128. These are resource limits, not a claim that all Arrow data
streams or future format extensions are understood. Record batches are not schema documents.

JSON Schema selection follows only known subschema-bearing keywords, not annotation objects.
Avro selection uses declared full names; aliases do not silently select an entity. Neither
provider follows a network reference. Avro defaults retain their original value forms even
though Parsing Canonical Form intentionally excludes defaults and other annotations.
