# Native AOT Testing

## Overview

The OPC UA .NET Standard stack supports
[Native AOT (Ahead-of-Time) compilation](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/?tabs=windows%2Cnet8plus),
which produces a self-contained executable that is compiled to native code at
publish time rather than at run time. This eliminates the need for a JIT
compiler and the .NET runtime on the target machine, resulting in faster startup
and a smaller deployment footprint.

The **Opc.Ua.Aot.Tests** project verifies that the core OPC UA libraries work
correctly when published as a Native AOT binary. The tests exercise encoding,
sessions, subscriptions, monitored items, discovery, security, events, history,
diagnostics, batch operations, node cache, complex types, GDS client operations,
and client sample patterns — all running inside a single ahead-of-time compiled
executable.

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
Tests/Opc.Ua.Aot.Tests/
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
└── AotClientSamples.cs        # Helper methods for client samples
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
dotnet publish Tests/Opc.Ua.Aot.Tests/Opc.Ua.Aot.Tests.csproj --configuration Release
```

The publish step compiles the entire application (tests, server, and all
referenced OPC UA libraries) into a single native executable. This can take
several minutes depending on the machine.

### 2. Run the Tests

**Windows (x64):**

```powershell
./Tests/Opc.Ua.Aot.Tests/bin/Release/net10.0/win-x64/publish/Opc.Ua.Aot.Tests.exe
```

**Linux (x64):**

```bash
./Tests/Opc.Ua.Aot.Tests/bin/Release/net10.0/linux-x64/publish/Opc.Ua.Aot.Tests
```

The executable discovers and runs all tests, producing TUnit console output and
writing results to a `TestResults` directory.

### Build + Run in a Single Step (Development)

For iterative development you can combine both commands:

```bash
# Windows
dotnet publish Tests/Opc.Ua.Aot.Tests/Opc.Ua.Aot.Tests.csproj -c Release && ^
  Tests\Opc.Ua.Aot.Tests\bin\Release\net10.0\win-x64\publish\Opc.Ua.Aot.Tests.exe

# Linux / macOS
dotnet publish Tests/Opc.Ua.Aot.Tests/Opc.Ua.Aot.Tests.csproj -c Release && \
  ./Tests/Opc.Ua.Aot.Tests/bin/Release/net10.0/linux-x64/publish/Opc.Ua.Aot.Tests
```

> **Note:** `dotnet test` and `dotnet run` do **not** perform AOT compilation.
> You must use `dotnet publish` followed by direct execution of the resulting
> binary.

## CI Integration

The GitHub Actions workflow `.github/workflows/buildandtest.yml` defines an
`aot-test` job that runs on both `ubuntu-latest` and `windows-latest`. The
steps are:

1. **Checkout** the repository.
2. **Setup** .NET 10.0 SDK.
3. **Publish** the project with `dotnet publish` in `Release` configuration.
4. **Execute** the platform-specific binary directly.
5. **Upload** any `TestResults` artifacts.

The job runs in a separate matrix from the main `dotnet test` build so that AOT
failures are isolated and clearly visible.

## Writing New AOT Tests

### 1. Choose or Create a Test Class

Place AOT tests in `Tests/Opc.Ua.Aot.Tests/`. Each file should focus on a
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
