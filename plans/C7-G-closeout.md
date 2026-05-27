# C7 Phase G close-out — full DI NodeSet2 removal

> Continuation plan. Picks up where commit `cd81c5cad` left off:
> the snapshot wire format already carries InstanceDesign children
> end-to-end, but the consumer-side validator integration of those
> children was rolled back due to an NRE in
> `GetNodeStateClassName` during downstream emission.

## The original plan still makes sense — with three additions

The original
`plans/C7-cross-assembly-model-snapshot.md` Phase F/G described:

> "Each snapshot entry is materialised as a TypeDesign subclass and
> registered in the validator's node table by SymbolicId. Downstream
> BaseType / TypeDefinition lookups then resolve through the same
> m_nodes dictionary the XML-loading path populates."

The **type-level** materialisation already works. The override
discovery (`SetOverriddenNodes`) walks `type.BaseTypeNode →
type.Children.Items` — so populating Children on snapshot types is
the right approach.

**Three additions** to the original plan, based on review feedback:

1. **`OutputKind` gating** — `ModelDependencyAttribute` and
   `ModelSnapshotAttribute` should only be emitted when building a
   **library** (i.e. `Compilation.Options.OutputKind ==
   DynamicallyLinkedLibrary` or `NetModule`). Console
   applications (`OutputKind.ConsoleApplication`) and Windows
   applications never get referenced as libraries, so the bloat is
   pointless there. Add an MSBuild property
   `ModelSourceGeneratorEmitDependencyMetadata` (default
   `auto`) that lets consumers override the gate when needed
   (e.g. unit-test apps that simulate library behaviour).

2. **TypeDefinition + DataType + ValueRank on snapshot children** —
   so `GetNodeStateClassName` works during consumer's
   `BuildInstanceHierarchy` pass.

3. **Method arguments** — include input/output parameter lists for
   `MethodDesign` snapshot children so the consumer's call-site
   generation can reproduce the upstream method signature for
   inherited methods. Necessary because companion specs other than
   DI (e.g. Onboarding) DO override inherited methods.

## Root cause of the previous NRE

The previous Phase G attempt added snapshot children to
`design.Children = new ListOfChildren { Items = instanceItems }`.
Downstream:

1. `SetOverriddenNodes` walks correctly — `OveriddenNode` is set on
   Machinery's re-declared `Manufacturer`/`SerialNumber` (✅ works)
2. `BuildInstanceHierarchy` recursively descends through
   `type.Children.Items` and calls `CreateMergedInstance` →
   `GetNodeStateClassName(instance, ...)` for each child
3. `GetNodeStateClassName` for a `PropertyDesign` reads
   `instance.TypeDefinitionNode` which is `null` for snapshot-
   materialised children → **NRE**

## Fix design

### 1. OutputKind gating

**Where**: `ModelDependencyGenerator.Emit()` and
`ModelSnapshotGenerator.Emit()`.

The generator chain receives a `CompilationOptions` snapshot (see
`Tools/Opc.Ua.SourceGeneration/CompilationOptions.cs`) — extend it
with an `OutputKind` field captured from
`Compilation.Options.OutputKind`. Both metadata emitters skip when
the consuming compilation is an application **and** the override
is not set.

**MSBuild property**:

```
<ModelSourceGeneratorEmitDependencyMetadata>auto</ModelSourceGeneratorEmitDependencyMetadata>
```

Values:
- `auto` (default): library → emit; application → skip.
- `true`: always emit (applications can override if they need to be
  reused).
- `false`: never emit (libraries can override if they want minimal
  output).

**Consequence**: `MinimalPumpServer` (an `OutputType=Exe` app) no
longer carries either `ModelDependencyAttribute` or
`ModelSnapshotAttribute` entries by default. Pump server users
re-deploy the EXE without 700 KB of attribute payload that nobody
references downstream.

### 2. Wire format additions — children carry full resolution data

