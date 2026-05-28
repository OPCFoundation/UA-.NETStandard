# Plan: Split ComplexTypes into Common / Client / Server (revised against actual repo state)

> **Status:** revised plan against `master @ f3928db0`. The original plan was
> written against an earlier shape of the repo; this version reflects what is
> actually on disk today and the user's chosen scope:
>
> - **Breaking change** — no `[TypeForwardedTo]` shims, no `Obsolete` wrappers.
> - **Minimal working** server impl (not just a skeleton).
> - Full build + ComplexTypes test runs are the acceptance gate.

---

## 1. Current state (as of f3928db0)

The original plan assumed every `IComplexType*` interface plus
`ComplexTypeSystem`, `NodeCacheResolver`, `DataDictionary`, and the exception
types lived in `Libraries/Opc.Ua.Client.ComplexTypes/`. **They don't.** The
current layout is:

### `Libraries/Opc.Ua.Client/ComplexTypes/` (inside the main client project)
- Interfaces — `IComplexTypeFactory.cs`, `IComplexTypeResolver.cs` (the file
  also defines `IComplexTypeBuilder`/`IComplexTypeFieldBuilder`)
- Default (AOT-friendly, non-Emit) implementations:
  - `DefaultComplexTypeFactory.cs`
  - `DefaultComplexTypeBuilder.cs`
  - `DefaultComplexTypeFieldBuilder.cs`
- Orchestration / client-only:
  - `ComplexTypeSystem.cs` — main loader, depends on `ISession`
  - `NodeCacheResolver.cs` — `IComplexTypeResolver` backed by a session's
    `NodeCache`
  - `DataDictionary.cs`, `DataTypeDefinitionExtension.cs`
- Exceptions — `DataTypeException.cs` (declares both
  `DataTypeNotFoundException` and `DataTypeNotSupportedException`)
- Namespace: **`Opc.Ua.Client.ComplexTypes`** (yes — even though the files
  live in `Opc.Ua.Client.csproj`).

### `Libraries/Opc.Ua.Client.ComplexTypes/` (the legacy add-on package)
- Reflection.Emit builders (NOT AOT-compatible):
  - `TypeBuilder/ComplexTypeBuilderFactory.cs` — `IComplexTypeFactory`
  - `TypeBuilder/ComplexTypeBuilder.cs` — `IComplexTypeBuilder`
  - `TypeBuilder/ComplexTypeFieldBuilder.cs` — `IComplexTypeFieldBuilder`
  - `TypeBuilder/AssemblyModule.cs`, `TypeBuilder/AttributeExtensions.cs`
- Runtime types that Emit-generated classes derive from:
  - `Types/BaseComplexType.cs`, `Types/OptionalFieldsComplexType.cs`,
    `Types/UnionComplexType.cs`
  - `Types/ComplexTypePropertyInfo.cs`, `Types/IComplexTypeProperties.cs`
  - `Types/StructureDefinitionAttribute.cs`,
    `Types/StructureFieldAttribute.cs`, `Types/StructureTypeAttribute.cs`
- DI / convenience surface:
  - `OpcUaComplexTypesBuilderExtensions.cs` — `IOpcUaBuilder.AddComplexTypes()`
  - `ComplexTypeSystemFactory.cs` — DI-resolvable factory that builds a
    `ComplexTypeSystem` per `ISession`
  - `ComplexTypesExtensions.cs` — C#-extension members on
    `ComplexTypeSystem` for `Create(session, telemetry)` /
    `Create(IComplexTypeResolver, telemetry)`
- `Properties/AssemblyInfo.cs` — `[assembly: CLSCompliant(false)]`
- Namespace: **`Opc.Ua.Client.ComplexTypes`** (same namespace as the files
  inside `Opc.Ua.Client.csproj` — fine because consumers see one namespace).

### Tests
- `Tests/Opc.Ua.Client.ComplexTypes.Tests/` — tests the Emit factory,
  type-system loading via `MockResolver`, encoder behaviour, and the
  `AddComplexTypes()` DI surface.
