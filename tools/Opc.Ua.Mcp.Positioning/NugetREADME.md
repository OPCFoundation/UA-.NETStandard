# OPC UA Positioning MCP tools

Read-only Model Context Protocol tools over the existing RSL and GPOS typed
clients, targeting .NET 8, 9 and 10.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.Mcp;

services.AddOpcUaMcpCore();
services.AddOpcUaMcpPositioning();
services.AddMcpServer()
    .WithOpcUaMcpFilters()
    .WithOpcUaPositioningTools(McpToolProfile.Positioning);
```

Connect with the contributed connection tools first. Pass `sessionName` to
select a session, or omit it when exactly one session is active. Each call
resolves that session anew; there is no cached inventory or proxy.

| Tool | Operations |
|---|---|
| `positioning_rsl_list` | `SpatialObjectLists`, `SpatialObjects`, `Frames` |
| `positioning_rsl_read` | `PositionFrame`, `Frame`, `WorldFrame` |
| `positioning_rsl_observe` | `PositionFrame`, `Frame`, `NodeVersion` |
| `positioning_gpos_list` | Zones in GlobalLocations |
| `positioning_gpos_read` | `GlobalPosition`, `GlobalLocation`, `ZoneTransform` |
| `positioning_gpos_transform` | `GlobalToLocal`, `LocalToGlobal` |
| `positioning_gpos_observe` | `GlobalPosition`, `GlobalLocation` |

Discovery pages default to 100 entries, maximum 500. Observations default to
1,000 ms / 100 items and allow at most 30,000 ms / 500 items. They borrow
`ManagedSession.DefaultStreaming`, dispose only their own enumerators, and
propagate caller cancellation. No tool writes, invokes a server method, owns
the shared subscription, or caches a fit.

For example, call `positioning_rsl_read` with:

```json
{
  "nodeId": "ns=2;s=RobotFrame",
  "facet": "WorldFrame",
  "angleUnit": "Radians"
}
```

Use the real NodeId and orientation units from your server. Raw frame/global
reads preserve server values rather than guessing or converting their units.
Their `metadata` contains UA-encoded status and source timestamp; their
structured value is encoded separately using the selected session's context.
Optional structure fields remain absent when not supported/present.

For `positioning_gpos_transform`, `x`/`y` mean longitude/latitude for global
input and X/Y for local input. `z` is optional elevation in metres for global
input, and local Z (default zero) for local input. `angleUnit` specifies
geographic input/output units, while `fitOptions.controlPointAngleUnit`
specifies the server's control-point units independently.

```json
{
  "zoneNodeId": "ns=2;s=FactoryZone",
  "direction": "GlobalToLocal",
  "x": 8.55,
  "y": 47.37,
  "angleUnit": "Degrees",
  "fitOptions": {
    "mode": "Rigid",
    "controlPointAngleUnit": "Degrees",
    "allowReflection": false,
    "coordinateReferenceSystem": "EPSG:4326"
  }
}
```

Fits reuse `GroundControlPointFitter` through `GlobalPositioningClient`.
The default CRS is WGS84/EPSG:4326. For another CRS, register the existing
`ICoordinateReferenceSystemTransformer` interface with DI and explicitly
request its identifier. Mismatched CRS identifiers are rejected, not silently
treated as WGS84. Raw server numeric CRS codes are returned unchanged and
are distinct from the transformer identifier. ZoneTransform reports the
selected dimension, rank, residuals, determinant and invertibility.
