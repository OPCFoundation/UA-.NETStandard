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
  <PackageReference Include="OPCFoundation.NetStandard.Opc.Ua.MigrationAnalyzer" Version="2.0.*-*" PrivateAssets="all" />
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

## G3 — `OPCFoundation.NetStandard.Opc.Ua.Quickstarts.Servers` meta-package not on 2.0

**Symptom:** `Reference Server.csproj` depends on the legacy
`OPCFoundation.NetStandard.Opc.Ua.Quickstarts.Servers` meta-package which is
not published on 2.0.

**Mitigation:** consumers must switch to a `<ProjectReference>` to
`samples/Quickstarts.Servers` in this repo, or to an equivalent
first-party project of their own. All 2.0 packages are published to the
OPC Foundation Azure Artifacts preview feed
(`https://opcfoundation.visualstudio.com/opcua-netstandard/_packaging/opcua-preview/nuget/v3/index.json`)
until they are promoted to nuget.org.

## G4 — `Samples/Opc.Ua.Sample` has > 1000 errors from `INodeManager` interface changes

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

## G5 — Public APIs returning `<Type>Collection` shim trip `CS0050`

The shim source generator emits `internal sealed` types by design (so
the shim never leaks across the consumer's public surface). Internal
call sites that consume the shim continue to compile incrementally
while the public API migrates to `List<T>` / `ArrayOf<T>`. No further
action required — `internal` accessibility is the intended design
and matches the long-term migration path.

## G6 — `GlobalDiscoverySampleServer` ctor inserted `ITelemetryContext` mid-arg-list

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

## G7 — Generator MIG01 on element types from unreferenced NuGets

**Symptom:**

```
MIG01: Cannot resolve element type 'Foo' for legacy wrapper 'FooCollection'.
```

…even though `Foo` is "obvious" to a human reader because it lived in a NuGet
that 1.5.378 referenced transitively (via `Quickstarts.Servers`) but 2.0 no
longer pulls in.

**Mitigation:** add the missing `<PackageReference>` (or `<ProjectReference>`)
explicitly. The generator runs in the consumer's compilation context and can
only see types that `dotnet restore` materialized.

## G8 — `TreatWarningsAsErrors=true` blocks the warning-driven migration

**Symptom:** every UA00xx warning becomes a build error; the consumer can't
even start applying fixes incrementally.

**Mitigation:** use the `NoWarn` recipe in
[`assets/Directory.Build.targets.example.xml`](../assets/Directory.Build.targets.example.xml)
for the migration window. Peel each ID back as you fix the rule. Drop the
whole block once the MigrationAnalyzer package is removed.

## G9 — Analyzer silently doesn't load under csc.exe (historical, fixed)

**Symptom (historical):** the analyzer DLL initially co-shipped its
code-fixers in one assembly, which transitively referenced
`Microsoft.CodeAnalysis.Workspaces.dll`. csc.exe's analyzer host ships only
`Microsoft.CodeAnalysis.dll` + `CSharp.dll` in its bincore and silently
swallowed the Workspaces load failure → zero diagnostics across all samples
even though `/analyzer:` was on the csc command line.

**Status:** fixed (commit `861fa6ee1`). Analyzer split into two DLLs; analyzer
DLL is Workspaces-free + targets stable Roslyn 4.14 API.

**Verification:** if you ever suspect the analyzer isn't firing, run with
`/p:ReportAnalyzer=true` and confirm `Opc.Ua.MigrationAnalyzer` and
`Opc.Ua.MigrationAnalyzer.Generator` appear in the per-analyzer-execution
report. See [`compatibility-matrix.md`](compatibility-matrix.md).

## G10 — `XmlElement` ambiguous between `Opc.Ua.XmlElement` and `System.Xml.XmlElement`

**Symptom:** `CS0104: 'XmlElement' is an ambiguous reference between
'Opc.Ua.XmlElement' and 'System.Xml.XmlElement'`.

**Mitigation:** remove `using System.Xml;` from the file. The OPC UA
`XmlElement` is what the consumer wants in 99% of OPC UA call sites; for the
rare case the user needs the BCL type, use:

```csharp
System.Xml.XmlElement sysXml = opcUaXmlElement.ToXmlElement();
```

## G11 — Auto-fix may produce verbose `Variant.From(...)` for hot paths

**Symptom:** UA0008's auto-fix wraps every `Session.Call` argument with
`Variant.From(...)`, which is correct but verbose for hot paths.

**Mitigation:** keep `Variant.From(...)` — direct casts to `Variant`
(`(Variant)arg`) are discouraged because they obscure the concrete
source type and may go through the boxed-object overload. For genuine
hot paths, construct the `Variant[]` once and reuse it across calls.

## G12 — Migration analyzer + central package management interaction

**Symptom:** consumer uses Central Package Management (`Directory.Packages.props`),
but the migration package's transitive `<PackageReference>` declarations get
overridden by older entries in the consumer's CPM file.

**Mitigation:** add `<PackageVersion>` entries for all OPC UA packages
(including `MigrationAnalyzer`) to `Directory.Packages.props` at the new 2.0
version. See [`package-install.md`](package-install.md#central-pinning-recommended-for-multi-project-solutions).

## G13 — Old Net4 projects that depend on `OPCFoundation.NetStandard.Opc.Ua.Configuration.Debug`

**Symptom:** older sample projects reference `.Debug` variants of the OPC UA
packages (e.g. `OPCFoundation.NetStandard.Opc.Ua.Configuration.Debug`,
`OPCFoundation.NetStandard.Opc.Ua.Server.Debug`).

**Cause:** 1.5.378 published `.Debug` variants of every package; 2.0 publishes
the Debug build under the same package id (configuration switches).

**Mitigation:** strip the `.Debug` suffix from the package id and use
`Configuration=Debug` in the consumer's build.

## G14 — Sample csprojs without `<Nullable>enable</Nullable>` see cascade of `CS8600`

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
