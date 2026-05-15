# Phase 2 plan: `design-deps` — cross-assembly type emission via referenced prefixes

## Goal

When a referenced assembly already contains generated types for an upstream model, the consumer's generator should:

1. **Resolve missing-namespace dependencies** in `AdditionalFiles` from the referenced-assembly closure (today `CollectDependencies` already does this for nodesets — extend the same to the design path).
2. **Emit cross-namespace type references** that point at the *referenced assembly's* C# namespace (`referenced.Prefix`) instead of either erroring or duplicating the type tree under the local prefix.
3. **Optionally support a different-prefix override**: same model URI, different consumer prefix → generate locally (current behaviour) but still record the referenced prefix as a sibling type-source so partial reuse is possible.

## Problem snapshot (current state)

- `NodesetFile.CollectDependencies` already consults `referencedModels` and skips missing nodesets ✅ — but `ImportDesignFiles`/`GetNamespaceList` for design files (`*.xml`) does **not**: a referenced-only namespace will produce a missing-design-file error.
- `m_designFilePaths[Ua.Types.Namespaces.OpcUa] = string.Empty` is the only "external" namespace pre-seeded today (`ModelDesignValidator.cs:349`, `:520`). There is no equivalent for arbitrary `(uri → prefix)` pairs from `referencedModels`.
- `Namespace[] Namespaces` on `IModelDesign` is what generators use to look up `Prefix` (e.g. `DataTypeGenerator.cs:462`, `NodeStateGenerator.cs:267`, `BinarySchemaGenerator.cs:138`). When a namespace exists in `NodeSet.NamespaceUris` but no design file backs it, it never makes it into `m_dictionary.Namespaces` — `GetNamespacePrefix` returns `null` and the template token renders empty.
- `MODELGEN011` (unresolved-dependency warning) descriptor exists in `SourceGenerator.cs` but is **never fired**.

## Approach

Introduce a thin `RegisterExternalNamespace(uri, prefix, version, publicationDate)` mechanism that:

- Pre-seeds `m_designFilePaths[uri] = string.Empty` (treats it as resolved without a backing file, mirroring the OpcUa root case).
- Pre-seeds a synthetic `Namespace { Value = uri, Prefix = referenced.Prefix, Name = NameFromUri(uri), Version = ..., PublicationDate = ... }` into the in-memory namespace table so `GetNamespacePrefix(uri)` returns the **referenced** prefix.
- Records the `(uri → ModelDependencyReference)` map on `IGeneratorContext.ReferencedModels` so individual generators (DataType, NodeState, BinarySchema, XmlSchema, etc.) can detect "external — emitted by a referenced assembly" and switch reference-formatting accordingly.

Cross-namespace references then naturally render as `global::{referenced.Prefix}.DataTypes.Foo` because the prefix lookup goes through `Namespaces.GetNamespacePrefix(uri)` — no per-generator changes are needed for the **same-prefix** path. The **different-prefix** path is opt-in via a new option (`AllowSiblingTypeSource`, default off in v1) and out of scope for this phase if it complicates schema generation.

## Files to change

### Modified
- `Tools/Opc.Ua.SourceGeneration.Core/Schema/ModelDesignValidator.cs`
  - Add `public void RegisterExternalNamespaces(IReadOnlyDictionary<string, ModelDependencyReference> external)` invoked from `ValidateModel`/`ValidateNodeSet` *before* `GetNamespaceList(designFilePaths)`.
  - For each external entry whose URI is in `NodeSet.NamespaceUris` but not yet in `m_designFilePaths`: seed `m_designFilePaths[uri] = string.Empty` and append a synthetic `Namespace` to the resulting `namespaces` list (or post-process after `GetNamespaceList`).
  - Synthesize `Name` via existing `NodesetFile.GetNameFromUri` helper (move it to a shared `NamespaceNaming` static if needed).
- `Tools/Opc.Ua.SourceGeneration.Core/Schema/NodeSetToModelDesign.cs`
  - Same in `ImportNodeSet` path: when a transitive `NamespaceUri` resolves to an external entry, register the synthetic Namespace with the referenced prefix so node imports reference the external types correctly.
- `Tools/Opc.Ua.SourceGeneration.Core/Generators.cs`
  - In both `GenerateCode(this DesignFileCollection ...)` and `GenerateCode(this NodesetFileCollection ...)`: pass `referencedModels` into the validator's `RegisterExternalNamespaces` call (currently only the dictionary is propagated to `IGeneratorContext.ReferencedModels` but the validator never sees it).
- `Tools/Opc.Ua.SourceGeneration.Core/NodesetFile.cs`
  - When `CollectDependencies` skips a missing nodeset because `referencedModels.ContainsKey(ns)`, also record the URI on a `Resolved` set so the caller can emit `MODELGEN011` info-level "satisfied by reference" diagnostic instead of staying silent (helps debugging).
- `Tools/Opc.Ua.SourceGeneration/SourceGenerator.cs`
  - Wire `MODELGEN011` (warning) for the truly-unresolved case (URI in NamespaceUris, not in AdditionalFiles, not in `referencedModels`) — currently this still falls through to a hard error. Demote to warning + return null so partial generation can proceed where possible.
- `Tools/Opc.Ua.SourceGeneration.Core/Schema/ModelDesignExtensions.cs`
  - `GetNamespacePrefix` already does the right thing once the synthetic Namespace is in the table — no change.
  - Consider exposing a helper `IsExternalNamespace(uri)` (lookup in `IGeneratorContext.ReferencedModels`) for generators that need to skip *type definition* emission for external namespaces (vs reference emission).

