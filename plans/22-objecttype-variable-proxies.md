# Plan: Read<Variable>Async / Write<Variable>Async on generated proxies

## Problem
The source-generated `*TypeClient` proxies currently expose only `MethodDesign`
children as async wrappers. Object types also declare `VariableDesign` and
`PropertyDesign` children (the typed variables/properties of an instance), but
the proxies provide no strongly-typed way to read or write them.

Goal: every declared `VariableDesign`/`PropertyDesign` child of an
`ObjectTypeDesign` should produce a pair of async wrappers on the generated
proxy class:

```csharp
public ValueTask<DataValue<T>> Read<Name>Async(CancellationToken ct = default);
public ValueTask Write<Name>Async(T value, CancellationToken ct = default);
```

`T` is the strongly-typed CLR type derived from `DataTypeNode + ValueRank`,
identical to the typing already produced for method arguments.

## Approach

### 1. Variable NodeId resolution
Variables on object instances do not have known NodeIds at design time (each
server allocates them per instance). Resolution uses
`TranslateBrowsePathsToNodeIds` against `ObjectId` with a `RelativePath` whose
single element is the variable's `QualifiedName`. The resolved `NodeId` is
cached per browse name on the proxy instance for the lifetime of the proxy.

### 2. ObjectTypeClient base helpers (Stack/Opc.Ua.Core)
Add a new `public readonly record struct DataValue<T>(T Value, StatusCode StatusCode, DateTime SourceTimestamp, ushort SourcePicoseconds, DateTime ServerTimestamp, ushort ServerPicoseconds)` next to `ObjectTypeClient` (namespace `Opc.Ua`), then add four `protected` async helpers to `ObjectTypeClient.cs`:

- `ValueTask<NodeId> ResolveChildNodeIdAsync(QualifiedName browseName, CancellationToken ct)`
  - Lazily translates a browse path of length 1 from `ObjectId`.
  - Caches results in a `ConcurrentDictionary<QualifiedName, NodeId>`.
  - Throws `ServiceResultException` on `BadNodeIdUnknown` / no targets.
- `ValueTask<DataValue<T>> ReadValueAsync<T>(QualifiedName browseName, CancellationToken ct)`
  - Resolves child, issues a `ReadAsync` for `Attributes.Value`,
    `TimestampsToReturn.Both`, `maxAge=0`.
  - Decodes `Variant.Value` to `T` (with array/scalar coercion
    mirroring method-output unpacking) and returns a strongly-typed
    `DataValue<T>` capturing value + StatusCode + SourceTimestamp +
    SourcePicoseconds + ServerTimestamp + ServerPicoseconds.
- `ValueTask WriteValueAsync<T>(QualifiedName browseName, T value, CancellationToken ct)`
  - Resolves child, issues a `WriteAsync` for `Attributes.Value`.
  - Boxes `value` into a `Variant` (reuses the same boxing logic the
    method generator already emits for inputs).
- A small private static helper to build the single-element `RelativePath`
  using `ReferenceTypeIds.HierarchicalReferences` (`includeSubtypes=true`).

### 3. Generator changes (`ObjectTypeProxyGenerator`)
- New `GetDeclaredVariables(ObjectTypeDesign)` mirroring
  `GetDeclaredMethods` — returns both `VariableDesign` and `PropertyDesign`
  children that are NOT placeholders (skip
  `MandatoryPlaceholder` / `OptionalPlaceholder`) and not excluded.
- New `CollectInheritedVariableNames(ObjectTypeDesign)` for `new` shadowing
  parity with methods.
- `WriteTemplate_ProxyClass` adds a second token `Tokens.VariableList`
  populated from `GetDeclaredVariables` rendered via `LoadTemplate_Variable`.
