# Opc.Ua.OpenUsd

Draft **OPC UA - OpenUSD Bindings** and **OpenUSD Scene Materialization** companion
information models for the OPC UA .NET Standard stack.

## Bindings model

The source-generated `Opc.Ua.OpenUsd` namespace describes an external OpenUSD stage and
binds OPC UA values to it:

- `OpenUsdRootType`, `OpenUsdStageType`, and `OpenUsdRepresentationType`;
- live, alarm, history, and command binding types;
- component composition and aggregation;
- content integrity and Part 5 asset delivery metadata.

The generic, dependency-injectable connector lives in **Opc.Ua.OpenUsd.Client**.

## Scene materialization model

The source-generated `Opc.Ua.OpenUsd.Scene` namespace describes a USD stage materialized
inside an OPC UA address space:

- stage and prim types, including UsdGeom and UsdShade typed prims;
- attributes, relationships, composition arcs, variant sets, and applied API schemas;
- georeferencing types;
- USD value-role DataTypes and reference types.

The same namespace contains the scene document/value model (`UsdStage`, `UsdPrim`,
`UsdAttribute`, `UsdValue`, and related types). The
`Opc.Ua.OpenUsd.Scene.Conversion` namespace provides:

- `.usda` reading and writing;
- value coercion and OPC UA DataType mapping;
- deterministic scene signatures.

`UsdValue` implements `INullable`; use `UsdValue.Null` and its `TryGet*` accessors rather
than nullable wrappers or boxed values.

Server-side authoring, asset delivery, materialization, export, discovery, and Part 1
interop live in **Opc.Ua.OpenUsd.Server**.

Both companion models are drafts published for review. Their model shapes and NodeIds may
change until the specifications are ratified.

Scene namespace URI: `http://opcfoundation.org/UA/OpenUSD/Scene/`
