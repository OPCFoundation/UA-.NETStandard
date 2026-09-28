# Opc.Ua.Machinery.Result

Source-generated **OPC 40001-101 — OPC UA for Machinery, Part 101: Result
Transfer** information model.

The model is standalone on the base OPC UA namespace — it declares no dependency
on Machinery 40001-1, DI or IA — so a server that only publishes measurement or
inspection results does not have to carry the machine model.

It supplies `ResultManagementType` with the `GetLatestResult`, `GetResultById`,
`GetResultIdListFiltered`, `AcknowledgeResults` and `ReleaseResultHandle`
methods, `ResultTransferType` (a `TemporaryFileTransferType` subtype for handing
the payload over as a file), the `ResultReadyEventType` event, and the
`ResultDataType` / `ResultMetaDataType` / `ResultTransferOptionsDataType` /
`ProcessingTimesDataType` data types with their binary, XML and JSON encodings.

## Target frameworks

net472, net48, netstandard2.1, net8.0, net9.0, net10.0

## Additional documentation

See the [Machinery developer guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/Machinery.md).
