# Breaking Changes: ComplexTypes Refactor

This document accumulates the breaking changes from the multi-wave
ComplexTypes refactor. **Wave 1** split the legacy mono-package into a
three-project architecture; **Wave 2** isolated the Reflection.Emit path
into a separate opt-in package so AOT-published consumers can omit it.

---

# Wave 1: Three-Project Split

## 1. Namespace moves (the big one)

Every type listed below moved from `Opc.Ua.Client.ComplexTypes` to **`Opc.Ua.ComplexTypes`**. Consumers must add `using Opc.Ua.ComplexTypes;` (in addition to or in place of `using Opc.Ua.Client.ComplexTypes;`, depending on what they touch).

**Interfaces:**
- `IComplexTypeResolver`
- `IComplexTypeFactory`
- `IComplexTypeBuilder`
- `IComplexTypeFieldBuilder`

**Default (AOT-friendly) implementations:**
- `DefaultComplexTypeFactory`
- `DefaultComplexTypeBuilder`
- `DefaultComplexTypeFieldBuilder`

**Reflection.Emit implementations:**
- `ComplexTypeBuilderFactory`
- `ComplexTypeBuilder`
- `ComplexTypeFieldBuilder`
- `AssemblyModule`
- `AttributeExtensions`

**Runtime base types and attributes:**
- `BaseComplexType`
- `OptionalFieldsComplexType`
- `UnionComplexType`
- `ComplexTypePropertyInfo`
- `IComplexTypeProperties`
- `StructureDefinitionAttribute`
- `StructureFieldAttribute`
- `StructureTypeIdAttribute`
- `StructureBaseDataType` (enum)

**Schema / dictionary:**
- `DataDictionary`
- `DataTypeDefinitionExtension`

**Exceptions:**
- `DataTypeNotFoundException`
- `DataTypeNotSupportedException`

## 2. Types that stayed in `Opc.Ua.Client.ComplexTypes` namespace but moved assemblies

These were previously compiled into `Opc.Ua.Client.dll`; they are now in `Opc.Ua.Client.ComplexTypes.dll`. Source code that only uses `using Opc.Ua.Client.ComplexTypes;` keeps working, but **reflection-based** or **assembly-qualified-name** consumers will break:

- `ComplexTypeSystem`
- `NodeCacheResolver`

## 3. Assembly redistribution

| Type | Was in | Now in |
| --- | --- | --- |
| Interfaces, default builders, `DataDictionary`, `DataTypeDefinitionExtension`, exceptions | `Opc.Ua.Client.dll` | **`Opc.Ua.ComplexTypes.dll`** (new) |
| Reflection.Emit builders, runtime base types, attributes | `Opc.Ua.Client.ComplexTypes.dll` | **`Opc.Ua.ComplexTypes.dll`** (new) |
| `ComplexTypeSystem`, `NodeCacheResolver` | `Opc.Ua.Client.dll` | `Opc.Ua.Client.ComplexTypes.dll` |

Reflection by `Type.AssemblyQualifiedName`, `Assembly.GetType("Opc.Ua.Client.ComplexTypes.X, Opc.Ua.Client")`, or strong-name-pinned loaders will fail.

## 4. New required dependencies

- Anyone consuming the moved types via source code needs the new `OPCFoundation.NetStandard.Opc.Ua.ComplexTypes` NuGet package. It comes transitively when you depend on `OPCFoundation.NetStandard.Opc.Ua.Client.ComplexTypes`, so most consumers won't notice — but **projects that previously got the interfaces and `Default*` builders from `Opc.Ua.Client` alone now need to add the new package explicitly**, because those types no longer ship inside `Opc.Ua.Client.dll`.

## 5. No backward-compatibility shims

The original plan called for `[assembly: TypeForwardedTo(...)]` to forward the old type identities. **This was intentionally not done.** Consequence:

- Source code must update `using` statements.
- Binary consumers compiled against the old layout (1.5.x or earlier `Opc.Ua.Client.dll` / `Opc.Ua.Client.ComplexTypes.dll`) will throw `TypeLoadException` at runtime when they touch any moved type. Recompile against the new layout.

## 6. `InternalsVisibleTo` changes

