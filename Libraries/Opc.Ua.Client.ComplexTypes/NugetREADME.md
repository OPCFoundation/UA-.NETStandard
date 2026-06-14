# OPC UA .NET Standard — Client.ComplexTypes

`OPCFoundation.NetStandard.Opc.Ua.Client.ComplexTypes` is the
client-side complex-type and data-type loader. It walks a server's
`DataTypeSystem`, fetches the missing DataType definitions, generates
runtime types (`StructureDefinition`, `EnumDefinition`,
`OptionSetDefinition`), and registers them with the
`IServiceMessageContext` so the encoders / decoders can round-trip
custom Structures, Optionals, Unions, and Enumerations.

## Overview

Reference this package alongside `OPCFoundation.NetStandard.Opc.Ua.Client`
when the servers you connect to expose application-specific complex
types that are not part of the standard NodeSet (companion specs,
custom DataTypes added at deployment time, …).

## Getting started

```csharp
using Opc.Ua.Client;
using Opc.Ua.Client.ComplexTypes;

var loader = new ComplexTypeSystem(session);
await loader.LoadAsync();
// session.MessageContext.Factory now resolves the server's custom types.
```

## Target frameworks

`net48`, `netstandard2.0`, `netstandard2.1`, `net8.0`, `net9.0`,
`net10.0`.

## Additional documentation

See the [main repository README](https://github.com/OPCFoundation/UA-.NETStandard)
and the [Complex Types guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/ComplexTypes.md).
