# OPC UA Complex Types

## Overview

The `Opc.Ua.Client` and `Opc.Ua.Client.ComplexTypes` libraries provide support for handling custom data types (complex types) in OPC UA client applications. Complex types include:

- **Custom Structures**: User-defined structured data types with multiple fields
- **Custom Enumerations**: User-defined enumeration types with custom values

The library allows OPC UA clients to automatically discover, load, and work with server-specific custom types, enabling seamless reading and writing of structured data without manual type definitions.

## Key Concepts

### What are Complex Types?

In OPC UA, complex types are custom data types defined by the server that extend beyond the built-in OPC UA data types. These types are commonly used to represent structured data such as:

- Configuration structures with multiple parameters
- Device status information with multiple fields
- Custom enumerations specific to a domain or device

### Type Discovery and Loading

The `ComplexTypeSystem` class manages the discovery and loading of custom types from an OPC UA server. It:

1. Browses the server's type system to discover custom types
2. Loads type definitions (using DataTypeDefinition attribute or binary/XML dictionaries)
3. Registers types complying to the type definitions in the session's type factory for encoding/decoding

### Supported Type Systems

The library supports multiple type definition mechanisms:

- **OPC UA 1.04+ DataTypeDefinition Attribute**: Modern structured type definitions
- **OPC UA 1.03 Binary Schema Dictionaries**: Legacy binary type dictionaries
- **OPC UA 1.03 XML Schema Dictionaries**: Legacy XML type dictionaries

The library automatically uses the most appropriate mechanism available on the server.

## Getting Started

### Type builders

#### Default type builder

A type builder builds the types that are registered in the `EncodeableFactory` by the `ComplexTypeSystem` class.  The default type builder registers in memory `IEncodeable` "adapter" classes that wrap discovered `DataTypeDefinition` and provide "state" (a list of Variants for the properties) clone, compare, and encode/decode behavior. The default type builder is part of the Opc.Ua.Client library and used when no type builder is provided in the constructor of the `ComplexTypeSystem` class.

#### Reflection.Emit based type builder

The `OPCFoundation.NetStandard.Opc.Ua.Client.ComplexTypes` nuget package extends the Opc.Ua.Client library by adding a type builder implementation that supports dynamically generating .NET types via `Reflection.Emit` at runtime. This has the added benefit over the default approach that the types can be reflected over by other tools, e.g. serializers. However, this is not NativeAOT compliant, and therefore building against a NativeAOT runtime will always fall back to the default behavior.

To use the Reflection.Emit type builder add the NuGet package to your project:

```bash
dotnet add package OPCFoundation.NetStandard.Opc.Ua.Client.ComplexTypes
```

Or via Package Manager:

```powershell
Install-Package OPCFoundation.NetStandard.Opc.Ua.Client.ComplexTypes
```

### Basic Usage

#### 1. Loading All Custom Types from a Server

The most common approach is to load all custom types after establishing a session:

```csharp
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.ComplexTypes;

// Create and connect session
var session = await Session.Create(...);

// Create and load the complex type system
ComplexTypeSystem complexTypeSystem;
if (!useReflectionEmitTypeBuilder)
{
    // Uses the default type builder
    complexTypeSystem = new ComplexTypeSystem(session);
}
else
{
    // Uses the Reflection.Emit type builder
    // Only works if the OPCFoundation.NetStandard.Opc.Ua.Client.ComplexTypes
    // nuget is referenced
    complexTypeSystem = ComplexTypeSystem.Create(session);
}

await complexTypeSystem.LoadAsync();

Console.WriteLine($"Loaded {complexTypeSystem.GetDefinedTypes().Count} custom types");
```

After loading, the session can automatically encode and decode custom types when reading or writing values.

#### 2. Reading Values with Complex Types

Once the type system is loaded, reading complex type values is straightforward:

