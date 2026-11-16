# OPC UA .NET Standard — Local Discovery Server (LDS / LDS-ME)

`OPCFoundation.NetStandard.Opc.Ua.Lds.Server` implements the OPC UA
Local Discovery Server (LDS) and Local Discovery Server with
Multicast Extension (LDS-ME). It accepts inbound `FindServers` /
`FindServersOnNetwork` / `RegisterServer` / `RegisterServer2` calls
and re-publishes the registered endpoints over mDNS for the local
network.

## Overview

Reference this package from an LDS / LDS-ME executable. The package
is intended to replace the C++ LDS in .NET deployments.

## Target frameworks

`net48`, `net10.0`,
`net11.0`.

## Additional documentation

See the [main repository README](https://github.com/OPCFoundation/UA-.NETStandard)
and the
[Global Discovery Server guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/GDS.md).
