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
| Thing Description root type information | **Default** | Root is a `UAObject` with `HasTypeDefinition` to `BaseObjectType` (`i=58`). |
| Root `description` | **Default** | No `Description` field is materialized. |
| Property affordance `uav:browseName` | **Default** | The affordance map key is used as the local name and BrowseName `1:<key>`. |
| Property affordance `uav:id` | **Default** | Deterministic NodeId `ns=1;s=<rootLocal>/<propertyLocal>`. |
| Property DataSchema `type` or an unrecognized `type` | **Default** | OPC UA `BaseDataType` (`i=24`). `type: object` maps to `BaseObjectType` (`i=22`) and emits an unsupported-schema warning; `type: array` currently falls back to `BaseDataType` with the same warning, which is a deliberately conservative but coarse default. |
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
| Event type abstraction/supertype | **Default** | Event affordances materialize as non-abstract `UAObjectType` nodes with inverse `HasSubtype` to `BaseEventType` (`i=2041`) and a root `GeneratesEvent` reference. |
| `uav:modellingRule` on a property or action | **Default** | No `HasModellingRule` reference is materialized. |
| `uav:hasComponent` / `uav:componentOf` entry has no matching typed ReferenceType link | **Default** | `HasComponent` is used for the component reference. |
| Binding link has no resolvable model-name relation and no `uav:refId` | **Default** | `uav:componentModel` maps to `HasComponent`; other generic binding relations map to `Organizes`. |
| Reference link points to another Thing by URI and no resolver is supplied or the resolver cannot find `uav:id` | **Default** | The reference is omitted and a warning diagnostic is emitted; no placeholder NodeId is generated. |
| Invalid namespace-qualified `uav:id` / `uav:browseName` syntax or unbound compact-name prefix | **Fails** | An error diagnostic is emitted; `ToNodeSet` throws even though synthesis continues far enough to collect diagnostics. |
| Event affordance says `@type: uav:eventType` and `uav:isEvent: false` | **Fails** | `EventAnnotationConflict` error; the two terms must not contradict each other. |
