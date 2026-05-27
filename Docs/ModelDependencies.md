# Cross-Assembly Model Dependencies

The OPC UA source generator emits `[assembly: Opc.Ua.ModelDependencyAttribute(...)]`
metadata on every assembly that has nodesets or design files in `<AdditionalFiles>`.
The attribute records, for each model the assembly emits or transitively consumes,
the model URI, the C# namespace prefix the generator used, (when known) the
version and publication date, and the C# identifier of the assembly's
`Namespaces` class entry for the model.

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
3. **Cross-namespace prefix mapping** — when a local generator loads a
   NodeSet2 that depends on another model, it auto-generates dependency
   prefixes (e.g. `Opc.Ua.DI`) that may not match the C# namespace the
   referenced assembly actually uses (`Opc.Ua.Di`). The override step rewrites
   the dependency namespace's `Prefix` (and `Name`) to match the values
   published by the referenced assembly's `[ModelDependencyAttribute]`, so
   cross-namespace type references emitted into the local code compile
   correctly. The target namespace itself is never rewritten.
4. **Tie-break** when more than one referenced assembly provides the same model
   URI by selecting the entry with the highest `(Version, PublicationDate)`
   lexicographic tuple. The losing entries are reported as `MODELGEN012` info
   diagnostics.

## Attribute shape

The attribute constructor accepts five arguments (the trailing three are
optional):

```csharp
[assembly: Opc.Ua.ModelDependencyAttribute(
    modelUri:        "http://opcfoundation.org/UA/DI/",
    prefix:          "Opc.Ua.Di",
    version:         "1.05.0",
    publicationDate: "2025-11-15T00:00:00Z",
    name:            "OpcUaDi")]
```

The `name` parameter records the C# identifier the assembly used inside its
`Namespaces` class — i.e. the `name` consumers must use when emitting
`global::{Prefix}.Namespaces.{Name}` cross-namespace constant references.

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

The cross-namespace prefix override step lives in
`Tools/Opc.Ua.SourceGeneration.Core/Generators.cs` as
`OverrideDependencyPrefixes`. It runs after `OpenModelDesign` and before
generation so that all downstream emitters see the harmonised prefix/name
values.

