# UaLens

An engineering-first OPC UA desktop workspace built with Avalonia and the
OPC Foundation .NET stack.

Use the persistent connection bar to select a server, security policy and identity.
Explore the address space, monitor values and events, read history, or open an
administration or diagnostic tool from the searchable catalog. Document actions
and document or connection settings let you perform advanced operations and
control their configuration.

## Run from source

From the repository root:

```powershell
$env:CustomTestTarget = 'net10.0'
dotnet run --project tools\Opc.Ua.Lens\Opc.Ua.Lens.csproj -c Release -f net10.0
```

The project targets .NET 8, .NET 9 and .NET 10. Its source is in `tools\Opc.Ua.Lens`.
The assembly is `UaLens`, the package is `OPCFoundation.NetStandard.Opc.Ua.Lens`,
and the tool command is `ualens`.

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

Publish a native Windows desktop executable with the installed Visual Studio C++
build tools available:

```powershell
$env:CustomTestTarget = 'net10.0'
dotnet publish tools\Opc.Ua.Lens\Opc.Ua.Lens.csproj -c Release -f net10.0 -r win-x64 --self-contained -p:PackAsTool=false -o publish\UaLens
```

The normal .NET tool package is a separate managed distribution. Restore its
Release/net10 graph before packing, particularly after building another framework:

```powershell
dotnet build tools\Opc.Ua.Lens\Opc.Ua.Lens.csproj -c Release -f net10.0 -p:CustomTestTarget=net10.0 -p:PublishAotEnabled=false
dotnet restore tools\Opc.Ua.Lens\Opc.Ua.Lens.csproj -p:CustomTestTarget=net10.0 -p:Configuration=Release -p:PublishAotEnabled=false
dotnet pack tools\Opc.Ua.Lens\Opc.Ua.Lens.csproj -c Release --no-build --no-restore -p:CustomTestTarget=net10.0 -p:TargetFramework=net10.0 -p:PublishAotEnabled=false -o artifacts\packages
```

## Development diagnostics

```powershell
$env:CustomTestTarget = 'net10.0'
dotnet test tests\Opc.Ua.Lens.Tests\Opc.Ua.Lens.Tests.csproj -c Release -f net10.0
dotnet run --project tools\Opc.Ua.Lens\Opc.Ua.Lens.csproj -c Release -f net10.0 -- --smoke --endpoint opc.tcp://localhost:62541/Quickstarts/ReferenceServer
```

The ordinary protocol probes use an explicitly selected Anonymous/None endpoint
on a development reference server. Secure connection probes require the
`UALENS_SECURE_PROBE_ENDPOINT` environment variable and their explicit NUnit
selector. Headless probes are not a substitute for opening and exercising the
desktop.

The ordinary test suite also drives real Avalonia windows on Windows and Linux:
editors, administration dialogs, charts, and document lifetimes share one owned
dispatcher and use controlled services or temporary certificate stores. These
tests do not require a running server, access the host PKI, or open native file
pickers. On Linux, run the test command under `xvfb-run -a`; both CI systems do
this automatically. Cocoa requires the process main thread, so these
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
