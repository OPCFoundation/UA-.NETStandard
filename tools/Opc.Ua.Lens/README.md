# UaLens

An engineering-first OPC UA desktop workspace built with Avalonia and the
OPC Foundation .NET stack.

Use the persistent connection bar to select a server, security policy and identity.
Explore the address space, monitor values and events, read history, or open an
administration or diagnostic tool from the searchable catalog. Document actions
and document or connection settings let you perform advanced operations and
control their configuration.

UaLens supports the OPC UA OpenAPI service mapping over HTTPS and WebSockets.
Enter the server's `https://`, `opc.https://`, `ws://`, `wss://`, or
`opc.wss://` discovery URL and select the advertised **HTTPS OpenAPI** or
**WSS OpenAPI** endpoint. Plain `ws://` is intended for trusted local networks.
The same browser, attribute inspector, subscriptions, event view, history,
method calls, and write workflows operate through the selected transport
profile. TLS trust remains fail-closed; OPC UA user identity selection is
separate from optional HTTP or WebSocket transport authentication.

**File > Open NodeSet2 files...** opens one or more information models in the
same explorer without a server. Lens includes the OPC UA core model, resolves
local dependencies, and offers approved downloads from OPCFoundation/UA-Nodeset
or a file picker for missing models. See the
[offline NodeSet2 guide](../../docs/UaLensNodeSets.md).

The explorer's **Namespace** dropdown lists the active server or NodeSet
namespaces. Select one to highlight its nodes without filtering the tree;
choose **None (no highlighting)** to clear the highlight.

## Run from source

From the repository root:

```powershell
$env:CustomTestTarget = 'net10.0'
dotnet run --project tools\Opc.Ua.Lens\Opc.Ua.Lens.csproj -c Release -f net10.0
```

The project targets .NET 8, .NET 9 and .NET 10. Its source is in `tools\Opc.Ua.Lens`.
The assembly is `UaLens`, the package is `OPCFoundation.NetStandard.Opc.Ua.Lens`,
and the tool command is `ualens`.

### Automatic connection

Set `UALENS_ENDPOINT_URL` to connect when the desktop opens. The remaining
variables pin the discovered server and prevent fallback to another security or
identity profile:

| Variable | Meaning | Default |
| --- | --- | --- |
| `UALENS_ENDPOINT_URL` | OPC UA discovery/endpoint URL; enables automatic connection | none |
| `UALENS_APPLICATION_URI` | Expected server ApplicationUri | any |
| `UALENS_SECURITY_MODE` | `None`, `Sign`, or `SignAndEncrypt` | `None` |
| `UALENS_SECURITY_POLICY` | SHA-2-or-higher policy name such as `Basic256Sha256`, `None`, or its full URI | `None` |
| `UALENS_USERNAME` | Username identity; omit for Anonymous | none |
| `UALENS_PASSWORD` | Password required when a username is supplied | none |

Secure endpoints remain fail-closed. Their server certificate must already be
trusted or accepted through the existing trust dialog; environment startup does
not add a blanket certificate-trust bypass.

To start the pump Device Integration sample and UaLens together, with Lens
automatically connected over the local `None/None` development endpoint:

```powershell
$env:CustomTestTarget = 'net10.0'
dotnet run --project tools/Opc.Ua.Lens.AppHost/Opc.Ua.Lens.AppHost.csproj
```

Override AppHost settings with standard configuration environment variables,
for example `UaLens__SecurityMode`, `UaLens__SecurityPolicy`,
`UaLens__Username`, and `UaLens__Password`. Do not put passwords in
`appsettings.json`.

## Tools

Monitor values, quality and timestamps with optional trends and timing charts.
Other documents provide events, history, file operations, local certificate stores,
GDS discovery/management/push, users/roles, write/call performance workloads, and
the live-scaling Subscription Bench.

Alarms, Models, Continuity Lab, PubSub and Companion Tasks provide typed stack
workflows through the tool catalog. Capability checks distinguish
unavailable, unsupported and unauthorized operations. X.509, issued-token and
hardware-backed identities use explicitly configured providers; reverse connect
keeps listener, peer, security and identity requirements visible.

