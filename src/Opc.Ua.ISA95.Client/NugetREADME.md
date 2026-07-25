# OPC UA .NET Standard — ISA-95 client

`OPCFoundation.NetStandard.Opc.Ua.ISA95.Client` provides typed discovery, common-model traversal, Job Control V1/V2 method wrappers, and V2 status-event streaming over `ManagedSession`.

## Overview

`Isa95Client` discovers OPC-10030 common-model objects and Job Control V1/V2 endpoints (including vendor-defined subtypes) with continuation-safe browsing (`ManagedBrowseAsync`), and creates the direct `Isa95JobControlV1Client`/`Isa95JobControlV2Client` wrappers. Direct clients register the required Common/V1/V2 encodeables with their session, while `AddIsa95Client` registers a lazily-connecting client factory alongside `AddClient`. See the [ISA-95 guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/ISA95.md#client) for full client usage, including typed V2 status-event streaming.

## Additional documentation

See the [ISA-95 guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/ISA95.md).
