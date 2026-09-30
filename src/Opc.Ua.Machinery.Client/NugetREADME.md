# OPCFoundation.NetStandard.Opc.Ua.Machinery.Client

Client-side helpers for the OPC 40001 *OPC UA for Machinery* specification
series.

A Machinery server publishes its machines in one well-known place — the
`Machines` folder that OPC 40001-1 organizes from `Objects`, **not** from the
Device Integration `DeviceSet`. This package starts there and hands back typed
access to what a machine exposes.

```csharp
MachineryClient machinery = session.Machinery(telemetry);

await foreach (MachineEntry machine in machinery.EnumerateMachinesAsync(ct))
{
    MachineIdentification id = await machinery.ReadIdentificationAsync(machine.NodeId, ct);
    Console.WriteLine($"{id.Manufacturer} {id.Model} — {id.SerialNumber}");

    FiniteStateSnapshot? state = await machinery.GetItemStateAsync(machine.NodeId, ct);
    Console.WriteLine($"  state: {state?.CurrentState}");
}
```

| Concern | API |
| --- | --- |
| Discovery | `EnumerateMachinesAsync`, `DiscoverMachinesAsync`, `FindComponentsAsync` |
| Identification | `ReadIdentificationAsync` |
| State | `GetItemStateAsync`, `ObserveItemStateAsync`, `GetOperationModeAsync`, `ObserveOperationModeAsync` |
| Process values | `ReadProcessValueAsync` |
| Jobs | `JobManagement(machine)` → `Isa95JobControlV2Client` |
| Results | `ResultManagement(machine)`, `DownloadResultAsync` |

State observation rides the generated state-machine proxies, so it works
against any server that publishes the OPC 40001-1 state machines — including
vendor subtypes.

See the [Machinery developer guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/Machinery.md).
