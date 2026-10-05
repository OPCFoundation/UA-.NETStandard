# Opc.Ua.AMB

Source-generated **OPC 10000-110 Asset Management Basics (AMB)** information
model, version 1.01.1.

The package exposes the generated ObjectTypes (`IRootCauseIndicationType`,
`IMaintenanceEventType`, `MaintenanceEventStateMachineType`,
`DocumentationLinksType` and the fourteen AMB condition classes), the
`RootCauseDataType`, `NameNodeIdDataType` and `MaintenanceMethodEnum` data
types, the `Contains`, `HierarchicalContains` and `OperationalContains`
reference types, typed node states and client proxies, plus the `AddOpcUaAMB`
model loader. The generated `ObjectTypes`, `DataTypes`, `ReferenceTypes`,
`Objects` and `Methods` classes and `Opc.Ua.AMB.Namespaces` are the source of
truth for the model; the predefined `Assets`, `AssetsByProductInstanceUri` and
`AssetsByAssetId` alias categories and the `HierarchicalLocations` and
`OperationalLocations` entry points are part of it.

`AmbBrowseNames` adds the browse names AMB defines without a node of its own
(`Requirements`, `Capabilities`, `HierarchicalLocation`, `OperationalLocation`,
`DigitalLocation` and the `NoAssetIdAssigned` alias name), `AmbConditionClass`
lists the condition classes with the standard class each one refines, and the
contracts `MaintenanceStateKind`, `AssetFaultSeverity` and `AssetLocationKind`
carry the state numbers of the maintenance state machine, the severity bands
of Table 15 and the three kinds of location the server and client packages
share.

The NodeSet is the unmodified OPC Foundation publication from
`UA-Nodeset/latest/AMB` (model version 1.01.1, 2024-02-27). It depends on the
base namespace only; the Device Integration building blocks AMB applies to an
asset are used by the server and client packages.

## Target frameworks

net48, net8.0, net9.0, net10.0
