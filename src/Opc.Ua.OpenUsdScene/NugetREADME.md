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

## The scene value model

An authored USD value is carried by `UsdValue`, a readonly struct that scopes a value to the shapes
a `.usda` document can express. A `Variant` cannot stand in for it: the USD value model is recursive
and ragged, with tuples (`float3`), arrays, *arrays of tuples* (`color3f[]`), matrices authored as a
tuple of row tuples, and asset paths and prim path references that must round-trip as their own
syntax.

`UsdValue` implements `INullable`, so an attribute with no authored value is `UsdValue.Null` — never
`UsdValue?`. Values are read through `TryGet*` accessors; there is no boxing accessor:

```csharp
UsdAttribute radius = prim.Attributes["radius"];

if (radius.Value.TryGetDouble(out double r))
{
    // a double3 or color3f arrives as a tuple instead
}

if (radius.Value.TryGetTuple(out ArrayOf<UsdValue> components))
{
    foreach (UsdValue component in components.Span)
    {
        component.TryGetNumber(out double v);
    }
}
```

Construction mirrors the authored syntax — `UsdValue.From(1.5)`, `UsdValue.FromToken("vertex")`,
`UsdValue.FromAssetPath("./tool.usda")`, `UsdValue.FromTuple(...)`, `UsdValue.FromArray(...)`. The
attribute's `TypeName` stays authoritative for how a value is rendered back out, so the kind adds
type safety without changing the emitted `.usda`.

The same type carries `UsdAttribute.TimeSamples` and `UsdPrim.Metadata`, and a nested metadata
dictionary is a `UsdValue` of kind `Dictionary`.

An integral value that does not fit a signed 64 bit integer — a `uint64` above `long.MaxValue` — has
no integral kind to carry it, so it arrives as a `Token` holding its exact decimal digits rather than
being wrapped into a negative `Integer`; the coercion layer reads that form back into a `uint64`.

## Related packages

- `Opc.Ua.OpenUsdScene.Server` — materializes a scene into a server address space and exports it back
- `Opc.Ua.OpenUsd` — the Part 1 Bindings model
- `Opc.Ua.OpenUsd.Client` — the Part 1 connector

> This package tracks a **draft** specification; the model URI, versions and NodeIds may change.

Namespace URI: `http://opcfoundation.org/UA/OpenUSD/Scene/`
