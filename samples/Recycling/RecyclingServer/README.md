# Generic recycling simulation and OPC UA coordination

This independently authored **synthetic sample** has one causal engine in a coordinator and
ten separately launchable process views. It does not control physical equipment. It does not
reproduce an unavailable upstream payload contract, claim calibration, or claim companion-model
conformance.

**Every process has a typed equipment and process-context binding.** The default
`CompanionProcessViewAdapter` creates typed sample DI process assets, Machinery state AddIns,
real Scales/Pumps instruments, Machine Vision Part 1/Part 2 systems, and native granulate-drying,
extrusion-line/extruder, filter and pelletizer interfaces where appropriate.
AMB registers the actual assets. Typed committed measurements and genealogy are projected onto
those instances. `ProcessView.CompanionBindingsComplete` is `true` for the default selected
**read-only equipment profile**. It does not claim optional remote production controls,
formal profile certification, calibration, or parity with an unavailable original producer schema.
Generic Docker, eleven-service Compose, and Kubernetes examples are documented in the
[deployment guide](deploy/README.md). Their presence and static validation are not container or
cluster runtime verification. Cloud ingestion, MQTT/ontology exporters, and source-field parity
are not implemented here.

## Runtime and public seams

The project targets .NET 10 only, following the repository's modern-host sample convention.
`RestrictForLegacyTfm` makes incompatible per-TFM solution builds explicit no-op shells;
it does not make this application run on those frameworks.

| API | Purpose |
| --- | --- |
| `RecyclingHost.Build(options, configureServices, configureServer)` | Builds one secure Generic Host; call `StartAsync`/`RunAsync`. |
| `RecyclingRuntime.AddressSpaceReady` | Actual server-start signal; independent of production readiness. |
| `RecyclingRuntime.Coordinator` | Direct coordinator API, available only in the coordinator role. |
| `RecyclingCoordinator.OpenAsync(configuration, fileSystem, lineId, timeProvider, telemetry)` | Direct asynchronous construction/recovery without a host. |
| `RecyclingCoordinator.ExecuteAsync(principal, command)` | Single-writer, authorized, revision-checked durable commands. The in-process principal must come from trusted authentication. |
| `RecyclingCoordinator.TickAsync()` | Sole logical clock input; not an equipment method or public remote tick endpoint. |
| `RecyclingCoordinator.WatchAsync()` / `GetHistory()` | Bounded current-state observation and genealogy pages. |
| `RecyclingCoordinatorClient` | Managed-session wrapper using generated typed method clients and actual UA monitored items. |
| `IRecyclingProcessViewAdapter` | Builds companion instances under the existing DI/Machinery owner; applies immutable, absolute committed snapshots and unavailable status. |
| `IRecyclingUserCommandForwarder` | Optional same-user authenticated forwarding; process service credentials are never upgraded to operator credentials. |

Use `configureServices` to inject the existing `IFileSystemProvider`, `TimeProvider`, a process
adapter, an `ICertificatePasswordProvider` backed by the secret registry for protected user keys,
or an end-user forwarder. The default password provider only opens unprotected keys; it embeds
no credential. Use `configureServer` to contribute additional compiled models
to the same Machinery owner. Do not register another DI/Machinery owner or start another engine.
The supplied adapter has no material clock, persistent production counters, or local optimistic
control updates.
Inject `RecyclingSignalDefaults` to change explicitly synthetic display coefficients; these never
change the coordinator's material transformations or persisted operating configuration.

The model in `Model\Recycling.xml` is source-generated into namespace
`Opc.Ua.Recycling.Model`, URI `urn:opcfoundation.org:sample:recycling`. Its generated clients are
`RecyclingCoordinatorTypeClient` and `RecyclingProcessViewTypeClient`. The coordinator exposes:

- `Coordinator.State`: `SnapshotDataType`, a structured UA value, not JSON or an opaque string.
- `Execute(CommandDataType) -> CommandOutcomeDataType`.
- `ReadSnapshot() -> SnapshotDataType`.
- `ReadGenealogy(afterExecutionId, maximumResults) -> ExecutionDataType[]`, limited to 128 records.
- `LineId` and `Available`, separately from lifecycle, equipment state, and product disposition.

