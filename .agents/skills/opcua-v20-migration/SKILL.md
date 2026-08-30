---
name: opcua-v20-migration
description: |
  Migrate OPC UA .NET Standard applications from version 1.5.378 to
  version 2.0.x. Walks consumers through installing the
  OPCFoundation.NetStandard.Opc.Ua.MigrationAnalyzer NuGet (25 analyzers through
  UA0028, UA0029 runtime-shim guidance, source-generated <Type>Collection shims,
  runtime compat shim), running
  `dotnet format analyzers` to apply auto-fixes, and walking the residual manual
  patterns. Use when asked to "migrate to v20", "update from 1.5.378", "fix
  v20 build errors", "migrate OPC UA code to 2.0", "update to new Variant API",
  "fix ArrayOf migration", "update NodeId readonly struct", "migrate from object
  to Variant", "fix CS0246 on <Type>Collection wrappers", "fix CS0246 on
  CertificateValidator", or "address UA00xx / MIG01 warnings". Sample triggers:
  user says "my project targets 1.5.378 and I need to update to v20"; user
  provides build errors after updating NuGet packages to 2.0; user asks "how do
  I update my custom NodeManager for v20?"; user says "fix all the CS0029
  errors after upgrading to v20".
license: MIT
compatibility: |
  Requires .NET SDK 9.0.300+ (10.0.300+ for the `dotnet format analyzers`
  auto-fix pass), a C# project, and resolvable access to the
  OPCFoundation.NetStandard.Opc.Ua.MigrationAnalyzer NuGet package
  (v2.0.0-preview.*).
  IDE auto-fixes need a Workspaces-aware host (Visual Studio, Rider, or
  `dotnet format`). Generator + analyzers load in csc.exe too.
metadata:
  author: OPC Foundation
  version: "1.0.0"
  upstream: https://github.com/OPCFoundation/UA-.NETStandard
  canonical-docs: >-
    https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/MigrationGuide.md;
    https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/migrate/2.0.x/README.md;
    https://github.com/OPCFoundation/UA-.NETStandard/tree/master/docs/migrate/2.0.x;
    https://github.com/OPCFoundation/UA-.NETStandard/blob/master/tools/Opc.Ua.MigrationAnalyzer/NugetREADME.md
---

# OPC UA .NET Standard 1.5.378 → 2.0 Migration

Upgrade existing OPC UA .NET Standard consumer projects from 1.5.378
(`master378`) to 2.0.x (`master`). The skill assumes you already have a working
1.5.378 codebase; it does not teach OPC UA from scratch.

## Migration sub-doc index — load only what you need

The plugin bundles a snapshot of every thematic migration sub-doc under
[`references/stack-migration/`](references/stack-migration/README.md). Use
those local resources for the migration workflow; they remain available
offline and do not change underneath an installed plugin. The upstream links
at the end are optional references for checking newer repository changes.

**Context-efficiency rule.** The full migration content is no longer in
a single document; it is split across thematic sub-docs in
[`references/stack-migration/`](references/stack-migration/README.md).
Match the user's symptom to a row below and load the **single** sub-doc named
in that row. For `UA0024`–`UA0026` or `UA0028`, load only the matching section
of [`references/analyzer-rules.md`](references/analyzer-rules.md); it contains
the cross-cutting exposed-lock guidance without loading the entire Migration
Guide.