Extend `SnapshotChild` from the current 4-field shape
`(BrowseName, SymbolicName, ModellingRule, InstanceKind)` to:

```
public readonly struct SnapshotChild
{
    public string BrowseName;
    public string SymbolicName;
    public string? TypeDefinitionName;       // for ALL kinds
    public string? TypeDefinitionNamespace;
    public string? DataTypeName;             // variables only
    public string? DataTypeNamespace;
    public int ValueRank;                    // variables only
    public byte ModellingRule;
    public byte InstanceKind;
    public IReadOnlyList<SnapshotMethodArg> InputArgs;   // methods only
    public IReadOnlyList<SnapshotMethodArg> OutputArgs;  // methods only
}

public readonly struct SnapshotMethodArg
{
    public string Name;
    public string DataTypeName;
    public string DataTypeNamespace;
    public int ValueRank;
}
```

Wire format: per-child writes the existing fields, then 4
nullable-strings + 1 int + arg lists (length-prefixed). Strict
bounds-check on arg counts (max 100 per direction).

### 3. Producer-side fills

`ModelSnapshotGenerator`:
- For Object/Variable/Property children, set `TypeDefinitionName`
  + `TypeDefinitionNamespace` from `instance.TypeDefinition`
  (already resolved by validator at emit time).
- For Variable/Property: also set `DataTypeName` +
  `DataTypeNamespace` + `ValueRank` from
  `instance.DataType` / `(int)instance.ValueRank`.
- For Methods: walk
  `((MethodDesign)instance).InputArguments` and `OutputArguments`
  (both `Parameter[]`). Each `Parameter` provides `Name`,
  `DataType`, `ValueRank`. Emit `SnapshotMethodArg` per arg.

### 4. Consumer-side materialisation + linkage

`MaterialiseSnapshotNode`:
- For each child (re-enable the materialisation that was rolled
  back in `cd81c5cad`):
  - Set `instance.TypeDefinition` (XmlQualifiedName).
  - For variables: `instance.DataType` + `instance.ValueRank`.
  - For methods: `((MethodDesign)instance).InputArguments` and
    `.OutputArguments` filled with `Parameter[]` arrays
    reconstructed from `SnapshotMethodArg`.

New private method `LinkSnapshotChildren()` (in
`ModelDesignValidator.SnapshotImport.cs`), called from
`ApplyPendingSnapshots()` AFTER all snapshots have been
materialised but BEFORE the downstream model loads:

```csharp
private void LinkSnapshotChildren()
{
    foreach (NodeDesign node in m_nodes.Values)
    {
        if (node.IsDeclaration != true) { continue; }     // snapshot type
        if (node is not TypeDesign type || !type.HasChildren) { continue; }
        foreach (InstanceDesign instance in type.Children.Items)
        {
            if (instance.TypeDefinition != null &&
                m_nodes.TryGetValue(instance.TypeDefinition, out NodeDesign td))
            {
                instance.TypeDefinitionNode = td as TypeDesign;
            }
            if (instance is VariableDesign variable &&
                variable.DataType != null &&
                m_nodes.TryGetValue(variable.DataType, out NodeDesign dt))
            {
                variable.DataTypeNode = dt as DataTypeDesign;
            }
        }
    }
}
```

Built-in OpcUa types are loaded by `LoadBuiltInModel()` BEFORE
`ApplyPendingSnapshots`, so most references resolve. Cross-snapshot
namespace references (e.g. Machinery's child referencing DI's
`IVendorNameplateType`) resolve too because the DI snapshot is
applied before Machinery's snapshot is loaded.

