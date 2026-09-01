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
| Property `title` | **Default** | No `DisplayName` field is materialized for the variable. A `titles` map materializes one `LocalizedText` per locale, the default locale's entry first (Section 9.1.1). |
| Property `description` | **Default** | No `Description` field is materialized for the variable. A `descriptions` map materializes one `LocalizedText` per locale. |
| Property `uav:valueRank` (Sections 7, 9.1) | **Default** / **Fails** | Absent: `ValueRank` `-1` (Scalar), which is what a NodeSet omits. Present: the stated rank, so `-3`, `-2`, `-1`, `0` and a fixed positive rank stay distinct. Not an integer literal, or below `-3`: `InvalidValueRank` error. |
| Property `uav:arrayDimensions` (Sections 7, 9.1) | **Default** / **Fails** | Absent: no `ArrayDimensions` attribute. Present: the ordered bounds, with `0` meaning a dimension whose length is not fixed. Not an array of non-negative integers, a length other than a fixed `uav:valueRank`, or any dimension against a rank that fixes none: `InvalidValueRank` error. |
| Property type definition | **Default** | `HasTypeDefinition` to `BaseDataVariableType` (`i=63`). An affordance that binds itself to `PropertyType` (`i=68`) is held by `HasProperty` rather than `HasComponent`, which is the only ReferenceType OPC 10000-3 reaches a Property through. |
| Action affordance `uav:browseName` | **Default** | The affordance map key is used as the local name and BrowseName `1:<key>`. |
| Action affordance `uav:id` | **Default** | Deterministic NodeId `ns=1;s=<rootLocal>/<actionLocal>`. |
| Action `title` | **Default** | No `DisplayName` field is materialized for the method. |
| Action `input` / `output` (Section 9.1) | **Default** / **Fails** | Absent: the Method is materialized with no argument Property. Present: the schema becomes an `InputArguments` / `OutputArguments` Property (`Argument[]`, `DataType` `i=296`, `ValueRank` `1`) held by `HasProperty`, with NodeId `ns=1;s=<rootLocal>/<actionLocal>/<InputArguments\|OutputArguments>`. A schema that names one DataType — through `uav:mapToType`, `uav:dataTypeId`, `uav:dataTypeName` or an inline `uav:dataTypeDefinition` — is one argument, named from its `uav:browseName` or `title` and otherwise `Input` / `Output`; an object schema's members are the arguments. Each member's DataType resolves by the property rules above, `uav:valueRank` defaults to `-1` and `uav:arrayDimensions` and `description` are carried onto the `Argument`. Order comes from `uav:fieldOrder` (Section 6.11.4); a single member needs none, and an `Acknowledge`, `Confirm` or `AddComment` action takes the fixed OPC 10000-9 order `EventId`, `Comment`. A multi-member schema with no order **fails** (`MethodArgumentOrderAmbiguous`) rather than using JSON member order, and one whose `uav:fieldOrder` does not list every member exactly once, or that is not a JSON object, **fails** (`MethodArgumentSchemaInvalid`). A reported schema is still carried verbatim through residue, so a rejected document never loses the signature it authored. |
| Event affordance `uav:browseName` | **Default** | The affordance map key is used as the local name and BrowseName `1:<key>`. |
| Event affordance `uav:id` | **Default** | Deterministic NodeId `ns=1;s=<rootLocal>/<eventLocal>`. |
| Event `title` | **Default** | No `DisplayName` field is materialized for the event type. |
| Event type abstraction/supertype | **Default** | Event affordances materialize as non-abstract `UAObjectType` nodes with inverse `HasSubtype` to `BaseEventType` (`i=2041`) and a root `GeneratesEvent` reference, unless the affordance carries `uav:conditionType` or `uav:conditionTypeId`. A Condition event derives from the named ConditionType instead (WoT Binding Section 13.2). A `uav:conditionType` and a `uav:conditionTypeId` naming different types **fail** (`ConditionTypeConflict`). |
| Event `data` (Section 13.3) | **Default** / **Fails** | Absent, or a member naming a field the projected type inherits: no Node is materialized, because `BaseEventType` and the ConditionTypes already declare those fields and `ConditionId` is the Condition's NodeId Attribute rather than a Variable. A member the type adds becomes a Property (`HasTypeDefinition` `PropertyType`, NodeId `ns=1;s=<rootLocal>/<eventLocal>/<fieldLocal>`) whose DataType resolves by the property rules above, with `uav:valueRank` defaulting to `-1`, `uav:arrayDimensions` and `description` carried across, and `HasModellingRule` `Mandatory` when the schema lists the member in `required` and `Optional` otherwise. A member that is not a DataSchema, or that reaches a BrowseName another member already reached, **fails** (`EventFieldInvalid`) and is carried verbatim through residue. |
| `uav:conditionAction` / `uav:actsOn` (Section 13.4) | **Default** / **Fails** | Absent: the Method is a component of the Thing with no `MethodDeclarationId`. Present and admitted by the ConditionType the target event projects: the Method takes the base-namespace BrowseName of the named Condition Method, carries the OPC 10000-9 declaration as its `MethodDeclarationId` (`Acknowledge` `i=9111`, `Confirm` `i=9113`, `AddComment` `i=9029`, `Enable` `i=9027`, `Disable` `i=9028`) and becomes a component of the EventType instead of the Thing. A Method the projected ConditionType does not declare — `Acknowledge` or `Confirm` against a plain `ua:ConditionType` — **fails** (`ConditionActionNotDeclared`). |
| `uav:severity` (Section 6.6) | **Default** / **Fails** | Absent: no `Severity` Property is materialized, so the EventType inherits the one `BaseEventType` declares and a server applies its own default. Present and in the OPC 10000-5 range `1..1000`: a `Severity` Property (`UInt16`, `HasTypeDefinition` `PropertyType`, `HasModellingRule` `Mandatory`) holding that value, with NodeId `ns=1;s=<rootLocal>/<eventLocal>/Severity`. Outside that range, non-integer, or on an affordance that is not an event: `InvalidEventSeverity` error. The value is **not** clamped — Section 7 forbids it — and the rejected value is carried through residue rather than dropped. |
| `uav:modellingRule` on a property or action | **Default** | No `HasModellingRule` reference is materialized. |
| `uav:hasComponent` / `uav:componentOf` entry has no matching typed ReferenceType link | **Default** | `HasComponent` is used for the component reference. |
| Link `rel` names a ReferenceType the local context holds (Sections 5.1.2, 5.1.5 and 6.2) | **Default** / **Fails** | The relation is created with that exact ReferenceType, stated as a NodeSet-local NodeId. A `rel` matching the ReferenceType's **InverseName** clears `IsForward`; a symmetric ReferenceType reads forward under its BrowseName in both directions. A `rel` matching more than one ReferenceType with no `uav:refId` to settle it: `ReferenceTypeAmbiguous` error. A `rel` or `uav:refId` naming a Node of another NodeClass: `ReferenceTypeNodeClassInvalid` error. A `rel` and a `uav:refId` naming different ReferenceTypes, or a `uav:refId` naming none of an ambiguous name's candidates: `ModelConceptConflict` error. Nothing falls back to `HasComponent` or to a standard alias. |
| `uav:inverseName` / `uav:symmetric` on a document that projects a ReferenceType | **Default** | Absent: the ReferenceType is materialized with no `InverseName` and `Symmetric` `false`. Present: they become the projected Node's `InverseName` (tagged with the document's default locale) and `Symmetric` Attributes, which is what lets a local context built from documents alone resolve an inverse relation. |
| Binding link has no resolvable model-name relation and no `uav:refId` | **Default** | The link maps to `Organizes`. Spec PR #19 removed `uav:componentModel`, `uav:capability` and `uav:reference`; a link now names its ReferenceType directly in `rel` (`ua:HasComponent`, `ua:HasInterface`, `ua:NonHierarchicalReferences`). |
| Reference link points to another Thing by URI and no resolver is supplied or the resolver cannot find `uav:id` | **Default** | The reference is omitted and a warning diagnostic is emitted; no placeholder NodeId is generated. |
| Invalid namespace-qualified `uav:id` / `uav:browseName` syntax or unbound compact-name prefix | **Fails** | An error diagnostic is emitted; `ToNodeSet` throws even though synthesis continues far enough to collect diagnostics. |
| Event affordance says `@type: uav:eventType` and `uav:isEvent: false` | **Fails** | `EventAnnotationConflict` error; the two terms must not contradict each other. |
| `uav:isComposite` (Section 6.1) | **Default** / **Fails** | Absent: the type is treated as atomic and no `HasComponent` walk is forced. Malformed (non-boolean): `InvalidModelVocabularyValue` error and `ToNodeSet` throws. Present and valid: the flag has no distinct readable NodeSet structure, so it is carried verbatim through the `uav:nodes` residue and restored on the reverse conversion. |
| `uav:contains` (Section 6.3) | **Default** / **Fails** | Absent: sub-components come from links only. Malformed (not an array, or an entry that does not match a link `uav:refName` declared on the same type): `InvalidContainment` error. Present and valid: preserved via residue. |
| `uav:containedIn` (Section 6.3) | **Default** / **Fails** | Absent: no parent is recorded. Malformed (not a non-empty string, or naming the type itself, which is a cycle): `InvalidContainment` error. The reciprocal "the named composite exists" check is cross-document and out of scope for the single-document converter, which validates range and self-cycle only. Present and valid: preserved via residue. |
| `uav:unitProperty` (Section 6.4) | **Default** / **Fails** | Absent: the affordance names no unit Property. Malformed (not a canonical RFC 6901 pointer of the form `/properties/<name>`, naming the affordance that carries it, or resolving to something other than a sibling property affordance whose DataSchema `type` is `string`): `InvalidUnitPointer` error. Present and valid: the named affordance becomes the annotated Variable's own `EngineeringUnits` Property (`HasProperty`), unless it states its own `uav:componentOf`. |
| `uav:engineeringUnits` (Section 6.4.1) | **Default** / **Fails** | Absent: no `EUInformation` value is materialized. Malformed (not an object carrying `namespaceUri`, an integer `unitId` and `displayName`): `InvalidEngineeringUnits` error. Present and valid: the affordance's Variable gets `DataType` `EUInformation` (`i=887`) — unless `uav:mapToType` pins another — and a `Value` holding the `EUInformation` in its default XML encoding (`i=888`). |
| `minimum` / `maximum` (Section 6.4.1) | **Default** / **Fails** | Absent, or only one of the two: no `EURange` Property is materialized. `minimum` above `maximum`: `InvalidRangeValue` error. Present and valid: an `EURange` Property (`Range` `i=884`, `HasTypeDefinition` `PropertyType`, `HasModellingRule` `Mandatory`, NodeId `ns=1;s=<rootLocal>/<propertyLocal>/EURange`) holding the interval, or the value of the `EURange` affordance the document authored itself. |
| `uav:instrumentRange` (Section 6.4.1) | **Default** / **Fails** | Absent: no `InstrumentRange` Property. Malformed, or an engineering range not contained in it: `InvalidRangeValue` error. Present and valid: an `InstrumentRange` Property (`Range` `i=884`, `HasModellingRule` `Optional`) holding the interval. |
| `uav:scaleFactor` (Section 6.4) | **Default** / **Fails** | Absent: identity scaling (factor `1`). Malformed (not a non-zero number): `InvalidModelVocabularyValue` error. Present and valid: preserved via residue. It is a static presentation and transport transform, never derived from — nor used to derive — `EngineeringUnits`, `EURange` or `InstrumentRange`. |
| `uav:decimalPlaces` (Section 6.4) | **Default** / **Fails** | Absent: no rounding is recorded. Malformed (not an integer greater than or equal to zero; `2.0` is rejected as a non-integer literal): `InvalidModelVocabularyValue` error. Present and valid: preserved via residue. |
| `titles` / `descriptions` (Section 9.1.1) | **Default** / **Fails** | Absent: the singular member alone materializes one locale-free `LocalizedText` — the form a UANodeSet writes when it names one language — or one tagged with the document's `@language` where the context declares it. A plural member without its singular member, without an entry for the document's default locale, or whose default-locale entry differs from the singular member: `InvalidLocalizedText` error. |
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

