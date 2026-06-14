# OPC UA .NET Standard — Core.Types

`OPCFoundation.NetStandard.Opc.Ua.Core.Types` is the core type
infrastructure that backs every OPC UA encoded message: the built-in
data types (`NodeId`, `Variant`, `DataValue`, `LocalizedText`,
`QualifiedName`, `StatusCode`, `ExtensionObject`, `DiagnosticInfo`,
`ByteString`, `ArrayOf<T>`, …), the
`IServiceMessageContext` surface, and the standard `StringTable` /
`NamespaceTable` plumbing the encoders use.

## Overview

Most consumers reach for `Opc.Ua.Types` (the generated type assembly)
or `Opc.Ua.Core` (which depends on this package). Reference
`Opc.Ua.Core.Types` directly when you need a strict subset — for
example a library that only encodes / decodes UA primitives without
pulling in the channel layer.

## Target frameworks

`net472`, `net48`, `netstandard2.0`, `netstandard2.1`, `net8.0`,
`net9.0`, `net10.0`.

## Additional documentation

See the [main repository README](https://github.com/OPCFoundation/UA-.NETStandard)
and the [Types guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/Types.md)
for the full built-in type design, value representation rules, and the
migration notes (especially around the readonly-struct conversion of
`NodeId`, `Variant`, `DataValue`, etc. in 2.0).
