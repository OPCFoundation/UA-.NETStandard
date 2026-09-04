# WoT / NodeSet conversion

`Opc.Ua.Wot.WotNodeSetConverter` converts OPC UA NodeSet2 documents to
WoT Thing Models / Thing Descriptions and materializes WoT documents back
to NodeSet2. The converter preserves a byte-exact `uav:nodeSet` envelope
when requested, uses the structured `uav:nodes` projection when the
readable vocabulary is not complete, and otherwise synthesizes NodeSet2
from the readable WoT terms.

## Which specification revision this tracks

This implementation tracks **WoT Binding revision 1.1**. The revision is
stated once, as `Opc.Ua.Wot.WotBindingConformance.CurrentRevision`, and
everything that depends on it — the `uav:bindingVersion` values strict
conformance accepts, the versioned artifact base
`http://opcfoundation.org/UA/WoT-Binding/v1.1/`, and the closed Section 11
name set — derives from that constant. No public behaviour is keyed to an
upstream commit: a revision is a published contract, a commit is not.

The `uav` prefix binds to `http://opcfoundation.org/UA/WoT-Binding/`. The
reader matches the compact `uav:` spelling and never consults the IRI a
document binds the prefix to, in permissive *and* in strict mode, because
strictness is about the vocabulary a document uses and not about the IRI it
declares that vocabulary under. A document written against the earlier
`http://opcfoundation.org/UA/WoT/v1#` draft binding therefore stays
readable; that leniency is deliberate and is covered by a test.

