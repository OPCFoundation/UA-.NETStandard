# Source Generation

> **When to read this:** Read this for the move from pre-generated code files to the source-generated NodeManager / data-type model, including the project-structure changes and the new default for boolean properties.

Instead of generating code for OPC UA design files using the [ModelCompiler](https://github.com/OPCFoundation/UA-ModelCompiler), this version of the stack uses [Source Generators](https://learn.microsoft.com/dotnet/csharp/roslyn-sdk/#source-generators) to generate code behind for your project. Input into the source generator can be NodeSet2.xml files or ModelDesign.xml files (the same that ModelCompiler consumes). Example projects are provided in the Applications folder. Source generators are Roslyn analyzers, that are called by the Roslyn compiler and emit code during the build process.

**Model compiler generated csharp code is not supported in this version!**

To migrate remove all your generated files (ending in `*.Classes.cs`, `*.Constants.cs`, etc.) and only leave the design file(s) (.xml and .csv files) in your project. Add an entry into your `csproj` file similar to the following to provide the location of the design files to the source generation process:

```xml
  <PropertyGroup>
    <!-- Optional: to configure whether to allow sub types - see model compiler documentation -->
    <ModelSourceGeneratorUseAllowSubtypes>true</ModelSourceGeneratorUseAllowSubtypes>
  </PropertyGroup>
  <ItemGroup>
    <!-- Generate code behind for the following design or nodeset2.xml files during build-->
    <AdditionalFiles Include="Boiler\Generated\BoilerDesign.csv" />
    <AdditionalFiles Include="Boiler\Generated\BoilerDesign.xml" />
    <AdditionalFiles Include="MemoryBuffer\Generated\MemoryBufferDesign.csv" />
    <AdditionalFiles Include="MemoryBuffer\Generated\MemoryBufferDesign.xml" />
    <AdditionalFiles Include="TestData\Generated\TestDataDesign.csv" />
    <AdditionalFiles Include="TestData\Generated\TestDataDesign.xml" />
  </ItemGroup>
```

The [source generator model](https://devblogs.microsoft.com/dotnet/introducing-c-source-generators/) has several benefits that go beyond custom `msbuild` targets: Among the most important is that the generator ships with the stack and therefore code that is generated conforms to the stack version that ships the analyzer (the source generator will be part of `Opc.Ua.Core` nuget package). Therefore when updating to a newer version the code generated automatically takes advantage of the improvements made across the entire stack.

Code generation during compilation also allows not just emitting code ahead of time, but also to generate code while you are developing. We now take advantage of this feature to generate `IEncodeable` implementations for partial POCO types on the fly using the `[DataType]` and `[DataTypeField]` attributes as annotation (similar to `DataContract`/`DataMember`).

The stack itself uses source generators to generate the core opc ua code. Therefore all pre-generated code files (`Generated/` folders) have been removed and are now generated at build time. As a result of using source generators to generate the stack code all `*.nodeset2.xml` files previously included as embedded zip have been removed. Also, all `*.Types.xsd` and `*.Types.bsd` files are now included as string resource instead of embedded resources. If you need access to these, use the new `Schemas.XmlAsStream` and `Schemas.BinaryAsStream` APIs in the node manager namespace which produce a utf8 stream. Alternatively you can use the existing ModelCompiler tool to generate these files.

When you encounter slower build times use incremental compilation and avoid changes to code in Opc.Ua and Opc.Ua.Core project. In addition you can change your builds to only build for your target framework using the dotnet `-f <tfm>` command line option, e.g. `-f net10`.

### Default value of boolean properties in source-generated data types is now false

**Breaking Change**: Boolean properties on source-generated data types now correctly default to `false` instead of `true`.

Generated code produced by the model compiler contained a bug because it inverted the default value for boolean fields in generated data types. Boolean fields without an explicit `<DefaultValue>` in the model design XML were initialized to `true` instead of `false` as expected and defined in Part 6. This has been fixed.

**Impact**: Any code that creates instances of source-generated data types and relies on boolean properties being `true` by default must now explicitly set those properties to `true`. This primarily affects PubSub configuration types:

| Type | Property | Old Default | New Default |
|---|---|---|---|
| `PubSubConfigurationDataType` | `Enabled` | `true` | `false` |
| `PubSubConnectionDataType` | `Enabled` | `true` | `false` |
| `WriterGroupDataType` | `Enabled` | `true` | `false` |
| `ReaderGroupDataType` | `Enabled` | `true` | `false` |
| `DataSetWriterDataType` | `Enabled` | `true` | `false` |
| `DataSetReaderDataType` | `Enabled` | `true` | `false` |
| `PublishedDataSetCustomSourceDataType` | `CyclicDataSet` | `true` | `false` |

Other affected types include all source-generated structures with boolean fields (e.g., `AggregateConfiguration.TreatUncertainAsBad`, `MonitoringParameters.DiscardOldest`, `CreateSubscriptionRequest.PublishingEnabled`) as well as
some hand-written types in `Opc.Ua.Types` (such as `BrowseDescription`, `RelativePathElement`).

**Migration**: Add explicit initialization where your code depends on `true` as the default:

```csharp
// Before (relied on incorrect true default)
var connection = new PubSubConnectionDataType
{
    Name = "MyConnection"
};

// After (explicitly set Enabled)
var connection = new PubSubConnectionDataType
{
    Enabled = true,
    Name = "MyConnection"
};
```

### Server default Aggregate configuration now treats Uncertain as Bad (Part 13)

**Behavioral Change (Part 13 compliance)**: The server-side default aggregate configuration returned by
`AggregateManager.GetDefaultConfiguration(...)` — used when a `ReadProcessedDetails` request sets
`AggregateConfiguration.UseServerCapabilitiesDefaults = true` — now sets `TreatUncertainAsBad = true`,
matching the default mandated by OPC 10000-13 (Aggregates) v1.05.07 §4.2.1.2. Previously it defaulted to
`false`.

**Impact**: Processed (aggregate) history reads that rely on the server-capabilities defaults now treat
Uncertain-quality samples as Bad when computing aggregate `StatusCode`s (unless a specific aggregate
definition states otherwise). Clients that require the previous behavior should send an explicit
`AggregateConfiguration` with `TreatUncertainAsBad = false` instead of `UseServerCapabilitiesDefaults = true`.

### Project Structure

New `Opc.Ua` project as an intermediate project. Impact:

- Most applications using NuGet packages are not affected. Continue linking to Opc.Ua.Core project as it includes the Opc.Ua intermediate assembly
- Assembly loading order *may* change

---

**See also**

- Related: [types.md](types.md), [encoders.md](encoders.md), [node-states.md](node-states.md).
- [2.0 migration index](README.md) — analyzer quick-start + symptom → sub-doc table.
- [Migration Guide](../../MigrationGuide.md) — landing page across versions.

