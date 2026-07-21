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
  <AdditionalFiles Include="Path\To\Your\ModelFile.xml" />
</ItemGroup>
```

The file to include must be part of your project and have its `Build Action` set to `AdditionalFiles`.
When you build your project, the source generator will process the specified model files and generate
the corresponding C# classes and structures.  

Both `ModelDesign` and `NodeSet2` XML files are supported, and they can be combined in one project —
a node in one input may reference a type defined in another (for example, instances authored as a
`ModelDesign` whose `TypeDefinition` points at object types authored as a `NodeSet2`). Every input is
supplied to the others as a resolution dependency, so such cross-model references resolve automatically.

Per-file behaviour is controlled with `AdditionalFiles` metadata:

| Metadata | Description |
| -------- | ----------- |
| `ModelSourceGeneratorModelUri` | The model (namespace) URI of the input. Required when it cannot be inferred, and used to match the input to a namespace. |
| `ModelSourceGeneratorName` | Overrides the generated `Namespaces` class identifier for the model. |
| `ModelSourceGeneratorPrefix` | Overrides the C# namespace / prefix under which the model's types are generated. For a `NodeSet2` input this defaults to a value derived from the model URI — set it explicitly to choose the generated C# namespace. A `Prefix` declared inside a *referencing* `ModelDesign`'s `<opc:Namespaces>` does not rename the referenced model's generated types. |

```xml
<ItemGroup>
  <AdditionalFiles Include="Model\EquipmentTypes.NodeSet2.xml">
    <ModelSourceGeneratorModelUri>http://example.org/EquipmentTypes</ModelSourceGeneratorModelUri>
    <ModelSourceGeneratorPrefix>Example.EquipmentTypes</ModelSourceGeneratorPrefix>
  </AdditionalFiles>
  <AdditionalFiles Include="Model\Instances.ModelDesign.xml">
    <ModelSourceGeneratorModelUri>http://example.org/EquipmentInstances</ModelSourceGeneratorModelUri>
  </AdditionalFiles>
</ItemGroup>
```

See [Source-Generated NodeManagers](../../docs/SourceGeneratedNodeManagers.md#mixing-modeldesign-and-nodeset2-in-one-project)
for the end-to-end pattern.

## Generate code from WoT Thing Models / Thing Descriptions

An `AdditionalFiles` input can also be a W3C Web of Things (WoT) Thing Model
or Thing Description. It is converted in memory to a NodeSet2 document (using
the `Opc.Ua.Wot` converter) before the normal NodeSet2/ModelDesign pipeline
above runs — the rest of the generator cannot tell the difference. No file or
network I/O beyond the supplied `AdditionalFiles` content is performed, and
externally referenced TD/TM documents are not resolved.

The following extensions are always recognized as WoT model input:

| Extension | Content |
| --------- | ------- |
| `.tm.json` | Thing Model, plain JSON |
| `.td.json` | Thing Description, plain JSON |
| `.tm.jsonld` | Thing Model, JSON-LD |
| `.td.jsonld` | Thing Description, JSON-LD |

A plain `.jsonld` file is **not** treated as a WoT input by default — arbitrary
JSON-LD is not consumed as a model. Opt a specific file in with the
`ModelSourceGeneratorWot` metadata:

```xml
<ItemGroup>
  <AdditionalFiles Include="Model\Sensor.jsonld">
    <ModelSourceGeneratorWot>true</ModelSourceGeneratorWot>
  </AdditionalFiles>
