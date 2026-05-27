# C7 — Cross-assembly model snapshot

> **Status:** proposed plan (`[[PLAN]]` mode). Repurposes the
> `c7-modeldep-templating` slot to address the underlying need:
> let a referenced assembly fully expose its compiled-in model to
> the source generator so downstream consumers no longer need to
> re-add the upstream NodeSet2 (or ModelDesign) XML to their own
> `<AdditionalFiles>`.
>
> The original `c7-modeldep-templating` plan (template-refactor of
> the dependency-attribute emitter) is folded into the work in
> §"Phase A — templating prerequisites" so the two related plans
> ship together.

## Problem statement

The MinimalPumpServer host today carries a copy of
`Opc.Ua.Di.NodeSet2.xml` (285 KB) **only** so the source generator
can resolve Machinery + Pumps types that inherit from DI:

- `MachineryComponentType` ← `DI.ComponentType`
- `PumpType` ← `MachineryComponentType` ← (transitively) DI
- Inherited child placeholders / browse-names / NodeIds

The `Opc.Ua.Di` library already contains all of this information
(generated `*.NodeStates.g.cs` + `*.Identifiers.g.cs` etc.), and
the new `[ModelDependencyAttribute(modelUri, prefix, version,
publicationDate, name)]` (added in P1) records the namespace
metadata — but the **node table** itself is not exposed to
downstream generators in any form. So the generator running on the
Pumps consumer cannot resolve `BaseType="DI:ComponentType"`
without an XML file to parse.

The goal: ship the necessary node-table data **inside** the
`Opc.Ua.Di` assembly in a form the source generator can consume
both from `PortableExecutableReference` (NuGet) and
`CompilationReference` (project-to-project), and have the
generator integrate it into the validator's `m_nodes` table
without re-parsing the original NodeSet2/ModelDesign XML.

## What data does the generator need?

The validator builds `Dictionary<XmlQualifiedName, NodeDesign> m_nodes`
during loading. Downstream models look up:

1. **Type hierarchy** — every `TypeDesign` referenced as `BaseType`
   by a downstream type. Need: `SymbolicId`, `ClassName`,
   `BaseType` (for transitive walk), `IsAbstract`, `NumericId`,
   `Children` (for placeholder/mandatory resolution inherited by
   subtypes).
2. **InstanceDesign children** of dependency types — so optional
   / mandatory children are inherited correctly.
3. **DataTypeDesign** definitions — fields, enum values,
   `BasicDataType`, `IsEnumeration` for any structure/enum
   referenced from a downstream `Parameter.DataType` or
   `Variable.DataType`.
4. **MethodDesign signatures** — input/output arguments of methods
   inherited by downstream subtypes.
5. **NodeId mapping** — `SymbolicId` ↔ `NodeId(NumericId, StringId,
   GuidId)` per namespace index. Already partially exposed via the
   generated `Identifiers` / `BrowseNames` static classes, but not
   in a form the generator can read at compile-time.
6. **Namespace metadata** — Prefix, Name, version, publication
   date. **Already exposed** by `[ModelDependencyAttribute]`.

A downstream generator does **not** need:
- Internal XmlSchema / BinarySchema strings (they're regenerated
  from the type definitions).
- Reference resolutions internal to the upstream model
  (`HasComponent` etc. between upstream nodes only).
- Documentation / Localized texts (purely cosmetic).
- Service definitions / OPC binary encodings.

The minimum payload is the upstream model's `m_nodes` table
restricted to **exported** entries (anything with public visibility
in the upstream's emitted C# — i.e. anything not marked
`Purpose=Internal`).

## Options surveyed