| When the user hits… | Load only |
| --- | --- |
| `CS0029` / `CS1503` / `CS0266` on `NodeId`, `Variant`, `DataValue`, `ExtensionObject`, `QualifiedName`, `LocalizedText`, `ArrayOf<T>` / `MatrixOf<T>`, `ByteString`, `StatusCode`, `XmlElement`, `EnumValue`, or `[Obsolete]` on built-in type APIs (analyzers `UA0002`–`UA0008`, `UA0014`, `UA0019`) | [`types.md`](references/stack-migration/types.md) |
| `Utils.LogX`, `Utils.Trace`, static logger helpers (`Utils.SetLogger` / `Utils.SetLogLevel` removed), `ITelemetryContext` constructor parameter shape per type, OLD-vs-NEW logger snippets, fluent `AddOpcUa().AddLogging().AddMetrics()` registration, breaking-changes inventory across Core / Configuration / Client / Server / PubSub / Certificate / Transport, migration utilities (`DefaultTelemetry`, `Telemetry.NullLogger`, `Utils.Fallback.Logger`) | [`telemetry.md`](references/stack-migration/telemetry.md) |
| Package upgrades, TFM changes, `Newtonsoft.Json` removal from `Opc.Ua.Core`, new published packages | [`packages.md`](references/stack-migration/packages.md) |
| Source-generated `*Collection` shims, NodeManager generator, default of `bool` properties, project structure | [`source-generation.md`](references/stack-migration/source-generation.md) |
| `IEncodeableFactoryBuilder`, `IType`, JSON / XML / binary encoders, `EncodeableFactory.GlobalFactory`, `IJsonEncodeable`, `ComplexTypes` namespace move | [`encoders.md`](references/stack-migration/encoders.md) |
| Custom NodeManagers, `NodeState` clone / read / write helpers, `Clone` → `CreateCopy`, `OnAfterCreate(CancellationToken)`, `FindChild` / `CreateChild` NodeId assignment, `INodeManager3`, `INodeCache.InvalidateNode`, generics on `BaseVariableState` / `BaseVariableTypeState`, predefined-node processing, `lock (node)` on a `NodeState`, `NodeBrowser.DataLock` (`UA0027`) | [`node-states.md`](references/stack-migration/node-states.md) |
| `IUserIdentityTokenHandler`, `IClientIdentityProvider`, `IUserTokenAuthenticator`, `IAccessTokenProvider`, `ITokenIssuer`, `IIdentityClaims`, caller-supplied secrets, secret store | [`identity.md`](references/stack-migration/identity.md) |
| `CertificateValidator` rename (`UA0021`), ref-counted `Certificate` wrapper, `CertificateManager`, `ICertificateProvider`, obsoleted `X509Certificate2` direct-exposure APIs, PushManagement transactions (`ApplyChanges`-gated TrustList updates) | [`certificates.md`](references/stack-migration/certificates.md) |
| `ApplicationConfiguration` changes, Data-Contract serializer removal, `MinMetadataSamplingInterval` → `MinSupportedSamplingInterval`, `ParseExtension` / `UpdateExtension` signature, session / browser state persistence | [`configuration.md`](references/stack-migration/configuration.md) |
| `Session` → `ManagedSession`, V2 subscription engine, GDS-client `Task` → `ValueTask` modernisation, removed obsolete GDS APIs, durable subscriptions, removed `ReverseConnectClientCollection`, `IMessageSocket`, or `TransportBindings` APIs | [`sessions-subscriptions.md`](references/stack-migration/sessions-subscriptions.md) |
| `UaPubSubApplication.Create*`, `IUaPubSubConnection`, `UaPubSubConfigurator`, `IUaPublisher`, AMQP transport, `JsonEncodingMode.Reversible` / `NonReversible`, PubSub JSON encoder changes, `DataSetFieldContentMask` RawData / timestamp behaviour | [`pubsub.md`](references/stack-migration/pubsub.md) |
| `AlarmConditionState` state-transition behaviour, auto-emitted `GeneralModelChangeEvent`, `ModelChangeAggregator`, `INodeCache.InvalidateNode` triggered by model change | [`alarms-model-change.md`](references/stack-migration/alarms-model-change.md) |
| `DateTime.UtcNow`, `Timer`, deterministic-time tests, `System.TimeProvider` adoption | [`timeprovider.md`](references/stack-migration/timeprovider.md) |
| `ITransportListener.Open` / `Close` removed, `ReverseConnectManager.StartService` / `Dispose` obsolete, reverse-connect DI/provider migration, custom `ITransportListenerFactory` / `ITransportListenerCertificateRotation` implementers need the new async method names | [`transport-listener-async.md`](references/stack-migration/transport-listener-async.md) |

If the user's symptom does not obviously map to one row, read
[`references/stack-migration/README.md`](references/stack-migration/README.md) (small —
the same table plus a short intro) and pick from there. Avoid loading
multiple sub-docs unless the symptom genuinely spans two areas (for
example, `node-states.md` *and* `types.md` when a NodeManager runs into
both `INodeManager3` adoption and `Variant`-for-`object` API changes).

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
- **Let tooling do the mechanical work.** 14 of the 25 `UA00xx` rules have
  auto-fixes — apply them via the IDE quick-fix or `dotnet format analyzers`
  before opening a single file by hand.
- **Reserve human judgement for the 11 manual rules** — `UA0001` (telemetry
  plumbing), `UA0011` / `UA0015` (sync→async promotion), `UA0018` (cert load
  refactor), `UA0021` (`CertificateValidator` structural rewrite), and
  `UA0023`–`UA0028` (PubSub and removed exposed-lock APIs).
- **Treat `UA0029`-tagged obsolete warnings as manual work too.** The runtime
  shim marks moved `SecurityPolicies` statics, but no analyzer currently emits
  `UA0029`; migrate the `CS0618` sites to `ISecurityPolicyRegistry`.
