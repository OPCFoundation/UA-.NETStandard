# OPC UA — OpenUSD scene materialization

Where the [OpenUSD binding](OpenUsd.md) (Part 1) keeps the USD scene **outside** OPC UA and binds
process values to it, scene materialization (Part 2) puts the scene **inside** the address space.
The composed prim tree becomes the OPC UA node hierarchy, so browsing the server is browsing the
scene, and an ordinary Subscription on an attribute Variable is a live feed of that part of the
scene.

The two models interoperate but neither requires the other.

|  | Part 1 — bindings | Part 2 — scene materialization |
|---|---|---|
| USD scene lives | outside OPC UA | inside OPC UA |
| OPC UA carries | *which* prim/attribute a value maps to | the prims and attributes themselves |
| Consumer | a connector that writes an external stage | a client that browses/subscribes, or exports `.usda` |

> Both companion models track **draft** specifications; model URIs, versions and NodeIds may change.

## Libraries

| Package | Contents |
|---|---|
| `Opc.Ua.OpenUsdScene` | the source-generated companion model, the scene document model, the `.usda` reader/writer, and the value-type map |
| `Opc.Ua.OpenUsdScene.Server` | materializer, exporter, discovery, and Part 1 interop |

## Materializing a stage

```csharp
UsdStage stage = UsdaReader.ReadFile("Plant.usda");

FolderState stages = context.EnsureStagesFolder(serverObject, sceneNamespaceIndex);
UsdMaterializationResult result = context.MaterializeUsdStage(stages, stage, sceneNamespaceIndex);
```

`MaterializeUsdStage` walks the composed prim tree depth first and creates, for each prim, a
`HasComponent` child typed by its USD `typeName`; for each attribute a `UsdAttributeType`
Variable; and for each relationship a `UsdRelationshipType` Object with ordered `Targets` and
`TargetPaths`.

The result carries two indexes — `PrimsByPath` and `AttributesByPath` — so a caller can bind live
data to a materialized attribute without re-browsing.

## Nothing is ever dropped

The specification is emphatic that an importer must not discard what it does not understand
(§8.4), because an exporter has to be able to reproduce it. So:

- an **unknown typed schema** stays a `UsdPrimType` carrying its `TypeName` token;
- an **unknown API schema** degrades to a generic `UsdApiSchemaType` AddIn carrying its
  `SchemaName`;
- an **unknown value type** is carried opaquely as `BaseDataType`, with the exact
  `SdfValueTypeName` preserved in `UsdTypeName`.

## Types carry USD roles

USD's `color3f`, `normal3f`, `point3f` and `vector3f` all decompose to three floats and differ
only by *role*. Rather than flatten that away, the model gives each its own DataType that
**subtypes the built-in** — the same idiom the OPC UA standard uses for `Duration : Double` or
`UtcTime : DateTime`.

Because the role type subtypes a built-in, the value bytes are unchanged: a generic client reads
a `Float[3]` exactly as before, while a renderer or material editor can tell a colour from a point
straight from the type system instead of parsing a string. `UsdValueTypeMap` implements the
mapping, and the attribute's `UsdTypeName` keeps it reversible even where several USD types share
one OPC UA DataType.

## Live attributes

A materialized attribute is an ordinary Variable, so USD's static/time-sampled duality maps onto
the OPC UA value surface in two modes a Server may mix per attribute:

- **Mode A — live.** The Value is server-maintained and time-varying; a Subscription delivers
  changes and, where retained, `HistoryRead` exposes the timeline — the OPC UA counterpart of USD
  time samples. Rotating `xformOp`s and process-driven attributes use this mode.
- **Mode B — static.** The Value is the authored default.

Driving Mode A is a **Server-side** responsibility. An external Part 1 connector authors into a
USD sink and cannot write an in-server Variable; a Part 1 binding *declares* the mapping, and the
Server (or a server-hosted connector) applies it.

## Georeferencing

Core OpenUSD has no geodetic schema, so a georeferenced stage is expressed today through vendor
extension schemas (Cesium for Omniverse, NVIDIA's geospatial schema). Those materialize through
the ordinary vendor-extension mechanism — a georeference prim type as an ObjectType subtype, an
anchor API schema as an AddIn.

To give a client something portable, the model also defines `UsdGeoreferenceApiType` (a
stage-level origin: latitude, longitude, height, EPSG code, tangent plane) and
`UsdGlobeAnchorApiType` (a per-prim geodetic position). When the materializer recognises a Cesium
schema it **dual-authors** the portable one with the same values, so a generic client reads the
anchor from one well-known type while a vendor-aware client still reads the native schema.

A partial anchor is never published: a wrong position is worse than no position.

## Conversion and the round-trip contract

`UsdaReader` and `UsdaWriter` convert between `.usda` and the scene document model, and
`UsdSceneExporter` reads a materialized address space back out, so the full round trip is

```text
.usda → UsdStage → address space → UsdStage → .usda
```

The contract (§7.4) is **composed-scene lossless**: the exported stage is prim-for-prim,
attribute-for-attribute (name, `SdfValueTypeName`, resolved value and array shape),
relationship-for-relationship (ordered targets), metadata-, variant-selection-, kind- and
specifier-equivalent to the input's *composed* result, and the recorded composition arc list is
preserved.

It is deliberately **not** authoring-layer lossless: the input's per-layer opinion stack, sublayer
structure and value clips are summarised as provenance under `Composition/` and
`RootLayerIdentifier` rather than reproduced layer by layer. `UsdSceneSignature` computes the
equivalence used to check the contract.

## Discovery and Part 1 interop

`EnsureStagesFolder` returns the folder stages are organized under: Part 1's
`Server/OpenUSD/Stages` when the bindings model is also implemented — so one connector discovers
both external-stage bindings and in-server materialized stages — or a standalone
`Server/OpenUSDScene/Stages` otherwise.

A Part 1 live binding may target a Part 2 attribute Variable. Part 1 ≥ 0.3.0 carries the target
two ways, and a Server should author both so that path-resolving and NodeId-resolving connectors
agree:

```csharp
NodeId target = result.ResolveBindingTargetNodeId(
    "/Plant/Pumps/P101/Pump/Impeller", "xformOp:rotateZ");
```

## Conformance units

`UsdMaterializationOptions` maps one-to-one onto the specification's conformance units, each
independent and additive. **Scene Structure** is the baseline; the rest are opt-in:
composition provenance, typed schemas, applied schemas, georeferencing, live attributes,
conversion, and Part 1 interop.
