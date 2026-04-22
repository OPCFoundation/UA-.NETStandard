# Cross-Assembly Model Dependencies

The OPC UA source generator emits `[assembly: Opc.Ua.ModelDependencyAttribute(...)]`
metadata on every assembly that has nodesets or design files in `<AdditionalFiles>`.
The attribute records, for each model the assembly emits or transitively consumes,
the model URI, the C# namespace prefix the generator used, and (when known) the
version and publication date.

Downstream consumers that reference such an assembly no longer need to re-add
those upstream nodesets to their own `<AdditionalFiles>`. The generator scans the
attributes on referenced assemblies and uses them to:

1. **Suppress the missing-dependency error** in nodeset dependency collection
   when the missing namespace is known to be supplied by a referenced assembly.
2. **Apply override resolution** when a local `<AdditionalFiles>` entry refers
   to a model URI that is also in a referenced assembly:
   - **Same C# prefix** → local generation is silently skipped (the referenced
     assembly already supplies the types; duplicate emission would cause
     `CS0101`).
   - **Different C# prefix** → local generation proceeds; types live in a
     different C# namespace so no symbol conflict occurs.
3. **Tie-break** when more than one referenced assembly provides the same model
   URI by selecting the entry with the highest `(Version, PublicationDate)`
   lexicographic tuple. The losing entries are reported as `MODELGEN012` info
   diagnostics.

## Diagnostics

| ID            | Severity | Meaning                                                            |
| ------------- | -------- | ------------------------------------------------------------------ |
| `MODELGEN010` | Info     | Local model skipped because a referenced assembly provides it.     |
| `MODELGEN012` | Info     | Multiple referenced assemblies provide the same model URI.         |

## Implementation

The Roslyn-side scan lives in
`Tools/Opc.Ua.SourceGeneration/ReferencedModelDependencyScanner.cs` and uses
`IAssemblySymbol.GetAttributes()` so it works for both `PortableExecutableReference`
and `CompilationReference`, hooks Roslyn's per-symbol incremental cache, avoids
file IO, and is AOT-safe inside the generator.

The emitter lives in
`Tools/Opc.Ua.SourceGeneration.Core/Generators/ModelDependencyGenerator.cs`
and produces one `{prefix}.ModelDependencies.g.cs` per generated model
containing assembly-attribute lines for the model itself and every model it
consumes (including the closure recovered from referenced assemblies).

## Limitations (Phase 1)

This first cut keeps cross-assembly type resolution out of scope. When a
nodeset's transitive namespace is satisfied only by a referenced assembly the
generator skips the missing-dependency error but does **not** wire that
namespace's type definitions into the validator. Downstream users that need to
emit references to types from those upstream models (e.g. subtypes, complex
type fields, method input/output arguments) must still add the upstream design
or nodeset files to `<AdditionalFiles>` for now. A follow-up will register the
referenced model prefixes with `ModelDesignValidator` so that no AdditionalFile
duplication is required.
