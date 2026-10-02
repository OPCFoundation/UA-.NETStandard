# OPC 40001 Machinery samples

Two console applications that show the whole OPC 40001 series end to end.

| Sample | What it does |
| --- | --- |
| [MachineryServer](MachineryServer/) | Serves one simulated hydraulic press with every part of the series |
| [MachineryClient](MachineryClient/) | Walks the `Machines` folder, prints what it finds, downloads a result and streams state |

Run them in two terminals:

```sh
dotnet run --project samples/Machinery/MachineryServer -- --port 62546
dotnet run --project samples/Machinery/MachineryClient -- --insecure
```

The client prints something like:

```
Machine 12:HydraPress-1 (ns=12;i=1)
  Acme Machine Tools HydraPress 500 — serial HP500-2024-0001
  location: Hall 3 / Line 2
  state: Executing / mode: Processing
  component 12:HydraulicUnit
  building block 13:Identification
  building block 3:MachineryEquipment
  building block 8:JobManagement
  building block 3:Monitoring
  building block 3:MachineryItemState
  building block 3:Notifications
  building block 13:OperationCounters
  building block 3:MachineryOperationMode
  building block 3:Components
  device health: NORMAL
  operation counters: power-on 12480.0 h, operating 9215.5 h
  equipment UpperDie (urn:acme-machine-tools.example:equipment:die)
    life: 118400 of 250000 left
  process value HydraulicOilTemperature: 44.0 °C (setpoint 45.0, 45.7 % of range)
    limits: 5.0 / 15.0 / 65.0 / 80.0
  energy CompressedAir: Main = HydraPress-1/CompressedAir
    NeEnergyImportHp = 0
    NeEnergyExportHp = 0
    Volume = 184320
    VolumeFlowRate = 18.4
    Pressure = 620000
    Temperature = 294.65
  energy Electricity: Main = HydraPress-1/Electricity
    AcActivePowerTotal = 41250
Latest result: stroke-000019
  downloaded 42 bytes:
  resultId,stroke-000019
  peakForceKN,4703.6
Observing MachineryItemState for 4s (OPC 40001-1 state changes always come from the server) …
  22:33:52  -> Executing (via FromNotExecutingToExecuting)
  22:33:52  -> NotExecuting (via FromExecutingToNotExecuting)
```

The namespace indices above are whatever the server happened to assign; only
the browse names are stable. `2:Identification` and `2:OperationCounters` show
up as `13:` because OPC 40001-1 gives both the Device Integration namespace —
see [Default instance browse names are
load-bearing](../../docs/Machinery.md#default-instance-browse-names-are-load-bearing).

The building blocks come from the OPC 40001-1 `MachineryBuildingBlocks`
organizer, which is the one place a client can discover what a machine
supports instead of probing for each block in turn. The oil temperature walks a
slow ramp across the datasheet limits, so the exclusive limit alarm on the
process value activates and clears while the sample runs; subscribe to
`HydraulicOilTemperature` to watch it.

What the sample does **not** need a second terminal for is the same walk as an
automated test: `MachineryEndToEndTests` runs this server and this client over
`opc.tcp` inside the test suite.

See the [Machinery developer guide](../../docs/Machinery.md).
