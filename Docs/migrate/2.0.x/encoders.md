# Encoders and Complex Types

> **When to read this:** Read this for `IEncodeableFactoryBuilder` / `IType` / `EncodeableFactory.GlobalFactory` migration, JSON / XML / binary encoder/decoder changes (removed Default JSON encoding infrastructure, `IJsonEncodeable` removal), and complex-types moves to `Opc.Ua.Client`.

## Encodeable Factory and Complex Type System

### IType hierarchy

New type abstraction layer: `IType` (base) with `IBuiltInType`, `IEnumeratedType` (new), and `IEncodeableType` (now extends `IType`). Many APIs return `IType` instead of `Type`:

- `TypeInfo.GetSystemType(ExpandedNodeId, IEncodeableTypeLookup)` → returns `IType` (was `Type`). Use `.Type` property to get the CLR `Type`.
- The overload `TypeInfo.GetSystemType(BuiltInType, int valueRank)` was removed.

### IEncodeableTypeLookup changes

- `TryGetEncodeableType<T>()` removed.
- Added: `TryGetEnumeratedType(ExpandedNodeId, out IEnumeratedType?)`, `TryGetType(XmlQualifiedName, out IType?)`.

### IEncodeableFactoryBuilder changes

- `AddEncodeableType(ExpandedNodeId, Type)` → renamed to `AddType(ExpandedNodeId, Type)`.
- Added: `AddEnumeratedType(IEnumeratedType)`, `AddEnumeratedType(ExpandedNodeId, IEnumeratedType)`.
- `AddEncodeableType(Type)` and `AddEncodeableTypes(Assembly)` now have AOT annotations (`[DynamicallyAccessedMembers]`, `[RequiresUnreferencedCode]`).

### EncodeableFactory.GlobalFactory removed

The `[Obsolete]` static `EncodeableFactory.GlobalFactory` was removed. `EncodeableFactory.Create()` renamed to `Fork()`. Use `ServiceMessageContext.Factory` instead.

### ComplexTypes moved to Opc.Ua.Core.Schema

The shared `ComplexTypeSystem` orchestrator, the complex type interfaces and the default (non-reflection-emit) type builder moved to the `Opc.Ua.Core.Schema` assembly under the root `Opc.Ua` namespace (which consumers already import) so they can be used by both client and server and existing code keeps compiling without adding a new `using`. Remove the old `Opc.Ua.Client.ComplexTypes` import if it is now unused (the client-only `NodeCacheResolver` and the `ComplexTypeSystem.Create(session, ...)` helpers stay in `Opc.Ua.Client.ComplexTypes`).
The `ComplexTypeSystem(ISession, ...)` constructors were removed; construct a session-bound instance with `ComplexTypeSystem.Create(session, telemetry)` (the default, NativeAOT friendly builder) or `ComplexTypeSystem.Create(session, new ComplexTypeBuilderFactory(), telemetry)` for the Reflection.Emit builder.
Servers build the same stand-ins for runtime-loaded DataTypes **by default** (`StandardServer.LoadComplexTypes`; opt out by setting it to `false`); configure the pass with `AddComplexTypeSystem()` or invoke `IServerInternal.LoadComplexTypesAsync(...)` directly. See `Docs/ComplexTypes.md`.

### OptionSet DataType support

Concrete Structure-backed sub-types of the abstract `OptionSet` DataType (`i=12755`) are now automatically registered by the default `ComplexTypeSystem` builder with a new runtime class `Opc.Ua.Encoders.OptionSet` (in `Stack/Opc.Ua.Types`). Bit-field metadata is resolved from `DataTypeDefinition` (`EnumDefinition`) or, as a fallback, synthesized from the `OptionSetValues` property (`LocalizedText[]`).

Impact on existing code:

