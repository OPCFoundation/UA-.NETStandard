# OPC UA .NET Standard — Devices (DI) client

`OPCFoundation.NetStandard.Opc.Ua.Di.Client` is the client-side
helper library for the OPC UA Devices information model
(OPC 10000-100, also referred to as **DI**). It composes the
generated proxies from `Opc.Ua.Di` with the high-level
`Opc.Ua.Client` session surface so DI consumers can browse, read, and
invoke device-level methods from a fluent API.

## Overview

Reference this package from any application that needs to interact
with DI-conformant servers (BlockType, DeviceType, OnlineAccess, …)
without re-implementing the standard service calls by hand.

## Target frameworks

`net472`, `net48`, `netstandard2.1`, `net8.0`, `net9.0`,
`net10.0`.

## Additional documentation

See the [main repository README](https://github.com/OPCFoundation/UA-.NETStandard)
and the
[OPC 10000-100 specification](https://reference.opcfoundation.org/DI/v103/docs/).