- `Tests/Opc.Ua.Client.Tests/ComplexTypes/` — tests the **default**
  (non-Emit) builder, plus `NodeCacheResolver`, `DataDictionary`,
  `ComplexTypeSystem` end-to-end with `Quickstarts.Servers`.

### Consumers using `Opc.Ua.Client.ComplexTypes` namespace today
13 in-repo files (apps, tests, docs):
`ConsoleReferenceClient/Program.cs`, `ConsoleReferenceClient/ClientSamples.cs`,
all `Tests/Opc.Ua.Client.Tests/ComplexTypes/*.cs`,
`Tests/Opc.Ua.Aot.Tests/ComplexTypeAotTests.cs`, plus
`Docs/ComplexTypes.md` and the legacy plan doc.

---

## 2. Target layout

```
Libraries/
  Opc.Ua.ComplexTypes/                  ← NEW common library
    Builders/
      IComplexTypeFactory.cs
      IComplexTypeResolver.cs           (and IComplexTypeBuilder / IComplexTypeFieldBuilder)
      DefaultComplexTypeFactory.cs
      DefaultComplexTypeBuilder.cs
      DefaultComplexTypeFieldBuilder.cs
      ComplexTypeBuilderFactory.cs      (Emit)
      ComplexTypeBuilder.cs             (Emit)
      ComplexTypeFieldBuilder.cs        (Emit)
      AssemblyModule.cs
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
    Schema/
      DataDictionary.cs
      DataTypeDefinitionExtension.cs
    Exceptions/
      DataTypeException.cs              (both exception classes)
    Properties/AssemblyInfo.cs          ([CLSCompliant(false)] moved here)
    Opc.Ua.ComplexTypes.csproj

  Opc.Ua.Client.ComplexTypes/           ← refactored
    ComplexTypeSystem.cs                (moved from Opc.Ua.Client)
    NodeCacheResolver.cs                (moved from Opc.Ua.Client)
    ComplexTypeSystemFactory.cs         (stays)
    ComplexTypesExtensions.cs           (stays — extension on ComplexTypeSystem)
    OpcUaComplexTypesBuilderExtensions.cs   (stays — AddComplexTypes())
    Opc.Ua.Client.ComplexTypes.csproj

  Opc.Ua.Server.ComplexTypes/           ← NEW
    AddressSpaceComplexTypeResolver.cs  (IComplexTypeResolver over IServerInternal/INodeManager)
    ServerComplexTypeSystem.cs          (server-side loader; analogous to client ComplexTypeSystem)
    ServerComplexTypeSystemFactory.cs   (DI factory bound to telemetry)
    OpcUaComplexTypesServerBuilderExtensions.cs   (IOpcUaServerBuilder.AddComplexTypes())
    Opc.Ua.Server.ComplexTypes.csproj
```

### Namespaces (breaking)

| Old (Opc.Ua.Client.ComplexTypes) | New |
| --- | --- |
| `IComplexTypeFactory`, `IComplexTypeBuilder`, `IComplexTypeFieldBuilder`, `IComplexTypeResolver` | **`Opc.Ua.ComplexTypes`** |
| `DefaultComplexType*` | **`Opc.Ua.ComplexTypes`** |
| `ComplexTypeBuilder*`, `ComplexTypeFieldBuilder`, `AssemblyModule`, `AttributeExtensions` | **`Opc.Ua.ComplexTypes`** |
| `BaseComplexType`, `OptionalFieldsComplexType`, `UnionComplexType`, `ComplexTypePropertyInfo`, `IComplexTypeProperties` | **`Opc.Ua.ComplexTypes`** |
| `StructureDefinitionAttribute`, `StructureFieldAttribute`, `StructureTypeIdAttribute`, `StructureBaseDataType` | **`Opc.Ua.ComplexTypes`** |
| `DataDictionary`, `DataTypeDefinitionExtension` | **`Opc.Ua.ComplexTypes`** |
| `DataTypeNotFoundException`, `DataTypeNotSupportedException` | **`Opc.Ua.ComplexTypes`** |
| `ComplexTypeSystem`, `NodeCacheResolver` | **`Opc.Ua.Client.ComplexTypes`** (unchanged) |
| `ComplexTypeSystemFactory`, `ComplexTypesExtensions` | **`Opc.Ua.Client.ComplexTypes`** (unchanged) |
| `OpcUaComplexTypesBuilderExtensions` | **`Microsoft.Extensions.DependencyInjection`** (unchanged) |

