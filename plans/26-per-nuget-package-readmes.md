# Plan — Per-NuGet package READMEs

## Status

**Proposed.** Surfaced from PR feedback on
[#3880](https://github.com/OPCFoundation/UA-.NETStandard/pull/3880)
(`Stack/Opc.Ua.Bindings.Kestrel.Tcp/Opc.Ua.Bindings.Kestrel.Tcp.csproj`
review comment: *"Maybe have a special readme for all nugets? And
remove the default?"*).

PR #3880 already took the first step: it removed the `common.props`
default that auto-packed the shared `Docs/NugetREADME.md` (which
described the *entire stack*, not the individual package, and was
therefore misleading on nuget.org). This plan tracks the follow-up
work — give every packable project its own package-local README.

## Problem

After PR #3880, packages without a `<PackageReadmeFile>` opt-in no
longer ship a README on nuget.org. That is intentional but leaves a
visible gap on the published packages:

- `OPCFoundation.NetStandard.Opc.Ua.Core`
- `OPCFoundation.NetStandard.Opc.Ua.Core.Types`
- `OPCFoundation.NetStandard.Opc.Ua.Types`
- `OPCFoundation.NetStandard.Opc.Ua.Security.Certificates`
- `OPCFoundation.NetStandard.Opc.Ua.Server`
- `OPCFoundation.NetStandard.Opc.Ua.Client`
- `OPCFoundation.NetStandard.Opc.Ua.Client.ComplexTypes`
- `OPCFoundation.NetStandard.Opc.Ua.Configuration`
- `OPCFoundation.NetStandard.Opc.Ua.PubSub`
- `OPCFoundation.NetStandard.Opc.Ua.Bindings.Https`
- `OPCFoundation.NetStandard.Opc.Ua.Gds.Common`
- `OPCFoundation.NetStandard.Opc.Ua.Gds.Client.Common`
- `OPCFoundation.NetStandard.Opc.Ua.Gds.Server.Common`
- `OPCFoundation.NetStandard.Opc.Ua.Lds.Server`
- `OPCFoundation.NetStandard.Opc.Ua.Di`
- `OPCFoundation.NetStandard.Opc.Ua.Di.Client`
- `OPCFoundation.NetStandard.Opc.Ua.Di.Server`
- `OPCFoundation.NetStandard.Opc.Ua.WotCon`
- `OPCFoundation.NetStandard.Opc.Ua.WotCon.Client`
- `OPCFoundation.NetStandard.Opc.Ua.WotCon.Server`
- `OPCFoundation.NetStandard.Opc.Ua.SourceGeneration`
- `OPCFoundation.NetStandard.Opc.Ua.SourceGeneration.Core`
- `OPCFoundation.NetStandard.Opc.Ua.SourceGeneration.Stack`
- `OPCFoundation.NetStandard.Opc.Ua.MigrationAnalyzer`
- `OPCFoundation.NetStandard.Opc.Ua.Mcp` (already has `McpREADME.md`)
- `OPCFoundation.NetStandard.Opc.Ua.Bindings.Kestrel.Tcp` (already has `KestrelTcpREADME.md`)
- `Quickstarts.Servers`

That's ~25 packages still without a package-specific README.

## Goals

For every packable project in the repo (excluding test / tool
projects that are not published):

1. Author a short package-local README (`NugetREADME.md` or a more
   specific filename like `CoreREADME.md`) that says:
   - what this *specific* package contains,
   - which TFMs it ships on,
   - a one-paragraph getting-started snippet pointing at the right
     `Docs/*.md` for deeper guidance,
   - a link to the main repository README.
2. Wire each one up via:
   ```xml
   <PropertyGroup>
     <PackageReadmeFile>NugetREADME.md</PackageReadmeFile>
   </PropertyGroup>
   <ItemGroup>
     <None Include="NugetREADME.md" Pack="true" PackagePath="\" />
   </ItemGroup>
   ```

## Non-goals

- Reintroducing a shared / generic README at the `common.props` level
  — that pattern was misleading on nuget.org and was deliberately
  removed in #3880.
- Substantial rewrites of `Docs/NugetREADME.md` (which today doubles
  as a docs landing page) — that file stays as repository
  documentation.

## Acceptance

- Every package listed above ships a unique README in the
  `.nupkg` (verify with `nuget locals` / `Expand-Archive -Path *.nupkg`).
- `dotnet pack UA.slnx` produces zero NU5xxx (`NU5039`, `NU5046`,
  `NU5047`) warnings related to missing README content.
- The package-specific READMEs render correctly on nuget.org
  (smoke-tested on a single representative package, e.g.
  `Opc.Ua.Core`, in CI).

## Rough sizing

~25 small READMEs (50-150 lines each), plus 25 csproj edits. A single
PR with one commit per package family
(`feat(packaging): add per-package READMEs to Opc.Ua.<group>`)
keeps the diff reviewable.

## Out of scope (file separately if needed)

- Updating `Docs/NugetREADME.md` itself (it remains the repository's
  documentation landing page).
- Restructuring `nuget/` packaging assets (logo, license).
