# Opc.Ua.OpenUsdScene

Companion information model for the draft **OPC UA — OpenUSD Scene Materialization (Part 2)**
specification, source-generated from the shared `Opc.Ua.OpenUsdScene.NodeSet2.xml`.

Where the Bindings model (Part 1, `Opc.Ua.OpenUsd`) keeps the USD scene *outside* OPC UA and binds
process values to it, Scene Materialization puts the scene *inside* the address space: the composed
prim tree becomes the OPC UA node hierarchy, so browsing the server is browsing the scene.

## What is in the model

| Area | Types |
|---|---|
| Stage and prims | `UsdStageType`, `UsdPrimType`, `UsdTypedType` |
| Typed prim hierarchy | `UsdGeomImageableType`, `UsdGeomScopeType`, `UsdGeomXformableType`, `UsdGeomXformType`, `UsdGeomGprimType`, `UsdGeomMeshType`, `UsdGeomCylinderType`, `UsdGeomSphereType`, `UsdGeomCubeType`, `UsdGeomConeType`, `UsdGeomCapsuleType`, `UsdShadeMaterialType`, `UsdShadeShaderType` |
| Properties | `UsdAttributeType` (VariableType), `UsdRelationshipType` |
| Composition | `UsdCompositionArcType`, `UsdVariantSetType` |
| Applied schemas | `UsdApiSchemaType`, `UsdCollectionAPIType` |
| Georeferencing | `UsdGeoreferenceApiType`, `UsdGlobeAnchorApiType` |
| Enumerations | `UsdSpecifierEnum`, `UsdVariabilityEnum`, `UsdPrimKindEnum`, `UsdListOpTypeEnum`, `UsdArcKindEnum` |
| Value-role DataTypes | `UsdToken`, `UsdAssetPath`, `UsdTimeCode`, `UsdColor3f`, `UsdNormal3f`, `UsdPoint3f`, `UsdVector3f`, `UsdTexCoord2f`, `UsdQuatf`, `UsdQuatd`, `UsdMatrix4d` |
| ReferenceTypes | `UsdRelationshipTarget`, `UsdConnection` |

The value-role DataTypes follow the OPC UA idiom of conveying meaning by extending a primitive
(`Duration : Double`, `UtcTime : DateTime`), so a role such as *colour* versus *point* is discoverable
from the type system while the built-in encoding stays unchanged for generic clients.

## Related packages

- `Opc.Ua.OpenUsdScene.Server` — materializes a scene into a server address space and exports it back
- `Opc.Ua.OpenUsd` — the Part 1 Bindings model
- `Opc.Ua.OpenUsd.Client` — the Part 1 connector

> This package tracks a **draft** specification; the model URI, versions and NodeIds may change.

Namespace URI: `http://opcfoundation.org/UA/OpenUSD/Scene/`
