# Opc.Ua.ECM

Source-generated **OPC 34100 — Energy Consumption Management (ECM)** information
model, built over the source-generated OPC 10000-200 IA and OPC 10000-100 DI
base models.

It supplies the energy measurement vocabulary that production equipment reports
against: `EnergyMeasurementType` and `EnergyMeasurementValueType` with their
accuracy classes and measurement periods, the energy saving mode model
(`EnergySavingModeType`, `EnergySavingModeStatusType`,
`EnergySavingModesContainerType`, `EnergyStandbyManagementType`,
`EnergyDevicePowerOffType`), the E0-E3 and D0/D1 energy profile interfaces, and
the `AcPeDataType` / `AcPpDataType` / `EnergyStateInformationDataType` /
`MeasurementPeriodDataType` / `StandbyModeTransitionDataType` structures.

OPC 40001-4 Machinery Energy Management (`Opc.Ua.Machinery.Energy`) builds on
this model.

## Target frameworks

net48, net8.0, net9.0, net10.0

## Additional documentation

See the [ECM developer guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/ECM.md)
for standalone standby/WOL server bindings, typed clients and samples.
Standalone measurement profiles, accuracy domains and shared IA statistics
integration remain incomplete; specification support is Partial.
The [Machinery developer guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/Machinery.md)
describes the existing energy-management composition.
