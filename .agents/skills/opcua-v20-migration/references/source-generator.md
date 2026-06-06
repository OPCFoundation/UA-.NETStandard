# Source generator: `<Type>Collection` shim emission + `MIG01` playbook

The migration package's `Opc.Ua.MigrationAnalyzer.Generator.dll` is a Roslyn
`IIncrementalGenerator` that emits `internal sealed [Obsolete] class
<Name>Collection : List<TElement>` shims into the consumer's compilation for
every legacy `<Type>Collection` wrapper the consumer references but that 2.0
removed.

## Why a generator (and not a runtime shim)

The 1.5.378 model compiler historically emitted `<UserType>Collection` for
**every** user-defined complex type a consumer compiled into their own DLL —
`BoilerStateCollection`, `WaterPumpEventCollection`, vendor-specific
structures, etc. A shipped runtime shim could cover the small set of built-in
names, but **not** arbitrary user-compiled element types.

The generator pattern lets the package cover the full open-ended catalog: it
runs in the consumer's compilation context, sees every `<Foo>Collection` the
consumer's code references, resolves `Foo` against the consumer's own types
+ NuGet dependencies, and emits an internal subclass of `List<Foo>` only for
the names the consumer actually uses.

## Pipeline

```
┌─────────────────────────────┐
│ Consumer source has         │       ┌────────────────────┐
│   new Int32Collection {}    │ ─────▶│ Syntactic filter   │  match: ends in "Collection"
│   DataValueCollection x;    │       │ (cheap, every kb)  │  in type position
│   WaterPumpCollection wps;  │       └────────┬───────────┘
│   NeverSeenCollection nss;  │                │
└─────────────────────────────┘                ▼
                                  ┌──────────────────────────┐
                                  │ Semantic transform       │
                                  │                          │
                                  │ if symbol binds → skip   │
                                  │ else:                    │
                                  │   1. catalog override?   │  (rename cases only)
                                  │   2. semantic lookup?    │  (Compilation.GetSymbolsWithName)
                                  │   3. else MIG01          │
                                  └────────┬─────────────────┘
                                           │
                                           ▼
                                  ┌──────────────────────────┐
                                  │ Dedup + emit             │
                                  │ <Name>Collection.g.cs    │
                                  │ per unique entry         │
                                  └──────────────────────────┘
```

### 1. Catalog override (rename cases only)

The catalog only contains entries where semantic lookup would resolve to the
**wrong** type (because the underlying element type was renamed across the
1.5.378 → 2.0 boundary) or where it would resolve **ambiguously**:

| Legacy short name | Pinned 2.0 element type | Why |
|---|---|---|
| `DateTimeCollection` | `Opc.Ua.DateTimeUtc` | 1.5.378 element was `System.DateTime`; 2.0 needs `DateTimeUtc`. Semantic lookup of `DateTime` finds `System.DateTime` (wrong). |
| `GuidCollection` | `Opc.Ua.Uuid` | 1.5.378 element was `System.Guid`; 2.0 needs `Uuid`. Semantic lookup of `Guid` finds `System.Guid` (wrong). |
| `ByteStringCollection` | `Opc.Ua.ByteString` | 1.5.378 element was `byte[]`; 2.0 needs `Opc.Ua.ByteString` (now a value type). |
| `XmlElementCollection` | `System.Xml.XmlElement` | 2.0 introduced a value-typed `Opc.Ua.XmlElement` alongside the BCL `System.Xml.XmlElement` → the bare short name "XmlElement" resolves ambiguously. Pin the legacy interpretation so 1.5.378 callers drop in unchanged. |

Everything else falls through to semantic lookup. This covers:

- **Primitive-typed wrappers** (`Int32Collection`, `BooleanCollection`,
  `StringCollection`, …) — semantic lookup resolves to `System.Int32`,
  `System.Boolean`, `System.String`, etc., which behave identically to the C#
  aliases.
- **Built-in OPC UA-typed wrappers whose element name didn't change**
  (`NodeIdCollection`, `VariantCollection`, `DataValueCollection`,
  `ExtensionObjectCollection`, `EndpointDescriptionCollection`,
  `ReferenceDescriptionCollection`, `BrowsePathCollection`,
  `ReadValueIdCollection`, `WriteValueCollection`,
  `MonitoredItemCreateRequestCollection`, …) — semantic lookup resolves
  uniquely to the matching `Opc.Ua.*` type.
- **Model-compiled `<UserType>Collection`** patterns — the historical
  model-compiler convention emitted `Foo.BarCollection` for any complex type
  `Foo.Bar`. Semantic lookup of `Bar` resolves uniquely in the consumer's
  compilation.

### 2. Semantic lookup (arbitrary user types)

For every other `<Foo>Collection` short name, the generator strips the
`Collection` suffix and calls
`Compilation.GetSymbolsWithName("Foo", SymbolFilter.Type, …)`. If exactly one
`INamedTypeSymbol` matches, it emits the shim with the fully-qualified type
reference (`global::Acme.WaterPump`, `global::Vendor.Devices.BoilerState`, etc).

### 3. MIG01 (unresolvable)