The specification's worked examples are vendored as test fixtures under
`tests/Opc.Ua.Types.Tests/Wot/Assets`, and *those* do record the exact
source. `spec-examples.manifest.json` beside them names the source
repository, branch and commit together with the size and SHA-256 of every
example, and `WotSpecFixtureManifestTests` fails the build when the two
disagree. See [How this is checked](WotBindings.md#how-this-is-checked)
for the drift check and the regeneration step.

## Importable output

A NodeSet2 document may write a standard name such as `HasComponent` or
`Double` wherever a NodeId is expected, but only if it declares that name
in its own `<Aliases>` table; the importer reports `BadNodeIdInvalid` for a
name it cannot find. Both halves of the conversion produce such names —
synthesis writes the readable names directly, and a restore reproduces
whatever spelling the document it restores from used — so every converted
NodeSet is passed through an alias-completion pass before it is returned.
That pass is not a WoT concept: `NodeSetAliasCompleter` sits beside
`UANodeSet` itself and declares whatever the `INodeSetAliasResolver` it is
handed knows. The WoT Binding's own policy is one object,
`WotNodeSetAliases`, which states that a converted document writes the
standard base-namespace names and delegates to the single table of them
(`NodeSetStandardAliases`) rather than repeating any.

The pass declares only names that policy resolves, appended after the
declarations the document already carried in ascending ordinal order of
the alias. It is idempotent, so a byte-exact `uav:nodeSet` restore stays
byte-exact, and it never rewrites a name: a vendor alias the document uses
but does not declare still fails the import with the message that names
it, rather than being quietly discarded.

Completion is a decision a *producer* makes about a document it writes.
Comparison makes none of its own: `NodeSetComparer.CompareEquivalent`
resolves each document through its own declarations first and asks
`NodeSetComparisonOptions.AliasResolver` about the rest. That property
defaults to nothing at all, so a comparison of two arbitrary NodeSets does
not report a document that writes `HasComponent` without declaring it —
which no Server could load — as equivalent to one that writes `i=47`. The
WoT conversion injects `WotNodeSetAliases` through
`WotNodeSetConverterOptions.ToComparisonOptions`, so its Section 9.2
completeness check reads the names the Binding writes, and the comparison
itself stays free of any knowledge of WoT.

That matters beyond tidiness, because the runtime registry materialization
path is `ConvertAsync` → serialize → `UANodeSet.Read` → `Import`. Every
vendored specification example that converts is run through exactly that
sequence by `WotNodeSetImportTests`, and so is every preservation mode.

### Modelling rules and the two placeholder identifiers

OPC 10000-5 assigns `OptionalPlaceholder` the identifier `11508` and
`MandatoryPlaceholder` the identifier `11510`; `11509` is not a
ModellingRule Object at all. The four rules therefore map as:

| `uav:modellingRule` | NodeId |
| --- | --- |
| `Mandatory` | `i=78` |
| `Optional` | `i=80` |
| `OptionalPlaceholder` | `i=11508` |
| `MandatoryPlaceholder` | `i=11510` |

Both conversion directions derive these identifiers from one pair of constants,
so a round trip preserves the modelling rule it started with.

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
| Root `uav:id` | **Default** | Deterministic NodeId by Annex G.1: `ns=1;s=/nsu=<escaped model NamespaceUri>;<rootLocal>`, and an informational `GeneratedNodeId` diagnostic. |
| Root `uav:browseName` | **Default** | `1:<rootLocal>`. |
| Root `title` | **Default** | Root `DisplayName` becomes `<rootLocal>`. |
| Thing Model root event annotation `uav:eventType` | **Default** | Root is a non-abstract `UAObjectType` with inverse `HasSubtype` to `BaseObjectType` (`i=58`). If the event annotation is present, the default supertype is `BaseEventType` (`i=2041`). |
| Thing Description root type information | **Default** / **Bound** / **Fails** | Absent: root is a `UAObject` with `HasTypeDefinition` to `BaseObjectType` (`i=58`). Present: a `ua:HasTypeDefinition` link (WoT Binding Section 5.2.1) whose `href` is the ExpandedNodeId of the type, and/or a compact model name in `@type`, binds the root to that type so the converter reuses the existing type rather than defining a second one. The two forms are resolved against the Section 5.1.5 local context — the sibling documents of the conversion first, a loaded AddressSpace as the fallback — supplied through `IWotNodeResolver`. A binding that names a type the local context does not hold **fails** (`UnresolvedTypeBinding`) rather than falling back to `BaseObjectType`; two bindings that disagree, an ambiguous name with nothing to settle it, or a resolved type of the wrong NodeClass **fail** as `AmbiguousTypeBinding`. A compact name is a binding when its namespace is one the local context holds; any other `@type` member is ordinary annotation and is retained as residue. |
| Root `description` | **Default** | No `Description` field is materialized. |
| Property affordance `uav:browseName` | **Default** | The affordance map key is used as the local name and BrowseName `1:<key>`. |
| Property affordance `uav:id` | **Default** | Deterministic NodeId by Annex G.1: `ns=1;s=/nsu=<escaped model NamespaceUri>;<rootLocal>/nsu=<escaped model NamespaceUri>;<propertyLocal>`. |
| Property DataSchema `type` or an unrecognized `type` | **Default** | The canonical table of WoT Binding §6.11.4: `boolean` → `Boolean`, `integer` → the **abstract** `Integer` (`i=27`), `number` → the **abstract** `Number` (`i=26`), `string` → `String`, refined by `contentEncoding: base64` → `ByteString`, `format: date-time` → `DateTime`, `format: uuid` → `Guid`, `format: uri` → `UriString`. An explicit `uav:dataTypeId` or `uav:mapToType` outranks the inference. Anything unrecognized falls back to `BaseDataType` (`i=24`). A bare `integer` or `number` is deliberately abstract: the schema states only that the value is whole or numeric, and a concrete width is recovered from an annotation rather than guessed. |
| Property `readOnly` and `writeOnly` | **Default** | Missing flags mean read/write access (`CurrentRead | CurrentWrite`, value `3`). If both flags are `true`, the zero-access result is coerced to `CurrentRead` (`1`); this is an arbitrary safety default and should be specified explicitly. |
| Property `title` | **Default** | No `DisplayName` field is materialized for the variable. A `titles` map materializes one `LocalizedText` per locale, the default locale's entry first (Section 9.1.1). |
| Property `description` | **Default** | No `Description` field is materialized for the variable. A `descriptions` map materializes one `LocalizedText` per locale. |
| Property `uav:valueRank` (Sections 7, 9.1) | **Default** / **Fails** | Absent: `ValueRank` `-1` (Scalar), which is what a NodeSet omits. Present: the stated rank, so `-3`, `-2`, `-1`, `0` and a fixed positive rank stay distinct. Not an integer literal, or below `-3`: `InvalidValueRank` error. |
| Property `uav:arrayDimensions` (Sections 7, 9.1) | **Default** / **Fails** | Absent: no `ArrayDimensions` attribute. Present: the ordered bounds, with `0` meaning a dimension whose length is not fixed. Not an array of non-negative integers, a length other than a fixed `uav:valueRank`, or any dimension against a rank that fixes none: `InvalidValueRank` error. |
| Property type definition | **Default** | `HasTypeDefinition` to `BaseDataVariableType` (`i=63`). An affordance that binds itself to `PropertyType` (`i=68`) is held by `HasProperty` rather than `HasComponent`, which is the only ReferenceType OPC 10000-3 reaches a Property through. |
| Action affordance `uav:browseName` | **Default** | The affordance map key is used as the local name and BrowseName `1:<key>`. |
| Action affordance `uav:id` | **Default** | Deterministic NodeId by Annex G.1: `ns=1;s=/nsu=<escaped model NamespaceUri>;<rootLocal>/nsu=<escaped model NamespaceUri>;<actionLocal>`. |
| Action `title` | **Default** | No `DisplayName` field is materialized for the method. |
| Action `input` / `output` (Section 9.1) | **Default** / **Fails** | Absent: the Method is materialized with no argument Property. Present: the schema becomes an `InputArguments` / `OutputArguments` Property (`Argument[]`, `DataType` `i=296`, `ValueRank` `1`) held by `HasProperty`, with NodeId `ns=1;s=/nsu=<escaped model NamespaceUri>;<rootLocal>/nsu=<escaped model NamespaceUri>;<actionLocal>/<InputArguments\|OutputArguments>` (Annex G.1; the standard child keeps its bare base-namespace name). A schema that names one DataType — through `uav:mapToType`, `uav:dataTypeId`, `uav:dataTypeName` or an inline `uav:dataTypeDefinition` — is one argument, named from its `uav:browseName` or `title` and otherwise `Input` / `Output`; an object schema's members are the arguments. Each member's DataType resolves by the property rules above, `uav:valueRank` defaults to `-1` and `uav:arrayDimensions` and `description` are carried onto the `Argument`. Order comes from `uav:fieldOrder` (Section 6.11.4); a single member needs none, and an `Acknowledge`, `Confirm` or `AddComment` action takes the fixed OPC 10000-9 order `EventId`, `Comment`. A multi-member schema with no order **fails** (`MethodArgumentOrderAmbiguous`) rather than using JSON member order, and one whose `uav:fieldOrder` does not list every member exactly once, or that is not a JSON object, **fails** (`MethodArgumentSchemaInvalid`). A reported schema is still carried verbatim through residue, so a rejected document never loses the signature it authored. |
| Event affordance `uav:browseName` | **Default** | The affordance map key is used as the local name and BrowseName `1:<key>`. |
| Event affordance `uav:id` | **Default** | Deterministic NodeId by Annex G.1: `ns=1;s=/nsu=<escaped model NamespaceUri>;<rootLocal>/nsu=<escaped model NamespaceUri>;<eventLocal>`. |
| Event `title` | **Default** | No `DisplayName` field is materialized for the event type. |
| Event type abstraction/supertype | **Default** | Event affordances materialize as non-abstract `UAObjectType` nodes with inverse `HasSubtype` to `BaseEventType` (`i=2041`) and a root `GeneratesEvent` reference, unless the affordance carries `uav:conditionType` or `uav:conditionTypeId`. A Condition event derives from the named ConditionType instead (WoT Binding Section 13.2). A `uav:conditionType` and a `uav:conditionTypeId` naming different types **fail** (`ConditionTypeConflict`). |
| Event `data` (Section 13.3) | **Default** / **Fails** | Absent, or a member naming a field the projected type inherits: no Node is materialized, because `BaseEventType` and the ConditionTypes already declare those fields and `ConditionId` is the Condition's NodeId Attribute rather than a Variable. A member the type adds becomes a Property (`HasTypeDefinition` `PropertyType`, NodeId `ns=1;s=/nsu=<escaped model NamespaceUri>;<rootLocal>/nsu=<escaped model NamespaceUri>;<eventLocal>/nsu=<escaped model NamespaceUri>;<fieldLocal>` (Annex G.1)) whose DataType resolves by the property rules above, with `uav:valueRank` defaulting to `-1`, `uav:arrayDimensions` and `description` carried across, and `HasModellingRule` `Mandatory` when the schema lists the member in `required` and `Optional` otherwise. A member that is not a DataSchema, or that reaches a BrowseName another member already reached, **fails** (`EventFieldInvalid`) and is carried verbatim through residue. |
| `uav:conditionAction` / `uav:actsOn` (Section 13.4) | **Default** / **Fails** | Absent: the Method is a component of the Thing with no `MethodDeclarationId`. Present and admitted by the ConditionType the target event projects: the Method takes the base-namespace BrowseName of the named Condition Method, carries the OPC 10000-9 declaration as its `MethodDeclarationId` (`Acknowledge` `i=9111`, `Confirm` `i=9113`, `AddComment` `i=9029`, `Enable` `i=9027`, `Disable` `i=9028`) and becomes a component of the EventType instead of the Thing. A Method the projected ConditionType does not declare — `Acknowledge` or `Confirm` against a plain `ua:ConditionType` — **fails** (`ConditionActionNotDeclared`). |
| Unknown event member `uav:severity` | **Default** | WoT Binding 1.1 defines no such term. A document that carries one is consumed permissively: the member is ordinary unknown residue, nothing is materialized from it, and no `Severity` Property is synthesized. Strict authoring reports it as an unknown term. |
| `uav:modellingRule` on a property or action | **Default** | No `HasModellingRule` reference is materialized. |
| `uav:hasComponent` / `uav:componentOf` entry has no matching typed ReferenceType link | **Default** | `HasComponent` is used for the component reference. |
| Link `rel` names a ReferenceType the local context holds (Sections 5.1.2, 5.1.5 and 6.2) | **Default** / **Fails** | The relation is created with that exact ReferenceType, stated as a NodeSet-local NodeId. A `rel` matching the ReferenceType's **InverseName** clears `IsForward`; a symmetric ReferenceType reads forward under its BrowseName in both directions. A `rel` matching more than one ReferenceType with no `uav:refId` to settle it: `ReferenceTypeAmbiguous` error. A `rel` or `uav:refId` naming a Node of another NodeClass: `ReferenceTypeNodeClassInvalid` error. A `rel` and a `uav:refId` naming different ReferenceTypes, or a `uav:refId` naming none of an ambiguous name's candidates: `ModelConceptConflict` error. Nothing falls back to `HasComponent` or to a standard alias. |
| `uav:inverseName` / `uav:symmetric` on a document that projects a ReferenceType | **Default** | Absent: the ReferenceType is materialized with no `InverseName` and `Symmetric` `false`. Present: they become the projected Node's `InverseName` (tagged with the document's default locale) and `Symmetric` Attributes, which is what lets a local context built from documents alone resolve an inverse relation. |
| Binding link has no resolvable model-name relation and no `uav:refId` | **Default** | The link maps to `Organizes`. A typed link names its ReferenceType directly in `rel` (`ua:HasComponent`, `ua:HasInterface`, `ua:NonHierarchicalReferences`). |
| Reference link points to another Thing by URI and no resolver is supplied or the resolver cannot find `uav:id` | **Default** | The reference is omitted and a warning diagnostic is emitted; no placeholder NodeId is generated. |
| Invalid namespace-qualified `uav:id` / `uav:browseName` syntax or unbound compact-name prefix | **Fails** | An error diagnostic is emitted; `ToNodeSet` throws even though synthesis continues far enough to collect diagnostics. |
| Event affordance says `@type: uav:eventType` and carries `uav:isEvent` | **Default** | WoT Binding 1.1 defines event identity with the `@type` annotation and defines no `uav:isEvent` term. The unknown member is preserved as residue and reported as an unknown term in strict authoring. |
| `uav:isComposite` (Section 6.1) | **Default** / **Fails** | Absent: the type is treated as atomic and no `HasComponent` walk is forced. Malformed (non-boolean): `InvalidModelVocabularyValue` error and `ToNodeSet` throws. Present and valid: the flag has no distinct readable NodeSet structure, so it is carried verbatim through the `uav:nodes` residue and restored on the reverse conversion. |
| `uav:contains` (Section 6.3) | **Default** / **Fails** | Absent: sub-components come from links only. Malformed (not an array, or an entry that does not match a link `uav:refName` declared on the same type): `InvalidContainment` error. Present and valid: preserved via residue. |
| `uav:containedIn` (Section 6.3) | **Default** / **Fails** | Absent: no parent is recorded. Malformed (not a non-empty string, or naming the type itself, which is a cycle): `InvalidContainment` error. The reciprocal "the named composite exists" check is cross-document and out of scope for the single-document converter, which validates range and self-cycle only. Present and valid: preserved via residue. |
| `uav:unitProperty` (Section 6.4) | **Default** / **Fails** | Absent: the affordance names no unit Property. Malformed (not a canonical RFC 6901 pointer of the form `/properties/<name>`, naming the affordance that carries it, or resolving to something other than a sibling property affordance whose DataSchema `type` is `string`): `InvalidUnitPointer` error. Present and valid: the named affordance becomes the annotated Variable's own `EngineeringUnits` Property (`HasProperty`), unless it states its own `uav:componentOf`. |
| `uav:engineeringUnits` (Section 6.4.1) | **Default** / **Fails** | Absent: no `EUInformation` value is materialized. Malformed (not an object carrying `namespaceUri`, an integer `unitId` and `displayName`): `InvalidEngineeringUnits` error. Present and valid: the affordance's Variable gets `DataType` `EUInformation` (`i=887`) — unless `uav:mapToType` pins another — and a `Value` holding the `EUInformation` in its default XML encoding (`i=888`). |
| `minimum` / `maximum` (Section 6.4.1) | **Default** / **Fails** | Absent, or only one of the two: no `EURange` Property is materialized. `minimum` above `maximum`: `InvalidRangeValue` error. Present and valid: an `EURange` Property (`Range` `i=884`, `HasTypeDefinition` `PropertyType`, `HasModellingRule` `Mandatory`, NodeId `ns=1;s=/nsu=<escaped model NamespaceUri>;<rootLocal>/nsu=<escaped model NamespaceUri>;<propertyLocal>/EURange` (Annex G.1)) holding the interval, or the value of the `EURange` affordance the document authored itself. |
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

