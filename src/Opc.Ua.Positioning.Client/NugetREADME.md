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

## Opt-in strict reads

The existing two-argument constructors and `AddPositioningClient()` retain their
tolerant behavior. Supply immutable read options to opt into strict validation:

```csharp
var options = new PositioningClientReadOptions
{
    MaximumBrowseReferences = 1024,
    MaximumBrowsePages = 128,
    MaximumFrameChainLength = 128,
    MaximumReadArrayItems = 1024
};
var relative = new RelativeSpatialLocationClient(session, telemetry, options);
var global = new GlobalPositioningClient(session, telemetry, options);

// Or, on an IOpcUaClientBuilder:
clientBuilder.AddPositioningClient(options);
```

All limits must be positive. Discovery counts every raw reference and every
page (including empty pages), before accumulating references. Each discovery
enumeration currently browses one parent. Exceeding a limit fails with
`BadEncodingLimitsExceeded`; it never returns a truncated result. RSL resolution
checks the frame-chain limit before reading another frame. Structure arrays,
including GroundControlPoints, are bounded before allocating typed arrays or
fitting. Incoming message bytes and initial protocol decoding remain governed
by the session's transport quotas, not these options.

Strict reads require Good service and value quality, exact response counts, and
unique, complete, local browse-path targets. Only a genuine optional
`BadNoMatch` represents absence. Missing model namespaces are rejected without
appending them to the session namespace table. Base, SourceId, CRS, and structure
errors preserve their status; CRS must be an actual UInt32, not a StatusCode.
Strict streams reject non-Good values rather than emit a successful projection.

Strict RSL reads request the frame and Base together; one request reduces skew
but does not promise an atomic server snapshot. GlobalLocation retains its
three-attempt aggregate/component coherence check. Geometry validation rejects
non-finite inputs and results, reusing the existing RSL, GCP-fitting, and WGS84
math. WGS84 ranges are applied only for EPSG:4326 (or a WGS84 fitter); other CRS
identifiers remain uninterpreted rather than being silently treated as WGS84.

Clients borrow sessions and streaming subscriptions. Browse cleanup releases
only acquired continuation points, with a fresh five-second cleanup token.
Primary and cleanup failures are both retained. Generated proxies returned by
the `Open...` methods retain their original contracts; the read options govern
the high-level clients, not arbitrary session or proxy operations.