| Option | Mechanism | Reference types | Size for DI | Compile-time cost | Verdict |
|--------|-----------|-----------------|-------------|-------------------|---------|
| **A** | NodeSet2 XML as `<EmbeddedResource>` | PE ✅ · Compilation ❌ | ~280 KB raw, ~30-45 KB gzip | Roslyn `IAssemblySymbol` doesn't directly expose manifest resources; need `MetadataReference.GetMetadata()` plumbing | Partial — needs hybrid for project-to-project |
| **B** | Binary snapshot as single `[ModelSnapshotAttribute(base64)]` | PE ✅ · Compilation ✅ | ~25-40 KB compressed | One attribute read per dep; cached per `IAssemblySymbol` | **Recommended** |
| **C** | Per-node attributes (`[ModelTypeAttribute]` × N) | PE ✅ · Compilation ✅ | ~150 KB raw, attribute-table bloat | N attribute reads per dep | Too much metadata bloat |
| **D** | Generated companion C# data class (`OpcUaDiSnapshot.Types`) | PE ✅ · Compilation ✅ | ~60 KB compiled IL | Symbol traversal at compile-time | Workable but unusual |
| **E** | MSBuild `.props` that re-emits AdditionalFiles | PE ⚠ (only via NuGet packaging) · Compilation ✅ | 0 (uses external file) | Standard MSBuild | Doesn't work for non-package PE references |

**Decision: Option B** — single base64-encoded compressed binary
snapshot attribute. It is the only mechanism that works
**uniformly** for both `PortableExecutableReference` (NuGet) and
`CompilationReference` (project-to-project) without auxiliary
MSBuild infrastructure, and it keeps assembly bloat to a single
attribute per dependency.

## Approach — Option B in detail

### Phase A — templating prerequisites (folds the original C7 plan)

Before adding the snapshot machinery, the dependency-attribute
emitter is migrated to the template system as described in the
*original* `c7-modeldep-templating` plan (above this rewrite was
filed). Tokens added:

- `ListOfModelDependencies`, `ModelVersion`, `ModelPublicationDate`
  — already added in P2.
- `ListOfModelSnapshots`, `SnapshotPayload` — new in Phase A.

The existing `ModelDependencyTemplates.cs` gets a second template
(`SnapshotEntry`) for the new attribute.

### Phase B — Snapshot wire format

`Tools/Opc.Ua.SourceGeneration.Core/Snapshot/ModelSnapshotV1.cs`
defines the binary format (a small custom format — not protobuf or
JSON — to keep zero external dependencies and AOT-safety).

```
ModelSnapshotV1 stream layout (little-endian):
   header
     u8     0xAA, 0xC7              // magic
     u8     1                       // version
     u8     1                       // compression: 1=Deflate
   payload (Deflate-compressed below this point)
     i32    typeCount
     for each type:
       string                       // SymbolicId.Name
       string                       // SymbolicId.Namespace
       string                       // ClassName
       u8     kind                  // 1=Object 2=Variable 3=Method 4=ReferenceType 5=DataType 6=ObjectType 7=VariableType 8=View
       string?                      // BaseType.Name (null = none)
       string?                      // BaseType.Namespace
       u32                          // NumericId (0 = none)
       string?                      // StringId
       u8     flags                 // bit0=IsAbstract bit1=HasChildren bit2=IsEnumeration ...
       i32    childCount
       for each child:
         u8   modellingRule          // 0=None 1=Mandatory 2=Optional 3=OptionalPlaceholder 4=MandatoryPlaceholder
         string                      // BrowseName
         string                      // SymbolicId.Name (declared)
         string                      // SymbolicId.Namespace
       (if kind == DataType) {
         u8   basicType              // BasicDataType enum
         i32  fieldCount
         for each field: string name, string typeName, string typeNs, i32 valueRank
       }
       (if kind == Method) {
         i32  inputCount; for each: string name, string dtName, string dtNs, i32 rank
         i32  outputCount; for each: string name, string dtName, string dtNs, i32 rank
       }
```

All strings are length-prefixed UTF-8 (varint length).
**Forward-compat**: readers reject `version != 1` with
`MODELGEN013` warning + skip the snapshot (consumer falls back to
explicit `<AdditionalFiles>` resolution).

A single payload for DI fits in ~30 KB compressed (estimate based
on type/child counts × average symbol length).

### Phase C — Snapshot emitter

