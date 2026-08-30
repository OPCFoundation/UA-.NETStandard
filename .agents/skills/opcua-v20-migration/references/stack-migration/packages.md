# Package, Target Framework, and Dependency Changes

> **When to read this:** Read this for NuGet package renames / additions / removals, the new published packages, target-framework changes on `Opc.Ua.Types`, and the Newtonsoft.Json removal from `Opc.Ua.Core`.

### New published packages

The 2.0 packages are currently prereleases on nuget.org. Use
`2.0.0-preview.*` to float to the latest published `2.0.0-preview.N` release,
or select *Include prerelease* in Visual Studio. No additional package source
is required.

Two assemblies that previously shipped only as transitive content inside `Opc.Ua.Core` are now published as standalone NuGet packages. Add an explicit `<PackageReference>` only if your project depends on these types without also depending on `Opc.Ua.Core` (which still includes them transitively).

**`OPCFoundation.NetStandard.Opc.Ua.Core.Types`** (project `src/Opc.Ua.Core.Types/Opc.Ua.Core.Types.csproj`, `IsPackable=true`, target frameworks `$(LibCoreTargetFrameworks)`). Owns the framework-neutral built-in type and node-state contracts. Headline public types include `IServiceRequest`, `IServiceResponse`, `BaseEventState`, `EventSeverity`, `InstanceStateSnapshot`, `FolderState`, `FolderTypeState`, `LimitAlarmStates`, `ContentFilter` (including `Result` / `ElementResult`), and `MonitoringFilter` / `MonitoringFilterResult`.

```xml
<PackageReference Include="OPCFoundation.NetStandard.Opc.Ua.Core.Types" Version="2.0.0-preview.*" />
```

**`OPCFoundation.NetStandard.Opc.Ua.Security.Certificates`** (project `src/Opc.Ua.Security.Certificates/Opc.Ua.Security.Certificates.csproj`, `IsPackable=true`, target frameworks `$(LibCoreTargetFrameworks)`). Owns the wrapper certificate type system. Headline public types: `Certificate`, `CertificateCollection`, `IX509Certificate`, `ICertificateFactory`, `ICertificateIssuer`, `CertificateChangeKind`, `X509AuthorityKeyIdentifierExtension`, `X509CrlNumberExtension`, `X509SubjectAltNameExtension`, `CRLReason`.

```xml
<PackageReference Include="OPCFoundation.NetStandard.Opc.Ua.Security.Certificates" Version="2.0.0-preview.*" />
```

### Target Frameworks (only Opc.Ua.Types changes)

The TFM matrix for the main libraries (Core, Client, Server, Configuration, etc.) is unchanged from 1.5.378: `net472;net48;netstandard2.1;net8.0;net9.0;net10.0`. The only consumer-visible change is the `Opc.Ua.Types` assembly: on 1.5.378 it tracked the dedicated `LibTypesTargetFrameworks` variable (`net472;net48;netstandard2.0;netstandard2.1;net8.0;net9.0;net10.0`); on 2.0 the variable is removed and `Opc.Ua.Types` tracks `LibCoreTargetFrameworks`, the same matrix as every other library. The net effect is that `netstandard2.0` is no longer offered for `Opc.Ua.Types`.

The minimum SDK is the **.NET 10 SDK**, and projects compile with **`LangVersion 14.0`**. Projects that target `netstandard2.0` and pull in `Opc.Ua.Types` will fail to restore with `NU1202` ("package is not compatible") - retarget to `netstandard2.1` or one of the .NET / .NET Framework TFMs above.

### NuGet dependency additions and removals