- **Source-breaking for custom `IComplexTypeBuilder` implementations**: a new member `AddOptionSetType(QualifiedName, ExpandedNodeId, ExpandedNodeId, ExpandedNodeId, ExpandedNodeId, EnumDefinition)` was added to `IComplexTypeBuilder`. Custom implementations must provide it.
- The Reflection.Emit builder in `Opc.Ua.Client.ComplexTypes` throws `NotSupportedException` from `AddOptionSetType`; callers relying on the Reflection.Emit path for OptionSet sub-types should switch to the default builder (`ComplexTypeSystem.Create(session, telemetry)`).
- No wire-format changes: encoders/decoders continue to route through `IEncodeableFactory` → `IEncodeableType.CreateInstance`, which now yields `Opc.Ua.Encoders.OptionSet` for registered sub-types.
- UInteger-backed OptionSet DataTypes remain treated as their underlying unsigned integer in a `Variant` (unchanged).

## Encoders and Decoders

The `IEncoder` and `IDecoder` interfaces have changed to use `ArrayOf<T>` instead of Collection and `System.Array`. Also generic versions of `ReadEncodeable`/`WriteEncodeable` and `ReadEnumerated`/`WriteEnumerated` were added with the ones taking a `System.Type` paramter removed. There are 2 versions of `ReadEncodeable<T>` and `WriteEncodeable<T>`, one with a `new()` constraint bypassing `EncodeableFactory` lookups, and one with a `ExpandedNodeId` used to look up the concrete type and allowing to use `IEncodeable` as `T` constraint.

Furthermore, `ReadArray`/`WriteArray` methods have been removed. A new `ReadVariantValue` and `WriteVariantValue` method has been added to write "only" the content (Value) of a Variant, or read the value using `TypeInfo` information. Neither supports `DiagnosticInfo` but also supports writing and reading scalar values. The return type is Variant. To read a `TypeInfo.Scalars.Variant` use ReadVariant instead because a Variant cannot contain a scalar Variant.

In addition to the generic Write/ReadEnumerated, the non-generic `EnumValue` variants were also added.

- `IEncoder`: `WriteEnumerated(string, EnumValue)`, `WriteEnumeratedArray(string, ArrayOf<EnumValue>)`
- `IDecoder`: `ReadEnumerated(string)` returning `EnumValue`, `ReadEnumeratedArray(string)` returning `ArrayOf<EnumValue>`

Custom encoder/decoder implementations must adjust to comply with the new interfaces.

**Change code as follows:**

- Change all `ReadEncodeable`/`WriteEncodeable` calls to use the type as part of the generic expression. E.g. `ReadEncodeable("field", typeof(T))` to `ReadEncodeable<T>("field")` and `WriteEncodeable("field", value, typeof(T))` to `WriteEncodeable("field", value)`. If value is a type that cannot be created using a parameterless constructor, pass the type id as last argument.
- Change all `ReadEnumerated` calls to use the enumeration type as part of the generic expression. E.g. `ReadEnumerated("field", typeof(T))` to `ReadEnumerated<T>("field")`.
- Change calls to `ReadArray`/`WriteArray` to use `ReadVariantValue` and `WriteVariantValue` and extract the value from the returned `Variant` based on the type you intended to read. A good example can be found in `BaseComplexType` `EncodeProperty` and `DecodeProperty`.

## Experimental Encodings (Avro, Arrow)

Two additional, **experimental** wire encodings ship in `Opc.Ua.Types` alongside Binary/JSON/XML and are surfaced through the same `IEncoder`/`IDecoder` abstractions and the `EncodingType` enum (`EncodingType.Avro`, `EncodingType.Arrow`). Both codec surfaces are annotated with `[Experimental("UA_NETStandard_1")]`: to use `AvroEncoder`/`AvroDecoder`, `ArrowEncoder`/`ArrowDecoder`, or the corresponding `EncodingType` members you must acknowledge diagnostic `UA_NETStandard_1` (suppress it or set it to a non-error severity), and the API and wire format may change without a major-version bump.