`ProcessView` exposes the same typed snapshot, its process identity, availability, the binding
completeness flag, and a restricted `ForwardCommand`. The model publishes structured
`DataTypeDefinition`, enumeration metadata, and generated method argument metadata.
The default forwarder deliberately returns `BadNotSupported`: applications must supply a
managed client authenticated with the **same end-user certificate**. `RecyclingUserCommandForwarder`
checks that identity before forwarding. The view permits only operator/admin enable/disable
commands for its own process. Simulator administration and dispositions go to the coordinator.
No caller-supplied actor name crosses the coordination protocol.

## Equipment bindings and discovery

The same sample namespace declares `RecyclingProcessAssetType`, derived from DI `DeviceType`,
and specific process subtypes. This is **sample ownership**, not a new standard namespace.
Every stage is discoverable from `ProcessView`, DI `DeviceSet`, and Machinery `Machines`.
Its `Identification` is the Machinery AddIn with DI-qualified nameplate members, including
`ProductInstanceUri = urn:recycling-sample:<line-id>:<stage-name>`. AMB's
`AssetsByProductInstanceUri` aliases point to the actual instances. Instrument suffixes identify
their own physical assets; the Part 1 vision interface and its Part 2 asset represent the same
vision system, linked with `RepresentsSameEntityAs`. The camera is a separately identified asset.

Instance NodeIds are strings derived from URI-qualified containment paths in the configured
instance namespace. Resolve namespace **URIs**, never assume a numeric namespace-table index.
Run IDs do not enter equipment NodeIds. `Equipment` contains/references actual instruments, not
untyped telemetry tags. DI, Machinery, Pumps, Scales and Vision share **one** Machinery owner;
AMB is a sidecar, not a second DI node manager.

| Selection | Default equipment binding |
| --- | --- |
| Collection | Sample `CollectionBinType` and `CollectionTransportType`; native `VehicleScaleType` on the transport/intake weighing point. |
| Sorting | Sample inbound-identification and feed-conveyor assets; native `SimpleScaleType` at intake. Each of two `OpticalSortingStationType` instances links a native Part 1 `VisionSystemType`, Part 2 `VisionSystemAssetType`, and identified image sensor. A vision system is **not** presented as the entire sorter. |
| Shredding | Sample `PrimaryReductionType` / `SecondaryReductionType`, Machinery identification, item state and operation mode. |
| Washing | Sample prewash/friction/hotwash/rinse assets; native `PumpType` circulation and drain instruments at each operation. |
| Density separation | Sample `DensitySeparationType` and a native circulation `PumpType`. |
| Drying | Separate mechanical dewatering, thermal process context and dry-material buffer. Thermal equipment is a native OPC 40082-6 `GranulateDryingDeviceType` with a bin and desiccant bed. |
| Extrusion | Native OPC 40084 `ExtrusionLine_InterfaceType` and `Extruder_InterfaceType`, with temperature zones and melt measurements, linked to `SampleExtrusionType` process context. |
| Melt filtration | Native OPC 40084-6 `Filter_InterfaceType`, `FilterPackageType` and `ScreenPackageType`, linked to sample differential-pressure/contamination context. The disputed Filter profile is not advertised. |
| Pelletizing | Native OPC 40084-8 `Pelletizer_InterfaceType` with drive-speed measurement, knives and die plate; pellet size, mass throughput, strand breaks and screening remain explicit sample process measurements. |
| Bagging | Sample `BaggingStationType`; native `AutomaticFillingScaleType` with current weight, absolute production statistics and the reused PackML state controller. |

Native scale readings use the package's weighing engine, kilograms and a declared 1 mg display
interval; they are not legal-for-trade measurements. Native pump
`Operational/PumpActuation/Status` follows committed utility/execution state.
`Operational/Measurements/FluidTemperature` uses the documented synthetic wash temperature.
Native vision result methods return one bounded `latest` result containing a typed sample
`ExecutionDataType`; `JobId` identifies its run/execution, `IsSimulated=true`, and creation time
is the committed snapshot source time. Reprojection of that execution does not republish it.
The mechanical station retains the material-routing context and cumulative inspection proxies.

