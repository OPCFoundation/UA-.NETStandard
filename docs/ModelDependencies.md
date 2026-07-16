# Cross-Assembly Model Dependencies

The OPC UA source generator emits `[assembly: Opc.Ua.ModelDependencyAttribute(...)]`
metadata on every assembly that has nodesets or design files in `<AdditionalFiles>`.
A single attribute carries both the lightweight dependency-closure information
**and**, on the assembly's self-declaration entry, a compact binary type-table
payload that downstream source generators can decode without re-walking
`AdditionalFiles`. Each instance of the attribute records, for one model the
assembly emits or transitively consumes:

- the model URI,
- the C# namespace prefix the generator used,
- (when known) the version and publication date,
- the C# identifier of the assembly's `Namespaces` class entry for the model,
- and — on self-declaration entries only — a base64-encoded Deflate-compressed
  `ModelDependencyV1` type-table payload.

Downstream consumers that reference such an assembly do not need to re-add
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
5. **Import the type-table payload** on self-declaration entries so the
   validator's node table is pre-populated with the upstream's types,
   children, method arguments, and DataType fields. Cross-namespace
   `BaseType` / `TypeDefinition` / `DataType` references in the consumer's
   own models then resolve against the imported types without needing the
   upstream NodeSet2/ModelDesign XML in `AdditionalFiles`.

## Attribute shape

The attribute constructor accepts six arguments (the trailing four are
optional):

```csharp
[assembly: Opc.Ua.ModelDependencyAttribute(
    modelUri:        "http://opcfoundation.org/UA/DI/",
    prefix:          "Opc.Ua.Di",
    version:         "1.05.0",
    publicationDate: "2025-11-15T00:00:00Z",
    name:            "OpcUaDi",
    payload:         "qscBA…<base64 ModelDependencyV1>…AAA=")]
```

The `name` parameter records the C# identifier the assembly used inside its
`Namespaces` class — i.e. the `name` consumers must use when emitting
`global::{Prefix}.Namespaces.{Name}` cross-namespace constant references.

The `payload` parameter is non-null only on the assembly's own
self-declaration entry. Transitive-dependency entries (the one-per-referenced-
model rows the generator re-emits) carry `null`. The producing assembly is the
canonical source of its type-table description.

## Payload wire format (`ModelDependencyV1`)

Encoding lives in
`tools/Opc.Ua.SourceGeneration.Core/Dependency/ModelDependencyV1.cs`:

- Magic header: `0xAA 0xC7`
- Version byte: `0x01`
- Compression byte: `0x01` (Deflate)
- Body (compressed): `ModelUri` string + node array, each carrying symbolic
  name/namespace, class name, kind, base-type chain, numeric/string NodeId,
  abstract / enumeration flags, DataType fields, and declared instance
  children (with method-argument lists). Deterministically sorted by
  `(SymbolicNamespace, SymbolicName)` so the produced base64 string is
  byte-reproducible across builds.

Readers reject unknown versions cleanly and the downstream pipeline falls
back to explicit `AdditionalFiles` resolution when a payload cannot be
decoded.

## Diagnostics

| ID            | Severity | Meaning                                                            |
| ------------- | -------- | ------------------------------------------------------------------ |
| `MODELGEN010` | Warning  | A `[NodeManager]` attribute could not be bound to a model (unmatched selector, or ambiguous when the project has multiple models). |
| `MODELGEN012` | Info     | Multiple referenced assemblies provide the same model URI.         |
| `MODELGEN013` | Info     | Local model skipped because a referenced assembly provides it.     |

## Implementation

The Roslyn-side scan lives in
`tools/Opc.Ua.SourceGeneration/ReferencedModelDependencyScanner.cs` and uses
`IAssemblySymbol.GetAttributes()` so it works for both
`PortableExecutableReference` and `CompilationReference`, hooks Roslyn's
per-symbol incremental cache, avoids file IO, and is AOT-safe inside the
generator. Each attribute is read into a `ModelDependencyReference` whose
`GetDependency()` method lazily decodes the payload through a memoising
`ConditionalWeakTable` keyed on the payload string.

The emitter lives in
`tools/Opc.Ua.SourceGeneration.Core/Generators/ModelDependencyGenerator.cs`
and produces one `{prefix}.ModelDependencies.g.cs` per generated model
containing assembly-attribute lines for the model itself (with the
`ModelDependencyV1` payload) and every model it consumes (with a `null`
payload). The template lives in `ModelDependencyTemplates.cs` and uses the
shared `Token` infrastructure for replacement.

The cross-namespace prefix override step lives in
`tools/Opc.Ua.SourceGeneration.Core/Generators.cs` as
`OverrideDependencyPrefixes`. It runs after `OpenModelDesign` and before
generation so that all downstream emitters see the harmonised prefix/name
values.

The payload-import surface lives directly on
`tools/Opc.Ua.SourceGeneration.Core/Schema/ModelDesignValidator.cs` (the
former `ModelDesignValidator.SnapshotImport.cs` partial was folded into the
main file). The validator's `ImportDependency(dependency, prefix, name)` API
queues a `ModelDependencyV1` for ingestion; `ApplyPendingDependencies()`
materialises the carried types into the validator's node table before the
dependency-loading pass walks `AdditionalFiles`, so consumer types can
resolve `BaseType` / `TypeDefinition` / `DataType` references against the
upstream's published types without those upstream models being present in
`AdditionalFiles`.


