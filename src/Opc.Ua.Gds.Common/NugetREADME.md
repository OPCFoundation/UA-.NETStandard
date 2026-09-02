# OPC UA .NET Standard — GDS common types

`OPCFoundation.NetStandard.Opc.Ua.Gds.Common` contains the shared
information model and DataType definitions used by the OPC UA Global
Discovery Server (GDS) — OPC 10000-12 (Part 12) — and the OPC 10000-21
Onboarding companion model. It is the type-only contract that the client
(`Opc.Ua.Gds.Client.Common`) and server (`Opc.Ua.Gds.Server.Common`) GDS
packages depend on.

## Overview

Reference this package directly only when you need the GDS proxies
without the client / server-side helpers — typically for a tool that
inspects or transcodes GDS payloads outside a full GDS pipeline.

The package ships generated `Opc.Ua.Onboarding` constants, node states,
encodeable types, and typed ObjectType clients. This includes
`DeviceRegistrarState`, `DeviceRegistrarAdminState`,
`DeviceRegistrarAdminTypeClient`, and the well-known
`ObjectIds.DeviceRegistrar_Administration` NodeId.

## Target frameworks

`net472`, `net48`, `netstandard2.1`, `net8.0`, `net9.0`,
`net10.0`.

## Additional documentation

See the [main repository README](https://github.com/OPCFoundation/UA-.NETStandard)
and the
[GDS guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/GDS.md).
