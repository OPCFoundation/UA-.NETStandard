# OPC UA .NET Standard — Client.ComplexTypes (legacy reflection-emit loader)

`OPCFoundation.NetStandard.Opc.Ua.Client.ComplexTypes` is the legacy
**reflection-emit** complex-type and data-type loader. It walks a
server's `DataTypeSystem`, fetches the missing DataType definitions,
generates runtime types using `System.Reflection.Emit`, and registers
them with the `IServiceMessageContext` so the encoders / decoders can
round-trip server-defined Structures, Optionals, Unions, and
Enumerations.

## When to use this package

> **Most consumers do not need this package.** The
> `OPCFoundation.NetStandard.Opc.Ua.Client` package ships a
> NativeAOT-friendly complex-type loader that does **not** require
> `System.Reflection.Emit` — it builds the runtime
> `StructureDefinition` / `EnumDefinition` graphs directly. Prefer
> that path for any new application, and especially for trimmed /
> NativeAOT deployments where reflection-emit is unavailable.

Reference `Opc.Ua.Client.ComplexTypes` **only** when:

- You have a 1.5.378-era codebase that relied on the
  `ComplexTypeSystem` reflection-emit API and you are not ready to
  migrate, **or**
- You explicitly need to reflect over the runtime-generated CLR types
  (e.g. you bind them into a UI grid, serialize them through a
  third-party reflection-based serializer, or use them with
  `System.Linq.Expressions`).

For straight-through encode / decode of server-defined complex types
no consumer-side reflection is required — use `Opc.Ua.Client`.

## Getting started

```csharp
using Opc.Ua.Client;
using Opc.Ua.Client.ComplexTypes;

var loader = new ComplexTypeSystem(session);
await loader.LoadAsync();
// session.MessageContext.Factory now resolves the server's custom types
// as reflection-emit-generated CLR types.
```

## Target frameworks

`net472`, `net48`, `netstandard2.1`, `net8.0`, `net9.0`, `net10.0`.
The package is not NativeAOT-compatible because the runtime type
emission depends on `System.Reflection.Emit`.

## Additional documentation

See the [main repository README](https://github.com/OPCFoundation/UA-.NETStandard)
and the [Complex Types guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/ComplexTypes.md)
for the full design (legacy reflection-emit loader vs. the
NativeAOT-friendly loader in `Opc.Ua.Client`).
