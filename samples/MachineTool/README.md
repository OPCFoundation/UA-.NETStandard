# MachineTool and wireless-peripheral samples

These applications use the released typed MachineTool/WMTP runtime and clients.
Their providers are explicitly simulated: no CNC instructions, Bluetooth
pairing, radio driver, firmware installation or licence activation is performed.

Build both applications before starting the bounded server:

```powershell
dotnet build samples\MachineTool\MachineToolServer\MachineToolServer.csproj -f net10.0
dotnet build samples\MachineTool\MachineToolClient\MachineToolClient.csproj -f net10.0
```

## Run a secure, isolated workcell

In one terminal:

```powershell
dotnet run --no-build --project samples\MachineTool\MachineToolServer -f net10.0 -- --mode mixed --advanced --instances 2 --seconds 120 --demo-trust --allow-commands --simulate-job Machine1
```

After its actual `READY` message, in another terminal:

```powershell
dotnet run --no-build --project samples\MachineTool\MachineToolClient -f net10.0 -- --demo-trust
```

Default endpoint: `opc.tcp://127.0.0.1:62558/MachineToolServer`.
`--port 0` chooses an available port; pass the reported URL to the client's
`--endpoint`. The server always binds loopback. `--demo-trust` is an explicit
local-only certificate exception; SignAndEncrypt/Basic256Sha256 remain
required. Without it, administer separate client/server trust stores with
`--pki`. Never deploy demo trust as production authorization.

`--mode machine`, `wmtp` and `mixed` select independent model closures.
Mandatory-only is the default; `--advanced` selects production/jobs/files and
all eight wireless measurement kinds, metadata and independent work/service
records. `--instances 1..4` creates independent providers/stores for each
selected family.

The client discovers native identities, reads raw values/capabilities and
complete bounded records, then makes one bounded observation. It preserves
UA quality and acquisition time. Reports copy records rather than consuming
them. A WMTP-only endpoint never fabricates a MachineTool.

## Explicit commands

Every client mutation requires `--target` and `--consent`. The server separately
requires `--advanced --demo-trust --allow-commands`; `--refuse-commands` provides
a deterministic refusal scenario.

```powershell
# Read the non-executable WorkMasters file and submit the declared ISA95 order.
dotnet run --no-build --project samples\MachineTool\MachineToolClient -f net10.0 -- --demo-trust --target Machine1 --read-files --command job-store-start --consent

# Accept a UInt32 trigger setting, then explicitly prove a second request is refused.
dotnet run --no-build --project samples\MachineTool\MachineToolClient -f net10.0 -- --demo-trust --target Peripheral1 --command configure --sensor Temperature --trigger-ms 2000 --consent --prove-refusal

# Transfer generated, nonsecret demonstration bytes. Acceptance is not installation.
dotnet run --no-build --project samples\MachineTool\MachineToolClient -f net10.0 -- --demo-trust --target Peripheral1 --command firmware --transfer-bytes 32 --consent
```

Use `--help` for calibration, explicit UTC time, selected record deletion,
licence transfer and explicit abort. Local timeout/cancellation never sends
AbortOperation or retries an uncertain write.

Each peripheral accepts at most one simulated configuration, calibration,
time setting and transfer of each kind; later requests are refused. A refused
configuration retains the previously accepted trigger/delta.
The separately selected server `--simulate-job Machine1` supplies explicit
execution/part observations after StoreAndStart. Job acceptance alone never
means a machine executed anything.

## Lifetimes and bounds

Server windows are 1-600 seconds. Client work has a 90-second deadline;
observation is 0-20 seconds and 1-100 notifications. Discovery, sensors, files,
record reports and transfer bytes have explicit ceilings. Oversize data fails,
not silently truncated success. `--freeze-measurements` demonstrates real stale
quality on advanced wireless observations.

`--cancel-after-ms` exercises bounded cancellation. Exit codes are 0 completed,
1 error/refusal, and 2 cancelled. Natural server shutdown joins acquisition,
drains runtimes, releases providers and deletes only its generated temporary
read-only program folders. PKI remains available for administration.
Clients close call-owned subscriptions/files and their managed session.

## Frameworks and NativeAOT

Both projects support net48, net8.0, net9.0 and net10.0. NativeAOT is selected
only on the application projects for net10.0:

```powershell
dotnet publish samples\MachineTool\MachineToolServer -c Release -f net10.0 -r win-x64 --self-contained true
dotnet publish samples\MachineTool\MachineToolClient -c Release -f net10.0 -r win-x64 --self-contained true
```

Run the published executables with the same CLI arguments; publishing alone
does not prove native behavior. See [the developer guide](../../docs/MachineTool.md)
for provider contracts and [CompanionSuite](../CompanionSuite/) for all families
on one DI/Machinery owner.