- `Opc.Ua.Client.ComplexTypes.csproj` now also grants internals to `Opc.Ua.Client.Tests` (because `NodeCacheResolver`'s `internal` extension methods like `LoadDictionaryAsync` / `ReadDictionaryAsync` moved out of `Opc.Ua.Client`).
- `Opc.Ua.ComplexTypes.csproj` grants internals to `Opc.Ua.Client`, `Opc.Ua.Client.ComplexTypes`, `Opc.Ua.Server.ComplexTypes`, and the corresponding test assemblies.

External assemblies that previously relied on `InternalsVisibleTo` on the old `Opc.Ua.Client` or `Opc.Ua.Client.ComplexTypes` for any of the moved types will need that grant restated on `Opc.Ua.ComplexTypes`.

## 7. Net-new public surface (additive, not breaking — listed for completeness)

- `Opc.Ua.Server.ComplexTypes.ServerComplexTypeSystem` (`RegisterEnumeration`, `RegisterStructure`, `Flush`)
- `Opc.Ua.Server.ComplexTypes.ServerComplexTypeSystemFactory`
- `Microsoft.Extensions.DependencyInjection.OpcUaComplexTypesServerBuilderExtensions.AddComplexTypes(this IOpcUaServerBuilder)`

## 8. Recommended migration path

```csharp
// Before
using Opc.Ua.Client.ComplexTypes;

IComplexTypeFactory factory = new ComplexTypeBuilderFactory();
var typeSystem = new ComplexTypeSystem(session);
```

```csharp
// After
using Opc.Ua.ComplexTypes;          // interfaces, builders, DataDictionary, exceptions, attributes, base types
using Opc.Ua.Client.ComplexTypes;   // ComplexTypeSystem, NodeCacheResolver, DI extension

IComplexTypeFactory factory = new ComplexTypeBuilderFactory();
var typeSystem = new ComplexTypeSystem(session);
```

Drop a single new `using Opc.Ua.ComplexTypes;` at the top of the file and most code compiles unchanged.

---

# Wave 2: AOT / Reflection.Emit Split

The single `Opc.Ua.ComplexTypes` library produced by Wave 1 mixed an
AOT-friendly core (interfaces, `Default*` builders, `DataDictionary`,
`DataTypeDefinitionExtension`, exceptions) with a Reflection.Emit code
path (the `ComplexTypeBuilder*` trio, runtime base classes, runtime
attributes). Wave 2 extracts the Emit path into its own NuGet package so
that AOT-published apps never load the Emit code and trim-safe analysis
keeps working.

## 1. Namespace moves (from `Opc.Ua.ComplexTypes` to **`Opc.Ua.ComplexTypes.Emit`**)

**Reflection.Emit builders:**
- `ComplexTypeBuilder`
- `ComplexTypeBuilderFactory`
- `ComplexTypeFieldBuilder`
- `AssemblyModule`
- `AttributeExtensions`

**Runtime base types and helpers (only used as bases for Emit-generated classes):**
- `BaseComplexType`
- `OptionalFieldsComplexType`
- `UnionComplexType`
- `ComplexTypePropertyInfo`
- `IComplexTypeProperties`

**Attributes (consumed via reflection on Emit-generated types):**
- `StructureDefinitionAttribute`
- `StructureFieldAttribute`
- `StructureTypeIdAttribute`
- `StructureBaseDataType` (enum)

Consumers add `using Opc.Ua.ComplexTypes.Emit;` to keep these symbols
visible.

## 2. Assembly redistribution

| Type | Was in (after Wave 1) | Now in |
| --- | --- | --- |
| Emit builders + base types + attributes | `Opc.Ua.ComplexTypes.dll` | **`Opc.Ua.ComplexTypes.Emit.dll`** (new) |
| `IComplexType*`, `DefaultComplexType*`, `DataDictionary`, `DataTypeDefinitionExtension`, exceptions | `Opc.Ua.ComplexTypes.dll` | `Opc.Ua.ComplexTypes.dll` (unchanged) |

Reflection by `Type.AssemblyQualifiedName` against any moved type
breaks; recompile.

## 3. New required NuGet package (opt-in)

`OPCFoundation.NetStandard.Opc.Ua.ComplexTypes.Emit` is the new
opt-in package. It is **not** pulled in transitively by
`Opc.Ua.Client.ComplexTypes` — that package now only depends on the
AOT-friendly core. Apps that need runtime concrete .NET classes for
custom DataTypes must add the Emit package explicitly.

Server hosts (`Opc.Ua.Server.ComplexTypes`) are unaffected by default —
they use `DefaultComplexTypeFactory`. To use the Emit factory on the
server side, add the Emit package and pass
`() => new ComplexTypeBuilderFactory()` to the
`ServerComplexTypeSystemFactory(ITelemetryContext, Func<IComplexTypeFactory>)`
constructor.

## 4. DI default flip — **behavioural break**

| Call | Before (Wave 1) | After (Wave 2) |
| --- | --- | --- |
| `IOpcUaBuilder.AddComplexTypes()` | Registers `ComplexTypeSystemFactory` bound to **`ComplexTypeBuilderFactory`** (Reflection.Emit). | Registers `ComplexTypeSystemFactory` bound to **`DefaultComplexTypeFactory`** (AOT-friendly). |
| `IOpcUaServerBuilder.AddComplexTypes()` | Same flip — server factory now defaults to `DefaultComplexTypeFactory`. | Same flip. |

To restore the Wave 1 behaviour, add the Emit package and call the new
opt-in extension:

```csharp
// Wave 1 (AddComplexTypes used Emit by default)
services.AddOpcUa().AddComplexTypes();

// Wave 2 — explicit Emit
services.AddOpcUa().AddComplexTypesWithReflectionEmit();
```

`AddComplexTypesWithReflectionEmit()` is shipped by the
`Opc.Ua.ComplexTypes.Emit` package in the
`Microsoft.Extensions.DependencyInjection` namespace and replaces the
default registration via `ServiceCollectionDescriptorExtensions.Replace`.

## 5. Static factory rename

The `ComplexTypeSystem.Create(...)` extension members shipped by the
old `ComplexTypesExtensions` (in `Opc.Ua.Client.ComplexTypes`) have
been **removed** and replaced by `CreateWithReflectionEmit(...)`
extension members shipped by the new
`Opc.Ua.ComplexTypes.Emit.ComplexTypesEmitExtensions`. The signatures
are otherwise identical:

```csharp
// Before
using Opc.Ua.Client.ComplexTypes;
var ts = ComplexTypeSystem.Create(session, telemetry);
var ts2 = ComplexTypeSystem.Create(resolver, telemetry);

// After (still want Emit)
using Opc.Ua.ComplexTypes.Emit;
var ts  = ComplexTypeSystem.CreateWithReflectionEmit(session, telemetry);
var ts2 = ComplexTypeSystem.CreateWithReflectionEmit(resolver, telemetry);

// Or (use the AOT-friendly default)
var ts = new ComplexTypeSystem(session);   // already used DefaultComplexTypeFactory in Wave 1
```

## 6. `ComplexTypeSystemFactory` / `ServerComplexTypeSystemFactory` ctor surface

Both DI factories gained a second constructor that accepts a
`Func<IComplexTypeFactory>` delegate. The original single-arg ctor
keeps the AOT-friendly behaviour:

```csharp
// AOT-friendly (Wave 2 default)
new ComplexTypeSystemFactory(telemetry);

// Emit (manual wiring, e.g. on the server side without the DI extension)
new ServerComplexTypeSystemFactory(
    telemetry,
    static () => new ComplexTypeBuilderFactory());
```

This is additive at the source level (existing single-arg call sites
still compile) but is a behavioural break in combination with §4 — the
factory the single-arg ctor produces now defaults to
`DefaultComplexTypeFactory`.

## 7. `InternalsVisibleTo` shifts

- `Opc.Ua.ComplexTypes.Emit.csproj` grants internals to
  `Opc.Ua.Client.ComplexTypes`, `Opc.Ua.Server.ComplexTypes`, and
  `Opc.Ua.Client.ComplexTypes.Tests`.
- `Opc.Ua.ComplexTypes.csproj`'s grants are unchanged but the practical
  surface area exposed by them is smaller because most consumers of
  `internal` members now live in `Opc.Ua.ComplexTypes.Emit`.

External assemblies that relied on `InternalsVisibleTo` against
`Opc.Ua.ComplexTypes` for any of the moved Emit types need to restate
the grant against `Opc.Ua.ComplexTypes.Emit`.

## 8. Recommended migration path

```csharp
// Before (Wave 1)
using Opc.Ua.ComplexTypes;
using Opc.Ua.Client.ComplexTypes;

services.AddOpcUa().AddComplexTypes();        // got Emit factory
var ts = ComplexTypeSystem.Create(session, telemetry);
IComplexTypeFactory factory = new ComplexTypeBuilderFactory();
```

```csharp
// After (Wave 2) — keep Emit behaviour
using Opc.Ua.ComplexTypes;
using Opc.Ua.ComplexTypes.Emit;
using Opc.Ua.Client.ComplexTypes;

services.AddOpcUa().AddComplexTypesWithReflectionEmit();   // explicit Emit opt-in
var ts = ComplexTypeSystem.CreateWithReflectionEmit(session, telemetry);
IComplexTypeFactory factory = new ComplexTypeBuilderFactory();   // namespace changed
```

```csharp
// After (Wave 2) — accept the AOT-friendly default
using Opc.Ua.ComplexTypes;
using Opc.Ua.Client.ComplexTypes;

services.AddOpcUa().AddComplexTypes();        // now DefaultComplexTypeFactory
var ts = new ComplexTypeSystem(session);
```