`Tools/Opc.Ua.SourceGeneration.Core/Generators/ModelSnapshotGenerator.cs`:

```csharp
internal sealed class ModelSnapshotGenerator
{
    public TextFileResource Emit() {
        // 1. Filter m_nodes to "exported" entries (skip Internal,
        //    skip excluded, skip OpcUa root namespace which is
        //    well-known to every generator).
        // 2. Serialize via ModelSnapshotV1.Writer to MemoryStream.
        // 3. Deflate-compress; base64-encode.
        // 4. Emit  [assembly: global::Opc.Ua.ModelSnapshotAttribute(
        //              "{modelUri}", "{base64Payload}")]
        //    into the same {prefix}.ModelDependencies.g.cs file.
    }
}
```

The emitter runs **once per generated model**, immediately after
the existing `ModelDependencyGenerator`. The payload is folded
into the same `*.ModelDependencies.g.cs` file (no new `.g.cs`).

### Phase D — Attribute definition

`Stack/Opc.Ua.Types/Attributes/ModelSnapshotAttribute.cs`
(new public surface on `Opc.Ua.Types`):

```csharp
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class ModelSnapshotAttribute : Attribute
{
    public ModelSnapshotAttribute(string modelUri, string payload) {
        ModelUri = modelUri;
        Payload = payload;
    }
    public string ModelUri { get; }
    /// <summary>Base64-encoded Deflate-compressed ModelSnapshotV1 payload.</summary>
    public string Payload { get; }
}
```

The attribute is consumer-only metadata — no runtime use. Pairs
1:1 with `ModelDependencyAttribute`: every model the assembly
emits gets BOTH a `ModelDependencyAttribute` (metadata) AND a
`ModelSnapshotAttribute` (full node table).

### Phase E — Reader / decompressor in the generator

`Tools/Opc.Ua.SourceGeneration/ReferencedModelSnapshotScanner.cs`
(sibling to `ReferencedModelDependencyScanner`):

```csharp
internal static class ReferencedModelSnapshotScanner
{
    public const string AttributeMetadataName = "Opc.Ua.ModelSnapshotAttribute";

    public static ImmutableArray<ModelSnapshot> Scan(Compilation compilation) {
        // Same pattern as ReferencedModelDependencyScanner.
        // For each [assembly: ModelSnapshotAttribute(uri, payload)]:
        //   - extract uri + base64 payload from constructor args
        //   - lazily decode (defer until consumer actually asks)
        //   - return ModelSnapshot { Uri, AssemblyName, LazyPayload }
    }
}
```

The `ModelSnapshot` struct holds an `Lazy<ModelSnapshotV1>` so
decompression is deferred to first use — many consumers only need
2-3 of the 10 dependencies their referenced assemblies declare.

### Phase F — Validator integration

`Tools/Opc.Ua.SourceGeneration.Core/Schema/ModelDesignValidator.cs`:

Add a new public method:

```csharp
public void ImportSnapshot(string modelUri, ModelSnapshotV1 snapshot) {
    // 1. Pre-seed m_designFilePaths[modelUri] = string.Empty
    //    (treated as "resolved, no backing file" — mirroring the
    //    existing OpcUa root namespace handling at line 349).
    // 2. Build the synthetic Namespace entry and inject into the
    //    namespace table.
    // 3. For each type in snapshot.Types:
    //      - Construct an ObjectTypeDesign / VariableTypeDesign /
    //        DataTypeDesign with the recorded ClassName, BaseType,
    //        IsAbstract, NumericId.
    //      - Register in m_nodes by SymbolicId.
    //      - Register in m_nodesByNodeId by reconstructed NodeId.
    //      - Add Children placeholders (InstanceDesign skeletons
    //        with just enough metadata for inheritance resolution).
    // 4. Subsequent downstream models that reference these types
    //    by SymbolicId resolve through m_nodes exactly as if the
    //    upstream NodeSet2/ModelDesign had been parsed.
}
```

Invoked from `Generators.GenerateCode` once per referenced model
**before** the validator processes the downstream targets.

### Phase G — `MinimalPumpServer` migration

