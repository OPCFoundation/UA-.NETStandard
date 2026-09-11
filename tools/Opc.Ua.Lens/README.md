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
part of saved workspaces.

PubSub commissioning includes registered offline presets, ordered scalar fields,
identity/content-mask editing and atomic configuration import/export. Transport
setup presents forward/reverse readiness and preserves pinned security intent.
Both workflows require explicit Start/Connect; saved configuration is not consent.

One primary connection is shared by documents; GDS tools can retain independent
secondary connections. Local certificate management and discovery are available
offline. Workspaces preserve safe configuration, not credentials or running jobs.

See the [UaLens guide](../../docs/UaLens.md) for navigation, settings, trust,
workspace behavior, guided workflows, external prerequisites and intentional limits.

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
