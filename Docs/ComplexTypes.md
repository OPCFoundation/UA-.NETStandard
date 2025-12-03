# OPC UA Complex Types

## Overview

The `Opc.Ua.Client.ComplexTypes` library provides support for handling custom data types (complex types) in OPC UA client applications. Complex types include:

- **Custom Structures**: User-defined structured data types with multiple fields
- **Custom Enumerations**: User-defined enumeration types with custom values

This library allows OPC UA clients to automatically discover, load, and work with server-specific custom types, enabling seamless reading and writing of structured data without manual type definitions.

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
3. Dynamically generates .NET types at runtime
4. Registers these types in the session's type factory for encoding/decoding

### Supported Type Systems

The library supports multiple type definition mechanisms:

- **OPC UA 1.04+ DataTypeDefinition Attribute**: Modern structured type definitions
- **OPC UA 1.03 Binary Schema Dictionaries**: Legacy binary type dictionaries
- **OPC UA 1.03 XML Schema Dictionaries**: Legacy XML type dictionaries

The library automatically uses the most appropriate mechanism available on the server.

## Getting Started

### Installation

Add the NuGet package to your project:

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
var complexTypeSystem = new ComplexTypeSystem(session);
await complexTypeSystem.LoadAsync();

Console.WriteLine($"Loaded {complexTypeSystem.GetDefinedTypes().Length} custom types");
```

After loading, the session can automatically encode and decode custom types when reading or writing values.

#### 2. Reading Values with Complex Types

Once the type system is loaded, reading complex type values is straightforward:

```csharp
// Read a value that contains a complex type
NodeId nodeId = new NodeId("ns=2;s=MyCustomStructVariable");
DataValue dataValue = await session.ReadValueAsync(nodeId);

// The value is automatically decoded to a .NET type
if (dataValue.Value is ExtensionObject extensionObject)
{
    // Access the structured data
    if (extensionObject.Body is IComplexTypeProperties complexType)
    {
        // Access properties by name
        var temperatureValue = complexType["Temperature"];
        var pressureValue = complexType["Pressure"];
        
        Console.WriteLine($"Temperature: {temperatureValue}");
        Console.WriteLine($"Pressure: {pressureValue}");
        
        // Enumerate all properties
        foreach (var property in complexType.GetPropertyEnumerator())
        {
            Console.WriteLine($"{property.Name}: {property.GetValue(complexType)}");
        }
    }
}
```

#### 3. Writing Values with Complex Types

To write complex type values, modify the properties and write back:

```csharp
// Read current value
DataValue dataValue = await session.ReadValueAsync(nodeId);
var extensionObject = (ExtensionObject)dataValue.Value;
var complexType = (IComplexTypeProperties)extensionObject.Body;

// Modify properties
complexType["Temperature"] = 25.5;
complexType["Pressure"] = 101.3;