Models and the Write/Call dialogs share named-bit OptionSet and dimension-aware
array/matrix editors. Performance compares a chosen baseline with real retained
latency distributions and configuration evidence. Companion Tasks use an explicit,
single-use **Prepare / Review / Run** flow; preparations and confirmation are never
part of saved workspaces. Array/custom task inputs use the same structured editor,
with isolated values and metadata revalidation. ISA-95 and AI workflows keep
authorization, service acceptance and observed completion separate. Non-rendering
OpenUSD tasks provide bounded live/history capture and explicitly configured
peer telemetry export; they do not fetch remote assets or render a stage.
WoT/xRegistry tasks distinguish logical Resource metadata from individual
Versions, require nonzero scope epochs and explicit deployment policy, and offer
bounded single-Version materialization rather than a blanket refresh command.
Vision tasks add reviewed simulated media, inference and feedback, with exact
result correlation and fresh-budget cleanup of returned leases and acknowledged
continuous runs. Managed SVG exports contain pixel-space boxes and labels, not
downloaded images or GPU-rendered scenes. Physical/hybrid sensors and implicit
external execution are rejected.

PubSub commissioning includes registered offline presets, ordered scalar fields,
identity/content-mask editing and atomic configuration import/export. Transport
setup presents forward/reverse readiness and preserves pinned security intent.
Both workflows require explicit Start/Connect; saved configuration is not consent.

One primary connection is shared by documents; GDS tools can retain independent
secondary connections. Local certificate management and discovery are available
offline. Workspaces preserve safe configuration, not credentials or running jobs.

See the [UaLens guide](../../docs/UaLens.md) for navigation, settings, trust,
workspace behavior, guided workflows, external prerequisites and intentional limits.

Identity setup remains provider-driven. Named token providers can expose explicit
authorization interaction; configured user-certificate adapters offer separate
CSR preparation, enrollment and adoption. The GDS adapter requires a matching
registered ApplicationUri and certificate group, strict Users trust validation,
and a private-key-capable destination. It neither provisions an authority nor
replaces application-instance or hardware key management.

## Publish and package

The dedicated [artifact workflow](../../.github/workflows/ualens-artifacts.yml)
builds all three desktop TFMs and validates two separate distributions on this
explicit matrix:

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

All jobs need .NET SDK 10, package-feed access during restore, and Python 3
for the bounded process runner. The workflow uses Python's standard library
and adds no NuGet dependencies. Linux prerequisites are installed explicitly
in the workflow. The Windows and macOS jobs use their hosted runner's compiler
and desktop; do not substitute a service-only runner or a headless Avalonia backend.

### Native artifact

From the repository root, using PowerShell 7, select the matching RID from the
table. Publish directly for that RID, not by cross-compiling on another OS:

```powershell
$project = 'tools/Opc.Ua.Lens/Opc.Ua.Lens.csproj'
$rid = 'win-x64' # linux-x64 on Ubuntu; osx-arm64 on macOS Arm64
New-Item -ItemType Directory -Force artifacts/lens/logs | Out-Null
dotnet publish $project -c Release -f net10.0 -p:CustomTestTarget=net10.0 -r $rid --self-contained true -p:PackAsTool=false -p:PublishAotEnabled=true -p:SuppressTrimAnalysisWarnings=false -p:TrimmerSingleWarn=false -m:2 -o artifacts/lens/native -bl:artifacts/lens/logs/native.binlog '-flp:logfile=artifacts/lens/logs/native.log;verbosity=normal'
if ($LASTEXITCODE -ne 0) { throw 'Native publish failed.' }

# Windows; launch the published executable, not dotnet run:
python tools/Opc.Ua.Lens/validate-artifact.py --executable artifacts/lens/native/UaLens.exe --log-directory artifacts/lens/logs/native-smoke
```

`PublishAotEnabled=true` enables the application's net10.0 NativeAOT setting.
Do not pass `PublishAot=true` or `PublishTrimmed=true` as global MSBuild
properties: those also reach the netstandard source-generator projects, which
cannot themselves be published with NativeAOT.

On Ubuntu, launch with a real X server, using this command when no display
is already available:

```bash
xvfb-run --auto-servernum --server-args="-screen 0 1600x1000x24 -dpi 96" \
  python3 tools/Opc.Ua.Lens/validate-artifact.py \
  --executable artifacts/lens/native/UaLens \
  --log-directory artifacts/lens/logs/native-smoke
```

On macOS:

```bash
python3 tools/Opc.Ua.Lens/validate-artifact.py \
  --executable artifacts/lens/native/UaLens \
  --log-directory artifacts/lens/logs/native-smoke
```

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

