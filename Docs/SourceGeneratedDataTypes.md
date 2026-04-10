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
`IEncodeable` and `IJsonEncodeable`, including:

- `TypeId`, `BinaryEncodingId`, `XmlEncodingId`, `JsonEncodingId` properties
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
    public string? JsonEncodingId { get; set; }
}
```

### Properties

| Property | Description |
|---|---|
| `Namespace` | The OPC UA namespace URI for this type. If omitted, falls back to `[DataContract(Namespace=...)]` if present, or `urn:<dotnet.namespace.lowered>`. |
| `DataTypeId` | Node ID string for the data type (e.g. `"i=12345"`, `"s=MyType"`, `"g=<guid>"`). Must be prefixed with `i=`, `s=`, `g=`, or `b=`. If omitted, defaults to a string identifier with the class name (`"s=DeviceConfiguration"`). |
| `BinaryEncodingId` | Optional binary encoding Node ID. Same prefix rules. |
| `XmlEncodingId` | Optional XML encoding Node ID. Same prefix rules. |
| `JsonEncodingId` | Optional JSON encoding Node ID. Same prefix rules. |

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
    public object? ForceEncodeable { get; set; }
    public bool IsRequired { get; set; }
}
```

### Properties

| Property | Description |
|---|---|
| `Order` | The encoding/decoding order of this field. Fields are sorted by `Order` before code generation. |
| `Name` | The serialized field name. Defaults to the property name if not set. |
| `ForceEncodeable` | For `IEncodeable`-typed fields: `true` forces `WriteEncodeable` (exact type), `false` forces `WriteEncodeableAsExtensionObject` (allows subtyping). If omitted, auto-detected based on whether the type is sealed. |
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

### Scalar Built-In Types

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
| `byte[]` (`ByteString`) | ByteString | `WriteByteString` |

### OPC UA Built-In Types

| Type | Encoder Method |
|---|---|
| `NodeId` | `WriteNodeId` |
| `ExpandedNodeId` | `WriteExpandedNodeId` |
| `StatusCode` | `WriteStatusCode` |
| `QualifiedName` | `WriteQualifiedName` |
| `LocalizedText` | `WriteLocalizedText` |
| `ExtensionObject` | `WriteExtensionObject` |
| `DataValue` | `WriteDataValue` |
| `Variant` | `WriteVariant` |
| `DiagnosticInfo` | `WriteDiagnosticInfo` |
| `XmlElement` | `WriteXmlElement` |

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

        [DataTypeField(Order = 1, ForceEncodeable = true)]
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
- `ForceEncodeable = true` on `Buffers` forces exact-type encoding (rather
  than extension object wrapping) since the element type is known at compile
  time
- `Order` controls the encoding sequence

## ForceEncodeable Explained

When a property's type implements `IEncodeable`, the generator must choose
between two encoding strategies:

| Strategy | Method | Use When |
|---|---|---|
| Exact type | `WriteEncodeable` / `ReadEncodeable` | The concrete type is always known (sealed, no subtypes) |
| Extension Object | `WriteEncodeableAsExtensionObject` / `ReadEncodeableAsExtensionObject` | Subtypes may be substituted at runtime |

**Auto-detection (default)**: If the field type is `sealed` and does not derive
from another `IEncodeable`, `WriteEncodeable` is used. Otherwise,
`WriteEncodeableAsExtensionObject` is used.

**Override**: Set `ForceEncodeable = true` to force exact-type encoding, or
`ForceEncodeable = false` to force extension object encoding.

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
