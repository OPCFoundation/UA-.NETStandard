# Plan: Split `Opc.Ua.ComplexTypes` into AOT-Friendly Core + Opt-In Reflection.Emit Package

> **Goal:** Keep the AOT (no-Emit) and Reflection.Emit paths as separate
> NuGet packages that can be referenced and used as needed. After this
> split, an AOT-published application can depend on the AOT-friendly
> core alone and never load any Emit code; an application that wants
> runtime concrete .NET classes for custom DataTypes opts into the
> Emit package on top.
>
> Follow-on to `OpcComplexTypeSystemOverhaul.md`. Same conventions:
> breaking change (no `[TypeForwardedTo]` shims), full build + tests
> are the acceptance gate.

---

## 1. Current state (post-overhaul)

The `Opc.Ua.ComplexTypes` library produced by the previous refactor
mixes two categories of code:

### AOT-friendly (no `Reflection.Emit`, trim-safe)
| Folder | File(s) | Why it's AOT-safe |
| --- | --- | --- |
| `Builders/` | `IComplexTypeFactory.cs`, `IComplexTypeResolver.cs` | Pure interface surface |
| `Builders/` | `DefaultComplexTypeFactory.cs`, `DefaultComplexTypeBuilder.cs`, `DefaultComplexTypeFieldBuilder.cs` | Use `Encoders.Enumeration` / `Encoders.OptionSet` from `Opc.Ua.Core`; no Emit, no `MakeArrayType`, no `Activator` on dynamic types |
| `Schema/` | `DataDictionary.cs`, `DataTypeDefinitionExtension.cs` | XML schema parsing only |
| `Exceptions/` | `DataTypeException.cs` | Plain exception classes |

### NOT AOT-friendly (uses `Reflection.Emit` or runtime reflection on dynamic types)
| Folder | File(s) | Why it isn't AOT-safe |
| --- | --- | --- |
| `Builders/` | `AssemblyModule.cs` | `AssemblyBuilder.DefineDynamicAssembly`, suppressed with `IL3050` |
| `Builders/` | `ComplexTypeBuilder.cs`, `ComplexTypeBuilderFactory.cs`, `ComplexTypeFieldBuilder.cs` | `TypeBuilder.DefineType` / `DefineProperty` / IL emission |
| `Builders/` | `AttributeExtensions.cs` | `CustomAttributeBuilder`, requires unreferenced code |
| `Types/` | `BaseComplexType.cs`, `OptionalFieldsComplexType.cs`, `UnionComplexType.cs` | Runtime reflection on dynamically-generated derived classes (`Activator.CreateInstance`, `MemberwiseClone`, property walks) |
| `Types/` | `ComplexTypePropertyInfo.cs`, `IComplexTypeProperties.cs` | Reflection on `PropertyInfo` of emitted classes |
| `Types/` | `StructureDefinitionAttribute.cs`, `StructureFieldAttribute.cs`, `StructureTypeAttribute.cs` | Attribute classes ARE AOT-safe to declare, but their only consumer is `BaseComplexType` / Emit — keeping them with their consumers reduces fan-out |

### Confirmed by grep
- The source generator (`Tools/Opc.Ua.SourceGeneration*`) does **not** reference `BaseComplexType`, the union/optional types, or the attribute classes — it emits standalone classes implementing `IEncodeable` directly.
- `Tests/Opc.Ua.Aot.Tests/ComplexTypeAotTests.cs` exercises the AOT path via `DefaultComplexTypeFactory` only.
- The attribute classes are referenced only from `BaseComplexType` reflection paths, the Emit builders, and `MockResolverTests.cs` (which tests the Emit path).

---

## 2. Target layout