After Phases A–F land, MPS deletes:

- `Model/Opc.Ua.Di.NodeSet2.xml` (285 KB)
- The `<AdditionalFiles Include="Model/Opc.Ua.Di.NodeSet2.xml">` entry
  with `ModelSourceGeneratorIgnore=true` in `MinimalPumpServer.csproj`

…and keeps only the Machinery + Pumps NodeSet2 inputs (~6.6 MB)
that source-generate locally. Cross-namespace DI references
resolve through the snapshot attached to the `Opc.Ua.Di` project
reference.

The same pattern applies to any future companion-spec consumer
that depends on DI.

## Performance characteristics

| Concern | Approach |
|---|---|
| **Assembly bloat** | One attribute string per emitted model. DI snapshot ≈ 30 KB base64 (≈ 22 KB binary). Pumps would add ~700 KB if it self-exposed, but Pumps is a leaf model — only foundation models (DI, Machinery, IA) need to expose themselves. |
| **Compile-time CPU** | Snapshot is read once per `IAssemblySymbol` per Compilation. Incremental cache key is `assembly.Identity` + payload hash, so unchanged dependencies are skipped on subsequent builds. |
| **Memory** | Lazy decompression — only paid for snapshots the validator actually requests (typically 1-3 of N referenced assemblies). Decompressed payload is held for the duration of the generator run only. |
| **AOT-safety** | Custom binary format avoids reflection-based serializers. `System.IO.Compression.DeflateStream` is AOT-safe. Base64 is `System.Convert` (AOT-safe). |
| **Incremental builds** | Roslyn's `IncrementalValueProvider<ImmutableArray<ModelSnapshot>>` keyed on `compilation.SourceModule.ReferencedAssemblySymbols` already invalidates only when references change. |
| **Cold-start cost** | First decode of DI snapshot estimated at ~5 ms (deflate + binary deserialize for ~500 nodes). |

## Backward compatibility

- Old `[ModelDependencyAttribute]` keeps working — it remains the
  authoritative source of the (uri, prefix, version, date, name)
  tuple. `ModelSnapshotAttribute` is **additive**.
