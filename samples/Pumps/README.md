# OPC 40223 Pumps samples

Two console applications that show the `Opc.Ua.Pumps*` libraries end to end:
a server that publishes pumps with the fluent builders, and a client that
discovers and reads them with `PumpsClient`.

| Sample | What it does |
| --- | --- |
| [PumpsServer](PumpsServer/) | Serves a duty/standby pair of cooling-water pumps and simulates them once a second |
| [PumpsClient](PumpsClient/) | Discovers every pump, prints the whole pump, then streams live values and supervision changes |

The [Pumps developer guide](../../docs/Pumps.md) explains every API used here.

## Run

In two terminals:

```sh
dotnet run --project samples/Pumps/PumpsServer -- --port 62547
dotnet run --project samples/Pumps/PumpsClient -- --insecure --seconds 30
```

`--insecure` makes the client accept the server's self-signed certificate;
never use it in production. The client also takes `--url <endpoint>` (default
`opc.tcp://localhost:62547/PumpsServer`) and `--seconds <n>` (how long to
stream, `0` to skip).

The client prints something like:

```
Pump CoolingPump_A  (ns=1;i=3788608060)
  Acme Pumps CW-80, serial CW80-0042, built 2024/3 in DE
  location: Plant 1 / Cooling skid
  Design:
    ClockwiseRotation                  True
    MaximumAllowableContinuousSpeed    3000 1/min
    MaximumAllowableWorkingPressure    1.6E+06 Pa
  Implementation:
    PumpBestEfficiency                 78 %
    RatedFlow                          25 kg/s
    RatedSpeed                         2900 1/min
  SystemRequirements:
    Fluid                              Cooling water
    MaximumFlow                        32 kg/s
    MinimumFlow                        6 kg/s
  Measurements:
    BearingTemperature                 27.92 °C
    DifferentialPressure               367831 Pa  [0 … 1600000]
    MassFlow                           21.58 kg/s  [0 … 32]
    NumberOfStarts                     0
    PumpEfficiency                     75.08 %  [0 … 100]
    PumpPowerInput                     9.64 kW
    Speed                              2821.17 1/min  [0 … 3000]
  Measurements/DriveEndBearing:
    OverallVibrationVelocityRMS        2.88 mm/s  [0 … 11.2]
    ReferenceStandardForVibrationMeasurement ISO 10816-3
  Signals:
    PumpOperation                      True
    StandBy                            False
  MultiPump: role Master, mode RedundancyOperation, 1 of 2 pumps in operation
  supervision: no active signals
  maintenance: OperatingState, Level1, failure False, operating time 12480 h
  port Suction: InletConnection, direction In, DN100 PN16 flange
  port Discharge: OutletConnection, direction Out, DN80 PN16 flange
  port Motor: Drive, direction -, no category
  Motor/Measurements:
    MotorCurrent                       18.84 A
    MotorTemperature                   35.92 °C
  document OperationManualLink: https://pumps.example.com/manuals/cw-80.pdf
  document TechnicalDataLink: https://pumps.example.com/datasheets/cw-80.pdf

Pump CoolingPump_B  (ns=1;i=1919434908)
  …

Polled 7 measurements with PumpReadOptions.ValuesOnly; MassFlow = 21.58

Watching 8 values for 45s (the pumps swap duty every 90 s; cavitation appears once a minute) …
  00:04:19  CoolingPump_A DriveEndBearing vRMS 2.91
  00:04:19  CoolingPump_A Cavitation       False
  00:04:19  CoolingPump_B PumpOperation    False
  00:04:19  CoolingPump_B Cavitation       False
  00:04:19  CoolingPump_A MassFlow         21.75
  00:04:19  CoolingPump_B MassFlow         0
  …
  00:04:56  CoolingPump_A DriveEndBearing vRMS 7.1
  00:04:56  CoolingPump_A Cavitation       True
  00:05:02  CoolingPump_A Cavitation       False
```

NodeIds and namespace indices are whatever the server assigned; only the
browse names are stable.

## What the server shows

| File | Shows |
| --- | --- |
| [`Program.cs`](PumpsServer/Program.cs) | `AddPumps()` and `ConfigurePumps(...)` on the generic host |
| [`PumpFleet.cs`](PumpsServer/PumpFleet.cs) | `CreatePumpAsync`, `WithNameplate`, every group builder (`Design`, `Implementation`, `SystemRequirements`, `Measurements`, `Signals`, `Supervision(...)`, `MultiPump`, `MaintenanceCategory(...)`, `AddPort`, `Documentation`), engineering units and ranges |
| [`PumpFleet.cs`](PumpsServer/PumpFleet.cs) | A vibration measurement from the `<Vibration>` placeholder with `AddVibration`, and the `PumpUnit` holder of the builders the simulation writes through |
| [`PumpSimulation.cs`](PumpsServer/PumpSimulation.cs) | A `BackgroundService` driving values, a duty/standby exchange every 90 s, and a cavitation episode once a minute |
| [`PumpDatasheet.cs`](PumpsServer/PumpDatasheet.cs) | Static pump data and UNECE engineering units built per OPC 10000-8 §5.6.3 |

**Runtime updates.** The simulation writes through the builders it kept at
configuration time. Every `Set`/`SetAnalog`/`SetDiscrete` publishes the
change, so subscribed clients see each update without further code.

## What the client shows

| Code in [`Program.cs`](PumpsClient/Program.cs) | Shows |
| --- | --- |
| `connection.Session.Pumps(telemetry)` | Creating the client; registers the OPC 40223 structured types |
| `pumps.IsSupported` | Detecting whether the server publishes OPC 40223 |
| `EnumeratePumpsAsync` | Discovery from the DI `DeviceSet` and the Machinery `Machines` folder |
| `ReadPumpAsync` → `PumpSnapshot` | The whole pump in one pass; each group has its own `Read…Async` too |
| `PumpValueSet` / `PumpValue` | Values with engineering unit and range, `AsDouble`, `AsString` |
| `PumpValueSet.Groups` | Nested groups such as the `DriveEndBearing` vibration measurement |
| `PumpSupervisionStatus.ActiveSignals` | Only the raised supervision signals |
| `PumpMaintenanceData` | `StateOfTheItem`, `MaintenanceLevel`, `HasFailure` |
| `ReadMeasurementsAsync(…, PumpReadOptions.ValuesOnly, …)` | Cheap polling once the metadata is known |
| `Streaming.SubscribeDataChangesAsync(value.NodeId, …)` | Subscribing to a reading; `PumpValue.NodeId` is the node that carries it, also for discrete signals |

[`SampleSession.cs`](PumpsClient/SampleSession.cs) is the session setup. It
keeps the `ApplicationInstance` alive until the session closes, because the
instance owns the `CertificateManager` the session validates with.

## Publish as NativeAOT

Both samples are NativeAOT-compatible on `net10.0`:

```sh
dotnet publish samples/Pumps/PumpsServer -c Release -f net10.0 -r osx-arm64
```

## Troubleshooting

| Symptom | Fix |
| --- | --- |
| Client reports `BadCertificateUntrusted` | Pass `--insecure` for the demo, or trust the server certificate in the client's PKI (under the local application data folder, `OPC Foundation/PumpsClient/pki`) |
| `The server publishes no pumps.` | Wait for the server log line `Materialised pump …` before starting the client |
| Port already in use | Start the server with `--port <n>` and the client with `--url opc.tcp://localhost:<n>/PumpsServer` |
