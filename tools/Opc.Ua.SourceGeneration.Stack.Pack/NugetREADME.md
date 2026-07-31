# OPC UA .NET Standard — source generators (stack-side NodeSet emitter)

`OPCFoundation.NetStandard.Opc.Ua.SourceGeneration.Stack` ships the
Roslyn source generators that emit the standard NodeSet proxies
consumed by `OPCFoundation.NetStandard.Opc.Ua.Core`,
`Opc.Ua.Types`, and `Opc.Ua.Core.Types`. It is the *internal*
generator the stack uses to keep the standard
`ObjectType` / `VariableType` proxies, well-known `NodeId`s, and
service-call wrappers in sync with the standard XML NodeSet.

## Overview

Reference this package only when you are forking the stack or
maintaining a parallel set of standard proxies. End-user consumers
should use `OPCFoundation.NetStandard.Opc.Ua.SourceGeneration` (or
just reference `Opc.Ua.Core` and `Opc.Ua.Types`).

## Target frameworks

`netstandard2.0` (Roslyn analyzer host TFM).

## Supported hosts

The package ships the generator under a Roslyn-versioned analyzer folder.
The .NET SDK loads it when its compiler supports that Roslyn API and
ignores it otherwise, so an older host cleanly skips the generator.

| Roslyn API | Minimum host |
| --- | --- |
| 4.14 | Visual Studio 2022 17.14 / .NET 9 SDK |
| 5.0 | Visual Studio 2026 18.0 / .NET 10 SDK |

## Additional documentation

See the [main repository README](https://github.com/OPCFoundation/UA-.NETStandard)
and the
[Source-Generated Data Types guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/SourceGeneratedDataTypes.md).
