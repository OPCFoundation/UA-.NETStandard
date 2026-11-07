# OPC UA PA-DIM client

This package discovers OPC 30081 PA-DIM devices and vendor subtypes, reads their
typed values and instance dictionary references, and calls operations through
generated proxies. Managed sessions provide subscribed value changes.

Register `AddPadimClient()` after `AddClient()` and resolve
`PadimClientFactory`, or construct `PadimClient` with an existing session and
telemetry context. The client borrows the session; it does not close, dispose
or replay operations on it.

See the [PA-DIM guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/PADIM.md)
for discovery bounds, optional capabilities, fluent setup and the demonstration.
