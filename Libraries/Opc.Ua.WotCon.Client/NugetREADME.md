# OPC UA .NET Standard — WoT Connectivity client

`OPCFoundation.NetStandard.Opc.Ua.WotCon.Client` is the client-side
helper library for the OPC UA Web of Things Connectivity information
model (OPC 10100-1). It composes the generated proxies from
`Opc.Ua.WotCon` with the `Opc.Ua.Client` session surface so
applications can browse WoT-configured asset connections, push Thing
Descriptions, and call connectivity-management methods through a
fluent API.

## Overview

Reference this package alongside `Opc.Ua.Client` from any tool that
manages connectivity configuration on a WoT-conformant OPC UA
server.

## Target frameworks

`net472`, `net48`, `netstandard2.1`, `net8.0`, `net9.0`,
`net10.0`.

## Additional documentation

See the [main repository README](https://github.com/OPCFoundation/UA-.NETStandard)
and the
[WoT Connectivity guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/WoTConnectivity.md).
