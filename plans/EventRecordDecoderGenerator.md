# Plan v2: Source-generated event-record decoders (combined emission)

## Goal

Implement the "future work" tracked in `AlarmEventDecoder.cs:57-66`
by extending the **existing** `EventRecordGenerator` to also emit
decoders into the same output file, with a per-model **extension-
method-based** registration into an extensible decoder registry.

No new generator, no self-registration via `[ModuleInitializer]`.

## Decision 1 — Where does the decoder live?

Three locations were considered:

### A. Methods on the record itself

```csharp
public partial record ConditionTypeRecord
{
    public static QualifiedName[][] StandardFields => …;
    public static ConditionTypeRecord? Decode(IReadOnlyList<Variant> fields);
}
```

**Pros**: single discoverable type; natural fit since the generator
already emits the record as `partial`.
**Cons**: mixes data and behavior on a DTO type; static methods
can't be virtual so user-side overrides are awkward; generic
dispatch needs an inline wrapper to convert `static T Decode(...)`
into a `Func<IReadOnlyList<Variant>, EventRecord>` delegate.

### B. Sibling static class

```csharp
public static class ConditionTypeRecordDecoder
{
    public static QualifiedName[][] StandardFields => …;
    public static ConditionTypeRecord? Decode(IReadOnlyList<Variant> fields);
}
```

**Pros**: separation of concerns; conventional BCL pattern
(`Tuple` ↔ `TupleExtensions`).
**Cons**: two types per event type clutters the namespace and
breaks browse-by-prefix discoverability; an "Extensions" suffix on
a class with non-extension methods reads oddly.

### C. Nested static `Decoder` class on the partial record  ← chosen

```csharp
public partial record ConditionTypeRecord
{
    public static class Decoder
    {
        public static QualifiedName[][] StandardFields => …;
        public static ConditionTypeRecord? Decode(IReadOnlyList<Variant> fields);
    }
}
```

**Pros**:
* Co-located with the record but cleanly separated (data vs
  decoder).
* Discoverable as `ConditionTypeRecord.Decoder.Decode(…)` — the
  record name is the natural starting point.
* No namespace pollution — one type per event in the namespace.
* Generated extension methods reference `T.Decoder` consistently.
* Works in `partial record` declarations on all TFMs.

**Cons**:
* Slightly more verbose at call sites (`X.Decoder.Decode` vs
  `XDecoder.Decode`).
* C# nested generic name resolution sometimes confuses
  IntelliSense (mitigated by the fact that the decoder is a plain
  static class).

**Decision: Option C** — nested `Decoder` static class on each
generated record. The `EventRecordGenerator` is extended to emit
this alongside the record's properties.

## Decision 2 — Registration strategy

Four options considered:

### A. Self-registration via `[ModuleInitializer]`

Generated decoder runs on assembly load, registering itself with a
static registry.

**Pros**: zero user-side wiring; vendor models work transparently.
**Cons**:
* `[ModuleInitializer]` requires net5+ — net472/net48 need a
  reflection-based fallback, doubling the code path.
* AOT-unfriendly — initializers complicate trimming analysis.
* Order-of-load issues — a vendor whose parent record lives in a
  not-yet-loaded assembly fails the lookup at registration time.
* Untestable in isolation — global mutable state.

### B. Reflection scan over loaded assemblies

`EventRecordDecoderRegistry.RegisterAllFromAssembly(Assembly)`
walks every type, finds nested `Decoder` classes, registers.

**Pros**: still single API call per assembly.
**Cons**: reflection at startup; AOT-unfriendly; opaque (the
registration is "magic"); registration cost scales with the
assembly's type count.

### C. Generated per-model extension method called at startup

```csharp
EventRecordDecoderRegistry.Default
    .RegisterOpcUaDecoders()
    .RegisterMyVendorDecoders();
```

**Pros**: explicit, ordered, AOT-friendly, multi-TFM, testable.
**Cons**: requires user code at application startup; consumer has
to remember to call the registration even when the model is only
used downstream in one subscription site.

### D. Inline / lazy fluent registration at the call site  ← chosen

Standard UA decoders are always in `Default`; vendor decoders
register inline at the subscription site, scoped to that
subscription's registry. No startup wiring required.