| Package | Status in 2.0 | Referenced by |
|---|---|---|
| `Makaretu.Dns.Multicast` 0.27.0 | Added | `src/Opc.Ua.Lds.Server/Opc.Ua.Lds.Server.csproj`; previously vendored in-tree |
| `Microsoft.Bcl.TimeProvider` 10.0.10 | Added | `src/Opc.Ua.Core`, `src/Opc.Ua.Core.Types`; backs `TimeProvider` on net472/net48 |
| `Microsoft.CodeAnalysis.Analyzers` 4.14.0 | Added (pinned) | Centralised pin only, no direct reference; holds the analyzer closure on the `roslyn.props` band |
| `Microsoft.CodeAnalysis.Common` 5.0.0 | Added | `tools/SourceGeneratorVariant.targets`, `tools/MigrationAnalyzerVariant.targets` |
| `Microsoft.CodeAnalysis.CSharp` 5.0.0 | Added | `tools/SourceGeneratorVariant.targets`, `tools/MigrationAnalyzerVariant.targets` |
| `Microsoft.Extensions.Caching.Abstractions` 10.0.10 | Added (pinned) | Introduced as a transitive dependency by the ModelContextProtocol 2.x SDK |
| `Microsoft.Extensions.Configuration.Abstractions` 10.0.10 | Added | `src/Opc.Ua.Client.ComplexTypes`, `src/Opc.Ua.PubSub` |
| `Microsoft.Extensions.Diagnostics` 10.0.10 | Added | `src/Opc.Ua.Core/Opc.Ua.Core.csproj` |
| `Microsoft.Extensions.Hosting` 10.0.10 | Added | Samples and tools that host a server or client |
| `Microsoft.Extensions.Hosting.Abstractions` 10.0.10 | Added | `src/Opc.Ua.Lds.Server` and other hosted-service libraries |
| `Microsoft.Extensions.Options` 10.0.10 | Added | Libraries that expose options-based configuration |
| `Microsoft.Extensions.Options.ConfigurationExtensions` 10.0.10 | Added | `src/Opc.Ua.PubSub/Opc.Ua.PubSub.csproj` |
| `ModelContextProtocol` 2.1.0 | Added | The `tools/Opc.Ua.Mcp*` projects |
| `ModelContextProtocol.AspNetCore` 2.1.0 | Added | `tools/Opc.Ua.Mcp/Opc.Ua.Mcp.csproj` |
| `ModelContextProtocol.Core` 2.1.0 | Added (pinned) | Centralised pin; the SDK requires an exact version |
| `System.CommandLine` 2.0.10 | Added | `tools/Opc.Ua.Mcp`, the console samples and the `fuzzing/*.Fuzz.Tools` projects |
| `System.Threading.Channels` 10.0.10 | Added | `src/Opc.Ua.Core`, `src/Opc.Ua.Core.Diagnostics`, `src/Opc.Ua.PubSub.Diagnostics` |
| `TUnit` 1.64.6 | Added (test-only) | `tests/Opc.Ua.Aot.Tests/Opc.Ua.Aot.Tests.csproj` |
| `NUnit.Analyzers` 4.14.0 | Added (test-only) | All NUnit test projects |
| `ObjectLayoutInspector` 0.2.0 | Added (test-only) | `tests/Opc.Ua.Types.Tests/Opc.Ua.Types.Tests.csproj` |
| `System.Reflection.Metadata` 9.0.0 | Added (pinned) | Centralised pin only, no direct reference; tracks `$(RoslynRuntimeVersion)` for the analyzer closure |
| `Mono.Options` 6.12.0.148 | Removed | Previously referenced by `samples/Reference/ConsoleReferenceServer/MonoReferenceServer.csproj` |

### ASP.NET Core packages are versioned per target framework

`Microsoft.AspNetCore.Authentication.Certificate`, `Microsoft.AspNetCore.Authentication.JwtBearer`, `Microsoft.AspNetCore.Mvc.Testing` and `Microsoft.AspNetCore.TestHost` ship one band per .NET major and, unlike the `Microsoft.Extensions.*` packages, carry no `netstandard2.0` asset and do not roll forward across majors - a `net8.0` project cannot consume the `10.0.x` band. `Directory.Packages.props` therefore selects the version from `$(TargetFramework)`: `net8.0` gets `8.0.29`, `net9.0` gets `9.0.18`, and every other TFM (including `net10.0` and the `net10.0` shell that legacy `netstandard2.0`/`netstandard2.1` `$(CustomTestTarget)` builds fall back to) gets `10.0.10`.

Consumers that pin these packages themselves are unaffected. Consumers that inherit them transitively through `Opc.Ua.Bindings.Https` receive the band matching their own target framework.

### Newtonsoft.Json - what really changed

`Newtonsoft.Json` was removed as a direct dependency of `src/Opc.Ua.Core/Opc.Ua.Core.csproj` in 2.0. The only direct `<PackageReference Include="Newtonsoft.Json" ... />` remaining anywhere under `src/` and `src/` is in `src/Opc.Ua.PubSub/Opc.Ua.PubSub.csproj`. Consequences:

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
- [Migration Guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/MigrationGuide.md) — landing page across versions.