```csharp
// Read a value that contains a complex type
NodeId nodeId = new NodeId("ns=2;s=MyCustomStructVariable");
DataValue dataValue = await session.ReadValueAsync(nodeId);

// The value is automatically decoded to a .NET type
// Note: DataValue.Value is obsolete. Use WrappedValue (Variant) instead.
if (dataValue.WrappedValue.TryGet(out ExtensionObject extensionObject))
{
    // Access the structured data
    if (extensionObject.Body is IStructure complexType)
    {
        // Access properties by name
        Variant temperatureValue = complexType["Temperature"];
        Variant pressureValue = complexType["Pressure"];

        Console.WriteLine($"Temperature: {temperatureValue}");
        Console.WriteLine($"Pressure: {pressureValue}");

        // Enumerate all properties
        foreach (IStructureField property in complexType.GetFields())
        {
            Console.WriteLine($"{property.Name}: {complexType[property.Name]}");
        }
    }
}
```

#### 3. Writing Values with Complex Types

To write complex type values, modify the properties and write back:

```csharp
// Read current value
DataValue dataValue = await session.ReadValueAsync(nodeId);
dataValue.WrappedValue.TryGet(out ExtensionObject extensionObject);
var complexType = (IStructure)extensionObject.Body;

// Modify properties
complexType["Temperature"] = 25.5;
complexType["Pressure"] = 101.3;

// Write the modified value back
var writeValue = new WriteValue
{
    NodeId = nodeId,
    AttributeId = Attributes.Value,
    Value = new DataValue
    {
        WrappedValue = extensionObject,
        SourceTimestamp = DateTime.UtcNow
    }
};

var writeValues = new WriteValueCollection { writeValue };
var response = await session.WriteAsync(null, writeValues, CancellationToken.None);

if (StatusCode.IsGood(response.Results[0]))
{
    Console.WriteLine("Value written successfully");
}
```

## Advanced Usage

### Loading Specific Types

Instead of loading all types, you can load specific types or namespaces:

#### Load a Specific Type

```csharp
var complexTypeSystem = new ComplexTypeSystem(session);

// Load a specific type by NodeId
ExpandedNodeId typeNodeId = new ExpandedNodeId("ns=2;i=3001");
IType? systemType = await complexTypeSystem.LoadTypeAsync(typeNodeId);

if (systemType != null)
{
    Console.WriteLine($"Loaded type: {systemType.XmlName}");
}
```

#### Load a Specific Type with Subtypes

```csharp
// Load a type and all its subtypes
ExpandedNodeId typeNodeId = new ExpandedNodeId("ns=2;i=3001");
IType? systemType = await complexTypeSystem.LoadTypeAsync(typeNodeId, subTypes: true);
```

#### Load All Types from a Namespace

```csharp
var complexTypeSystem = new ComplexTypeSystem(session);

// Load all custom types from a specific namespace
string namespaceUri = "http://mycompany.com/MyCustomTypes";
bool success = await complexTypeSystem.LoadNamespaceAsync(namespaceUri);

if (success)
{
    Console.WriteLine($"Successfully loaded types from namespace: {namespaceUri}");
}
```

### Working with Complex Type Properties

The `IStructure` interface provides flexible access to complex type fields:

#### Access by Name

```csharp
if (extensionObject.Body is IStructure complexType)
{
    // Get property value by name
    Variant value = complexType["PropertyName"];

    // Set property value by name
    complexType["PropertyName"] = newValue;
}
```

#### Access by Index

```csharp
if (extensionObject.Body is IStructure complexType)
{
    // Get property value by index
    Variant value = complexType[0];

    // Set property value by index
    complexType[0] = newValue;
}
```

#### Enumerate Properties

```csharp
if (extensionObject.Body is IStructure complexType)
{
    // Enumerate with detailed information
    foreach (IStructureField property in complexType.GetFields())
    {
        Console.WriteLine($"Property: {property.Name}");
        Console.WriteLine($"  Type: {property.TypeInfo}");
        Console.WriteLine($"  IsOptional: {property.IsOptional}");
        Console.WriteLine($"  Value: {complexType[property.Name]}");
    }
}
```

### Handling Enumeration Types

Custom enumerations are also supported:

