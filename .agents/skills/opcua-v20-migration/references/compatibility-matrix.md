# Compatibility matrix

What target frameworks, SDK versions, and Roslyn API surface the migration
package and the consumer's project need to match for everything to load
correctly.

## Required consumer-side versions

| Component | Minimum | Recommended | Notes |
|---|---|---|---|
| .NET SDK | 9.0.100 | latest 10.x | 9.0.100 ships Roslyn 4.14, which the package's `roslyn4.14` band targets. Earlier SDKs ship older Roslyn that has known incremental-generator bugs |
| `dotnet format` | bundled with SDK 10.0.300+ | latest 10.x | The `analyzers` subcommand is what applies UA0002…UA0022 fixes. Diagnostics alone need only SDK 9.0.100; the auto-fix pass needs 10.0.300+ |
| C# language version | 13 | 14 (default in SDK 10) | Required for `extension` keyword the runtime shim uses |
| Consumer project SDK | `Microsoft.NET.Sdk` (SDK-style) | same | Pre-SDK MSBuild XML projects (`xmlns="…/2003"`) cannot install the analyzer — see [`known-gaps.md` G1](known-gaps.md#g1--legacy-net-framework-winforms-projects-in-pre-sdk-msbuild-xml) |

## Supported consumer target frameworks

The migration package's runtime shim DLL (`Opc.Ua.MigrationAnalyzer.Core.dll`)
ships in 6 TFMs:

| TFM | Shipped? | OPC UA 2.0 main packages? |
|---|---|---|
| `net472` | ✅ | ✅ |
| `net48` | ✅ | ✅ |
| `netstandard2.1` | ✅ | ✅ |
| `net8.0` | ✅ | ✅ (LTS) |
| `net9.0` | ✅ | ✅ (STS) |
| `net10.0` | ✅ | ✅ (LTS, current) |

Consumers on other TFMs (`net6.0`, `net7.0`) can still install the package; the
analyzer + source generator still run, but the runtime shim DLL won't be
applied at compile-time — they fall back to migrating any shim-shaped patterns
manually. To upgrade the consumer's TFM as part of the migration, use the .NET
[**modernize**](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.modernize)
skill / agent from the dotnet tooling marketplace, which automates TFM bumps
and SDK-style csproj rewrites.

## Roslyn API targeting (internal)

Every Roslyn component ships twice, once per band, under
`analyzers/dotnet/<band>/cs/`. The .NET SDK picks the highest band its compiler
supports and ignores the rest, so a single package serves both hosts:

| Band | Built against | Loaded by |
|---|---|---|
| `roslyn4.14` | `Microsoft.CodeAnalysis.CSharp 4.14.0` | Visual Studio 2022 17.14+ / .NET 9 SDK |
| `roslyn5.0` | `Microsoft.CodeAnalysis.CSharp 5.0.0` | Visual Studio 2026 18.0+ / .NET 10 SDK |

Within a band, the three components differ only in what else they reference:

| DLL | Extra reference | Why |
|---|---|---|
| `Opc.Ua.MigrationAnalyzer.dll` | none | csc-safe (loads in `csc.exe`); Workspaces-free |
| `Opc.Ua.MigrationAnalyzer.Generator.dll` | none | csc-safe; needed for `IIncrementalGenerator` |
| `Opc.Ua.MigrationAnalyzer.CodeFixer.dll` | `Microsoft.CodeAnalysis.CSharp.Workspaces` | Loaded only in Workspaces-aware hosts (Visual Studio, `dotnet format`) |

> An analyzer built against a **newer** Roslyn than the host is skipped silently
> with warning `CS9057` — the consumer simply gets no diagnostics and no
> generated shims. That is why the package ships a band per supported host
> rather than a single `analyzers/dotnet/cs/` folder; see
> [`known-gaps.md` G9](known-gaps.md#g9--analyzer-silently-doesnt-load-under-cscexe-historical-fixed).

## Verifying analyzer + generator loaded under csc.exe

If you suspect the analyzer or generator isn't firing on a particular build
(e.g. `UA0002` doesn't appear despite `Int32Collection` references), run
the build with `/p:ReportAnalyzer=true`:

```bash
dotnet build YourProject.csproj /p:ReportAnalyzer=true
```

Output near the end should include:

```
Generator: Opc.Ua.MigrationAnalyzer.Generator
                Time (s)    %   Generator
                  <0.001  <1   Opc.Ua.MigrationAnalyzer.Generator.MigrationGenerator

Analyzer: Opc.Ua.MigrationAnalyzer
                Time (s)    %   Analyzer (Opc.Ua.MigrationAnalyzer)
                  0.012   23   UA0001UtilsTraceToILoggerAnalyzer
                  0.008   16   UA0002RemovedCollectionTypeAnalyzer
                  …
```

If neither line appears, either:

1. The package didn't resolve (check `obj/project.assets.json` for
   `OPCFoundation.NetStandard.Opc.Ua.MigrationAnalyzer`).
2. The analyzer was loaded but crashed at initialization. Pass
   `/p:ReportAnalyzer=true /v:detailed` and look for `warning CS8032:
   An instance of analyzer …MigrationAnalyzer cannot be created …`.

## IDE vs command-line behaviour

| Behaviour | `csc.exe` / `dotnet build` | Visual Studio / Rider / `dotnet format` |
|---|---|---|
| `Opc.Ua.MigrationAnalyzer.dll` (diagnostics) | ✅ Loads | ✅ Loads |
| `Opc.Ua.MigrationAnalyzer.Generator.dll` (source generator) | ✅ Loads | ✅ Loads |
| `Opc.Ua.MigrationAnalyzer.CodeFixer.dll` (code fixes) | — Not loaded | ✅ Loads + offers Quick Fixes |
| `dotnet format analyzers --diagnostics UA0002 …` | ✅ Applies fixes | ✅ Same |

This is why the CodeFixer DLL is split out: the `Workspaces` reference is only
safe in Workspaces-aware hosts; csc.exe gets the smaller analyzer DLL.

## What else changed across 1.5.378 → 2.0

| Component | 1.5.378 | 2.0.x |
|---|---|---|
| .NET SDK | 10.0.x | 10.0.x |
| Version stream | `1.5.378-preview` | `2.0-preview` → `2.0` |
| Target frameworks | `net8.0; net9.0; net10.0; net48` | `net8.0; net9.0; net10.0; net48` (unchanged) |
| NUnit | `4.4.0` | `4.5.1` |
| `coverlet.collector` | `6.0.4` | `8.0.0` |
| `DotNext` | — | `5.26.3` (new dependency) |
| `NUnit.Analyzers` | — | `4.12.0` (new analyzer) |
| Source Generators | — | now shipped via `Opc.Ua.SourceGeneration` (replaces ModelCompiler-generated C#) |

## NuGet feed configuration

Until the package promotes to nuget.org, it ships on the OPC Foundation preview
feed. Add to your `NuGet.config`:

```xml
<packageSources>
  <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  <add key="opcua-preview" value="https://opcfoundation.visualstudio.com/opcua-netstandard/_packaging/opcua-preview/nuget/v3/index.json" />
</packageSources>
```

Stable release goes to nuget.org and needs no extra configuration.
