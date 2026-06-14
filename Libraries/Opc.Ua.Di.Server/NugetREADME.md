# OPC UA .NET Standard — Devices (DI) server

`OPCFoundation.NetStandard.Opc.Ua.Di.Server` is the server-side
helper library for the OPC UA Devices information model
(OPC 10000-100, also referred to as **DI**). It plugs DI-conformant
node managers into a `StandardServer` so the standard
TopologyElement / Block / Device / OnlineAccess types are exposed
without per-server boilerplate.

## Overview

Reference this package from a custom server that needs to advertise
DI-conformant assets and from the GDS / push-management server
scenarios that lean on DI for asset metadata.

## Target frameworks

`net472`, `net48`, `netstandard2.0`, `netstandard2.1`, `net8.0`,
`net9.0`, `net10.0`.

## Additional documentation

See the [main repository README](https://github.com/OPCFoundation/UA-.NETStandard)
and the
[OPC 10000-100 specification](https://reference.opcfoundation.org/DI/v103/docs/).