- **Remove the migration NuGet at the end.** It is a `PrivateAssets="all"`
  build-only dependency; once warning-free, drop the reference and you're on
  clean 2.0 with zero shim dependency.

### Quick reference

```xml
<!-- 1. In every csproj that references OPCFoundation.NetStandard.Opc.Ua.*, bump
        the OPC UA package version and add this one extra reference: -->
<ItemGroup>
  <PackageReference Include="OPCFoundation.NetStandard.Opc.Ua.MigrationAnalyzer"
                    Version="2.0.0-preview.*"
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

# 4. Walk UA0001 / UA0011 / UA0015 / UA0018 / UA0021 and UA0023-UA0028 by hand,
#    plus CS0618 SecurityPolicies calls whose message references UA0029.
#    See references/migration-patterns.md for the categorical playbook.

# 5. Once the build is warning-free, drop the package reference. You're done.
```

### Essential checklist

- [ ] Every `<PackageReference Include="OPCFoundation.NetStandard.Opc.Ua.*">` bumped to `2.0.0-preview.*`
- [ ] `OPCFoundation.NetStandard.Opc.Ua.MigrationAnalyzer` added as `PrivateAssets="all"` build-only dependency in every consumer project
- [ ] `dotnet build` succeeds (warnings allowed, errors fixed)
- [ ] `dotnet format analyzers --diagnostics UA0002 …` applied
- [ ] `UA0001`/`UA0011`/`UA0015`/`UA0018`/`UA0021` and `UA0023`–`UA0028` manual residuals resolved
- [ ] `SecurityPolicies` obsolete warnings tagged `UA0029` migrated manually
- [ ] `[Obsolete]` (CS0612/CS0618) warnings fixed, **not** suppressed
- [ ] `MigrationAnalyzer` package reference removed before merging

### Common pitfalls

- **Do not suppress `[Obsolete]` or `UA00xx` warnings.** Obsolete API will be
  removed in the next minor 2.0 release; if you `<NoWarn>` it now, your build
  will break on upgrade.
- **Generated `<Type>Collection` shims are temporary public types.** They keep
  legacy public signatures compiling during migration, but disappear when the
  analyzer package is removed. Migrate every public signature and call site to
  `List<T>` / `ArrayOf<T>` before removing the package.
- **Legacy `.NET Framework` `xmlns="http://schemas.microsoft.com/developer/msbuild/2003"`
  projects ignore `Directory.Build.targets` `<PackageReference>` injection.** Add
  the migration package directly into the legacy csproj's existing `<ItemGroup>`.
- **`TreatWarningsAsErrors=true` blocks the warning-driven workflow.** Use the
  `NoWarn` recipe in `assets/Directory.Build.targets.example.xml` for the
  migration window, then peel each entry back as you fix the rule.
---

## Level 2: Implementation (30 minutes)

### What the migration package ships

The single `OPCFoundation.NetStandard.Opc.Ua.MigrationAnalyzer` NuGet contains
**three Roslyn components plus a runtime compat shim**:

| Component | Where | Loaded by | Purpose |
|---|---|---|---|
| `Opc.Ua.MigrationAnalyzer.dll` | `analyzers/dotnet/roslyn4.14/cs/` and `roslyn5.0/cs/` | csc.exe and IDE | 25 `DiagnosticAnalyzer`s through UA0028 (excluding UA0013, UA0016, and UA0017). No `Workspaces` reference, csc-safe. |
| `Opc.Ua.MigrationAnalyzer.CodeFixer.dll` | `analyzers/dotnet/roslyn4.14/cs/` and `roslyn5.0/cs/` | Workspaces-aware hosts only (Visual Studio, Rider, `dotnet format analyzers`) | 14 `CodeFixProvider`s. |
| `Opc.Ua.MigrationAnalyzer.Generator.dll` | `analyzers/dotnet/roslyn4.14/cs/` and `roslyn5.0/cs/` | csc.exe and IDE | `IIncrementalGenerator` that emits `public sealed [Obsolete] class <Name>Collection : List<TElement>` shims into the consumer compilation for every `<Type>Collection` reference that fails to bind. |
| `Opc.Ua.MigrationAnalyzer.Core.dll` | `lib/<tfm>/` × 6 TFMs (`net472`, `net48`, `netstandard2.1`, `net8.0`, `net9.0`, `net10.0`) | Runtime | Re-supplies the obsolete extension surface 2.0 moved or removed so 1.5.378 call sites continue to compile with `[Obsolete]` warnings. |

### The 25 analyzer rules at a glance

