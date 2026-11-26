# OPC UA Energy Consumption Management client

This package uses generated ECM proxies for standby control and WOL sleep,
separately or together. `EcmClient` borrows an existing session, discovers standby
objects by type and retains Call status separately from the device's
`ReturnCode`. A bad Call status has no interpreted device return code.

Generated `EcmClientMembers` accessors provide typed reads, writes and
observations with the original quality and source/server timestamps.
`PauseTimeVariable()` exposes the writable PauseTime variable. It does
not invent a PauseTime method.

Register `AddEcmClient()` after `AddClient(...)`, then resolve
`EcmClientFactory` to create a client for the selected standby/WOL NodeIds.
The session remains owned by the container; factory consumers must not close
or dispose it. `EcmClient.DiscoverAsync` finds direct standby children beneath
an application-supplied parent by type, including vendor subtypes.
For WOL without standby, use `EcmWolClient.DiscoverAsync` and the factory's
`CreateWolAsync`. The returned client's generated `PowerOff` proxy exposes
MAC metadata and explicit sleep control without any standby NodeId.

The client never sends wake packets or automatically repeats a power
command after a disconnect. Applications own any physical wake transport.

`EcmMeasurementClient` discovers standalone measurement points and exposes
generated typed accessors for `ApplicationTag` and every E0, E1, E2, E3, D0 and D1
member, for example E0 `AcCurrentPe`, E1 `AcActivePowerTotal`, E2
`AcActiveEnergyTotalExportLp`, E3 `AcVoltagePp` and D1 `DcElectricalCharge`.
Reads return the value together with its original quality and source/server
timestamps. `AcPeDataType` and `AcPpDataType` stay structures; they are not
flattened into scalars. `ReadStatisticsStartTimeAsync` reads the collection
`StartTime`, and `ResetStatisticsAsync` calls the single shared
`ResetStatistics` method through the generated IA proxy. An authorization
failure surfaces as the Call status and the client never retries it.