Zero matches or > 1 matches → the generator emits **no** shim and reports
`MIG01` instead, with the help link pointing at this skill's
[`analyzer-rules.md`](analyzer-rules.md#mig01--generator-cant-resolve-element-type-for-foocollection)
and `Docs/MigrationGuide.md`.

## Generated file shape

For a detected `Int32Collection` reference, the generator emits
`Int32Collection.g.cs` into the consumer's compilation:

```csharp
// <auto-generated/>
#nullable enable
namespace Opc.Ua
{
    /// <summary>
    /// Source-generated shim for the legacy 'Int32Collection' wrapper that was
    /// removed in 2.0. Inherits from List<int> so 1.5.378-style call sites
    /// compile, and converts implicitly to ArrayOf<int> so 2.0 APIs that
    /// expect ArrayOf still accept the instance. Use List<int> or ArrayOf<int>
    /// directly. (UA0002)
    /// </summary>
    [global::System.Obsolete(
        "'Int32Collection' was removed in 2.0. Use 'List<int>' " +
        "or 'ArrayOf<int>' instead. (UA0002)")]
    internal sealed class Int32Collection : global::System.Collections.Generic.List<int>
    {
        public Int32Collection() { }
        public Int32Collection(int capacity) : base(capacity) { }
        public Int32Collection(global::System.Collections.Generic.IEnumerable<int> collection)
            : base(collection) { }
        public static implicit operator global::Opc.Ua.ArrayOf<int>(Int32Collection? value)
            => value is null ? default : value.ToArrayOf();
    }
}
```

### Design choices in the generated shape

- **`internal sealed`** — the shim never leaks through the consumer's public
  API surface. (Consequence: consumer public APIs returning a
  `<Type>Collection` will hit `CS0050: Inconsistent accessibility`. That's the
  intended signal that the public surface must migrate first.)
- **`[Obsolete]` with `(UA0002)` suffix** — fires both CS0612 (or CS0618) **and**
  UA0002 with a consistent rule id.
- **Implicit conversion to `ArrayOf<TElement>`** — graceful bridge into 2.0
  APIs that take `ArrayOf<T>`.
- **`global::` qualification everywhere** — avoids ambiguity with any
  consumer-defined types of the same name.
- **One file per detected name** — stable incremental cache keys; the generator
  re-runs only when the consumer's reference set changes.

## MIG01 resolution playbook

When the user reports `MIG01: Cannot resolve element type 'Foo' for legacy
wrapper 'FooCollection'`, walk through these in order:

### 1. Missing `using` directive (most common)

```csharp
// Before — generator can't see Acme.WaterPump because there's no using
public sealed class Pumps : IList<WaterPumpCollection> { … }   // MIG01 fires

// After — add the using; generator emits WaterPumpCollection : List<Acme.WaterPump>
using Acme;
public sealed class Pumps : IList<WaterPumpCollection> { … }
```

### 2. Multiple candidates

```csharp
// Before — both Acme.Boiler and Heaters.Boiler exist; generator picks neither
public BoilerCollection MyBoilers { get; set; }   // MIG01

// After — fully-qualify the type, or rename one of the conflicting types
public global::Acme.BoilerCollection MyBoilers { get; set; }
```

Note: name resolution treats the wrapper reference itself as
`Opc.Ua.BoilerCollection` (the generator emits into the `Opc.Ua` namespace).
The MIG01 is on the *element* type lookup, not the wrapper.

### 3. Element type lives in an unreferenced NuGet

The generator only sees types in NuGets that `dotnet restore` materialized. Add
the missing `<PackageReference>` to the consumer csproj. Common case: vendor
device libraries that 1.5.378 consumers referenced transitively via
`Quickstarts.Servers` (which no longer exists on 2.0 — see
[`known-gaps.md`](known-gaps.md)).

### 4. Element type was deleted in your own migration

If `Foo` itself was renamed / removed during your 2.0 work, the
`<Foo>Collection` reference is dead. Replace it with the new shape directly
(`List<NewFoo>` / `ArrayOf<NewFoo>`) and the generator stops complaining.

### 5. Force the shape manually

Last resort: define an `internal` partial of the wrapper yourself in the
consumer code, mirroring what the generator would have produced. Useful when
the element type is genuinely synthesized at runtime (rare).

## Performance

The generator is incremental — Roslyn re-runs the transform stage only for
changed syntax trees, and the emit stage only when the deduplicated
`(shortName, elementDisplay)` set changes. `dotnet build` + IDE
`/p:ReportAnalyzer=true` numbers from real consumer projects:

```
Generator: Opc.Ua.MigrationAnalyzer.Generator
  Time (s)    %   Generator
  < 0.001  < 1   Opc.Ua.MigrationAnalyzer.Generator.MigrationGenerator
```

In practice the generator is unmeasurable next to csc.exe's own startup cost.

## Limitations

- **Identifier-position only.** The generator matches `IdentifierNameSyntax`
  in type positions (object-creation type, variable/parameter/field/property
  type, generic argument, `typeof`, cast). It does **not** intercept calls like
  `Activator.CreateInstance(typeof(Int32Collection))` — but those are extremely
  rare in real consumer code and would have failed at runtime on 2.0 anyway.
- **Single-element-name semantic lookup.** The generator looks up the bare
  element short name with `Compilation.GetSymbolsWithName`; it doesn't try to
  enumerate generic instantiations or open generics. `MyCollection<T>` patterns
  are out of scope (and were never produced by the OPC UA model compiler).
- **Internal-only emission.** No way to override; consumers with `public` APIs
  returning `<Type>Collection` must migrate the public surface first (the
  resulting `CS0050` is the intended forcing function).