```csharp
// Tier 1 — Default case, no vendor models, no registration:
await foreach (var ev in streaming.SubscribeAlarmsAsync(notifierId, ct: ct))
{
    // …
}

// Tier 2a — Inline lambda — registers on a derived child registry
// that inherits Default's pre-registrations:
await foreach (var ev in streaming
    .SubscribeAlarmsAsync(notifierId, ct: ct)
    .WithDecoders(reg => reg.RegisterMyVendorDecoders()))
{
    if (ev is VibrationAlarmTypeRecord v) { … }
}

// Tier 2b — Generated per-model wrapper that bundles
// WithDecoders + RegisterMyVendorDecoders into a single call:
await foreach (var ev in streaming
    .SubscribeAlarmsAsync(notifierId, ct: ct)
    .AndIncludeMyVendor())
{
    if (ev is VibrationAlarmTypeRecord v) { … }
}

// Tier 3 — Application builds a registry once and reuses:
EventRecordDecoderRegistry app = EventRecordDecoderRegistry.Default
    .CreateChildScope()
    .RegisterMyVendorDecoders()
    .RegisterAnotherVendorDecoders();

await foreach (var ev in streaming
    .SubscribeAlarmsAsync(notifierId, registry: app, ct: ct))
{
    …
}
```

The generator emits **two** extensions per model into the same
`*.EventRecords.g.cs` file:

1. `Register{ModelPrefix}Decoders(this EventRecordDecoderRegistry)`
   — registers every decoder in the model on the supplied registry
   (idempotent via `TryRegister`). Used by Tier 2a and Tier 3.
2. `AndInclude{ModelPrefix}<TRecord>(this IAsyncEnumerable<TRecord>)`
   — wraps `WithDecoders(reg => reg.Register{ModelPrefix}Decoders())`
   into a single call so vendor consumers don't have to write a
   lambda. Used by Tier 2b.

Both extensions live in the `static class {ModelPrefix}EventRecordDecoders`
companion class emitted at the end of the file.

**Pros**:
* **Lazy by use** — vendor decoders cost nothing for subscriptions
  that don't care about them.
* **Co-located** — registration sits next to the subscription that
  needs it; reviewer sees the connection inline.
* **AOT-friendly + multi-TFM clean** — no module initializers, no
  reflection.
* **Testable** — each subscription's registry is isolated.
* **Default works out of the box** — the standard UA decoders are
  always there; consumers only ever register vendor models.
* **Tier 2b minimizes ceremony** — vendor users write
  `.AndIncludeMyVendor()`, no lambda, no `reg.` qualification.
* **Tier 3 still available** — build-once registries plug into
  any subscription via the `registry:` parameter.

**Cons**:
* **Generator emits two extensions per model** — small additional
  surface to maintain; same template module.
* **Repeated `AndInclude{Prefix}()` across many subscription sites**
  — if many subscriptions need the same vendor registry, the
  application should still use Tier 3.
* **`AndInclude{Prefix}` extension naming collision** — two
  vendor models with the same `ModelPrefix` would emit clashing
  extension methods. Mitigation: documented in the model's README;
  prefix collision in OPC UA is rare because the namespace URIs
  are globally unique.

**Decision: Option D** — inline fluent registration backed by:

1. Static `Default` singleton always pre-registered with the
   standard UA model.
2. Generated **two** per-model extensions:
   * `Register{Prefix}Decoders(this Registry)` — registry-level.
   * `AndInclude{Prefix}<T>(this IAsyncEnumerable<T>)` —
     stream-level wrapper that bundles
     `WithDecoders + Register{Prefix}Decoders`.
3. `WithDecoders(Action<Registry>)` extension on the subscription
   `IAsyncEnumerable<T>` as the underlying primitive both Tier 2a
   and Tier 2b use.
4. Optional `registry:` parameter on subscription methods for the
   "build once, reuse" pattern (Tier 3).

## Decision 3 — Registry shape

