# WoT / NodeSet conversion

`Opc.Ua.Wot.WotNodeSetConverter` converts OPC UA NodeSet2 documents to
WoT Thing Models / Thing Descriptions and materializes WoT documents back
to NodeSet2. The converter preserves a byte-exact `uav:nodeSet` envelope
when requested, uses the structured `uav:nodes` projection when the
readable vocabulary is not complete, and otherwise synthesizes NodeSet2
from the readable WoT terms.

## WoT to NodeSet defaults

The table below lists the defaults the current WoT-to-NodeSet
materializer applies when the WoT input lacks information needed for a
NodeSet2 projection. Rows marked **Fails** emit an error diagnostic and
`ToNodeSet` throws. Rows marked **Default** substitute the listed value;
warnings or informational diagnostics are noted where the code emits
them.

| Missing WoT input | Behaviour | Materialized value |
|---|---|---|
| Convertible content: no `uav:nodeSet`, no `uav:nodes`, and neither a Thing Model nor a Thing Description kind | **Fails** | `NoConvertibleContent` error; no NodeSet is returned. |
| Model namespace: root `uav:id` has no `nsu=<NamespaceUri>;...` part and document `id` is absent | **Default** | `NamespaceUris = ["urn:opcua:wot:synthesized"]` and `Models[0].ModelUri = "urn:opcua:wot:synthesized"`. This is intentionally synthetic and should be replaced by an authored namespace in portable documents. |
| Root local name: root `uav:browseName` is absent | **Default** | Sanitized `title` (letters, digits, `_`, `-`) when non-empty; otherwise `Thing`. |
| Root `uav:id` | **Default** | Deterministic NodeId `ns=1;s=<rootLocal>` and an informational `GeneratedNodeId` diagnostic. |
| Root `uav:browseName` | **Default** | `1:<rootLocal>`. |
| Root `title` | **Default** | Root `DisplayName` becomes `<rootLocal>`. |
| Thing Model root event annotation `uav:eventType` | **Default** | Root is a non-abstract `UAObjectType` with inverse `HasSubtype` to `BaseObjectType` (`i=58`). If the event annotation is present, the default supertype is `BaseEventType` (`i=2041`). |
| Thing Description root type information | **Default** / **Bound** / **Fails** | Absent: root is a `UAObject` with `HasTypeDefinition` to `BaseObjectType` (`i=58`). Present: a `ua:HasTypeDefinition` link (WoT Binding Section 5.2.1) whose `href` is the ExpandedNodeId of the type, and/or a compact model name in `@type`, binds the root to that type so the converter reuses the existing type rather than defining a second one. The two forms are resolved against the Section 5.1.5 local context — the sibling documents of the conversion first, a loaded AddressSpace as the fallback — supplied through `IWotNodeResolver`. A binding that names a type the local context does not hold **fails** (`UnresolvedTypeBinding`) rather than falling back to `BaseObjectType`; two bindings that disagree, an ambiguous name with nothing to settle it, or a resolved type of the wrong NodeClass **fail** as `AmbiguousTypeBinding`. A compact name is a binding when its namespace is one the local context holds; any other `@type` member is ordinary annotation and is retained as residue. |
| Root `description` | **Default** | No `Description` field is materialized. |
| Property affordance `uav:browseName` | **Default** | The affordance map key is used as the local name and BrowseName `1:<key>`. |
| Property affordance `uav:id` | **Default** | Deterministic NodeId `ns=1;s=<rootLocal>/<propertyLocal>`. |
| Property DataSchema `type` or an unrecognized `type` | **Default** | The canonical table of WoT Binding §6.11.4: `boolean` → `Boolean`, `integer` → the **abstract** `Integer` (`i=27`), `number` → the **abstract** `Number` (`i=26`), `string` → `String`, refined by `contentEncoding: base64` → `ByteString`, `format: date-time` → `DateTime`, `format: uuid` → `Guid`, `format: uri` → `UriString`. An explicit `uav:dataTypeId` or `uav:mapToType` outranks the inference. Anything unrecognized falls back to `BaseDataType` (`i=24`). A bare `integer` or `number` is deliberately abstract: the schema states only that the value is whole or numeric, and a concrete width is recovered from an annotation rather than guessed. |
| Property `readOnly` and `writeOnly` | **Default** | Missing flags mean read/write access (`CurrentRead | CurrentWrite`, value `3`). If both flags are `true`, the zero-access result is coerced to `CurrentRead` (`1`); this is an arbitrary safety default and should be specified explicitly. |
| Property `title` | **Default** | No `DisplayName` field is materialized for the variable. |
| Property `description` | **Default** | No `Description` field is materialized for the variable. |
| Property type definition | **Default** | `HasTypeDefinition` to `BaseDataVariableType` (`i=63`). |
| Action affordance `uav:browseName` | **Default** | The affordance map key is used as the local name and BrowseName `1:<key>`. |
| Action affordance `uav:id` | **Default** | Deterministic NodeId `ns=1;s=<rootLocal>/<actionLocal>`. |
| Action `title` | **Default** | No `DisplayName` field is materialized for the method. |
| Event affordance `uav:browseName` | **Default** | The affordance map key is used as the local name and BrowseName `1:<key>`. |
| Event affordance `uav:id` | **Default** | Deterministic NodeId `ns=1;s=<rootLocal>/<eventLocal>`. |
| Event `title` | **Default** | No `DisplayName` field is materialized for the event type. |
| Event type abstraction/supertype | **Default** | Event affordances materialize as non-abstract `UAObjectType` nodes with inverse `HasSubtype` to `BaseEventType` (`i=2041`) and a root `GeneratesEvent` reference, unless the affordance carries `uav:conditionType` or `uav:conditionTypeId`. A Condition event derives from the named ConditionType instead (WoT Binding Section 13.2). |
| `uav:modellingRule` on a property or action | **Default** | No `HasModellingRule` reference is materialized. |
| `uav:hasComponent` / `uav:componentOf` entry has no matching typed ReferenceType link | **Default** | `HasComponent` is used for the component reference. |
| Binding link has no resolvable model-name relation and no `uav:refId` | **Default** | The link maps to `Organizes`. Spec PR #19 removed `uav:componentModel`, `uav:capability` and `uav:reference`; a link now names its ReferenceType directly in `rel` (`ua:HasComponent`, `ua:HasInterface`, `ua:NonHierarchicalReferences`). |
| Reference link points to another Thing by URI and no resolver is supplied or the resolver cannot find `uav:id` | **Default** | The reference is omitted and a warning diagnostic is emitted; no placeholder NodeId is generated. |
| Invalid namespace-qualified `uav:id` / `uav:browseName` syntax or unbound compact-name prefix | **Fails** | An error diagnostic is emitted; `ToNodeSet` throws even though synthesis continues far enough to collect diagnostics. |
| Event affordance says `@type: uav:eventType` and `uav:isEvent: false` | **Fails** | `EventAnnotationConflict` error; the two terms must not contradict each other. |
| `uav:isComposite` (Section 6.1) | **Default** / **Fails** | Absent: the type is treated as atomic and no `HasComponent` walk is forced. Malformed (non-boolean): `InvalidModelVocabularyValue` error and `ToNodeSet` throws. Present and valid: the flag has no distinct readable NodeSet structure, so it is carried verbatim through the `uav:nodes` residue and restored on the reverse conversion. |
| `uav:contains` (Section 6.3) | **Default** / **Fails** | Absent: sub-components come from links only. Malformed (not an array, or an entry that does not match a link `uav:refName` declared on the same type): `InvalidContainment` error. Present and valid: preserved via residue. |
| `uav:containedIn` (Section 6.3) | **Default** / **Fails** | Absent: no parent is recorded. Malformed (not a non-empty string, or naming the type itself, which is a cycle): `InvalidContainment` error. The reciprocal "the named composite exists" check is cross-document and out of scope for the single-document converter, which validates range and self-cycle only. Present and valid: preserved via residue. |
| `uav:unitProperty` (Section 6.5) | **Default** / **Fails** | Absent: the value is treated as already in engineering units and no `EngineeringUnits` pointer is recorded. Malformed (not a non-empty RFC 6901 JSON Pointer resolving, within the same document, to a string property): `InvalidUnitPointer` error. Present and valid: preserved via residue. |
| `uav:scaleFactor` (Section 6.5) | **Default** / **Fails** | Absent: identity scaling (factor `1`). Malformed (not a non-zero number): `InvalidModelVocabularyValue` error. Present and valid: preserved via residue. |
| `uav:decimalPlaces` (Section 6.5) | **Default** / **Fails** | Absent: no rounding is recorded. Malformed (not an integer greater than or equal to zero; `2.0` is rejected as a non-integer literal): `InvalidModelVocabularyValue` error. Present and valid: preserved via residue. |
| `uav:semanticId` (Section 6.7) | **Default** / **Fails** | Absent: no semantic reference is recorded. Malformed (not an absolute IRI with a scheme): `NonAbsoluteIri` error. Present and valid: preserved via residue. |
| `uav:metadata` (Section 6.7) | **Default** | Absent: nothing is recorded. Present: opaque; carried verbatim through residue, never validated and never a reason to reject the document (Section 6.7). |
| `uav:propertyConfiguration` (Section 6.7) | **Default** | Absent: nothing is recorded. Present: opaque per-affordance configuration; carried verbatim through residue and never validated. |
| `uav:actionConfiguration` (Section 6.7) | **Default** | Absent: nothing is recorded. Present: opaque per-affordance configuration; carried verbatim through residue and never validated. |
| `uav:eventConfiguration` (Section 6.7) | **Default** | Absent: nothing is recorded. Present: opaque per-affordance configuration; carried verbatim through residue and never validated. |
| `uav:includeInherited` (Section 6.8) | **Default** / **Fails** | Absent: no inheritance-span flag is recorded. Malformed (non-boolean): `InvalidModelVocabularyValue` error. Present and valid: preserved via residue. |
| `uav:additionalProperties` (Section 6.8) | **Default** / **Fails** | Absent: no open-content flag is recorded. Malformed (non-boolean): `InvalidModelVocabularyValue` error. Present and valid: preserved via residue. |
| `uav:browsePathAnchor` (Section 5.1.4) | **Default** / **Fails** | Absent: a relative `uav:browsePath` resolves against the nearest enclosing `uav:id`. Malformed (not an ExpandedNodeId): `ValidationError` error; the session-local `ns=<index>` form is reported `NonPortableIdentity` (an error unless `AllowNonPortableIdentifiers` is set). Present and valid: preserved via residue. |

