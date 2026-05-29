# OPC UA 1.5.378 → 1.6 compatibility shim

This project provides extension-method shims for the obsolete API surface that
the 1.6 release line is moving away from. It ships in the
`OPCFoundation.NetStandard.Opc.Ua.CodeFixers` NuGet package alongside the
analyzer DLL.

## Directory convention

Source files live under `Shims/<libname-without-Opc.Ua-prefix>/...` mirroring
the project layout in `Stack/` and `Libraries/`. For example:

| Source project            | Shim path        |
| ------------------------- | ---------------- |
| `Opc.Ua.Types`            | `Shims/Types/`   |
| `Opc.Ua.Core.Types`       | `Shims/Core.Types/` |
| `Opc.Ua.Core`             | `Shims/Core/`    |
| `Opc.Ua.Client`           | `Shims/Client/`  |
| `Opc.Ua.Configuration`    | `Shims/Configuration/` |
| `Opc.Ua.Gds.Client.Common`| `Shims/Gds.Client.Common/` |

Within each `Shims/<libname>/` directory the file path mirrors the directory
layout of the source project — e.g. `Stack/Opc.Ua.Core/Stack/Server/ServerBaseObsolete.cs`
moves to `Shims/Core/Stack/Server/ServerBase.cs`.

## Conventions

- Every shim member carries **both** `[Obsolete]` and
  `[OpcUaShim(RuleId = "UANNNN")]` so the analyzer can route calls back to
  the corresponding migration-guide section.
- Sync-over-async shims use `Task.Run(() => XxxAsync()).GetAwaiter().GetResult()`
  to avoid sync-context deadlocks (see Phase 6 in the plan).
- Removed types (e.g. the legacy `<Type>Collection` wrappers) are
  **not** shimmed — consumers must run the UA0002 fixer to migrate
  declarations to `List<T>` or `ArrayOf<T>`.

## Status

Phase 6.A scaffolding only. Move-from-libraries work happens in Phase 6.C;
new shims for genuinely-removed members in Phase 6.D.
