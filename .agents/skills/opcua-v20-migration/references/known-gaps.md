# Known gaps — patterns the migration package can't fully automate

Real-world dogfood findings from migrating
[`OPCFoundation/UA-.NETStandard-Samples`](https://github.com/OPCFoundation/UA-.NETStandard-Samples).
The patterns below either require manual action or are intentionally
not auto-fixed.

## G1 — Legacy `.NET Framework` WinForms projects in pre-SDK MSBuild XML

**Symptom:** the 5 WinForms `.Net4` sample projects use the pre-SDK XML
format:

```xml
<Project ToolsVersion="12.0" DefaultTargets="Build"
         xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
```

`Directory.Build.targets` `<PackageReference>` injection is **silently
ignored** by this format. The migration NuGet never resolves into the
compile.

**Cause:** the pre-SDK format predates `PackageReference` and only honours
the `packages.config` / `<Reference>` model.

**Mitigation:** add the `<PackageReference>` inline to each legacy csproj's
existing `<ItemGroup>`:

```xml
<ItemGroup>
  <Reference Include="…" />     <!-- existing -->
  <PackageReference Include="OPCFoundation.NetStandard.Opc.Ua.MigrationAnalyzer" Version="2.0.0-preview.*" PrivateAssets="all" />
</ItemGroup>
```

Long-term, migrate the project to the SDK-style format. The repo's reference
samples should not regress to the legacy format. The .NET
[**modernize**](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.modernize)
skill / agent (available on the dotnet tooling marketplace) automates the
pre-SDK → SDK-style conversion end-to-end.

## G2 — Resource tooling MSB3822 / MSB3823 under `dotnet build` for `.Net4` projects

**Symptom:** WinForms `.Net4` projects fail under `dotnet build` with:

```
MSB3823: Non-string resources require the property GenerateResourceUsePreserializedResources to be set to true.
MSB3822: Non-string resources require the System.Resources.Extensions assembly at runtime, but it was not found in this project's references.
```

**Cause:** `.resx` files containing non-string resources (icons, embedded
images) require `System.Resources.Extensions` auto-reference and the
preserialized-resources flag, which the legacy MSBuild flow doesn't add
automatically.

**Mitigation:** build these projects with `MSBuild.exe` (full Visual Studio
MSBuild), not `dotnet build`. Unrelated to the migration analyzer — would
happen on plain 1.5.378 too.

## G3 — `Samples/Opc.Ua.Sample` has > 1000 errors from `INodeManager` interface changes

**Symptom:** the legacy `Samples/Opc.Ua.Sample` consumer hit 146–1364 build
errors on initial 2.0 migration (depending on TFM): deep
`INodeManager`-interface changes (covariant return,
`IDataChangeMonitoredItem.QueueValue(in DataValue)`, `IList<T>` → `ArrayOf<T>`
signatures, `OnAfterCreate` / `Dispose` override signature shifts).

**Cause:** the 1.5.378 sample subclasses `CustomNodeManager2` deeply; 2.0's
`AsyncCustomNodeManager` base class changed many of the abstract method
signatures.

**Mitigation:** the analyzer covers the mechanical parts (UA0002 for
`DataValueCollection`, UA0021/UA0022 for `CertificateValidator`), but the
deep `INodeManager` interface changes require the structural migration to
`AsyncCustomNodeManager` documented in
[`migration-patterns.md` §8](migration-patterns.md#8-server-side-node-manager-changes).

## G4 — Public APIs can retain temporary `<Type>Collection` shims

The source generator emits `public sealed [Obsolete]` shim types so legacy
public signatures keep compiling during an incremental migration. Those types
exist only while the MigrationAnalyzer package is installed. Migrate every
public signature and call site to `List<T>` / `ArrayOf<T>` before removing the
package, or the remaining references become `CS0246` errors.

## G5 — `GlobalDiscoverySampleServer` ctor inserted `ITelemetryContext` mid-arg-list

**Symptom:** the 1.5.378 sample code

```csharp
var gds = new GlobalDiscoverySampleServer(
    database, request, certificateGroup, userDatabase, autoApprove: true);
```

fails to compile on 2.0 because the new 6-arg ctor takes
`ITelemetryContext telemetry` **before** the trailing `bool autoApprove`, so
`true` binds to `ITelemetryContext` (compile error).

**Mitigation:** the repo ships an in-tree `[Obsolete]` 5-arg back-compat ctor
matching the 1.5.378 signature (forwards to the modern ctor with `telemetry:
null!`). The shim covers downstream consumers using this 5-arg shape.

## G6 — Generator MIG01 on element types from dependency metadata

**Symptom:**

```
MIG01: Cannot resolve a unique element type 'Foo' for legacy wrapper 'FooCollection'.
```

…even though `Foo` is visible from a referenced project or NuGet.

**Cause:** source-declaration lookup covers the consumer project; metadata
lookup checks only the exact names `System.<Type>` and `Opc.Ua.<Type>`. Adding
a `PackageReference`, `ProjectReference`, or `using` does not extend that
lookup.

**Mitigation:** migrate the site manually to the intended fully qualified
`List<T>` / `ArrayOf<T>`, or define the legacy wrapper class explicitly in
consumer source so the unresolved reference binds and generator emission is
skipped.

## G7 — `TreatWarningsAsErrors=true` blocks the warning-driven migration

**Symptom:** every UA00xx warning becomes a build error; the consumer can't
even start applying fixes incrementally.

**Mitigation:** use the `NoWarn` recipe in
[`assets/Directory.Build.targets.example.xml`](../assets/Directory.Build.targets.example.xml)
for the migration window. Peel each ID back as you fix the rule. Drop the
whole block once the MigrationAnalyzer package is removed.

## G8 — Analyzer silently doesn't load under csc.exe (historical, fixed)

**Symptom (historical):** the analyzer DLL initially co-shipped its
code-fixers in one assembly, which transitively referenced
`Microsoft.CodeAnalysis.Workspaces.dll`. csc.exe's analyzer host ships only
`Microsoft.CodeAnalysis.dll` + `CSharp.dll` in its bincore and silently
swallowed the Workspaces load failure → zero diagnostics across all samples
even though `/analyzer:` was on the csc command line.

**Status:** fixed (commit `861fa6ee1`). Analyzer split into two DLLs; the analyzer
DLL is Workspaces-free. Each DLL now ships once per Roslyn band (`roslyn4.14`,
`roslyn5.0`) so the host always loads a build it can run — see
[`compatibility-matrix.md`](compatibility-matrix.md#roslyn-api-targeting-internal).

**Verification:** if you ever suspect the analyzer isn't firing, run with
`/p:ReportAnalyzer=true` and confirm `Opc.Ua.MigrationAnalyzer` and
`Opc.Ua.MigrationAnalyzer.Generator` appear in the per-analyzer-execution
report. See [`compatibility-matrix.md`](compatibility-matrix.md).

## G9 — `XmlElement` ambiguous between `Opc.Ua.XmlElement` and `System.Xml.XmlElement`

**Symptom:** `CS0104: 'XmlElement' is an ambiguous reference between
'Opc.Ua.XmlElement' and 'System.Xml.XmlElement'`.

**Mitigation:** remove `using System.Xml;` from the file. The OPC UA
`XmlElement` is what the consumer wants in 99% of OPC UA call sites; for the
rare case the user needs the BCL type, use:

```csharp
System.Xml.XmlElement sysXml = opcUaXmlElement.ToXmlElement();
```

## G10 — Auto-fix may produce verbose `Variant.From(...)` for hot paths

**Symptom:** UA0008's auto-fix wraps every `Session.Call` argument with
`Variant.From(...)`, which is correct but verbose for hot paths.

**Mitigation:** keep `Variant.From(...)` — direct casts to `Variant`
(`(Variant)arg`) are discouraged because they obscure the concrete
source type and may go through the boxed-object overload. For genuine
hot paths, construct the `Variant[]` once and reuse it across calls.

## G11 — Migration analyzer + central package management interaction

**Symptom:** consumer uses Central Package Management (`Directory.Packages.props`),
but the migration package's transitive `<PackageReference>` declarations get
overridden by older entries in the consumer's CPM file.

**Mitigation:** add `<PackageVersion>` entries for all OPC UA packages
(including `MigrationAnalyzer`) to `Directory.Packages.props` at the new 2.0
version. See
[`package-install.md`](package-install.md#centralized-variant-recommended-for-multi-project-solutions).

## G12 — Old Net4 projects that depend on `.Debug` package IDs

**Symptom:** older sample projects reference `.Debug` variants of the OPC UA
packages (e.g. `OPCFoundation.NetStandard.Opc.Ua.Configuration.Debug`,
`OPCFoundation.NetStandard.Opc.Ua.Server.Debug`).

**Status:** the 2.0 previews continue to publish the `.Debug` package IDs.
The stack projects append `.Debug` to `PackageId` for Debug builds, so
`OPCFoundation.NetStandard.Opc.Ua.Configuration.Debug` and
`OPCFoundation.NetStandard.Opc.Ua.Server.Debug` are valid 2.0 preview packages.

**Mitigation:** retain the `.Debug` suffix and upgrade that package to the same
2.0 preview version as the corresponding release package. Do not silently
replace it with the non-`.Debug` package unless changing the consumer's package
selection is intentional.

## G13 — Sample csprojs without `<Nullable>enable</Nullable>` see cascade of `CS8600`

**Symptom:** after migration, projects without explicit `<Nullable>enable</Nullable>`
see many `CS8600 Converting null literal or possible null value to non-nullable
type` warnings from 2.0's now-nullable signatures.

**Mitigation:** prefer `<Nullable>annotations</Nullable>` in the
consumer csproj — that opts in to the **annotations only** (consumers
see proper `T?` / non-nullable shapes from 2.0 signatures) **without
enabling the warnings**, so `CS8600` and friends stay silent during
the migration window. Once the consumer is ready for full nullable
analysis, flip to `<Nullable>enable</Nullable>` and fix the residuals.
As a last resort, use `<NoWarn>CS8600</NoWarn>` instead.