```csharp
namespace Opc.Ua
{
    /// <summary>
    /// Extensible registry for source-generated event-record
    /// decoders. The static <see cref="Default"/> singleton is
    /// pre-populated with the standard UA model's decoders.
    /// Vendor consumers either register inline at the subscription
    /// site via the <c>WithDecoders</c> extension, or build an
    /// application-scoped child registry via
    /// <see cref="CreateChildScope"/> and pass it explicitly.
    /// </summary>
    public sealed class EventRecordDecoderRegistry
    {
        /// <summary>
        /// Process-wide registry seeded with the standard UA
        /// model on first access (lazy + thread-safe).
        /// </summary>
        public static EventRecordDecoderRegistry Default { get; }

        /// <summary>
        /// Creates an isolated child registry that inherits every
        /// registration from this registry. Subsequent
        /// <see cref="Register"/> calls on the child do NOT mutate
        /// the parent.
        /// </summary>
        public EventRecordDecoderRegistry CreateChildScope();

        public EventRecordDecoderRegistry Register(
            NodeId eventTypeId,
            QualifiedName[][] standardFields,
            Func<IReadOnlyList<Variant>, EventRecord?> decode);

        /// <summary>
        /// Idempotent counterpart for the inline/fluent pattern —
        /// registration is a no-op if the eventTypeId is already
        /// registered. Returns true if the registration was added.
        /// </summary>
        public bool TryRegister(
            NodeId eventTypeId,
            QualifiedName[][] standardFields,
            Func<IReadOnlyList<Variant>, EventRecord?> decode);

        /// <summary>
        /// Decodes <paramref name="fields"/> by routing on the
        /// EventType field. Walks the OPC UA event-type hierarchy
        /// for the closest registered ancestor decoder if the
        /// exact type is not registered. Returns null when no
        /// ancestor is registered.
        /// </summary>
        public EventRecord? Decode(IReadOnlyList<Variant> fields);

        /// <summary>
        /// Composed filter superset across every registered
        /// decoder (own + inherited from parent scope). Rebuilt
        /// lazily after each Register call; thread-safe.
        /// </summary>
        public QualifiedName[][] StandardFields { get; }

        /// <summary>
        /// Allows callers to plug a custom super-type resolver
        /// (typically backed by ITypeTable for dynamic discovery).
        /// </summary>
        public Func<NodeId, NodeId?>? SuperTypeResolver { get; set; }
    }

    /// <summary>
    /// Fluent inline-registration extension on alarm/event async
    /// subscription streams.
    /// </summary>
    public static class EventRecordSubscriptionExtensions
    {
        /// <summary>
        /// Returns a new IAsyncEnumerable that decodes events
        /// through an augmented registry: a child scope of the
        /// stream's current registry (or Default) with
        /// <paramref name="configure"/> applied.
        /// </summary>
        public static IAsyncEnumerable<TRecord> WithDecoders<TRecord>(
            this IAsyncEnumerable<TRecord> stream,
            Action<EventRecordDecoderRegistry> configure)
            where TRecord : EventRecord;
    }
}
```

Generated extension methods (`RegisterOpcUaDecoders`,
`RegisterMyVendorDecoders`) all internally use `TryRegister` so
calling the same extension method twice (e.g. once at startup,
once inline at a subscription) is a no-op the second time.

The subscription extension methods (`SubscribeAlarmsAsync`,
`SubscribeConditionsAsync`, `SubscribeDialogsAsync`) gain an
optional `EventRecordDecoderRegistry? registry = null` parameter;
when null, they use `Default`.

## Generator changes

`EventRecordGenerator` (existing) extended — **not replaced** — to:

1. Continue emitting `partial record {Type}Record` with init-only
   properties as today.
2. Emit a nested `public static class Decoder` block inside each
   record with:
   * `public static readonly QualifiedName[][] StandardFields = […]`
     — own + inherited browse paths in stable positional order.
   * `public static {Type}Record? Decode(IReadOnlyList<Variant>
     fields)` — positional reads from `StandardFields` indices,
     populates every property the type and its ancestors declare.
3. Emit ONE per-file `static class {ModelPrefix}EventRecordDecoders`
   with a single `Register{ModelPrefix}Decoders(this
   EventRecordDecoderRegistry registry)` extension method
   that registers every emitted decoder.

All three blocks land in the same `{ModelPrefix}.EventRecords.g.cs`
file. No new generator file, no separate template module — the
existing `EventRecordTemplates.cs` grows two new template strings
(`DecoderClass`, `RegistrationExtension`) plus a corresponding
`Tokens.ListOfDecoders` slot in the file template.

### Field-list strategy

Each per-type `StandardFields` is the union of:

* The type's directly-declared fields (already computed by
  `CollectDeclaredFields`).
* All inherited fields from ancestors (already computed by
  `CollectInheritedFieldNames` — extend to also return the path
  metadata, not just the name).

Positional order: ancestor fields first (in ancestor-emission
order, root-to-leaf), then own fields in declaration order.

This means a vendor `VibrationAlarmType : AlarmConditionType` gets
a `StandardFields` containing every condition + alarm + vibration
field at predictable indices. Cross-model registration works
because each decoder's positional layout is self-contained.

### Polymorphic dispatch — the filter superset

When subscribing polymorphically (e.g. `OfType(ConditionType)`), the
filter needs ALL fields the registry knows about. `Registry.Decode`
routes by the `EventType` field; each decoder reads from its OWN
positional layout, so the registry must remember each registration's
`StandardFields`.

