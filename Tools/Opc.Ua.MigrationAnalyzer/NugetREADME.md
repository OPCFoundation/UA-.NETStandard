# OPC UA migration analyzers, code fixers, source generator, and compatibility shim

## What you get

A single NuGet install (`OPCFoundation.NetStandard.Opc.Ua.MigrationAnalyzer`) that
ships **three Roslyn components + a runtime shim** to help migrate from OPC UA
.NET Standard 1.5.378 to 2.0:

- a Roslyn **analyzer + code-fixer** set (`UA0001`–`UA0022`) that flags every
  pattern covered by [`Docs/MigrationGuide.md`](../../Docs/MigrationGuide.md)
  and, where safe, applies the fix automatically;
- a Roslyn **source generator** (`Opc.Ua.MigrationAnalyzer.Generator.dll`) that
  emits per-consumer `internal sealed [Obsolete] class <Name>Collection : List<TElement>`
  shims for every `<Type>Collection` wrapper the consumer references but that
  2.0 removed — **including** model-compiled `<UserType>Collection` patterns,
  not just the 30 well-known built-in ones. Element types renamed across the
  1.5.378 → 2.0 boundary (e.g. `DateTime`→`DateTimeUtc`, `Guid`→`Uuid`,
  `byte[]`→`ByteString`, `XmlElement`→`Opc.Ua.XmlElement`) are pinned through
  a 30-entry override table; everything else falls back to semantic lookup in
  the consumer's compilation; and
- a **compatibility shim** assembly (`Opc.Ua.MigrationAnalyzer.Core.dll`) that
  re-supplies the obsolete extension surface 2.0 moved or removed, so most
  consumer projects still compile after the upgrade.