## Portable identity and preservation semantics

The following sections describe generated NodeId identity and the distinct
measurements used for preserved JSON values.

### Generated NodeIds follow Annex G.1

A document that authors no `uav:id` gets a **generated** NodeId, and the
generation is the Annex G.1 formula rather than a convenience of this
implementation:

> `GeneratedNodeId(U, P) = "nsu=" + U + ";s=" + P`

`U` is the NamespaceUri the synthesized Node is created in, and `P` is the
Node's absolute browse path in OPC 10000-4 Annex A.2 relative-path syntax: each
element is preceded by `/`, an element of the base OPC UA namespace is written
bare, any other element is `nsu=<percent-encoded NamespaceUri>;<name>`, and the
Annex A.2 reserved characters `&/.<>:#!` are escaped with `&` inside a name. A
NodeSet file carries the same identity in its NodeSet-local spelling,
`ns=1;s=<P>`, because namespace index 1 is `U`; the reverse mapping renders it
back as `nsu=U;s=P`.

`WotPortableIdentity.GenerateNodeId` / `GenerateBrowsePath` is the single
implementation, so a conversion and a published Annex G.1 vector measure the
same function. The same class answers `IsPortableNodeId`,
`IsPortableQualifiedName` and `IsResolvableBrowsePath`, which is what the
conversion's Section 5.1.1 and 5.1.4 validation calls.

