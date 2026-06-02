---
name: opcua-v20-migration
description: |
  Migrate OPC UA .NET Standard applications from version 1.5.378 (master378) to
  version 2.0.x (master). Walks consumers through installing the
  OPCFoundation.NetStandard.Opc.Ua.MigrationAnalyzer NuGet (analyzers UA0001-UA0022,
  source-generated <Type>Collection shims, runtime compat shim), running
  `dotnet format analyzers` to apply auto-fixes, and walking the residual manual
  patterns. Use when asked to "migrate to v20", "update from master378", "fix
  v20 build errors", "migrate OPC UA code to 2.0", "update to new Variant API",
  "fix ArrayOf migration", "update NodeId readonly struct", "migrate from object
  to Variant", "fix CS0246 on <Type>Collection wrappers", "fix CS0246 on
  CertificateValidator", or "address UA00xx / MIG01 warnings". Sample triggers:
  user says "my project targets master378 and I need to update to v20"; user
  provides build errors after updating NuGet packages to 2.0; user asks "how do
  I update my custom NodeManager for v20?"; user says "fix all the CS0029
  errors after upgrading to v20".
license: MIT
compatibility: |
  Requires .NET SDK 10.0.300+, a C# project, and resolvable access to the
  OPCFoundation.NetStandard.Opc.Ua.MigrationAnalyzer NuGet package (v2.0.*-*).
  IDE auto-fixes need a Workspaces-aware host (Visual Studio, Rider, or
  `dotnet format`). Generator + analyzers load in csc.exe too.
metadata:
  author: OPC Foundation
  version: "1.0.0"
  upstream: https://github.com/OPCFoundation/UA-.NETStandard
  canonical-docs:
    - Docs/MigrationGuide.md
    - Tools/Opc.Ua.MigrationAnalyzer/NugetREADME.md
---

# OPC UA .NET Standard 1.5.378 → 2.0 Migration

Upgrade existing OPC UA .NET Standard consumer projects from 1.5.378
(`master378`) to 2.0.x (`master`). The skill assumes you already have a working
1.5.378 codebase; it does not teach OPC UA from scratch.

## Level 1: Quick Start (5 minutes)

### What you'll do

Install one NuGet, bump the OPC UA package versions, build once, apply the
analyzer auto-fixes, walk the handful of manual residuals, then remove the
migration NuGet. The package ships **three Roslyn components and a runtime
compat shim** that together cover most mechanical migration patterns
automatically.

### Core principles

