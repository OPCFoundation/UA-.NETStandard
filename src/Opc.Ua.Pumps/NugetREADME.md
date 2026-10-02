# Opc.Ua.Pumps

Server/client-independent foundation for **OPC 40223 — OPC UA for Pumps and
Vacuum Pumps**.

The Pumps NodeSet is **source-generated** here over the source-generated
OPC 40001-1 Machinery and OPC 10000-100 DI base models, exposing the generated
ObjectTypes, enums, typed node states and client proxies, plus the
`AddOpcUaPumps` model loader. The generated `ObjectTypeIds` and
`Opc.Ua.Pumps.Namespaces` classes are the source of truth for the model.

`PumpType` is the entry point: a subtype of the OPC 10000-100 DI
`TopologyElementType` carrying the
`PumpIdentificationType` nameplate and the optional `Design`,
`Implementation`, `SystemRequirements`, `Control`, `Configuration`, `Ports`,
`Operational`, `Events` (supervision) and `Maintenance` groups.
`MultiPumpType` is not a pump: it is the functional group below
`Operational` that describes one pump's role in a multi-pump set.

The model ships the OPC 40223 enumerations and option sets as generated CLR
types — `PumpClassEnum`, `ControlModeEnum`, `OperatingModeEnum`,
`OperationModeEnum`, `PumpRoleEnum`, `PumpKickModeEnum`,
`MultiPumpOperationModeEnum`, `DistributionTypeEnum`, `ExchangeModeEnum`,
`FieldbusEnum`, `MaintenanceLevelEnum`, `StateOfTheItemEnum`,
`PortDirectionEnum`, `ExplosionZoneOptionSet`, `ExplosionProtectionOptionSet`,
`OfferedControlModesOptionSet`, `OfferedFieldbusesOptionSet`,
`DeclarationOfConformityOptionSet` and `PhysicalAddressDataType`.

Alongside the generated model this package carries the **snapshot contracts**
both sides of the wire agree on — `PumpNameplate`, `PumpConfigurationData`,
`PumpOperationalData`, `PumpMaintenanceData`, `PumpSupervisionStatus`,
`PumpPortDescriptor` and `MultiPumpConfiguration`, gathered into one
`PumpSnapshot`, plus `PumpEntry` for discovery. The large OPC 40223 groups are
open sets of optional variables, so their values travel as a `PumpValueSet` of
`PumpValue`s keyed by the generated `BrowseNames`. A server fills them from its
process, a client reads them back, and neither has to reference the other.

For server hosting and a fluent pump-topology API use `Opc.Ua.Pumps.Server`;
for discovery and typed reads over a session use `Opc.Ua.Pumps.Client`.

## Model provenance

`Model/Opc.Ua.Pumps.NodeSet2.xml` is the unmodified OPC Foundation
publication from
[`UA-Nodeset/latest/Pumps`](https://github.com/OPCFoundation/UA-Nodeset/tree/latest/Pumps)
(model version 1.0.0, published 2021-04-19). The sibling
`Opc.Ua.Pumps.NodeSet2.csv` pins the symbol/NodeId mapping so NodeIds stay
stable across builds; the generator fails the build if the two ever disagree.
