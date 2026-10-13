# Simulated companion workcell

One nonpackable console application hosts **MachineTool, WMTP, GMS,
CranesHoists and MachineVision** on the existing `MachineryNodeManager`.
It also runs a typed client, or a bounded self-check that starts both ends and
uses a real encrypted `opc.tcp` connection. It is not a hardware controller.

## Run

From the repository root with the pinned .NET SDK:

```powershell
dotnet run --project samples\CompanionSuite\CompanionSuite.csproj -f net10.0 -- `
  --selfcheck --demo-trust --instances 2 `
  --endpoint opc.tcp://localhost:0/CompanionSuite --pki-root .csuite-demo
```

`--selfcheck` waits for `IServerStartupTask.OnServerStartedAsync`, connects a
separate managed client session, verifies the acquired values and result
payloads for each independent instance, closes the client, and stops the
server. Port zero selects an available loopback port. A failure in a family or
in cleanup gives a nonzero exit code; a model count alone is not success.

To run the two modes separately:

```powershell
# Terminal 1: finite server, stopped automatically after 60 seconds.
dotnet run --project samples\CompanionSuite\CompanionSuite.csproj -f net10.0 -- `
  --server --demo-trust --instances 2 --seconds 60 --pki-root .csuite-demo

# Terminal 2: read the matching sample, then disconnect.
dotnet run --project samples\CompanionSuite\CompanionSuite.csproj -f net10.0 -- `
  --client --demo-trust --instances 2 --pki-root .csuite-demo
```

The default endpoint is `opc.tcp://localhost:48485/CompanionSuite`. Use
`--endpoint` to change it. `--instances` is bounded to 1–2; `--seconds` is
bounded to 1–300 and applies to server mode. Ctrl+C also initiates awaited
shutdown. The client has a 90-second deadline and does not remain in the
background.

## Message security and trust

**SignAndEncrypt / Basic256Sha256 is required.** No unsecured policy is
enabled. Normal certificate validation and domain checking remain the
defaults. Server and client certificates use the existing certificate-store
system, below the explicitly selected `--pki-root` (`.companion-suite-pki` by
default), in separate `server` and `client` directories.

`--demo-trust` is an explicit exception for disposable local demonstrations:
it accepts untrusted application certificates, is rejected for nonloopback
endpoint addresses, and does **not** disable message encryption. Do not use
this exception as a production trust policy. Without it, provision mutual
trust in the application certificate stores before connecting. Never commit
the generated PKI or reuse demonstration certificates as production secrets.
The sample exposes no arbitrary host directory through OPC UA.

The sample converts `localhost` to numeric `127.0.0.1` **before listener
registration**, not merely before discovery. The transport treats DNS names
as wildcard bind requests, so validating the hostname alone is insufficient.
Explicit numeric IPv4 and IPv6 addresses retain their selected address family;
automatic port selection uses that same family. Without `--demo-trust`,
explicit nonloopback/DNS endpoints keep their existing transport binding intent.
The self-check connects to the exact registered URL. With demo trust enabled
it also checks the operating system's listener table and fails if the selected
port has a wildcard/nonloopback listener or lacks the requested loopback socket.

Run the bounded configuration regression without starting an application
host, certificate store or listener:

```powershell
dotnet run --project samples\CompanionSuite\CompanionSuite.csproj -f net10.0 -- `
  --verify-endpoint-config
```

It covers default/explicit loopback URLs, both address families, automatic
ports, rejected wildcard demo configurations, and unchanged strict-trust
nonloopback intent. For a real IPv6 self-check, supply
`--endpoint "opc.tcp://[::1]:0/CompanionSuite"` with `--selfcheck --demo-trust`.

## What the client actually verifies

| Family | Acquired state and typed workflow |
| --- | --- |
| MachineTool | Native machine discovery; `CELL1.NC` / `CELL2.NC`, retained production jobs and independent `Cutter1` / `Cutter2` tool inventories |
| WMTP | Separate peripheral roots, battery and temperature samples, engineering units and repeated nonconsuming record reads |
| GMS | Native `SensorType`, `Qualified` hours, active-sensor NodeIds restricted to that GMS inventory, independent result metadata/content and standard temporary-file download |
| CranesHoists | Native Robotics-derived crane/axis topology, position, load, protection state and the protection reference to its own axis |
| MachineVision | Real control runtime, native AMCM asset, current control state and independent simulated inspection result metadata/content |

Values retain their quality and acquisition timestamps. Every adapter is
explicitly simulated. GMS result transfer reuses Machinery Result and the
shared file protocol. WMTP records use the existing bounded record store.
There is no second registry, namespace owner, result protocol or manually
fabricated response header.

The sample selects:

```csharp
server.AddMachinery(options =>
{
    options.Parts = MachineryParts.BuildingBlocks | MachineryParts.Jobs |
        MachineryParts.Result | MachineryParts.ProcessValues;
})
    .AddMachineToolToMachinery(options => options.Parts = MachineToolParts.All)
    .AddGmsToMachinery()
    .AddCranesHoistsToMachinery()
    .AddVisionToMachinery();
```

The current MachineVision composition API is named `AddVisionToMachinery`.
`ConfigureMachineToolFor<MachineryNodeManager>`, `ConfigureGmsFor`,
`ConfigureCranesHoistsFor` and `ConfigureVisionFor` all receive the same owner.
Native generated family types are preserved. One owner supplies the DI
`DeviceSet` and Machinery `Machines` roots and the model-provider union.
Providers, source observations and result/record stores remain per instance.
Loaded namespaces are not interpreted as a claim that every optional facet
is enabled. The undefined upstream GMS production-statistics type and the
affected complete GMS Production facet are not invented or advertised.

## Explicit interaction

The default client performs discovery, reads and result/file retrieval only.
It does not start jobs, modify peripheral settings, clear records or request
crane limits.

Add `--exercise` to client or self-check mode to explicitly select automatic
mode and run **one standard, simulated MachineVision single job per system**.
The typed client waits for Ready, retrieves the result correlated to the
returned JobId and releases its metadata handle. This is a software
demonstration, not a request to a physical machine. There are no invented GMS
measurement-start, correction or crane-motion commands.

## Lifetime and validation

The workcell is injectable; all construction uses public family builders.
It awaits the actual UA-ready callback rather than assuming `Host.StartAsync`
means the address space is usable. Shutdown joins native runtime cleanup,
then disposes the application-owned record stores and simulated providers.
Errors from execution and cleanup are retained.

Cross-family regression cases live in
`tests\Opc.Ua.Machinery.Tests\Integration\CompanionSuite*.cs`, in the existing
NUnit project. They exercise mixed ownership, instance isolation, reordered
namespaces, WMTP-only hosting, registration rejection and negative lifetimes
over real encrypted sessions. The sample uses the existing
`AppTargetFrameworks`; it introduces no new package dependency.

The managed sample has been built and run with two instances of every family
on **net48, net8.0, net9.0 and net10.0**, including automatic loopback port
selection. The opt-in vision-job workflow is also exercised on net10.0.
The composition regression suite runs on net48 and net10.0.

NativeAOT compatibility is not claimed by a managed build or self-check.
Only a separately completed native publish and run could establish that.
