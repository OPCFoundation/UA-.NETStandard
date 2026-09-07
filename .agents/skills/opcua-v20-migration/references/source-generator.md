# Source generator: `<Type>Collection` shim emission + `MIG01` playbook

The migration package's `Opc.Ua.MigrationAnalyzer.Generator.dll` is a Roslyn
`IIncrementalGenerator` that emits `public sealed [Obsolete] class
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
and NuGet dependencies, and emits a public subclass of `List<Foo>` only for
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

Everything else falls through to two lookup paths:

- **Consumer-source `<UserType>Collection` patterns** — the historical model
  compiler emitted `Foo.BarCollection` for a complex type `Foo.Bar`.
  `Compilation.GetSymbolsWithName` finds source declarations by short name
  independently of `using` directives. Exactly one match is required.
- **Primitive-typed wrappers** (`Int32Collection`, `BooleanCollection`,
  `StringCollection`, …) — metadata lookup probes `System.<Name>`.
- **Built-in OPC UA-typed wrappers whose element name didn't change**
  (`NodeIdCollection`, `VariantCollection`, `DataValueCollection`,
  `ExtensionObjectCollection`, `EndpointDescriptionCollection`,
  `ReferenceDescriptionCollection`, `BrowsePathCollection`,
  `ReadValueIdCollection`, `WriteValueCollection`,
  `MonitoredItemCreateRequestCollection`, …) — metadata lookup probes
  `Opc.Ua.<Name>`.

Dependency metadata whose exact full name is not `System.<Type>` or
`Opc.Ua.<Type>` is not scanned.

### 2. Source declaration and standard metadata lookup

For every other `<Foo>Collection` short name, the generator strips the
`Collection` suffix. It first calls
`Compilation.GetSymbolsWithName("Foo", SymbolFilter.Type, …)` for consumer
source declarations. If exactly one `INamedTypeSymbol` matches, it emits the
shim with the fully qualified type reference. If no source declaration matches,
it probes `System.Foo` and `Opc.Ua.Foo` with `GetTypeByMetadataName`.

### 3. MIG01 (unresolvable)

Zero matches or > 1 matches → the generator emits **no** shim and reports
`MIG01` instead. Its help link opens the
[resolution playbook below](#mig01-resolution-playbook);
[`analyzer-rules.md`](analyzer-rules.md#mig01--generator-cant-resolve-element-type-for-foocollection)
provides the compact rule summary.

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
    public sealed class Int32Collection : global::System.Collections.Generic.List<int>
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

- **`public sealed`** — legacy public signatures keep compiling while the
  migration package is installed. The shim remains `[Obsolete]` and disappears
  when the package is removed, so it is a migration bridge rather than a stable
  public API.
- **`[Obsolete]` with `(UA0002)` suffix** — fires both CS0612 (or CS0618) **and**
  UA0002 with a consistent rule id.
- **Implicit conversion to `ArrayOf<TElement>`** — graceful bridge into 2.0
  APIs that take `ArrayOf<T>`.
- **`global::` qualification everywhere** — avoids ambiguity with any
  consumer-defined types of the same name.
- **One file per detected name** — stable incremental cache keys; the generator
  re-runs only when the consumer's reference set changes.

## MIG01 resolution playbook

When the user reports `MIG01: Cannot resolve a unique element type 'Foo' for
legacy wrapper 'FooCollection'`, walk through these in order:

### 1. Zero source matches

```csharp
// Before — Vendor.WaterPump is dependency metadata, which is not scanned
public WaterPumpCollection Pumps { get; set; }   // MIG01

// After — state the intended element type directly
public ArrayOf<global::Vendor.WaterPump> Pumps { get; set; }
```

### 2. Multiple candidates

```csharp
// Before — both Acme.Boiler and Heaters.Boiler exist; generator picks neither
public BoilerCollection MyBoilers { get; set; }   // MIG01

// After — fully-qualify the intended element type in the replacement
public ArrayOf<global::Acme.Boiler> MyBoilers { get; set; }
```

Qualifying the legacy wrapper does not disambiguate the element lookup. The
generator emits wrappers into `Opc.Ua`, while MIG01 concerns the stripped
element short name.

### 3. Element type lives in dependency metadata

Adding a `PackageReference`, `ProjectReference`, or `using` makes the element
available to C# but does not extend the generator's metadata search beyond the
exact full names `System.<Type>` and `Opc.Ua.<Type>`. Migrate the site manually
to the intended fully qualified `List<T>` / `ArrayOf<T>`, or define the wrapper
explicitly.

### 4. Element type was deleted in your own migration

If `Foo` itself was renamed / removed during your 2.0 work, the
`<Foo>Collection` reference is dead. Replace it with the new shape directly
(`List<NewFoo>` / `ArrayOf<NewFoo>`) and the generator stops complaining.

### 5. Force the shape manually

Last resort: define the wrapper class yourself in consumer code. Match its
accessibility to the signatures that use it; when the symbol binds, the
generator skips emission. This is useful when the element type is genuinely
synthesized at runtime (rare).

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
- **Temporary public emission.** Generated types keep legacy public signatures
  compiling only while the package is installed. Migrate all remaining
  references before removing the package.