Native extrusion temperature zones use the OPC 40084 `ExtrusionTemperatureZoneType`'s
`ActualValue` / selected `SetValue`, not the differently shaped OPC 40083 `TemperatureZoneType`.
Melt temperature and pressure use `MeltTemperatureZones/.../ActualValue` and
`MeltPressureZones/.../ActualValue`; line/extruder throughput is kg/h. Native filter mesh
fineness is micrometres. `FilterPackage/ScreenPackage` has no differential-pressure field,
so `DiffPressureBar` remains a declared sample measurement rather than being misrepresented
as another standard variable.

The desiccant dryer publishes committed residence durations in milliseconds, electrical
energy in kWh, and bin/bed airflow in their distinct m3/h and l/s units. The optional
`RecyclingNativeProcessOptions` coefficients are explicitly synthetic: 80 C conditioned
temperature, 60 m3/h airflow, -40 C conditioned dewpoint, 10 C ambient dewpoint,
100 micrometre mesh and 300 rpm pelletizer speed. Register alternative options through
`RecyclingHost.Build`'s service configuration. These coefficients transform committed state;
they do not add a production clock, resample signals, or represent equipment calibration.
The pelletizer shaft speed is not used as a substitute for mass throughput.

Application certificates use the RSA-SHA256 type, matching the sample's advertised
SignAndEncrypt policies. Unadvertised ECC certificates are not generated; this keeps startup
independent of optional curves in the operating system's cryptographic provider.
Peer/user trust validation and SHA-1 rejection remain enabled.

Production methods/writes on native instruments are deliberately disabled/refused; the sample
does not turn a scale zero/tare, PackML Start, or vision StartContinuous call into an optimistic
local operation. Use the restricted `ProcessView.ForwardCommand` with a configured same-user
forwarder, or call the coordinator. Native vision result retrieval and handle release remain
available; protocol handles are not production state. Native profiles/conformance units from the
Machinery owner are conservatively withheld because structural runtime presence cannot establish
the complete process-control contract. This also withholds some valid read-only Machinery/PackML
claims. The independent AMB sidecar retains its actual asset-discovery capabilities.

### Verified semantic field map

All names on the left below are **source semantics**, not claimed standard BrowseNames.
Unless the native path is specified above, paths on the right are declared members of the
sample process type. Numeric signals use `SyntheticMeasurementType` (a UA
`AnalogUnitRangeType`) with `EngineeringUnits`, `EURange`, `SemanticField`, and a discoverable
`Formula`. UNECE units are used where identified; NTU, composite rates and proxy scores use
explicit sample-owned engineering-unit identities.

