# Opc.Ua.Scales

Source-generated **OPC 40200 Weighing Technology (Scales)** information model,
version 2.00, over the OPC 10000-100 DI, OPC 10000-200 IA, OPC 40001-1
Machinery and OPC 30050 PackML base models.

The package exposes the generated ObjectTypes (`ScaleDeviceType` and its
simple, laboratory, checkweigher, catchweigher, filling, continuous,
loss-in-weight, piece-counting, recipe, hopper, totalizing-hopper, vehicle and
price-labeller subtypes, `ScaleSystemType`, the modules, products, recipes and
statistics), the weight data types (`AbstractWeightType`, `WeightType`,
`PrintableWeightType`), enums, typed node states and client proxies, plus the
`AddOpcUaScales` model loader.

The client and server libraries build on this package:

- `Opc.Ua.Scales.Server` hosts scales in a server address space.
- `Opc.Ua.Scales.Client` discovers and reads scales from a client.

The NodeSet is the unmodified OPC Foundation publication from
`UA-Nodeset/latest/Scales` (model `http://opcfoundation.org/UA/Scales/V2/`,
version 2.00, 2025-03-01).

## Target frameworks

net48, net8.0, net9.0, net10.0

## Additional documentation

See the [Scales developer guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/Scales.md).
