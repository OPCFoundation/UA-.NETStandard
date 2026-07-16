# OPC UA .NET Standard — WoT Connectivity server

`OPCFoundation.NetStandard.Opc.Ua.WotCon.Server` is the server-side
helper library for the OPC UA Web of Things Connectivity information
model (OPC 10100-1). It plugs a WoT-conformant
`AsyncCustomNodeManager` plus pluggable file-system / Thing-Description
providers into a `StandardServer` so the standard
`AssetConnectionManagement` object is exposed without per-server
boilerplate.

## Overview

Reference this package from a custom server that needs to expose
WoT-configured asset connections. The split provider model
(FileSystem, ThingDescription, ConnectionStore) lets each backing
store be swapped independently.

## Target frameworks

`net472`, `net48`, `netstandard2.1`, `net8.0`, `net9.0`,
`net10.0`.

## Additional documentation

See the [main repository README](https://github.com/OPCFoundation/UA-.NETStandard)
and the
[WoT Connectivity guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/WoTConnectivity.md).
