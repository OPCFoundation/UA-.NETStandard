# Source-Generated Data Types

The OPC UA .NET Standard stack includes a C# source generator that automatically
implements the `IEncodeable` interface for annotated POCO classes and enums. This
eliminates the need to hand-write `Encode`, `Decode`, `IsEqual`, and `Clone`
methods for custom OPC UA data types.

## Quick Start

1. Mark your class as `partial` and decorate it with `[DataType]`.
2. Add public properties for each field — the generator does the rest.

```csharp
using Opc.Ua;

namespace MyApp.Configuration
{
    [DataType(Namespace = "urn:mycompany:myapp")]
    public partial class DeviceConfiguration
    {
        public string Name { get; set; }
        public int Port { get; set; }
        public bool Enabled { get; set; }
    }
}
```

The source generator will produce a partial class file that implements
`IEncodeable`, including:

- `TypeId`, `BinaryEncodingId`, `XmlEncodingId` properties
- `Encode(IEncoder)` and `Decode(IDecoder)` methods
- `IsEqual(IEncodeable)` for value comparison
- `Clone()` for deep copy
- An activator class (e.g. `DeviceConfigurationActivator`) for type registration

## Prerequisites

### Project Reference (internal development)

When developing within the OPC UA .NET Standard repository, reference the
source generator project and import its props file:

```xml
<ItemGroup>
  <ProjectReference
    Include="..\..\Tools\Opc.Ua.SourceGeneration\Opc.Ua.SourceGeneration.csproj">
    <OutputItemType>Analyzer</OutputItemType>
    <ReferenceOutputAssembly>false</ReferenceOutputAssembly>
  </ProjectReference>
</ItemGroup>
<Import
  Project="..\..\Tools\Opc.Ua.SourceGeneration\OPCFoundation.Opc.Ua.SourceGeneration.props" />
```

### NuGet Package (external consumers)

Reference the `OPCFoundation.NetStandard.Opc.Ua.SourceGeneration` package:

```xml
<ItemGroup>
  <PackageReference
    Include="OPCFoundation.NetStandard.Opc.Ua.SourceGeneration"
    Version="..."
    OutputItemType="Analyzer"
    ReferenceOutputAssembly="false" />
</ItemGroup>
```

## The `[DataType]` Attribute

Applied to a `partial class`, `partial record class`, or an `enum` to opt in
to source generation.

```csharp
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum)]
public sealed class DataTypeAttribute : Attribute
{
    public string? Namespace { get; set; }
    public string? DataTypeId { get; set; }
    public string? BinaryEncodingId { get; set; }
    public string? XmlEncodingId { get; set; }
}
```

### Properties

| Property | Description |
|---|---|
| `Namespace` | The OPC UA namespace URI for this type. If omitted, falls back to `[DataContract(Namespace=...)]` if present, or `urn:<dotnet.namespace.lowered>`. |
| `DataTypeId` | Node ID string for the data type (e.g. `"i=12345"`, `"s=MyType"`, `"g=<guid>"`). Must be prefixed with `i=`, `s=`, `g=`, or `b=`. If omitted, defaults to a string identifier with the class name (`"s=DeviceConfiguration"`). |
| `BinaryEncodingId` | Optional binary encoding Node ID. Same prefix rules. |
| `XmlEncodingId` | Optional XML encoding Node ID. Same prefix rules. |

### Namespace Resolution Order

The generator resolves the OPC UA namespace URI in this order:

1. `[DataType(Namespace = "...")]` — explicitly specified
2. `[DataContract(Namespace = "...")]` — from `System.Runtime.Serialization`
3. `urn:<dotnet.namespace.lowered>` — automatic fallback (e.g. `urn:myapp.configuration`)

## The `[DataTypeField]` Attribute

Applied to properties to control which fields participate in encoding and in
what order.