## Consumer-visible compatibility notes

Two WoT-to-NodeSet behaviours are intentionally silent because they follow the
current WoT Binding vocabulary, but both can change what a consumer observes in
the materialized NodeSet.

### Pre-PR #19 reference vocabulary is residue

Spec PR #19 removed six `uav:` terms from the Binding vocabulary:
`uav:capability`, `uav:componentModel`, `uav:reference`,
`uav:congruentType`, `uav:congruentTypeName` and `uav:nameNamespace`. A
document authored against the earlier vocabulary that still carries those terms
is not rejected. The terms are now unmapped JSON and are carried through the
Section 9 `uav:nodes` residue mechanism, with no diagnostic.

That preserves the source document for WoT &rarr; NodeSet &rarr; WoT round-trip,
but it no longer creates the References that earlier conversions produced. A
consumer that browses the projected NodeSet therefore will not see those legacy
references even though conversion succeeds.

Use the current vocabulary instead:

| Earlier term | Current form |
| --- | --- |
| `uav:componentModel` | link `rel` names `ua:HasComponent` directly |
| `uav:capability` | link `rel` names `ua:HasInterface` directly |
| `uav:reference` | link `rel` names `ua:NonHierarchicalReferences` directly |
| `uav:congruentType` + `uav:congruentTypeName` | Section 5.2.1 type binding with `@type` and a `ua:HasTypeDefinition` link |