```csharp
// After loading the type system, enum values are automatically decoded
DataValue dataValue = await session.ReadValueAsync(enumNodeId);

if (dataValue.WrappedValue.TryGet(out EnumValue enumValue))
{
    // The value is the numeric representation
    Console.WriteLine($"Enum value: {enumValue}");

    // You can also get the enum type from the factory
    ExpandedNodeId enumTypeId = ...;
    if (session.Factory.TryGetEnumeratedType(enumTypeId, out IEnumeratedType enumType))
    {
        Console.WriteLine($"Enum type: {enumType.XmlName}");
    }
}
```

### Type Information and Introspection

#### Get All Loaded Types

```csharp
var complexTypeSystem = new ComplexTypeSystem(session);
await complexTypeSystem.LoadAsync();

// Get all types that were dynamically created
IReadOnlyList<XmlQualifiedName> definedTypes = complexTypeSystem.GetDefinedTypes();

foreach (XmlQualifiedName type in definedTypes)
{
    Console.WriteLine($"Type: {type.Namespace}.{type.Name}");
}
```

#### Get Type Definitions

```csharp
// Get the DataTypeDefinition for a specific type
ExpandedNodeId dataTypeId = new ExpandedNodeId("ns=2;i=3001");
var definitions = complexTypeSystem.GetDataTypeDefinitionsForDataType(dataTypeId);

foreach (var kvp in definitions)
{
    Console.WriteLine($"NodeId: {kvp.Key}");
    Console.WriteLine($"Definition: {kvp.Value}");
}
```

#### Get Loaded Data Type IDs

```csharp
// Get all NodeIds for loaded data types
IEnumerable<ExpandedNodeId> dataTypeIds = complexTypeSystem.GetDefinedDataTypeIds();

foreach (var dataTypeId in dataTypeIds)
{
    Console.WriteLine($"Loaded type: {dataTypeId}");
}
```

### Using Custom Type Factories

You can provide a custom type factory for advanced scenarios:

```csharp
// Create a custom factory (implement IComplexTypeFactory)
IComplexTypeFactory customFactory = new MyCustomComplexTypeFactory();

// Use it with the ComplexTypeSystem
var complexTypeSystem = new ComplexTypeSystem(session, customFactory);
await complexTypeSystem.LoadAsync();
```

### Working with Telemetry and Logging

The ComplexTypeSystem supports the OPC UA telemetry context for observability:

```csharp
// Use the session's telemetry context (default)
var complexTypeSystem = new ComplexTypeSystem(session);

// Or provide a custom telemetry context
ITelemetryContext telemetry = myCustomTelemetryContext;
var complexTypeSystem = new ComplexTypeSystem(session, telemetry);

// The system will log type loading information
await complexTypeSystem.LoadAsync();
```

## Common Patterns

### Pattern 1: One-Time Type Loading at Session Start

```csharp
public async Task<ISession> CreateSessionWithTypes(string endpointUrl)
{
    var session = await Session.Create(
        configuration,
        new ConfiguredEndpoint(null, new EndpointDescription(endpointUrl)),
        false,
        "MyClient",
        60000,
        null,
        null
    );

    // Load all custom types immediately after connection
    var complexTypeSystem = new ComplexTypeSystem(session);
    await complexTypeSystem.LoadAsync();

    return session;
}
```

### Pattern 2: Lazy Loading on Demand

```csharp
public class OpcUaClient
{
    private ISession _session;
    private ComplexTypeSystem _complexTypeSystem;
    private bool _typesLoaded;

    public async Task EnsureTypesLoadedAsync()
    {
        if (!_typesLoaded)
        {
            _complexTypeSystem = new ComplexTypeSystem(_session);
            await _complexTypeSystem.LoadAsync();
            _typesLoaded = true;
        }
    }

    public async Task<DataValue> ReadComplexValueAsync(NodeId nodeId)
    {
        await EnsureTypesLoadedAsync();
        return await _session.ReadValueAsync(nodeId);
    }
}
```

### Pattern 3: Reading Multiple Complex Values