- Existing projects with the upstream NodeSet2 in their
  `<AdditionalFiles>` keep working unchanged: when both the
  attribute snapshot and an AdditionalFile resolve the same URI,
  the AdditionalFile wins (consumer is explicitly saying "use
  this version"). Generator emits `MODELGEN014` info diagnostic
  noting the override.
- Assemblies built before Phase D ship without snapshot — the
  generator silently falls back to the existing
  "missing-AdditionalFile → error" behaviour, unchanged.

## Files to change

### New
- `Stack/Opc.Ua.Types/Attributes/ModelSnapshotAttribute.cs`
- `Tools/Opc.Ua.SourceGeneration.Core/Snapshot/ModelSnapshotV1.cs`
  (reader + writer + struct definitions)
- `Tools/Opc.Ua.SourceGeneration.Core/Snapshot/ModelSnapshotEntry.cs`
  (per-type record struct)
- `Tools/Opc.Ua.SourceGeneration.Core/Generators/ModelSnapshotGenerator.cs`
- `Tools/Opc.Ua.SourceGeneration.Core/Generators/ModelSnapshotTemplates.cs`
- `Tools/Opc.Ua.SourceGeneration/ReferencedModelSnapshotScanner.cs`

### Modified
- `Tools/Opc.Ua.SourceGeneration.Core/Schema/ModelDesignValidator.cs`
  — add `ImportSnapshot` method; pre-seed `m_designFilePaths` and
  `m_nodes` for snapshot entries.
- `Tools/Opc.Ua.SourceGeneration.Core/Schema/NodeSetToModelDesign.cs`
  — recognise snapshot-resolved namespaces during nodeset import.
- `Tools/Opc.Ua.SourceGeneration.Core/Generators.cs`
  — call `ImportSnapshot` for each referenced model before the
  main `OpenModelDesign` validate step in both
  `GenerateCode(DesignFileCollection)` and
  `GenerateCode(NodesetFileCollection)`.
- `Tools/Opc.Ua.SourceGeneration.Core/Generators/IGeneratorContext.cs`
  — add `IReadOnlyDictionary<string, ModelSnapshot> ReferencedSnapshots`.
- `Tools/Opc.Ua.SourceGeneration.Core/Generators/GeneratorContext.cs`
  — same.
- `Tools/Opc.Ua.SourceGeneration\ModelSourceGenerator.cs`
  — wire `ReferencedModelSnapshotScanner.Scan` as an additional
  `IncrementalValueProvider`.
- `Tools/Opc.Ua.SourceGeneration.Core/Templating/Tokens.cs`
  — add `Tokens.SnapshotPayload` + `Tokens.ListOfModelSnapshots`.
- `Tools/Opc.Ua.SourceGeneration.Core/Generators/ModelDependencyTemplates.cs`
  — add `SnapshotEntry` template + extend `File` template with
  the snapshot block.

### Removed (after migration)
- `Applications/MinimalPumpServer/Model/Opc.Ua.Di.NodeSet2.xml`
- The `<AdditionalFiles>` entry with `ModelSourceGeneratorIgnore=true`
  in `MinimalPumpServer.csproj`.

## Tests

### Snapshot wire format
`Tests/Opc.Ua.SourceGeneration.Core.Tests/Snapshot/ModelSnapshotV1Tests.cs`

1. `WriteThenRead_RoundTripsExactly` — round-trip with all kinds.
2. `WriteThenRead_PreservesUnicodeBrowseNames` — Unicode strings.
3. `Read_RejectsWrongMagic` — first byte tampered.
4. `Read_RejectsFutureVersion` — version=2 throws/returns null.
5. `Read_HandlesNullBaseType` — root types without parents.
6. `Write_DeterministicByteForByte` — same input → same bytes
   (for assembly-content reproducibility).
7. `Compression_DeflateRoundTrip` — verifies the gzip-wrapped layer.

### Generator emission
`Tests/Opc.Ua.SourceGeneration.Core.Tests/Generators/ModelSnapshotGeneratorTests.cs`

1. `EmitsSingleAttributePerModel` — one `[ModelSnapshotAttribute]` per emitted model.
2. `PayloadDecodesBackToInputModel` — round-trip via the actual emit + scan path.
3. `OmitsInternalNodes` — `Purpose=Internal` types not in payload.
4. `OmitsOpcUaRootNamespace` — types in `http://opcfoundation.org/UA/` not in payload (well-known to all consumers).
5. `BaseTypeReferencesResolveAcrossNamespaces` — type referencing another model in same payload.

### Validator integration
`Tests/Opc.Ua.SourceGeneration.Core.Tests/Schema/ImportSnapshotTests.cs`

1. `ImportSnapshot_PreSeedsDesignFilePaths`
2. `ImportSnapshot_RegistersNodesInMNodes`
3. `ImportSnapshot_ResolvesNodeIdLookups`
4. `ImportSnapshot_TypeHierarchyResolves` — downstream subtype's `BaseType.Name` walks back to a snapshot type.
5. `ImportSnapshot_DataTypeFieldsResolve`
6. `ImportSnapshot_PlaceholderChildrenInherited`

### End-to-end
`Tests/Opc.Ua.SourceGeneration.Tests/CrossAssemblySnapshotTests.cs`

1. `PumpsCompilesWithoutDiNodeSet2InAdditionalFiles` — synthetic
   2-project compilation: producer with DI snapshot,
   consumer with only Pumps NodeSet2; assert generation succeeds
   and emitted code contains `global::Opc.Ua.Di.ComponentTypeState`
   references.
2. `AdditionalFileOverridesSnapshot` — both available; consumer's
   AdditionalFile wins; `MODELGEN014` info-level diagnostic fires.
3. `MissingSnapshotAndMissingAdditionalFile_StillErrors` —
   neither path provides the dep; `MODELGEN011` warning + skip.

## Diagnostic IDs

| ID | Severity | Fires when | Status |
|---|---|---|---|
| `MODELGEN010` | Info | Override silently skipped (same prefix) | Already wired |
| `MODELGEN011` | Warning | Truly unresolved dependency | Already wired |
| `MODELGEN012` | Info | Multiple assemblies declare same URI | Already wired |
| `MODELGEN013` | Warning | Snapshot version unknown / corrupt | **New** |
| `MODELGEN014` | Info | Snapshot superseded by local `AdditionalFile` | **New** |

## Migration sequence

1. **PR A — Phase A + B + C + D**: emit `[ModelSnapshotAttribute]`
   on every assembly that uses the source generator. No
   consumer-side change. Verify `Opc.Ua.Di.dll` size increase
   ≤ 50 KB; round-trip tests pass. *(Self-contained; safely
   mergeable on its own.)*
2. **PR B — Phase E + F**: consumer-side snapshot reader + validator
   integration. Existing consumers see no functional change
   (snapshot is consulted only when an `AdditionalFile` is missing).
3. **PR C — Phase G**: delete `Opc.Ua.Di.NodeSet2.xml` from MPS;
   verify all 10 Pumps tests still pass.

## Risks

| Risk | Mitigation |
|---|---|
| **Snapshot payload incomplete** — generator can't resolve a downstream node because some property of the upstream node wasn't captured. | Comprehensive round-trip tests that build the downstream model with snapshot-only resolution and compare emitted bytes against the XML-resolved baseline. |
| **Assembly size blow-up** for very large foundation models. | Restrict snapshot to "type-shape" data; exclude any field that is not consulted by downstream resolution. Add hard limit (`MODELGEN015` info if payload > 256 KB) and document. |
| **CompilationReference resource visibility** in IDE scenarios. | Attribute-based payload sidesteps the Roslyn resource-API gap entirely — works uniformly for both reference kinds. |
| **Version compat** when snapshot format evolves. | Embed version byte; readers reject unknown versions cleanly with `MODELGEN013`. Producers can ship v1 and v2 attributes side-by-side during deprecation windows. |
| **Determinism** (assembly-reproducibility). | Sort snapshot entries by `(Namespace, Name)` before serialise; compression at level=Optimal for stable output. |

## Out of scope

- **Schema embedding** (BinarySchema / XmlSchema). Downstream
  generators re-emit these from the type definitions; no need to
  copy bytes.
- **Cross-assembly proxy reuse**. Consumers still emit their own
  ObjectType proxies for their own types; we never reach across
  to instantiate someone else's proxy.
- **Backward compat with older NodeSet2 versions** in snapshot
  format. The snapshot is generated from the producer's
  already-validated `m_nodes`; whatever version semantics the
  producer settled on apply.
- **Different-prefix override** (covered by C6
  `design-deps-phase2.md`). Snapshot integration assumes
  same-prefix; different-prefix consumers continue to use
  `<AdditionalFiles>`.

## Open questions

1. **Should the snapshot also carry NodeId numeric assignments**,
   or only `SymbolicId`s? Numeric ids let downstream emit
   constant references like `global::Opc.Ua.Di.ObjectTypes.ComponentType`
   directly. **Recommendation: yes, include NumericId** — adds
   ~4 bytes per node (negligible).
2. **Where to ship the snapshot — same `*.ModelDependencies.g.cs` file
   or a separate `*.ModelSnapshot.g.cs`?** Plan above bundles them;
   separating would let consumers strip the snapshot via
   `<RemoveCustomAttribute>` if size matters. Defer to a later PR.
3. **Localized text policy.** Skip Description / DisplayName from
   snapshot (cosmetic). Confirm no downstream generator consults
   them during validation. (Spot-check: validator does not; OK.)
4. **Should snapshot replace `[ModelDependencyAttribute]` entirely** since
   the metadata is derivable from the snapshot? Keep both: the
   attribute is the fast-path metadata; the snapshot is opt-in
   bulk data. Allowing consumers to inspect just the attributes
   without parsing snapshots keeps incremental work fast.
