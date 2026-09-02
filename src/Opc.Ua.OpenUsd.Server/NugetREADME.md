# Opc.Ua.OpenUsd.Server

Server-side reusable functionality for the draft **OPC UA - OpenUSD Bindings** and
**OpenUSD Scene Materialization** companion models.

Built on **Opc.Ua.OpenUsd**, it supports both ways of connecting an OPC UA address
space to OpenUSD.

## Bindings and asset delivery

The `Opc.Ua.OpenUsd.Server` namespace helps a server expose a live external twin:

- author `OpenUsdRepresentation` live, alarm, history, and command bindings;
- author component compositions on represented Objects;
- serve the artist-authored USD asset closure through a read-only OPC UA Part 5
  `FileType`, allowing a connector to fetch and verify the complete stage without an
  external asset resolver.

Pair these APIs with **Opc.Ua.OpenUsd.Client** on the connector side.

## Scene materialization

The `Opc.Ua.OpenUsd.Server.Scene` namespace materializes a composed USD stage inside
the OPC UA address space and exports it back to a scene document:

| API | Purpose |
| --- | --- |
| `ISystemContext.MaterializeUsdStage(...)` | Materializes a `UsdStage` as a `UsdStageType` Object with its prim tree. |
| `ISystemContext.ExportUsdStage(...)` | Reads a materialized stage back into a scene document. |
| `ISystemContext.EnsureStagesFolder(...)` | Finds or creates the discovery folder containing materialized stages. |
| `UsdMaterializationResult.TryResolveBindingTarget(...)` | Resolves the scene attribute targeted by a Part 1 binding. |

`UsdMaterializationOptions` selects the optional composition, applied-schema, metadata,
georeference, and historical-access conformance units.

Notable behavior:

- Known UsdGeom and UsdShade schemas are materialized as typed ObjectType subtypes.
- Unknown typed schemas, API schemas, and value types are retained for faithful export.
- Each attribute retains its exact `SdfValueTypeName` so many-to-one OPC UA DataType
  mappings remain reversible.
- Recognized Cesium georeference schemas can be dual-authored through the portable
  companion types.
- Live attributes can be server-maintained and exposed through HistoricalAccess.

Both companion models are drafts; their APIs, model shapes, and NodeIds may change until
the specifications are ratified.
