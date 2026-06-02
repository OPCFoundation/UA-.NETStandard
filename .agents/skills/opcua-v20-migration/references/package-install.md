# Installing the MigrationAnalyzer package

## Add the package reference

In **every** csproj that references an `OPCFoundation.NetStandard.Opc.Ua.*`
package, add this one line (alongside the existing `<PackageReference>` block,
not in place of it):

```xml
<ItemGroup>
  <PackageReference Include="OPCFoundation.NetStandard.Opc.Ua.MigrationAnalyzer"
                    Version="2.0.*-*"
                    PrivateAssets="all" />
</ItemGroup>
```

- `Version="2.0.*-*"` floats to the latest 2.0 preview, which is what you want
  during the migration window. Pin to a specific version (e.g.
  `"2.0.10-preview.gXXXXXXXX"`) once you have a stable target.
- `PrivateAssets="all"` ensures the package does not flow as a transitive
  dependency to downstream consumers of your library — it is a build-only
  helper, not a runtime dependency. After you finish the migration and remove
  the reference, downstream consumers see no change.

## Centralized variant (recommended for multi-project solutions)

Put it in `Directory.Build.targets` at the solution root so every csproj picks
it up without an edit:

```xml
<Project>
  <ItemGroup Condition="'$(SkipOpcUaMigrationAnalyzer)' != 'true'">
    <PackageReference Include="OPCFoundation.NetStandard.Opc.Ua.MigrationAnalyzer"
                      Version="2.0.*-*"
                      PrivateAssets="all" />
  </ItemGroup>
</Project>
```

The `Condition` lets individual projects opt out by setting
`<SkipOpcUaMigrationAnalyzer>true</SkipOpcUaMigrationAnalyzer>` in their
PropertyGroup — useful for the test or AOT-validation projects that you don't
want the analyzer running against.

> **Legacy `.NET Framework` caveat.** Projects that use the pre-SDK MSBuild XML
> format (`<Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">`)
> silently ignore `Directory.Build.targets` PackageReference injection. For
> those, add the `<PackageReference>` inline to the existing `<ItemGroup>` in
> the csproj.

## Bump the OPC UA package versions

In the same edit, change every existing OPC UA reference from `1.5.378.x` to
`2.0.*-*`:

```xml
<!-- Before -->
<PackageReference Include="OPCFoundation.NetStandard.Opc.Ua.Core" Version="1.5.378.145" />
<PackageReference Include="OPCFoundation.NetStandard.Opc.Ua.Server" Version="1.5.378.145" />

<!-- After -->
<PackageReference Include="OPCFoundation.NetStandard.Opc.Ua.Core" Version="2.0.*-*" />
<PackageReference Include="OPCFoundation.NetStandard.Opc.Ua.Server" Version="2.0.*-*" />
```

Common packages and their 2.0 paths:

| Package | Notes |
|---|---|
| `OPCFoundation.NetStandard.Opc.Ua.Core` | Replaces 1.5.378's `Opc.Ua.Core` + parts of `Opc.Ua.Configuration` |
| `OPCFoundation.NetStandard.Opc.Ua.Core.Types` | New in 2.0 — split from `Core`. Add this if you reference encoder/decoder types. |
| `OPCFoundation.NetStandard.Opc.Ua.Types` | New in 2.0 — intermediate types layer. |
| `OPCFoundation.NetStandard.Opc.Ua.Client` | Unchanged name. |
| `OPCFoundation.NetStandard.Opc.Ua.Configuration` | Unchanged name. |
| `OPCFoundation.NetStandard.Opc.Ua.Server` | Unchanged name. |
| `OPCFoundation.NetStandard.Opc.Ua.Bindings.Https` | Unchanged name. |
| `OPCFoundation.NetStandard.Opc.Ua.Gds.Common` | **New in 2.0** — intermediate project with shared GDS types. If you reference `Gds.Client.Common` or `Gds.Server.Common`, they now depend on it transitively (no action needed in most cases). |
| `OPCFoundation.NetStandard.Opc.Ua.Quickstarts.Servers` | **Not published on 2.0** — switch to a `<ProjectReference>` to `Applications/Quickstarts.Servers` or equivalent. |

If your solution already uses **Central Package Management** (`Directory.Packages.props`),
update there instead:

```xml
<ItemGroup>
  <PackageVersion Include="OPCFoundation.NetStandard.Opc.Ua.Core" Version="2.0.*-*" />
  <PackageVersion Include="OPCFoundation.NetStandard.Opc.Ua.MigrationAnalyzer" Version="2.0.*-*" />
  <!-- … -->
</ItemGroup>
```

## Restore and build

```bash
dotnet restore
dotnet build
```

If `dotnet build` reports:

- Only `[Obsolete]` (CS0612/CS0618), `UA00xx`, and `MIG01` **warnings** → success.
  Move on to applying the auto-fixes.
- `NU1102: Unable to find package … with version (>= 2.0.…)` → the requested
  package version isn't on a feed in your `NuGet.config`. Either bump the
  package versions to match what is published, or add the
  `https://opcfoundation.visualstudio.com/opcua-netstandard/_packaging/opcua-preview/nuget/v3/index.json`
  preview feed as a source.
- `CS0246: type or namespace not found` for a `<Type>Collection` name → the
  source generator either couldn't resolve the element type (look for an
  accompanying `MIG01`) or the analyzer didn't load (verify with
  `/p:ReportAnalyzer=true`; see `references/compatibility-matrix.md`).

## Apply the auto-fix batch

```bash
dotnet format analyzers <YourSolution>.sln \
    --diagnostics UA0002 UA0003 UA0004 UA0005 UA0006 UA0007 UA0008 \
                  UA0009 UA0010 UA0012 UA0014 UA0019 UA0020 UA0022 \
    --severity warn
```

Or run the helper script in `scripts/apply-codefixes.ps1` which auto-discovers
the solution file and reports before/after warning counts.

The 14 rules listed above are the **auto-fixable** subset of UA00xx. The 5
remaining (`UA0001`, `UA0011`, `UA0015`, `UA0018`, `UA0021`) are diagnostic-only
because they require human judgement — see `references/migration-patterns.md`
for the manual playbook.

## Remove the package once warning-free

When the build is `0 Warnings 0 Errors` on the migration diagnostics:

1. Delete the `<PackageReference Include="OPCFoundation.NetStandard.Opc.Ua.MigrationAnalyzer">`
   line (or the centralized one in `Directory.Build.targets` / `Directory.Packages.props`).
2. Re-run `dotnet build`. If new errors appear, you missed a residual — re-add
   the package, fix the diagnostic, and try again.
3. Commit the change. You are now on clean OPC UA 2.0 with **zero** shim
   dependency.

## Quick recap

```bash
# 1. Edit csprojs: bump OPC UA versions + add MigrationAnalyzer
# 2. Restore + build (warnings only)
dotnet restore && dotnet build
# 3. Apply auto-fixes
dotnet format analyzers MySolution.sln --diagnostics UA0002 UA0003 UA0004 UA0005 UA0006 UA0007 UA0008 UA0009 UA0010 UA0012 UA0014 UA0019 UA0020 UA0022 --severity warn
# 4. Walk UA0001/UA0011/UA0015/UA0018/UA0021 manually
# 5. Remove MigrationAnalyzer reference; rebuild; commit
```
