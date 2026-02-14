# Source generation for OPC UA .NET Standard Library

This project uses C# source generators to create parts of the OPC UA .NET Standard Library at compile time. 
Source generators are a powerful feature in C# that allow developers to generate additional source code 
during the compilation process, enabling more efficient and maintainable codebases.

The OPC UA source generator is designed to automate working with the OPC UA core library in that it supports
several ways to generate code at design time.

1. Generate all required classes for servers and clients from a given OPC UA information model. The model
   can be provided in various formats, such as NodeSet2 XML files or Model design xml file. 

2. **DataType Generators**: These generators create classes and structures that represent OPC UA data types,
   ensuring that the generated code adheres to the OPC UA specifications.



## Generate code from OPC UA Information Models

To generate code from OPC UA information models, you need to include the source generator in your project.
This can be done by adding the appropriate NuGet package to your project file.

```xml
<PackageReference Include="Opc.Ua.SourceGeneration" Version="x.y.z" />
```

Once the package is added, you can configure the source generator to process your OPC UA information models.
You can do this by adding an `ItemGroup` to your project file that specifies the model files to be processed.

```xml
<ItemGroup>
  <AdditionaFiles Include="Path\To\lYour\ModelFile.xml" />
</ItemGroup>
```

The file to include must be part of your project and have its `Build Action` set to `AdditionalFiles`.
When you build your project, the source generator will process the specified model files and generate
the corresponding C# classes and structures.  

## Using DataType Generators

The OPC UA source generator includes several data type generators that can be used to create classes
by annotating your existing code with specific attributes. Here are some of the available data type generators:

### **DataTypeAttribute** 

Generates IEncodeable implementation for your class. Simply annotate your class with the `[DataType]` attribute 
to generate the necessary serialization and deserialization code and other required implementation. 
The `[DataContract}` and `[DataMember]` attributes from `System.Runtime.Serialization` can be used to
control the serialization behavior.

```csharp
[DataType(TypeId = "nsu=http://your-namespace-uri/;i=1000")]
public partial class MyCustomDataType
{
    [DataMember(Order = 1)]
    public int Id { get; set; }
    [DataMember(Order = 2)]
    public string Name { get; set; }
}
```

>> IMPORTANT: your class must be a partial class with a default constructor for the source generator to work. 
   Do not implement the `IEncodeable` interface yourself, as this will conflict with the generated code.

The source generator generates the necessary implementation for the `IEncodeable` interface, including 
methods for encoding and decoding the data type.  If the type extends another type this type also must
be annotated with the `[DataType]` attribute to be included in the source generation. Only the properties
of the annotated type will be part of the generated implementation.

If the structure is abstract the type will be generated as abstract as well. 

The following functionality will be implemented over time

- Record types and structs
- Support for generating structures with optional fields, option set and union types
- Support to understand nullable annotation for optional fields
- Support for generating collection types (arrays, lists, etc.)
- Support for generating complex types with nested data types. If the type references other data types, these 
  must be built in or implement IEncodeable to be included.

## Implementation details

The source generator is implemented using the Roslyn API, which provides a rich set of tools for analyzing
and generating C# code. The generator finds all marker attributes such as `[DataType]` in your code and
builds the model design from it. It then processes the model design to generate the necessary C# code.
The annotated types are grouped by namespace. Each namespace will result in a separate generated file.
A namespace can be annotated with a namespace URI which 