```
Libraries/
  Opc.Ua.ComplexTypes/                           ← stays, AOT-friendly only
    Builders/
      IComplexTypeFactory.cs                     (interfaces only)
      IComplexTypeResolver.cs
      DefaultComplexTypeFactory.cs
      DefaultComplexTypeBuilder.cs
      DefaultComplexTypeFieldBuilder.cs
    Schema/
      DataDictionary.cs
      DataTypeDefinitionExtension.cs
    Exceptions/
      DataTypeException.cs
    Properties/AssemblyInfo.cs
    Opc.Ua.ComplexTypes.csproj
      └── ProjectReference: Opc.Ua.Core
      └── IsAotCompatible = true (already set)

  Opc.Ua.ComplexTypes.Emit/                      ← NEW opt-in package
    Builders/
      AssemblyModule.cs
      ComplexTypeBuilder.cs
      ComplexTypeBuilderFactory.cs
      ComplexTypeFieldBuilder.cs
      AttributeExtensions.cs
    Types/
      BaseComplexType.cs
      OptionalFieldsComplexType.cs
      UnionComplexType.cs
      ComplexTypePropertyInfo.cs
      IComplexTypeProperties.cs
      StructureDefinitionAttribute.cs
      StructureFieldAttribute.cs
      StructureTypeAttribute.cs
    Properties/AssemblyInfo.cs
    Opc.Ua.ComplexTypes.Emit.csproj
      └── ProjectReference: Opc.Ua.Core, Opc.Ua.ComplexTypes
      └── IsAotCompatible = false  (intentional — this package is the escape hatch)
```

### Namespaces (breaking)
| Type | Old | New |
| --- | --- | --- |
| `BaseComplexType`, `OptionalFieldsComplexType`, `UnionComplexType` | `Opc.Ua.ComplexTypes` | **`Opc.Ua.ComplexTypes.Emit`** |
| `ComplexTypePropertyInfo`, `IComplexTypeProperties` | `Opc.Ua.ComplexTypes` | **`Opc.Ua.ComplexTypes.Emit`** |
| `StructureDefinitionAttribute`, `StructureFieldAttribute`, `StructureTypeIdAttribute`, `StructureBaseDataType` | `Opc.Ua.ComplexTypes` | **`Opc.Ua.ComplexTypes.Emit`** |
| `ComplexTypeBuilder`, `ComplexTypeBuilderFactory`, `ComplexTypeFieldBuilder`, `AssemblyModule`, `AttributeExtensions` | `Opc.Ua.ComplexTypes` | **`Opc.Ua.ComplexTypes.Emit`** |
| Everything in the AOT list above | `Opc.Ua.ComplexTypes` | **`Opc.Ua.ComplexTypes`** (unchanged) |

NuGet package id: `OPCFoundation.NetStandard.Opc.Ua.ComplexTypes.Emit`.
Assembly name: `Opc.Ua.ComplexTypes.Emit`.

### Project dependency graph (new)

```
Opc.Ua.Core
   ▲
   ├── Opc.Ua.ComplexTypes              (AOT-friendly)
   │      ▲
   │      ├── Opc.Ua.ComplexTypes.Emit  (NEW, opt-in, NOT AOT)
   │      │      ▲
   │      │      └── (referenced by apps that need runtime concrete classes)
   │      │
   │      ├── Opc.Ua.Client.ComplexTypes
   │      │      └── DI factory defaults to DefaultComplexTypeFactory
   │      │
   │      └── Opc.Ua.Server.ComplexTypes
   │             └── DI factory defaults to DefaultComplexTypeFactory
```

---

## 3. DI surface changes

### `Opc.Ua.Client.ComplexTypes`

| Before | After |
| --- | --- |
| `ComplexTypeSystemFactory.Create(session)` → returns `ComplexTypeSystem` bound to `ComplexTypeBuilderFactory` (Emit). | `ComplexTypeSystemFactory.Create(session)` → returns `ComplexTypeSystem` bound to **`DefaultComplexTypeFactory`** (AOT-friendly). |
| `OpcUaComplexTypesBuilderExtensions.AddComplexTypes()` registers the singleton factory (which uses Emit). | `AddComplexTypes()` keeps the same name and signature but the registered factory uses `DefaultComplexTypeFactory`. |
| `ComplexTypesExtensions.Create(ISession, ITelemetryContext)` → returns `ComplexTypeSystem` bound to `ComplexTypeBuilderFactory`. | Existing extension method **moves to** `Opc.Ua.ComplexTypes.Emit` (since it constructs an Emit factory). |

### New DI surface in `Opc.Ua.ComplexTypes.Emit`

New static class `OpcUaComplexTypesEmitBuilderExtensions` in the
`Microsoft.Extensions.DependencyInjection` namespace:

```csharp
// Replace the default (AOT-friendly) ComplexTypeSystemFactory with one
// that builds Emit-based ComplexTypeSystems. Apps that need runtime
// concrete .NET classes for custom DataTypes call this instead of
// AddComplexTypes().
public static IOpcUaBuilder AddComplexTypesWithReflectionEmit(this IOpcUaBuilder builder);

// Server-side equivalent.
public static IOpcUaServerBuilder AddComplexTypesWithReflectionEmit(this IOpcUaServerBuilder builder);
```

