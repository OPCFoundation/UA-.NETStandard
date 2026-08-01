# Opc.Ua.OpenUsdScene.Server

Server-side functionality for the draft **OPC UA — OpenUSD Scene Materialization (Part 2)**
specification: it materializes a composed USD stage into an OPC UA address space, and reads it
back out again.

Where the Bindings model (Part 1) keeps the USD scene *outside* OPC UA and binds process values
to it, Scene Materialization puts the scene *inside* the address space — the prim hierarchy
becomes the node hierarchy, so browsing the server is browsing the scene.

## What it does

| API | Purpose |
|---|---|
| `ISystemContext.MaterializeUsdStage(...)` | Materializes a `UsdStage` as a `UsdStageType` Object with the full prim tree (§7.1) |
| `ISystemContext.ExportUsdStage(...)` | Reads a materialized stage back into a scene document (§7.2) |
| `ISystemContext.EnsureStagesFolder(...)` | Finds or creates the discovery folder stages live under (§4.3) |
| `UsdMaterializationResult.TryResolveBindingTarget(...)` | Resolves the attribute a Part 1 binding targets (§10) |

## Conformance units

`UsdMaterializationOptions` switches map one-to-one onto the specification's conformance units
(§12), so a Server materializes only what it needs. **Scene Structure** is the always-on
baseline; `MaterializeComposition`, `MaterializeAppliedSchemas`, `MaterializeMetadata`,
`DualAuthorPortableGeoreference` and `HistorizeLiveAttributes` select the rest.

## Notable behaviour

- **Typed prims.** A prim whose `typeName` is a known UsdGeom or UsdShade schema is
  materialized as the matching ObjectType subtype (§5.3). An **unknown** typed schema is never
  dropped — it stays a `UsdPrimType` carrying its `TypeName` token, so an exporter reproduces
  it faithfully (§8.4). The same applies to unknown API schemas and unknown value types.
- **Reversible typing.** Every attribute records the exact `SdfValueTypeName` in `UsdTypeName`,
  which is what makes the many-to-one §6.2 DataType mapping reversible.
- **Georeferencing.** When a Cesium georeference or globe-anchor schema is recognised, the
  portable `UsdGeoreferenceApiType` / `UsdGlobeAnchorApiType` is dual-authored with the same
  values, so a generic client reads the anchor from one well-known type (§5.8, Annex B). A
  partial anchor is not published — a wrong position is worse than none.
- **Live attributes.** An attribute flagged live is materialized for Mode A (§9): the Value is
  server-maintained, and where retained it is exposed through HistoricalAccess. Driving those
  values is a Server-side responsibility — an external Part 1 connector authors into a USD sink
  and cannot write an in-server Variable.

## Related packages

- `Opc.Ua.OpenUsdScene` — the companion information model plus the `.usda` reader/writer
- `Opc.Ua.OpenUsd` / `Opc.Ua.OpenUsd.Server` — the Part 1 Bindings model

> This package tracks a **draft** specification; APIs, model URI and NodeIds may change.
