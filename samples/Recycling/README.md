# Recycling line simulation

This sample models a recycling line with **one durable coordinator and ten OPC UA process
servers**. The same executable starts either the coordinator or one selected process.
It targets **.NET 8, 9 and 10** and exposes typed process data, companion-specification
equipment and material genealogy over OPC UA. It is a synthetic software simulation,
not a controller for physical equipment. The optional [.NET 10 operator console](../../docs/RecyclingOperatorConsole.md)
adds a local browser schematic and role-limited controls; there is no MQTT publisher or WoT connector.

## Contents

- [How the sample fits together](#how-the-sample-fits-together)
  - [Material, time and faults](#material-time-and-faults)
  - [Holds and recovery](#holds-and-recovery)
- [What the servers expose](#what-the-servers-expose)
  - [Companion assets and process data](#companion-assets-and-process-data)
  - [Units, timestamps and quality](#units-timestamps-and-quality)
- [Run the sample](#run-the-sample)
  - [Secure identities](#secure-identities)
  - [One shared scenario](#one-shared-scenario)
  - [Build and start the roles](#build-and-start-the-roles)
- [Operator console and images](#operator-console-and-images)
- [Observe and control the line](#observe-and-control-the-line)
- [Tests and publishing](#tests-and-publishing)
  - [Managed targets](#managed-targets)
  - [Native smoke profile](#native-smoke-profile)

## How the sample fits together

[`Program.cs`](RecyclingServer/Program.cs) loads the role, scenario and certificate mappings.
`RecyclingHost.Build` assembles the Generic Host, secure endpoint and dependency-injected
runtime. The coordinator alone owns `RecyclingEngine`, `MaterialLedger` and `RecyclingJournal`.
Each process server uses `CompanionProcessViewAdapter` to project committed snapshots onto its
equipment: it has no material clock, independent random signals or production counters.

The [shared sample model](Recycling.Client/Model/Recycling.xml) supplies generated structures, methods,
clients and DI-derived process types in `urn:opcfoundation.org:sample:recycling`. Native
companion instances and Machinery state AddIns share one DI/Machinery owner; AMB registers
the actual assets. The implementation is divided into:

| Directory | Responsibility |
| --- | --- |
| [Recycling.Client](Recycling.Client) | Shared generated model, immutable contracts, mapping and typed coordinator client; no server executable dependency. |
| [Coordination](RecyclingServer/Coordination) | Authenticated membership, serialized commands and committed snapshots. |
| [Simulation](RecyclingServer/Simulation) | Deterministic transformations, finite buffers, utilities, genealogy and holds. |
| [Persistence](RecyclingServer/Persistence) | Checksummed journal/checkpoint storage through the existing file-system provider. |
| [Hosting](RecyclingServer/Hosting) | Role startup, authorization, companion assembly and read-only equipment projections. |

### Material, time and faults

```text
collection → sorting → shredding → washing → density separation
           → drying → extrusion → melt filtration → pelletizing → bagging
```

These ten selections contain **21 separately queued substeps**. For example, washing contains
prewash, friction wash, hotwash and rinse; drying separates mechanical dewatering, thermal
drying and buffering.

A fresh run waits for all ten distinct authenticated roles to register and acknowledge an
initial snapshot. The coordinator then advances one logical tick per pacing interval:

1. Downstream operations use previously committed inventory first. Newly received material
   cannot traverse another substep in that tick.
2. Residence time, throughput, available buffer capacity and electrical/thermal supply
   determine which transformations can run. Disabled or unavailable equipment, jams,
   strand breaks and utility constraints freeze the affected residence progress.
3. Transformations split or merge component-wise source contributions. The complete result
   is journaled before it becomes visible to clients or process views.

Defaults are a fixed seed of `104729`, 30 logical seconds per tick, one wall-clock second
between automatic ticks, 24 source batches, 60 kg buffers, 6 kg/tick throughput, 4 kg complete
packages, and one to four residence ticks. Each source batch contains 5 kg dry material plus
0.25 kg water. These settings are in
[`RecyclingConfiguration`](RecyclingServer/Simulation/RecyclingConfiguration.cs).

Mass is counted in integer **milligrams**, separately for natural polymer, colored polymer,
dense polymer, labels, dirt and water. Every transition checks the following balance globally,
per source, per substep and per split/merge:

```text
source inputs + utility additions
  = active inventory + complete packages + rejects/wastewater + evaporation
```

Optical sorting removes contaminants; washes add tracked water and remove dirt/labels.
Density separation rejects dense polymer while retaining natural and colored polymer equally.
Mechanical drying drains water; thermal drying and extrusion evaporate it. Filtration removes
particles, not dissolved color. Screening removes proportional fines. Bagging merges
contributions into exact full packages and retains any incomplete package as inventory.

The named [scenarios](RecyclingServer/scenarios) are `healthy`, `sorter-drift`,
`utility-disturbance`, `equipment-faults`, `combined` and `recovery`. Fault windows use logical
ticks. Jam/strand-break counts are absolute onset counters, not counters incremented by reads.
Source zones, synthetic hotwash pH and package RGB also belong to committed coordinator state.

### Holds and recovery

Equipment execution, data availability and material disposition are separate. Color/moisture
holds follow descendants and merged packages; recovering equipment does not release material.
An authorized operator or administrator explicitly releases or rejects a hold. Produced-package
count can therefore differ from released-product count.

The coordinator writes complete state and accepted command outcomes before acknowledging them.
Requests carry line/run identity, expected revision and a caller-owned request ID. Identical
control-command retries return the original outcome, including after restart; conflicting reuse
or operationally stale revisions are rejected. Membership-only metadata changes do not invalidate an
otherwise current control precondition; material ticks, other controls, lifecycle changes and coordinator
recovery do. Membership requests separately fence changes to their own role and its lease,
without conflicting on material ticks, controls or unrelated roles' metadata. Their observed
revision records the image actually applied by the view. Authenticated session ownership is unchanged.
Equipment and hold actions separately fence explicit control changes, so continuous material
ticks cannot starve a confirmed enable/disable or hold-disposition action.
Request IDs and submitted
payloads are never rewritten to obtain this compatibility. Reconnection and repeated projections do
not duplicate material.

The journal uses length-delimited SHA-256 frames, atomic same-directory publication and an
exclusive `writer.lock`. Checkpoints precede removal of superseded generations. Recovery checks
the chain, configuration and embedded model, and refuses corrupt/incompatible state rather than
starting an empty replacement run. A valid commit can recover past an interrupted `.pending`
write. Storage is single-writer process-crash recovery, not distributed consensus or a universal
power-loss guarantee. The retained schema is version 2; schema-1 stores are rejected.

A paused run stays paused after recovery. Pausing freezes material time but not membership,
administration or disposition. Old session leases are invalidated on restart; participants
re-register without resetting production. The default lease lasts 30 wall-clock seconds, and
loss of a process eventually blocks its flow while upstream buffers remain finite.

Default bounds are 256 active parcels, 4,096 retained records per material/history category,
4,096 control receipts, 64 admitted writer calls, 32 local observers and 10 administrative
steps per command. Heartbeats retain only their latest durable outcome per process, separately
from control receipts. Exhausted material retention enters `ResourceLimited`; a full writer
queue refuses the call and automatic pacing retries on the next interval. Frames are capped
at 64 MiB and UA messages at 16 MiB.

## What the servers expose

`Coordinator.State` is a structured `SnapshotDataType`, not JSON in a UA string. It contains
run/lifecycle identity, revision, logical tick/time, stages, parcels, complete packages, sinks,
source contributions, holds, the latest execution per stage and source admission, participant leases and
electrical/thermal energy.
`ProcessView.State` exposes the same live projection alongside `Process`, `LineId`,
`Available` and `CompanionBindingsComplete`. Live values are coalescible current state, not a
genealogy archive: repeating all historical executions in every monitored notification causes
serialization, cloning and delivery backpressure as a run grows. `Coordinator.ReadSnapshot`
still returns the complete retained snapshot; `Coordinator.ReadGenealogy` pages every retained
execution. No journal records or authoritative material history are discarded.

Every sample stage also exposes `RunState`, `Available`, `Timestamp`, `MachineId`, `Process`, `RunId`,
`Revision`, `Tick`, `Stage`, `Material`, `Contributions`, `SourceIds`, `SourceBinId`, `SourceZone`,
`LotId`, `Executions` and `ThroughputKgHr`. Mixed lots retain every contribution; `SourceZone`
reports `mixed` while the contribution records preserve individual zone labels.

### Companion assets and process data

Each stage is discoverable from `ProcessView`, DI `DeviceSet` and Machinery `Machines`.
AMB's `AssetsByProductInstanceUri` aliases identify the actual equipment. Machinery identification
uses `urn:recycling-sample:<line-id>:<asset-name>`. Instance NodeIds are stable URI-qualified
containment paths, independent of run IDs and numeric namespace-table positions.

The table lists the process selections, default endpoint ports and principal live bindings.
The sample namespace carries process-specific measurements alongside the native companion objects.

| Selection / port | Equipment and substeps | Exposed measurements and state |
| --- | --- | --- |
| `collection` / 62571 | Collection bin and transport; native `VehicleScaleType`. | Inventory, source contributions/zones and vehicle weight in kg. |
| `sorting` / 62572 | Inbound identification, feed conveyor, two optical stations; `SimpleScaleType`, Part 1 `VisionSystemType`, Part 2 `VisionSystemAssetType` and identified cameras. | `BeltSpeedMps`, `ThroughputTph`, `JamAlarm`/`JamEvents`, `ClassificationAccuracyPct`, `ContaminationPpm`, `DriftScore`, `CameraHealth` and native vision results. |
| `shredding` / 62573 | Primary reduction and secondary granulation with DI/Machinery state. | `MotorLoadPct`, `FeedRateKgMin`, `FlakeSizeAvgMm`, `ThroughputKgHr` and jam flags/counters. |
| `washing` / 62574 | Prewash, friction wash, hotwash and rinse; native circulation/drain `PumpType` instruments. | Pump status, `TurbidityNtu`, `WaterTempC`, `FaultState`, `WashEffectivenessPct`, `MotorLoadPct`, `BathTempC`, `Ph`, `AdhesiveRemovalPct`. |
| `density-separation` / 62575 | Float/sink process and native circulation pump. | `CirculationPumpStatus`, `SeparationPurityPct`, `HdpeRecoveryPct` (a target-polymer recovery proxy). |
| `drying` / 62576 | Mechanical dewatering, native OPC 40082-6 `GranulateDryingDeviceType` with bin/desiccant bed, then dry-material buffer. | `OutletMoisturePct`, throughput, native temperature, airflow, dewpoint, residence durations, power and energy. |
| `extrusion` / 62577 | Native OPC 40084 `ExtrusionLine_InterfaceType`, `Extruder_InterfaceType` and temperature zones. | Melt temperature/pressure, zone actual/set values, throughput and sample `ExtrusionStabilityScore`. |
| `melt-filtration` / 62578 | Native OPC 40084-6 `Filter_InterfaceType`, `FilterPackageType`, `ScreenPackageType` and temperature zones. | Mesh fineness, filter status; sample `AlarmState`, `DiffPressureBar`, `CloggingPct`, `MeltCleanlinessIndex`. |
| `pelletizing` / 62579 | Native OPC 40084-8 `Pelletizer_InterfaceType`, drive, knives and die plate; pellet screening. | Native drive speed, sample `PelletSizeAvgMm`, throughput, `FaultState`, `StrandBreakAlarm`/`StrandBreakEvents`. |
| `bagging` / 62580 | `BaggingStationType`, native `AutomaticFillingScaleType` and PackML state. | `BagWeightKg`, `PackageCount`, `BagsPerHr`, `LotId`, `ColourR`, `ColourG`, `ColourB`, `QualityClassification` and absolute weighing statistics. |

Vision's `latest` result contains typed `ExecutionDataType` content, a run/execution `JobId`
and `IsSimulated=true`; reprojecting it does not publish a new inspection. Mechanical dewatering
remains a separate process, not a desiccant dryer. Filter differential pressure is a sample
measurement because the standard screen-package type has no such member.

The package models are described in [Machinery](../../docs/Machinery.md),
[Vision](../../docs/Vision.md), [Pumps](../../docs/Pumps.md), [Scales](../../docs/Scales.md),
[AMB](../../docs/AMB.md), [Extrusion](../../docs/Extrusion.md) and
[Plastics/rubber, including granulate drying](../../docs/PlasticsRubber.md).

### Units, timestamps and quality

- Numeric sample signals use `SyntheticMeasurementType` (`AnalogUnitRangeType`) with
  `EngineeringUnits`, `EURange`, `SemanticField` and `Formula`. Weights are kg; native extrusion
  throughput is kg/h, pressure bar, mesh fineness micrometres and pelletizer speed rpm.
  Dryer durations are milliseconds, electrical energy kWh, bin airflow m³/h and bed airflow l/s.
- Rates are **run-to-date averages**, not instantaneous flow. Removal, purity, recovery and
  moisture derive from cumulative committed material. `Energy` exposes separate electrical
  and thermal joule totals divided by packaged dry mass, including held packages;
  a zero denominator sets `IsApplicable=false`.
- pH defaults to a committed synthetic 8.5 set-point observation. RGB is the floored
  polymer-mass-weighted mix of configured natural/colored anchors, not calibrated colorimetry.
  Their individual source timestamps survive subsequent commits and reconnects.
  Other display coefficients are in
  [`RecyclingSignalDefaults`](RecyclingServer/Hosting/RecyclingSignalDefaults.cs) and
  [`RecyclingNativeProcessOptions`](RecyclingServer/Hosting/RecyclingNativeProcessOptions.cs).
- Views publish atomic images with coordinator-owned values and source times. On connection loss,
  `Available=false`; present equipment readings retain their last values/time with
  `UncertainLastUsableValue`, while the root snapshot reports unavailable status.
  Missing observations or undefined ratios report `BadNoData`, not fabricated zero readings.
- A `Good` DataValue means usable data, not accepted product. `CompanionBindingsComplete=true`
  identifies this selected read-only equipment binding, not optional remote controls or formal
  conformance. Signals and weighing are synthetic, not calibrated/legal-for-trade measurements
  or a reproduction of an external producer schema.

## Run the sample

### Secure identities

The host uses encrypted/signed OPC UA sessions, RSA-SHA256 application certificates and X.509
user authentication. Anonymous access and automatic trust of unknown peers are disabled.
Each role has a separate application PKI root; each process view also has a distinct user
certificate and private key. Certificate provisioning uses the stack's
[certificate manager](../../docs/CertificateManager.md) and
[certificate stores](../../docs/Certificates.md).

Application public certificates go into peer trust; user public certificates go into user trust.
The external role map authorizes already authenticated user certificates:

```json
{
  "identities": [
    {
      "certificateSha256": "<64 hexadecimal SHA-256 digits of the DER user certificate>",
      "identity": "collection-view",
      "access": "ProcessView",
      "process": "Collection"
    }
  ]
}
```

Replace the placeholder and include one distinct `ProcessView` identity per process, plus any
`Observer`, `Operator` and `SimulatorAdministrator` users. The mapping contains fingerprints,
not keys. The application URI is `urn:localhost:OPCFoundation:Recycling-<line-id>-<role>`;
the certificate's names must match the advertised `--endpoint`.

### One shared scenario

All eleven roles use the **same effective configuration and embedded sample model**. Each
executable independently defaults to `healthy`, so another scenario must be passed to both
the coordinator and all ten views. For `--scenario <file.json>`, every role needs a readable
copy with identical effective settings; container copies are mounted read-only.

JSON scenario files override the documented top-level defaults: omitted fields retain
their defaults, while explicit zero values, nulls and unknown members still undergo
normal validation. The checked-in scenario files have the same effective settings as
their corresponding named scenarios.

Views compare the full configuration/model hash before membership or projection. It includes
residence, seconds per tick, source settings, fault schedules, leases and bounds—not just the
scenario name. A mismatch or missing identity reports `BadConfigurationError` and stops automatic
reconnect, retaining the last compatible image as unavailable. Correcting the view's inputs/build
and restarting it preserves the coordinator's retained state. Host endpoints, certificate stores,
`--pace-ms`, and `--view-poll-ms` may differ; they are outside the simulation identity.

### Build and start the roles

Build with the repository's **.NET 10 SDK**. For managed builds and runs, select `net8.0`,
`net9.0` or `net10.0` and install the corresponding runtime. The examples select `net10.0`;
substitute your choice in both `-f` and `CustomTestTarget` for the managed commands.

From the repository root:

```powershell
dotnet build samples\Recycling\RecyclingServer\RecyclingServer.csproj -f net10.0 -p:CustomTestTarget=net10.0
```

Start each role in a separate terminal **from `samples\Recycling\RecyclingServer`**, where
the default `.recycling` directory is ignored by Git. The following assumes provisioned
`roles.json`, application PKI stores and user stores; explicit `--pki-root` and `--state-root`
can select external locations.

```powershell
dotnet run -f net10.0 --no-build -- --role coordinator --line-id line-1 --scenario healthy --identities .\roles.json
```

The coordinator listens on `opc.tcp://localhost:62570/Recycling`. Start a view with its own
user store and subject, then repeat for every selection/port in the equipment table:

```powershell
dotnet run -f net10.0 --no-build -- --process collection --line-id line-1 --scenario healthy `
    --coordinator opc.tcp://localhost:62570/Recycling --identities .\roles.json `
    --identity-store .\user-stores\collection --identity-subject CN=CollectionView
```

`--pace-ms` changes wall-clock pacing, not logical seconds per tick. Views may start before the
coordinator; production starts only after all ten initialize. `--help` lists the flags.
[Docker Compose and Kubernetes examples](RecyclingServer/deploy/README.md) run the same eleven
roles with isolated storage; their single server Docker image uses the managed .NET 10 target.

## Operator console and images

The [operator console guide](../../docs/RecyclingOperatorConsole.md) covers the separate ASP.NET Core 10
workstation host, local browser launch/session, explicit coordinator/process endpoints, application
trust and dedicated UA identity. The host defaults to read-only `Observer`; its configured mode
is a permission ceiling, never a substitute for coordinator authorization. Commands go through
the typed coordinator client. Stop is not a physical emergency stop, timeouts are not success,
and destructive/reset actions require explicit confirmation and lifecycle checks.

The console observes independently: closing it cannot reset production, renew process membership
or stop the eleven servers. Bounded trends and command history are session-local observations,
not a lossless historian or full audit log. Missing native endpoints remain unavailable rather
than being filled with invented measurements.

`Recycling.Client` is the sample-local, non-packable shared typed model/client, with no dependency
on the server executable. The console and server are registered as `recyclingclient` and
`recyclingserver` in the existing Images CI workflow; triggers, tags and publication rules
are unchanged. Both images support source builds and CI's native portable-IL/prebuilt path for
AMD64/ARM64. The optional Compose `operator-console` profile publishes the browser on host
loopback and mounts separate client certificates/secrets. See the
[deployment guide](RecyclingServer/deploy/README.md#optional-operator-console) for provisioning.

## Observe and control the line

1. Connect an OPC UA client with a trusted application certificate and mapped **Observer**
   user certificate. Browse namespace URIs rather than assuming numeric namespace indexes.
2. Subscribe to `Coordinator.State`, `ProcessView.State` or individual equipment measurements.
   Compare `RunId`, `Revision`, `Tick`, `Available` and source timestamps: heartbeat/control
   commits can advance revision without advancing material time.
3. Follow `Parcels`, `Packages[].Material.Contributions`, `Holds` and `History` to trace material.
   `ReadGenealogy(afterExecutionId, maximumResults)` returns pages of up to 128 executions;
   `ReadSnapshot()` returns the current typed image.
4. Observe `sorter-drift` through classification, contamination and inherited holds;
   `utility-disturbance` through constrained stages/residence queues; and `equipment-faults`
   through jam/strand-break state and absolute counters. The `recovery` scenario restores
   equipment conditions without implicitly releasing product holds.

The generated clients are `RecyclingCoordinatorTypeClient` and `RecyclingProcessViewTypeClient`.
`RecyclingCoordinatorClient` wraps a managed session with typed calls and subscriptions. For an
already authenticated observer session:

```csharp
await using var client = new RecyclingCoordinatorClient(session, telemetry);
await foreach (CoordinatorNotification update in client.WatchAsync(cancellationToken))
{
    CommittedSnapshot state = update.Snapshot;
    Console.WriteLine(
        $"Available={update.IsAvailable}, tick={state.Tick}, packages={state.Packages.Count}");
}
```

The coordinator's typed `Execute` method accepts operator/admin enable/disable and hold-disposition
commands, plus administrator pause/resume/stop/reset/step/checkpoint commands. Stepping is
paused-only; reset requires an explicit stop and is not recovery. Retries preserve the original
request ID and payload. Native production writes/methods are refused; vision result retrieval
remains available. `ProcessView.ForwardCommand` is limited to its own process's enable/disable
commands and defaults to `BadNotSupported`; the optional forwarder preserves the originating
end-user certificate, never elevating the process service identity.

## Tests and publishing

### Managed targets

The NUnit project covers deterministic conservation, genealogy, faults, authorization,
configuration agreement, bounded queues, crash recovery and encrypted OPC UA subscriptions.
From the repository root, run each managed target separately:

```powershell
$tests = 'tests\Opc.Ua.Recycling.Samples.Tests\Opc.Ua.Recycling.Samples.Tests.csproj'
foreach ($framework in 'net8.0', 'net9.0', 'net10.0') {
    dotnet test $tests -f $framework -p:CustomTestTarget=$framework -m:1
    if ($LASTEXITCODE -ne 0) { throw "Tests failed for $framework" }
}
```

`CustomTestTarget` keeps referenced stack projects on the same framework. It makes a legacy
`net48` solution leg an empty shell, not a fourth supported runtime. Managed publication also
selects a framework explicitly:

```powershell
dotnet publish samples\Recycling\RecyclingServer\RecyclingServer.csproj -c Release `
    -f net8.0 -p:CustomTestTarget=net8.0 -p:PublishAot=false
```

### Native smoke profile

The explicit NativeAOT smoke profile remains **net10.0**. It launches a published coordinator
and a collection/drying/extrusion/melt-filtration/pelletizing view, checks secure typed values
and discovery, then restarts the coordinator to exercise paused recovery and durable retries.
Publication requires the selected platform's native toolchain:

```powershell
dotnet publish samples\Recycling\RecyclingServer\RecyclingServer.csproj -c Release `
    -f net10.0 -r win-x64 -p:CustomTestTarget=net10.0 `
    -o artifacts\recycling-native\published
$env:OPCUA_RECYCLING_NATIVE_EXE = (Resolve-Path artifacts\recycling-native\published\RecyclingServer.exe).Path
dotnet test tests\Opc.Ua.Recycling.Samples.Tests\Opc.Ua.Recycling.Samples.Tests.csproj `
    -c Release -f net10.0 -p:CustomTestTarget=net10.0 `
    --filter FullyQualifiedName~NativeCoordinatorAndProcessViewSupportSecureTypedCallsAndDurableRecovery
```

The project retains AOT compatibility analysis and enables native publication when a runtime
identifier is supplied. The smoke cases are excluded from net8.0/net9.0 test assemblies and
remain opt-in on net10.0; ordinary managed tests do not launch native executables or containers.
