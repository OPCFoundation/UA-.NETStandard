# Opc.Ua.Machinery

Server/client-independent foundation for **OPC 40001-1 — OPC UA for Machinery,
Part 1: Basic Building Blocks**.

The Machinery NodeSet is **source-generated** here over the source-generated
OPC 10000-200 IA and OPC 10000-100 DI base models, exposing the generated
ObjectTypes, enums, typed node states and client proxies, plus the
`AddOpcUaMachinery` model loader. The generated `ObjectTypeIds` and
`Opc.Ua.Machinery.Namespaces` classes are the source of truth for the model.

The model supplies the building blocks every machine shares: the `Machines`
folder under `Objects`, `MachineIdentificationType` /
`MachineryItemIdentificationType` /
`MachineryComponentIdentificationType` nameplates, the
`MachineryItemState_StateMachineType` and
`MachineryOperationModeStateMachineType` state machines, operation and lifetime
counters, `MachineComponentsType`, `MonitoringType` and `NotificationsType`.

The other parts of OPC 40001 ship as their own packages and can be added
independently:

| Package | Specification |
| --- | --- |
| `Opc.Ua.Machinery.ProcessValues` | OPC 40001-2 Process Values |
| `Opc.Ua.Machinery.Jobs` | OPC 40001-3 Job Management |
| `Opc.Ua.Machinery.Energy` | OPC 40001-4 Energy Management |
| `Opc.Ua.Machinery.Result` | OPC 40001-101 Result Transfer |

For server hosting and a fluent topology API use `Opc.Ua.Machinery.Server`; for
typed client access use `Opc.Ua.Machinery.Client`.

## Target frameworks

net472, net48, netstandard2.1, net8.0, net9.0, net10.0

## Additional documentation

See the [Machinery developer guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/Machinery.md).