- `LoadTemplate_Variable` emits:
  - A `private static readonly QualifiedName s_<name>BrowseName = new(...);`
    field initialized from the variable's symbolic name + namespace index
    (resolved through `Session.MessageContext.NamespaceUris` lazily on
    first read or write — the field stores the unresolved symbolic
    namespace URI + name as a tuple, OR we use the same
    `ExpandedNodeId.ToNodeId` pattern already used for `MethodIds`).
  - `Read<Name>Async` calling `ReadValueAsync<T>(...)`.
  - Conditional `Write<Name>Async(T value, ...)` calling
    `WriteValueAsync<T>(...)` — only when AccessLevel permits write
    (`AccessLevel == ReadWrite | WriteOnly` or unspecified, i.e. default
    to emitting both unless explicitly read-only).
  - `new` modifier when the variable name shadows a parent variable.

### 4. Type resolution & nullability
- Reuse the existing `DataTypeDesign.GetMethodArgumentTypeAsCode(...)`
  extension to compute `T`. Same array-rank handling already proven for
  method arguments.

### 5. Templates
Add a new `Tokens.VariableList` token, plus `ObjectTypeProxyTemplates`
gains a new `Variable` template. The `ProxyClass` template grows one
extra token expansion site between the constructor and `Tokens.MethodList`.

### 6. Tests
- `ObjectTypeProxyGeneratorTests`: add a fixture with an `ObjectType`
  that declares mandatory + optional + read-only variables and a
  property, plus a derived type that shadows one of them. Assert:
  - Read/Write wrappers emitted with correct signature and `T`.
  - Read-only variable produces only `Read…Async`.
  - Shadowed names use `new`.
  - Inheritance: derived type does not re-emit parent's variables.
- `CompilerUtils.OpcUaCoreStubs`: extend the `ObjectTypeClient` stub
  with the new helper methods (matching signatures, default-returning
  bodies).
- Re-run all 3 SG test projects + the GDS proxy compile (already exercises
  generated FileType etc.).

### 7. Documentation
Update `Tools/Opc.Ua.SourceGeneration/readme.md` "ObjectType client
proxies" section to describe the new variable read/write wrappers,
caching semantics, and the access-level filter.

## Open questions / decisions to confirm with user
1. **Generate writes for read-only variables?** Default plan: skip
   `Write…Async` when `AccessLevel == ReadOnly`. Alternative: always emit
   and let server reject.
2. **Property vs Variable distinction:** Treat both the same and emit
   wrappers for both (recommended). Properties on object types are
   functionally the same as variables to a caller.
3. **DataValue (timestamps) accessor?** The plan emits `T`-returning
   reads. Should we also emit `Read<Name>DataValueAsync` returning the
   full `DataValue`? Default: no — keep API surface minimal; can be
   added later behind a flag.
4. **Caching:** Per-proxy-instance cache (recommended) vs. shared static
   cache. Per-instance avoids stale entries when the same browse path
   resolves to a different NodeId on different objects.

## Todos
1. `var-helpers-base` — Add `ResolveChildNodeIdAsync` /
   `ReadValueAsync<T>` / `WriteValueAsync<T>` to `ObjectTypeClient`
   (Stack/Opc.Ua.Core).
2. `var-helpers-stub` — Mirror new helpers in
   `Tests/Opc.Ua.SourceGeneration.Core.Tests/CompilerUtils.cs`
   `OpcUaCoreStubs`.
3. `var-generator-discover` — Implement
   `GetDeclaredVariables` + `CollectInheritedVariableNames` +
   placeholder filtering in `ObjectTypeProxyGenerator`.
4. `var-generator-template` — Add `Tokens.VariableList`, extend
   `ObjectTypeProxyTemplates.ProxyClass`, add `Variable` template.
5. `var-generator-emit` — Implement `LoadTemplate_Variable` (signature,
   AccessLevel filter, `new` modifier, browse-name field).
6. `var-tests` — Add `ObjectTypeProxyGeneratorTests` cases for
   variables (mandatory/optional/read-only/shadow/inheritance).
7. `var-run-sg-tests` — Run all 3 SG test projects, fix regressions.
8. `var-run-build` — `dotnet build UA.slnx` (Release) — fix any
   downstream build errors in `Opc.Ua.Gds.Client.Common` etc. caused by
   the new generated members.
9. `var-docs` — Update
   `Tools/Opc.Ua.SourceGeneration/readme.md`.
10. `var-commit` — Commit with co-author trailer.
