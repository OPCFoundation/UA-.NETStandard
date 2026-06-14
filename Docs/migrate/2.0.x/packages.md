# Package, Target Framework, and Dependency Changes

> **When to read this:** Read this for NuGet package renames / additions / removals, the new published packages, target-framework changes on `Opc.Ua.Types`, and the Newtonsoft.Json removal from `Opc.Ua.Core`.

### New published packages

Two assemblies that previously shipped only as transitive content inside `Opc.Ua.Core` are now published as standalone NuGet packages. Add an explicit `<PackageReference>` only if your project depends on these types without also depending on `Opc.Ua.Core` (which still includes them transitively).

**`OPCFoundation.NetStandard.Opc.Ua.Core.Types`** (project `Stack/Opc.Ua.Core.Types/Opc.Ua.Core.Types.csproj`, `IsPackable=true`, target frameworks `$(LibCoreTargetFrameworks)`). Owns the framework-neutral built-in type and node-state contracts. Headline public types include `IServiceRequest`, `IServiceResponse`, `BaseEventState`, `EventSeverity`, `InstanceStateSnapshot`, `FolderState`, `FolderTypeState`, `LimitAlarmStates`, `ContentFilter` (including `Result` / `ElementResult`), and `MonitoringFilter` / `MonitoringFilterResult`.

```xml
<PackageReference Include="OPCFoundation.NetStandard.Opc.Ua.Core.Types" Version="2.0.*" />
```

**`OPCFoundation.NetStandard.Opc.Ua.Security.Certificates`** (project `Stack/Opc.Ua.Security.Certificates/Opc.Ua.Security.Certificates.csproj`, `IsPackable=true`, target frameworks `$(LibCoreTargetFrameworks)`). Owns the wrapper certificate type system. Headline public types: `Certificate`, `CertificateCollection`, `IX509Certificate`, `ICertificateFactory`, `ICertificateIssuer`, `CertificateChangeKind`, `X509AuthorityKeyIdentifierExtension`, `X509CrlNumberExtension`, `X509SubjectAltNameExtension`, `CRLReason`.

```xml
<PackageReference Include="OPCFoundation.NetStandard.Opc.Ua.Security.Certificates" Version="2.0.*" />
```

### Target Frameworks (only Opc.Ua.Types changes)

The TFM matrix for the main libraries (Core, Client, Server, Configuration, etc.) is unchanged from 1.5.378: `net472;net48;netstandard2.1;net8.0;net9.0;net10.0`. The only consumer-visible change is the `Opc.Ua.Types` assembly: on 1.5.378 it tracked the dedicated `LibTypesTargetFrameworks` variable (`net472;net48;netstandard2.0;netstandard2.1;net8.0;net9.0;net10.0`); on 2.0 the variable is removed and `Opc.Ua.Types` tracks `LibCoreTargetFrameworks`, the same matrix as every other library. The net effect is that `netstandard2.0` is no longer offered for `Opc.Ua.Types`.

The minimum SDK is the **.NET 10 SDK**, and projects compile with **`LangVersion 14.0`**. Projects that target `netstandard2.0` and pull in `Opc.Ua.Types` will fail to restore with `NU1202` ("package is not compatible") - retarget to `netstandard2.1` or one of the .NET / .NET Framework TFMs above.

### NuGet dependency additions and removals

| Package | Status in 2.0 | First introduced in |
|---|---|---|
| `DotNext` 5.26.3 | Added | `Libraries/Opc.Ua.Lds.Server/Opc.Ua.Lds.Server.csproj` |
| `Makaretu.Dns.Multicast` 0.27.0 | Added (pinned) | Centralised pin; previously vendored in-tree, no direct reference yet |
| `Microsoft.Bcl.TimeProvider` 10.0.8 | Added (pinned) | Centralised pin; transitive use for `TimeProvider` on net472/net48 |
| `Microsoft.CodeAnalysis.Analyzers` 4.14.0 | Added | `Stack/Opc.Ua.Core/Opc.Ua.Core.csproj` (runtime source-generation surface) |
| `Microsoft.CodeAnalysis.Common` 4.14.0 | Added | `Stack/Opc.Ua.Core/Opc.Ua.Core.csproj` |
| `Microsoft.CodeAnalysis.CSharp` 5.3.0 | Added | `Stack/Opc.Ua.Core/Opc.Ua.Core.csproj` |
| `Microsoft.Extensions.Configuration.Abstractions` 10.0.8 | Added (pinned) | Used by dependency injection integration |
| `Microsoft.Extensions.Diagnostics` 10.0.8 | Added (pinned) | Centralised pin |
| `Microsoft.Extensions.Hosting` 10.0.8 | Added (pinned) | Centralised pin |
| `Microsoft.Extensions.Hosting.Abstractions` 10.0.8 | Added (pinned) | Centralised pin |
| `Microsoft.Extensions.Options` 10.0.8 | Added (pinned) | Centralised pin |
| `Microsoft.Extensions.Options.ConfigurationExtensions` 10.0.8 | Added (pinned) | Centralised pin |
| `ModelContextProtocol` 1.3.0 | Added | `Applications/McpServer/Opc.Ua.Mcp.csproj` |
| `ModelContextProtocol.AspNetCore` 1.3.0 | Added | `Applications/McpServer/Opc.Ua.Mcp.csproj` |
| `SourceGenerator.Foundations` 2.0.14 | Added | `Tools/Opc.Ua.SourceGeneration.Stack/Opc.Ua.SourceGeneration.Stack.csproj` |
| `System.CommandLine` 2.0.8 | Added | `Applications/McpServer/Opc.Ua.Mcp.csproj` |
| `System.Threading.Channels` 10.0.8 | Added | `Libraries/Opc.Ua.Lds.Server/Opc.Ua.Lds.Server.csproj` |
| `TUnit` 1.45.8 | Added (test-only) | `Tests/Opc.Ua.Server.Tests/Opc.Ua.Server.Tests.csproj` |
| `NUnit.Analyzers` 4.13.0 | Added (test-only) | Test projects |
| `ObjectLayoutInspector` 0.2.0 | Added (test-only) | Test projects |
| `System.Reflection.Metadata` 10.0.6 | Added (test-only) | Test projects |
| `Mono.Options` 6.12.0.148 | Removed | Previously referenced by `Applications/ConsoleReferenceServer/MonoReferenceServer.csproj` |

### Newtonsoft.Json - what really changed

`Newtonsoft.Json` was removed as a direct dependency of `Stack/Opc.Ua.Core/Opc.Ua.Core.csproj` in 2.0. The only direct `<PackageReference Include="Newtonsoft.Json" ... />` remaining anywhere under `Libraries/` and `Stack/` is in `Libraries/Opc.Ua.PubSub/Opc.Ua.PubSub.csproj`. Consequences:

- Consumers that reached `Newtonsoft.Json` only transitively through `Opc.Ua.Core` now need to add their own explicit reference.
- Consumers of `Opc.Ua.PubSub` continue to receive `Newtonsoft.Json` transitively and are unaffected.

```xml
<PackageReference Include="Newtonsoft.Json" Version="13.0.4" />
```

Use `Version="13.0.4"` or any compatible later `13.x` release.

---

**See also**

- Related: [configuration.md](configuration.md), [encoders.md](encoders.md).
- [2.0 migration index](README.md) — analyzer quick-start + symptom → sub-doc table.
- [Migration Guide](../../MigrationGuide.md) — landing page across versions.

