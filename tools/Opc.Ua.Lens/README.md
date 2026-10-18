# UaLens

This document covers contributor setup, packaging and artifact qualification.
Use the [user guide](../../docs/UaLens.md) for application workflows, connection
settings and capability limits, or the [package README](NugetREADME.md) for
installing the managed tool.

## Run from source

Follow [Getting started](../../docs/UaLens.md#getting-started) to launch the
application from a checkout. Repository prerequisites and the standard test
commands are in the [Developer Guide](../../docs/DeveloperGuide.md#testing-ualens).

### Aspire AppHost

To start the pump Device Integration sample and UaLens together, with Lens
automatically connected over the local `None/None` development endpoint:

```powershell
$env:CustomTestTarget = 'net10.0'
dotnet run --project tools/Opc.Ua.Lens.AppHost/Opc.Ua.Lens.AppHost.csproj
```

The AppHost is included in `UA.slnx`, but is not packaged or included in the
installed `ualens` tool. It targets .NET 10 and selects the same framework for its
project resources without overriding their global build properties. This lets
solution and AppHost builds share one dependency graph instead of compiling
shared libraries concurrently into the same output files.

Override AppHost settings with standard configuration environment variables,
for example `UaLens__SecurityMode`, `UaLens__SecurityPolicy`,
`UaLens__Username`, and `UaLens__Password`. Do not put passwords in
`appsettings.json`. For direct application startup without Aspire, see
[Automatic connection](../../docs/UaLens.md#automatic-connection).

## Publish and package

The [artifact workflow](../../.github/workflows/ualens-artifacts.yml) builds all
three desktop TFMs and validates NativeAOT executables and installed managed
tools on these runners:

| Runner | Native RID | Native executable | Managed tool shim | Desktop prerequisite |
| --- | --- | --- | --- | --- |
| Windows Server 2022, x64 | `win-x64` | `UaLens.exe` | `ualens.exe` | Interactive Windows desktop; Visual Studio C++ desktop build tools for NativeAOT |
| Ubuntu 24.04, x64 | `linux-x64` | `UaLens` | `ualens` | X11 or Xvfb, Xauthority, Fontconfig/FreeType and X11 libraries; Clang and zlib headers for NativeAOT |
| macOS 15, Arm64 | `osx-arm64` | `UaLens` | `ualens` | Logged-in Cocoa desktop and Xcode command-line tools |

These are validation targets, not a record of passing runs. A platform is
validated for a revision only after **both** artifact jobs pass on that revision.
Other RIDs, Linux distributions, macOS Intel, signing/notarization, and installer
integration are not covered by this matrix. The managed package requires a
.NET 10 runtime; the native artifact does not.

All jobs install the exact .NET SDK pinned in [`global.json`](../../global.json)
and restore their dependencies from nuget.org. Artifact checks use NUnit in
`Opc.Ua.Lens.Artifacts.Tests`; the test project does not reference the application.
PowerShell 7 is used by the workflow and the process-runner regression fixtures.
Linux prerequisites are installed explicitly
in the workflow. The Windows and macOS jobs use their hosted runner's compiler
and desktop; do not substitute a service-only runner or a headless Avalonia backend.

The public OpenUSD Linux packages require their native-library directories on
`LD_LIBRARY_PATH` before process startup. The NUnit artifact launcher supplies
the publish directories or the installed tool's `.store` payload directory.
Direct launches need the same configuration; for a native publish, run from the
repository root:

```bash
export LD_LIBRARY_PATH="$PWD/artifacts/lens/native:$PWD/artifacts/lens/native/runtimes/linux-x64/native${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
```

For an installed tool, use the directory containing `libopenusd_dotnet.so` and
its dependencies under that installation's `.store`. .NET's managed assembly
probing does not supply these transitive ELF search paths.

### Why the package and native artifact paths differ

The NuGet tool is a managed .NET 10 selector with three RID-specific payloads;
the native-artifact jobs separately produce NativeAOT executables on their
matching Windows, Linux and macOS hosts. The other installable tools, such as
`opcua-mcp`, currently use managed, RID-agnostic tool packaging. UaLens needs
RID-specific payloads because its OpenUSD libraries and plugin resource paths
are platform-specific. The root selector contains no runnable payload: installing
it requires the matching RID package. These are the three platform implementations
of the tool, not additional host applications.

.NET 10 supports NativeAOT tool packages, but the SDK does not build their
cross-platform payloads from one host. Making the NuGet distribution NativeAOT
would therefore require per-OS publishing, signing and package aggregation;
enabling `PublishAot` in the Windows signing job alone is not sufficient.

The existing NuGet workflow builds all managed RID payloads **before**
Authenticode signing so `pack --no-build` consumes the signed assemblies.
Its explicit net10 pack invocation selects the tool's packable inner build;
the multi-target outer build is not packable. Removing either step without
replacing that build/sign/pack ordering can omit RID packages or rebuild
assemblies after they were signed.
The selector and all three RID package IDs are listed in the shared
[package inventory](../../.azurepipelines/expected-packages.txt), so the publishing
gate requires the complete tool alongside the library packages.

The artifact workflow verifies deployment behavior that ordinary solution builds
and unit tests do not: installation through the tool selector,
native-library/resource loading, the platform desktop event loop, retained
camera readback, changed pixels and bounded shutdown. Cocoa must run on the
process main thread. These checks supplement the shared CI matrix; they do not
waive its results or turn an unavailable renderer into a pass.

### Native artifact

From the repository root, using PowerShell 7, select the matching RID from the
table. Publish directly for that RID, not by cross-compiling on another OS:

```powershell
$project = 'tools/Opc.Ua.Lens/Opc.Ua.Lens.csproj'
$rid = 'win-x64' # linux-x64 on Ubuntu; osx-arm64 on macOS Arm64
New-Item -ItemType Directory -Force artifacts/lens/logs | Out-Null
dotnet publish $project -c Release -f net10.0 -p:CustomTestTarget=net10.0 -r $rid --self-contained true -p:PackAsTool=false -p:PublishAotEnabled=true -p:SuppressTrimAnalysisWarnings=false -p:TrimmerSingleWarn=false -m:2 -o artifacts/lens/native -bl:artifacts/lens/logs/native.binlog '-flp:logfile=artifacts/lens/logs/native.log;verbosity=normal'
if ($LASTEXITCODE -ne 0) { throw 'Native publish failed.' }

# Select the published executable for the NUnit checks below:
$env:UALENS_ARTIFACT_EXECUTABLE = (Resolve-Path artifacts\lens\native\UaLens.exe).Path
```

`PublishAotEnabled=true` enables the application's net10.0 NativeAOT setting.
Do not pass `PublishAot=true` or `PublishTrimmed=true` as global MSBuild
properties: those also reach the netstandard source-generator projects, which
cannot themselves be published with NativeAOT.

On Linux and macOS, select `artifacts/lens/native/UaLens` instead.
Run [the NUnit artifact fixture](#native-renderer-artifact-check) against that
executable, not `dotnet run` or a development build.

Keep the complete publish directory together; it includes native resources
needed by the desktop. The workflow retains the publish output, text log,
binary log, and startup results, including trimming/AOT diagnostics. Use the
included `ualens-<rid>.tar.gz` archive when downloading native output; it
preserves Unix executable permissions through artifact storage.

### Managed tool artifact

Restore and pack a managed graph separately. Do not reuse native publish
output, pass `--no-build` after an unrelated build, or install an already
published package from a remote feed when validating the local artifact.
The repository's versioning targets determine the package version. Read it
from the newly packed artifact rather than assuming a global `PackageVersion`
override took effect. Use an empty package output directory for each check.

Packing without `--runtime` produces four `.nupkg` files: the unchanged
`OPCFoundation.NetStandard.Opc.Ua.Lens` selector and its `win-x64`, `linux-x64`
and `osx-arm64` implementation packages. The .NET 10 SDK selects the matching
payload when installing the root package. Keep all four at the same version
in the feed; do not install a suffixed implementation package directly.
There is no RID-agnostic fallback because OpenUSD native libraries and plugin
manifests must match the platform. Debug builds follow the same layout with
`.Debug` before the optional RID suffix.

```powershell
$project = 'tools/Opc.Ua.Lens/Opc.Ua.Lens.csproj'
dotnet restore $project -p:Configuration=Release -p:CustomTestTarget=net10.0 -p:PublishAotEnabled=false -p:PublishAot=false -p:PublishTrimmed=false -p:PackAsTool=true
if ($LASTEXITCODE -ne 0) { throw 'Managed restore failed.' }
dotnet pack $project -c Release --no-restore -p:CustomTestTarget=net10.0 -p:TargetFramework=net10.0 -p:PublishAotEnabled=false -p:PublishAot=false -p:PublishTrimmed=false -p:PackAsTool=true -m:2 -o artifacts/lens/packages
if ($LASTEXITCODE -ne 0) { throw 'Managed pack failed.' }

$packages = @(Get-ChildItem artifacts/lens/packages -Filter '*.nupkg' -File)
if ($packages.Count -ne 4) { throw 'Expected one selector and three RID-specific tool packages.' }
$identities = @(foreach ($package in $packages) {
    $archive = [IO.Compression.ZipFile]::OpenRead($package.FullName)
    try {
        $entries = @($archive.Entries | Where-Object {
            $_.FullName -notmatch '/' -and $_.FullName.EndsWith('.nuspec')
        })
        if ($entries.Count -ne 1) { throw "Invalid package manifest: $($package.Name)" }
        $reader = [IO.StreamReader]::new($entries[0].Open())
        try { [xml] $manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
        [pscustomobject]@{
            Id = [string]$manifest.package.metadata.id
            Version = [string]$manifest.package.metadata.version
        }
    } finally { $archive.Dispose() }
})
$id = 'OPCFoundation.NetStandard.Opc.Ua.Lens'
$primary = @($identities | Where-Object Id -CEQ $id)
if ($primary.Count -ne 1) { throw 'Expected exactly one root tool selector.' }
$version = $primary[0].Version
$expectedIds = @($id, "$id.win-x64", "$id.linux-x64", "$id.osx-arm64")
if (@(Compare-Object $expectedIds $identities.Id -CaseSensitive).Count -ne 0 -or
    [string]::IsNullOrWhiteSpace($version) -or
    @($identities | Where-Object Version -CNE $version).Count -ne 0) {
    throw 'The tool packages must have the expected identities and exactly the same version.'
}

$root = Join-Path ([IO.Path]::GetTempPath()) ('ualens-installed-' + [Guid]::NewGuid())
New-Item -ItemType Directory $root | Out-Null
$source = [System.Security.SecurityElement]::Escape((Resolve-Path artifacts/lens/packages).Path)
$config = Join-Path $root 'NuGet.config'
[IO.File]::WriteAllText($config, "<configuration><packageSources><clear/><add key=`"artifact`" value=`"$source`"/></packageSources></configuration>")
$env:NUGET_PACKAGES = Join-Path $root 'cache'
$env:DOTNET_CLI_HOME = Join-Path $root 'cli'
dotnet tool install OPCFoundation.NetStandard.Opc.Ua.Lens --version $version --tool-path (Join-Path $root 'tool') --configfile $config --no-cache
if ($LASTEXITCODE -ne 0) { throw 'Isolated tool install failed.' }

# Windows; select this installation, never an unrelated ualens from PATH:
$env:UALENS_ARTIFACT_EXECUTABLE = (Resolve-Path (Join-Path $root 'tool/ualens.exe')).Path
```

Run the install commands in a disposable shell so the isolated NuGet/CLI
environment does not affect later development commands. On Linux or macOS,
the installed shim has no `.exe` extension:

```powershell
$env:UALENS_ARTIFACT_EXECUTABLE = (Resolve-Path (Join-Path $root 'tool/ualens')).Path
```

Use that path with [the NUnit artifact fixture](#native-renderer-artifact-check).
Remove `$root` after the checks and inspection. Each CI job gets a fresh installation directory,
empty NuGet cache, and a configuration containing only the local artifact feed.
The artifact workflow packs once, then installs that same package set on all
three platforms. It does not build different package contents under the same
identity on each installation host.

### What the startup check proves

`--smoke-test` runs the real classic desktop lifetime. It loads the compiled
application/theme resources, opens a Subscription Bench document through the
normal document factory/workspace, waits for visual-tree attachment,
verifies its chart is attached and laid out,
renders deterministic data with the document's ScottPlot/Skia renderer, and
awaits window, workspace, connection and service-container cleanup. It stays
offline and does not start optional resource monitoring or a benchmark.
It is distinct from the older server-dependent `--smoke` protocol probe.

The process runner changes to a temporary working directory, isolates
preferences/data paths, and imposes a 90-second timeout. It requires exit code
zero **and** the exact `UALENS_DESKTOP_SMOKE_PASS` line, printed only after
cleanup. Missing display, native dependency failure, startup exceptions,
cleanup failure, and hangs fail the job. Logs include stdout, stderr, and
`result.json`; a managed test pass cannot substitute for this check.
The application also bounds its asynchronous startup work to 30 seconds;
the external timeout covers native hangs and cleanup.

This check does not replace LENS-QA-02's desktop interaction lane, layout/DPI
and keyboard checks, screen-reader evaluation, live-server tests, or hardware
identity tests. See [desktop testing](DesktopTesting.md) for that separate lane.
It does not activate the OpenUSD viewport; native rendering and retained-camera
readback require their separate renderer qualification.
Artifact startup uses the runner's default system theme at
96 DPI on Xvfb; it does not claim full theme/DPI coverage.

### Native renderer artifact check

The `[Explicit]` `PublishedArtifactTests` fixture runs the selected executable
twice: `StartupDrainsAsync` checks ordinary startup, and
`NativeViewportRendersAndDrainsAsync` checks native rendering. Both execute in the
application's own process-main-thread desktop lifetime, including Cocoa/Metal.
They never load the application into the NUnit host.

Set `UALENS_ARTIFACT_EXECUTABLE` to the published executable or installed shim as
shown above, then run:

```powershell
$env:UALENS_ARTIFACT_LOG_DIRECTORY = Join-Path (Get-Location) 'artifacts\lens\logs'
dotnet test tests\Opc.Ua.Lens.Artifacts.Tests\Opc.Ua.Lens.Artifacts.Tests.csproj `
    -c Release -f net10.0 -p:CustomTestTarget=net10.0 `
    --filter FullyQualifiedName~PublishedArtifactTests `
    --logger trx --results-directory artifacts\lens\logs\nunit `
    -- NUnit.NumberOfTestWorkers=0 RunConfiguration.TreatNoTestsAsError=true
```

On Linux, set `UALENS_ARTIFACT_X11_VULKAN=true` and run the same `dotnet test`
command under `xvfb-run --auto-servernum --server-args="-screen 0 1600x1000x24 -dpi 96"`
when no display is available. `UALENS_ARTIFACT_ALLOW_SOFTWARE_GL=true` is a separate
opt-in for Mesa llvmpipe diagnosis; neither option relaxes acceptance.
On macOS, run the NUnit command from a logged-in desktop.
CI runs these cases on both the NativeAOT artifact and installed managed tool for every supported RID.
The ordinary startup check remains separate and does not activate OpenUSD.

The native check renders one local cube through D3D12, Vulkan or Metal, captures
its named camera through the retained Vision adapter, changes the cube's color,
and captures again without reopening the stage. It saves `native-before.png`
and `native-after.png`, then requires the native frame allocations, workspace
and service container to drain before emitting `UALENS_NATIVE_VIEWPORT_SMOKE_PASS`.
The runner requires both success markers and exit zero. It rejects stale,
missing, corrupt, oversized or uniform PNGs and unchanged decoded pixels;
`result.json` retains dimensions and file/pixel SHA-256 hashes.

The NUnit fixture supplies the internal `--smoke-native-viewport` and
`--smoke-artifact-directory` application arguments. Desktop smoke modes ignore
automatic connection environment configuration and never connect to a server.
This gate proves the selected native rendering path, not physical display,
screen-reader or exhaustive device compatibility. A missing native dependency or
unsupported compositor must fail explicitly, not become a startup-only pass.
Failures retain the test result and process logs; no automatic debugger replay
runs in the artifact workflow.

## Development diagnostics

The application no longer suppresses `CS0618` or `EXTOBS0001`. Keep analyzers
enabled and build each supported desktop TFM; the normal warning-as-error
settings make new obsolete calls visible:

```powershell
foreach ($tfm in @('net8.0', 'net9.0', 'net10.0')) {
    dotnet build tools/Opc.Ua.Lens/Opc.Ua.Lens.csproj -c Release -f $tfm -p:CustomTestTarget=$tfm -m:2 --nologo
    if ($LASTEXITCODE -ne 0) { throw "Desktop build failed: $tfm" }
}
```

Migrate obsolete calls without dropping existing behavior. If no compatible
replacement exists, document the reason and a follow-up TODO at a narrowly
scoped pragma; do not restore project-wide suppression.

```powershell
$env:CustomTestTarget = 'net10.0'
dotnet test tests\Opc.Ua.Lens.Tests\Opc.Ua.Lens.Tests.csproj -c Release -f net10.0
dotnet run --project tools\Opc.Ua.Lens\Opc.Ua.Lens.csproj -c Release -f net10.0 -- --smoke --endpoint opc.tcp://localhost:62541/Quickstarts/ReferenceServer
```

Pass `--node <NodeId>` when the target server does not expose the default
`ns=2;s=Scalar_Simulation_UInt32` reference-server simulation node.

The ordinary protocol probes use an explicitly selected Anonymous/None endpoint
on a development reference server. Secure connection probes require the
`UALENS_SECURE_PROBE_ENDPOINT` environment variable and their explicit NUnit
selector. Headless probes are not a substitute for opening and exercising the
desktop.

The ordinary test suite also drives real Avalonia windows on Windows and Linux:
editors, administration dialogs, charts, and document lifetimes share one owned
dispatcher and use controlled services or temporary certificate stores. These
tests do not require a running server, access the host PKI, or open native file
pickers. Native tests are in `Opc.Ua.Lens.Workflow.Tests` and
`Opc.Ua.Lens.Desktop.Tests`, separate from the unit-test assembly.
See [Testing UaLens](../../docs/DeveloperGuide.md#testing-ualens).
The CI runner uses `xvfb-run -a` on Linux. Cocoa requires the process main thread, so these
dispatcher-thread desktop fixtures do not run on macOS; non-desktop tests still do.

The opt-in `StructuredEditorDialogTests` fixture exercises the real Models,
Write and Call windows with mocked services, without network or certificate-store
access. Run it on an interactive desktop using:

```powershell
$env:CustomTestTarget = 'net10.0'
dotnet test tests\Opc.Ua.Lens.Tests\Opc.Ua.Lens.Tests.csproj -c Release -f net10.0 `
  --filter FullyQualifiedName~StructuredEditorDialogTests
```

`CommissioningDialogTests` exercises the real PubSub selectors and transport
readiness dialog without network activity. The
`CompiledViewTemplateBindsRealTypedEditorsAndHidesTheRawInputAsync` test checks the
typed companion form. These are explicitly selected desktop probes, not ordinary
headless-suite prerequisites.

`RepositorySampleLiveTests` is a separate opt-in process/endpoint qualification.
Build the Console Reference Server, DI pump simulator and Vision fixture cell in Release/net10.0,
then explicitly select their trusted local checkout:

```powershell
$env:CustomTestTarget = 'net10.0'
$env:UALENS_SAMPLE_SOURCE_ROOT = 'D:\trusted\UA-.NETStandard'
dotnet test tests\Opc.Ua.Lens.Tests\Opc.Ua.Lens.Tests.csproj -c Release -f net10.0 `
  --filter FullyQualifiedName~RepositorySampleLiveTests.BuiltManagedSampleAdvertisesOwnedIdentityAndCleansUp
```

This runs bounded managed processes with private configuration/PKI, verifies
their advertised identities and own-store certificates, and checks normal exit
and cleanup. The Vision cell uses the current-runtime build subdirectory and
checked-in PNG fixtures, without camera or rendering dependencies.
It does not grant peer trust or connect a user session. Sample build
paths containing symlinks/junctions are rejected; use a trusted materialized
build layout rather than weakening the no-link check. Ordinary CI tests use
controlled sample process/probe interfaces and do not start these processes.

The separate secure fixture workflow uses the same trusted build root:

```powershell
dotnet test tests\Opc.Ua.Lens.Tests\Opc.Ua.Lens.Tests.csproj -c Release -f net10.0 `
  --filter FullyQualifiedName~RepositorySampleLiveTests.PrivateFixtureAcquisitionInferenceAndFeedbackUsePinnedPeerTrust
```

It creates private client/server peer trust and exercises typed AI inference,
ISA-95 pause/resume and Vision acquisition, inference and feedback. Results are
correlated to the selected model, job, sensor and pipeline; the owned server must
exit normally. It does not use the host's PKI or qualify an external deployment.

`PrivateRegistryPreservesImmutableVersionsEpochsAndDeletionScope` and
`PrivateGeneratorBindingsRetainSourceAndPublishEvidence` host the repository's
registry and generator components in private in-process servers. They require
explicit selection and use signed/encrypted sessions with private peer trust.
The generator check verifies rejection of unsupported string conversion before
configuring its supported numeric/color/visibility capture profile.

For packaged-client qualification, set `UALENS_COMPANION_SERVER_ASSEMBLY` to the
absolute path of a trusted, normally built `Opc.Ua.Lens.Tests.dll`. These fixtures
then launch that assembly's fixed server entry point in a separate process, using
its own runtime configuration. The client keeps the package's runtime settings.
A current-user control pipe carries only readiness, one owned-fixture profile
selection and Stop; endpoints and peer stores remain private to the run. The
generator case also verifies the served root digest, actual export and rejection
of an existing destination.

`RunOwnedCompanionServerProcess` is an internal fixture entry point and is not a
standalone sample or a general command runner. It requires the per-run control
contract; do not select it manually. A package-payload test host must use the
installed production assemblies unchanged and add only its test dependencies.