The full table with default severity, replaces, auto-fix status, and
before/after examples lives in
[`references/analyzer-rules.md`](references/analyzer-rules.md). One-line summary
of where each lands in the workflow:

| Rule | Default | Auto-fix | One-liner |
|---|---|---|---|
| **UA0001** | Info | — | `Utils.Trace` → `ILogger` via `ITelemetryContext` (manual: pick log level + category) |
| **UA0002** | Warning | ✅ | `<Type>Collection` → `List<T>`; manually use `ArrayOf<T>` at applicable API boundaries |
| **UA0003** | Warning | ✅ | `x == null` on now-struct built-ins → `x.IsNull` |
| **UA0004** | Warning | ✅ | `x?.M()` on now-struct built-ins → drop the `?` |
| **UA0005** | Warning | ✅ | `byte[]` where `ByteString` expected → `.ToByteString()` |
| **UA0006** | Warning | ✅ | `new Variant(object\|DateTime\|Guid\|byte[])` → `Variant.From(...)` |
| **UA0007** | Warning | ✅ | `new NodeId(string)` / `new ExpandedNodeId(string)` → `Parse` |
| **UA0008** | Warning | ✅ | `Session.Call(..., params object[])` → wrap each arg with `Variant.From` |
| **UA0009** | Warning | ✅ | `[DataContract]`/`[DataMember]` → `[DataType]`/`[DataTypeField]` on config |
| **UA0010** | Warning | ✅ | `using`/`Dispose` on `CertificateIdentifier`/`UserIdentity`/`IUserIdentityTokenHandler` → drop disposable |
| **UA0011** | Info | — | Sync `IUserIdentityTokenHandler.{Encrypt,Decrypt,Sign,Verify}` → `*Async` (manual: async promotion) |
| **UA0012** | Warning | ✅ | `CertificateFactory.*` static → `DefaultCertificateFactory.Instance.*` |
| **UA0014** | Warning | ✅ | `DataValue.IsGood(dv)` static → `dv.IsGood` property |
| **UA0015** | Info | — | Sync / APM members on GDS / LDS clients → `*Async` (manual: async promotion) |
| **UA0018** | Info | — | `CertificateIdentifier.Certificate` getter → `CertificateIdentifierResolver.ResolveAsync` |
| **UA0019** | Warning | ✅ | `new DataValue(StatusCode[, ts])` → `DataValue.FromStatusCode(...)` |
| **UA0020** | Warning | ✅ | `EncodeableFactory.GlobalFactory` / `.Create()` → `ServiceMessageContext.Factory` / `.Fork()` |
| **UA0021** | Info | — | `CertificateValidator` / `CertificateValidationEventArgs` (structural rename — see manual playbook) |
| **UA0022** | Warning | ✅ | `ApplicationConfiguration.CertificateValidator` / `ServerBase.CertificateValidator` → `.CertificateManager` |
| **UA0023** | Warning | — | Legacy PubSub top-level types → `IPubSubApplication` / builder and DI APIs |
| **UA0024** | Warning | — | Exposed diagnostics locks → owner-side update/read methods |
| **UA0025** | Warning | — | `ILocalNode.DataLock` / `Node.DataLock` → node-owned synchronization |
| **UA0026** | Warning | — | `BaseVariableValue.Lock` → caller-owned `System.Threading.Lock` |
| **UA0027** | Warning | — | `NodeBrowser.DataLock` → single-consumer browser without external locking |
| **UA0028** | Warning | — | `ApplicationConfiguration.PropertiesLock` → concurrent properties APIs |

Runtime-shim-only migration marker:

| Marker | Signal | Auto-fix | One-liner |
|---|---|---|---|
| **UA0029** | `CS0618` from shim | — | `SecurityPolicies` lookup/crypto statics → `ISecurityPolicyRegistry` or `SecurityPolicies.Default`; no analyzer currently reports `UA0029` |

Plus one generator-only diagnostic:

| ID | Source | Default | Triggers |
|---|---|---|---|
| **MIG01** | `Opc.Ua.MigrationAnalyzer.Generator` | Warning | The generator can't uniquely resolve the element type for a `<Foo>Collection` reference. It discovers consumer source declarations plus exact `System.<Type>` / `Opc.Ua.<Type>` metadata names; other zero/ambiguous cases require a manual `List<T>` / `ArrayOf<T>` migration or an explicitly defined wrapper. See [`references/source-generator.md`](references/source-generator.md). |

### Source-generated `<Type>Collection` shims

