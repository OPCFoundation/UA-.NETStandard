# OPC UA .NET Standard — Devices (DI) information model

`OPCFoundation.NetStandard.Opc.Ua.Di` is the source-generated proxy
assembly for the OPC UA Devices information model (OPC 10000-100,
also referred to as **DI**) and the Package Metadata information
model. It is the shared type contract that the
`Opc.Ua.Di.Client` and `Opc.Ua.Di.Server` packages depend on.

## Overview

Reference this package when you need the strongly-typed DI proxies
(`TopologyElementState`, `DeviceState`, `BlockState`, …) without the
client or server-side helpers — for example to round-trip DI nodes
through an encoder.

## Target frameworks

`net472`, `net48`, `netstandard2.0`, `netstandard2.1`, `net8.0`,
`net9.0`, `net10.0`.

## Additional documentation

See the [main repository README](https://github.com/OPCFoundation/UA-.NETStandard)
and the
[OPC 10000-100 specification](https://reference.opcfoundation.org/DI/v103/docs/)
for the authoritative information model.
