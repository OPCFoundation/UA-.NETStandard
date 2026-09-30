# Opc.Ua.PADIM

Source-generated **OPC 30081 — Process Automation Device Information Model
(PADIM)** and the OPC UA **IRDI dictionary** it requires, built over the
source-generated OPC 10000-100 DI base model.

PADIM describes process automation field devices — their signals, ranges,
units and device parameters — and classifies every property against an IEC
61987 / eCl@ss IRDI dictionary entry. Both models ship in this one package
because PADIM is unusable without the dictionary it references.

OPC 40001-2 Machinery Process Values (`Opc.Ua.Machinery.ProcessValues`) derives
its `ProcessValueType` and `ProcessValueVariableType` from PADIM's
`AnalogSignalType` and `AnalogSignalVariableType`.

## Target frameworks

net472, net48, netstandard2.1, net8.0, net9.0, net10.0

## Additional documentation

See the [Machinery developer guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/Machinery.md).
