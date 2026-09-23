# Developer Guide

This guide is the starting point for contributing to the OPC UA .NET Standard stack. It covers what to install, how to build and test, the coding standards ("dos and don'ts"), and task-oriented "how to" recipes (starting with how to add logging). It links out to the topic-specific documents in [docs/README.md](README.md) rather than repeating them.

If you are new here, read the sections in order: [Prerequisites](#prerequisites) → [Repository layout](#repository-layout) → [Building](#building) → [Running tests](#running-tests) → [Coding standards](#coding-standards-dos-and-donts). The [How-to guides](#how-to-guides) and [Packages, platform support, and versioning](#packages-platform-support-and-versioning) sections are reference material you can jump to as needed.

## Prerequisites

- **.NET SDK 10.0** — the whole repository builds and restores with the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). Older SDKs are not supported for building `main`. The class libraries still *target* older frameworks (see [Packages, platform support, and versioning](#packages-platform-support-and-versioning)), but you build them with the .NET 10 SDK.
- **An IDE (optional but recommended)** — Visual Studio 2026, Visual Studio Code with the C# Dev Kit, or JetBrains Rider. Everything can also be done from the command line with `dotnet`.
- **git** — to clone and to create feature branches.
- **Docker Desktop (optional)** — only needed to run the containerized reference server; see [ContainerReferenceServer.md](ContainerReferenceServer.md).

The C# language version is pinned (`LangVersion` 14) and analyzer/style rules are enforced by the build, so no extra tooling install is required to get the same diagnostics locally that CI produces.

## Repository layout

| Path | Contents |
| --- | --- |
| `src/` | The core stack and higher-level libraries: `Opc.Ua.Types`, `Opc.Ua.Core*`, `Opc.Ua.Client`, `Opc.Ua.Server`, `Opc.Ua.Configuration`, `Opc.Ua.PubSub` (+ transports), the GDS / DI / LDS / WoT libraries, and the `Opc.Ua.Redundancy*` family. |
| `samples/` | Reference and sample apps: `ConsoleReferenceServer`, `ConsoleReferenceClient`, `Quickstarts.Servers`, the `Minimal*` / `PumpDeviceIntegrationServer` NativeAOT samples, `Redundant*`, etc. |
| `tests/` | Unit and integration test projects, mirroring the library structure, plus shared test frameworks. |
| `tools/` | Source generators, migration analyzers, and the installable `Opc.Ua.Mcp` tool. Each analyzer and generator has a build project and — for the source generators — a `*.Pack` project that packages it under a Roslyn-versioned analyzer folder. |
| `docs/` | This documentation set (indexed by [docs/README.md](README.md)). |
| `fuzzing/` | SharpFuzz / libFuzzer fuzz targets (see [Fuzzing.md](../fuzzing/Fuzzing.md)). |

Central build configuration lives at the repository root and is imported by every project:

- `UA.slnx` — the solution containing all projects.
- `Directory.Build.props` / `Directory.Build.targets` — global MSBuild properties and targets.
- `Directory.Packages.props` — [Central Package Management](https://learn.microsoft.com/nuget/consume-packages/central-package-management): every NuGet version is declared here.
- `common.props` / `targets.props` — shared properties, analyzer settings, and the target-framework matrix.
- `.editorconfig` — the authoritative code-style and analyzer-severity rules (enforced at build time).

## Building

From the repository root:

```bash
dotnet restore UA.slnx
dotnet build UA.slnx
```

Notes:

- **Warnings are errors.** `TreatWarningsAsErrors` is enabled, so compiler (`CSxxxx`) and Roslynator (`RCSxxxx`) diagnostics fail the build. Microsoft Code Analysis (`CAxxxx`) diagnostics are emitted as non-fatal warnings unless a rule is promoted to error in `.editorconfig`. Fix all of them before opening a pull request.
- **Building a single target framework.** By default the libraries multi-target the whole matrix (see [Packages, platform support, and versioning](#packages-platform-support-and-versioning)). To restrict a local build to one framework, pass `-p:CustomTargetFrameworks`, for example:

  ```bash
  dotnet build src/Opc.Ua.Core/Opc.Ua.Core.csproj -f net10.0 -p:CustomTargetFrameworks=net10.0
  ```

- **Offline / restricted networks.** `NuGetAudit` is enabled and fails the build with `NU1900` when it cannot reach the audit service. If you build offline, pass `-p:NuGetAudit=false`.
- **Source generators are consumed as project references.** Projects that use the in-repo generators reference `tools/Opc.Ua.SourceGeneration[.Stack]` with `OutputItemType=Analyzer`. MSBuild only hands the compiler the generator assembly itself, so `Directory.Build.targets` adds the generator's runtime closure (its output directory, minus the Roslyn host assemblies) as `Analyzer` items — the same payload the generator NuGet packages ship under `analyzers/dotnet/<roslyn>/cs`. Without it the generators cannot resolve their dependencies and fail to initialise with `CS8784`.
- **Analyzers and generators are shipped under a Roslyn-versioned analyzer folder.** `roslyn.props` pins the Roslyn API version, and each generator has a `*.Pack` project that ships it under `analyzers/dotnet/roslyn<major>.<minor>/cs`. See [Repository layout](#repository-layout) and the [support matrix](#supported-analyzer-and-source-generator-hosts). Because the repository's own projects consume that same build, **building this repository requires a Roslyn 5.x host** (the .NET 10 SDK or Visual Studio 2026).

## Running tests

Run the whole suite from the solution:

```bash
dotnet test UA.slnx
```

Conventions and requirements:

- **Frameworks.** Test projects use either **NUnit** (with `Assert.That` assertions and **Moq** for mocking) or **TUnit** (with its own assertions and mock helpers). Do not mix the two in one project, and do not use the classic NUnit asserts (`Assert.AreEqual`, …).
- **Coverage.** Coverage is measured with **Coverlet** and must not regress; every non-application, non-test project should stay at or above **80 %**. Two gates enforce this in CI — see [Continuous integration](#continuous-integration).
- **Integration tests.** Client/server and pub/sub features need integration tests as well as unit tests. A feature library's integration tests normally live with its unit tests in `<Component>.Tests`, for example `Opc.Ua.Robotics.Tests`, and every test project name ends in `.Tests`. Split integration tests into a separate project only when they run long, destabilise the unit tests, or the suite needs further division. Keep them deterministic: allocate a free port per fixture rather than hard-coding one, wait on the actual signal instead of using `Thread.Sleep` as a synchronisation primitive, and dispose every session, subscription and server in teardown including on failure. A flaky integration test is worse than none.
- **Test output.** Do not write per-test diagnostics to `TestContext.Out` (or the console) unconditionally. The NUnit adapter forwards every captured line to the test runner as its own message over the socket it shares with the test host, so output that is harmless in one test becomes a bottleneck when a data-driven fixture repeats it thousands of times — it inflates the published results artifact, slows the run, and can wedge that socket until the CI job times out with no output at all (issue #4213). Buffer the dump and emit it only when the test does not pass; `EncoderCommon.TestOutput` in [`tests/Opc.Ua.Core.TestFramework/EncoderCommon.cs`](../tests/Opc.Ua.Core.TestFramework/EncoderCommon.cs) does exactly that and is the pattern to copy.
- **Certificate leak diagnostics.** Set `OPCUA_CERTIFICATE_LEAK_TRACKING=1` before starting a
  test process to capture allocation stacks for `Certificate` handles that are not explicitly
  disposed. The assembly-level leak detector includes live and unreachable outstanding handles,
  their allocation stacks, and the NUnit fixture where available. Tracking is enabled by default
  in Debug builds and opt-in in Release because stack capture adds per-certificate overhead. The
  equivalent AppContext switch is `Opc.Ua.Security.Certificates.CertificateLeakTracking`; set it
  before the first `Certificate` is created. For example, in PowerShell:

  ```powershell
  $env:OPCUA_CERTIFICATE_LEAK_TRACKING = '1'
  dotnet test `
      tests\Opc.Ua.Sessions.Tests\Opc.Ua.Sessions.Tests.csproj `
      -f net10.0 `
      -p:CustomTestTarget=net10.0
  ```
- **Before a pull request** the `UA.slnx` suite must pass on at least **.NET Framework 4.8** and **.NET 10.0**.
- **Testing a specific target framework.** The libraries multi-target, but the test executables run on one framework at a time. To run the suite against a non-default framework, set `CustomTestTarget` (supported values: `netstandard2.0`, `netstandard2.1`, `net472`, `net48`, `net8.0`, `net9.0`, `net10.0`). The batch file [`tests/customtest.bat`](../tests/customtest.bat) cleans, restores, and runs the tests for a chosen target; in Visual Studio, uncomment and set the `CustomTestTarget` property in [`targets.props`](../targets.props). A clean build for the target is recommended when switching.
- **CI matrix.** The pull-request gate runs the test suite on **net48** and **net10.0**, and compiles the solution for *every* supported target framework; the remaining test matrices (Debug, .NET 9/8, .NET Framework 4.7.2, netstandard) run in scheduled or manual CI. Fix all failing, flaky, and CodeQL findings in the pipelines. See [Continuous integration](#continuous-integration).

## Coding standards (dos and don'ts)

All rules apply to new code and to existing code you touch. The `.editorconfig` is authoritative and enforced at build time; the highlights below are the ones most often missed.

**Formatting and style**

- Add the OPC Foundation MIT license header to every new source file.
- 4-space indentation, max line length 120, CRLF line endings, UTF-8, final newline, no trailing whitespace.
- Allman braces; always specify access modifiers explicitly; member order is constructors → properties/events → methods → fields, each `public` → `protected` → `internal` → `private`.
- Do **not** use `#region`/`#endregion` or comment-only section dividers. Do **not** add `#nullable enable` to a file when the project already sets `<Nullable>enable</Nullable>`.
- Put every XML-doc `<summary>` text on its own line (never a single-line `/// <summary> … </summary>`).
- Follow standard C# naming; no underscores in method or test-method names (tests use PascalCase).

**API and language**

- **Async only.** New code uses `async`/`await` (TAP). Do not add APM or sync-over-async (`.Result`, `.Wait()`, `GetAwaiter().GetResult()`) unless explicitly requested.
- **No `object` in public API** (except when overriding `Equals`). For OPC UA values use `Variant`.
- **`INullable` types** must not be wrapped in `System.Nullable<T>` (`T?`); use `.IsNull` / `.Null` instead. On struct types prefer `TryGet`/`TryGetValue` over casting; never use `Variant.AsBoxedValue` or `IUnion.Value`.
- Prefer `ArrayOf<T>` over read-only collection types / `IReadOnlyList<T>` / arrays in new public API; prefer `ByteString` over `byte[]`; prefer `Span<byte>`/`ReadOnlySpan<byte>` over `byte[]`.
- Do not use `[Obsolete]` API (outside test code) and do not add API that is not NativeAOT-compatible.
- Maintain backward compatibility with 1.5.378; mark replaced API `[Obsolete]` rather than removing it.

**Concurrency**

- Never expose locks in any API surface. For a synchronous lock use `System.Threading.Lock` (a polyfill is provided for older TFMs) — never `private readonly object m_lock = new()`. Prefer `SemaphoreSlim` where async coordination is needed.

**Architecture**

- Make non-abstract public classes `sealed` by default; prefer a provider model with injectable providers over inheritance.
- Wire new functionality into the dependency-injection infrastructure (with a direct "construct it yourself" fallback) and expose it through the fluent API where possible.
- Reuse the existing base services (telemetry, file system, certificate/secret stores, state machines, sessions, source generators, …) instead of re-implementing them.

**Security**

- Never hardcode credentials, certificates, or secrets. Manage certificates through the certificate store system and secrets through the secret store (see [CertificateManager.md](CertificateManager.md) and [Certificates.md](Certificates.md)).
- Use only SHA-2 or stronger hash algorithms; use the audit and redaction APIs for sensitive data.

**Logging** — use source-generated logging; never call `ILogger.LogInformation/LogError/…` directly. See [Add a log message (source-generated)](#add-a-log-message-source-generated).

## How-to guides

### Add a log message (source-generated)

The stack uses [`LoggerMessageAttribute`](https://learn.microsoft.com/dotnet/core/extensions/logger-message-generator) source-generated logging **everywhere**. It avoids boxing value-type arguments, caches the message formatter, and emits an `IsEnabled` check so a disabled level costs nothing. Direct `ILogger.LogInformation/LogError/…` calls are not allowed. The runtime/observability side (how the `ILogger` is created from `ITelemetryContext`) is documented in [Diagnostics.md](Diagnostics.md#high-speed-logging-and-source-generators); this section is the authoring recipe.

**Recipe**

1. **Get a logger.** Obtain an `ILogger` from the ambient `ITelemetryContext` (`telemetry.CreateLogger<T>()`); most types already hold one in an `m_logger` field.
2. **Find or create the log class.** Each file that logs has, at its end, an `internal static partial class <PrimaryClass>Log` holding `[LoggerMessage]` **extension methods on `ILogger`**. Add your message there. If several closely-related files emit the *same* messages, use one shared `<Area>Log` class instead of duplicating (for example the encoders/decoders in `Opc.Ua.Types` share `EncodingLog`).
3. **Reserve an event id.** Each project has one `internal static class <AssemblyToken>EventIds` at its root (see [Event-id convention](#event-id-convention)). Use the existing per-class offset.
4. **Declare the message.** Add a partial method with `[LoggerMessage(EventId = <AssemblyToken>EventIds.<Class> + <index>, Level = LogLevel.<Level>, Message = "…")]` (see [Log class convention](#log-class-convention)).
5. **Call it.** Replace the old `logger.LogXxx(...)` call with `logger.<MethodName>(args)`.

#### Event-id convention

Each project owns exactly one event-id class, named `<AssemblyToken>EventIds`, in `namespace Opc.Ua`, in a file `EventIds.cs` at the project root. `<AssemblyToken>` is the assembly name with the `Opc.Ua.` prefix removed and dots dropped — for example `Opc.Ua.Core` → `CoreEventIds`, `Opc.Ua.Core.Types` → `CoreTypesEventIds`, `Opc.Ua.Client` → `ClientEventIds`.

The token prefix is required because the stack uses `InternalsVisibleTo`: two `internal` classes with the same name in the same namespace collide across an IVT boundary (`CS0436`). The class holds one `public const int` offset per log class. Offsets are assigned in class-alphabetical order starting at 0; each block reserves at least five spare slots for future messages and is then rounded up to the next multiple of ten, so ids stay documented and managed in one place. Every log method sets `EventId = <AssemblyToken>EventIds.<Class> + <zero-based message index within that class>`.

##### Narrow exception: retained EventSource-compatibility ids

The four legacy `System.Diagnostics.Tracing.EventSource` providers (`OPC-UA-Core`, `OPC-UA-Client`, `OPC-UA-Server`, `Opc.Ua.ChannelManager`) were removed and replaced with `[LoggerMessage]` equivalents. Their compatibility log methods are a deliberate, narrow exception to the convention above: each keeps the exact numeric id, event name, level, message template, and structured fields the corresponding ETW event had, so consumers can preserve event identity when they migrate from ETW to `ILogger`. Concretely:

- The compatibility log class uses the **old provider name as its `ILogger` category** (e.g. `"OPC-UA-Core"`, `"Opc.Ua.ChannelManager"`) instead of the typed, per-class category used elsewhere in the project.
- `EventId` resolves to the **literal legacy numeric id** (e.g. `10` for `OPC-UA-Core`'s former `ServiceCallStart`) rather than a normal per-class offset. Keep these values in the affected project's `EventIds.cs`; compatibility ids are scoped to their own logger category, so they may intentionally overlap ordinary per-assembly values.
- Every compatibility method sets `EventName` explicitly (`[LoggerMessage(EventId = 10, EventName = "ServiceCallStart", Level = LogLevel.Trace, Message = "...")]`) so `EventId.Name` matches the original ETW event name exactly.
- ETW-only metadata (provider GUID, `Task`, `Keywords`, manifest) is **not** retained because there is no `ILogger` equivalent.
- Do **not** use this pattern for new log messages. It exists only to preserve the event identities that previously shipped through the four EventSource providers; see [Diagnostics.md](Diagnostics.md#high-speed-logging-and-source-generators) and [migrate/2.0.x/telemetry.md](migrate/2.0.x/telemetry.md) for the full removal/compatibility mapping.

#### Log class convention

- **One log class per file**, named `<PrimaryClass>Log`, `internal static partial`, appended at the end of the file inside the same namespace.
- Methods are **extension methods on `ILogger`** (`public static partial void <Name>(this ILogger logger, …)`) so call sites read naturally as `logger.<Name>(…)`.
- Identical `this ILogger` overloads (same name and parameter types) declared in more than one class of the same namespace collide (`CS0121`) — deduplicate them into a single shared `<Area>Log` class. Overloads that differ by name or by parameter type are fine.

#### Message, level, and parameter rules

- **Message text** is exact and static; use named placeholders (`{ChannelId}`) that match a parameter of the same name. Never interpolate (`$"…"`). An `Exception` argument is detected by its type and does not need a placeholder.
- **Parameter types** must match the real argument type. Do **not** use `object`/`object?`, and do **not** call `.ToString()` on an argument (type the parameter instead, e.g. an enum or `int`); an unnecessary `.ToString()` trips `RCS1097`/`CA1305`. Declare a parameter nullable (`string?`, `Uri?`, `Exception?`) only when the argument can actually be null, otherwise the compiler reports `CS8604`.
- **Guard only expensive arguments.** If a call passes an expensive computed argument (`string.Join(...)`, a LINQ projection, `.ToString()` on a complex object) wrap it in `if (logger.IsEnabled(<level>))`; source generation does not suppress eager evaluation of the *arguments*, and `CA1873` flags it. Do **not** guard cheap arguments (locals, fields, ids) — over-guarding trips `RCS1006`/`RCS1061`. A guard must never gate an expression that has an observable side effect.
- **Dynamic levels stay hand-written.** `[LoggerMessage]` needs a compile-time `Level`. A call whose level is only known at runtime keeps the structured `logger.Log(logLevel, "{Template}", args)` form wrapped in `if (logger.IsEnabled(logLevel))`. These are the only remaining direct `ILogger.Log` calls.
- **Shared/linked source files** that are `<Compile Include>`-d into more than one project (for example a sample file linked into a test project) cannot reference another assembly's `<AssemblyToken>EventIds` class — give their log class literal `EventId` integers in a high, dedicated range instead.
- **Duplicate generator on netstandard.** A project that also references an R9 package (`Microsoft.Extensions.Http.Resilience`, `.Compliance`, `.Telemetry`, …) gets the `Microsoft.Gen.Logging` generator in addition to the in-box one; on `netstandard` both implement every partial method (`CS0757`). The repo's `Directory.Build.targets` removes the R9 analyzer on `netstandard` only — no per-project action is needed.

**Worked example**

```csharp
// EventIds.cs (project root) — the assembly-token prefix avoids CS0436 across
// InternalsVisibleTo boundaries.
namespace Opc.Ua
{
    internal static class TypesEventIds
    {
        public const int Encoding = 20;   // shared codec block (reserves 20)
        public const int Matrix = 50;     // per-file block (reserves 10)
    }
}

// end of Matrix.cs
internal static partial class MatrixLog
{
    [LoggerMessage(EventId = TypesEventIds.Matrix + 0, Level = LogLevel.Debug,
        Message = "ReadArray read dimensions[{Index}] = {Dimensions}. Matrix will have 0 elements.")]
    public static partial void ReadArrayZeroDimension(this ILogger logger, int index, int[] dimensions);
}

// call site
logger.ReadArrayZeroDimension(index, dimensions);
```

**Checklist**

- [ ] Message text and level are unchanged from the original call (behavior-preserving).
- [ ] Placeholders are named and match parameter names; no interpolation.
- [ ] Parameter types match the arguments; nullable only where needed; no `object`.
- [ ] Expensive arguments are guarded with `IsEnabled`; cheap ones are not.
- [ ] `EventId` uses the project's `<AssemblyToken>EventIds` offset (or a literal range for a shared/linked file, or a literal legacy id with an explicit `EventName` for a retained EventSource-compatibility message — see [the narrow exception](#narrow-exception-retained-eventsource-compatibility-ids)).
- [ ] When testing with a mocked `ILogger`, stub `IsEnabled(...) => true` and match on `EventId.Name`, not the (empty) source-generated state `ToString()`.

### Other common tasks

- **Add a new feature** — implement it in the right library, add unit and (for client/server/pubsub) integration tests, update or add a doc under `docs/`, and keep backward compatibility (see [Coding standards](#coding-standards-dos-and-donts)).
- **Add a document** — put it in `docs/` and link it from [docs/README.md](README.md).
- **Add a dependency** — declare the version in `Directory.Packages.props` (Central Package Management), prefer AOT/trimmable and permissively licensed packages, and get maintainer approval first.
- **Certificates and secrets** — see [Certificates.md](Certificates.md) and [CertificateManager.md](CertificateManager.md).
- **Source-generated node managers / data types** — see [NodeManagers.md](NodeManagers.md#source-generated-node-managers) and [SourceGeneratedDataTypes.md](SourceGeneratedDataTypes.md).
- **Server namespace metadata / history advertisement** — see [NodeManagers.md](NodeManagers.md#server-address-space-metadata).
- **Dependency injection** — see [DependencyInjection.md](DependencyInjection.md).
- **NativeAOT** — see [NativeAoT.md](NativeAoT.md).

## Packages, platform support, and versioning

### Released packages

The following NuGet packages are released on a monthly cadence (with hot fixes for security issues). The `OPCFoundation` prefix is reserved, and the assemblies and packages are signed by the OPC Foundation.

- [OPCFoundation.NetStandard.Opc.Ua](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua/) — a convenience meta-package that pulls in everything except PubSub. Prefer referencing the individual packages below to reduce your dependency surface.
- [OPCFoundation.NetStandard.Opc.Ua.Types](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.Types/)
- [OPCFoundation.NetStandard.Opc.Ua.Core.Types](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.Core.Types/) — the generated OPC UA NodeSet models and state classes.
- [OPCFoundation.NetStandard.Opc.Ua.Core](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.Core/) and [OPCFoundation.NetStandard.Opc.Ua.Security.Certificates](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.Security.Certificates/) — required by both client and server projects.
- [OPCFoundation.NetStandard.Opc.Ua.Configuration](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.Configuration/) — configure a UA application from file or with the fluent API.
- [OPCFoundation.NetStandard.Opc.Ua.Server](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.Server/) — build a UA server.
- [OPCFoundation.NetStandard.Opc.Ua.Client](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.Client/) and [OPCFoundation.NetStandard.Opc.Ua.Client.ComplexTypes](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.Client.ComplexTypes/) — build a client; the complex-type library adds support for complex types.
- [OPCFoundation.NetStandard.Opc.Ua.Bindings.Https](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.Bindings.Https/) — optional `opc.https` transport.
- [OPCFoundation.NetStandard.Opc.Ua.PubSub](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.PubSub/) (Beta) — publisher/subscriber model.

For improved source-level debugging, symbol packages for non-`.Debug` package IDs are published on nuget.org in `snupkg` format. `Debug`-compiled packages are retained with a `.Debug` suffix on GitHub Packages but are not published to nuget.org.

In-development previews are published **only** to the [GitHub Packages feed](https://nuget.pkg.github.com/OPCFoundation/index.json) — nothing in this repository pushes a preview to nuget.org. nuget.org receives a version only through the manually approved [promotion](ReleaseProcess.md#approved-promotion) of a stable candidate built from a `release/<major>.<minor>` branch, which is where the `2.0.0-preview.N` packages currently on nuget.org came from (the earlier `release/2.0.0` line). To consume those, use `2.0.0-preview.*` to float to the latest published `2.0.0-preview.N` release, pass `--prerelease` to `dotnet add package`, or
select *Include prerelease* in Visual Studio. No additional package source or
credentials are required for nuget.org; the GitHub Packages feed needs a classic PAT with `read:packages`.

The full set of packages the preview pipeline produces is pinned in [`.azurepipelines/expected-packages.txt`](../.azurepipelines/expected-packages.txt). `.azurepipelines/validate-source-generator-packages.ps1` fails the build when the packed output does not match it, so adding, removing or renaming a shipped package has to be done deliberately in the same pull request. That script also validates the analyzer packages: their `analyzers/dotnet/roslyn<major>.<minor>/cs` layout, that they carry their runtime closure privately, that the model generator's auto-imported `build/<PackageId>.props` is named after the package id, and — end to end — that a standalone project consuming the packed generator with a NodeSet actually gets code generated.

### Supported target frameworks

The class libraries currently target:

1. .NET Standard 2.0 (`Opc.Ua.Types` only)
2. .NET Standard 2.1
3. .NET Framework 4.7.2 (limited support)
4. .NET Framework 4.8
5. .NET 8.0
6. .NET 9.0
7. .NET 10.0

The pull-request gate *compiles* every one of these targets, but only runs the test suite on (4) and (7) to keep the feedback loop short; the remaining test matrices are covered by scheduled or manual CI. See [Running tests](#running-tests) for how to build and test a specific framework locally with `CustomTestTarget` / `tests/customtest.bat`, and [Continuous integration](#continuous-integration) for how the matrices are split.

### Supported analyzer and source generator hosts

The analyzer and source generator packages ship under `analyzers/dotnet/roslyn<major>.<minor>/cs`. The .NET SDK loads the highest folder its compiler supports and **ignores** folders above it, so an older host cleanly skips the analyzer instead of loading it and failing at generator-initialization time.

| Roslyn API | Package folder | Minimum host |
| --- | --- | --- |
| 4.14 | `analyzers/dotnet/roslyn4.14/cs` | Visual Studio 2022 17.14 / .NET 9 SDK |
| 5.0 | `analyzers/dotnet/roslyn5.0/cs` | Visual Studio 2026 18.0 / .NET 10 SDK |

The version is declared once in `roslyn.props`.

> **Adding a band below 4.14 is not just another entry in that file.** The analyzer closure — the generator, `Opc.Ua.SourceGeneration.Core` **and** `Opc.Ua.Types` — must bind against the Roslyn host's own `System.Collections.Immutable` and `System.Reflection.Metadata`. .NET satisfies a reference from a *higher* assembly version but never from a lower one, and those assemblies are supplied by the compiler, so the closure must reference the lowest version across every supported band and must never ship a copy of its own. Roslyn 4.14 and 5.0 both depend on 9.0.0, which is why `$(RoslynRuntimeVersion)` in `roslyn.props` drives the central pin and one build of the non-Roslyn closure serves both bands. Going lower — Roslyn 4.8 wants 7.x — would mean building that whole closure, `Opc.Ua.Types` included, a second time.
>
> Get it wrong and the failure is silent: the generator is skipped (`CS9057`), fails to load (`CS8032`) or throws `MissingMethodException` while initializing (`CS8784`) — all *warnings*, so the consumer just gets no generated code. `validate-source-generator-packages.ps1` therefore refuses any package that ships `Microsoft.CodeAnalysis*`, `System.Collections.Immutable` or `System.Reflection.Metadata`, and runs the packed down-level payload through a real compiler of that band.

### Versioning

From **2.0** onward, package versions are produced by [Nerdbank.GitVersioning](https://github.com/dotnet/Nerdbank.GitVersioning) (nbgv) from the `version.json` file at the repository root. That file holds the base version (currently `2.0.0-preview.{height}` while `master` is developing the next release) and requests [SemVer 2.0](https://semver.org/) package versions (`nugetPackageVersion.semVer: 2`); nbgv derives the version height, prerelease tag, and build metadata from the git history, and `version.props` maps the computed values onto the assembly and package version properties.

Stable (public-release) versions are produced **only** from a canonical `release/<major>.<minor>` branch (e.g. `release/2.0`, `release/2.1`) — never from `master`, a tag, or any other branch — and only at the exact commit whose `version.json` carries the plain `<major>.<minor>.<patch>` version with no prerelease label (e.g. `2.0.0`). Patch numbers increase by exactly one per release on their line (`2.0.0` → `2.0.1` → `2.0.2`); a new minor line resets the patch to zero (`2.1.0`). See **[Release process](ReleaseProcess.md)** for the full branch/version model and the step-by-step procedure for cutting a release, shipping a patch or minor version, backporting a fix, and promoting a stable candidate.

The XRegistry, WoT Connectivity, Vision, Robotics, Redundancy, Positioning,
OpenUSD, ISA95, AI, and DI package families remain preview packages even when
the root version is stable. Their numeric version follows the root version:
for example, a stable `2.0.0` root produces `2.0.0-preview.N` for these
families (`N` a committed, manually curated number in `preview-version.props`
that always sorts above every already-published preview — see
[Release process](ReleaseProcess.md)) and `2.0.0` for the other packages. The
same policy applies to the Robotics and Vision MCP extensions and the
OpenUSD connector tools. The package validation manifest
(`.azurepipelines/validate-nuget-package-set.ps1`) records the root package
version, a `preview`/`stable` `channel`, and the distinct family versions so
the signed release workflow can promote an intentional mixed-version set.

> The earlier 1.x packages used a different, spec-derived scheme in which the first two digits encoded the embedded NodeSet spec version (for example `1.5.378.x` corresponds to OPC UA spec V1.05, mapped to release branches such as `release/1.4.372`). That scheme no longer applies from 2.0 onward.

## Continuous integration

**GitHub Actions owns pull-request validation.** [`.github/workflows/buildandtest.yml`](../.github/workflows/buildandtest.yml) runs the complete build and test workload on GitHub-hosted runners for every triggering branch, and [`.github/workflows/nightly.yml`](../.github/workflows/nightly.yml) runs the full-scope workload on demand. The other workflows in [`.github/workflows/`](../.github/workflows) cover CodeQL, container images and the opt-in stress and stability suites.

Azure Pipelines ([`azure-pipelines.yml`](../azure-pipelines.yml) plus the templates in [`.azurepipelines/`](../.azurepipelines)) no longer runs on pull requests: the Azure context was dropped from the master ruleset and the pipeline's PR trigger is now `pr: none`, leaving `build-and-test summary` as the only required check. Azure still runs on pushes to `master` and on its weekly schedule, where it **duplicates** coverage rather than supplying any of it; see [Migration status](#migration-status) for what is left to switch off.

Pull requests targeting `master378` or a `release/*` line are unaffected — Azure Pipelines evaluates a pull request against the *target* branch's copy of `azure-pipelines.yml`, and those branches keep their own copy and their own required Azure context.

### What runs where

One table describes the entire migrated workload: `$Profiles` and `$BuildProfiles` in [`.github/scripts/get-ci-matrix.ps1`](../.github/scripts/get-ci-matrix.ps1). Both workflows call that script, so a profile added there appears in both without editing any YAML.

| | Pull request (`buildandtest.yml`) | Full scope (`nightly.yml`) |
| --- | --- | --- |
| Test profiles | Windows net48, Windows/Linux/macOS net10.0 | the above plus net472, net9.0, net8.0, netstandard2.0, netstandard2.1, the Debug legs, and the tiers that lift the category filter |
| Solution builds | every `.slnx` on Windows for net48/net10.0 × Debug/Release, `UA.slnx` for net472/netstandard2.0 and the Linux TFMs | every `.slnx` on Windows for all seven TFMs × Debug/Release, plus the Linux legs |
| Native AoT | linux-x64, osx-x64, osx-arm64 | the above plus win-x64 |
| Coverage | project floors and the graduated patch gate | project floors only |
| Trigger | every push and pull request | `workflow_dispatch` only |

A profile pins its target framework through `CustomTestTarget`, not `--framework`, because that is the mechanism [`targets.props`](../targets.props) uses. Two profiles are not runnable target frameworks at all: `netstandard2.0` hosts its tests on net48 and `netstandard2.1` hosts them on net8.0, so each profile records both what it builds with and what its tests run on.

GitHub refuses to start a run whose matrices expand past 256 jobs, and the limit cannot be raised. The project fan-out is therefore **batched**: each matrix entry carries several projects that [`.github/scripts/run-dotnet-tests.ps1`](../.github/scripts/run-dotnet-tests.ps1) runs in sequence, keeping per-project results and per-project timeouts. The batch size is the smallest one that fits the budget, so it grows by itself as test projects are added instead of silently truncating the matrix. [`CiMatrixScriptTests`](../tests/Opc.Ua.Tools.Tests/CiMatrixScriptTests.cs) asserts that both scopes fit, that every `*.Tests.csproj` on disk is either scheduled or explicitly excluded, and that a narrowed matrix can never expand to nothing.

The executor fails a project when its build fails, when the TRX counters report any failure, when it produces **no** TRX at all, when nothing in it actually ran, or when it outlives its per-project ceiling. A "no tests ran" outcome is a failure, not a pass — and that includes a suite whose tests were *all* skipped, which records a non-zero total while verifying nothing.

That per-project ceiling is a **single combined budget** covering the project's build *and* its test run, measured by one stopwatch. The matrix derives each job's `timeout-minutes` from it as `20 + projectCount × perProjectTimeout`, so spending it twice per project would let a batch outlive its job: GitHub would cancel the run, and a cancelled job produces neither the executor's per-project annotation nor its results. A batch whose budgets add up past the job ceiling is an error in `get-ci-matrix.ps1` rather than a clamped `timeout-minutes`, for the same reason. `CiMatrixScriptTests` pins both halves of that arithmetic.

The verdict comes from the emitted TRX rather than from the `dotnet test` exit code, and lives in [`.github/scripts/get-test-verdict.ps1`](../.github/scripts/get-test-verdict.ps1) so it can be tested on its own — see [`CiTestVerdictTests`](../tests/Opc.Ua.Tools.Tests/CiTestVerdictTests.cs). A non-zero exit is tolerated when, and only when, the results record at least one **passing** test and no failure, error, timeout, abort or `passedButRunAborted`. That combination means the host died during process **exit**, after the last test and every teardown had already run; failing it would report a false red. It is not a macOS quirk — Windows hosts do it too (run 35714133848, `test-windows-net48 (5/30)`: `Opc.Ua.Client.Tests` reported 256 passed, 0 failed, host exit 1). A host that dies mid-run leaves a non-zero counter and is still rejected, and a run in which every test was skipped is rejected whatever the exit code says. Every tolerated run raises a warning annotation and is labelled in the job summary, so a host that keeps dying stays visible. This matches the Azure gate in [`.azurepipelines/test.yml`](../.azurepipelines/test.yml).

#### Why a project can be skipped

`RestrictForLegacyTfm` in [`targets.props`](../targets.props) turns a project that does not support the requested `CustomTestTarget` into an empty shell with `IsTestProject=false`. Running `dotnet test` against one of those produces no TRX, which the executor treats as a failure — so it first probes `dotnet msbuild -getProperty:IsTestProject` and records the project as **not applicable** instead. Those rows appear in the job summary, so a project that quietly stops being applicable everywhere is visible rather than invisible.

### Test tiers

The pull-request profiles filter out `TestCategory=LongRunning` and `TestCategory=Stress`. The tiers those filters leave out run elsewhere:

| Tier | Where it runs |
| --- | --- |
| `LongRunning` categories in mainline projects | `linux-long-running` profile, `nightly.yml` only |
| `Opc.Ua.Subscriptions.Durable.Tests` | `windows-durable` / `linux-durable` profiles, `nightly.yml` only |
| `Opc.Ua.Stress.Tests` | [`.github/workflows/stress-test.yml`](../.github/workflows/stress-test.yml), opt-in |
| `Opc.Ua.Aot.Tests` and the `.Historian` / `.Mcp` companions | `aot-test` job (both workflows) — published and run as native executables, not through `dotnet test` |
| `Opc.Ua.OneFuzz.Validator.Tests` | `fuzz-drop` job — it pins net10.0 to match the drop it validates |

### Running the full scope

`nightly.yml` is **manual on purpose** — it has no `schedule:` trigger. Start it from the Actions tab (or `gh workflow run nightly.yml`) with:

| Input | Effect |
| --- | --- |
| `include_macos` | Include the macOS profiles (default `true`) |
| `run_private_corpus` | Additionally replay the private fuzz crash corpus (default `false`, requires the protected environment below) |

It uploads a `workload-manifest` artifact recording every (project × profile) tuple the run intended to cover, so a run's scope can be compared against another inventory instead of inferred from job names.

#### The private fuzz corpus

The checked-in corpus under [`fuzzing/`](../fuzzing) runs on every pull request. The **private crash corpus** — inputs that reproduce unfixed findings — is deliberately not public, so it is replayed only by the `private-corpus` job in `nightly.yml`, which:

- runs in the protected `fuzz-private-corpus` environment, so a reviewer approves each run;
- never runs from a fork or from an unreviewed ref;
- verifies the downloaded archive against a pinned SHA-256 before extracting it;
- runs the executor with `-QuietOutput`, so no reproducer ever reaches a public log;
- publishes hash-only evidence and discards the extracted inputs.

It is skipped unless a maintainer has provisioned the environment:

| Kind | Name | Purpose |
| --- | --- | --- |
| Secret | `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` | Federated (OIDC) login — no stored storage key |
| Variable | `FUZZ_CORPUS_ACCOUNT`, `FUZZ_CORPUS_CONTAINER`, `FUZZ_CORPUS_BLOB` | Where the archive lives |
| Variable | `FUZZ_CORPUS_SHA256` | Expected digest of the archive |

The job fails loudly when any of them is missing, and the nightly summary labels a run that did not replay the corpus as **INCOMPLETE**, so its absence is never mistaken for a pass.

### Required checks and coverage

Three concerns are deliberately kept apart:

| Concern | Check | In the branch ruleset? |
| --- | --- | --- |
| Every build and test passed | **`build-and-test summary`** | **Yes — required** |
| Every sample container image built | **`images summary`** | **Eligible — require it** |
| Coverage meets the thresholds | **`code coverage`** | **No — advisory** |

`build-and-test summary` is a single rollup job on purpose. The jobs underneath it are matrix-generated, so their names change whenever a test project or a profile is added; requiring a generated name would break as soon as the matrix changed. The job runs on `always()` and inspects `needs.*.result` itself, calling `exit 1` on anything that is neither `success` nor `skipped` — a failing dependency therefore shows as a red X.

`always()` is not optional here: a job *skipped* because a dependency failed surfaces to GitHub as `skipped`, and a required check reporting `skipped` is treated as **satisfied**. Without `always()` the rollup would wave a red build straight through.

For the same reason the workflow carries no `paths:` filter. A workflow filtered out by `paths` never reports its checks at all, and a required check that never reports leaves a pull request permanently "Expected — waiting for status to be reported". The decision is applied inside the `discover` job instead: a docs-only pull request skips the expensive jobs and still gets a legitimate green summary.

That decision is a **deny-list**, not an allow-list of build inputs, and it lives in [`.github/scripts/get-path-relevance.ps1`](../.github/scripts/get-path-relevance.ps1) so it can be tested — see [`CiPathRelevanceTests`](../tests/Opc.Ua.Tools.Tests/CiPathRelevanceTests.cs). It fails dangerously in one direction only: calling a real change irrelevant skips the build, test and AoT jobs while the required summary still reports **success**, so a broken change merges behind a green check. An allow-list of extensions cannot be kept complete — the source generators consume `.xml` and `.csv` design files (the `AdditionalFiles` items in [`src/Opc.Ua.WotCon/Opc.Ua.WotCon.csproj`](../src/Opc.Ua.WotCon/Opc.Ua.WotCon.csproj)), test projects carry XML and JSON fixtures, and `.editorconfig` is enforced at build time. Only Markdown and the `docs/` tree, which holds nothing but Markdown and images, are skippable; everything else builds.

[`.github/workflows/docker-image.yml`](../.github/workflows/docker-image.yml) (`Images CI`) follows the identical pattern for the sample container images. Its build legs are named `build-and-push-image (refserver)`, `(boilerserver)` and so on, so they cannot be pinned in a ruleset either; **`images summary`** is the fixed name that rolls all of them up. One broken image fails it, because a matrix job aggregates to `success` only when every leg succeeded. Its `pull_request` trigger carries the same branch list as the `CI` workflow and no `paths-ignore`, so both gates report on exactly the same set of pull requests; the docs/tests exclusion moved into its own `discover` job. Add `images summary` to the ruleset alongside `build-and-test summary`.

The coverage check reports a clean failure when the thresholds are missed, so a miss is visible on the pull request, but it never blocks the merge. Do not add it to the ruleset — that would make a coverage dip unmergeable, which is not the intent.

> Azure Pipelines no longer posts any check on a pull request targeting `master`; `build-and-test summary` is the only required check. On the branches where Azure still validates pull requests (`master378`, `release/*`), note that its `Tests passed` context is fail-*closed* rather than fail-red: the verdict lives in the stage `condition`, so when a test stage fails the stage is skipped and Azure posts **no check at all** — the required check stays unfulfilled and the merge stays blocked. You will see the failing test job in red and `Tests passed` still waiting, rather than two red checks. Verified on build 16613.

#### How coverage is measured

Every test matrix entry collects coverage while it runs and publishes its raw Cobertura fragment as an artifact. The coverage job then downloads every fragment the run produced, merges them **once** with ReportGenerator, and evaluates the merged report. It never re-runs the tests — doing so serialises a suite that was deliberately fanned out across matrix jobs and blows the job timeout.

Coverage is collected only on .NET 8.0 and newer hosts. `coverlet.collector` 10.x ships build assets for net8.0+ only, so a .NET Framework test host cannot load the `XPlat Code Coverage` collector at all — VSTest merely warns and writes nothing. The expander therefore never requests coverage on a `net4*` profile, and `CiMatrixScriptTests` asserts that. The long-running and durable tiers also opt out: they re-run projects the filtered legs already covered, so folding their numbers in would double-count them.

The evaluation is [`.azurepipelines/check-coverage.ps1`](../.azurepipelines/check-coverage.ps1), driven by [`coverage-thresholds.json`](../coverage-thresholds.json):

| Check | Behaviour |
| --- | --- |
| **Project floor** | Total line and branch rates must meet the absolute floors in `coverage-thresholds.json`. The `ignore` globs are applied here too, so samples, tests and generated code do not count. |
| **Patch coverage** | On pull requests, lines you added or modified must reach a floor that is **graduated by how much changed** — see below. The uncovered changed lines are listed by file. |
| **Baseline delta** | Reports how total coverage compares with the recorded `baselineLineRate`. Warning only, even within this advisory check. |

Ratchet `minimumLineRate`, `minimumBranchRate` and `baselineLineRate` **upward** as coverage improves; never lower them to turn a red check green.

> The script still lives under `.azurepipelines/` because it is shared, not because it is Azure-specific. The directory keeps its name so the reusable PowerShell helpers there do not all have to move at once.

Two things about the `ignore` globs regularly catch people out. `samples/**` is ignored, so a sample can carry
tests for its own sake — a wrong kinematics solver would make a sample lie — without those lines counting
toward the patch gate. `tools/**` is **not** ignored, so anything you change under `tools/` is measured like
product code, and an assembly no test project references contributes changed lines that are counted as
**uncovered** because no report mentions them. If you add code there, make sure a test project loads the
assembly, or the patch gate will read far lower than the per-file numbers suggest.

##### Patch coverage is graduated by patch size

A coverage percentage over a handful of lines carries almost no information. One uncovered line in a two-line fix reads as 50%, and a flat floor would fail it — which teaches authors to ignore the check rather than act on it. So the requirement scales with how much actually changed:

| Coverable changed lines | Floor | Below the floor |
| --- | --- | --- |
| 1 – 10 | 50 % | :warning: **warning**, check still passes |
| 11 – 100 | 60 % | :warning: **warning**, check still passes |
| more than 100 | `patch.target` − `patch.threshold` (75 %) | :x: **failure** |

Only changes larger than the last band can fail the patch check. At that size the percentage is meaningful, and a large untested change is exactly what the check exists to catch. Below it you still get a warning naming the uncovered lines, so the signal is never silent — it just does not block.

The bands live in `patch.bands` in [`coverage-thresholds.json`](../coverage-thresholds.json). They are consulted in order and the first band whose `maxChangedLines` covers the patch wins; set `enforced: true` on a band to make it blocking. Anything larger than the last band falls through to `patch.target` − `patch.threshold` and is always enforced.

Remember that the coverage check as a whole is advisory and stays out of the branch ruleset — an enforced band produces a red `Code coverage` check, not a blocked merge.

##### Codecov

The merged report is also uploaded to [codecov.io](https://codecov.io), which is where the pull-request comment, the file-by-file diff view and the coverage trend live. **Codecov does not gate.** Both of its status checks are `informational: true` in [`codecov.yml`](../codecov.yml), because two gates with two sets of thresholds would eventually disagree about the same pull request and the easier one to silence would win. The rules that actually decide are the ones above.

Each CI system uploads the report it merged, under its own flag (`azure`, `actions`), since the two matrices deliberately cover different legs.

The upload is optional on both systems and never fails a build:

| | Turn it off with | Also skipped when |
| --- | --- | --- |
| GitHub Actions | the `ENABLE_CODECOV` workflow `env` (default `'true'`) | the `CODECOV_TOKEN` secret is unavailable, as on fork pull requests |
| Azure Pipelines | the `enableCodecov` pipeline parameter (default `true`) | the `CODECOV_TOKEN` secret variable is unset |

Keep the `ignore` list in `codecov.yml` in step with the one in `coverage-thresholds.json`, or the two will report on different code.

#### Where the numbers appear

The script renders a markdown summary that both systems surface, so you never have to open a raw log to see why coverage moved:

- **GitHub Actions** — appended to the run's job summary, and posted as a single sticky pull-request comment that is updated in place on each run. Threshold misses additionally appear as run annotations. On a pull request **from a fork** the token is read-only, so the comment is skipped and only the job summary is written.
- **Azure Pipelines** — attached to the build summary via `##vso[task.uploadsummary]`, alongside the usual Code Coverage tab and the Codacy upload.

Both also publish the merged HTML report as a `coverage-report` artifact.

> The two systems report **different numbers**, and that is expected. GitHub Actions merges every profile that collects coverage, whereas Azure Pipelines merges only its own fast legs. The GitHub figure is the representative one — and now the only one a pull request sees, since Azure no longer runs on pull requests — and a full-scope run reads differently again because it covers different profiles.

To reproduce a coverage failure locally, generate the same report with [`tests/codecoverage.cmd`](../tests/codecoverage.cmd) (or [`tests/codecoverage.sh`](../tests/codecoverage.sh)) and run the script against it:

```powershell
./.azurepipelines/check-coverage.ps1 -CoberturaPath ./CodeCoverage/Cobertura.xml -BaseRef master -SummaryPath ./coverage-summary.md
```

Omit `-BaseRef` to check only the project floor, and `-SummaryPath` to skip the markdown summary.

### Reproducing a CI leg locally

Both workflows delegate to the same two scripts, so any leg can be reproduced without a runner. Expand the matrix to see what a scope covers:

```powershell
./.github/scripts/get-ci-matrix.ps1 -Scope pr -ManifestPath ./pr-manifest.json
```

Then run one entry's projects exactly as CI would:

```powershell
./.github/scripts/run-dotnet-tests.ps1 `
    -Projects 'tests/Opc.Ua.Core.Tests/Opc.Ua.Core.Tests.csproj' `
    -CustomTestTarget net10.0 -Framework net10.0 -Configuration Release `
    -Filter 'TestCategory!=LongRunning&TestCategory!=Stress' `
    -ResultsDirectory ./TestResults
```

Add `-Coverage` to collect Cobertura fragments, and `-QuietOutput` to redirect child output to a log file instead of the console.

### Triggering a pipeline run on a pull request

Azure Pipelines no longer builds pull requests targeting `master` (`pr: none`). If you need an Azure run for comparison while the migration finishes, a repository owner or a collaborator with `Write` permission can still start one by commenting:

```text
/azp run
```

`/azp run <pipeline-name>` targets a single pipeline. If a comment appears to do nothing, check that your GitHub organization membership is **public** — Azure Pipelines cannot see private organization members unless they are direct repository collaborators, and it silently ignores their commands.

On the branches where Azure still validates pull requests (`master378` and the `release/*` lines, each governed by its own copy of `azure-pipelines.yml`), the definition is configured with **Require a team member's comment before building a pull request**, scoped to *pull requests from non-team members*. That setting lives in the Azure DevOps portal (pipeline → **More actions** → **Triggers** → **Pull request validation**), not in YAML. GitHub Actions applies its own equivalent "Approve and run workflows" gate to outside contributors and to the **GitHub Copilot coding agent**.

### Migration status

The Actions workflows were added while Azure Pipelines kept every trigger and required context it had, so a gap in the new system could not leave a change untested. Since then the master ruleset has dropped the Azure context, leaving `build-and-test summary` as the only required check, and this repository's PR trigger has been set to `pr: none`.

What is still outstanding — each needing repository- or organization-administrator access, and therefore **not** part of the source change:

1. Run `nightly.yml` on a trusted SHA with the private corpus provisioned and compare its manifest against a full-scope Azure run.
2. Confirm definition 14's service-side **Pull request validation** setting is off. `pr: none` covers the YAML trigger, but an enabled "Override the YAML PR trigger from here" would still queue builds.
3. Retire both scheduling sources for the Azure full scope — the YAML `cron` *and* the service-side schedule on definition 14 — and the push trigger, once the manual replacement is proven by step 1.
4. Retire `azure-pipelines-preview.yml` and definition 16's build-completion trigger. Development packages already publish to GitHub Packages from [`.github/workflows/nuget-publish.yml`](../.github/workflows/nuget-publish.yml), so that publisher is a duplicate. Ensure the stale definition 13 cannot restart it.

> **Before cutting a canonical `release/2.<minor>` branch:** the `Release` ruleset requires `OPCFoundation.UA-.NETStandard` for *every* `refs/heads/release/*`. A new 2.x branch inherits `pr: none` from `master`, so that Azure context would never report and would block every pull request into the new line. Exclude the new ref from that ruleset — or add a 2.x ruleset requiring `build-and-test summary` — as part of creating the branch. The existing 1.x lines and the frozen `release/2.0.0` keep their own copy of `azure-pipelines.yml` and must keep the Azure requirement.

Preserve the Azure definitions, their artifacts, feeds, secure files, pools and service connections — retiring a trigger is not the same as deleting history. The `master378` and 1.x pipelines are out of scope entirely.

Enabling a `schedule:` trigger on `nightly.yml` is a separate, deliberate decision; it stays manual until step 1 is complete.


## Contributing and pull requests

- Fork the repository (or, if you have write access, push a branch prefixed with your username) and open a pull request. You must agree to the [Contributor License Agreement](https://opcfoundation.org/license/cla/ContributorLicenseAgreementv1.0.pdf); the "I AGREE" prompt appears automatically on your first PR. See [CONTRIBUTING.md](../CONTRIBUTING.md).
- Before submitting: all tests pass, code analysis is clean (no new warnings), the change keeps backward compatibility, and security implications are reviewed.
- The pull-request template asks you to confirm the CLA, added tests/coverage, documentation, a warning-free build, that the `UA.slnx` suite passed on **.NET Framework 4.8** and **.NET 10.0**, and that CI and CodeQL are green.
- You can run the `opc-ua-codestyle-enforcer` agent to drive analyzer warnings to zero before opening the PR.

## Related documentation

- [Documentation index](README.md) — all topic guides.
- [Diagnostics](Diagnostics.md) — telemetry context, logging runtime, metrics, audit events, server diagnostics nodes, and packet capture.
- [Dependency Injection](DependencyInjection.md), [Certificates](Certificates.md) / [Certificate Manager](CertificateManager.md), [NativeAOT](NativeAoT.md), [Migration Guide](MigrationGuide.md), [What's New in 2.0](WhatsNewIn2.0.md).
- [Fuzz testing](../fuzzing/Fuzzing.md).

Fuzz replay has a dedicated GitHub Actions matrix and a local
[`fuzzing/Scripts/test-fuzzing.ps1`](../fuzzing/Scripts/test-fuzzing.ps1) entry point.
It covers the modern framework matrix and applicable .NET Framework 4.8 projects, including
seed-, dictionary- and script-only changes. Target inventory and fork behavior mappings belong
in `fuzzing/fuzz-targets.json`, not hardcoded test-count floors. The dedicated
`fuzz-parity.runsettings` includes generated protocol methods in fuzz-only coverage; it does not
alter the general coverage policy. Fuzz scripts also set `FuzzCoverage=true` to omit generated
data-type coverage-exclusion attributes only in those builds; an include filter alone cannot
override a compiled exclusion attribute. Internal OneFuzz drops must remain uninstrumented locally
and require owner-supplied configuration and actual worker execution evidence.
