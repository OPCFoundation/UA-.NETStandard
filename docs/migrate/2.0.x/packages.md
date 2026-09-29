# Package, Target Framework, and Dependency Changes

> **When to read this:** Read this for NuGet package renames / additions / removals, the new published packages, target-framework changes on `Opc.Ua.Types`, and the Newtonsoft.Json removal from `Opc.Ua.Core`.

## Contents

- [New published packages](#new-published-packages)
- [Renamed packages — the GDS libraries drop `.Common`](#renamed-packages--the-gds-libraries-drop-common)
- [`.Debug` packages are no longer published to nuget.org](#debug-packages-are-no-longer-published-to-nugetorg)
- [Target Frameworks (only Opc.Ua.Types changes)](#target-frameworks-only-opcuatypes-changes)
- [NuGet dependency additions and removals](#nuget-dependency-additions-and-removals)
- [ASP.NET Core packages are versioned per target framework](#aspnet-core-packages-are-versioned-per-target-framework)
- [Newtonsoft.Json - what really changed](#newtonsoftjson---what-really-changed)

## New published packages

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

## Renamed packages — the GDS libraries drop `.Common`

The three GDS packages lose their `.Common` suffix, so the family matches every
other companion specification in the stack (`Opc.Ua.Di`, `Opc.Ua.ISA95`,
`Opc.Ua.Robotics`, … each ship as `<Family>` / `<Family>.Client` /
`<Family>.Server`).

| 1.5.378 package | 2.0 package |
|---|---|
| `OPCFoundation.NetStandard.Opc.Ua.Gds.Common` | `OPCFoundation.NetStandard.Opc.Ua.Gds` |
| `OPCFoundation.NetStandard.Opc.Ua.Gds.Client.Common` | `OPCFoundation.NetStandard.Opc.Ua.Gds.Client` |
| `OPCFoundation.NetStandard.Opc.Ua.Gds.Server.Common` | `OPCFoundation.NetStandard.Opc.Ua.Gds.Server` |

Update the `<PackageReference>` id; nothing else changes:

```xml
<!-- Before -->
<PackageReference Include="OPCFoundation.NetStandard.Opc.Ua.Gds.Server.Common" Version="1.5.378.145" />

<!-- After -->
<PackageReference Include="OPCFoundation.NetStandard.Opc.Ua.Gds.Server" Version="2.0.0-preview.*" />
```

**No `using` directives change.** The assemblies were already named for their
namespaces — `Opc.Ua.Gds`, `Opc.Ua.Gds.Client`, `Opc.Ua.Gds.Server` — and only
the assembly and package ids carried the `.Common` suffix. Source that compiled
against 1.5.378 compiles unchanged once the package id is updated.

Assembly names follow the package ids (`Opc.Ua.Gds.Server.dll`, not
`Opc.Ua.Gds.Server.Common.dll`), so update any binding redirects, ILMerge or
trimming descriptors, or signing manifests that name the files directly.

## `.Debug` packages are no longer published to nuget.org

Up to `1.5.378` and through `2.0.0-preview.5`, every package also had a
Debug-configuration build on nuget.org under a `.Debug` suffix, for example
`OPCFoundation.NetStandard.Opc.Ua.Core.Debug`. From `2.0.0-preview.6` on,
nuget.org receives only the Release packages and their symbol packages
(`.snupkg`), including the `OPCFoundation.NetStandard.Opc.Ua` meta-package. The
`.Debug` packages are still built for every release, but they are published
only to the
[GitHub Packages feed](https://github.com/orgs/OPCFoundation/packages?repo_name=UA-.NETStandard).

`OPCFoundation.NetStandard.Opc.Ua.Symbols` is discontinued; `2.0.0-preview.5`
is its last version. It was the Debug counterpart of the meta-package: it
depended on the `.Debug` package IDs, so it could no longer be restored from
nuget.org. Reference the individual `.Debug` packages you need instead, as
described below, or use the `OPCFoundation.NetStandard.Opc.Ua` meta-package.

If you reference a `.Debug` package, do one of the following:

- **Switch to the Release package** by dropping the `.Debug` suffix. For
  source-level debugging, the symbol packages on nuget.org provide the
  portable PDBs, and Source Link maps them to this repository.

  ```xml
  <!-- Before -->
  <PackageReference Include="OPCFoundation.NetStandard.Opc.Ua.Client.Debug" Version="1.5.378.176" />

  <!-- After -->
  <PackageReference Include="OPCFoundation.NetStandard.Opc.Ua.Client" Version="2.0.0-preview.*" />
  ```

- **Keep the `.Debug` package** by adding the GitHub Packages feed as a
  package source. It requires authentication, even for public packages: use a
  classic personal access token with the `read:packages` scope.

  ```xml
  <!-- nuget.config -->
  <configuration>
    <packageSources>
      <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
      <add key="opcfoundation" value="https://nuget.pkg.github.com/OPCFoundation/index.json" />
    </packageSources>
    <packageSourceCredentials>
      <opcfoundation>
        <add key="Username" value="%GITHUB_USER%" />
        <add key="ClearTextPassword" value="%GITHUB_TOKEN%" />
      </opcfoundation>
    </packageSourceCredentials>
  </configuration>
  ```

  The feed also carries in-development builds from `master`
  (`2.0.0-preview.<N>.g<commit>`), which sort above the published previews,
  so a floating version such as `2.0.0-preview.*` resolves to a `master` build.
  **Pin an exact version** of each `.Debug` package, matching the Release
  version you use:

  ```xml
  <PackageReference Include="OPCFoundation.NetStandard.Opc.Ua.Client.Debug" Version="2.0.0-preview.6" />
  ```

  Optionally, add [package source mapping](https://learn.microsoft.com/nuget/consume-packages/package-source-mapping)
  so that only the `.Debug` IDs can come from this feed. Source mapping
  controls which source provides a package ID, not which versions of it are
  chosen, so it does not replace pinning.

## Target Frameworks (only Opc.Ua.Types changes)

The main library target matrix includes `net472`, `net48`, `netstandard2.1`,
`net8.0`, `net9.0`, and `net10.0`. `Opc.Ua.Types` additionally targets
`netstandard2.0`: its project uses `$(LibCoreTargetFrameworks);netstandard2.0`.
A Types-only consumer does not need to retarget merely because it uses
`netstandard2.0`. Higher-level packages can have different requirements; check
the target assets of every package your application consumes.

Building this repository requires the **.NET 10 SDK** and **C# 14.0**.
Those build prerequisites are not the minimum runtime for every NuGet consumer.
See [platform support](../../DeveloperGuide.md#packages-platform-support-and-versioning).

## NuGet dependency additions and removals

| Package | Status in 2.0 | Referenced by |
|---|---|---|
| `Makaretu.Dns.Multicast` 0.27.0 | Added | `src/Opc.Ua.Lds.Server/Opc.Ua.Lds.Server.csproj`; previously vendored in-tree |
| `Microsoft.Bcl.TimeProvider` 10.0.11 | Added | `src/Opc.Ua.Core`, `src/Opc.Ua.Core.Types`; backs `TimeProvider` on net472/net48 |
| `Microsoft.CodeAnalysis.Analyzers` 4.14.0 | Added (pinned) | Centralised pin only, no direct reference; holds the analyzer closure on the `roslyn.props` band |
| `Microsoft.CodeAnalysis.Common` 5.0.0 | Added | `tools/SourceGeneratorVariant.targets`, `tools/MigrationAnalyzerVariant.targets` |
| `Microsoft.CodeAnalysis.CSharp` 5.0.0 | Added | `tools/SourceGeneratorVariant.targets`, `tools/MigrationAnalyzerVariant.targets` |
| `Microsoft.Extensions.Caching.Abstractions` 10.0.11 | Added (pinned) | Introduced as a transitive dependency by the ModelContextProtocol 2.x SDK |
| `Microsoft.Extensions.Configuration.Abstractions` 10.0.11 | Added | `src/Opc.Ua.Client.ComplexTypes`, `src/Opc.Ua.PubSub` |
| `Microsoft.Extensions.Diagnostics` 10.0.11 | Added | `src/Opc.Ua.Core/Opc.Ua.Core.csproj` |
| `Microsoft.Extensions.Hosting` 10.0.11 | Added | Samples and tools that host a server or client |
| `Microsoft.Extensions.Hosting.Abstractions` 10.0.11 | Added | `src/Opc.Ua.Lds.Server` and other hosted-service libraries |
| `Microsoft.Extensions.Options` 10.0.11 | Added | Libraries that expose options-based configuration |
| `Microsoft.Extensions.Options.ConfigurationExtensions` 10.0.11 | Added | `src/Opc.Ua.PubSub/Opc.Ua.PubSub.csproj` |
| `ModelContextProtocol` 2.1.0 | Added | The `tools/Opc.Ua.Mcp*` projects |
| `ModelContextProtocol.AspNetCore` 2.1.0 | Added | `tools/Opc.Ua.Mcp/Opc.Ua.Mcp.csproj` |
| `ModelContextProtocol.Core` 2.1.0 | Added (pinned) | Centralised pin; the SDK requires an exact version |
| `System.CommandLine` 2.0.11 | Added | `tools/Opc.Ua.Mcp`, the console samples and the `fuzzing/*.Fuzz.Tools` projects |
| `System.Threading.Channels` 10.0.11 | Added | `src/Opc.Ua.Core`, `src/Opc.Ua.Core.Diagnostics`, `src/Opc.Ua.PubSub.Diagnostics` |
| `TUnit` 1.65.68 | Added (test-only) | `tests/Opc.Ua.Aot.Tests/Opc.Ua.Aot.Tests.csproj` |
| `NUnit.Analyzers` 4.14.0 | Added (test-only) | All NUnit test projects |
| `ObjectLayoutInspector` 0.2.0 | Added (test-only) | `tests/Opc.Ua.Types.Tests/Opc.Ua.Types.Tests.csproj` |
| `System.Reflection.Metadata` 9.0.0 | Added (pinned) | Centralised pin only, no direct reference; tracks `$(RoslynRuntimeVersion)` for the analyzer closure |
| `Mono.Options` 6.12.0.148 | Removed | Previously referenced by `samples/Reference/ConsoleReferenceServer/MonoReferenceServer.csproj` |

## ASP.NET Core packages are versioned per target framework

`Microsoft.AspNetCore.Authentication.Certificate`, `Microsoft.AspNetCore.Authentication.JwtBearer`, `Microsoft.AspNetCore.Mvc.Testing` and `Microsoft.AspNetCore.TestHost` ship one band per .NET major and, unlike the `Microsoft.Extensions.*` packages, carry no `netstandard2.0` asset and do not roll forward across majors - a `net8.0` project cannot consume the `10.0.x` band. `Directory.Packages.props` therefore selects the version from `$(TargetFramework)`: `net8.0` gets `8.0.30`, `net9.0` gets `9.0.19`, and every other TFM (including `net10.0` and the `net10.0` shell that legacy `netstandard2.0`/`netstandard2.1` `$(CustomTestTarget)` builds fall back to) gets `10.0.11`.

Consumers that pin these packages themselves are unaffected. Consumers that inherit them transitively through `Opc.Ua.Bindings.Https` receive the band matching their own target framework.

## Newtonsoft.Json - what really changed

Neither `Opc.Ua.Core` nor `Opc.Ua.PubSub` directly references `Newtonsoft.Json`
in 2.0. Consumers that used it only through either package's old dependency
chain must add an explicit reference if their own code still uses its API.
Do not depend on an unrelated package to provide it transitively.

```xml
<PackageReference Include="Newtonsoft.Json" Version="13.0.4" />
```

Use `Version="13.0.4"` or any compatible later `13.x` release.

---

**See also**

- Related: [configuration.md](configuration.md), [encoders.md](encoders.md).
- [2.0 migration index](README.md) — analyzer quick-start + symptom → sub-doc table.
- [Migration Guide](../../MigrationGuide.md) — landing page across versions.