```csharp
public async Task ReadMultipleComplexValuesAsync(IList<NodeId> nodeIds)
{
    // Load types once
    var complexTypeSystem = new ComplexTypeSystem(session);
    await complexTypeSystem.LoadAsync();

    // Read all values
    var nodesToRead = nodeIds.Select(id => new ReadValueId
    {
        NodeId = id,
        AttributeId = Attributes.Value
    }).ToList();

    var response = await session.ReadAsync(
        null,
        0,
        TimestampsToReturn.Both,
        new ReadValueIdCollection(nodesToRead),
        CancellationToken.None
    );

    // Process results
    for (int i = 0; i < response.Results.Count; i++)
    {
        var dataValue = response.Results[i];
        if (dataValue.WrappedValue.TryGet(out ExtensionObject extensionObject) &&
            extensionObject.Body is IStructure complexType)
        {
            Console.WriteLine($"NodeId: {nodeIds[i]}");
            foreach (IStructureField property in complexType.GetFields())
            {
                Console.WriteLine($"  {property.Name}: {complexType[property.Name]}");
            }
        }
    }
}
```

## Error Handling

### Handling Type Loading Failures

```csharp
try
{
    var complexTypeSystem = new ComplexTypeSystem(session);
    bool success = await complexTypeSystem.LoadAsync(throwOnError: true);

    if (success)
    {
        Console.WriteLine("All types loaded successfully");
    }
    else
    {
        Console.WriteLine("Some types could not be loaded");
    }
}
catch (ServiceResultException ex)
{
    Console.WriteLine($"Failed to load types: {ex.Message}");
    // Handle error appropriately
}
```

### Handling Missing Type Definitions

```csharp
// Try to load a specific type
var complexTypeSystem = new ComplexTypeSystem(session);
IType? systemType = await complexTypeSystem.LoadTypeAsync(typeNodeId, throwOnError: false);

if (systemType == null)
{
    Console.WriteLine($"Type {typeNodeId} could not be loaded");
    // Fall back to reading as ExtensionObject with opaque body
}
```

## Performance Considerations

### Type System Caching

- The `ComplexTypeSystem` loads types once and caches them in the session's factory
- Subsequent reads/writes use the cached types automatically
- Types remain available for the lifetime of the session

### Minimizing Load Time

```csharp
// Load only enumerations (faster)
var complexTypeSystem = new ComplexTypeSystem(session);
await complexTypeSystem.LoadAsync(onlyEnumTypes: true);

// Or load only specific namespaces
await complexTypeSystem.LoadNamespaceAsync("http://mycompany.com/MyTypes");
```

### Batch Operations

When reading multiple values with complex types, read them in batches to minimize round-trips:

```csharp
// Read multiple values in one call
var response = await session.ReadAsync(
    null,
    0,
    TimestampsToReturn.Both,
    new ReadValueIdCollection(nodesToRead),
    CancellationToken.None
);
```

## Troubleshooting

### Types Not Loading

**Problem**: `LoadAsync()` completes but types are not available

**Solutions**:

- Verify the server supports DataTypeDefinition or provides type dictionaries
- Check server logs for encoding/dictionary availability
- Use verbose logging to see what types are discovered

```csharp
// Enable detailed logging through telemetry context
var telemetry = session.MessageContext.Telemetry;
var complexTypeSystem = new ComplexTypeSystem(session, telemetry);

// The ComplexTypeSystem will log detailed information during type loading
await complexTypeSystem.LoadAsync();
```

### Values Still Encoded as ExtensionObject

**Problem**: Values are read as `ExtensionObject` with opaque body instead of structured types

**Solutions**:

- Ensure `LoadAsync()` was called before reading the value
- Verify the type is actually loaded: `complexTypeSystem.GetDefinedTypes()`
- Check if the server's type definition is complete and valid

### Performance Issues

**Problem**: Type loading takes too long

**Solutions**:

- Load only required namespaces instead of all types
- Load types once at session start rather than on-demand
- Consider caching type information across sessions if reconnecting frequently

## API Reference

### ComplexTypeSystem Class

The main class for managing complex types.

#### Constructors

