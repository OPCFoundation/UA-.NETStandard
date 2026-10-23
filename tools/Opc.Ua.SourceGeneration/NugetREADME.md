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
  <ProjectReference Include="../Opc.Ua.SourceGeneration.csproj">
    <OutputItemType>Analyzer</OutputItemType>
    <ReferenceOutputAssembly>false</ReferenceOutputAssembly>
  </ProjectReference>
  <AdditionalFiles Include="MyCompanionSpec.NodeSet2.xml" />
</ItemGroup>
```

`AdditionalFiles` inputs may also be `NodeSet2`/`ModelDesign` XML, or a WoT
Thing Model/Thing Description (`.tm.json`, `.td.json`, `.tm.jsonld`,
`.td.jsonld`, or an opted-in plain `.jsonld`) which is converted to a NodeSet2
document entirely in memory before generation. See
[`readme.md`](readme.md#generate-code-from-wot-thing-models--thing-descriptions)
for details, per-file options and diagnostics.

Generated factories retain explicit user write/execution restrictions and
namespace-qualified role identifiers, including runtime-provided roles.
Referenced-model security metadata must be produced and consumed by compatible
generators: upgrade both sides and regenerate affected models. Invalid or
unrepresentable security metadata fails generation rather than dropping policy.

Named ModelDesign permission sets are expanded in the declaring model before
dependency hierarchies are copied. A consumer's identically named permission set
does not replace an upstream ACL; an explicit local override still takes
precedence.

Dependency security trailer version 2 additionally preserves `AccessRestrictions`
and `DefaultAccessRestrictions`, including their independent presence. The
ModelDesign values `None` and `ApplyToBrowse` represent explicit NodeSet masks
`0` and `8`; neither is treated as an omitted attribute. Existing enumeration
values keep their numeric identities. The reader accepts legacy payloads without
security metadata and version-1 security trailers. Unknown versions and malformed
or truncated security extensions fail generation.

Upgrade both producer and consumer generators and rebuild the producer to
propagate channel restrictions across referenced assemblies. An older producer's
payload never recorded those restrictions; a new consumer cannot reconstruct
missing policy from that payload. An older consumer cannot interpret version-2
security metadata and must also be upgraded.

Current payloads mark declarations that require their security extension, so
removing the entire extension is also rejected by current consumers. Older
payloads without that marker retain their legacy interpretation.

## Target frameworks

`netstandard2.0` (Roslyn analyzer host TFM).

## Additional documentation

See the [main repository README](https://github.com/OPCFoundation/UA-.NETStandard)
and the
[Source-Generated Data Types guide][source-generated-data-types].

[source-generated-data-types]: https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/SourceGeneratedDataTypes.md
