# OPC UA .NET Standard — WoT Connectivity client

`OPCFoundation.NetStandard.Opc.Ua.WotCon.Client` is the client-side helper library for the OPC UA Web of Things Connectivity information model (OPC 10100-1) and the additive WoT Connectivity 1.1 registry surface. It composes the generated proxies from `Opc.Ua.WotCon` with the `Opc.Ua.Client` session surface so applications can browse WoT-configured asset connections, push Thing Descriptions, call connectivity-management methods, and manage the registry's Thing Description / Thing Model groups and resources through a fluent API.

## Overview

Reference this package alongside `Opc.Ua.Client` from any tool that manages connectivity configuration or the registry on a WoT-conformant OPC UA server.

* `WotConnectivityClient` / `WotAssetClient` — the OPC 10100-1 v1.02 asset-connection surface (`WoTAssetConnectionManagement`).
* `WotRegistryClient` / `WotRegistryGroupClient` / `WotRegistryResourceClient` — the WoT Connectivity 1.1 registry surface (`WoTRegistry`): create/get-or-create Thing Description and Thing Model groups and resources, upload document versions through the inherited `FileType` transfer, validate/enable/delete resources, trigger `Refresh`, and load a dependency-ordered batch of documents in one workflow via `LoadDocumentsAsync`. `WotRegistryClient` derives from the shared `XRegistryClient` (package `OPCFoundation.NetStandard.Opc.Ua.XRegistry.Client`), so it inherits the generic registry lifecycle and adds only the WoT-specific surface.

## Target frameworks

`net472`, `net48`, `netstandard2.1`, `net8.0`, `net9.0`,
`net10.0`.

## Additional documentation

See the [main repository README](https://github.com/OPCFoundation/UA-.NETStandard)
and the
[WoT Connectivity guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/WoTConnectivity.md).

The runnable [WoT aggregation sample](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/samples/WotAggregation/README.md) demonstrates `LoadDocumentsAsync`, dependency ordering, endpoint substitution, registry upload, `Refresh`, browsing, and reads from a runtime-materialized Pump.
