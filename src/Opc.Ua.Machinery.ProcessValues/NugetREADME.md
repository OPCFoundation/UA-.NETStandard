# Opc.Ua.Machinery.ProcessValues

Source-generated **OPC 40001-2 — OPC UA for Machinery, Part 2: Process Values**
information model.

It supplies `ProcessValueType` — a PADIM `AnalogSignalType` extended with limit
and deviation alarms, a setpoint, a status word, alarm suppression and the
`ZeroPointAdjustment` method — together with `ProcessValueVariableType`,
`ProcessValueSetpointVariableType` and the `ZeroPointAdjustmentEventType` event.

The model derives from OPC 30081 PADIM, which ships in `Opc.Ua.PADIM` along
with the IRDI dictionary; this package references it.

## Target frameworks

net472, net48, netstandard2.1, net8.0, net9.0, net10.0

## Additional documentation

See the [Machinery developer guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/Machinery.md).
