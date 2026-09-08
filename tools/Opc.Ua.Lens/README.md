# UaLens

An engineering-first OPC UA desktop workspace built with Avalonia and the
OPC Foundation .NET stack.

Use the persistent connection bar to select a server, security policy and identity.
Explore the address space, monitor values and events, read history, or open an
administration or diagnostic tool from the searchable catalog. Advanced controls
remain available in context without filling the initial workspace.

## Run from source

From the repository root:

```powershell
$env:CustomTestTarget = 'net10.0'
dotnet run --project tools\Opc.Ua.Lens\Opc.Ua.Lens.csproj -c Release -f net10.0
```

The project targets .NET 8, .NET 9 and .NET 10. It is a tool under `tools`,
not a sample application. Its assembly remains `UaLens` and its package/tool
identities remain `OPCFoundation.NetStandard.Opc.Ua.Lens` / `ualens`.

## Tools

Monitor values, quality and timestamps with optional trends and timing charts.
Other documents provide events, history, file operations, local certificate stores,
GDS discovery/management/push, users/roles, write/call performance workloads, and
the live-scaling Subscription Bench.

One primary connection is shared by documents; GDS tools can retain independent
secondary connections. Local certificate management and discovery are available
offline. Workspaces preserve safe configuration, not credentials or running jobs.

See the [UaLens guide](../../docs/UaLens.md) for navigation, settings, trust and
workspace behavior, tool coverage, and prioritized stack-showcase opportunities.

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