Plus the migrated `ComplexTypesExtensions` (extension on `ComplexTypeSystem`):

```csharp
namespace Opc.Ua.Client.ComplexTypes
{
    public static class ComplexTypesEmitExtensions
    {
        extension(ComplexTypeSystem)
        {
            public static ComplexTypeSystem CreateWithReflectionEmit(
                ISession session, ITelemetryContext telemetry);

            public static ComplexTypeSystem CreateWithReflectionEmit(
                IComplexTypeResolver resolver, ITelemetryContext telemetry);
        }
    }
}
```

(Lives in the Emit package; namespace stays `Opc.Ua.Client.ComplexTypes`
so the extension still attaches to `ComplexTypeSystem` — no consumer
needs a second `using` directive.)

### `Opc.Ua.Server.ComplexTypes`

`ServerComplexTypeSystemFactory.Create(IServerInternal)` already
defaults to `DefaultComplexTypeFactory`. The two-arg overload
`Create(IServerInternal, IComplexTypeFactory)` continues to accept a
caller-supplied Emit factory. The new
`AddComplexTypesWithReflectionEmit(this IOpcUaServerBuilder)` extension
swaps the registered factory descriptor over to Emit.

---

## 4. Implementation phases

### Phase A — Create `Opc.Ua.ComplexTypes.Emit` project
1. New project `Libraries/Opc.Ua.ComplexTypes.Emit/Opc.Ua.ComplexTypes.Emit.csproj`:
   - `AssemblyName = $(AssemblyPrefix).ComplexTypes.Emit`
   - `PackageId = $(PackagePrefix).Opc.Ua.ComplexTypes.Emit`
   - `RootNamespace = Opc.Ua.ComplexTypes.Emit`
   - `TargetFrameworks = $(LibxTargetFrameworks)`
   - **No** `IsAotCompatible` (this package is the non-AOT path)
   - `Nullable = enable`
   - References `Opc.Ua.Core` and `Opc.Ua.ComplexTypes`
   - `InternalsVisibleTo` for `Opc.Ua.Client.ComplexTypes`, `Opc.Ua.Server.ComplexTypes`, `Opc.Ua.Client.ComplexTypes.Tests`
2. `Properties/AssemblyInfo.cs` carries `[assembly: CLSCompliant(false)]`.
3. Add to `UA.slnx` under `/Libraries/`.

### Phase B — Move files into the Emit project
1. Move from `Libraries/Opc.Ua.ComplexTypes/Builders/`:
   - `AssemblyModule.cs`, `ComplexTypeBuilder.cs`, `ComplexTypeBuilderFactory.cs`, `ComplexTypeFieldBuilder.cs`, `AttributeExtensions.cs`
   → `Libraries/Opc.Ua.ComplexTypes.Emit/Builders/`
2. Move the entire `Libraries/Opc.Ua.ComplexTypes/Types/` directory → `Libraries/Opc.Ua.ComplexTypes.Emit/Types/`.
3. Update namespace on every moved file: `Opc.Ua.ComplexTypes` → `Opc.Ua.ComplexTypes.Emit`.
4. Add `using Opc.Ua.ComplexTypes;` to the Emit files that reference the interfaces / `DataTypeNotSupportedException` / etc. from the core package.
5. Re-evaluate `InternalsVisibleTo` on `Opc.Ua.ComplexTypes.csproj`: drop the `Opc.Ua.Client.ComplexTypes` grant if no internal access remains needed; keep `Opc.Ua.Client` if the moved-to-Client orchestrators still need any internal access.

### Phase C — Update `Opc.Ua.Client.ComplexTypes`
1. Add `<ProjectReference Include="..\Opc.Ua.ComplexTypes.Emit\Opc.Ua.ComplexTypes.Emit.csproj" />` ONLY if we want the existing DI default to keep working without forcing apps to add the Emit package. **Recommended: don't add it** — the existing `AddComplexTypes()` switches to Default, and apps that want Emit add the Emit package and call the new extension.
2. Update `ComplexTypeSystemFactory.Create(...)` to use `new DefaultComplexTypeFactory()` instead of `new ComplexTypeBuilderFactory()`.
3. Delete the existing `ComplexTypesExtensions.cs` (the `Create(session)` / `Create(resolver)` extension methods used Emit) — move to the Emit package as `ComplexTypesEmitExtensions.cs` with renamed methods (`CreateWithReflectionEmit`).
4. Update `using` statements where the file referenced moved types.