When 2.0 deleted the `<Type>Collection` wrapper types, every consumer call site
like `new Int32Collection { 1, 2, 3 }` and `IList<NodeIdCollection> nodes`
became a hard `CS0246` ("type or namespace not found"). The package's source
generator (`MigrationGenerator`) closes this gap: for every short name ending in
`Collection` that doesn't bind, it emits a `public sealed [Obsolete] class
<Name>Collection : List<TElement>` into the consumer's compilation.

- **Built-in catalog (rename overrides)** pins element types that **renamed**
  across the 1.5.378 → 2.0 boundary, where semantic lookup would resolve to
  the wrong type or fail with ambiguity: `DateTime→DateTimeUtc`, `Guid→Uuid`,
  `byte[]→ByteString`, `XmlElement→System.Xml.XmlElement` (the latter
  disambiguates against the new `Opc.Ua.XmlElement`). The generator uses these
  *over* whatever the consumer's compilation resolves.
- **Consumer-source `<UserType>Collection`** patterns (model-compiler output,
  application-defined structures, etc.) are resolved by stripping the
  `Collection` suffix and looking up the short name across source declarations
  via `Compilation.GetSymbolsWithName`. Standard metadata then falls back to
  exact `System.<Type>` and `Opc.Ua.<Type>` names. Other metadata, zero
  matches, or multiple source matches produce `MIG01`.
- **Implicit conversion** to `ArrayOf<TElement>` on every generated type so
  2.0 APIs that took `ArrayOf<T>` keep accepting the shim instance.
- **`public sealed`** — legacy public signatures continue compiling while the
  package is installed. The type remains `[Obsolete]` and must not become a
  permanent public dependency.

Deep-dive: [`references/source-generator.md`](references/source-generator.md).

### Runtime compatibility shim

`Opc.Ua.MigrationAnalyzer.Core.dll` re-exposes the 1.5.378 obsolete extension
surface (via C# 14 `extension` members) so 1.5.378-style call sites continue to
compile. Coverage and the sync-over-async caveat are documented in
[`references/runtime-shim.md`](references/runtime-shim.md).

### Manual residuals — priority order

For the 11 rules without auto-fixes and the patterns the analyzer doesn't catch
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
  <NoWarn>$(NoWarn);CS0612;CS0618;MIG01;UA0001;UA0002;UA0003;UA0004;UA0005;UA0006;UA0007;UA0008;UA0009;UA0010;UA0011;UA0012;UA0014;UA0015;UA0018;UA0019;UA0020;UA0021;UA0022;UA0023;UA0024;UA0025;UA0026;UA0027;UA0028</NoWarn>
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
- Public APIs can continue returning a generated `<Type>Collection` while the
  package is installed, but those temporary types disappear with the package.
  Migrate the public surface to `List<T>` / `ArrayOf<T>` before removal.

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
| [`references/known-gaps.md`](references/known-gaps.md) | ~1.5K | When legacy WinForms, generated-shim lifetime, or analyzer-loading issues surface |
| [`references/compatibility-matrix.md`](references/compatibility-matrix.md) | ~1K | When verifying the analyzer actually loaded under csc.exe vs IDE, or when picking a TFM |
| [`references/stack-migration/README.md`](references/stack-migration/README.md) | index | Bundled offline snapshot of all 15 thematic migration docs; load only the symptom-matched file |

### Scripts (invoke via `run_skill_script`)

| File | Purpose |
|---|---|
| [`scripts/apply-codefixes.ps1`](scripts/apply-codefixes.ps1) | PowerShell wrapper around `dotnet format analyzers --diagnostics UA0002 … --severity warn`. Auto-discovers `.sln`/`.slnx`, reports before/after warning counts. |

### Assets (pasteable templates)

| File | Purpose |
|---|---|
| [`assets/PackageReference.example.xml`](assets/PackageReference.example.xml) | Single `<PackageReference>` snippet for a consumer csproj. |
| [`assets/Directory.Build.targets.example.xml`](assets/Directory.Build.targets.example.xml) | Multi-project `<NoWarn>` recipe for `TreatWarningsAsErrors=true`. |

### Optional current upstream docs

- [`docs/MigrationGuide.md`](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/MigrationGuide.md) — current human-facing landing page and cross-cutting notes.
- [`docs/migrate/2.0.x/README.md`](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/migrate/2.0.x/README.md) — current upstream migration index.
- [`docs/migrate/2.0.x/`](https://github.com/OPCFoundation/UA-.NETStandard/tree/master/docs/migrate/2.0.x) — current versions of the thematic docs bundled under `references/stack-migration/`.
- [`tools/Opc.Ua.MigrationAnalyzer/NugetREADME.md`](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/tools/Opc.Ua.MigrationAnalyzer/NugetREADME.md) — the package's own README, shipped inside the NuGet.