### Project dependency graph (new)

```
Opc.Ua.Core
   ▲
   ├── Opc.Ua.ComplexTypes              (new)
   │      ▲
   │      ├── Opc.Ua.Client      (references ComplexTypes for interfaces/types)
   │      │      ▲
   │      │      └── Opc.Ua.Client.ComplexTypes  (references both)
   │      │
   │      └── Opc.Ua.Server      (no change required unless server uses types directly;
   │                              the new Opc.Ua.Server.ComplexTypes pulls these in)
   │
   └── Opc.Ua.Server.ComplexTypes (new) ──► Opc.Ua.Server, Opc.Ua.ComplexTypes
```

Note: `Opc.Ua.Client` will now reference `Opc.Ua.ComplexTypes` because its
`SessionConfiguration` / orchestration types use the interfaces. No
`Opc.Ua.Server` change is strictly required by the split — the new
`Opc.Ua.Server.ComplexTypes` is opt-in.

---

## 3. Implementation phases

### Phase A — Common project scaffold
1. Create `Libraries/Opc.Ua.ComplexTypes/Opc.Ua.ComplexTypes.csproj` with the
   same conventions used by `Opc.Ua.Client.ComplexTypes.csproj`:
   - `AssemblyName = $(AssemblyPrefix).ComplexTypes`
   - `PackageId = $(PackagePrefix).Opc.Ua.ComplexTypes`
   - `RootNamespace = Opc.Ua.ComplexTypes`
   - `TargetFrameworks = $(LibxTargetFrameworks)`
   - `IsPackable = true`, `GenerateDocumentationFile = true`,
     `Nullable = enable`, AOT-compatible on net10
   - `InternalsVisibleTo` Client/Server addon test projects + main client
2. Single `ProjectReference` to `Stack/Opc.Ua.Core/Opc.Ua.Core.csproj`.
3. `Properties/AssemblyInfo.cs` carries `[assembly: CLSCompliant(false)]`.
4. Add the project to `UA.slnx` under `/Libraries/`.

### Phase B — Move shared files into Common
1. Physically move (not copy) files into `Libraries/Opc.Ua.ComplexTypes/`:
   - From `Libraries/Opc.Ua.Client/ComplexTypes/`:
     `IComplexTypeFactory.cs`, `IComplexTypeResolver.cs`,
     `DefaultComplexTypeFactory.cs`, `DefaultComplexTypeBuilder.cs`,
     `DefaultComplexTypeFieldBuilder.cs`, `DataDictionary.cs`,
     `DataTypeDefinitionExtension.cs`, `DataTypeException.cs`
   - From `Libraries/Opc.Ua.Client.ComplexTypes/`:
     entire `TypeBuilder/` directory, entire `Types/` directory,
     `Properties/AssemblyInfo.cs`
2. Update namespace on every moved file:
   `Opc.Ua.Client.ComplexTypes` → `Opc.Ua.ComplexTypes`.
3. Inside the moved files, drop any explicit
   `using Opc.Ua.Client.ComplexTypes;` (now self-referencing); add
   `using Opc.Ua.ComplexTypes;` where consumers from outside the file
   reference its sibling types.
4. Keep `ComplexTypesModule` constant in `AssemblyModule.cs` as
   `Opc.Ua.ComplexTypes.Module` — already matches the new namespace.

### Phase C — Refactor `Opc.Ua.Client` (main library)
1. Move `ComplexTypeSystem.cs` and `NodeCacheResolver.cs` out of
   `Libraries/Opc.Ua.Client/ComplexTypes/` into
   `Libraries/Opc.Ua.Client.ComplexTypes/`. The empty `ComplexTypes/`
   directory is removed.