The leading separator, per-element namespace qualification and escaping make
the generated identity injective. A member named `A/B` of `Root` has a different
identifier from a member named `B` of `Root/A`, and a base-namespace
`InputArguments` has a different identity from a model member with the same
name. A document-authored `uav:id` always wins over generation; author one when
the identity must be fixed independently of its browse path.

### Preservation digests and the two things that can be measured

Annex G distinguishes three measurements over a JSON value, and this
implementation keeps them apart. Two of them are digests; the third is a size.

* **A digest over retained bytes.** The `Sha256` of a `WoTJsonResidue` member is
  the SHA-256 of the **decoded residue bytes exactly** — the bytes the producer
  encoded, and the bytes a verifier decoded. Nothing is canonicalized, reordered,
  re-escaped or re-formatted before digesting, in either direction, and a
  mismatch is a mismatch: the entry is corrupt, not merely differently spelled.
  A residue member exists to carry a value Section 6.6 forbids a consumer to
  reformat, so a digest over a re-serialization would pin a value no party is
  allowed to produce. The `sha256` of a `uav:nodeSet` envelope and the
  `uav:sourceDigest` of a projection source are of the same kind.
* **Equality over a JSON value.** RFC 8785 (JCS) is used where two JSON *values*
  are compared for equality — the Section 9.4 conflict test that asks whether a
  residue entry holds the same value as a member the readable mapping already
  produced. `WotJsonCanonicalizer` is that form, and `WotDocument.ToCanonicalUtf8`
  writes it: object members sorted by the UTF-16 code units of their names,
  the minimal string escaping of ECMAScript `JSON.stringify`, and numbers in the
  ECMAScript form of their IEEE-754 double value. A reordered object, an
  equivalent escape and `1.0` beside `1` are therefore one value and not a
  conflict. A number outside the interoperable domain of RFC 8259 §6 — a literal
  carrying more precision than a double holds, such as `9007199254740993` — is
  **diagnosed** rather than canonicalized, and the conflict test falls back to
  comparing the two as written: that can report a conflict JCS would not, but it
  never reports two different values as one. No digest is taken over this form.