### Phase D — Update `Opc.Ua.Server.ComplexTypes`
1. No project-reference change required (it already defaults to Default).
2. Add a sibling `OpcUaComplexTypesServerBuilderEmitExtensions.cs` to **the Emit package** that re-registers `ServerComplexTypeSystemFactory` with an Emit factory descriptor.

### Phase E — DI extensions in the Emit package
1. New file `Libraries/Opc.Ua.ComplexTypes.Emit/Hosting/OpcUaComplexTypesEmitBuilderExtensions.cs`:
   - `public static IOpcUaBuilder AddComplexTypesWithReflectionEmit(this IOpcUaBuilder builder)` — calls `AddComplexTypes()` and then `builder.Services.Replace(ServiceDescriptor.Singleton<ComplexTypeSystemFactory>(sp => new ComplexTypeSystemFactory(sp.GetRequiredService<ITelemetryContext>(), useReflectionEmit: true)))`. (Either add an internal ctor overload to `ComplexTypeSystemFactory` or expose a `WithFactory(Func<IComplexTypeFactory>)` setter.)
   - Same for the server: `IOpcUaServerBuilder.AddComplexTypesWithReflectionEmit()`.
2. New file `Libraries/Opc.Ua.ComplexTypes.Emit/ComplexTypesEmitExtensions.cs`:
   - Moves the `Create(session, telemetry)` / `Create(resolver, telemetry)` extensions on `ComplexTypeSystem`, renamed to `CreateWithReflectionEmit` to match the DI naming.

### Phase F — Update consumers
1. **Apps and tests**: the only in-repo consumers that name the moved symbols today are:
   - `Tests/Opc.Ua.Client.ComplexTypes.Tests/*` — exercises Emit. Add `using Opc.Ua.ComplexTypes.Emit;` and add a `<ProjectReference Include="..\..\Libraries\Opc.Ua.ComplexTypes.Emit\Opc.Ua.ComplexTypes.Emit.csproj" />`.
   - `Tests/Opc.Ua.Aot.Tests/ComplexTypeAotTests.cs` — uses only Default; **no change**.
   - `Applications/ConsoleReferenceClient/{Program.cs, ClientSamples.cs}` — currently uses `new ComplexTypeBuilderFactory()` via `using Opc.Ua.ComplexTypes;`. Switch to `using Opc.Ua.ComplexTypes.Emit;` and add the Emit project reference (the sample wants Emit-generated types for live demo).
   - `Tests/Opc.Ua.Client.Tests/ComplexTypes/Default*Tests.cs` — uses only Default; **no change**.
2. Run a repo-wide grep for `ComplexTypeBuilderFactory|BaseComplexType|OptionalFieldsComplexType|UnionComplexType|StructureDefinitionAttribute|StructureFieldAttribute|StructureTypeIdAttribute|ComplexTypePropertyInfo|IComplexTypeProperties|AssemblyModule|AttributeExtensions` to confirm.

### Phase G — Build + test gate
1. `dotnet build UA.slnx` clean (TreatWarningsAsErrors).
2. Run:
   - `dotnet test Tests/Opc.Ua.Client.ComplexTypes.Tests` (Emit path, 3396 tests).
   - `dotnet test Tests/Opc.Ua.Client.Tests --filter "FullyQualifiedName~ComplexTypes"` (default path, 625 tests).
   - The AOT tests (`Tests/Opc.Ua.Aot.Tests`) build under the .NET 10 test platform — confirm they don't pull in the Emit package transitively. A clean AOT-test artifact graph is the proof that the split achieves its goal.

### Phase H — Documentation
1. Update `OpcComplexTypeSystemOverhaul.md` → note that the architecture is now four projects (`ComplexTypes`, `ComplexTypes.Emit`, `Client.ComplexTypes`, `Server.ComplexTypes`).
2. Append a new section to `BreakingChanges.md` covering this second wave (see §6).
3. `Docs/ComplexTypes.md` — explain when to use which package (decision flow: AOT? → core only. Need runtime concrete classes? → add Emit. Default DI flow now uses non-Emit; opt in via `AddComplexTypesWithReflectionEmit()`).
4. `Docs/MigrationGuide.md` — append the `using Opc.Ua.ComplexTypes.Emit;` requirement and DI rename.