</ItemGroup>
```

The same per-file metadata described above (`ModelSourceGeneratorModelUri`,
`ModelSourceGeneratorName`, `ModelSourceGeneratorPrefix`, as well as
`ModelSourceGeneratorVersion` and `ModelSourceGeneratorIgnore`) is honored on a
WoT input exactly as it is on a `NodeSet2`/`ModelDesign` input, and is
preserved after the WoT document is wrapped as an in-memory NodeSet2 file.

A malformed or unsupported WoT document never crashes the generator. Instead
it is reported through one of these diagnostics, and the affected input is
excluded from generation while every other input continues to be processed:

| Diagnostic | Meaning |
| ---------- | ------- |
| `MODELGEN030` | The document could not even be parsed as JSON, or exceeded a configured resource bound. |
| `MODELGEN031` | The converter reported an error (a resource bound, a missing preservation envelope/native mapping, a dependency/resolver failure, or another conversion error). The `Opc.Ua.Wot.WotDiagnosticCode` is embedded in the message. |
| `MODELGEN032` | The converter reported a warning (for example an unresolved dependency reference); generation continues with the best-effort result. |
| `MODELGEN033` | The converter reported an informational note (for example a deterministically generated NodeId). |
| `MODELGEN034` | The in-memory NodeSet2 path synthesized for a WoT input (for example `Foo.tm.json` → `Foo.NodeSet2.xml`) collides with an explicitly supplied `.NodeSet2.xml` or with another WoT input's synthesized path. Rename one of the inputs, or set distinct `ModelSourceGeneratorName`/`ModelSourceGeneratorPrefix` metadata. |

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

## ObjectType client proxies

In addition to data type generation, the source generator emits a strongly
typed asynchronous **client proxy** for every `ObjectType` defined in the
processed model. Each emitted class is named `{TypeName}Client` and exposes
the OPC UA methods declared by that ObjectType as `await`-able C# methods
that internally translate to a `CallRequest` over an `ISessionClient`.

### Inheritance and shadowing

Generated proxies form an inheritance chain that mirrors the OPC UA type
hierarchy:

* If an ObjectType derives from another ObjectType, its proxy derives
  from that parent's proxy — even when the parent lives in a different
  assembly (e.g. `DirectoryTypeClient : Opc.Ua.FolderTypeClient`).
* The roots of the chain (types that derive directly from
  `BaseObjectType`) ultimately inherit from the hand-authored abstract
  base `Opc.Ua.ObjectTypeClient`, which provides the `Session`,
  `ObjectId`, `Telemetry` properties and the `CallMethodAsync` helper
  used by every generated wrapper.
* When an ObjectType declares a method with the same browse name as an
  ancestor method but a different signature, the generated wrapper is
  emitted with the C# `new` modifier so the derived signature shadows
  the inherited one.

The standard UA stack ships with proxies for every ObjectType in the
core NodeSet (e.g. `FileTypeClient`, `TrustListTypeClient`,
`ServerConfigurationTypeClient`, …) emitted into the `Opc.Ua` namespace,
so downstream models can simply derive from them.

For an ergonomic, `System.IO`-style async client built on top of the
generated `FileTypeClient` / `FileDirectoryTypeClient` /
`TemporaryFileTransferTypeClient` proxies, see
[`docs/FileSystemClient.md`](../../docs/FileSystemClient.md).

### Output namespace

By default each model emits its proxies into its **own** namespace —
the C# prefix of the model's target namespace. For example:

| Model           | Proxy namespace |
| --------------- | --------------- |
| Standard UA     | `Opc.Ua`        |
| GDS             | `Opc.Ua.Gds`    |
| Custom NodeSet  | matches the model's target namespace prefix |

To redirect proxy emission to a different namespace, set the
`ModelSourceGeneratorObjectTypeProxyNamespace` MSBuild property.

### MSBuild properties

The proxy generator is controlled by the following MSBuild properties on
the consuming project:

| Property | Description |
| -------- | ----------- |
| `ModelSourceGeneratorOmitObjectTypeProxies` | Set to `true` to suppress emission of `*TypeClient` proxies. By default proxies are emitted for every `ObjectType` in the model alongside the standard model output (constants, NodeIds, NodeStates, DataTypes, schemas). |
| `ModelSourceGeneratorObjectTypeProxyNamespace` | Optional. Overrides the C# namespace used for the emitted proxy classes. Defaults to the model's own namespace. |
| `ModelSourceGeneratorUseTypeDefinitionModellingRules` | Set to `true` to make instance code generation use the modelling rules from the referenced type definition unconditionally, rather than the overridden rules on the instance. Off by default; enabled by default for stack generation. **Note:** regardless of this setting, the generator always enforces OPC UA modelling rule promotion semantics — instances may only *promote* the type definition's rule (`Optional→Mandatory`, `OptionalPlaceholder→Mandatory\|MandatoryPlaceholder`, `MandatoryPlaceholder→Mandatory`); demotions are silently rejected. |

Example (suppress proxy emission for a model that does not need them):

```xml
<PropertyGroup>
  <ModelSourceGeneratorOmitObjectTypeProxies>true</ModelSourceGeneratorOmitObjectTypeProxies>
</PropertyGroup>
```

## Implementation details

The source generator is implemented using the Roslyn API,which provides a rich set of tools for analyzing
and generating C# code. The generator finds all marker attributes such as `[DataType]` in your code and
builds the model design from it. It then processes the model design to generate the necessary C# code.
The annotated types are grouped by namespace. Each namespace will result in a separate generated file.
A namespace can be annotated with a namespace URI which 