| Substep | Typed sample members (source semantics) |
| --- | --- |
| Feeding | `RunState` (`run_state`, sample enum); `BeltSpeedMps` (`belt_speed_mps`, m/s); `ThroughputTph` (`throughput_tph`, t/h); `JamAlarm` (`jam_alarm`, committed Boolean). `JamEvents` is a sample-extension absolute onset counter. |
| Each sorting station | `RunState`; `ClassificationAccuracyPct` (`classification_accuracy_pct`, % removal proxy); `ContaminationPpm` (`contamination_ppm`, ppm); `DriftScore` (`drift_score`, ratio); `CameraHealth` (`camera_health`, DI device-health enum). |
| Primary reduction | `RunState`; `MotorLoadPct` (`motor_load_pct`, %); `FeedRateKgMin` (`feed_rate_kg_min`, kg/min); `JamEvents` (`jam_events`, UInt64, absolute committed onsets). `JamAlarm` is the correlated sample-extension flag. |
| Secondary reduction | `RunState`; `FlakeSizeAvgMm` (`flake_size_avg_mm`, mm); `ThroughputKgHr` (`throughput_kg_hr`, kg/h); sample-extension `JamAlarm` and `JamEvents`. |
| Prewash | `PumpStatus` (`pump_status`, Boolean); `TurbidityNtu` (`turbidity_ntu`, NTU proxy); `WaterTempC` (`water_temp_c`, °C). |
| Friction wash | `FaultState` (`fault_state`, committed sample equipment enum, not a separate fault detector); `WashEffectivenessPct` (`wash_effectiveness_pct`, % dirt removal); `MotorLoadPct`. |
| Hotwash | `FaultState`; `BathTempC` (`bath_temp_c`, °C); `Ph` (`pH`, committed synthetic bath sample); `AdhesiveRemovalPct` (`adhesive_removal_pct`, % label-removal proxy, not chemistry). |
| Density separation | `CirculationPumpStatus` (`circulation_pump_status`, Boolean); `SeparationPurityPct` (`separation_purity_pct`, %); `HdpeRecoveryPct` (`hdpe_recovery_pct`, % target-polymer recovery proxy; the engine does not chemically identify HDPE). |
| Mechanical drying | `RunState`; `OutletMoisturePct` (`outlet_moisture_pct`, % wet mass); `ThroughputKgHr`. |
| Extrusion | `RunState`; `MeltTempC` (`melt_temp_c`, °C); `MeltPressureBar` (`melt_pressure_bar`, bar); `ExtrusionStabilityScore` (`extrusion_stability_score`, 0–100 proxy); `ThroughputKgHr`. |
| Filtering | `AlarmState` (`alarm_state`, committed sample equipment enum); `DiffPressureBar` (`diff_pressure_bar`, bar); `CloggingPct` (`clogging_pct`, %); `MeltCleanlinessIndex` (`melt_cleanliness_index`, 0–100 proxy). |
| Pelletizing | `FaultState`; `PelletSizeAvgMm` (`pellet_size_avg_mm`, mm); `ThroughputKgHr`; `StrandBreakEvents` (`strand_break_events`, UInt64, absolute committed onsets). `StrandBreakAlarm` is the correlated sample-extension flag. |
| Bagging | `RunState`; `BagWeightKg` (`bag_weight_kg`, kg); `BagsPerHr` (`bags_per_hr`, bags/h); `LotId` (`lot_id`); `ColourR` / `ColourG` / `ColourB` (`colour_r/g/b`, committed synthetic package RGB); `QualityClassification` (`quality_classification`, sample disposition enum). `PackageCount` is the absolute committed complete-package count. |

Every stage also publishes `Available`, `Timestamp`, `MachineId`, `Process`, `RunId`, `Revision`, `Tick`,
`Stage` (`StageDataType`), `Material` (`ParcelDataType[]`), `Contributions`
(`ContributionDataType[]`), `SourceIds`, `SourceBinId`, `SourceZone`, and `Executions`.
`LotId` identifies the latest currently resident parcel, or the latest complete package for
bagging; a mixed lot retains **all** component contributions. The scalar `SourceBinId` refers
to the sample's single collection-bin asset, not an invented original source identifier.
`SourceZone` comes from committed dry-material contributions: one zone is reported by name;
several zones report `mixed`, and `Contributions[].SourceZone` retains the exact labels. Utility
water is identified separately and does not invent dry-material provenance. Mechanical faults,
pH and RGB are now modeled **synthetic committed signals**, not recovered original measurements.
A ratio with no denominator, a package/lot not yet present, an unsampled bath, or RGB with no
modeled polymer basis reports `BadNoData`. Collection, rinse, thermal drying, buffering and screening expose the
known committed stage context without claiming an unavailable complete producer schema.

### Synthetic signals and projection semantics

Rates are **run-to-date averages**, not instantaneous rates: absolute transferred wet mass or
package count divided by committed logical seconds. Processed mass is received mass minus current
inventory. Purity, moisture, removal and recovery measurements describe cumulative committed
material, not a newly sampled physical outlet. No projection uses elapsed reconnect time.

