# Developer Guide

This guide is the starting point for contributing to the OPC UA .NET Standard stack. It covers what to install, how to build and test, the coding standards ("dos and don'ts"), and task-oriented "how to" recipes (starting with how to add logging). It links out to the topic-specific documents in [docs/README.md](README.md) rather than repeating them.

If you are new here, read the sections in order: [Prerequisites](#prerequisites) → [Repository layout](#repository-layout) → [Building](#building) → [Running tests](#running-tests) → [Coding standards](#coding-standards-dos-and-donts). The [How-to guides](#how-to-guides) and [Packages, platform support, and versioning](#packages-platform-support-and-versioning) sections are reference material you can jump to as needed.

## Contents

- [Prerequisites](#prerequisites)
- [Repository layout](#repository-layout)
- [Building](#building)
- [Running tests](#running-tests)
  - [Testing UaLens](#testing-ualens)
- [Coding standards (dos and don'ts)](#coding-standards-dos-and-donts)
- [How-to guides](#how-to-guides)
  - [Add a log message (source-generated)](#add-a-log-message-source-generated)
  - [Other common tasks](#other-common-tasks)
- [Packages, platform support, and versioning](#packages-platform-support-and-versioning)
  - [Released packages](#released-packages)
  - [Supported target frameworks](#supported-target-frameworks)
  - [Supported analyzer and source generator hosts](#supported-analyzer-and-source-generator-hosts)
  - [Versioning](#versioning)
- [Continuous integration](#continuous-integration)
  - [What runs where](#what-runs-where)
  - [Test tiers](#test-tiers)
  - [Running the full scope](#running-the-full-scope)
  - [Required checks and coverage](#required-checks-and-coverage)
  - [Reproducing a CI leg locally](#reproducing-a-ci-leg-locally)
  - [Pull requests from outside contributors](#pull-requests-from-outside-contributors)
  - [Azure Pipelines on other branches](#azure-pipelines-on-other-branches)
- [Contributing and pull requests](#contributing-and-pull-requests)
- [Related documentation](#related-documentation)

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

- **Warnings are errors.** `TreatWarningsAsErrors` and `CodeAnalysisTreatWarningsAsErrors` are enabled, so compiler (`CSxxxx`), Roslynator (`RCSxxxx`) and Microsoft Code Analysis (`CAxxxx`) warnings all fail the build. Fix them rather than suppressing them; a suppression must carry a comment explaining why and a TODO to remove it.
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

UaLens uses separate test assemblies for ordinary tests, native workflows and
application-desktop tests. See [Testing UaLens](#testing-ualens) for their commands.

Conventions and requirements:

- **Frameworks.** Test projects use either **NUnit** (with `Assert.That` assertions and **Moq** for mocking) or **TUnit** (with its own assertions and mock helpers). Do not mix the two in one project, and do not use the classic NUnit asserts (`Assert.AreEqual`, …).
- **Coverage.** Coverage is measured with **Coverlet** and must not regress; every non-application, non-test project should stay at or above **80 %**. Two gates enforce this in CI — see [Continuous integration](#continuous-integration).
- **Integration tests.** Client/server and pub/sub features need integration tests as well as unit tests. A feature library's integration tests normally live with its unit tests in `<Component>.Tests`, for example `Opc.Ua.Robotics.Tests`, and every test project name ends in `.Tests`. Split integration tests into a separate project only when they run long, destabilise the unit tests, or the suite needs further division. Keep them deterministic: allocate a free port per fixture rather than hard-coding one, wait on the actual signal instead of using `Thread.Sleep` as a synchronisation primitive, and dispose every session, subscription and server in teardown including on failure. A flaky integration test is worse than none.
- **Test output.** Keep successful-test output compact. NUnit and the test runner retain captured output and create additional copies when forwarding and serializing it to TRX; repeated exhaustive dumps can consume gigabytes of memory, inflate results artifacts, and stall the runner's shared socket (issue #4213). For small failure-only diagnostics, buffer the dump and emit it only when the test does not pass; see `EncoderCommon.TestOutput` in [`tests/Opc.Ua.Core.TestFramework/EncoderCommon.cs`](../tests/Opc.Ua.Core.TestFramework/EncoderCommon.cs). Stream large dumps to files and register them with `TestContext.AddTestAttachment` instead of building a large string or writing every line to `TestContext.Out`. `CommonTestWorkers.BrowseFullAddressSpaceWorkerAsync(..., outputResult: true)` follows this pattern: the full listing is a test-result attachment, while inline output contains only the reference count. Keep attachment files until the runner has collected them.
- **Certificate leak diagnostics.** Set `OPCUA_CERTIFICATE_LEAK_TRACKING=1` before starting a
  test process to capture allocation stacks for `Certificate` handles that are not explicitly
  disposed. The assembly-level leak detector includes live and unreachable outstanding handles,
  their allocation stacks, and the NUnit fixture where available. Tracking is enabled by default
  in Debug builds and opt-in in Release because stack capture adds per-certificate overhead. The
  equivalent AppContext switch is `Opc.Ua.Security.Certificates.CertificateLeakTracking`; set it
  before the first `Certificate` is created. A leak fails the assembly's `[OneTimeTearDown]`,
  which fails the CI run. For example, in PowerShell:

  ```powershell
  $env:OPCUA_CERTIFICATE_LEAK_TRACKING = '1'
  dotnet test `
      tests\Opc.Ua.Sessions.Tests\Opc.Ua.Sessions.Tests.csproj `
      -f net10.0 `
      -p:CustomTestTarget=net10.0
  ```
- **Before a pull request** the `UA.slnx` suite must pass on at least **.NET Framework 4.8** and **.NET 10.0**.
- **Testing a specific target framework.** The libraries multi-target, but the test executables run on one framework at a time. To run the suite against a non-default framework, set `CustomTestTarget` (supported values: `net48`, `net8.0`, `net9.0`, `net10.0`). The batch file [`tests/customtest.bat`](../tests/customtest.bat) cleans, restores, and runs the tests for a chosen target; in Visual Studio, uncomment and set the `CustomTestTarget` property in [`targets.props`](../targets.props). A clean build for the target is recommended when switching.
- **CI matrix.** The pull-request gate runs the test suite on **net48** and **net10.0**, and compiles the solution for *every* supported target framework; the remaining test matrices (Debug, .NET 9/8) run in scheduled or manual CI. Fix all failing, flaky, and CodeQL findings in the pipelines. See [Continuous integration](#continuous-integration).

Wire compatibility with the released 1.5 stack is covered by
[`tests/Opc.Ua.Interop.Tests`](../tests/Opc.Ua.Interop.Tests), in both
directions (2.0 client to 1.5 server and 1.5 client to 2.0 server):

- sessions over None, the RSA policies and the ECC policies, with anonymous and
  user name logon, and the read, write, browse, call and subscription services;
- the existing 2.0 `ClientTest` workers that do not depend on the in-process
  server, run against the 1.5 Quickstarts reference server
  (`ClientTestFramework.ExternalServerUrl`);
- custom data types (structures, unions, optional fields, enumerations) loaded,
  decoded and written back with the other side's complex type system;
- certificate trust with auto-accept off: untrusted peers, CA-issued
  certificates, revoked certificates;
- message sizes and service limits: multi-chunk messages, values above the
  peer's encoding limits, responses above the client's MaxMessageSize,
  operation limits, browse continuation points and security token renewal;
- events and alarms: event subscriptions with select and where clauses,
  ConditionRefresh and acknowledging an alarm of the reference server;
- subscription features: absolute and percent deadbands, queue overflow with
  DiscardOldest, triggering, Republish and TransferSubscriptions;
- identities and services: wrong passwords, X509 user tokens, RegisterNodes,
  HistoryRead, AddNodes/DeleteNodes, IndexRange, FindServers and reactivating
  a session on a new secure channel (`ServerFeatureInteropTests` and the
  feature checks of `LegacyClientInteropTests`).

The 1.5 side is
[`tests/Opc.Ua.Interop.LegacyPeer`](../tests/Opc.Ua.Interop.LegacyPeer), a
console application built from the `OPCFoundation.NetStandard.Opc.Ua.*` NuGet
packages and started as a child process, because the two stacks share assembly
names and cannot be loaded into one process. It deliberately does not import the
repository build settings. In server mode it hosts either a small interop server
with deliberately tight limits or the 1.5 Quickstarts reference server; in client
mode it runs named checks and prints one JSON result line per check, which the
tests report as separate results. The test project builds it and copies it to
`legacy-peer/` next to the tests; pass `-p:LegacyStackVersion=<version>` to test
another 1.5 release, or set `OPCUA_INTEROP_LEGACY_PEER` to the path of a
prebuilt `Opc.Ua.Interop.LegacyPeer.dll`. 1.5.378 cannot reload a certificate it
has just created in a long store path, so the tests keep their PKI folders
directly below the temp folder. The project is a `*.Tests.csproj`, so CI runs it
on every pull-request test leg like any other test project.

The same `LegacyServerInteropTests`, `LegacyClientInteropTests`,
`ServerFeatureInteropTests` and `EccInteropTests` also run against peers built
on other OPC UA stacks, in
[`tests/Opc.Ua.Interop.Peers`](../tests/Opc.Ua.Interop.Peers): `node-opcua`
(Node.js), `milo` (Eclipse Milo, Java), `asyncua` (Python), `open62541` (C),
`async-opcua` (Rust) and `gopcua` (Go, client only). The
[peer README](../tests/Opc.Ua.Interop.Peers/README.md) describes how to install
the toolchains, build each peer and run the fixtures against it. A peer speaks the same command line and
output protocol as the 1.5 peer: `server` prints `PEER-INFO {json}` (stack,
version, application URI, software version and, optionally, `policies`: the
security policy URIs the peer implements; `EccInteropTests` runs the policies
beyond the four 1.5 ECC policies, e.g. `ECC_nistP256_AesGcm`, only against a
peer that declares them) and then the ready line; `client`
prints one `RESULT {json}` line per check. Select a peer with three variables:

| Variable | node-opcua | Milo | asyncua | open62541, async-opcua, gopcua |
| --- | --- | --- | --- | --- |
| `OPCUA_INTEROP_PEER_HOST` | path of `node` | path of `java` | path of `python` | (unset) |
| `OPCUA_INTEROP_PEER_HOST_ARGUMENTS` | (unset) | `-jar` | (unset) | (unset) |
| `OPCUA_INTEROP_LEGACY_PEER` | `.../node-opcua/peer.mjs` | `.../milo/target/milo-peer.jar` | `.../asyncua/peer.py` | path of the executable |

Known defects of a stack are listed in the peer's `expected-differences.json`,
keyed by check name or `Fixture.Test`; a listed failure is reported as
inconclusive with its reason, a listed test that passes as a warning. The
[`Foreign stack interop`](../.github/workflows/interop-foreign.yml) workflow
runs every peer on Windows, Linux and macOS (open62541 on Linux only) nightly, on demand and for pull
requests labelled `interop`; it is not a required check.

Channel recovery regressions use `ManagedSessionReconnectTests` in the client
test project and `ClientChannelManagerManagedTests` / `ReconnectDeadlineTests`
in the core test project. The composed fixture scripts an in-process transport
while retaining the real session, V2 publishing workers and channel ready gate.
Use phase signals and the injected clock rather than a server-process kill or
sleeps. Keep every phase wait bounded. When checking the recreate/drain wiring,
disable the managed-session channel deadline so outer takeover cannot mask a
broken drain, and verify the parked Publish attempt has unwound before allowing
replacement-session creation to finish.

### Testing UaLens

All three assemblies are registered in `UA.slnx` and use the normal NUnit runner:

| Project | Scope |
| --- | --- |
| `tests/Opc.Ua.Lens.Tests` | Protocol, model, offline and unit tests; no desktop application |
| `tests/Opc.Ua.Lens.Workflow.Tests` | Native-window workflows with injected protocol clients and an owned dispatcher |
| `tests/Opc.Ua.Lens.Desktop.Tests` | Application resources, shell/dialog regressions, NodeSet and namespace UI |

Run from the repository root with an interactive Windows desktop. Linux native
tests require X11 or `xvfb-run`; the shared CI runner supplies Xvfb. Cocoa requires
the process main thread, so these dispatcher-thread native assemblies are
Windows/Linux-only. Their source still builds on macOS.

```powershell
$env:CustomTestTarget = 'net10.0'
dotnet test tests\Opc.Ua.Lens.Tests\Opc.Ua.Lens.Tests.csproj -c Release -f net10.0
dotnet test tests\Opc.Ua.Lens.Workflow.Tests\Opc.Ua.Lens.Workflow.Tests.csproj -c Release -f net10.0
dotnet test tests\Opc.Ua.Lens.Desktop.Tests\Opc.Ua.Lens.Desktop.Tests.csproj -c Release -f net10.0
```

The assemblies isolate native dispatcher lifetimes without category-dependent
multi-pass handling in the shared runner. Collect coverage with the normal
`tests/coverlet.runsettings.xml`; results from all applicable assemblies contribute
to the unchanged coverage gate.

The [desktop count gate](../tools/Opc.Ua.Lens/DesktopTesting.md) also selects
24 named regressions and rejects missing, skipped, duplicate or failed results.
It includes pending-write shutdown, dialog cancellation, keyboard navigation,
inspector publication and minimum-size checks. Native publishing and managed-tool
installation are [separate artifact checks](../tools/Opc.Ua.Lens/README.md).

Theme checks require 4.5:1 source-text contrast and 3:1 chart-series contrast on
their defined surfaces. The native lane attaches bounded PNG captures and CSV
color-vision diagnostics to its TRX. Captures use the rendered client visual;
they do not qualify an OS compositor, physical display, screen reader or human
color perception. DPI, physical keyboard and assistive-technology acceptance
remain separate checks. Historical execution reports belong in
[the review backlog](../plans/UaLensReview.md), not the user guide.

## Coding standards (dos and don'ts)

All rules apply to new code and to existing code you touch. The `.editorconfig` is authoritative and enforced at build time; the highlights below are the ones most often missed.

**Formatting and style**

- Add the OPC Foundation MIT license header to every new source file.
- 4-space indentation, max line length 120, CRLF line endings, UTF-8, final newline, no trailing whitespace.
- Allman braces; always specify access modifiers explicitly; member order is constructors → properties/events → methods → fields, each `public` → `protected` → `internal` → `private`.
- Do **not** use `#region`/`#endregion` or comment-only section dividers. Nullable reference types are enabled for every project in `common.props`: do **not** add `<Nullable>` to a project or `#nullable enable` to a file (only `<auto-generated>` files need the directive).
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

In-development builds from `master` (`2.0.0-preview.<N>.g<commit>`) are published **only** to the [GitHub Packages feed](https://nuget.pkg.github.com/OPCFoundation/index.json). nuget.org receives a version only through a manually dispatched `release.yml` run from a `release/<major>.<minor>` branch: an [approved promotion](ReleaseProcess.md#approved-promotion) of a stable release, or a numbered [preview release](ReleaseProcess.md#preview-release) such as `2.0.0-preview.6`. `2.0.0-preview.2` to `.5` came from the earlier, retired `release/2.0.0` line and are the last previews that also put `.Debug` packages on nuget.org; see [`.Debug` packages are no longer published to nuget.org](migrate/2.0.x/packages.md#debug-packages-are-no-longer-published-to-nugetorg). To consume the nuget.org previews, use `2.0.0-preview.*` to float to the latest published `2.0.0-preview.N` release, pass `--prerelease` to `dotnet add package`, or
select *Include prerelease* in Visual Studio. No additional package source or
credentials are required for nuget.org; the GitHub Packages feed needs a classic PAT with `read:packages`.

The full set of packages [`nuget-publish.yml`](../.github/workflows/nuget-publish.yml) produces is pinned in [`.azurepipelines/expected-packages.txt`](../.azurepipelines/expected-packages.txt). `.azurepipelines/validate-source-generator-packages.ps1` fails the build when the packed output does not match it, so adding, removing or renaming a shipped package has to be done deliberately in the same pull request. That script also validates the analyzer packages: their `analyzers/dotnet/roslyn<major>.<minor>/cs` layout, that they carry their runtime closure privately, that the model generator's auto-imported `build/<PackageId>.props` is named after the package id, and — end to end — that a standalone project consuming the packed generator with a NodeSet actually gets code generated.

### Supported target frameworks

The class libraries currently target:

1. .NET Standard 2.0 (`Opc.Ua.Types` and `Opc.Ua.SourceGeneration.Core` only, because the source generators load them)
2. .NET Framework 4.8
3. .NET 8.0
4. .NET 9.0
5. .NET 10.0

The pull-request gate *compiles* every one of these targets, but only runs the test suite on (2) and (5) to keep the feedback loop short; the remaining test matrices are covered by scheduled or manual CI. See [Running tests](#running-tests) for how to build and test a specific framework locally with `CustomTestTarget` / `tests/customtest.bat`, and [Continuous integration](#continuous-integration) for how the matrices are split.

### Supported analyzer and source generator hosts

The analyzer and source generator packages ship under `analyzers/dotnet/roslyn<major>.<minor>/cs`. The .NET SDK loads the highest folder its compiler supports and **ignores** folders above it, so an older host cleanly skips the analyzer instead of loading it and failing at generator-initialization time.

| Roslyn API | Package folder | Minimum host |
| --- | --- | --- |
| 4.14 | `analyzers/dotnet/roslyn4.14/cs` | Visual Studio 2022 17.14 / .NET 9 SDK |
| 5.0 | `analyzers/dotnet/roslyn5.0/cs` | Visual Studio 2026 18.0 / .NET 10 SDK |

The version is declared once in `roslyn.props`.

> **Adding a band below 4.14 is not just another entry in that file.** The analyzer closure — the generator, `Opc.Ua.SourceGeneration.Core` **and** `Opc.Ua.Types` — must bind against the Roslyn host's own `System.Collections.Immutable` and `System.Reflection.Metadata`. .NET satisfies a reference from a *higher* assembly version but never from a lower one, and those assemblies are supplied by the compiler, so the closure must reference the lowest version across every supported band and must never ship a copy of its own. Roslyn 4.14 and 5.0 both depend on 9.0.0, which is why `$(RoslynRuntimeVersion)` in `roslyn.props` drives the central pin and one build of the non-Roslyn closure serves both bands. Going lower — Roslyn 4.8 wants 7.x — would mean building that whole closure, `Opc.Ua.Types` included, a second time.

> Get it wrong and the failure is silent: the generator is skipped (`CS9057`), fails to load (`CS8032`) or throws `MissingMethodException` while initializing (`CS8784`) — all *warnings*, so the consumer just gets no generated code. `validate-source-generator-packages.ps1` therefore refuses any package that ships `Microsoft.CodeAnalysis*`, `System.Collections.Immutable` or `System.Reflection.Metadata`, and runs the packed down-level payload through a real compiler of that band.

### Versioning

From **2.0** onward, package versions are produced by [Nerdbank.GitVersioning](https://github.com/dotnet/Nerdbank.GitVersioning) (nbgv) from the `version.json` file at the repository root. That file holds the base version (currently `2.0.0-preview.{height}` while `master` is developing the next release) and requests [SemVer 2.0](https://semver.org/) package versions (`nugetPackageVersion.semVer: 2`); nbgv derives the version height, prerelease tag, and build metadata from the git history, and `version.props` maps the computed values onto the assembly and package version properties.

Stable (public-release) versions are produced **only** from a canonical `release/<major>.<minor>` branch (e.g. `release/2.0`, `release/2.1`) — never from `master`, a tag, or any other branch — and only at the exact commit whose `version.json` carries the plain `<major>.<minor>.<patch>` version with no prerelease label (e.g. `2.0.0`). Patch numbers increase by exactly one per release on their line (`2.0.0` → `2.0.1` → `2.0.2`); a new minor line resets the patch to zero (`2.1.0`). See **[Release process](ReleaseProcess.md)** for the full branch/version model and the step-by-step procedure for cutting a release, shipping a patch or minor version, backporting a fix, and promoting a stable candidate.

The XRegistry, WoT Connectivity, Vision, Robotics, Redundancy, Positioning,
OpenUSD, and AI package families remain preview packages even when
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

**GitHub Actions runs all CI for `master`.** [`.github/workflows/buildandtest.yml`](../.github/workflows/buildandtest.yml) runs the complete build and test workload on GitHub-hosted runners for every triggering branch, and [`.github/workflows/nightly.yml`](../.github/workflows/nightly.yml) runs the full-scope workload on the weekly schedule and on demand. [`.github/workflows/nuget-publish.yml`](../.github/workflows/nuget-publish.yml) builds, signs and publishes the packages. The other workflows in [`.github/workflows/`](../.github/workflows) cover CodeQL, container images and the opt-in stress, stability and long-haul suites.

The only Azure Pipelines definition left on `master` is the manually queued, opt-in [`.azurepipelines/onefuzz.yml`](../.azurepipelines/onefuzz.yml), kept because submission to the internal OneFuzz service requires the Azure DevOps-only `onefuzz-task`; it has no push, PR or schedule trigger. The other PowerShell helpers that remain under [`.azurepipelines/`](../.azurepipelines) are used by the GitHub Actions workflows; the directory keeps its historical name so those paths stay stable. Branches that still carry their own `azure-pipelines.yml` (`master378`, the 1.x `release/*` lines and the frozen `release/2.0.0`) keep their Azure validation; see [Azure Pipelines on other branches](#azure-pipelines-on-other-branches).

### What runs where

One table describes the entire migrated workload: `$Profiles` and `$BuildProfiles` in [`.github/scripts/get-ci-matrix.ps1`](../.github/scripts/get-ci-matrix.ps1). Both workflows call that script, so a profile added there appears in both without editing any YAML.

| | Pull request (`buildandtest.yml`) | Full scope (`nightly.yml`) |
| --- | --- | --- |
| Test profiles | Windows net48, Windows/Linux/macOS net10.0 | the above plus net9.0, net8.0, the Debug legs, and the tiers that lift the category filter |
| Solution builds | every `.slnx` on Windows for net48/net10.0 × Debug/Release, `UA.slnx` on Linux for net8.0/net9.0/net10.0 | every `.slnx` on Windows for all four TFMs × Debug/Release, plus the Linux legs |
| Native AoT | linux-x64, osx-x64, osx-arm64 | the above plus win-x64 |
| Coverage | project floors and the graduated patch gate | project floors only |
| Trigger | every push and pull request | weekly schedule and `workflow_dispatch` |

A profile pins its target framework through `CustomTestTarget`, not `--framework`, because that is the mechanism [`targets.props`](../targets.props) uses.

The project fan-out is **batched**: each matrix entry carries several projects that [`.github/scripts/run-dotnet-tests.ps1`](../.github/scripts/run-dotnet-tests.ps1) runs in sequence, keeping per-project results and per-project timeouts. Batches are packed by **duration**, not by count. Every batch pays a fixed cost of several minutes (checkout, SDK install and the cold build of the shared dependency graph), while each further project in it only adds an incremental build of under a minute plus its tests. So projects are packed, in path order, up to `BatchTargetMinutes` (30) of weight from [`.github/ci-test-durations.json`](../.github/ci-test-durations.json), with at most as many projects as the job timeout can budget for. Path order keeps related projects (`PubSub.*`, `Redundancy.*`, `WotCon.*`) together. Packing two projects per job, as before, spent almost half of the pull-request matrix recompiling the same assemblies.

The weights only steer balance, never what runs: a project missing from the table is weighted with its `defaultMinutes`. Each weight is the slowest pull-request profile's test minutes, rounded up, plus one minute of incremental build. The executor records every project's build and test seconds in `batch-summary.json` and in the job summary, so the table can be refreshed from any run:

```bash
gh run download <run-id> --pattern 'dotnet-results-*' --dir ./ci-results
./.github/scripts/update-test-durations.ps1 -ResultsPath ./ci-results -Source 'Run <run-id>'
```

GitHub refuses to start a run whose matrices expand past 256 jobs, and the limit cannot be raised. If the packed matrix would not fit, the target is raised until it does, so the matrix adapts as test projects are added instead of silently truncating. [`CiMatrixScriptTests`](../tests/Opc.Ua.Tools.Tests/CiMatrixScriptTests.cs) asserts that both scopes fit, that every `*.Tests.csproj` on disk is either scheduled or explicitly excluded, that each profile runs each of its projects exactly once, that only a project heavier than the target exceeds it, and that a narrowed matrix can never expand to nothing.

The executor fails a project when its build fails, when the TRX counters report any failure, when a test fixture's setup or teardown fails, when it produces **no** TRX at all, when nothing in it actually ran, or when it outlives its per-project ceiling. A "no tests ran" outcome is a failure, not a pass — and that includes a suite whose tests were *all* skipped, which records a non-zero total while verifying nothing.

That per-project ceiling is a **single combined budget** covering the project's build *and* its test run, measured by one stopwatch. The matrix derives each job's `timeout-minutes` from it as `20 + projectCount × perProjectTimeout`, so spending it twice per project would let a batch outlive its job: GitHub would cancel the run, and a cancelled job produces neither the executor's per-project annotation nor its results. A batch whose budgets add up past the job ceiling is an error in `get-ci-matrix.ps1` rather than a clamped `timeout-minutes`, for the same reason. `CiMatrixScriptTests` pins both halves of that arithmetic.

The verdict comes from the emitted TRX rather than from the `dotnet test` exit code, and lives in [`.github/scripts/get-test-verdict.ps1`](../.github/scripts/get-test-verdict.ps1) so it can be tested on its own — see [`CiTestVerdictTests`](../tests/Opc.Ua.Tools.Tests/CiTestVerdictTests.cs). A failed fixture setup or teardown, such as the assembly-level certificate leak check in a `[OneTimeTearDown]`, is not a test result, so no counter records it: the NUnit adapter writes it to the TRX as a run message (`TearDown failed for test fixture …`), and the verdict rejects it whatever the exit code says. A non-zero exit is tolerated when, and only when, the results record at least one **passing** test, no failure, error, timeout, abort or `passedButRunAborted`, and no failed fixture setup or teardown. That combination means the host died during process **exit**, after the last test and every teardown had already run; failing it would report a false red. It is not a macOS quirk — Windows hosts do it too (run 35714133848, `test-windows-net48 (5/30)`: `Opc.Ua.Client.Tests` reported 256 passed, 0 failed, host exit 1). A host that dies mid-run leaves a non-zero counter and is still rejected, and a run in which every test was skipped is rejected whatever the exit code says. Every tolerated run raises a warning annotation and is labelled in the job summary, so a host that keeps dying stays visible.

#### Why a project can be skipped

`RestrictForLegacyTfm` in [`targets.props`](../targets.props) turns a project that does not support the requested `CustomTestTarget` into an empty shell with `IsTestProject=false`. Running `dotnet test` against one of those produces no TRX, which the executor treats as a failure — so it first probes `dotnet msbuild -getProperty:IsTestProject` and records the project as **not applicable** instead. Those rows appear in the job summary, so a project that quietly stops being applicable everywhere is visible rather than invisible.

### Test tiers

The pull-request profiles filter out `TestCategory=LongRunning` and `TestCategory=Stress`.

Linux UaLens tests require the agent's installed `xvfb-run` and run under a virtual X server. The shared
GitHub batch executor retains the normal test selection, coverage, diagnostics,
and result gates, and fails explicitly if Xvfb is unavailable. The GitHub executor includes the virtual display
in the existing per-project timeout and process-tree cleanup.

The tiers those filters leave out run elsewhere:

| Tier | Where it runs |
| --- | --- |
| `LongRunning` categories in mainline projects | `linux-long-running` profile, `nightly.yml` only |
| `Opc.Ua.Subscriptions.Durable.Tests` | `windows-durable` / `linux-durable` profiles, `nightly.yml` only |
| `Opc.Ua.Stress.Tests` (`ChaosTCP`) | [`.github/workflows/stress-test.yml`](../.github/workflows/stress-test.yml), opt-in |
| `ConnectionStability` in `Opc.Ua.Sessions.Tests` | [`.github/workflows/stability-test.yml`](../.github/workflows/stability-test.yml), opt-in |
| `SampleHaLongHaul` in `Opc.Ua.Redundancy.Samples.Tests` | [`.github/workflows/sample-ha-longhaul.yml`](../.github/workflows/sample-ha-longhaul.yml), opt-in |
| `Opc.Ua.Aot.Tests` and the `.Historian` / `.Mcp` companions | `aot-test` job (both workflows) — published and run as native executables, not through `dotnet test` |
| `Opc.Ua.OneFuzz.Validator.Tests` | `fuzz-drop` job — it pins net10.0 to match the drop it validates |

The opt-in workflows run on their schedule, on demand from the Actions tab, and on a pull request that carries their label. Like `interop` for the [foreign stack interop](../.github/workflows/interop-foreign.yml) run, the label is not a required check: add it to start a run, and each later push to the pull request restarts it, cancelling the run in progress. Adding an unrelated label does not restart it.

| Label | Workflows | Pull-request run |
| --- | --- | --- |
| `stress` | Stress Tests | ChaosTCP with a random seed (in `chaos-seed.txt` of the results artifact) |
| `soak` | Connection Stability Test, Sample HA Long-Haul Test | 30 minutes stability; 15 minutes per HA scenario (the schedules run 90 and 60) |
| `interop` | Foreign stack interop | Every peer, as nightly |

Each opt-in workflow fails when its filter matched no test, so a moved or renamed test cannot leave it green while testing nothing.

### Running the full scope

`nightly.yml` runs weekly at Sunday 02:00 UTC. You can also start it from the Actions tab (or `gh workflow run nightly.yml`) with:

| Input | Effect |
| --- | --- |
| `include_macos` | Include the macOS profiles (default `true`) |

It uploads a `workload-manifest` artifact recording every (project × profile) tuple the run intended to cover, so a run's scope can be compared against another inventory instead of inferred from job names.

#### The fuzz crash corpus

The checked-in corpus under [`fuzzing/`](../fuzzing) runs on every pull request. The full **crash corpus** — about 22k inputs from earlier fuzzing campaigns, formerly the Azure secure file `FuzzingArtifacts.zip` — lives on the orphan branch `fuzz-corpus` and takes too long to replay per pull request. The `crash-corpus` job in `nightly.yml` checks out a commit of that branch pinned in the workflow (`FUZZ_CORPUS_COMMIT`), overlays it onto the matching `fuzzing/` projects and runs `Opc.Ua.Encoders.Fuzz.Tests` on every non-macOS profile. It needs no secrets, and the nightly summary fails if it was skipped. See [`fuzzing/CrashCorpus.md`](../fuzzing/CrashCorpus.md) for how to add inputs and update the pin.

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

[`.github/workflows/docker-image.yml`](../.github/workflows/docker-image.yml) (`Images CI`) follows the identical pattern for the sample container images. Its build legs are named `build-and-push-image (refserver)`, `(boilerserver)` and so on, so they cannot be pinned in a ruleset either; **`images summary`** is the fixed name that rolls all of them up. One broken image fails it, because a matrix job aggregates to `success` only when every leg succeeded. Its `pull_request` trigger carries the same branch list as the `CI` workflow and no `paths-ignore`, so both gates report on exactly the same set of pull requests; the docs/tests exclusion moved into its own `discover` job. A `publish-samples` job compiles every sample once, natively, and the image legs only copy that output into the multi-platform images. See [Container support](ContainerReferenceServer.md#other-published-sample-images). Add `images summary` to the ruleset alongside `build-and-test summary`.

The coverage check reports a clean failure when the thresholds are missed, so a miss is visible on the pull request, but it never blocks the merge. Do not add it to the ruleset — that would make a coverage dip unmergeable, which is not the intent.

> `build-and-test summary` is the only required check on `master`.

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

> The script lives under `.azurepipelines/` for historical reasons only; it is not Azure-specific.

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

The upload carries the `actions` flag. It is optional and never fails a build: turn it off with the `ENABLE_CODECOV` workflow `env` (default `'true'`); it is also skipped when the `CODECOV_TOKEN` secret is unavailable, as on fork pull requests.

Keep the `ignore` list in `codecov.yml` in step with the one in `coverage-thresholds.json`, or the two will report on different code.

#### Where the numbers appear

The script renders a markdown summary, so you never have to open a raw log to see why coverage moved. It is appended to the run's job summary and posted as a single sticky pull-request comment that is updated in place on each run. Threshold misses additionally appear as run annotations. On a pull request **from a fork or Dependabot** the token is read-only, so the comment is skipped and only the job summary is written. The merged HTML report is published as a `coverage-report` artifact.

> A full-scope `nightly.yml` run reports different numbers from a pull-request run, and that is expected: it merges a different set of profiles.

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

### Pull requests from outside contributors

GitHub Actions applies its "Approve and run workflows" gate to outside contributors and to the **GitHub Copilot coding agent**: a maintainer reviews the change and then approves the workflow run.

### Azure Pipelines on other branches

`master` no longer carries `azure-pipelines.yml`, `azure-pipelines-preview.yml` or the Azure CI templates, so a push to `master` (or to a branch cut from it) gives those Azure definitions nothing to run. The OneFuzz definition, which points at `.azurepipelines/onefuzz.yml`, is unaffected and stays manual. The Azure DevOps definitions themselves live in the Azure DevOps portal, not in this repository. Disabling their `master` triggers there (definition 14's push trigger and service-side schedule, definition 16's build-completion trigger, and the stale definition 13) is an administrator task and stops any residual "file not found" runs. Preserve the definitions, their artifacts, feeds, secure files, pools and service connections — `master378`, the 1.x `release/*` lines and the frozen `release/2.0.0` keep their own copy of `azure-pipelines.yml` and still use them.

> **Before cutting a canonical `release/2.<minor>` branch:** the `Release` ruleset requires `OPCFoundation.UA-.NETStandard` for *every* `refs/heads/release/*`. A new 2.x branch is cut from `master` and has no Azure pipeline, so that Azure context would never report and would block every pull request into the new line. Exclude the new ref from that ruleset — or add a 2.x ruleset requiring `build-and-test summary` — as part of creating the branch. The existing 1.x lines and the frozen `release/2.0.0` keep their own copy of `azure-pipelines.yml` and must keep the Azure requirement.


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
