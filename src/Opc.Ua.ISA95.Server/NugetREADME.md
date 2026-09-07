# OPC UA .NET Standard — ISA-95 server

`OPCFoundation.NetStandard.Opc.Ua.ISA95.Server` hosts the OPC-10030 common model and OPC-10031-4 Job Control V1/V2 through the stack's dependency-injection server model.

It includes typed common-model builders, pluggable Job Control providers, and a deterministic in-memory provider for development and single-process deployments.

## Overview

`services.AddOpcUa().AddServer(...).AddIsa95Server(...).ConfigureModel(...)` registers one multi-namespace `Isa95NodeManager` hosting all three namespaces, the typed `IIsa95ModelBuilder` common-model builder, the provider-backed `GeoSpatialLocationType` seam, and the default in-memory Job Control provider. Register a cohesive custom provider set beforehand to replace the in-memory store for durable/HA deployments; partial custom facets are not combined with default facets. See the [ISA-95 guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/ISA95.md) for the full hosting model, Job Control V1/V2 state engine, concrete status-event projection, and in-memory limitations.

## Additional documentation

See the [ISA-95 guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/ISA95.md).
