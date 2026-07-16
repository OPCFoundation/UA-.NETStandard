# OPC UA 1.5.378 → 2.0 compatibility shim

This project provides extension-method shims for the obsolete API surface that
2.0 is moving away from. It ships in the
`OPCFoundation.NetStandard.Opc.Ua.MigrationAnalyzer` NuGet package alongside the
analyzer + code-fixer + source-generator DLLs.

## Directory convention

Source files mirror the project layout in `src/` and `src/`,
flattened directly under the project root. For example:

| Source project              | Shim path           |
| --------------------------- | ------------------- |
| `Opc.Ua.Types`              | `Types/`            |
| `Opc.Ua.Core.Types`         | `Core/Types/` (combined with `Opc.Ua.Core`) |
| `Opc.Ua.Core`               | `Core/`             |
| `Opc.Ua.Client`             | `Client/`           |
| `Opc.Ua.Configuration`      | `Configuration/`    |
| `Opc.Ua.Gds.Client.Common`  | `Gds.Client.Common/`|

Within each top-level directory the file path mirrors the directory layout of
the source project — e.g. the obsolete extension surface around
`src/Opc.Ua.Core/Stack/Server/ServerBase.cs` lives at
`Core/Stack/Server/ServerBase.cs`.

## Conventions

- Every shim member carries **both** `[Obsolete]` and
  `[OpcUaShim(RuleId = "UANNNN")]` so the analyzer can route calls back to
  the corresponding migration-guide section.
- Sync-over-async shims use `Task.Run(() => XxxAsync()).GetAwaiter().GetResult()`
  to avoid sync-context deadlocks.
- Removed `<Type>Collection` wrapper types are **not** runtime-shimmed —
  the companion `Opc.Ua.MigrationAnalyzer.Generator.dll` emits per-consumer
  `internal sealed [Obsolete] class <Name>Collection : List<TElement>`
  shims into the consumer's compilation instead. Consumers walk the resulting
  `UA0002` analyzer warnings to migrate to `List<T>` / `ArrayOf<T>` at their
  own pace.
