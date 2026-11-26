# Opc.Ua.IA

Source-generated **OPC 10000-200 Industrial Automation (IA)** information model,
built over the source-generated OPC UA DI base model.

The package exposes the generated ObjectTypes, ReferenceTypes, enums, typed node
states and client proxies, plus the `AddOpcUaIA` model loader. The generated
`ObjectTypeIds` / `VariableTypeIds` / `ReferenceTypeIds` classes and
`Opc.Ua.IA.Namespaces` are the source of truth for the model.

IA supplies stacklight, statistics and calibration-target building blocks that other
companion specifications compose — notably OPC 40010 Robotics
(`Opc.Ua.Robotics`), OPC 40001-1 Machinery (`Opc.Ua.Machinery`) and OPC 34100
Energy Consumption Management (`Opc.Ua.ECM`).

## Target frameworks

net48, net10.0, net11.0

## Additional documentation

See the [Device Integration developer guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/DeviceIntegration.md)
and the [Industrial Automation guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/IndustrialAutomation.md)
for the separate server and client runtime packages.
