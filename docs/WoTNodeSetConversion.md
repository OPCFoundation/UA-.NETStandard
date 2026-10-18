# WoT / NodeSet conversion

`Opc.Ua.Wot.WotNodeSetConverter` converts OPC UA NodeSet2 documents to
WoT Thing Models / Thing Descriptions and materializes WoT documents back
to NodeSet2. The sections of the conversion guidance live in
[WoT bindings and conversion](WotBindings.md); the anchors below preserve
existing links.

For SDK callers, `ToNodeSetWithResolversAsync` converts a complete
`WotDocumentSet` with caller-supplied thing/schema resolution while preserving
set-local precedence. It performs no transport I/O itself.

## Contents

<a id="contents"></a>
<a id="which-specification-revision-this-tracks"></a>
<a id="conformance-claims-and-strict-mode-sections-41-61-66-and-11"></a>
<a id="authoring-a-claim-and-processing-one-are-different-acts"></a>
<a id="the-uavnodes-record-grammar-is-not-vocabulary"></a>
<a id="generated-documents-state-the-revision-they-were-generated-against"></a>
<a id="nodeclass-and-referencetype-vocabulary"></a>
<a id="model-and-platform-vocabulary-section-6"></a>
<a id="opaque-object-bounds"></a>

- [Revision, claims, vocabulary, and bounds](WotBindings.md#conformance-and-authoring-validation)

<a id="importable-output"></a>
<a id="verified-linked-document-sets"></a>
<a id="resolving-a-stated-selection-needs-the-asynchronous-conversion"></a>

- [Importable output and verified document sets](WotBindings.md#conversion-entry-points)

<a id="portable-identity-and-preservation-semantics"></a>
<a id="generated-nodeids-follow-annex-g1"></a>
<a id="preservation-digests-and-the-two-things-that-can-be-measured"></a>

- [Portable identities, preservation, and digests](WotBindings.md#identity-and-preservation)

<a id="wot-to-nodeset-defaults"></a>
<a id="modelling-rules-and-the-two-placeholder-identifiers"></a>
<a id="datatype-definitions-section-611"></a>
<a id="valuerank-and-arraydimensions-sections-7-and-91"></a>
<a id="method-arguments-section-91"></a>

- [DataTypes, ranks, arguments, and defaults](WotBindings.md#datatypes-ranks-arguments-and-model-vocabulary)

<a id="engineering-units-ranges-and-scaling-sections-64-and-641"></a>
<a id="localized-text-section-911"></a>

- [Engineering units, ranges, and locales](WotBindings.md#engineering-units-ranges-and-localized-text)

<a id="component-template-declarations"></a>
<a id="unmapped-reference-vocabulary-is-residue"></a>
<a id="referencetypes-and-relations-sections-512-53-and-62"></a>
<a id="uavcomponentof-and-its-uacomponentof-alias"></a>

- [Types and relations](WotBindings.md#resolving-a-relation-companion-referencetypes)

<a id="condition-events-derive-from-their-conditiontype"></a>
<a id="event-fields-come-from-the-type-not-only-from-the-document"></a>

- [Event selection](WotBindings.md#event-field-selection)
- [Condition runtime and mapping](WotBindings.md#projected-methods-events-and-conditions)

<a id="projection-documents-and-the-opc-10100-1-v102-asset-surface"></a>

- [Projection usage](WoTConnectivity.md#authoring-projection-views)
- [WoT-Con 1.02 compatibility](WoTConnectivity.md#wot-con-102-compatibility)