- **Install before editing.** Get the migration NuGet into the project *before*
  you start fixing build errors. The source generator turns `CS0246` ("type
  `<Type>Collection` not found") into `[Obsolete]` warnings + `UA0002`
  diagnostics, and the runtime shim turns "method removed" errors into
  `[Obsolete]` warnings too. Edit a working build, not a broken one.
- **Let tooling do the mechanical work.** 14 of the 19 `UA00xx` rules have
  auto-fixes — apply them via the IDE quick-fix or `dotnet format analyzers`
  before opening a single file by hand.
- **Reserve human judgement for the 5 manual rules** — `UA0001` (telemetry
  plumbing), `UA0011` / `UA0015` (sync→async promotion), `UA0018` (cert load
  refactor), `UA0021` (`CertificateValidator` structural rewrite).
- **Remove the migration NuGet at the end.** It is a `PrivateAssets="all"`
  build-only dependency; once warning-free, drop the reference and you're on
  clean 2.0 with zero shim dependency.

### Quick reference

```xml
<!-- 1. In every csproj that references OPCFoundation.NetStandard.Opc.Ua.*, bump
        the OPC UA package version and add this one extra reference: -->
<ItemGroup>
  <PackageReference Include="OPCFoundation.NetStandard.Opc.Ua.MigrationAnalyzer"
                    Version="2.0.*-*"
                    PrivateAssets="all" />
</ItemGroup>
```

```bash
# 2. Restore + build. Code that was hard-broken on 1.5.378 → 2.0 now compiles
#    with [Obsolete] warnings + UA00xx + (rarely) MIG01 diagnostics.
dotnet restore
dotnet build

# 3. Apply all auto-fix rules in one pass:
dotnet format analyzers <YourSolution>.sln \
    --diagnostics UA0002 UA0003 UA0004 UA0005 UA0006 UA0007 UA0008 \
                  UA0009 UA0010 UA0012 UA0014 UA0019 UA0020 UA0022 \
    --severity warn

# 4. Walk the residual UA0001 / UA0011 / UA0015 / UA0018 / UA0021 by hand.
#    See references/migration-patterns.md for the categorical playbook.

# 5. Once the build is warning-free, drop the package reference. You're done.
```

### Essential checklist

- [ ] Every `<PackageReference Include="OPCFoundation.NetStandard.Opc.Ua.*">` bumped to `2.0.*-*`
- [ ] `OPCFoundation.NetStandard.Opc.Ua.MigrationAnalyzer` added as `PrivateAssets="all"` build-only dependency in every consumer project
- [ ] `dotnet build` succeeds (warnings allowed, errors fixed)
- [ ] `dotnet format analyzers --diagnostics UA0002 …` applied
- [ ] `UA0001`/`UA0011`/`UA0015`/`UA0018`/`UA0021` manual residuals resolved
- [ ] `[Obsolete]` (CS0612/CS0618) warnings fixed, **not** suppressed
- [ ] `MigrationAnalyzer` package reference removed before merging

### Common pitfalls

- **Do not suppress `[Obsolete]` or `UA00xx` warnings.** Obsolete API will be
  removed in the next minor 2.0 release; if you `<NoWarn>` it now, your build
  will break on upgrade.
- **Public APIs returning a generated `<Type>Collection` shim trip `CS0050`
  ("inconsistent accessibility").** The generator emits `internal` types by
  design. The fix is to migrate the public-API signature to `List<T>` /
  `ArrayOf<T>` first; internal call sites can keep the shim while you iterate.
- **Legacy `.NET Framework` `xmlns="http://schemas.microsoft.com/developer/msbuild/2003"`
  projects ignore `Directory.Build.targets` `<PackageReference>` injection.** Add
  the migration package directly into the legacy csproj's existing `<ItemGroup>`.
- **`TreatWarningsAsErrors=true` blocks the warning-driven workflow.** Use the
  `NoWarn` recipe in `assets/Directory.Build.targets.example.xml` for the
  migration window, then peel each entry back as you fix the rule.
- **The legacy `Quickstarts.Servers` meta-package does not exist on 2.0.** If
  your project depends on it, switch to a `<ProjectReference>` to
  `Applications/Quickstarts.Servers` or to an equivalent project of your own.

---

## Level 2: Implementation (30 minutes)

### What the migration package ships

The single `OPCFoundation.NetStandard.Opc.Ua.MigrationAnalyzer` NuGet contains
**three Roslyn components plus a runtime compat shim**:

| Component | Where | Loaded by | Purpose |
|---|---|---|---|
| `Opc.Ua.MigrationAnalyzer.dll` | `analyzers/dotnet/cs/` | csc.exe and IDE | 19 `DiagnosticAnalyzer`s (UA0001–UA0022). Targets stable Roslyn 4.14 API, no `Workspaces` reference, csc-safe. |
| `Opc.Ua.MigrationAnalyzer.CodeFixer.dll` | `analyzers/dotnet/cs/` | Workspaces-aware hosts only (Visual Studio, Rider, `dotnet format analyzers`) | 14 `CodeFixProvider`s. |
| `Opc.Ua.MigrationAnalyzer.Generator.dll` | `analyzers/dotnet/cs/` | csc.exe and IDE | `IIncrementalGenerator` that emits `internal sealed [Obsolete] class <Name>Collection : List<TElement>` shims into the consumer compilation for every `<Type>Collection` reference that fails to bind. |
| `Opc.Ua.MigrationAnalyzer.Core.dll` | `lib/<tfm>/` × 6 TFMs (`net472`, `net48`, `netstandard2.1`, `net8.0`, `net9.0`, `net10.0`) | Runtime | Re-supplies the obsolete extension surface 2.0 moved or removed so 1.5.378 call sites continue to compile with `[Obsolete]` warnings. |

### The 19 analyzer rules at a glance

The full table with default severity, replaces, auto-fix status, and
before/after examples lives in
[`references/analyzer-rules.md`](references/analyzer-rules.md). One-line summary
of where each lands in the workflow:

| Rule | Default | Auto-fix | One-liner |
|---|---|---|---|
| **UA0001** | Info | — | `Utils.Trace` → `ILogger` via `ITelemetryContext` (manual: pick log level + category) |
| **UA0002** | Warning | ✅ | `<Type>Collection` → `List<T>` / `ArrayOf<T>` |
| **UA0003** | Warning | ✅ | `x == null` on now-struct built-ins → `x.IsNull` |
| **UA0004** | Warning | ✅ | `x?.M()` on now-struct built-ins → drop the `?` |
| **UA0005** | Warning | ✅ | `byte[]` where `ByteString` expected → `.ToByteString()` |
| **UA0006** | Warning | ✅ | `new Variant(object\|DateTime\|Guid\|byte[])` → `Variant.From(...)` |
| **UA0007** | Warning | ✅ | `new NodeId(string)` / `new ExpandedNodeId(string)` → `Parse` |
| **UA0008** | Warning | ✅ | `Session.Call(..., params object[])` → wrap each arg with `Variant.From` |
| **UA0009** | Warning | ✅ | `[DataContract]`/`[DataMember]` → `[DataType]`/`[DataTypeField]` on config |
| **UA0010** | Warning | ✅ | `using`/`Dispose` on `CertificateIdentifier`/`UserIdentity`/`IUserIdentityTokenHandler` → drop disposable |
| **UA0011** | Info | — | Sync `IUserIdentityTokenHandler.{Encrypt,Decrypt,Sign,Verify}` → `*Async` (manual: async promotion) |
| **UA0012** | Warning | ✅ | `CertificateFactory.*` static → instance methods |
| **UA0014** | Warning | ✅ | `DataValue.IsGood(dv)` static → `dv.IsGood` property |
| **UA0015** | Info | — | Sync / APM members on GDS / LDS clients → `*Async` (manual: async promotion) |
| **UA0018** | Info | — | `CertificateIdentifier.Certificate` getter → `LoadCertificate2Async` (manual: async + caller reshape) |
| **UA0019** | Warning | ✅ | `new DataValue(StatusCode[, ts])` → object-initializer form |
| **UA0020** | Warning | ✅ | `EncodeableFactory.GlobalFactory` / `.Create()` → `ServiceMessageContext.Factory` / `.Fork()` |
| **UA0021** | Info | — | `CertificateValidator` / `CertificateValidationEventArgs` (structural rename — see manual playbook) |
| **UA0022** | Warning | ✅ | `ApplicationConfiguration.CertificateValidator` / `ServerBase.CertificateValidator` → `.CertificateManager` |

Plus one generator-only diagnostic:

| ID | Source | Default | Triggers |
|---|---|---|---|
| **MIG01** | `Opc.Ua.MigrationAnalyzer.Generator` | Warning | The generator can't uniquely resolve the element type for a `<Foo>Collection` reference (0 or > 1 candidates in the compilation). Add a `using` for the namespace defining `Foo`, or migrate the call site manually. See [`references/source-generator.md`](references/source-generator.md). |

### Source-generated `<Type>Collection` shims

When 2.0 deleted the `<Type>Collection` wrapper types, every consumer call site
like `new Int32Collection { 1, 2, 3 }` and `IList<NodeIdCollection> nodes`
became a hard `CS0246` ("type or namespace not found"). The package's source
generator (`MigrationGenerator`) closes this gap: for every short name ending in
`Collection` that doesn't bind, it emits an `internal sealed [Obsolete] class
<Name>Collection : List<TElement>` into the consumer's compilation.

- **Built-in catalog (30 entries)** pins element-type renames across the 2.0
  boundary: `DateTime`→`DateTimeUtc`, `Guid`→`Uuid`, `byte[]`→`ByteString`,
  `XmlElement`→`Opc.Ua.XmlElement`. The generator uses these *over* whatever
  the consumer's compilation resolves so the emitted shim bridges naturally to
  `ArrayOf<TElement>` etc.
- **Arbitrary `<UserType>Collection`** patterns (model-compiler output, vendor
  structures, etc.) are resolved by stripping the `Collection` suffix and
  looking up the resulting short name in the consumer's compilation via
  `Compilation.GetSymbolsWithName`. Exactly one match → emit; zero or many →
  `MIG01`.
- **Implicit conversion** to `ArrayOf<TElement>` on every generated type so
  2.0 APIs that took `ArrayOf<T>` keep accepting the shim instance.
- **`internal sealed`** — the shim never leaks through the consumer's public
  API surface (intentional; see "Common pitfalls" above for the CS0050
  consequence).

Deep-dive: [`references/source-generator.md`](references/source-generator.md).

### Runtime compatibility shim

`Opc.Ua.MigrationAnalyzer.Core.dll` re-exposes the 1.5.378 obsolete extension
surface (via C# 14 `extension` members) so 1.5.378-style call sites continue to
compile. Coverage and the sync-over-async caveat are documented in
[`references/runtime-shim.md`](references/runtime-shim.md).

### Manual residuals — priority order

For the 5 rules without auto-fixes and the patterns the analyzer doesn't catch
at all (e.g. `Variant.Value` setter type changes, `BaseVariableState.Value`
becoming `Variant`, `INodeManager` covariant return changes), apply fixes in
this order to minimize cascading errors:

1. **Source generation** project-file changes (remove pre-generated `.Classes.cs`, add `<AdditionalFiles>` for design files)
2. **Null comparisons** on now-struct types (`UA0003` / `UA0004` cover most; manual for unusual patterns)
3. **Collection types** (`UA0002` covers most; manual for `IList<T>` → `ArrayOf<T>` signature shape)
4. **Built-in type replacements** (`DateTime`→`DateTimeUtc`, `Guid`→`Uuid`, `byte[]`→`ByteString`)
5. **`Variant` / `DataValue` / `ExtensionObject`** API changes
6. **Encoder / Decoder** signature updates (generic methods + `ByteString`)
7. **NodeState** generic `PropertyState<T>` → builder pattern
8. **Server-side NodeManager** migration to `AsyncCustomNodeManager`
9. **Client-side `Session` / `Subscription`** changes
10. **User identity token handler** pattern (`AsTokenHandler()` + disposable handlers)

Full categorical playbook for each layer is in
[`references/migration-patterns.md`](references/migration-patterns.md).

### TreatWarningsAsErrors recipe

If your project sets `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` and
you can't relax it during the migration window, exclude the migration diagnostics
from the failure set in your `Directory.Build.targets`:

```xml
<PropertyGroup>
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  <NoWarn>$(NoWarn);CS0612;CS0618;MIG01;UA0001;UA0002;UA0003;UA0004;UA0005;UA0006;UA0007;UA0008;UA0009;UA0010;UA0011;UA0012;UA0014;UA0015;UA0018;UA0019;UA0020;UA0021;UA0022</NoWarn>
</PropertyGroup>
```

Pasteable file at [`assets/Directory.Build.targets.example.xml`](assets/Directory.Build.targets.example.xml).
Peel each entry back as you fix the rule; drop the whole block once the
MigrationAnalyzer package is removed.

### Known compatibility gaps

- Legacy `.NET Framework` WinForms projects in pre-SDK MSBuild XML format
  (`xmlns="…/2003"`) — `<PackageReference>` injection via
  `Directory.Build.targets` is silently ignored; the migration package must be
  added inline to each csproj.
- The `OPCFoundation.NetStandard.Opc.Ua.Quickstarts.Servers` meta-package is
  not published on 2.0 — consumers must switch to a `<ProjectReference>` to
  `Applications/Quickstarts.Servers` or an equivalent first-party project.
- Public APIs returning a `<Type>Collection` will hit `CS0050` because the
  generator's shim is `internal` by design — migrate the public surface to
  `List<T>` / `ArrayOf<T>` first.

Full list of dogfood-discovered gaps in
[`references/known-gaps.md`](references/known-gaps.md).

### Compatibility matrix

Target framework, .NET SDK, and Roslyn API requirements (and how to verify
analyzers actually loaded under csc.exe) are documented in
[`references/compatibility-matrix.md`](references/compatibility-matrix.md).

---

## Level 3: Mastery (Extended Learning)

The `references/` and `scripts/` folders in this skill contain the extended
material. Load them on demand via your agent runtime's
`read_skill_resource` / `run_skill_script` tools:

### References (load via `read_skill_resource`)

| File | Token budget | When to load |
|---|---|---|
| [`references/package-install.md`](references/package-install.md) | ~1.5K | When the user asks "how do I install" or hits PackageReference / `Directory.Build.targets` resolution issues |
| [`references/analyzer-rules.md`](references/analyzer-rules.md) | ~3K | When the user asks about a specific `UA00xx` warning or wants the full rule reference |
| [`references/source-generator.md`](references/source-generator.md) | ~2K | When `MIG01` fires or the user asks how the `<Type>Collection` shims work |
| [`references/runtime-shim.md`](references/runtime-shim.md) | ~2K | When a 1.5.378 extension call still compiles but is flagged `[Obsolete]`, or when async-promotion guidance is needed |
| [`references/migration-patterns.md`](references/migration-patterns.md) | ~5K | The categorical playbook for the 14 manual layers — primary fallback for residuals |
| [`references/known-gaps.md`](references/known-gaps.md) | ~1.5K | When `CS0050` (internal/public accessibility), legacy WinForms, or `Quickstarts.Servers` resolution surfaces |
| [`references/compatibility-matrix.md`](references/compatibility-matrix.md) | ~1K | When verifying the analyzer actually loaded under csc.exe vs IDE, or when picking a TFM |

### Scripts (invoke via `run_skill_script`)

| File | Purpose |
|---|---|
| [`scripts/apply-codefixes.ps1`](scripts/apply-codefixes.ps1) | PowerShell wrapper around `dotnet format analyzers --diagnostics UA0002 … --severity warn`. Auto-discovers `.sln`/`.slnx`, reports before/after warning counts. |

### Assets (pasteable templates)

| File | Purpose |
|---|---|
| [`assets/PackageReference.example.xml`](assets/PackageReference.example.xml) | Single `<PackageReference>` snippet for a consumer csproj. |
| [`assets/Directory.Build.targets.example.xml`](assets/Directory.Build.targets.example.xml) | Multi-project `<NoWarn>` recipe for `TreatWarningsAsErrors=true`. |

### Canonical upstream docs

- [`Docs/MigrationGuide.md`](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/MigrationGuide.md) — the human-facing migration guide that this skill distils.
- [`Tools/Opc.Ua.MigrationAnalyzer/NugetREADME.md`](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Tools/Opc.Ua.MigrationAnalyzer/NugetREADME.md) — the package's own README, shipped inside the NuGet.