> ℹ **The generator emits `internal` types by design** — they never leak through
> the consumer's public API surface. If your consumer has *public* methods or
> properties that return / accept a `<Type>Collection`, you'll hit `CS0050:
> Inconsistent accessibility`. That's the intended signal that your **public
> API** has to migrate to `List<T>` / `ArrayOf<T>` first; internal call sites
> keep compiling under the shim so you can iterate at your own pace.

## How to migrate

1. Add the 2.0 OPC UA packages **and** the MigrationAnalyzer package to your
   consumer project:

   ```xml
   <PackageReference Include="OPCFoundation.NetStandard.Opc.Ua.MigrationAnalyzer" Version="x.y.z" PrivateAssets="all" />
   ```

2. Run `dotnet build`. Your code should compile: the shim covers the
   `[Obsolete]` extension surface that 2.0 moved or removed; the source
   generator covers `<Type>Collection` wrappers; what remains are warnings
   rather than errors.
3. Walk through the `UA00xx` analyzer warnings in the IDE and apply the
   offered auto-fixes. A handful (`UA0001`, `UA0011`, `UA0015`, `UA0018`,
   `UA0021`) are `Info`-level and need a manual review. A single generator
   diagnostic (`MIG01`) fires when the generator can't resolve a model-compiled
   element type — add the appropriate `using` or migrate the site manually.
4. Once the project is warning-free, remove the
   `OPCFoundation.NetStandard.Opc.Ua.MigrationAnalyzer` package reference. You are
   on clean 2.0 with no shim dependency.

## Rules

| ID     | Default  | Replaces                                                                                |
| ------ | -------- | ----------------------------------------------------------------------------------------|
| UA0001 | Info     | `Utils.Trace` / `Utils.LogX`                                                            |
| UA0002 | Warning  | Removed `<Type>Collection` wrappers                                                     |
| UA0003 | Warning  | `x == null` on now-struct built-in types                                                |
| UA0004 | Warning  | `?.` on now-struct built-in types                                                       |
| UA0005 | Warning  | `byte[]` where `ByteString` is now expected                                             |
| UA0006 | Warning  | `new Variant(object\|DateTime\|Guid\|byte[])`                                           |
| UA0007 | Warning  | `new NodeId(string)` / `new ExpandedNodeId(string)`                                     |
| UA0008 | Warning  | `Session.Call(..., params object[])` argument wrapping                                  |
| UA0009 | Warning  | `[DataContract]`/`[DataMember]` on configuration extensions                             |
| UA0010 | Warning  | `using`/`Dispose` on `CertificateIdentifier`, `UserIdentity`, `IUserIdentityTokenHandler` |
| UA0011 | Info     | Sync `IUserIdentityTokenHandler.Encrypt/Decrypt/Sign/Verify`                            |
| UA0012 | Warning  | `CertificateFactory.*` static helpers                                                   |
| UA0014 | Warning  | `DataValue.IsGood(dv)` static helper                                                    |
| UA0015 | Info     | Sync / APM members on GDS / LDS clients                                                 |
| UA0018 | Info     | `CertificateIdentifier.Certificate` getter                                              |
| UA0019 | Warning  | `new DataValue(StatusCode[, ts])`                                                       |
| UA0020 | Warning  | `EncodeableFactory.GlobalFactory` / `Create()`                                          |
| UA0021 | Info     | `CertificateValidator` / `CertificateValidationEventArgs` (structural rename in 1.6)    |
| UA0022 | Warning  | `ApplicationConfiguration.CertificateValidator` / `ServerBase.CertificateValidator` (renamed in 2.0 to `.CertificateManager`) |

## What the shim provides

`Opc.Ua.MigrationAnalyzer.Core.dll` is delivered as a regular reference assembly and
re-exposes the 1.5.378 surface in two flavors:

- **Moved obsolete extensions** the 1.6 libraries no longer carry inline:
  `NodeId` / `Variant` / `DataValue` null-check helpers, `Session` sync
  helpers, `Subscription` sync helpers, `ApplicationInstance` helpers,
  `ServerBase.Start` / `Stop`, `TransportChannel` APM (`BeginX` / `EndX`),
  `ChannelBase` static factory methods, and similar surface.
- **New shims for genuinely-removed members**:
  - `EncodeableFactory.GlobalFactory`
  - `CertificateIdentifier.Certificate` (throws `NotSupportedException`)
  - sync wrappers for
    `IUserIdentityTokenHandler.{Encrypt,Decrypt,Sign,Verify}`
  - sync + APM wrappers for the GDS / LDS client APIs.

## What the shim does NOT cover

These changes are source-level only; no extension method can paper over them.
Use the listed analyzer fix.

- `== null` / `!= null` on now-struct types — use the **UA0003** fixer.
- `?.` member access on now-struct types — use the **UA0004** fixer.
- `using var x = new CertificateIdentifier(...)` — use the **UA0010** fixer
  to drop the `using` / `Dispose` call.
- `[DataContract]` / `[DataMember]` on configuration extension classes — use
  the **UA0009** fixer.
- Removed `<Type>Collection` wrappers such as `Int32Collection`,
  `NodeIdCollection`, etc. — use the **UA0002** fixer to rewrite to
  `List<T>` or `ArrayOf<T>`.

## Sync-over-async caveat

The sync shims (for example `handler.Encrypt(bytes)`,
`gdsClient.RegisterApplication(...)`, the `Session` / `Subscription` sync
helpers) wrap their `*Async` counterparts via
`Task.Run(() => …Async(...)).GetAwaiter().GetResult()`. This is intended as
a **migration aid only**: it keeps legacy call sites compiling while you port
them to `async`/`await`. Do not leave these calls on production hot paths —
follow the `UA0011` / `UA0015` guidance and switch to the async APIs.

## TreatWarningsAsErrors recipe

If your project sets `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`
and you cannot relax it during the migration window, exclude the migration
diagnostics from the failure set:

```xml
<PropertyGroup>
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  <NoWarn>$(NoWarn);CS0618;UA0001;UA0002;UA0003;UA0004;UA0005;UA0006;UA0007;UA0008;UA0009;UA0010;UA0011;UA0012;UA0014;UA0015;UA0018;UA0019;UA0020</NoWarn>
</PropertyGroup>
```

Remove each entry as you finish fixing the corresponding rule, and drop the
whole block once the MigrationAnalyzer package is removed.

## Packaging note

The package ships **two analyzer DLLs** under `analyzers/dotnet/cs/`:

- `Opc.Ua.MigrationAnalyzer.dll` — the analyzer assembly. Targets `Microsoft.CodeAnalysis 4.x`
  (the stable analyzer API) and references **only** `Microsoft.CodeAnalysis.CSharp` so it
  loads cleanly in csc.exe's analyzer host (which ships only `Microsoft.CodeAnalysis.dll`
  + `CSharp.dll`, not `Workspaces`). All `DiagnosticAnalyzer` types live here.
- `Opc.Ua.MigrationAnalyzer.CodeFixer.dll` — the code-fix assembly. References
  `Microsoft.CodeAnalysis.CSharp.Workspaces` and hosts all `CodeFixProvider` types.
  Loaded only by Workspaces-aware hosts (Visual Studio / `dotnet format`).

This split is necessary because shipping a single DLL that references `Workspaces`
silently fails to load in csc.exe at command-line build time — csc loads the assembly
but JIT-resolution of `Workspaces` types fails (DLL not in bincore), and the analyzer
host swallows the load failure, producing zero diagnostics. Splitting keeps the
analyzer host happy while preserving full IDE/`dotnet format` code-fix functionality.

`RS1038` (suggesting separation) is the Roslyn rule that recommends this layout;
it is satisfied implicitly by the two-DLL design.

## Suppression recipes

To suppress an individual rule for a single line:

```csharp
#pragma warning disable UA0008 // Wrap Session.Call arguments with Variant.From
session.Call(objectId, methodId, "legacy");
#pragma warning restore UA0008
```

To set a project-wide severity, add to your `.editorconfig`:

```ini
[*.cs]
dotnet_diagnostic.UA0001.severity = none      # silence UA0001 entirely
dotnet_diagnostic.UA0008.severity = error     # treat UA0008 as an error
```