Default display coefficients are 1 m/s conveyor speed, 75% powered motor load, 8 mm nominal flakes,
3 mm nominal pellets, 20°C ambient/prewash, 80°C powered hotwash, 200°C powered melt and 120 bar
transferring melt pressure. Turbidity is 1000 times removed dirt / added wash water. Filter loading
is removed dirt+labels against a synthetic 1 kg capacity, capped at 100%; active differential
pressure is 0.4 bar plus up to 4 bar with loading. Camera temperature is ambient plus 5°C while
powered. These are explicit deterministic demonstration coefficients, not calibrated process
equations. The node's `Formula` supplies the per-field equation.

The coordinator owns the additional configured synthetic signals, not the view:

- `SourceZones` defaults to `["zone-1"]`, assigned round-robin by admitted source-batch ordinal.
  Split/merge genealogy preserves each source's zone; added wash water uses `utility-water`.
- `HotWashPh` defaults to 8.5 (range 0–14). Enabled, available hotwash material ticks commit a
  bath observation. It is a synthetic set-point sample, **not** an acid/base response model.
  The `Ph` DataValue uses the committed `PhSourceTimestampUtc`, which can precede the snapshot
  timestamp while the process is stopped, paused or unavailable.
- `NaturalPolymerColor=(224,228,232)` and `ColoredPolymerColor=(64,96,160)` are configurable
  synthetic RGB anchors. The coordinator floors the polymer-mass-weighted linear channel mix,
  excluding water, labels, dirt and dense polymer. Bagging displays the committed last complete
  package response; its material id and natural/colored mass basis are retained in the typed
  `Stage` structure. RGB DataValues preserve `ColorSourceTimestampUtc`. This is **not** physical
  RGB, Lab or DeltaE calibration, and a missing polymer basis is not displayed as black.
- `EquipmentFaults` contains named stage/kind half-open logical-tick windows. Jam is supported
  for the feed conveyor and both reduction operations; strand break for pelletizing. Active
  faults block residence and material flow. Each window start contributes one **absolute**
  committed event count; no projection, subscription, repeat or recovery increments it.
  `equipment-faults`, `combined` and `recovery` demonstrate these faults. The corresponding
  `Jammed` / `StrandBroken` states belong to the sample enum, not a standard companion enum.

These additions use simulation/wire/checkpoint schema 2. Schema-1 checkpoints are incompatible
and rejected rather than silently assigned zone, pH, color or counter defaults. There is no
automatic schema-1 migration. Neither the synthetic response nor passing binding tests establishes
the missing original producer schema or physical-calibration parity.

An atomic read image hides intermediate native-controller updates. Source timestamps come only
from the committed coordinator image, including native weight, pump and state-machine variables.
Individual bath/color sample times are retained even when a later image changes only other fields.
Repeated revisions are idempotent; counters are assigned absolutely and never replay
`Record`/`RegisterWeight` operations. A stale revision cannot overwrite a newer image.
On disconnection, stage `Available=false` and present readings report `UncertainLastUsableValue`,
retaining the last values and source time. Inherently unsupported/absent readings stay bad/null.
UA intentionally omits values with bad status: the root `ProcessView.State` uses
`BadNoCommunication`, while individual equipment readings use the explicit last-usable status.
Identification and engineering-unit metadata remain browseable. A `Good` DataValue is
data availability, **not** product acceptance; quality disposition follows inherited persistent
holds and only an explicit coordinator disposition changes them.

## Synthetic material and utility model

All mass arithmetic uses integer **milligrams**. Natural polymer, colored polymer, dense polymer,
labels, dirt, and water are separate components. Contributions retain each source's component
masses. Quantized retention rounds down to a milligram and retains the exact remainder in the
other split; package allocation uses cumulative deterministic rounding. Displays must not feed
rounded numbers back into the ledger.

