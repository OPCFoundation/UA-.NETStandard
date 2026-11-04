# OPC UA Energy Consumption Management server

This package binds OPC 34100 standby and Wake-on-LAN (WOL) nodes to
application-supplied providers. It uses the model types from `Opc.Ua.ECM`;
it does not generate another copy of that model.

Register `AddEcm()` on an OPC UA server builder, then use `ConfigureEcm`
to resolve providers and call `EcmNodeManager.CreatePowerAsync` for standby
with optional WOL, or `CreateWolAsync` for WOL alone. Direct construction
of `EcmNodeManager`, `EcmPowerBinding` and `EcmWolBinding` is also available.
The address-space owner disposes bindings; providers remain owned by the
application or dependency-injection container.
WOL-only hosting does not require or publish a standby graph.

`SimulatedEcmPowerProvider` changes only in-memory state. It never powers
down the host, sends wake packets or disconnects the server.

Standalone provider-backed E0/E1/D0 measurements, custom accuracy domains and
accuracy-class validation are available through `EcmNodeManager.CreateMeasurementAsync`
and `CreateAccuracyDomainAsync`.
E2/E3/D1 statistical measurements, grouped reset and collection `StartTime`
remain dependent on IA.Server integration. This package does not establish
metrological certification.