---

## 5. Decision points worth confirming before implementation

| Question | Recommendation |
| --- | --- |
| **Package name** — `.Emit` vs `.Reflection.Emit` vs `.Dynamic`. | **`.Emit`** — short, matches `System.Reflection.Emit` mental model, avoids the word "Legacy". |
| **Default DI behaviour** — should `AddComplexTypes()` keep its meaning (Emit, today) or flip to Default? | **Flip to Default.** AOT must be the default in a 1.6 ecosystem; users that need Emit explicitly opt in. This IS a behavioural breaking change — call it out clearly in `BreakingChanges.md`. |
| **Keep `BaseComplexType` etc. in core** for forward-compat reasons? | **No.** They're only useful in the Emit pipeline; keeping them in core forces AOT builds to ship type-metadata they never use. Move them. |
| **Attributes** (`StructureDefinitionAttribute`, etc.) — keep in core or move to Emit? | **Move to Emit.** Their only consumers are the Emit builders, `BaseComplexType` reflection, and Emit tests. The source generator does not use them. |
| **Single combined namespace** (`Opc.Ua.ComplexTypes`) preserved by `[Forward...]`? | **No.** Consistent with the previous overhaul's "no forwarders" decision. |

If any answer above flips during review, the plan adapts mechanically — e.g. keeping attributes in core would just remove one file move and leave the Emit package with a dependency-only relationship to those classes.

---

## 6. Additional breaking changes (to append to `BreakingChanges.md`)

When this plan ships, the `BreakingChanges.md` file gets a new section. Preview:

### Wave 2: AOT/Emit split

**Namespace moves** — from `Opc.Ua.ComplexTypes` to **`Opc.Ua.ComplexTypes.Emit`**:
- `BaseComplexType`, `OptionalFieldsComplexType`, `UnionComplexType`
- `ComplexTypePropertyInfo`, `IComplexTypeProperties`
- `StructureDefinitionAttribute`, `StructureFieldAttribute`, `StructureTypeIdAttribute`, `StructureBaseDataType`
- `ComplexTypeBuilder`, `ComplexTypeBuilderFactory`, `ComplexTypeFieldBuilder`
- `AssemblyModule`, `AttributeExtensions`

**Assembly redistribution** — these types now ship in `Opc.Ua.ComplexTypes.Emit.dll` instead of `Opc.Ua.ComplexTypes.dll`. Reflection by assembly-qualified name breaks; recompile.

**New required NuGet package** for consumers that want Emit-generated runtime types: `OPCFoundation.NetStandard.Opc.Ua.ComplexTypes.Emit`. It is **not** pulled in transitively by `Opc.Ua.Client.ComplexTypes` — explicit opt-in.

**DI default flip**:
- `IOpcUaBuilder.AddComplexTypes()` now registers a `ComplexTypeSystemFactory` bound to `DefaultComplexTypeFactory` (AOT-friendly). Previously it bound to `ComplexTypeBuilderFactory` (Emit).
- To keep the prior behaviour, add the new Emit package and call `IOpcUaBuilder.AddComplexTypesWithReflectionEmit()` instead.

**Static factory rename**:
- `ComplexTypeSystem.Create(ISession, ITelemetryContext)` (the Emit-producing extension) moves to `Opc.Ua.ComplexTypes.Emit` and is renamed to `ComplexTypeSystem.CreateWithReflectionEmit(ISession, ITelemetryContext)`.
- A new `ComplexTypeSystem.Create(ISession, ITelemetryContext)` extension shipping in `Opc.Ua.Client.ComplexTypes` returns a Default-backed instance.

**`InternalsVisibleTo` re-shuffle** — same pattern as the first overhaul; details land with the implementation.

---

## 7. Out of scope

- Splitting `Opc.Ua.Client.ComplexTypes` further (e.g. an `Opc.Ua.Client.ComplexTypes.Emit`). The orchestration logic in `ComplexTypeSystem` works with any `IComplexTypeFactory`; no per-path duplication needed.
- Source-generator changes. The generator's output is independent of this split.
- Performance work, encoder changes, or any non-packaging refactor.
- Backwards-compatibility shims.
