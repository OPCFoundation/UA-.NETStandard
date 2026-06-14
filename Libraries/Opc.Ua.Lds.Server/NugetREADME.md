# OPC UA .NET Standard — Local Discovery Server (LDS / LDS-ME)

`OPCFoundation.NetStandard.Opc.Ua.Lds.Server` implements the OPC UA
Local Discovery Server (LDS) and Local Discovery Server with
Multicast Extension (LDS-ME). It accepts inbound `FindServers` /
`FindServersOnNetwork` / `RegisterServer` / `RegisterServer2` calls
and re-publishes the registered endpoints over mDNS for the local
network.

## Overview

Reference this package from an LDS / LDS-ME executable. The package
is intended as a turn-key replacement for the C++ LDS for
.NET-friendly deployments.

## Target frameworks

`net472`, `net48`, `netstandard2.0`, `netstandard2.1`, `net8.0`,
`net9.0`, `net10.0`.

## Additional documentation

See the [main repository README](https://github.com/OPCFoundation/UA-.NETStandard)
and the
[Discovery guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/Discovery.md).