See [Resolving a type binding: the local context](WotBindings.md#resolving-a-type-binding-the-local-context)
for the Section 5.2.1 binding rules.

### Condition events derive from their ConditionType

A projected event normally derives from `BaseEventType` (`i=2041`). If the event
affordance carries `uav:conditionType` or `uav:conditionTypeId`, Section 13.2
instead makes the event type derive from the named ConditionType. The Condition
mapping is summarized in [Alarms and Conditions](WotBindings.md#alarms-and-conditions).

This changes event-filter behaviour. A client filtering for `BaseEventType`
subtypes still matches the projected Condition event, but a client that assumed
the exact immediate supertype was `BaseEventType`, or that selects
Condition-specific fields, observes a different type hierarchy and field set.

## DataType definitions (Section 6.11)

WoT Binding §6.11 gives Structures, Unions, Enumerations, OptionSets and
SimpleDataTypes a readable vocabulary, so that defining one is no longer on
its own a reason to fall back to the native `uav:nodes` projection. §6.11.8
states that as a completeness contract: a fact the clause covers **shall** be
emitted readably and **shall not** be the reason a converter adds `uav:nodes`.

Both directions are implemented.

**Authoring.** `uav:dataTypeDefinitions` on the Thing root holds the
definitions. Each is a JSON-LD node with an `@id`, a `@type` of
`uav:StructureDefinition`, `uav:EnumDefinition` or `uav:SimpleDataType`, and a
`uav:dataTypeName`. The complete definition occurs in exactly one place and
every other occurrence is an `@id`-only reference; two occurrences that each
contribute properties are rejected rather than merged, because merging two
ordered field lists has no defined answer.

**Identity.** `uav:dataTypeId` states the NodeId where the author wants to.
Otherwise it is derived from `uav:dataTypeName` alone as
`nsu=<NamespaceUri>;s=DataTypes/<Name>`, so the same definition read from a
differently ordered or differently nested document still lands on the same
Node. A field may name a sibling definition by its JSON-LD `@id`; that `@id`
is a graph identifier and is resolved to the sibling's NodeId, never read as
a NodeId itself.

**Inference.** A DataSchema with no explicit definition infers one, but only
where it determines every required fact — inference fails rather than guesses:

| Schema | Result |
|---|---|
| `type: object` with one property | A Structure. |
| `type: object` with more than one property | Requires `uav:fieldOrder`; JSON member order carries no meaning, so without it the encoding order is unknowable and inference **fails**. |
| `required` | Decides `IsOptional`; all required gives `Structure`, otherwise `StructureWithOptionalFields`. |
| `uav:structureType: Union` | A Union. |
| `type: integer` with `oneOf` branches carrying `const` and `uav:enumName` | An Enumeration. |
| `type: integer` with a bare `enum` array | **Not** an Enumeration: it states values but never names them. |
| A bare `integer` or `number` **inside a Structure field** | **Fails.** It is honest about a scalar Variable, where the abstract type permits subtype values, but inside a Structure accepting them would need a subtyped-value kind the schema has not asked for. |
| A custom type named without `uav:dataTypeSubtypeOf` | **Fails.** §6.11.4 forbids a custom type subtyping the abstract `Integer` or `Number`. |

**Encodings.** A non-abstract Structure or Union exposes `Default Binary`,
`Default XML` and `Default JSON`, with identities derived by appending
`/Default Binary` and so on to the type's own identity. An abstract type is
refused encoding identities outright.

**NodeSet to WoT.** Every DataType a NodeSet defines is emitted back into
`uav:dataTypeDefinitions`. Whether a definition is an enumeration is decided
by the shape a NodeSet actually gives it — an enumeration field carries a
value and no DataType, a structure field the reverse — because the file states
no kind directly. An alias is resolved rather than emitted, since a name like
`DataType="Structure"` means nothing outside the document that defines it.

**Known gap.** An inferred definition's own DataSchema terms
(`uav:fieldOrder`, `properties`, `required`, `oneOf`) still travel as residue
rather than being re-derived from the definition, so a document that relies on
inference does not yet reproduce byte-identically and keeps its `uav:nodes`
projection. Closing it is the canonical-schema equivalence of §6.11.6:
derive the schema from the definition, normalize both, and require the two
semantic normal forms to be equal.

## Model and platform vocabulary (Section 6)

The WoT Binding Section 6 model- and platform-vocabulary terms
(composition, containment, naming, units and scaling, semantics,
inheritance) and the anchored browse-path term of Section 5.1.4 are
**readable annotations**: they record OPC UA model facts but have no
distinct structure that this converter materializes into the readable
NodeSet (it does not model, for example, `AnalogUnitType`,
`EngineeringUnits`, or `HasDictionaryEntry` structures). The converter
therefore handles them in one direction with full round-trip fidelity:

- **WoT to NodeSet.** Each term is validated during synthesis against
  the per-term domain and range table of Section 7. A malformed value is
  an error (Section 7 requires a consumer to treat the document as
  invalid rather than repair it), so `ToNodeSet` throws and
  `ToNodeSetResult` reports the diagnostic. A well-formed value is
  carried unchanged through the generic `uav:nodes` residue mechanism.
- **NodeSet to WoT (reverse).** These readable terms are not synthesized
  from a plain NodeSet. When a NodeSet carries a structure that the
  readable vocabulary cannot yet express, the complete `uav:nodes`
  native projection preserves it losslessly, and any residue previously
  captured for a term is re-applied by JSON Pointer.
- **Round-trip.** A document carrying these terms survives
  WoT &rarr; NodeSet &rarr; WoT unchanged. Affordance-level terms
  (`uav:scaleFactor`, `uav:decimalPlaces`, `uav:unitProperty`,
  `uav:semanticId`) are preserved under the affordance's projected local
  name, so an affordance that also carries `uav:browseName` round-trips
  under that browse name's local part rather than its original map key.

The opaque terms `uav:metadata`, `uav:propertyConfiguration`,
`uav:actionConfiguration` and `uav:eventConfiguration` are never
validated and never cause rejection; they are carried verbatim.
