# OPC UA .NET Standard — Positioning Client

`OPCFoundation.NetStandard.Opc.Ua.Positioning.Client` provides high-level
clients for OPC UA Relative Spatial Location (OPC 10000-210) and Global
Positioning (OPC 10000-211).

The package composes the source-generated proxies with continuation-safe
discovery, typed reads, subscriptions, frame-chain resolution, Zone
transformation, and dependency-injection registration.

Use `AddPositioningClient()` over the managed-session registration or construct
`RelativeSpatialLocationClient` and `GlobalPositioningClient` directly. The
clients decode generated structured values without boxed APIs and expose
`IStreamingSubscription`-based position, location, and NodeVersion streams.