The composed `Registry.StandardFields` is a UNION (deduplicated by
browse-path equality) of every registered decoder's
`StandardFields`. Each decoder, when invoked, receives a SUBSET
view of the filter array — the registry slices/remaps incoming
fields by browse-path lookup back to the decoder's positional layout.

**Trade-off**: the slice/remap is per-event overhead (one
dictionary lookup per registered decoder's field). For high-rate
event streams this matters. Alternative discussed in "Open
questions" below.

## Files

### New (runtime)

* `Stack/Opc.Ua.Core/Stack/Client/EventRecordDecoderRegistry.cs`
  — registry implementation + `Default` singleton.
* `Stack/Opc.Ua.Core/Stack/Client/EventRecordFieldReaders.cs`
  (internal) — positional read helpers shared with generated
  decoders (`GetByteString`, `GetNodeId`, `GetNullableBool`,
  `GetDateTime` with `DateTimeUtc` ↔ `DateTime` conversion, …).
  Lifted from `AlarmEventDecoder` private helpers.

### Updated (generator)

* `Tools/Opc.Ua.SourceGeneration.Core/Generators/EventRecordGenerator.cs`
  — emit nested `Decoder` class + per-file registration extension.
* `Tools/Opc.Ua.SourceGeneration.Core/Generators/EventRecordTemplates.cs`
  — add `DecoderClass`, `DecoderField`, `RegistrationExtension`
  templates. Modify `RecordClass` template to embed the decoder
  block.
* `Tools/Opc.Ua.SourceGeneration.Core/GeneratorOptions.cs` — add
  `bool OmitEventRecordDecoders { get; set; }` (defaults to
  follow `OmitEventRecords`).

### Updated (consumers) — **shipped (deleted, not shimmed)**

* `Libraries/Opc.Ua.Client/Alarms/AlarmEventDecoder.cs` — **deleted**.
  The PR migrated `AlarmStreamExtensions` directly to
  `EventRecordDecoderRegistry.Default.Decode(...)`. No public
  consumer outside the alarms module shipped against the
  hand-rolled decoder, so no `[Obsolete]` shim is needed.
* `Libraries/Opc.Ua.Client/Alarms/AlarmEventFilterBuilder.cs` —
  **deleted**. Replaced by per-record source-generated
  `{Type}Record.EventFilters.Build(registry?)` factories emitted
  alongside the `Decoder` class (see Phase 2 below).
* `Libraries/Opc.Ua.Client/Alarms/AlarmStreamExtensions.cs` —
  rewired to use `EventRecordDecoderRegistry.Default.Decode` and
  the generated `EventFilters.Build` factories. Each
  `Subscribe*Async` gained an optional
  `EventRecordDecoderRegistry? registry = null` parameter for
  vendor extensibility.

### Per-record filter factory — **shipped**

The generator emits a second nested static class
`EventFilters` alongside `Decoder` on each `{Type}Record`. The
factory delegates to a new runtime helper
`Opc.Ua.EventFilterFactory.Create(NodeId eventTypeId,
EventRecordDecoderRegistry?)` that:
* builds `SelectClauses` from the supplied registry's composed
  `StandardFields` (defaults to `Default`);
* sets a single `OfType` where clause to `eventTypeId` (omitted
  when the id equals `BaseEventType`).

Standard examples available out of the box:
`AlarmConditionTypeRecord.EventFilters.Build()`,
`ConditionTypeRecord.EventFilters.Build()`,
`DialogConditionTypeRecord.EventFilters.Build()`,
`CertificateExpirationAlarmTypeRecord.EventFilters.Build()`, etc.
Vendor records automatically get their own factory.

### Tests — **shipped**

* `Tests/Opc.Ua.Core.Tests/Stack/Client/EventRecordDecoderRegistryTests.cs`
  exercises register / decode / dispatch / super-type fallback /
  composed StandardFields invariants.
* `Tests/Opc.Ua.Client.Tests/Alarms/AlarmStreamExtensionsTests.cs`
  asserts the filter shape (now `EventRecordDecoderRegistry.Default.StandardFields.Length`),
  yields-and-drops semantics, and dialog filtering. Test fixtures
  build their event payloads against the registry's composed
  positional layout via a small browse-name-keyed helper
  (`RegistryFieldBuilder`).
* `Tests/Opc.Ua.Client.Tests/Alarms/AlarmEventDecoderTests.cs` and
  `Tests/Opc.Ua.Client.Tests/Alarms/AlarmEventFilterBuilderTests.cs`
  — **deleted**. Scenarios are covered by the registry tests
  + the stream-side tests + the generator round-trip.

## Implementation order

1. **Rubber-duck Decisions 1-3** before any code (check for
   blind spots — especially the slice/remap performance
   assumption in the registry).
2. **Runtime registry** — `EventRecordDecoderRegistry`,
   `EventRecordFieldReaders`. Unit-tested with hand-built
   decoders that mimic what the generator will emit. Cover
   register-then-decode, super-type fallback, composed
   StandardFields rebuild semantics.
3. **Generator update** — extend `EventRecordGenerator` and
   templates. Smoke-test the generated `Opc.Ua.EventRecords.g.cs`
   to confirm the nested `Decoder` blocks and the
   `RegisterOpcUaDecoders` extension method are well-formed.
4. **Wire the standard registry** — point
   `EventRecordDecoderRegistry.Default` at the generated
   `RegisterOpcUaDecoders` (initialized lazily on first access).
5. **Shim `AlarmEventDecoder`** — delegate to
   `EventRecordDecoderRegistry.Default`. Run existing
   `AlarmEventDecoderTests` unchanged.
6. **`AlarmEventFilterBuilder`** — registry-backed
   `StandardFields`. Verify `AlarmEventFilterBuilderTests` still
   passes byte-equivalent select clauses.
7. **DI integration** — `AddAlarms()` pre-registers the standard
   model. Document the vendor pattern.
8. **Vendor generator test** — synthetic vendor model end-to-end:
   generate decoder, register via extension, dispatch real
   event, assert vendor record type returned with vendor fields
   populated.
9. **Docs** — `Docs/AlarmsAndConditions.md` (new section on the
   registry surface), `Docs/MigrationGuide.md` (note about
   `AlarmEventDecoder` shim and the migration to
   `EventRecordDecoderRegistry.Default.Decode(...)`).
10. **Final rubber-duck + commit** — single commit on `fullae`
    (no push).

## Open questions

1. **Slice/remap perf vs single shared layout.** The registry's
   per-decoder field layouts mean a `Decode()` call must remap
   incoming positions to the decoder's own layout. An alternative
   is to require ONE global layout — every registered decoder
   shares the registry's composed indices, and decoders read from
   the global positions. Simpler/faster but breaks isolation
   (vendor decoders need to know their offsets inside the global
   layout, computed at registration time and patched into the
   decoder's reads). Decision: ship per-decoder layouts in v1;
   add the global-layout fast path if profiling shows it matters.
2. **Default singleton thread-safety.** Lazy initialization with
   double-checked locking around `Default`. Register calls take
   a lock; reads use volatile snapshot.
3. **Vendor `RegisterFooDecoders()` discoverability.** Without
   self-registration the consumer must know the extension method
   name. The generator emits a deterministic name
   (`Register{ModelPrefix}Decoders`); the model's README/migration
   note should document it. Could also emit a marker attribute on
   each generated extension class that the DI helper enumerates.
4. **Conflict detection.** Two registrations for the same
   `NodeId` event type? Throw (the second registration is almost
   certainly a bug). Same browse path appearing in two registered
   decoders' `StandardFields` with different data types? Throw at
   registration time with a clear error.
5. **Position-keyed vs path-keyed decoding** — see #1; v1 is
   path-keyed at the registry boundary, position-keyed inside
   each decoder.

## Risks

* **Generator complexity creep** — extending
  `EventRecordGenerator` means the existing record-emission tests
  may need updates if `RecordClass` template structure changes.
  Keep changes additive (new templates compose with the existing
  ones; don't refactor `RecordClass` itself).
* **Test fixture overhead** — round-trip equivalence tests
  require per-subtype field-value arrays. Reuse the existing
  `AlarmEventDecoderTests` fixtures to seed the generated-decoder
  tests.
* **DI ordering** — `AddAlarms()` must run after the registry
  singleton exists. The standard-UA registration is idempotent so
  duplicate calls are safe.

## Verification

```
dotnet build UA.slnx -c Debug --nologo
dotnet test Tests/Opc.Ua.SourceGeneration.Tests -c Debug --nologo
dotnet test Tests/Opc.Ua.Client.Tests --filter "Category=Alarms" -c Debug --nologo
dotnet test Tests/Opc.Ua.Core.Tests --filter "FullyQualifiedName~EventRecord" -c Debug --nologo
```

Acceptance: existing `AlarmEventDecoderTests`,
`AlarmEventFilterBuilderTests`, `AlarmStreamExtensionsTests` pass
unchanged through the shim; new registry + vendor tests prove
extensibility.