| Process selection | Independently queued transformations |
| --- | --- |
| `collection` | Collection bins; collection transport |
| `sorting` | Inbound identification; feed conveyor; optical sorter one; optical sorter two |
| `shredding` | Primary shredding; secondary granulation |
| `washing` | Pre-wash; friction wash; hot wash; rinse |
| `density-separation` | Float/sink separation |
| `drying` | Mechanical dewatering; desiccant-based thermal drying; post-dry buffer |
| `extrusion` | Melting/extrusion |
| `melt-filtration` | Particle filtration |
| `pelletizing` | Pellet formation; pellet screening |
| `bagging` | Exact full-package weighing with residual material retained |

Defaults are sample choices: seed `104729`, 30 logical seconds per tick, 24 planned source
batches, 60 kg wet capacity per substep, 6 kg/tick throughput, 4 kg wet packages, and one to four
active residence ticks depending on the operation. Source batches contain 5 kg dry material and
0.25 kg water; the seeded variation reallocates a small amount of natural polymer to colored
polymer without changing total dry mass. Full recipes and independently configurable electrical
and thermal demands are in `RecyclingConfiguration`.

The engine resolves downstream proposals first, using previously committed inventory.
Newly received material cannot traverse its next substep in the same tick. Disabled/unavailable
equipment or insufficient utility supply freezes that substep's residence progress. Downstream
may consume previously committed material; upstream can fill only finite buffers.

Washes add tracked utility water and remove explicit fractions of dirt/labels. Neither washing
nor filtration removes colored polymer. Float/sink separation retains colored and natural
polymer equally and preferentially rejects the modeled dense polymer. Mechanical drying drains
water; thermal drying and extrusion evaporate water. Filtration removes particles, not dissolved
color. Screening rejects a proportional fines fraction.

Every transition checks:

```text
source inputs + utility additions
  = active inventory + complete packages + rejects/wastewater + evaporation
```

The equality is component-wise and also checked per source, substep, and split/merge execution.
Holds are inherited by descendants, including merged packages. A recovered machine or expired
fault schedule never releases an existing hold. Only an authorized explicit disposition changes
it. Produced package count is not released-product count: inspect each package's hold identifiers
against `Holds`. No incomplete package increments the full-package count.

Electrical and thermal energy are separate joule counters. `Energy` contains a configuration
baseline identity, aligned start/end ticks, both energy numerators, packaged dry milligrams
(including held packages), and derived joules/kg. A zero denominator has `IsApplicable=false`
and a numeric placeholder of zero, never infinity. No color-to-energy or RGB/Lab calibration is
claimed.

Generic scenarios are `healthy`, `sorter-drift`, `utility-disturbance`, `equipment-faults`, `combined`, and `recovery`.
Equivalent small versioned fixtures are in `scenarios`. Fault windows are half-open tick ranges.
The recovery scenario restores machine conditions, **not** held-product disposition.
Unknown scenarios, overlapping windows, unsupported schemas, negative quantities, invalid
references and oversized documents are rejected, not silently clamped.

## Commits, recovery and limits

A new run remains `WaitingForParticipants` until all ten **distinct authenticated identities**
register their own roles and acknowledge an initial committed snapshot. A process view can
start first: its endpoint is browseable, but its values report `BadWaitingForInitialData` or
`BadNoCommunication` until synchronization succeeds.

The coordinator journals complete state plus accepted command receipts before acknowledging or
publishing effects. Requests carry line/run/revision and a caller-owned request ID. Identical
authenticated control-command retries return the original outcome, including after restart;
conflicting reuse is rejected. New requests with stale run/revision fail. Invalid or unauthorized
requests are not accepted into the durable receipt ledger. Production-call retries must preserve
their original request ID and payload.

`MaxCommands` bounds non-heartbeat outcomes; these receipts are never evicted. Routine lease
renewals use a separate bounded window: the latest accepted heartbeat outcome for each of the
ten processes is retained durably. Retrying that heartbeat returns its original outcome without
extending the lease twice. Once a newer heartbeat for the process commits, an older verbatim
retry fails its stale-revision check rather than reapplying it. Request IDs must not be reused.
This prevents a paused or long-running line from exhausting operator-command capacity merely
by keeping its views alive. Recovery of an earlier schema-2 journal preserves all control
receipts and compacts historical heartbeats to these latest per-process receipts; the persisted
material image, configuration/model identity and journal framing are unchanged.

