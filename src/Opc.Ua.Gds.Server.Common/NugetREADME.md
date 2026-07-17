# OPC UA .NET Standard — GDS server

`OPCFoundation.NetStandard.Opc.Ua.Gds.Server.Common` is the
server-side library for the OPC UA Global Discovery Server (GDS) —
OPC 10000-12 (Part 12). It implements the GDS service-call surface
on top of `Opc.Ua.Server`, with pluggable `ICertificateGroupProvider`
/ `IApplicationsDatabase` / `ICertificateRequest` providers so the
backing certificate authority and applications store can be swapped
to match the deployment.

## Overview

Reference this package from a GDS executable. The standard
applications-database provider ships an in-process LINQ
implementation; production deployments usually plug a real database
back-end behind the same `IApplicationsDatabase` surface.

## Target frameworks

`net472`, `net48`, `netstandard2.1`, `net8.0`, `net9.0`,
`net10.0`.

## Additional documentation

See the [main repository README](https://github.com/OPCFoundation/UA-.NETStandard)
and the
[GDS guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/GDS.md).