// Write the modified value back
var writeValue = new WriteValue
{
    NodeId = nodeId,
    AttributeId = Attributes.Value,
    Value = new DataValue(dataValue.WrappedValue)
    {
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
Type systemType = await complexTypeSystem.LoadTypeAsync(typeNodeId);

if (systemType != null)
{
    Console.WriteLine($"Loaded type: {systemType.Name}");
}
```

#### Load a Specific Type with Subtypes

```csharp
// Load a type and all its subtypes
ExpandedNodeId typeNodeId = new ExpandedNodeId("ns=2;i=3001");
Type systemType = await complexTypeSystem.LoadTypeAsync(typeNodeId, subTypes: true);
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

The `IComplexTypeProperties` interface provides flexible access to complex type fields:

#### Access by Name

```csharp
if (extensionObject.Body is IComplexTypeProperties complexType)
{
    // Get property value by name
    object value = complexType["PropertyName"];
    
    // Set property value by name
    complexType["PropertyName"] = newValue;
}
```

#### Access by Index

```csharp
if (extensionObject.Body is IComplexTypeProperties complexType)
{
    // Get property value by index
    object value = complexType[0];
    
    // Set property value by index
    complexType[0] = newValue;
}
```

#### Enumerate Properties

```csharp
if (extensionObject.Body is IComplexTypeProperties complexType)
{
    // Get all property names
    IList<string> names = complexType.GetPropertyNames();
    
    // Get all property types
    IList<Type> types = complexType.GetPropertyTypes();
    
    // Enumerate with detailed information
    foreach (ComplexTypePropertyInfo property in complexType.GetPropertyEnumerator())
    {
        Console.WriteLine($"Property: {property.Name}");
        Console.WriteLine($"  Type: {property.PropertyType}");
        Console.WriteLine($"  Order: {property.Order}");
        Console.WriteLine($"  IsOptional: {property.IsOptional}");
        Console.WriteLine($"  ValueRank: {property.ValueRank}");
        Console.WriteLine($"  Value: {property.GetValue(complexType)}");
    }
}
```

### Handling Enumeration Types

Custom enumerations are also supported:

```csharp
// After loading the type system, enum values are automatically decoded
DataValue dataValue = await session.ReadValueAsync(enumNodeId);

if (dataValue.Value is int enumValue)
{
    // The value is the numeric representation
    Console.WriteLine($"Enum value: {enumValue}");
    
    // You can also get the enum type from the factory
    var enumType = session.Factory.GetSystemType(enumTypeId);
    if (enumType?.IsEnum == true)
    {
        var enumName = Enum.GetName(enumType, enumValue);
        Console.WriteLine($"Enum name: {enumName}");
    }
}
```

### Type Information and Introspection

#### Get All Loaded Types

```csharp
var complexTypeSystem = new ComplexTypeSystem(session);
await complexTypeSystem.LoadAsync();

// Get all types that were dynamically created
Type[] definedTypes = complexTypeSystem.GetDefinedTypes();

foreach (Type type in definedTypes)
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
var customFactory = new MyCustomComplexTypeFactory();

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
        if (dataValue.Value is ExtensionObject extensionObject &&
            extensionObject.Body is IComplexTypeProperties complexType)
        {
            Console.WriteLine($"NodeId: {nodeIds[i]}");
            foreach (var property in complexType.GetPropertyEnumerator())
            {
                Console.WriteLine($"  {property.Name}: {property.GetValue(complexType)}");
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
Type systemType = await complexTypeSystem.LoadTypeAsync(typeNodeId, throwOnError: false);

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
// Enable detailed logging
var telemetry = session.MessageContext.Telemetry;
var logger = telemetry.CreateLogger<ComplexTypeSystem>();
var complexTypeSystem = new ComplexTypeSystem(session, telemetry);
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
// Create with session
ComplexTypeSystem(ISession session)
ComplexTypeSystem(ISession session, ITelemetryContext telemetry)
ComplexTypeSystem(ISession session, IComplexTypeFactory factory)
ComplexTypeSystem(ISession session, IComplexTypeFactory factory, ITelemetryContext telemetry)

// Create with resolver
ComplexTypeSystem(IComplexTypeResolver resolver, ITelemetryContext telemetry)
ComplexTypeSystem(IComplexTypeResolver resolver, IComplexTypeFactory factory, ITelemetryContext telemetry)
```

#### Methods

```csharp
// Load all custom types from the server
ValueTask<bool> LoadAsync(bool onlyEnumTypes = false, bool throwOnError = false, CancellationToken ct = default)

// Load a specific type with optional subtypes
Task<Type> LoadTypeAsync(ExpandedNodeId nodeId, bool subTypes = false, bool throwOnError = false, CancellationToken ct = default)

// Load all types from a namespace
Task<bool> LoadNamespaceAsync(string ns, bool throwOnError = false, CancellationToken ct = default)

// Get all dynamically created types
Type[] GetDefinedTypes()

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

### IComplexTypeProperties Interface

Interface for accessing properties of complex types.

#### Methods and Properties

```csharp
// Access by index
object this[int index] { get; set; }

// Access by name
object this[string name] { get; set; }

// Get property count
int GetPropertyCount()

// Get property names
IList<string> GetPropertyNames()

// Get property types
IList<Type> GetPropertyTypes()

// Enumerate properties with metadata
IEnumerable<ComplexTypePropertyInfo> GetPropertyEnumerator()
```

### ComplexTypePropertyInfo Class

Provides metadata about a property in a complex type.

#### Properties

```csharp
PropertyInfo PropertyInfo { get; }        // Reflection property info
string Name { get; }                      // Property name
Type PropertyType { get; }                // Property type
int Order { get; }                        // Field order in structure
bool IsOptional { get; }                  // Whether field is optional
int ValueRank { get; }                    // Array rank (-1 = scalar, >=0 = array)
BuiltInType BuiltInType { get; }         // OPC UA built-in type
```

#### Methods

```csharp
object GetValue(object o)                 // Get property value
void SetValue(object o, object v)         // Set property value
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
- [Reference Client Documentation](../Applications/ConsoleReferenceClient/README.md) - Example client implementation
