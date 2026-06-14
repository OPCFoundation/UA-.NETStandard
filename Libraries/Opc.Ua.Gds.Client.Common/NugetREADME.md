# OPC UA .NET Standard — GDS client

`OPCFoundation.NetStandard.Opc.Ua.Gds.Client.Common` is the
client-side library for the OPC UA Global Discovery Server (GDS) —
OPC 10000-12 (Part 12). It implements the GDS service-call surface
(`RegisterApplication`, `FindApplications`, `GetCertificateGroups`,
`StartSigningRequest`, `FinishRequest`, `GetTrustList`, …) on top of
`Opc.Ua.Client`, with the certificate-push management flows for
servers that delegate PKI to a GDS.

## Overview

Reference this package from any application or tool that needs to
talk to a GDS — typically a provisioning utility, a certificate
authority bridge, or a server that uses GDS-driven push management.

## Target frameworks

`net472`, `net48`, `netstandard2.1`, `net8.0`, `net9.0`,
`net10.0`.

## Additional documentation

See the [main repository README](https://github.com/OPCFoundation/UA-.NETStandard)
and the
[GDS guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/GDS.md).