2. `Opc.Ua.Client.csproj`: add
   `<ProjectReference Include="..\Opc.Ua.ComplexTypes\Opc.Ua.ComplexTypes.csproj" />`.
   This is required because `SessionConfiguration` and other client
   internals still reference `IComplexTypeFactory` etc. Drop the
   `InternalsVisibleTo` for `Opc.Ua.Client.ComplexTypes.Tests` only if the
   tests no longer touch client internals (verify; likely keep).
3. Update `using` statements in any remaining `Opc.Ua.Client` source that
   referenced the moved types.

### Phase D — Refactor `Opc.Ua.Client.ComplexTypes`
1. `Opc.Ua.Client.ComplexTypes.csproj`:
   - Drop the moved files (Types/, TypeBuilder/, Properties/).
   - Add `<ProjectReference Include="..\Opc.Ua.ComplexTypes\Opc.Ua.ComplexTypes.csproj" />`.
2. In the remaining files (`ComplexTypeSystem.cs`, `NodeCacheResolver.cs`,
   `ComplexTypeSystemFactory.cs`, `ComplexTypesExtensions.cs`,
   `OpcUaComplexTypesBuilderExtensions.cs`):
   - Keep namespace `Opc.Ua.Client.ComplexTypes`.
   - Add `using Opc.Ua.ComplexTypes;` where needed.
   - `ComplexTypesExtensions` still extends `ComplexTypeSystem` — no
     namespace move required.
   - `OpcUaComplexTypesBuilderExtensions` keeps its
     `Microsoft.Extensions.DependencyInjection` namespace.

### Phase E — Create `Opc.Ua.Server.ComplexTypes` (minimal working impl)
The goal is enough functionality that a hosted server can advertise complex
custom DataTypes whose definitions live in the server's address space (e.g.
loaded from a NodeSet2) and round-trip those types on the wire.

1. `Libraries/Opc.Ua.Server.ComplexTypes/Opc.Ua.Server.ComplexTypes.csproj`:
   - Same conventions as sibling projects;
     `AssemblyName = $(AssemblyPrefix).Server.ComplexTypes`,
     `PackageId = $(PackagePrefix).Opc.Ua.Server.ComplexTypes`,
     `RootNamespace = Opc.Ua.Server.ComplexTypes`,
     `TargetFrameworks = $(LibxTargetFrameworks)`.
   - References `Opc.Ua.Core`, `Opc.Ua.Server`, `Opc.Ua.ComplexTypes`.
2. **`AddressSpaceComplexTypeResolver`** — implements `IComplexTypeResolver`
   against an `IServerInternal` (or, more narrowly, against
   `NodeManagerTable` + `MessageContext`). It walks the DataType subtype tree
   in the address space, materialises `DataTypeNode`s from local nodes, and
   exposes the same `LoadDataTypesAsync` / `FindAsync` / `FindSuperTypeAsync`
   surface the client resolver provides. Browse helpers required by the
   resolver interface are short-circuited because the server knows its own
   encoding ids directly.
3. **`ServerComplexTypeSystem`** — analog of the client
   `ComplexTypeSystem` that consumes an `IComplexTypeResolver` (typically the
   `AddressSpaceComplexTypeResolver`) and an `IComplexTypeFactory` (default
   non-Emit or the Emit factory) and produces runtime types registered with
   `IServerInternal.MessageContext.Factory`. Reuses the same per-namespace
   walk the client uses; the wiring is much simpler because the resolver is
   in-process.
4. **`ServerComplexTypeSystemFactory`** + **`OpcUaComplexTypesServerBuilderExtensions`**
   — DI surface analogous to the client side:
   `IOpcUaServerBuilder.AddComplexTypes()` registers the factory as a
   singleton against the configured server host so server code resolves a
   `ServerComplexTypeSystem` bound to the live address space and the host's
   `ITelemetryContext`.

### Phase F — Update solution + tests
1. `UA.slnx` — add the two new projects under `/Libraries/`.
2. `Tests/Opc.Ua.Client.ComplexTypes.Tests/Opc.Ua.Client.ComplexTypes.Tests.csproj`:
   - Add ProjectReference to `Opc.Ua.ComplexTypes` (transitive, but explicit
     is clearer for the tests).
