# Native AOT

[Native AOT](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
(ahead-of-time) compilation publishes an application and the parts of the SDK
that it uses as a single native executable. The executable starts quickly and
runs without an installed .NET runtime, but it supports only code that the
compiler can analyze at publish time. This guide first shows how to publish an
application, then describes the test harness that verifies the stack under
Native AOT.

## Contents

- [Publish an application](#publish-an-application)
  - [Enable Native AOT](#enable-native-aot)
  - [Publish and verify the executable](#publish-and-verify-the-executable)
  - [Use AOT-compatible features](#use-aot-compatible-features)
- [Test harness overview](#test-harness-overview)
- [Prerequisites](#prerequisites)
  - [.NET SDK](#net-sdk)
  - [Platform-Specific Native Toolchain](#platform-specific-native-toolchain)
- [Project Structure](#project-structure)
  - [Why TUnit Instead of NUnit?](#why-tunit-instead-of-nunit)
  - [Test Fixture Pattern](#test-fixture-pattern)
- [How to Build and Run](#how-to-build-and-run)
  - [Publish the Native AOT Binary](#1-publish-the-native-aot-binary)
  - [Run the Tests](#2-run-the-tests)
  - [Build + Run in a Single Step (Development)](#build--run-in-a-single-step-development)
- [CI Integration](#ci-integration)
- [Writing New AOT Tests](#writing-new-aot-tests)
  - [Choose or Create a Test Class](#1-choose-or-create-a-test-class)
  - [Use TUnit Attributes and Assertions](#2-use-tunit-attributes-and-assertions)
  - [Keep Code AOT-Compatible](#3-keep-code-aot-compatible)
  - [Handle Trimming Warnings](#4-handle-trimming-warnings)
- [Troubleshooting](#troubleshooting)
  - [`'vswhere.exe' is not recognized` on Windows](#vswhereexe-is-not-recognized-on-windows)
  - [Publish Fails with Linker Errors](#publish-fails-with-linker-errors)
  - [`TypeInitializationException` or `MissingMetadataException` at Runtime](#typeinitializationexception-or-missingmetadataexception-at-runtime)
  - [Tests Pass Under `dotnet test` but Fail Under AOT](#tests-pass-under-dotnet-test-but-fail-under-aot)
  - [Slow Publish Times](#slow-publish-times)
  - [`IL2104` or Other Trimming Warnings](#il2104-or-other-trimming-warnings)

## Publish an application

### Enable Native AOT

Install the [native toolchain](#platform-specific-native-toolchain) for your
platform. Then set `PublishAot` in the application's project file:

```xml
<PropertyGroup>
  <OutputType>Exe</OutputType>
  <TargetFramework>net10.0</TargetFramework>
  <PublishAot>true</PublishAot>
</PropertyGroup>
```

The stack's Native AOT tests run on `net10.0`. `dotnet build` and `dotnet run`
still use the JIT compiler; only `dotnet publish` compiles the native
executable.

### Publish and verify the executable

Publish for the runtime identifier of the target platform, such as `win-x64`,
`linux-x64`, `linux-arm64`, or `osx-arm64`:

```bash
dotnet publish -c Release -r win-x64
```

The `bin/Release/net10.0/<runtime-identifier>/publish` folder then contains one
native executable and its symbol file. If the folder instead contains the
application's `.dll` and `coreclr.dll`, the publish did not use Native AOT.
Check that `PublishAot` applies to the target framework you publish, delete the
`bin` and `obj` folders, and publish again.

The client and server from
[Getting started](GettingStarted.md#route-b-build-applications-from-nuget-packages)
publish this way without trim or AOT warnings. A published application is also
a new instance for its certificate stores when `PkiRoot` depends on the working
directory, as in Getting started. Run it from the folder that contains its
`pki` folder, or trust its new certificate again.

### Use AOT-compatible features

Fix every trim and AOT warning (`IL2026`, `IL3050`, and related codes) that the
publish reports for your code. The following features are AOT-compatible:

- Source-generated models and node managers; see
  [Node managers: NativeAOT publishing](NodeManagers.md#nativeaot-publishing).
- Source-generated data types; see
  [Source-generated data types](SourceGeneratedDataTypes.md).
- The default complex-type builder. The optional Reflection.Emit builder
  requires the JIT compiler; see [Complex types: type builders](ComplexTypes.md#type-builders).
- Hosting with dependency injection; see
  [Dependency injection: Native AOT](DependencyInjection.md#native-aot).
- PubSub; see [PubSub: Native AOT](PubSub.md#native-aot).
- The crypto provider model; see [Crypto providers](CryptoProvider.md).

In your own code, avoid unbounded reflection and runtime code generation; see
[Keep code AOT-compatible](#3-keep-code-aot-compatible).

Do not enable globalization-invariant mode for a server, a common size
optimization for native executables. With the `InvariantGlobalization` property
or the `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT` environment variable set, the
server does not start: it reports a `CultureNotFoundException` for the `en-US`
culture. Clients run in this mode.

## Test harness overview

The **Opc.Ua.Aot.Tests** project verifies that the core OPC UA libraries work
correctly when published as a Native AOT binary. The tests cover:

- Encoding, sessions, subscriptions, and monitored items.
- Discovery, security, events, and history.
- Diagnostics, batch operations, and the node cache.
- Complex types, GDS client operations, and client sample patterns.

All tests run inside a single ahead-of-time compiled executable.

The crypto provider model is AOT-compatible and covered by
`CryptoProviderAotTests`; see [CryptoProvider](CryptoProvider.md). The optional
`OPCFoundation.NetStandard.Opc.Ua.Security.Pkcs11` package is covered too, by
`Pkcs11AotTests`: although `Pkcs11Interop` carries no trim or AOT annotations of
its own, the ILCompiler analysis produces no warnings for the paths the package
uses, so it is marked `IsAotCompatible` on `net10.0`. Resolving the token module
is native interop, which trimming does not affect.

## Prerequisites

### .NET SDK

- **.NET 10.0 SDK** (or later LTS) is required.

### Platform-Specific Native Toolchain

Native AOT compilation requires a C/C++ toolchain on the build machine.

| Platform | Requirement |
|----------|-------------|
| **Windows** | Visual Studio 2022+ with the **Desktop development with C++** workload, or the equivalent Build Tools package. |
| **Linux** | `clang` and `zlib1g-dev` (Debian/Ubuntu) or the equivalent packages for your distribution. |
| **macOS** | Xcode Command Line Tools (`xcode-select --install`). |

See the official Microsoft documentation for full details:
<https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/?tabs=windows%2Cnet8plus>

## Project Structure

```
tests/Opc.Ua.Aot.Tests/
├── Opc.Ua.Aot.Tests.csproj   # Project file (PublishAot=true, net10.0)
├── AotServerFixture.cs        # Lightweight AOT-compatible server host
├── AotTestFixture.cs          # Shared fixture: starts server + client session
├── GdsTestFixture.cs          # GDS-specific test fixture
├── EncodingAotTests.cs        # Binary & JSON encoding round-trips
├── DataTypeAotTests.cs        # OPC UA data type verification
├── SessionAotTests.cs         # Session lifecycle & reconnect
├── SubscriptionAotTests.cs    # Subscription create / modify / delete
├── MonitoredItemAotTests.cs   # Monitored item operations
├── DiscoveryAotTests.cs       # Endpoint & server discovery
├── SecurityAotTests.cs        # Security policy negotiation
├── EventsAotTests.cs          # Event subscription & filtering
├── HistoryAotTests.cs         # Historical read operations
├── DiagnosticsAotTests.cs     # Server diagnostics
├── BatchOperationsAotTests.cs # Batch read / write / call
├── NodeCacheAotTests.cs       # Client-side node cache
├── ComplexTypeAotTests.cs     # Complex type loading & serialization
├── GdsClientAotTests.cs       # Global Discovery Server client
├── ClientSamplesAotTests.cs   # End-to-end client sample patterns
├── AotClientSamples.cs        # Helper methods for client samples
├── PubSubAotTests.cs          # Part 14 PubSub publisher / subscriber round-trips
├── WebApiAotFixture.cs        # Kestrel host for REST endpoint round-trips
├── WebApiAotTests.cs          # REST binding (Opc.Ua.Bindings.Https) smoke + Basic auth
└── StubWebApiServer.cs        # IWebApiServer stub backing WebApiAotFixture
```

### Why TUnit Instead of NUnit?

The project uses the [TUnit](https://tunit.dev/) test framework instead of
NUnit. TUnit relies on **source generation** rather than runtime reflection for
test discovery, which makes it fully compatible with Native AOT and the IL
trimmer. NUnit (and most traditional .NET test frameworks) depend heavily on
reflection, which is not supported in trimmed / AOT-published applications.

### Test Fixture Pattern

`AotTestFixture` implements `IAsyncInitializer` and `IAsyncDisposable` from
TUnit. It is shared across all test classes via the attribute:

```csharp
[ClassDataSource<AotTestFixture>(Shared = SharedType.PerTestSession)]
```

On initialization the fixture:

1. Starts a `ReferenceServer` in-process using `AotServerFixture<T>`.
2. Creates a client `ApplicationConfiguration` programmatically.
3. Connects an `ISession` to the server over `opc.tcp`.

Every test class receives the fixture through its primary constructor and reuses
the same server and session, keeping the test suite fast.

`AotServerFixture<T>` is a cut-down, AOT-compatible version of
`ServerFixture<T>` from the NUnit test infrastructure. It avoids transitive
references to BenchmarkDotNet, Moq, and other packages that are not
AOT-friendly.

## How to Build and Run

### 1. Publish the Native AOT Binary

```bash
dotnet publish tests/Opc.Ua.Aot.Tests/Opc.Ua.Aot.Tests.csproj --configuration Release
```

The publish step compiles the entire application (tests, server, and all
referenced OPC UA libraries) into a single native executable. This can take
several minutes depending on the machine.

### 2. Run the Tests

**Windows (x64):**

```powershell
./tests/Opc.Ua.Aot.Tests/bin/Release/net10.0/win-x64/publish/Opc.Ua.Aot.Tests.exe
```

**Linux (x64):**

```bash
./tests/Opc.Ua.Aot.Tests/bin/Release/net10.0/linux-x64/publish/Opc.Ua.Aot.Tests
```

The executable discovers and runs all tests, producing TUnit console output and
writing results to a `TestResults` directory.

### Build + Run in a Single Step (Development)

For iterative development you can combine both commands:

```bash
# Windows
dotnet publish tests/Opc.Ua.Aot.Tests/Opc.Ua.Aot.Tests.csproj -c Release && ^
  tests\Opc.Ua.Aot.Tests\bin\Release\net10.0\win-x64\publish\Opc.Ua.Aot.Tests.exe

# Linux / macOS
dotnet publish tests/Opc.Ua.Aot.Tests/Opc.Ua.Aot.Tests.csproj -c Release && \
  ./tests/Opc.Ua.Aot.Tests/bin/Release/net10.0/linux-x64/publish/Opc.Ua.Aot.Tests
```

> **Note:** `dotnet test` and `dotnet run` do **not** perform AOT compilation.
> You must use `dotnet publish` followed by direct execution of the resulting
> binary.

## CI Integration

The GitHub Actions workflow `.github/workflows/buildandtest.yml` runs AOT
jobs on Ubuntu and both Intel and ARM64 macOS. Azure's `Test Native AoT`
matrix covers Windows. Each platform performs these steps:

1. **Checkout** the repository.
2. **Setup** .NET 10.0 SDK.
3. **Publish** the project with `dotnet publish` in `Release` configuration.
4. **Execute** the platform-specific binary directly.
5. **Publish + execute** the `.Historian` and `.Mcp` companions the same way,
   keeping results separate.
6. **Upload** any `TestResults` artifacts.

The job runs in a separate matrix from the main `dotnet test` build so that AOT
failures are isolated and clearly visible.

## Writing New AOT Tests

### 1. Choose or Create a Test Class

Place AOT tests in `tests/Opc.Ua.Aot.Tests/`. Each file should focus on a
single area (encoding, sessions, etc.). Apply the shared fixture:

```csharp
[ClassDataSource<AotTestFixture>(Shared = SharedType.PerTestSession)]
public class MyFeatureAotTests(AotTestFixture fixture)
{
    [Test]
    public async Task MyTestAsync()
    {
        // Use fixture.Session, fixture.ServerUrl, etc.
        await Assert.That(fixture.Session.Connected).IsTrue();
    }
}
```

### 2. Use TUnit Attributes and Assertions

- Mark tests with `[Test]` (not NUnit's `[Test]`—they are different types).
- Use `await Assert.That(…)` for assertions. TUnit assertions are async.
- Do **not** reference NUnit, xUnit, or MSTest assemblies.

### 3. Keep Code AOT-Compatible

- **Avoid unbounded reflection.** Do not use `Type.GetType()`,
  `Activator.CreateInstance()`, or similar APIs unless the types are statically
  reachable.
- **Avoid dynamic code generation.** `Reflection.Emit`,
  `System.Linq.Expressions.Expression.Compile()`, and similar APIs are not
  supported.
- **Prefer concrete generic instantiations.** The trimmer must see every generic
  type combination at compile time.
- **Annotate when necessary.** Use `[DynamicallyAccessedMembers]` or
  `[RequiresUnreferencedCode]` attributes to preserve metadata the trimmer would
  otherwise remove.

### 4. Handle Trimming Warnings

The project suppresses `IL2104` (see the `.csproj`), which comes from
third-party packages that are not yet trim-annotated. For warnings in your own
code, fix the root cause rather than suppressing.

## Troubleshooting

### `'vswhere.exe' is not recognized` on Windows

The native link step locates the Visual C++ tools with `vswhere.exe`. If the
publish fails with `'vswhere.exe' is not recognized as an internal or external
command`, add the Visual Studio Installer folder to `PATH` and publish again:

```powershell
$env:PATH = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer;$env:PATH"
dotnet publish -c Release -r win-x64
```

### Publish Fails with Linker Errors

Ensure the platform-specific C/C++ toolchain is installed (see
[Prerequisites](#prerequisites)). On Windows, verify the **Desktop development
with C++** workload is present in the Visual Studio Installer.

### `TypeInitializationException` or `MissingMetadataException` at Runtime

The IL trimmer removed type metadata that is needed at runtime. Common fixes:

- Add `[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]` to
  parameters or fields that hold types accessed via reflection.
- Add an explicit `rd.xml` file or `[DynamicDependency]` attribute to preserve
  specific types.
- Ensure the type is statically referenced somewhere in the code path.

### Tests Pass Under `dotnet test` but Fail Under AOT

`dotnet test` runs with JIT and full reflection. Some APIs silently work under
JIT but are unsupported in AOT. Compare the stack trace from the AOT binary to
identify which API is problematic, then refactor to a trim-safe alternative.

### Slow Publish Times

Native AOT compilation is inherently slower than JIT builds because it performs
whole-program optimization. On CI this is expected. For local development,
consider running the NUnit tests with `dotnet test` for fast feedback, and
reserve AOT publish for final validation.

### `IL2104` or Other Trimming Warnings

Warnings prefixed with `IL` come from the IL trimmer/linker. The project
suppresses `IL2104` for third-party packages. If you see new warnings from OPC
UA code, investigate and fix the root cause. Suppress only as a last resort and
document the reason in a code comment.