### Event fields come from the type, not only from the document

The fields a notification carries are declared by `BaseEventType` and, for a
Condition, by the ConditionTypes of OPC 10000-9 — types a converted NodeSet
almost never contains. Both directions therefore read one table of those
declarations rather than the Nodes:

- going **from** a NodeSet, an event affordance's `data` object states the
  effective field list of the projected type — the inherited fields in
  inheritance order, then the Variables the type declares itself in the order
  its References state them — so a document describes the notification a
  consumer actually receives rather than an empty schema;
- going **to** a NodeSet, only the members the projected type *adds* become
  Nodes, because re-declaring an inherited field would leave a Server holding
  two declarations of one field.

A NodeSet holding an Object and the EventType it raises is projected about the
**Object**: an EventType something else generates is a declaration that Node
uses, not the subject of the document.

The `data` object states what a notification *carries*; `uav:eventSelectClauses`
states what a MonitoredItem *asks for* (Section 6.1). The two are independent:
the schema is derived from the type, the request is authored on the affordance,
and where no request is authored a consumer selects the eight mandatory
`BaseEventType` fields. The converter validates the list and carries it
unchanged — it is a client-side request rather than a model fact, so it projects
to no Node — and the OPC UA binding runtime compiles it into the
`EventFilter.SelectClauses` of the MonitoredItem (see
[WotBindings.md](WotBindings.md#event-field-selection-uaveventselectclauses)).

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
refused encoding identities outright. A concrete type used only inside other
Structures — never directly in an ExtensionObject — may set
`uav:hasDefaultEncoding: false`, and then no encodings are generated for it;
the term is refused on any kind that has no encodings to begin with.

An `OptionSet` states a base of `Byte`, `UInt16`, `UInt32` or `UInt64` wide
enough for its highest authored bit. The abstract `UInteger` is not legal: the
base has to say how many bits exist, and an abstract type says only that there
are some.

**NodeSet to WoT.** Every DataType a NodeSet defines is emitted back into
`uav:dataTypeDefinitions`. Whether a definition is an enumeration is decided
by the shape a NodeSet actually gives it — an enumeration field carries a
value and no DataType, a structure field the reverse — because the file states
no kind directly. The same is true of the structure kind: only `IsUnion` is
recorded, so optionality and subtype allowance are read back off the fields.
An encoding link is searched from both ends, because a NodeSet may write it
from either and real companion models write it from the Object. An alias is
resolved rather than emitted, since a name like `DataType="Structure"` means
nothing outside the document that defines it.

**Known gap.** An inferred definition's own DataSchema terms
(`uav:fieldOrder`, `properties`, `required`, `oneOf`) still travel as residue
rather than being re-derived from the definition, so a document that relies on
inference does not yet reproduce byte-identically and keeps its `uav:nodes`
projection. Closing it is the canonical-schema equivalence of §6.11.6:
derive the schema from the definition, normalize both, and require the two
semantic normal forms to be equal.

## Model and platform vocabulary (Section 6)

The WoT Binding Section 6 model- and platform-vocabulary terms
(composition, containment, naming, semantics, inheritance) and the
anchored browse-path term of Section 5.1.4 are **readable annotations**:
they record OPC UA model facts but have no distinct structure that this
converter materializes into the readable NodeSet (it does not model, for
example, a `HasDictionaryEntry` structure). The converter therefore
handles them in one direction with full round-trip fidelity:

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
  (`uav:scaleFactor`, `uav:decimalPlaces`, `uav:semanticId`) are
  preserved under the affordance's projected local name, so an affordance
  that also carries `uav:browseName` round-trips under that browse name's
  local part rather than its original map key.

The opaque terms `uav:metadata`, `uav:propertyConfiguration`,
`uav:actionConfiguration` and `uav:eventConfiguration` are never read and
never cause rejection; they are carried verbatim. Their **shape** is
checked, because a consumer that must carry a value unchanged and must
not reject it is otherwise obliged to carry an unbounded, unattributable
value (Section 6.6): every top-level key is an absolute IRI or a compact
IRI whose prefix the document's `@context` binds, and the object stays
within 65 536 canonical octets, 32 levels of nesting and 256 top-level
keys. Revision 1.0 stated no key rule, so a document whose keys are not
namespaced is **preserved** and reported as deprecated rather than
rejected; strict conformance (below) turns the same finding into an
error.

## Conformance claims and strict mode (Sections 4.1, 6.1, 6.6 and 11)

Four document-level terms describe the document rather than the
AddressSpace, so none of them becomes a Node and all of them survive a
round trip verbatim through the residue mechanism:

| Term | Where | What is checked |
|---|---|---|
| `uav:bindingVersion` | TD/TM root | a `<major>.<minor>` revision string (Section 4.1) |
| `uav:profile` | TD/TM root | a non-empty array of the Section 11 unit and profile names |
| `uav:eventSelectClauses` | event affordance | a non-empty ordered list of two-member clauses with relative paths and no repeats (Section 6.1) |
| `uav:minimumSecurity` | `auto` security scheme | `uav:securityMode`, `uav:securityPolicy`, or both, and no other member (Section 5.7.1) |

`WotNodeSetConverterOptions.ConformanceMode` chooses how strictly the rest
is held:

- **`Permissive`** (the default) processes what it understands and
  preserves the rest. An unknown `uav:` term is carried unchanged as
  residue rather than reported, a revision this library does not
  implement is accepted, and an unnamespaced opaque key is a warning.
  This is what Sections 4.1, 6.6, 9.4 and 10.2 require of a consumer.
- **`Strict`** additionally reports a `uav:` term this revision does not
  define, a declared revision this library does not implement, an opaque
  object that breaks the key or bound rules, and — where
  `RequiredConformance` names units or profiles — a claim the document
  does not make. Claiming a profile claims every unit it names, so
  `WoT-Modeller` satisfies a requirement of `WoT-EventMapping`.

Strict mode is for authoring and conformance testing: a misspelled term
should fail there rather than travel silently. It is never the default,
because a consumer is not allowed to reject a document for carrying a
term it does not know.

The revision this library implements, the closed Section 11 name set, the
opaque bounds and the security strength orders are stated once, in
`Opc.Ua.Wot.WotBindingConformance`; the select-clause term, its shape rules
and its documented `BaseEventType` default are stated once, in
`Opc.Ua.Wot.WotEventSelectClauses`.

`uav:severity` is the exception to the "readable annotation" rule above: it
names one OPC UA model fact, the EventType's own `Severity` Property, so both
directions map it rather than carrying it. A NodeSet whose EventType declares a
`Severity` Property with a value in `1..1000` emits the term; a document
authoring the term materializes that Property. Because the term is mapped it is
**not** also captured as residue — an out-of-range value, which is not mapped,
still is, so a rejected document keeps what its author wrote.

## Engineering units, ranges and scaling (Sections 6.4 and 6.4.1)

`unit`, `uav:unitProperty`, `uav:engineeringUnits`, `minimum`/`maximum` and
`uav:instrumentRange` are **mapped**, not carried: each names a Property Node
of an `AnalogUnitType` or `AnalogItemType` Variable, and both directions
materialize it.

**NodeSet to WoT.** For every Variable that holds them, the converter decodes
the three OPC 10000-8 Properties:

- `EngineeringUnits` projects to a property affordance of its own. That
  affordance carries `type: "string"` — what a client reads there at run time —
  the definitive `uav:mapToType` `i=887`, and `uav:engineeringUnits` with the
  `EUInformation`'s `namespaceUri`, integer `unitId`, `displayName` and
  `description`. The authority and its machine-readable code are what a display
  string alone cannot recover.
- The annotated Variable's affordance states `unit` (the `EUInformation`
  `DisplayName`, never a quantity kind) and `uav:unitProperty`, a canonical
  `/properties/<name>` pointer at that sibling affordance.
- `EURange` becomes `minimum` and `maximum`; `InstrumentRange` becomes
  `uav:instrumentRange`.

Only a Property whose value actually decodes contributes. A foreign encoding, a
partial structure or a missing value leaves the Property an ordinary affordance,
the readable mapping is then incomplete, and the `uav:nodes` projection carries
it — a reported gap rather than a number nobody wrote. Nothing is ever derived
from the width of a DataType: an `Int16` reads from −32768 to 32767, and that is
a fact about the machine representation rather than an engineering range.

**WoT to NodeSet.** `uav:engineeringUnits` materializes the `EUInformation`
value; `uav:unitProperty` makes the affordance it names a `HasProperty` child of
the annotated Variable, unless that affordance states its own `uav:componentOf`;
`minimum`/`maximum` and `uav:instrumentRange` materialize `EURange` and
`InstrumentRange` Properties (`Range` `i=884`, `PropertyType`), or fill in the
value of the Node an authored `EURange`/`InstrumentRange` affordance already
produced.

`uav:scaleFactor` and `uav:decimalPlaces` stay readable annotations carried
through residue. Section 6.4 describes them as a static presentation and
transport transform, so a converter neither derives them from the analog
Properties nor derives those Properties from them; a source NodeSet that states
neither never gains either.

## Localized text (Section 9.1.1)

`DisplayName` and `Description` are `LocalizedText`, and both directions carry
every locale.

**NodeSet to WoT.** The document's default locale is the locale the root Node's
own `DisplayName` (or `Description`) states, declared as the `@language` of the
generated `@context`; a source that names none declares none, and Section
9.1.1's `en` then applies. A Node with one locale writes `title` and
`description` alone. A Node with several writes the plural `titles` and
`descriptions` maps as well, and the singular member is always the
default-locale entry, so the two never disagree. Where a Node's locales do not
include the document's default locale, the plural member is **not** written —
inventing an entry would state a translation the source never made — and the
completeness check reports the gap so preservation carries the rest.

**WoT to NodeSet.** A plural member becomes one `LocalizedText` per entry with
the default locale's entry first, which is the one the Node's own attribute
carries. A singular member alone becomes one `LocalizedText` tagged with the
document's declared `@language`, or untagged where the context declares none —
the form a UANodeSet writes when it names one language without saying which.

The mapping applies to the root, to property, action and event affordances, to
event fields, to `Method` argument descriptions, and to DataType definitions and
their structure and enumeration fields. A field's `DisplayName` used to be
dropped on the way out; it is now `title`.

## ValueRank and ArrayDimensions (Sections 7 and 9.1)

A DataSchema's `type` says whether a value is an array, not which of the five
things an OPC 10000-3 ValueRank says. `uav:valueRank` and `uav:arrayDimensions`
are therefore emitted and read on ordinary Variable affordances as well as on
`Method` arguments, event fields and DataType-definition fields.

The scalar rank `-1` is the default a NodeSet omits, so it is not restated;
`-3` (ScalarOrOneDimension), `-2` (Any), `0` (OneOrMoreDimensions) and every
fixed positive rank are written and read back exactly, and none of them is
collapsed to a scalar. `uav:arrayDimensions` carries one bound per dimension —
`0` for a dimension whose length is not fixed — so its length is the rank by
construction: a count that disagrees with a fixed rank, or any dimension against
a rank that fixes none, is an `InvalidValueRank` error rather than a silently
malformed Variable.

## Method arguments (Section 9.1)

A UA Method's `InputArguments` and `OutputArguments` are the WoT action's
`input` and `output` DataSchemas, in both directions.

**NodeSet to WoT.** The `Argument` structures the argument Properties hold are
decoded into an object DataSchema whose members are the arguments, whose
`uav:fieldOrder` states their declaration order — the order an OPC 10000-4
`Call` is positional over — and whose `required` lists all of them, because a
Call supplies all of them. Each member carries the WoT type members that stand
for its DataType, the definitive `uav:mapToType`, `uav:valueRank`, any
`uav:arrayDimensions` and its `Description`. An argument Property the schemas
represent is no longer emitted a second time as a sibling property of the
Thing; one whose value cannot be decoded still is, so no Node is lost. The
readable schemas state what the arguments are and not which Nodes hold them, so
the exact NodeId and attributes of the argument Properties travel in the
`uav:nodes` preservation projection.

**WoT to NodeSet.** See the `input` / `output` row of the defaults table above.

## ReferenceTypes and relations (Sections 5.1.2, 5.3 and 6.2)

The readable mapping carries some References structurally — containment as
affordances and `uav:hasComponent` / `uav:componentOf`, the type hierarchy as
`tm:extends`, the type definition as a `ua:HasTypeDefinition` link, the
modelling rule as `uav:modellingRule`, the event source as an event affordance
and a DataType's encodings as `uav:defaultEncodingId`. Section 6.2 says a
Reference is a single relation and a document shall not be read as declaring
two, so none of those is written a second time.

**NodeSet to WoT.** Every *other* Reference — a companion model's own
ReferenceType, `ua:HasInterface`, `ua:Organizes` — is written as a typed link:
the ReferenceType's compact model name in `rel`, its portable
`ExpandedNodeId` in `uav:refId`, and the target's own BrowseName in
`uav:refName` where the NodeSet holds the target. A reference that runs inverse
is written under the ReferenceType's **InverseName**, which is what states the
direction. The compact name comes from the NodeSet itself, in order of
specificity: its own `UAReferenceType` declarations (the only place an
InverseName and the Symmetric flag are stated), its `<Aliases>` table, then the
standard base-namespace names. A ReferenceType none of the three names, and the
inverse direction of one that declares no InverseName, are reported
(`ModelConceptUnresolved`) rather than written under a substitute relation: a
link whose `rel` named a different ReferenceType, or whose direction was
silently reversed, would read as a fact the source never stated.

A document that projects a ReferenceType Node itself additionally carries
`uav:inverseName` and `uav:symmetric`, so a set of documents describes a
companion model's relations completely enough to convert back.

**WoT to NodeSet.** See the ReferenceType rows of the defaults table above, and
[Resolving a relation: companion ReferenceTypes](WotBindings.md#resolving-a-relation-companion-referencetypes)
for how a `rel` and a `uav:refId` are resolved against the Section 5.1.5 local
context.