```powershell
$project = 'tools/Opc.Ua.Lens/Opc.Ua.Lens.csproj'
dotnet restore $project -p:Configuration=Release -p:CustomTestTarget=net10.0 -p:PublishAotEnabled=false -p:PublishAot=false -p:PublishTrimmed=false -p:PackAsTool=true
if ($LASTEXITCODE -ne 0) { throw 'Managed restore failed.' }
dotnet pack $project -c Release --no-restore -p:CustomTestTarget=net10.0 -p:TargetFramework=net10.0 -p:PublishAotEnabled=false -p:PublishAot=false -p:PublishTrimmed=false -p:PackAsTool=true -m:2 -o artifacts/lens/packages
if ($LASTEXITCODE -ne 0) { throw 'Managed pack failed.' }

$packages = @(Get-ChildItem artifacts/lens/packages -Filter '*.nupkg')
if ($packages.Count -ne 1) { throw 'Expected exactly one freshly packed tool artifact.' }
$archive = [IO.Compression.ZipFile]::OpenRead($packages[0].FullName)
try {
    $entry = $archive.GetEntry('OPCFoundation.NetStandard.Opc.Ua.Lens.nuspec')
    if ($null -eq $entry) { throw 'The tool artifact has no expected package manifest.' }
    $reader = [IO.StreamReader]::new($entry.Open())
    try { [xml] $manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
    $version = $manifest.package.metadata.version
} finally { $archive.Dispose() }

$root = Join-Path ([IO.Path]::GetTempPath()) ('ualens-installed-' + [Guid]::NewGuid())
New-Item -ItemType Directory $root | Out-Null
$source = [System.Security.SecurityElement]::Escape((Resolve-Path artifacts/lens/packages).Path)
$config = Join-Path $root 'NuGet.config'
[IO.File]::WriteAllText($config, "<configuration><packageSources><clear/><add key=`"artifact`" value=`"$source`"/></packageSources></configuration>")
$env:NUGET_PACKAGES = Join-Path $root 'cache'
$env:DOTNET_CLI_HOME = Join-Path $root 'cli'
dotnet tool install OPCFoundation.NetStandard.Opc.Ua.Lens --version $version --tool-path (Join-Path $root 'tool') --configfile $config --no-cache
if ($LASTEXITCODE -ne 0) { throw 'Isolated tool install failed.' }

# Windows; never invoke an unrelated ualens from PATH:
python tools/Opc.Ua.Lens/validate-artifact.py --executable (Join-Path $root 'tool/ualens.exe') --log-directory artifacts/lens/logs/managed-smoke
```

Run the install commands in a disposable shell so the isolated NuGet/CLI
environment does not affect later development commands. For Linux or macOS,
replace the final Windows launch command with the matching PowerShell command:

```powershell
# Ubuntu:
xvfb-run --auto-servernum '--server-args=-screen 0 1600x1000x24 -dpi 96' python3 tools/Opc.Ua.Lens/validate-artifact.py --executable (Join-Path $root 'tool/ualens') --log-directory artifacts/lens/logs/managed-smoke

# macOS:
python3 tools/Opc.Ua.Lens/validate-artifact.py --executable (Join-Path $root 'tool/ualens') --log-directory artifacts/lens/logs/managed-smoke
```

Remove `$root` after inspection. Each CI job gets a fresh installation directory,
empty NuGet cache, and a configuration containing only the local artifact feed.

### What the startup check proves

`--smoke-test` runs the real classic desktop lifetime. It loads the compiled
application/theme resources, opens a Subscription Bench document through the
normal document factory/workspace, waits for actual visual-tree attachment,
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
Artifact startup uses the runner's default system theme at
96 DPI on Xvfb; it does not claim full theme/DPI coverage.

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
pwsh .github\scripts\run-dotnet-tests.ps1 `
  -Projects tests\Opc.Ua.Lens.Tests\Opc.Ua.Lens.Tests.csproj `
  -CustomTestTarget net10.0 -Framework net10.0 -Configuration Release `
  -Filter 'TestCategory!=LongRunning&TestCategory!=Stress' -ResultsDirectory TestResults\lens
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
pickers. The runner isolates ordinary and native workflow selections in separate
processes and uses `xvfb-run -a` on Linux. Cocoa requires the process main thread, so these
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