- **Avro** (`AvroEncoder`/`AvroDecoder`) is available on every target framework. On the legacy targets (`net472`, `net48`, `netstandard2.0`, `netstandard2.1`) it uses the polyfilled span/stream/`Encoding` helpers in `Opc.Ua.Types/Polyfills`; on `net8.0`+ it uses the BCL fast paths, so there is no `net8.0`+ performance regression. It implements the full built-in / Variant / `ExtensionObject` / `Enumeration` surface and runs in the same shared Part 6 encoder round-trip test matrix as Binary/JSON/XML.
- **Arrow** (`ArrowEncoder`/`ArrowDecoder`, a columnar Apache Arrow representation) targets `net8.0`+ only. It also runs the full shared round-trip matrix, including `IEncodeable`/`ExtensionObject` values (decoded back to the concrete `IEncodeable` through the message context's `EncodeableFactory`, falling back to the raw binary body when the type id is not registered) and `Enumeration` Variants (scalar/array/matrix, carried as `Int32` columns). Directly writing top-level struct arrays of `Variant`/`DataValue` remains limited to a single element, and full message-envelope decode (`ArrowDecoder.DecodeMessage<T>()`) is not implemented.

> **Protobuf note:** An experimental Protobuf codec existed transiently on this branch and was removed before release. There is no `EncodingType.Protobuf` member and no public Protobuf encoder/decoder to migrate away from.

## Complex Types

### ComplexTypes moved to Opc.Ua.Core.Schema

The shared `ComplexTypeSystem` orchestrator, the complex type interfaces and the default (non-reflection-emit) type builder moved to the `Opc.Ua.Core.Schema` assembly under the root `Opc.Ua` namespace (which consumers already import) so they can be used by both client and server and existing code keeps compiling without adding a new `using`. Remove the old `Opc.Ua.Client.ComplexTypes` import if it is now unused (the client-only `NodeCacheResolver` and the `ComplexTypeSystem.Create(session, ...)` helpers stay in `Opc.Ua.Client.ComplexTypes`).
The `ComplexTypeSystem(ISession, ...)` constructors were removed; construct a session-bound instance with `ComplexTypeSystem.Create(session, telemetry)` (the default, NativeAOT friendly builder) or `ComplexTypeSystem.Create(session, new ComplexTypeBuilderFactory(), telemetry)` for the Reflection.Emit builder.
Servers build the same stand-ins for runtime-loaded DataTypes **by default** (`StandardServer.LoadComplexTypes`; opt out by setting it to `false`); configure the pass with `AddComplexTypeSystem()` or invoke `IServerInternal.LoadComplexTypesAsync(...)` directly. See `Docs/ComplexTypes.md`.

### OptionSet DataType support

Concrete Structure-backed sub-types of the abstract `OptionSet` DataType (`i=12755`) are now automatically registered by the default `ComplexTypeSystem` builder with a new runtime class `Opc.Ua.Encoders.OptionSet` (in `Stack/Opc.Ua.Types`). Bit-field metadata is resolved from `DataTypeDefinition` (`EnumDefinition`) or, as a fallback, synthesized from the `OptionSetValues` property (`LocalizedText[]`).

Impact on existing code:

- **Source-breaking for custom `IComplexTypeBuilder` implementations**: a new member `AddOptionSetType(QualifiedName, ExpandedNodeId, ExpandedNodeId, ExpandedNodeId, ExpandedNodeId, EnumDefinition)` was added to `IComplexTypeBuilder`. Custom implementations must provide it.
- The Reflection.Emit builder in `Opc.Ua.Client.ComplexTypes` throws `NotSupportedException` from `AddOptionSetType`; callers relying on the Reflection.Emit path for OptionSet sub-types should switch to the default builder (`ComplexTypeSystem.Create(session, telemetry)`).
- No wire-format changes: encoders/decoders continue to route through `IEncodeableFactory` → `IEncodeableType.CreateInstance`, which now yields `Opc.Ua.Encoders.OptionSet` for registered sub-types.
- UInteger-backed OptionSet DataTypes remain treated as their underlying unsigned integer in a `Variant` (unchanged).

---

**See also**

- Related: [types.md](types.md), [source-generation.md](source-generation.md).
- [2.0 migration index](README.md) — analyzer quick-start + symptom → sub-doc table.
- [Migration Guide](../../MigrationGuide.md) — landing page across versions.

