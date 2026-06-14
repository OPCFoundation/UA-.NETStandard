# OPC UA .NET Standard — Types (standard NodeSet proxies)

`OPCFoundation.NetStandard.Opc.Ua.Types` is the generated type
assembly for the standard OPC UA NodeSet (OPC 10000-5 / Part 5). It
contains the strongly-typed proxies for the standard
`ObjectType` / `VariableType` / `ReferenceType` definitions
(`BaseObjectState`, `BaseVariableState`, `MethodState`, all standard
NodeIds, browse names, status codes, error codes, …) plus the
companion data type classes (`Argument`, `Range`, `EUInformation`,
`HistoryUpdateResult`, …).

## Overview

Reference this package when you write a server-side
`NodeManager`, register a custom information model, or consume the
standard address-space proxies on the client side.

The classes in this package are emitted from the standard XML NodeSet
by the OPC UA source generators (`Opc.Ua.SourceGeneration.Stack`).

## Target frameworks

`net472`, `net48`, `netstandard2.0`, `netstandard2.1`, `net8.0`,
`net9.0`, `net10.0`.

## Additional documentation

See the [main repository README](https://github.com/OPCFoundation/UA-.NETStandard)
and the [Types guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/Types.md)
for the model-driven extension story (how to add companion specs and
plug new generated NodeSets into the stack).