* **A size over the compact received form.** The opaque-object size bound of
  Section 6.6 is measured over the **compact received form** of Annex G.4: the
  received text of the value with insignificant whitespace removed — the four
  RFC 8259 whitespace characters outside a string literal — and nothing else
  changed. Member order is the received order, numbers keep the lexical form
  they were written in, and strings keep the escapes they were written with.
  Deliberately *not* a canonical re-serialization: measuring one would oblige a
  consumer to produce exactly the reordered, renumbered value the preservation
  rule forbids, and an "almost-JCS" measurement — compact separators but a
  language's own `double` formatting — is what two implementations disagree
  about in practice. Whitespace removal outside string literals is decidable by
  a scanner with one bit of state, so two implementations that received the same
  bytes measure the same number. The depth and key-count bounds are measured
  over the parsed value, where formatting cannot matter.

### Unmapped reference vocabulary is residue

The Binding vocabulary does not define `uav:capability`,
`uav:componentModel`, `uav:reference`, `uav:congruentType`,
`uav:congruentTypeName` or `uav:nameNamespace`. The converter treats them as
unmapped JSON and carries them through the Section 9 `uav:nodes` residue
mechanism without creating References or emitting a permissive-mode diagnostic.

Use the following Binding forms to state those relationships:

| Unmapped term | Binding form |
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

The `data` object states what a notification *carries*; the affordance's `tm:ref`
and `uav:eventSelectClauses` state what a MonitoredItem *asks for* (Section 6.1).
The schema is derived from the type; the request is a link to the EventType
definition the fields come from, refined by the clauses the affordance writes,
and where the affordance states neither a consumer selects the eight mandatory
`BaseEventType` fields.

Going **from** a NodeSet, an EventType Node becomes a Thing Model of its own
whose root carries `uav:eventType`, the portable `uav:id`, and the effective
`data` schema with a `uav:fieldOrder` on every walked object of more than one
property — which is exactly the EventType definition a fast path derives its
baseline from. Where a document set carries that sibling document, the Object
or ObjectType that raises the event names it with `tm:ref`; a document that
projects the EventType itself keeps its inline definition and does not reference
itself.

