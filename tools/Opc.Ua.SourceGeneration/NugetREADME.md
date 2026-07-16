# OPC UA .NET Standard — source generators (server-side NodeSet emitter)

`OPCFoundation.NetStandard.Opc.Ua.SourceGeneration` ships the Roslyn
source generators that emit strongly-typed proxy classes from a
NodeSet XML for use on the **server** side. The standard NodeSet,
companion specs (Devices, GDS, WoT Connectivity, …), and custom
information models all flow through this generator to produce the
`ObjectType` / `VariableType` proxies a `NodeManager` consumes.

## Overview

Reference this package on a project that owns a NodeSet XML and wants
the generator to emit C# proxies at build time. The generator
participates in the standard `dotnet build` pipeline; no separate
tool invocation is required.

## Getting started

Reference the generator as an **analyzer** (no runtime dependency):

```xml
<ItemGroup>
  <ProjectReference Include="...\Opc.Ua.SourceGeneration.csproj">
    <OutputItemType>Analyzer</OutputItemType>
    <ReferenceOutputAssembly>false</ReferenceOutputAssembly>
  </ProjectReference>
  <AdditionalFiles Include="MyCompanionSpec.NodeSet2.xml" />
</ItemGroup>
```

## Target frameworks

`netstandard2.0` (Roslyn analyzer host TFM).

## Additional documentation

See the [main repository README](https://github.com/OPCFoundation/UA-.NETStandard)
and the
[Source-Generated Data Types guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/SourceGeneratedDataTypes.md).
