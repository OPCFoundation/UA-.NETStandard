# Opc.Ua.Machinery.Energy

Source-generated **OPC 40001-4 — OPC UA for Machinery, Part 4: Energy
Management** information model.

The model is interface-shaped: `IBaseFlowType`, `IVolumeFlowType`,
`IMassFlowType` and `INonElectricalEnergyType` are applied to a machine or an
equipment object to declare which energy carriers it consumes or produces, and
the `Contains` reference type relates a carrier to its sub-measurements. It
ships a ready-made instance tree of the common carriers — electricity,
compressed air, cooling lubricant, natural gas, saturated and superheated steam,
chilled/hot/hot-hot water, crude, fuel, diesel oil, gasoline, propane, biogas
and hydraulic oil.

The measurements themselves are OPC 34100 ECM values, which ship in
`Opc.Ua.ECM`; this package references it.

## Target frameworks

net472, net48, netstandard2.1, net8.0, net9.0, net10.0

## Additional documentation

See the [Machinery developer guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/Machinery.md).