Going **to** a NodeSet, the link is resolved before the EventType's fields and
Condition Methods are synthesized, so an affordance that declares no `data` of
its own materializes the field set the linked definition declares. The clause
list itself projects to no Node — it is a client-side request rather than a
model fact — and the OPC UA binding runtime compiles the resolved selection into
the `EventFilter.SelectClauses` of the MonitoredItem (see
[WotBindings.md](WotBindings.md#event-field-selection-tmref-and-uaveventselectclauses)).

### Resolving a stated selection needs the asynchronous conversion

An EventType definition is a *document*, so deriving a selection means following
a document link. Only `WotNodeSetConverter.ToNodeSetResultAsync` does that, and
only when it is given an `IWotThingResolver` holding the local document context
of Section 5.1.5:

```csharp
WotConversionResult<UANodeSet> result = await WotNodeSetConverter
    .ToNodeSetResultAsync(document, options, thingResolver, cancellationToken: ct)
    .ConfigureAwait(false);
```

The synchronous `ToNodeSetResult` and `ToNodeSet` follow no link. An affordance
that states a selection — a `tm:ref`, `uav:eventSelectClauses`, or both — is
therefore reported with `WotDiagnosticCode.EventSelectionUnresolved` rather than
converted, because an EventType materialized without the fields its linked
definition declares is indistinguishable from one that never had any. Where
every reference a document writes is local to that document,
`NullWotResolver.Instance` is the explicit "no external resolution" policy that
still takes the asynchronous path. An affordance that states no selection needs
no resolution: it takes the implicit `BaseEventType` default and converts
synchronously as before.

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
within 65 536 octets in the **compact received form** of Annex G.4 (see
[Preservation digests and the two things that can be measured](#preservation-digests-and-the-two-things-that-can-be-measured)),
32 levels of nesting and 256 top-level keys. Revision 1.0 stated no key
rule, so a document whose keys are not namespaced is **preserved** and
reported as deprecated rather than rejected; strict conformance (below)
turns the same finding into an error.

## Conformance claims and strict mode (Sections 4.1, 6.1, 6.6 and 11)

Four document-level terms describe the document rather than the
AddressSpace, so none of them becomes a Node and all of them survive a
round trip verbatim through the residue mechanism:

| Term | Where | What is checked |
|---|---|---|
| `uav:bindingVersion` | TD/TM root | a `<major>.<minor>` revision string (Section 4.1) |
| `uav:profile` | TD/TM root | a non-empty array of `WoT-<name>` conformance claims |
| `uav:eventSelectClauses` | event affordance | a non-empty ordered list of clauses carrying exactly `tm:ref` and a relative `uav:browsePath`, with no two clauses of the final selection materializing the same `data` member path (Section 6.1) |
| `tm:ref` | event affordance and select clause | a document URI with an optional RFC 6901 JSON Pointer resolving, in the local document set, to an EventType definition that carries `uav:eventType`, a portable `uav:id`, an object `data` schema and no selection of its own (Section 6.1) |
| `uav:minimumSecurity` | `auto` security scheme | `uav:securityMode`, `uav:securityPolicy`, or both, and no other member (Section 5.7.1) |

### Authoring a claim and processing one are different acts

Section 4.1 states the two checks side by side and makes them deliberately
different, and `WotNodeSetConverterOptions.AuthoringValidation` selects
between them:

| | Consumer (the default) | `AuthoringValidation = true` |
|---|---|---|
| `uav:bindingVersion` | **shall** be `<major>.<minor>`; a well-formed value this library does not implement is a `UnsupportedBindingRevision` **warning** and is preserved | **shall** name a revision this Binding publishes (`1.0` or `1.1`); anything else is an **error** |
| `uav:profile` | **shall** be a non-empty array of `WoT-<name>` strings; an entry Section 11 does not define is an `UnrecognizedConformanceClaim` **warning** and is preserved | **shall** name only units and profiles Section 11 defines |
| `unit` carrying a quantity kind | a `QuantityKindInUnit` **warning**; the authored value is preserved and never reinterpreted | an **error** (Section 6.4) |

A consumer that rejected a document for naming a revision or a unit it
does not know would be refusing to read a document that is syntactically
valid, whose known terms it understands, and whose unknown terms it is
already required to carry — which is the failure that makes a vocabulary
unextendable. A malformed claim stays an error in every mode: the
syntactic rule is what a consumer enforces, and it enforces it always.

Both published revisions are read here. `WotBindingConformance.SupportedRevisions`
names `1.0` and `1.1`, and the two places revision 1.0 differs — unnamespaced
opaque keys (Section 6.6) and a quantity kind in `unit` (Section 6.4) — are
reported as deprecated and preserved rather than rejected.
`Opc.Ua.Wot.WotUnitMigration.MoveQuantityKinds` is the opt-in helper that moves
such a value to `qudt:hasQuantityKind`; it never invents an engineering unit in
the vacated member, because none is recoverable from a quantity kind, and it
leaves a value alone where the affordance already states a *different* quantity
kind.

`WotNodeSetConverterOptions.ConformanceMode` chooses how strictly the rest
is held:

- **`Permissive`** (the default) processes what it understands and
  preserves the rest. An unknown `uav:` term is carried unchanged as
  residue rather than reported, and an unnamespaced opaque key is a
  warning. This is what Sections 4.1, 6.6, 9.4 and 10.2 require of a
  consumer.
- **`Strict`** additionally reports a `uav:` term this revision does not
  define, an opaque object that breaks the key or bound rules, and —
  where `RequiredConformance` names units or profiles — a claim the
  document does not make. Claiming a profile claims every unit it names,
  so `WoT-Modeller` satisfies a requirement of `WoT-EventMapping`.

Strict mode is for authoring and conformance testing: a misspelled term
should fail there rather than travel silently. It is never the default,
because a consumer is not allowed to reject a document for carrying a
term it does not know.

The revision this library implements, the Section 11 name set, the
opaque bounds and the security strength orders are stated once, in
`Opc.Ua.Wot.WotBindingConformance`; the select-clause term, its shape rules,
its member-naming rule and its documented `BaseEventType` default are stated
once, in `Opc.Ua.Wot.WotEventSelectClauses`. `WotBindingConformance.VocabularyTerms`
is the complete set of 113 `uav:` IRIs the published `@context` mints:
`IsKnownTerm` answers for the 100 a document spells with the prefix, and
`ScopedTerms` / `IsScopedTerm` for the 13 a scoped context mints under a short
member name (`namespaceUri`, `unitId`, `minimum`, `sha256` and the rest).

### The `uav:nodes` record grammar is not vocabulary

The member names inside `uav:nodes` — `nodeClass`, `nodeId`, `browseName`,
`references` and the rest — are the lower-camel-case spellings of the UANodeSet
XSD field names. They expand in the record-grammar namespace
`WotBindingConformance.NodesVocabularyNamespace`
(`http://opcfoundation.org/UA/WoT-Binding/nodes/`) rather than in the term
namespace, because they are a versioned record format and not vocabulary that
outlives it. Keeping them apart is also what stops a grammar token from
colliding with a class annotation of the same spelling: a node record's
`dataType` member holds the ExpandedNodeId of a Variable's DataType and is not
the NodeClass annotation `uav:dataType`, and a reference record's
`referenceType` member is not `uav:referenceType`. Strict conformance skips both
structured projections whole, so a record member name is never reported as an
unknown term.

### Generated documents state the revision they were generated against

Section 4.1 requires a *generator* to state `uav:bindingVersion`, because a
generator — unlike a hand author — always knows which revision it emitted. Every
document `FromNodeSet` produces therefore carries
`uav:bindingVersion: "1.1"`. An authored claim wins over that stamp: a document
that declared `1.9` round-trips as `1.9`, because a claim a consumer shall
preserve is not one it may overwrite. A claim that agrees with the stamp is
re-derived rather than also carried as residue.

### NodeClass and ReferenceType vocabulary

The NodeClass annotations and ReferenceType Attributes are:

| Term | Where | What it says |
|---|---|---|
| `uav:referenceType` | `@type` at TM root | the document projects a ReferenceType Node (Sections 5.2 and 6.2.1) |
| `uav:dataType` | `@type` at TM root | the document projects a DataType Node, whose definition travels in `uav:dataTypeDefinitions` |
| `uav:inverseName` | TM root with `uav:referenceType` | the OPC 10000-3 `InverseName`, in the document's default locale |
| `uav:symmetric` | TM root with `uav:referenceType` | the OPC 10000-3 `Symmetric` flag, `false` where absent |

A `@type` carries **at most one** NodeClass annotation — a Node has exactly one
NodeClass — and a second is a `NodeClassAnnotationConflict` error. A document
that sets `uav:symmetric: true` **shall not** carry `uav:inverseName`, because a
symmetric Reference reads the same in both directions; that, and either term on
a document that projects no ReferenceType, is a
`ReferenceTypeProjectionInvalid` error.

WoT Binding 1.1 defines neither `uav:severity` nor `uav:isEvent`. Event identity
comes from the `@type: uav:eventType` annotation alone, and `Severity` is a field
of an occurrence carried by the notification data schema rather than affordance
metadata. Generated documents emit neither member, nothing is synthesized from
either, and permissive conversion preserves either unknown member as ordinary
residue. Strict authoring reports either as an unknown term.

## Engineering units, ranges and scaling (Sections 6.4 and 6.4.1)

`unit`, `uav:unitProperty`, `uav:engineeringUnits`, `minimum`/`maximum` and
`uav:instrumentRange` are **mapped**, not carried: each names a Property Node
of an `AnalogUnitType` or `AnalogItemType` Variable, and both directions
materialize it.

Five of the `uav:` terms — `uav:engineeringUnits`, `uav:unitProperty`,
`uav:instrumentRange`, `uav:scaleFactor` and `uav:decimalPlaces` — describe the
Variable a **property affordance** projects, and are rejected anywhere else. The
role is decided once, at the document root: the members of the root `properties`
map are property affordances, and everything below one — a member of an action's
`input`, of an event's `data`, or of a property affordance's own DataSchema —
rejects all five. A field of a payload is not a Variable, so it carries no
`EngineeringUnits` Property for a unit to describe, and a `properties` map deeper
in a document is a DataSchema member map rather than an affordance map however it
is spelled.

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
`descriptions` maps as well. The plural member is **authoritative**: it carries
every locale the source had, and the singular member is the default projection a
consumer that knows nothing of the plural members reads. Where the plural member
has an entry for the document's default locale the singular member is that
entry, so the two never disagree. Where it has **no** such entry — a Node
authored in the plant's language and read against another default locale — the
plural member is still written in full and the singular member carries the entry
whose BCP 47 tag is first in ascending Unicode code-point order (Annex G.3), as
a display fallback that asserts no locale. The document does not claim that text
is written in the default locale, and nothing is pushed into the exceptional
`uav:nodes` projection to say something the plural member already says.

Asserting no locale is a claim a JSON-LD reader has to be told about. `title` and
`description` are terms of the W3C Thing Description context, and a `@context`
that declares `@language` tags every unqualified value with it - so a German
singular member would expand as English text. Where any projected text states no
entry for the document's default locale, the generated `@context` therefore
carries **one** further entry re-declaring the two terms with `"@language": null`.
It is written only where the document needs it: adding it unconditionally would
strip the language tag from every document this library writes. Being derived
from the projected Nodes it is re-derivable and is not also captured as residue;
an author's own override of the same terms says something different and is kept.

The same problem has a different answer inside `uav:engineeringUnits`. Section
6.4.1 mints `displayName` and `description` there as **short members under a
type-scoped context**, so a root-level override cannot reach them: the scoped
context is entered on that object and nowhere else. Where the EUInformation's
text is not in the document's default locale, the object therefore carries its
own node-local `@context` re-declaring `displayName` as `uav:unitDisplayName`
and `description` as `uav:unitDescription`, each with `"@language": null`.
`namespaceUri` and `unitId` are short members of that same scoped context, which
is why the generated document names the Binding context itself
(`http://opcfoundation.org/UA/WoT-Binding/v1.1/opc-ua-wot-binding.context.jsonld`)
alongside the W3C one: a short member is a term only while the context defining
it is in scope, and a document that named only the `uav` prefix would expand
those members to nothing. The identity is version-pinned because a document
states which revision it was written against, and a context that moves under a
document is a document whose meaning changed without it being edited.

**WoT to NodeSet.** A plural member becomes one `LocalizedText` per entry. The
entry written first — the one the Node's own attribute carries — is the
default-locale entry where the map has one and the code-point-first entry
otherwise, which is the same entry the singular member carries, so the round
trip is stable. A singular member alone becomes one `LocalizedText` tagged with
the document's declared `@language`, or untagged where the context declares none
— the form a UANodeSet writes when it names one language without saying which.

The same selection is used wherever a term reduces a `LocalizedText` to one
string: a ReferenceType's `uav:inverseName`, and the `displayName` and
`description` of `uav:engineeringUnits`. The `unit` member of the annotated
Variable is taken in that same locale, so a multi-locale `EUInformation` states
one text in both places instead of falling back to preservation because the two
disagree.

The mapping applies to the root, to property, action and event affordances, to
event fields, to `Method` argument descriptions, and to DataType definitions and
their structure and enumeration fields. A field's `DisplayName` maps to `title`.

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
Call supplies all of them. Each member carries the WoT type members that stand for its DataType, the
definitive `uav:mapToType`, `uav:valueRank`, any `uav:arrayDimensions` and its
`Description`. An argument Property represented by the schemas is not also
emitted as a sibling property of the Thing. A Property whose value cannot be
decoded remains a sibling so no Node is lost. The readable schemas state what
the arguments are and not which Nodes hold them, so the exact NodeId and
attributes of the argument Properties travel in the `uav:nodes` preservation
projection.

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

### `uav:componentOf` and its `ua:ComponentOf` alias

Section 9.1 spells the parent-placement relation `uav:componentOf` and declares `ua:ComponentOf` as an alias of it. Both
spellings are accepted wherever the relation is read, and they are treated as
the same term rather than as a term and a lookalike: the alias reads as a
compact model name whose local part is the InverseName of `HasComponent`, so it
is intercepted as a binding term instead of being realized a second time as a
generic inverse typed link.

This implementation writes `uav:componentOf` and accepts but does not emit the
alias, keeping one relation in one spelling on output.

### Projection documents and the OPC 10100-1 v1.02 asset surface

Two neighbouring subjects are documented elsewhere so they are stated once:

- A **projection document** declares affordances instead of defining them, and
  Section 12.5 closes the set of members it may annotate beside `tm:ref`. See
  [Projection documents and the View NodeClass](WoTConnectivity.md#124-projection-documents-and-the-view-nodeclass).
- The **OPC 10100-1 v1.02 asset surface** (`CreateAsset`, `WoTFile` upload, the
  `AssetRegistry` POCO reader) is a separate code path governed by that
  specification. Nothing on this page describes it. See
  [The 1.02 asset surface](WoTConnectivity.md#126-the-102-asset-surface).