### New tests
- `Tests/Opc.Ua.SourceGeneration.Tests/ModelDependencyResolutionTests.cs`
  - **`MissingDependencySatisfiedByReferencedAssembly`** — design file declares a `Namespaces` entry for a non-OpcUa URI, no AdditionalFile for that URI, but a producer compilation declares it via `[assembly: ModelDependencyAttribute]`. Assert: no error, generated `*.cs` references resolve to `global::ReferencedPrefix.DataTypes.X`.
  - **`UnresolvedDependencyEmitsMODELGEN011`** — same setup, *no* producer compilation. Assert: `runResult.Diagnostics` contains `MODELGEN011` warning, generation does not throw.
  - **`TransitiveNodeSetSatisfiedByReference`** — consumer adds a NodeSet whose `NamespaceUris` includes an upstream-only URI; producer compilation declares it. Assert: generation succeeds, references render with referenced prefix, and the `MODELGEN011` info-level "satisfied" diagnostic fires (or no diagnostic if we keep the path silent — pick one and document).
  - **`DifferentPrefixOverrideStillGeneratesLocally`** — consumer's design file uses a different prefix than the producer's same-URI declaration. Assert: local generation proceeds (no silent skip), output prefix matches the consumer's, and both prefixes appear in the consumer's emitted `*.ModelDependencies.g.cs` closure.
- `Tests/Opc.Ua.SourceGeneration.Core.Tests/Schema/ModelDesignValidatorTests.cs` (extend existing)
  - **`RegisterExternalNamespacesSeedsPrefixTable`** — direct unit test on `ModelDesignValidator.RegisterExternalNamespaces` ensuring `Namespaces.GetNamespacePrefix(uri)` returns the referenced prefix.

### Documentation
- Update `Docs/ModelDependencies.md` with:
  - The "missing namespace satisfied by reference" rule.
  - How `MODELGEN011` is now wired (warning, not error).
  - Same-prefix vs different-prefix override semantics, with a worked example showing the emitted `[assembly: ...]` closure.

## Diagnostic IDs (resolved)

| ID | Severity | Fires when | Status after Phase 2 |
| --- | --- | --- | --- |
| `MODELGEN010` | Info | Override silently skipped (same prefix) | Still suppressed by design |
| `MODELGEN011` | Warning | Dependency in NodeSet `NamespaceUris` but not in AdditionalFiles **and** not in `ReferencedModels` | Wired |
| `MODELGEN012` | Info | Two referenced assemblies declare same URI; tie-break by `(Version, PublicationDate)` | Already wired (no change) |

## Todos (proposed)

1. `validator-register-external` — add `RegisterExternalNamespaces` API on `ModelDesignValidator`; pre-seed `m_designFilePaths` and synthetic `Namespace` entries.
2. `nodeset-to-design-external` — same plumbing in `NodeSetToModelDesign.ImportNodeSet` so transitive nodeset references register external prefixes.
3. `wire-validator-from-generators` — pass `referencedModels` from `Generators.GenerateCode` into both validator entry points.
4. `fire-modelgen011` — wire `MODELGEN011` in `SourceGenerator`/`ModelCompilation` for truly-unresolved dependencies (demote current error → warning).
5. `e2e-design-deps-tests` — `ModelDependencyResolutionTests.cs` (4 cases above).
6. `validator-unit-tests` — direct unit test for `RegisterExternalNamespaces`.
7. `docs-design-deps` — update `Docs/ModelDependencies.md`.

## Risks

- **`ModelDesignValidator` is large (~5900 lines).** Adding namespace-registration logic could surface latent assumptions about every namespace having a backing design file — particularly in `LoadDesignFiles`, `MergeDictionary`, and the schema generators. Mitigation: gate the new code on `external.ContainsKey(uri)` so the existing single-namespace-per-file path is unchanged.
- **NodeId resolution for external types.** `NodeIdGenerator` emits `Namespaces.{Name}` constants; if no `*.NodeIds.g.cs` is generated locally for an external URI we need the upstream's constant to be visible. Should already work because the upstream emits its own `Namespaces.{Name}` under `referenced.Prefix`. Verify with a test.
- **Schema generation (XmlSchemaGenerator/BinarySchemaGenerator).** External types should be referenced via their published schemas, not re-emitted inline. The current generators may emit duplicate schema entries for any namespace they enumerate. Mitigation: add an `IsExternalNamespace` skip in the inline-emit loop; rely on the upstream assembly's embedded schema resource at runtime.
- **Different-prefix scenario.** If the consumer picks a prefix that differs from the producer's, the local types will live alongside but cannot inherit from the upstream's types directly (different C# namespaces). For Phase 2 we generate locally in this case; cross-assembly inheritance is Phase 3 and probably a non-goal.

## Open questions (must answer before implementation)

1. When a transitive NodeSet is missing AND no referenced-assembly entry resolves it, should `MODELGEN011` be a **warning** (allows partial generation, possibly producing CS errors) or stay an **error** (current behaviour, hard-aborts)? Plan default: **warning** + abort that single model only — let the rest of the compilation proceed.
2. For different-prefix overrides: emit the `[assembly: ModelDependencyAttribute]` for both prefixes, or only the local one? Plan default: **local only**, since the upstream already emits its own.
3. Do we need a `<ModelSourceGeneratorIgnoreReferences>true</ModelSourceGeneratorIgnoreReferences>` MSBuild escape hatch for users who want the old behaviour? Plan default: **yes**, low-cost insurance for migration.
