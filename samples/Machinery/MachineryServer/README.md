# MachineryServer

A self-contained OPC UA server that publishes one simulated hydraulic press
using every part of the OPC 40001 series.

```sh
dotnet run --project samples/Machinery/MachineryServer -- --port 62546
```

## What the address space contains

The press is `Organizes`-referenced from the OPC 40001-1 `Machines` folder —
which hangs off `Objects`, not off the Device Integration `DeviceSet`. Below it:

| Part | What the sample builds |
| --- | --- |
| 40001-1 | `Identification`, `Monitoring` (item state, operation mode, health, process, consumption), `Components` with one component and its lifetime counters, `MachineryEquipment` with one die, `Notifications`, `OperationCounters` |
| 40001-2 | `HydraulicOilTemperature`, a process value over PADIM's analog-signal model, with limits and a setpoint |
| 40001-3 | `JobManagement`, whose eleven job verbs come from the ISA-95 Job Control V2 model and bind to the in-memory provider |
| 40001-4 | `CompressedAirSupply`, tied to the well-known `CompressedAir` carrier with the Energy model's `Contains` reference |
| 40001-101 | `ResultManagement`, including the `ResultTransfer` download path |

## What the simulation does

[`PressSimulation`](PressSimulation.cs) runs one stroke every four seconds. Each
stroke moves `MachineryItemState` to `Executing` and back, sets
`MachineryOperationMode` to `Processing`, and publishes a result whose CSV
payload a client downloads through `GenerateFileForRead`.

State only ever moves from the server: OPC 40001-1 declares no cause method on
either state machine — the whole Machinery NodeSet contains no method at all —
so a client observes state and cannot request it.

## Where the numbers come from

[`PressDatasheet`](PressDatasheet.cs) holds every published value, so the
address space and the simulation cannot disagree.