```csharp
[AttributeUsage(AttributeTargets.Property)]
public sealed class DataTypeFieldAttribute : Attribute
{
    public int Order { get; set; }
    public string? Name { get; set; }
    public StructureHandling StructureHandling { get; set; }
    public DefaultValueHandling DefaultValueHandling { get; set; }
    public bool IsRequired { get; set; }
}
```

### Properties

| Property | Description |
|---|---|
| `Order` | The encoding/decoding order of this field. Fields are sorted by `Order` before code generation. |
| `Name` | The serialized field name. Defaults to the property name if not set. |
| `StructureHandling` | For `IEncodeable`-typed fields: controls the encoding strategy. See [StructureHandling](#structurehandling-enum). |
| `DefaultValueHandling` | Controls how default values are handled during encode/decode. See [DefaultValueHandling](#defaultvaluehandling-enum). |
| `IsRequired` | Indicates whether the field is required. Reserved for future use with optional-field structures. |

### Field Selection Rules

- **If any property has `[DataTypeField]`**: only annotated properties are
  included in encoding. Non-annotated properties are excluded.
- **If no properties have `[DataTypeField]`**: all public read-write
  properties are automatically included, ordered by declaration position.

This lets you mix serialized and non-serialized properties in the same class:

```csharp
[DataType(Namespace = "urn:myapp")]
public partial class ServerSettings
{
    [DataTypeField(Order = 0)]
    public string EndpointUrl { get; set; }

    [DataTypeField(Order = 1, Name = "session_timeout")]
    public uint SessionTimeout { get; set; }

    // Not annotated — excluded from encoding
    public string InternalNote { get; set; }
}
```

## Supported Property Types

The source generator supports the following property types:

| C# Type | OPC UA Type | Encoder Method |
|---|---|---|
| `bool` | Boolean | `WriteBoolean` |
| `sbyte` | SByte | `WriteSByte` |
| `byte` | Byte | `WriteByte` |
| `short` / `Int16` | Int16 | `WriteInt16` |
| `ushort` / `UInt16` | UInt16 | `WriteUInt16` |
| `int` / `Int32` | Int32 | `WriteInt32` |
| `uint` / `UInt32` | UInt32 | `WriteUInt32` |
| `long` / `Int64` | Int64 | `WriteInt64` |
| `ulong` / `UInt64` | UInt64 | `WriteUInt64` |
| `float` / `Single` | Float | `WriteFloat` |
| `double` / `Double` | Double | `WriteDouble` |
| `string` | String | `WriteString` |
| `DateTime` | DateTime | `WriteDateTime` |
| `Guid` / `Uuid` | Guid | `WriteGuid` |
| `ByteString` | ByteString | `WriteByteString` |
| `NodeId` | NodeId | `WriteNodeId` |
| `ExpandedNodeId` | ExpandedNodeId | `WriteExpandedNodeId` |
| `StatusCode` | StatusCode | `WriteStatusCode` |
| `QualifiedName` | QualifiedName | `WriteQualifiedName` |
| `LocalizedText` | LocalizedText | `WriteLocalizedText` |
| `ExtensionObject` | ExtensionObject | `WriteExtensionObject` |
| `DataValue` | DataValue | `WriteDataValue` |
| `Variant` | Variant | `WriteVariant` |
| `DiagnosticInfo` | DiagnosticInfo | `WriteDiagnosticInfo` |
| `XmlElement` | XmlElement | `WriteXmlElement` |

### Collections

- **`ArrayOf<T>`** — use for arrays of any supported type
- **`MatrixOf<T>`** — use for multi-dimensional arrays

### Enums and IEncodeable Types

- Any C# `enum` type (encoded with `WriteEnumerated`/`ReadEnumerated`)
- Any type implementing `IEncodeable` or decorated with `[DataType]`

### Unsupported Types

Properties with types not in the above list will produce:
- A **warning** if the property is not annotated with `[DataTypeField]`
  (the property is silently excluded)
- An **error** if the property is annotated with `[DataTypeField]`
  (the type will not generate)

## Class Variants

The generator handles several class shapes:

### Regular Partial Class

```csharp
[DataType(Namespace = "urn:myapp")]
public partial class MyConfig
{
    public string Name { get; set; }
    public int Value { get; set; }
}
```

Generates `virtual` methods for `Encode`, `Decode`, `IsEqual`, and `Clone`.

### Sealed Partial Class

```csharp
[DataType(Namespace = "urn:myapp")]
public sealed partial class MyConfig
{
    public string Name { get; set; }
}
```

Generates non-virtual methods (no `virtual` or `override` keywords).

### Record Class

```csharp
[DataType(Namespace = Namespaces.OpcUaXsd)]
public partial record class BrowserOptions
{
    [DataTypeField(Order = 0)]
    public RequestHeader? RequestHeader { get; set; }

    [DataTypeField(Order = 1)]
    public uint MaxReferencesReturned { get; set; }
}
```

For record types, `Clone()` uses `this with { }` and `IsEqual` delegates to
the record's `Equals` implementation.

### Derived Class (Inheritance)

When a `[DataType]` class derives from another `IEncodeable` base type, the
generator uses `override` instead of `virtual` and calls `base.Encode()`/
`base.Decode()` before encoding the derived fields.

### Internal Class

Classes declared as `internal` produce `internal` generated members.

## Enum Support

Annotate an enum with `[DataType]` to register it as an OPC UA enumerated type:

```csharp
[DataType(Namespace = "urn:myapp")]
public enum DeviceStatus
{
    Unknown = 0,
    Online = 1,
    Offline = 2,
    Error = 3
}
```

The generator produces an `EnumeratedType<T>` registration and an
`EnumDefinition` factory. The enum does **not** need to be `partial`.

## Registering Types with the Encodeable Factory

The source generator creates an extension method to register all `[DataType]`
types in a namespace with an `IEncodeableFactoryBuilder`. The method is named
`Add<NamespaceWithoutDots>DataTypes()`:

```csharp
// Generated extension method name is derived from the .NET namespace:
// For namespace "MyApp.Configuration" → AddMyAppConfigurationDataTypes()
MessageContext.Factory.Builder
    .AddMyAppConfigurationDataTypes()
    .Commit();
```

For example, types in `Opc.Ua.Gds.Server` produce a method named
`AddOpcUaGdsServerDataTypes()`:

```csharp
protected override void OnServerStarting(
    ApplicationConfiguration configuration)
{
    base.OnServerStarting(configuration);

    MessageContext.Factory.Builder
        .AddOpcUaGdsServerDataTypes()
        .Commit();
}
```

## Complete Example

This example shows a node manager configuration type used in the
MemoryBuffer quickstart server:

```csharp
using Opc.Ua;

namespace MemoryBuffer
{
    [DataType(Namespace = Namespaces.MemoryBuffer)]
    public partial class MemoryBufferConfiguration
    {
        public MemoryBufferConfiguration()
        {
        }

        [DataTypeField(Order = 1, StructureHandling = StructureHandling.Inline)]
        public ArrayOf<MemoryBufferInstance> Buffers { get; set; }
    }

    [DataType(Namespace = Namespaces.MemoryBuffer)]
    public partial class MemoryBufferInstance
    {
        public MemoryBufferInstance()
        {
        }

        [DataTypeField(Order = 1)]
        public string Name { get; set; }

        [DataTypeField(Order = 2)]
        public int TagCount { get; set; }

        [DataTypeField(Order = 3)]
        public string DataType { get; set; }
    }
}
```

Key points in this example:

- Both classes are `partial` and have parameterless constructors
- `StructureHandling = StructureHandling.Inline` on `Buffers` forces exact-type
  encoding (rather than extension object wrapping) since the element type is known
  at compile time
- `Order` controls the encoding sequence

## `StructureHandling` Enum

When a property's type implements `IEncodeable`, the generator must choose
between two encoding strategies:

| Strategy | Method | Use When |
|---|---|---|
| Exact type | `WriteEncodeable` / `ReadEncodeable` | The concrete type is always known (sealed, no subtypes) |
| Extension Object | `WriteEncodeableAsExtensionObject` / `ReadEncodeableAsExtensionObject` | Subtypes may be substituted at runtime |

The `StructureHandling` enum controls this choice:

```csharp
public enum StructureHandling
{
    Auto = 0,             // Generator decides based on type analysis
    Inline = 1,           // Force WriteEncodeable / ReadEncodeable
    ExtensionObject = 2   // Force WriteEncodeableAsExtensionObject
}
```

| Value | Behavior |
|---|---|
| `Auto` (default) | If the field type is `sealed` and does not derive from another `IEncodeable`, `WriteEncodeable` is used. Otherwise, `WriteEncodeableAsExtensionObject` is used. |
| `Inline` | Forces `WriteEncodeable`/`ReadEncodeable` — the exact type is written inline. Use when the concrete type is always known at compile time. |
| `ExtensionObject` | Forces `WriteEncodeableAsExtensionObject`/`ReadEncodeableAsExtensionObject` — the value is wrapped in an `ExtensionObject`. Use when subtypes may be substituted at runtime. |

Example:

```csharp
[DataType(Namespace = "urn:myapp")]
public partial class MyConfig
{
    // Inline: always encodes as EndpointDescription directly
    [DataTypeField(Order = 0, StructureHandling = StructureHandling.Inline)]
    public EndpointDescription Endpoint { get; set; }

    // Auto (default): generator decides based on whether the type is sealed
    [DataTypeField(Order = 1)]
    public OperationLimits Limits { get; set; }
}
```

## `DefaultValueHandling` Enum

Controls how default values are handled during encode and decode. This is
particularly important for configuration types where constructor defaults
(e.g., `NonceLength = 32`, `RejectSHA1SignedCertificates = true`) should be
preserved when the field is absent from XML/JSON.

```csharp
[Flags]
public enum DefaultValueHandling
{
    Exclude = 0,                       // Omit on write, preserve default on read
    Emit = 1,                          // Always write, even if default value
    SetIfMissing = 2,                  // Always set on read, even if absent
    Include = Emit | SetIfMissing      // Always write AND read (legacy behavior)
}
```

| Value | Encode Behavior | Decode Behavior |
|---|---|---|
| `Exclude` (default) | Omits the field from XML/JSON if value equals `default(T)`. Binary always writes. | Skips assignment if field is absent from XML/JSON, preserving the constructor default. Binary always reads. |
| `Emit` | Always writes the field, even if default. | Same as `Exclude` for decode. |
| `SetIfMissing` | Same as `Exclude` for encode. | Always assigns the decoded value, even if the field is absent (overwrites constructor default). |
| `Include` | Always writes. | Always reads and assigns. |

### How It Works

The generated code uses two new `IEncoder`/`IDecoder` APIs:

- **`IEncoder.CanOmitFields`**: Returns `true` for XML and JSON encoders,
  `false` for Binary. Used by the encode guard.
- **`IDecoder.HasField(string)`**: Returns `true` if the field exists in the
  encoded data. Always `true` for Binary. Checks element/property existence
  for XML/JSON.

Generated encode (when `Exclude` or `SetIfMissing`, i.e., `Emit` flag is NOT set):
```csharp
if (!encoder.CanOmitFields || NonceLength != default)
    encoder.WriteInt32("NonceLength", NonceLength);
```

Generated decode (when `Exclude` or `Emit`, i.e., `SetIfMissing` flag is NOT set):
```csharp
if (decoder.HasField("NonceLength"))
    NonceLength = decoder.ReadInt32("NonceLength");
```

### Example: Configuration with Defaults

```csharp
[DataType(Namespace = "urn:myapp")]
public partial class SecuritySettings
{
    public SecuritySettings()
    {
        NonceLength = 32;
        RejectExpiredCertificates = true;
    }

    // Exclude (default): omit from XML when 32, preserve 32 when absent
    [DataTypeField(Order = 0)]
    public int NonceLength { get; set; } = 32;

    // Include: always write and read — use for bool fields that default
    // to true, since false (the type default) is a valid explicit value
    [DataTypeField(Order = 1, DefaultValueHandling = DefaultValueHandling.Include)]
    public bool RejectExpiredCertificates { get; set; }

    // Exclude (default): omit false from XML, preserve false when absent
    [DataTypeField(Order = 2)]
    public bool AllowAnonymous { get; set; }
}
```

> **Guideline**: Use `DefaultValueHandling.Include` for bool properties that
> default to `true` in the constructor. Since `false` is the type default,
> `Exclude` would omit an explicit `false` on encode. `Include` ensures the
> value always round-trips correctly.

## Partial Init Properties

The source generator supports `partial` properties with `init` accessors for
immutable record types. This enables record classes with init-only properties
to participate in OPC UA binary and XML encoding — the generator creates a
private backing field for each such property and assigns to it directly during
`Decode()`, bypassing the init-only constraint.

### Example

```csharp
[DataType(Namespace = "urn:mycompany:myapp")]
public partial record class DeviceConfig
{
    [DataTypeField(Order = 0)]
    public partial string Name { get; init; } = "Default";

    [DataTypeField(Order = 1)]
    public partial int Port { get; init; }
}
```

The generator produces:

```csharp
partial record class DeviceConfig : IEncodeable
{
    private string __Name = "Default";
    public partial string Name { get => __Name; init => __Name = value; }

    private int __Port;
    public partial int Port { get => __Port; init => __Port = value; }

    public virtual void Decode(IDecoder decoder)
    {
        // Assigns to backing field, bypassing init constraint
        if (decoder.HasField("Name")) __Name = decoder.ReadString("Name");
        if (decoder.HasField("Port")) __Port = decoder.ReadInt32("Port");
    }
    // ...
}
```

### How It Works

1. For each property declared as `partial` with an `init` accessor, the
   generator emits a private backing field named `__<PropertyName>`.
2. The partial property implementation delegates `get` and `init` to that
   backing field.
3. `Decode()` assigns to the backing field directly, which is legal because
   the field is a regular mutable field — only the public `init` accessor is
   restricted to object-initializer contexts.
4. `Clone()` for record types uses `this with { }`, which copies init-only
   properties automatically via the copy constructor.

### When to Use

- Use `partial` + `init` properties when you want an immutable public API
  (callers can only set values at construction time) while still allowing
  the decoder to populate the object from a binary or XML stream.
- This is especially useful for configuration and options types modeled as
  `record class`.

## Requirements and Constraints

1. **Must be `partial`**: The class must be declared `partial` so the generator
   can extend it. Non-partial classes produce a compile error.
2. **Parameterless constructor**: The class must have a parameterless
   constructor (either explicit or implicit).
3. **Read-write properties**: Only properties with both getter and setter are
   considered. Read-only properties are ignored.
4. **One `[DataType]` per type**: `AllowMultiple = false` — each type can
   have at most one `[DataType]` attribute.
5. **Unique identifiers**: `DataTypeId` must be unique across all data types
   in the same namespace. For open namespaces, using a random GUID
   (`g=<guid>`) is recommended.

## Generated File Output

The generator produces one `.g.cs` file per .NET namespace. The file name
follows the pattern `<NamespaceWithoutDots>.Types.g.cs`.

For example, types in `Opc.Ua.Gds.Server` produce a file named
`OpcUaGdsServer.Types.g.cs` containing:

- All partial class bodies (IEncodeable implementation)
- Activator classes for each type
- A single extension class with the `Add...DataTypes()` registration method

## MSBuild Configuration

The source generator supports the following MSBuild properties via the
`OPCFoundation.Opc.Ua.SourceGeneration.props` file:

| Property | Description |
|---|---|
| `ModelSourceGeneratorPublicDataTypeExtensions` | When `true`, the generated extension methods and their containing static class are `public` instead of `internal`. |
