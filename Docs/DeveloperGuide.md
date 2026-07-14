# Developer Guide

This guide is the starting point for contributing to the OPC UA .NET Standard stack. It covers what to install, how to build and test, the coding standards ("dos and don'ts"), and task-oriented "how to" recipes (starting with how to add logging). It links out to the topic-specific documents in [Docs/README.md](README.md) rather than repeating them.

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
| `Stack/` | The core stack: `Opc.Ua.Types`, `Opc.Ua.Core.Types`, `Opc.Ua.Core.Schema`, `Opc.Ua.Core`, `Opc.Ua.Core.Diagnostics`, `Opc.Ua.Security.Certificates`, and `Opc.Ua.Bindings.Https`. |
| `Libraries/` | Higher-level libraries: `Opc.Ua.Client`, `Opc.Ua.Server`, `Opc.Ua.Configuration`, `Opc.Ua.PubSub` (+ transports), the GDS / DI / LDS / WoT libraries, and the `Opc.Ua.Redundancy*` family. |
| `Applications/` | Reference and sample apps: `ConsoleReferenceServer`, `ConsoleReferenceClient`, `Quickstarts.Servers`, the `Minimal*` / `PumpDeviceIntegrationServer` NativeAOT samples, `McpServer`, `Redundant*`, etc. |
| `Tests/` | Unit and integration test projects, mirroring the library structure, plus shared test frameworks. |
| `Tools/` | Build-time tooling, in particular the OPC UA source generators. |
| `Docs/` | This documentation set (indexed by [Docs/README.md](README.md)). |
| `Fuzzing/` | SharpFuzz / libFuzzer fuzz targets (see [Fuzzing.md](../Fuzzing/Fuzzing.md)). |

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
  dotnet build Stack/Opc.Ua.Core/Opc.Ua.Core.csproj -f net10.0 -p:CustomTargetFrameworks=net10.0
  ```

- **Offline / restricted networks.** `NuGetAudit` is enabled and fails the build with `NU1900` when it cannot reach the audit service. If you build offline, pass `-p:NuGetAudit=false`.

## Running tests

Run the whole suite from the solution:

```bash
dotnet test UA.slnx
```

Conventions and requirements:

- **Frameworks.** Test projects use either **NUnit** (with `Assert.That` assertions and **Moq** for mocking) or **TUnit** (with its own assertions and mock helpers). Do not mix the two in one project, and do not use the classic NUnit asserts (`Assert.AreEqual`, …).
- **Coverage.** Coverage is measured with **Coverlet** and must not regress; every non-application, non-test project should stay at or above **80 %**.
- **Before a pull request** the `UA.slnx` suite must pass on at least **.NET Framework 4.8** and **.NET 10.0**.
- **Testing a specific target framework.** The libraries multi-target, but the test executables run on one framework at a time. To run the suite against a non-default framework, set `CustomTestTarget` (supported values: `netstandard2.0`, `netstandard2.1`, `net472`, `net48`, `net8.0`, `net9.0`, `net10.0`). The batch file [`Tests/customtest.bat`](../Tests/customtest.bat) cleans, restores, and runs the tests for a chosen target; in Visual Studio, uncomment and set the `CustomTestTarget` property in [`targets.props`](../targets.props). A clean build for the target is recommended when switching.
- **CI matrix.** To keep pull-request builds fast, only **net48** and **net8.0** are exercised in the qualifying CI build; the other frameworks run in scheduled/manual CI. Fix all failing, flaky, and CodeQL findings in the pipelines.

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
- [ ] `EventId` uses the project's `<AssemblyToken>EventIds` offset (or a literal range for a shared/linked file).
- [ ] When testing with a mocked `ILogger`, stub `IsEnabled(...) => true` and match on `EventId.Name`, not the (empty) source-generated state `ToString()`.

### Other common tasks

- **Add a new feature** — implement it in the right library, add unit and (for client/server/pubsub) integration tests, update or add a doc under `Docs/`, and keep backward compatibility (see [Coding standards](#coding-standards-dos-and-donts)).
- **Add a document** — put it in `Docs/` and link it from [Docs/README.md](README.md).
- **Add a dependency** — declare the version in `Directory.Packages.props` (Central Package Management), prefer AOT/trimmable and permissively licensed packages, and get maintainer approval first.
- **Certificates and secrets** — see [Certificates.md](Certificates.md) and [CertificateManager.md](CertificateManager.md).
- **Source-generated node managers / data types** — see [SourceGeneratedNodeManagers.md](SourceGeneratedNodeManagers.md) and [SourceGeneratedDataTypes.md](SourceGeneratedDataTypes.md).
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

For improved source-level debugging, symbol packages are published on nuget.org in `snupkg` format, and `Debug`-compiled packages are available with a `.Debug` suffix. In addition, every successful `master` build publishes preview packages to the [Azure DevOps preview feed](https://opcfoundation.visualstudio.com/opcua-netstandard/_artifacts/feed/opcua-preview).

### Supported target frameworks

The class libraries currently target:

1. .NET Standard 2.0 (`Opc.Ua.Types` only)
2. .NET Standard 2.1
3. .NET Framework 4.7.2 (limited support)
4. .NET Framework 4.8
5. .NET 8.0
6. .NET 9.0
7. .NET 10.0

To keep pull-request CI fast, only (4) and (6) are part of the qualifying build; the other platforms are covered by scheduled or manual CI. See [Running tests](#running-tests) for how to build and test a specific framework locally with `CustomTestTarget` / `Tests/customtest.bat`.

### Versioning

The NuGet version scheme is intentionally **not** [SemVer](https://semver.org/), because of constraints inherited from the OPC UA specification:

- The first two digits are the spec version of the embedded NodeSet (`1.3.x.x` → spec V1.03, `1.4.x.x` → V1.04, `1.5.x.x` → V1.05). The spec is backward compatible, so a library built on a newer NodeSet can still be used to certify an application against an older certification test.
- The next digits are the **API level** (mapped to a release branch such as `release/1.4.372`, corresponding to `MAJOR`/breaking changes) and the **build number** (a mix of `MINOR`/`PATCH`). An API level stays internally consistent — it should not receive breaking changes that would require application code changes — while build updates may add internal improvements or non-breaking API extensions. Hotfixes are cherry-picked onto release branches.

## Contributing and pull requests

- Fork the repository (or, if you have write access, push a branch prefixed with your username) and open a pull request. You must agree to the [Contributor License Agreement](https://opcfoundation.org/license/cla/ContributorLicenseAgreementv1.0.pdf); the "I AGREE" prompt appears automatically on your first PR. See [CONTRIBUTING.md](../CONTRIBUTING.md).
- Before submitting: all tests pass, code analysis is clean (no new warnings), the change keeps backward compatibility, and security implications are reviewed.
- The pull-request template asks you to confirm the CLA, added tests/coverage, documentation, a warning-free build, that the `UA.slnx` suite passed on **.NET Framework 4.8** and **.NET 10.0**, and that CI and CodeQL are green.
- You can run the `opc-ua-codestyle-enforcer` agent to drive analyzer warnings to zero before opening the PR.

## Related documentation

- [Documentation index](README.md) — all topic guides.
- [Diagnostics](Diagnostics.md) — telemetry context, logging runtime, metrics, audit events, server diagnostics nodes, and packet capture.
- [Dependency Injection](DependencyInjection.md), [Certificates](Certificates.md) / [Certificate Manager](CertificateManager.md), [NativeAOT](NativeAoT.md), [Migration Guide](MigrationGuide.md), [What's New in 2.0](WhatsNewIn2.0.md).
- [Fuzz testing](../Fuzzing/Fuzzing.md).