Missing references emit `MODELGEN016` info-level diagnostic.
Treat as opaque — the consumer's hierarchy walk will still set
`OveriddenNode` (it doesn't follow `TypeDefinitionNode` itself)
but downstream `GetNodeStateClassName` calls may still fail. The
diagnostic lets users discover the gap.

### 5. Mark snapshot types `IsDeclaration=true`

Belt-and-braces: prevents any code path from accidentally trying
to emit the snapshot type locally. Required by step 4's
`LinkSnapshotChildren` to find the snapshot-imported types.

## Files to change

### Wire format
- `Tools/Opc.Ua.SourceGeneration.Core/Snapshot/ModelSnapshotV1.cs`
  - Extend `SnapshotChild` (5 new fields + method arg lists).
  - New `SnapshotMethodArg` struct.
  - Writer/reader updates with strict bounds on arg counts.

### Producer
- `Tools/Opc.Ua.SourceGeneration.Core/Generators/ModelSnapshotGenerator.cs`
  - Populate the new SnapshotChild fields.
  - For methods, walk `InputArguments` + `OutputArguments`.
- `Tools/Opc.Ua.SourceGeneration.Core/Generators/ModelDependencyGenerator.cs`
  - Guard with OutputKind check.

### Compilation options
- `Tools/Opc.Ua.SourceGeneration/CompilationOptions.cs`
  - Capture `OutputKind`.
- `Tools/Opc.Ua.SourceGeneration/ModelCompilationOptions.cs`
  - Add `EmitDependencyMetadata` enum
    (`Auto`/`Always`/`Never`).
- `Tools/Opc.Ua.SourceGeneration/OPCFoundation.Opc.Ua.SourceGeneration.props`
  - Register
    `ModelSourceGeneratorEmitDependencyMetadata` as a
    `CompilerVisibleProperty`.

### Consumer / validator
- `Tools/Opc.Ua.SourceGeneration.Core/Schema/ModelDesignValidator.SnapshotImport.cs`
  - Re-enable child materialisation in `MaterialiseSnapshotNode`.
  - For variables: set `DataType` / `ValueRank`.
  - For methods: reconstruct `InputArguments` / `OutputArguments`.
  - Set `IsDeclaration=true` on snapshot types.
  - Add `LinkSnapshotChildren()` post-import pass.

### Generator
- `Tools/Opc.Ua.SourceGeneration.Core/Generators.cs`
  - Pass the `EmitDependencyMetadata` setting into the
    `ModelDependencyGenerator` + `ModelSnapshotGenerator` chain.

### Tests
- `Tests/Opc.Ua.SourceGeneration.Core.Tests/Snapshot/ModelSnapshotV1Tests.cs`
  - Extend `WriteThenRead_RoundTripsChildren` to verify
    `TypeDefinitionName`/`DataTypeName`/`ValueRank`.
  - Add new `WriteThenRead_RoundTripsMethodArgs` test.
- `Tests/Opc.Ua.SourceGeneration.Tests/ModelDependencyScannerTests.cs`
  - New test: applications (OutputKind.ConsoleApplication) by
    default emit no dependency/snapshot attributes.
  - New test: applications WITH
    `ModelSourceGeneratorEmitDependencyMetadata=true` DO emit
    them.

### Apps
- `Applications/MinimalPumpServer/MinimalPumpServer.csproj`
  - Delete the `<AdditionalFiles>` entry for
    `Model/Opc.Ua.Di.NodeSet2.xml`.
- `Applications/MinimalPumpServer/Model/Opc.Ua.Di.NodeSet2.xml`
  - **Delete** (−285 KB).
- `Applications/MinimalSoftwareUpdateServer/` — verify it still
  builds (uses DI library; should be unaffected).

## Verification plan

After the changes:

1. **Wire-format tests** must still pass (`ModelSnapshotV1Tests`).
2. **Producer + scanner tests** must still pass
   (`ModelSnapshotScannerTests`).
3. **Library output**: DI library rebuilds; `*.ModelDependencies.g.cs`
   and `*.ModelSnapshot.g.cs` still emitted (because DI is a
   library).
4. **Application output**: MPS rebuild produces no
   `*.ModelDependencies.g.cs` and no `*.ModelSnapshot.g.cs` files —
   verify by listing generated files.
5. **Critical**: remove
   `Applications/MinimalPumpServer/Model/Opc.Ua.Di.NodeSet2.xml`
   and the corresponding `<AdditionalFiles>` entry. Rebuild
   `MinimalPumpServer`. Expectation:
   - 0 errors (no `MODELGEN003` "Could not find supertype").
   - 0 `CS0108` (hides-inherited) errors.
   - Generated Machinery + Pumps code references
     `global::Opc.Ua.Di.IVendorNameplateState` (etc.) — confirmed
     by grepping the generated `.g.cs` for `Opc.Ua.Di.`.
6. `MinimalPumpServer` test suite (10 tests) must still pass.
7. Full baseline (`Opc.Ua.Server.Tests` Fluent +
   `Opc.Ua.Di.Tests` + `Opc.Ua.SourceGeneration.*`) remains green.

## Risks / fallbacks

| Risk | Mitigation |
|---|---|
| Snapshot-resolved BaseType chain doesn't bottom out at OpcUa root types. | Built-in types are loaded BEFORE `ApplyPendingSnapshots`. Add a verification test that walks a snapshot type's BaseType chain end-to-end. |
| `IsOverriddenWithSameClass` returns wrong answer for snapshot children. | Snapshot children's `OveriddenNode` is `null` and they're not iterated by the generator's emission path. |
| `MergeTypeHierarchy` calls `Copy()` on `TypeDesign`s including snapshot ones. | `IsDeclaration=true` short-circuits emission paths that copy children. Verify by running the full sourcegen test suite. |
| OutputKind detection: net8/net10 application vs library compilation contexts may differ slightly. | `Compilation.Options.OutputKind` is the canonical Roslyn API — same value across all .NET versions. |
| Existing libraries emit larger snapshots after extension. | Estimated +~50% on DI snapshot size (still ~4 KB base64). Well below the 50 KB budget. Method arg carriage adds ~30 bytes per method × ~40 methods = ~1.2 KB raw → ~300 B compressed. |

## Out of scope for this close-out

- Wire-format V2 / version negotiation.
- Schema/BinarySchema regeneration from snapshot data.

## Sequence

1. **Wire format extension** (+5 fields on SnapshotChild +
   SnapshotMethodArg struct + writer/reader updates).
2. **Producer fill-in** (~30 LoC in `ModelSnapshotGenerator`).
3. **OutputKind gating** (CompilationOptions field + emitter
   guard + MSBuild prop registration).
4. **Wire-format test extension** (+TypeDef/DataType/Method args
   round-trip).
5. **Build DI** → verify snapshot now includes all fields (assert
   via decoded payload reflection).
6. **Consumer fill-in** (re-enable child materialisation + add
   `LinkSnapshotChildren` + set `IsDeclaration=true`).
7. **Remove DI NodeSet2 from MPS** (`csproj` + delete file).
8. **Verify** end-to-end: MPS builds; baseline tests pass.
9. **Commit** as a single "C7 Phase G close-out" commit. Net delta:
   −285 KB of XML + ~250 LoC of source-gen plumbing + ~30 LoC of
   tests.

## Open questions resolved

1. **Methods' args carriage?** ✅ Yes (per user feedback).
2. **`IsDeclaration=true` on snapshot types?** ✅ Yes
   (belt-and-braces; required by `LinkSnapshotChildren`).
3. **Library-only attribute emission?** ✅ Yes
   (`Auto`/`Always`/`Never` enum via MSBuild prop).
4. **Transitive snapshot dependencies?** ✅ Handled by scanner
   reading all referenced assemblies.

## Action items

If approved:
1. Implement Sequence steps 1–9 above (single commit).
2. Update `plans/C7-cross-assembly-model-snapshot.md` to mark Phase
   G COMPLETE.
3. Update the session-state plan.md to reflect the full
   close-out.
