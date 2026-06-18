# OPC UA .NET Standard — source generation core helpers

`OPCFoundation.NetStandard.Opc.Ua.SourceGeneration.Core` contains the
shared types used by the OPC UA source generators
(`Opc.Ua.SourceGeneration`, `Opc.Ua.SourceGeneration.Stack`) and the
runtime types the emitted proxies depend on. It is referenced
transitively from any package whose proxies are source-generated.

## Overview

Reference this package directly only when you are authoring a custom
source generator that participates in the OPC UA NodeSet emit
pipeline.

## Target frameworks

`net472`, `net48`, `netstandard2.0`, `netstandard2.1`, `net8.0`,
`net9.0`, `net10.0`.

## Additional documentation

See the [main repository README](https://github.com/OPCFoundation/UA-.NETStandard)
and the
[Source-Generated Data Types guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/SourceGeneratedDataTypes.md).
