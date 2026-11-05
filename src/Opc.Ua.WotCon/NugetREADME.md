# OPC UA .NET Standard — WoT Connectivity information model

`OPCFoundation.NetStandard.Opc.Ua.WotCon` is the source-generated
proxy assembly for the OPC UA Web of Things Connectivity information
model (OPC 10100-1). It is the shared type contract that the
`Opc.Ua.WotCon.Client` and `Opc.Ua.WotCon.Server` packages depend
on.

## Overview

WoT Connectivity defines a standard companion-spec layer for
binding OPC UA address-space nodes to W3C WoT Thing Descriptions —
useful for asset onboarding pipelines, MQTT bridges, and connectivity
configuration servers.

The generated input is the unpublished **WoT Connectivity 1.1 draft** dated
2026-11-02, corresponding to specification document **1.1-draft10**, and paired
with the **xRegistry 0.7.0 draft** dated 2026-09-12. The incorporated
OPC 10100-1 v1.02 ModelDesign and existing identities remain intact. Generated
successor declarations do not by themselves establish server capability support;
clients must discover the applicable runtime contract.
The current server implements SemVer selection, not the Incremental allocator or
publication channel/snapshot runtime described by the additional declarations.

## Target frameworks

`net48`, `net8.0`, `net9.0`,
`net10.0`.

## Additional documentation

See the [main repository README](https://github.com/OPCFoundation/UA-.NETStandard)
and the
[WoT Connectivity guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/WotCon.md).
