# Opc.Ua.Machinery.Jobs

Source-generated **OPC 40001-3 — OPC UA for Machinery, Part 3: Job Management**
information model.

`JobManagementType` composes the OPC 10031-4 ISA-95 Job Control V2 endpoints —
`JobOrderControl` (an `ISA95JobOrderReceiverObjectType`) and `JobOrderResults`
(an `ISA95JobResponseProviderObjectType`) — and adds the machinery-specific
result vocabulary: the `JobExecutionMode`, `JobResult` and `ProcessIrregularity`
enumerations and the `OutputInformationDataType`, `BOMInformationDataType`,
`BOMComponentInformationDataType` and `OutputPerformanceInfoDataType`
structures, with their binary and XML encodings.

The job verbs themselves (`Store`, `StoreAndStart`, `Start`, `Stop`, `Abort`,
`Pause`, `Resume`, `Cancel`, `Clear`, `RevokeStart`, `Update`) belong to Job
Control V2 and ship in `Opc.Ua.ISA95`, which this package references. The model
declares no dependency on Machinery 40001-1, DI or IA.

## Target frameworks

net472, net48, netstandard2.1, net8.0, net9.0, net10.0

## Additional documentation

See the [Machinery developer guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/Machinery.md)
and the [ISA-95 developer guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/ISA95.md).