The persistence format uses source-generated JSON **only on disk**, inside versioned,
length-delimited SHA-256 frames. Unique journal generations and self-contained checkpoints are
published by same-directory rename. Checkpoint publication precedes deletion of old journal
generations. Checksums, chain sequence, line identity, configuration hash, and the embedded
sample-model XML hash are checked during recovery.

An interrupted `.pending` record is explicitly reported and discarded only when a valid
committed image exists. Corrupt/truncated committed records, missing journal generations,
incompatible configuration/model/schema, or an initialized root with no valid commit fail
startup. They never create an empty replacement run. A persistence failure stops further writes
and publishes unavailable status; recover by reopening the coordinator.

The default physical provider supplies exclusive writer streams; a held `writer.lock` stream
prevents a second local writer. Injected providers must preserve exclusive write handles,
durable flushing, and atomic same-directory rename. This is **single-writer process-crash
recovery**, not HA/consensus or a guarantee against filesystem/controller power-loss behavior.
Do not share a state root among active replicas.
A journal instance is permanently disposed after releasing its lease: it cannot reopen, commit,
or checkpoint using its retained last image. Recovery always constructs a new journal owner.

Recovery restores persisted running/paused/stopped state and all inventories, queues, counters,
random state, holds, genealogy, and receipts. Old session leases are invalidated durably; roles
must re-register/reinitialize. A paused recovery remains paused. Pause freezes material time,
not administration, disposition, lease expiry, or checkpoint commits. Administrative stepping is
explicit, paused-only and bounded. Reset requires an explicit stop, creates a new run identity,
and discards that run's production image; do not use reset as recovery or retention management.

Leases use injected wall time, independently of material time. A missed view's flow is blocked
when its journaled lease expires (30 seconds by default); orderly explicit `Disconnect` blocks it
on the next coordinator evaluation. The view also marks readings unavailable after loss of
connection or three missed polling intervals. Last source timestamps are retained, including
while paused and on resynchronization. Absolute projections never repeat transfers.
Membership revision contention is not transport failure: bounded rounds of at most 16 attempts
are paced on the **same authenticated session**, with unavailable status while renewal is pending.
After a real reconnect, a view waits for its known prior lease to expire rather than taking over
an active session. Authorization refusals and unrelated live leases remain explicit failures
requiring attention, not automatic credential substitution or endless reconnect retries.

Default bounds: 256 active parcels, 4096 retained executions/packages/sinks/holds/source entries,
4096 accepted command receipts, 64 admitted writer calls, 32 local current-value observers, and
10 ticks per administrative step. Bounds are configurable within hard ceilings. Current-value
delivery coalesces to one complete image; durable genealogy and receipts are never silently
evicted. Exhausted material retention enters `ResourceLimited`; a full command ledger/queue
returns an explicit error. This finite demonstration does not implement production archival or
unlimited operation. Persistence frames are capped at 64 MiB; UA messages are separately capped
at 16 MiB, so a deliberately enlarged configuration may need a stricter operational profile.

## Provisioning and launching

Build from the repository root:

```powershell
dotnet build samples\Recycling\RecyclingServer\RecyclingServer.csproj -f net10.0 -p:CustomTargetFrameworks=net10.0
```

Provision certificates through the stack's certificate-store APIs; see the repository's
`docs\CertificateManager.md`. Unknown peers are **not** auto-accepted, anonymous sessions are
disabled, and the endpoint offers signing/encryption only. Use a distinct application PKI root
and a distinct X.509 user identity for every process. The standard root has `own`, `trusted`,
`issuer`, `rejected`, `trustedUser`, and `issuerUser` stores. Exchange application public
certificates into peer trust and user public certificates into user trust. Keep private keys
and any password/secret-store configuration out of source control.

Supply an external role-mapping JSON document:

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