3. `Tests/Opc.Ua.Client.Tests/Opc.Ua.Client.Tests.csproj`: add ProjectReference
   to `Opc.Ua.ComplexTypes` (the `ComplexTypes/` test sub-folder uses these).
4. Update `using` statements across all 13 consumer files:
   - `using Opc.Ua.Client.ComplexTypes;` may need to become **both**
     `using Opc.Ua.Client.ComplexTypes; using Opc.Ua.ComplexTypes;`
     depending on which symbols the file touches. Triage per file.
5. **New tests** (minimum):
   - One smoke test in `Tests/Opc.Ua.Client.ComplexTypes.Tests` or a new
     `Tests/Opc.Ua.ComplexTypes.Tests` exercising the moved
     `ComplexTypeBuilderFactory` and `DefaultComplexTypeFactory` directly —
     proving the Common surface compiles and runs without referencing
     `Opc.Ua.Client.*`.
   - One server smoke test (new `Tests/Opc.Ua.Server.ComplexTypes.Tests`,
     or hosted inside `Tests/Opc.Ua.Server.Tests`) that boots a server with a
     small custom DataType in a NodeSet2 and confirms
     `ServerComplexTypeSystem.LoadAsync` registers the type on the
     `MessageContext.Factory`.

### Phase G — Documentation
1. `Docs/ComplexTypes.md` — update to mention the three-package structure
   and the new `Opc.Ua.ComplexTypes` namespace. Show migration snippets.
2. `Docs/MigrationGuide.md` — append a "1.5 → 1.6" section calling out the
   namespace move.
3. NuGet README for the new packages (optional for the first PR — the
   `Docs/NugetREADME.md` is shared today; leave as-is for v1).

### Phase H — Verification gate (acceptance)
1. `dotnet build UA.slnx` clean (TreatWarningsAsErrors is on).
2. Run:
   - `dotnet test Tests/Opc.Ua.Client.ComplexTypes.Tests/...`
   - `dotnet test Tests/Opc.Ua.Client.Tests/... --filter "FullyQualifiedName~ComplexTypes"`
   - `dotnet test Tests/Opc.Ua.Aot.Tests/... --filter "FullyQualifiedName~ComplexType"`
   - Plus the new server smoke test if created.

---

## 4. Risk register (revised)

| Risk | Mitigation |
| --- | --- |
| Hidden compile-time fan-out from the breaking namespace change (the 13 consumer files plus indirect ones). | Treat first build pass as a discovery tool; iterate using-statements until clean. |
| `Opc.Ua.Client` now transitively depending on `Opc.Ua.ComplexTypes` increases its closure. | Acceptable — the new lib has only `Opc.Ua.Core` as a dependency. |
| `InternalsVisibleTo` chain shifts (e.g. tests that poked at internal members of `Opc.Ua.Client.ComplexTypes` now need access to internals of `Opc.Ua.ComplexTypes`). | The new csproj declares `InternalsVisibleTo` for all three test assemblies; verify each `internal` usage. |
| Server impl mis-models address-space traversal (the server's NodeId scheme for DataType encodings differs from a remote client's). | Start the server resolver by reusing the same browse semantics the client uses against the in-proc node managers; if that produces correct binary/xml encoding ids, ship it. |
| Source generation (`Opc.Ua.SourceGeneration`) emits code referencing the old namespace. | Search the generation outputs at first build; either retarget the generator or add `using` aliases at consumption sites. |

---

## 5. Out of scope for this overhaul

- Type forwarders / obsolete shims (user opted for clean break).
- New server features beyond loading complex types into the encodeable
  factory (no dictionary generation, no dynamic type creation API).
- Re-organising tests beyond what the move strictly requires (e.g. a
  dedicated `Opc.Ua.ComplexTypes.Tests` is optional; tests for the moved
  types can live in the client test assembly for now).
- NuGet metadata polish beyond what the new csproj inherits from
  `common.props`.
