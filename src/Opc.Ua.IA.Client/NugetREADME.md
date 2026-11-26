# Opc.Ua.IA.Client

Typed reads, subscriptions, discovery, and statistics reset for OPC 10000-200
Industrial Automation. The client recognizes stacklights, statistics interfaces,
calibration categories, and standard or custom target features over an existing
managed session.

Register `.AddIndustrialAutomationClient()` after configuring the existing
OPC UA client. Resolve `IndustrialAutomationClientFactory` and await
`CreateAsync()`, or construct `IndustrialAutomationClient` directly with a
borrowed `ISession` and telemetry context.

The DI container owns its cached session. Consumers must not close or dispose
it. Generated accessors retain source quality and timestamps; omitted optional
members and rejected operations return errors rather than fabricated values.
Feature inspection preserves native units, resolution, and display names.
Certificate URIs are never fetched.

See the [Industrial Automation guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/IndustrialAutomation.md)
and [public-API sample](https://github.com/OPCFoundation/UA-.NETStandard/tree/master/samples/IA).
