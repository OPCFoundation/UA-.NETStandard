# OPC 30500-1 LADS samples

Two console applications that run a LADS (Laboratory and Analytical Device
Standard, OPC 30500-1) device end to end. The server publishes a simulated
shaking incubator. The client discovers it, runs an incubation program, watches
the chamber and downloads the result file.

| Sample | What it does |
| --- | --- |
| [LadsServer](LadsServer/) | Describes the incubator with `AddLads().ConfigureLads(...)`: one functional unit `Chamber` with the `Temperature` and `ShakerSpeed` analog controls, a `CO2` analog sensor, a `Door` cover, a `Timer` and the program templates `Incubation` and `Overnight`. `IncubatorHardware` accepts setpoints without applying them; `IncubatorSimulation` moves the measured values towards their targets every 500 ms |
| [LadsClient](LadsClient/) | Connects with `AddClient().AddDiscoveryAndConnect(...).AddLadsClient()`, prints the device and its functions, locks the door, starts `Incubation`, prints temperature, shaker speed and CO2 changes until the unit stops, unlocks the door and prints the result file |

The server uses the default `InMemoryLadsProgramExecutor`. It applies the
setpoints of each step, waits for the step duration and adds `Protocol.csv` to
the result. The `Incubation` template takes 13 seconds: `WarmUp` (37 °C,
150 rpm, 5 s), `Shake` (250 rpm, 5 s) and `CoolDown` (25 °C, 0 rpm, 3 s).

## Run

In two terminals:

```sh
dotnet run --project samples/LADS/LadsServer -f net10.0
dotnet run --project samples/LADS/LadsClient -f net10.0
```

The server listens on `opc.tcp://localhost:62565/LadsServer` and takes
`--port`, `--host`, `--pki-root` (absolute path), `--autoaccept` and
`--run-seconds`. The client connects with `SignAndEncrypt` and
`Basic256Sha256` and takes `--url <endpoint>`, `--pki-root <absolute path>` and
`--autoaccept true|false`. Both accept untrusted certificates by default so that
the pair runs without trust setup; never do this in production.

The client prints something like this (the value lines are shortened):

```
Device Incubator: OPC Foundation Shaking incubator (SN-0001), state Operate
Functional unit Chamber: Stopped
  Temperature  AnalogControl                current 22, target 22
  ShakerSpeed  AnalogControl                current 0, target 0
  CO2          AnalogScalarSensor           value 0.04
  Door         Cover                        Closed
  Timer        TimerControl                 current 0, target 0
  Program template Incubation: Warm up, shake and cool down a culture.
  Program template Overnight: Shake at 37 degrees for twelve hours.
Door: Locked
Started program run aedc879dea954ba28077194beb1bbf65
  Temperature  22
  ShakerSpeed  50
  Temperature  24
  ...
  Temperature  37
  CO2          5
  ShakerSpeed  250
  ...
  Temperature  25
Program run aedc879dea954ba28077194beb1bbf65 completed with 1 result file(s).
--- Protocol.csv (text/csv) ---
step;name;started;durationMs
1;WarmUp;2026-10-31T13:09:53.8479514Z;5000
2;Shake;2026-10-31T13:09:58.8588382Z;5000
3;CoolDown;2026-10-31T13:10:03.8627787Z;3000
```

The server logs each command it receives, for example
`Temperature: StartWithTargetValue 37` and `Door: Lock`.
