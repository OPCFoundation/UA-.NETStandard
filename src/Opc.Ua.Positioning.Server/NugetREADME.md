# OPC UA .NET Standard — Positioning Server

`OPCFoundation.NetStandard.Opc.Ua.Positioning.Server` hosts the OPC UA
Relative Spatial Location (OPC 10000-210) and Global Positioning
(OPC 10000-211) models.

It provides a reusable node manager, address-space builders, provider
contracts, dependency-injection integration, and a composition path for
servers that already own a companion node manager.

The package depends on `OPCFoundation.NetStandard.Opc.Ua.Positioning` for the
source-generated model and transformation primitives.

Use `AddPositioningServer()` for standalone hosting or
`AddPositioningFor<TNodeManager>()` to compose RSL/GPOS into an existing
companion node manager. `PositioningAddressSpaceBuilder` creates spatial
lists, SpatialObject AddIns, frames, Zones, GlobalPosition Variables, and
GlobalLocation Variables. Technology-neutral global and relative providers
publish cancellation-aware streams with quality and timestamps.