The placeholder is intentionally invalid until provisioned. Add one unique mapping for each
process and any `Observer`, `Operator`, and `SimulatorAdministrator` users. Application trust,
user trust, and role authorization are three different checks. A trusted user certificate alone
does not grant a command role.

Run from this sample directory so local `.recycling` state falls under the sample's ignore rule,
or specify explicit external state/PKI paths:

```powershell
dotnet run -f net10.0 --no-build -- --role coordinator --line-id line-1 --scenario healthy --identities .\roles.json
```

The ten independent view commands use the same executable. Each needs its own provisioned
`--identity-store` and `--identity-subject`, plus the role mapping. For example:

```powershell
dotnet run -f net10.0 --no-build -- --process collection --line-id line-1 --coordinator opc.tcp://localhost:62570/Recycling --identities .\roles.json --identity-store .\user-stores\collection --identity-subject CN=CollectionView
```

Repeat with each row below, supplying that role's store and subject. Defaults are independent
endpoints, not an all-process-in-one-server mode.

| Selection | Default port |
| --- | --- |
| collection | 62571 |
| sorting | 62572 |
| shredding | 62573 |
| washing | 62574 |
| density-separation | 62575 |
| drying | 62576 |
| extrusion | 62577 |
| melt-filtration | 62578 |
| pelletizing | 62579 |
| bagging | 62580 |

Use `--endpoint` for an advertised address matching the application certificate's SANs.
Use `--scenario <file.json>` for a bounded external synthetic configuration and `--pace-ms`
for independent wall pacing. Do not embed private fixtures, keys, or deployment configuration
in the source tree or image context.

## Validation

The test project contains deterministic material/conservation scenarios, registration and
authorization, idempotency, pause/disposition, bounded queues, partial-write/corruption,
exclusive-writer/restart checks, and real encrypted OPC UA typed-call/subscription tests using
workspace-local ephemeral PKI and actual startup/commit signals.

```powershell
dotnet test tests\Opc.Ua.Recycling.Samples.Tests\Opc.Ua.Recycling.Samples.Tests.csproj -f net10.0 -p:CustomTestTarget=net10.0 -p:CustomTargetFrameworks=net10.0 -m:1
```

Passing these tests is not evidence of every optional companion feature, source-data parity, certification,
power-loss tolerance, or deployment readiness.

The project enables `IsAotCompatible` analysis and turns on `PublishAot` for a selected runtime
identifier. A native publish requires the corresponding native toolchain, for example:

```powershell
dotnet publish samples\Recycling\RecyclingServer\RecyclingServer.csproj -c Release -r win-x64 -p:CustomTargetFrameworks=net10.0 -p:CustomTestTarget=net10.0
```

An analyzer-clean managed build is not proof that a native artifact has been published or
exercised. Native publish/run verification must be reported separately for each target runtime.

An explicit NativeAOT smoke test exercises a **published executable**, rather than starting the
managed sample assembly. It provisions isolated test PKI and role identities through the existing
certificate stores, starts a native coordinator and one selected view with the normal CLI, verifies
encrypted typed calls/metadata/subscriptions, and terminates/restarts the native coordinator to
verify paused-state recovery and durable request retries. Its five cases cover collection and the
new drying, extrusion, melt-filtration and pelletizing equipment graphs, including native type
identity, measurements, units and AMB relationships. It stops only its own child processes
and removes its ephemeral PKI. Select the cases explicitly after publishing:

```powershell
$env:OPCUA_RECYCLING_NATIVE_EXE = (Resolve-Path artifacts\recycling-native\published\RecyclingServer.exe).Path
dotnet test tests\Opc.Ua.Recycling.Samples.Tests\Opc.Ua.Recycling.Samples.Tests.csproj -c Release -f net10.0 -p:CustomTestTarget=net10.0 -p:CustomTargetFrameworks=net10.0 --filter FullyQualifiedName~NativeCoordinatorAndProcessViewSupportSecureTypedCallsAndDurableRecovery
```

The executable path can point to another native publish directory. This opt-in smoke does not
run as part of ordinary managed-test discovery and does not claim container, cluster, or other
runtime-identifier coverage.