```csharp
// Create with session (uses DefaultComplexTypeFactory and session's telemetry)
ComplexTypeSystem(ISession session)

// Create with session and custom telemetry
ComplexTypeSystem(ISession session, ITelemetryContext telemetry)

// Create with session and custom type builder factory
ComplexTypeSystem(ISession session, IComplexTypeFactory complexTypeBuilderFactory)

// Create with session, custom factory and telemetry
ComplexTypeSystem(ISession session, IComplexTypeFactory complexTypeBuilderFactory, ITelemetryContext telemetry)

// Create with resolver and telemetry
ComplexTypeSystem(IComplexTypeResolver complexTypeResolver, ITelemetryContext telemetry)

// Create with resolver, custom factory and telemetry
ComplexTypeSystem(IComplexTypeResolver complexTypeResolver, IComplexTypeFactory complexTypeBuilderFactory, ITelemetryContext telemetry)

// Create with Reflection.Emit type builder (requires OPCFoundation.NetStandard.Opc.Ua.Client.ComplexTypes)
static ComplexTypeSystem ComplexTypeSystem.Create(ISession session, ITelemetryContext telemetry)
static ComplexTypeSystem ComplexTypeSystem.Create(IComplexTypeResolver complexTypeResolver, ITelemetryContext telemetry)
```

#### Methods

```csharp
// Load all custom types from the server
ValueTask<bool> LoadAsync(bool onlyEnumTypes = false, bool throwOnError = false, CancellationToken ct = default)

// Load a specific type with optional subtypes
Task<IType?> LoadTypeAsync(ExpandedNodeId nodeId, bool subTypes = false, bool throwOnError = false, CancellationToken ct = default)

// Load all types from a namespace
Task<bool> LoadNamespaceAsync(string ns, bool throwOnError = false, CancellationToken ct = default)

// Get all dynamically created type names
IReadOnlyList<XmlQualifiedName> GetDefinedTypes()

// Get all loaded data type NodeIds
IEnumerable<ExpandedNodeId> GetDefinedDataTypeIds()

// Get data type definitions for a type
NodeIdDictionary<DataTypeDefinition> GetDataTypeDefinitionsForDataType(ExpandedNodeId dataTypeId)

// Clear the data type cache
void ClearDataTypeCache()
```

#### Properties

```csharp
// Get the loaded data type dictionaries
NodeIdDictionary<DataDictionary> DataTypeSystem { get; }
```

### IStructure Interface

Interface for accessing properties of complex types.

```csharp
// Access property by zero-based index
Variant this[int index] { get; set; }

// Access property by name
Variant this[string name] { get; set; }

// Get property names
IReadOnlyList<IStructureField> GetFields()

```

### IStructureField Interface

Provides metadata about a property in a complex type.

```csharp
string Name { get; }                      // Property name
bool IsOptional { get; }                  // Whether field is optional
TypeInfo TypeInfo { get; }                // Type info of the field
```

## Known Limitations

1. **OptionSet Support**: OPC UA 1.04 OptionSet types do not automatically create enumeration flags
2. **Legacy Dictionary Support**: Some OPC UA 1.03 structured types that cannot be mapped to OPC UA 1.04 definitions are ignored
3. **Type Modifications**: Once loaded, types cannot be dynamically updated during a session. Reconnect to reload modified types.

## Additional Resources

- [OPC UA Specification Part 3 - Address Space Model](https://reference.opcfoundation.org/Core/Part3/)
- [OPC UA Specification Part 5 - Information Model](https://reference.opcfoundation.org/Core/Part5/)
- [OPC UA Specification Part 6 - Mappings](https://reference.opcfoundation.org/Core/Part6/)
- [OPC Foundation .NET Standard Samples](https://github.com/OPCFoundation/UA-.NETStandard-Samples)
- [NuGet Package: OPCFoundation.NetStandard.Opc.Ua.Client.ComplexTypes](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.Client.ComplexTypes/)

## See Also

- [Platform Build Documentation](PlatformBuild.md) - Information about building and versioning
- [Observability Documentation](Observability.md) - Information about logging and telemetry
- [Console Reference Client](../Applications/ConsoleReferenceClient/README.md) - Example client implementation
