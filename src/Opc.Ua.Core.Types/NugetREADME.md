# OPC UA .NET Standard — Core.Types

`OPCFoundation.NetStandard.Opc.Ua.Core.Types` provides generated standard
OPC UA NodeSet model and state types. It depends on
`OPCFoundation.NetStandard.Opc.Ua.Types`, which supplies built-in values,
encoding/context infrastructure and the node-state base classes.

## Overview

Reference this package when you need generated standard model types without
the channel layer in `Opc.Ua.Core`. A library that needs only UA built-in
values and foundational type infrastructure can reference `Opc.Ua.Types`.
The stack source generator emits the standard model types in this assembly.

## Target frameworks

`net48`, `net8.0`, `net9.0`,
`net10.0`.

## Additional documentation

See the [main repository README](https://github.com/OPCFoundation/UA-.NETStandard)
and the [Source-Generated Data Types guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/SourceGeneratedDataTypes.md)
for generated-type authoring. The
[Types migration guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/migrate/2.0.x/types.md)
covers changes to the foundational value types supplied by `Opc.Ua.Types`.
