# CTT (Compliance Test Tool) script defects

This document reports defects found in the **OPC UA Compliance Test Tool** test
scripts (not in the server under test) while working through issue #3960 against
the OPC Foundation .NET reference server. Each entry describes the defect, why
the reference server's behaviour is correct per the OPC UA specification, and the
recommended fix to the CTT script.

Paths below are relative to the CTT installation
(`.../Compliance Test Tool/ServerProjects/Standard/`). Line numbers refer to the
CTT build used for the run (`NewCTT2`), UA 1.05.

---

## 1. Base Info State Machine Instance — `GeneratesEvent` target validation uses the wrong helper

**Test:** `maintree/Base Information/Base Info State Machine Instance/Test Cases/001.js`, line 39
**Helper:** `library/Information/InformationModelUtilities.js` — `IsNodeOfTypeOrSubType` / `GetTypeDefinitionOfNode`
**Observed error:** *"Step 1: TargetNode 'i=2311' of GeneratesEvent reference is not of type BaseEventType or a subtype."* (7 occurrences)

### What the test does

For every StateMachine instance, 001.js walks the instance's type chain looking
for a `GeneratesEvent` reference, then validates the reference **target** with:

```js
if( InfoUtils.IsNodeOfTypeOrSubType( { Node: referencesResults[rR].ReferenceNodeId, Type: new UaNodeId( Identifier.BaseEventType ) } ) ) { ... }
else { addError( "... TargetNode '" + ... + "' of GeneratesEvent reference is not of type BaseEventType or a subtype." ); }
```

`IsNodeOfTypeOrSubType` resolves the **HasTypeDefinition** of the supplied node
(`GetTypeDefinitionOfNode` browses/looks up a forward `HasTypeDefinition`
reference) and then checks whether *that type definition* is `BaseEventType` or a
subtype.

### Why this is a CTT defect

Per **OPC UA Part 3 §7.15 (GeneratesEvent ReferenceType)**, the **TargetNode of a
`GeneratesEvent` reference shall be an `ObjectType`** (the EventType that may be
generated). An `ObjectType` node has **no `HasTypeDefinition` reference** — only
`Object`/`Variable` *instances* do (Part 3 §7.2). Consequently
`GetTypeDefinitionOfNode(i=2311)` returns an empty NodeId and
`IsNodeOfTypeOrSubType` returns `false` for **every** spec-compliant
`GeneratesEvent` reference. The check can never pass.

The reference server is correct: `StateMachineType` (i=2299) and
`FiniteStateMachineType` (i=2771) declare `GeneratesEvent → TransitionEventType
(i=2311)` per **Part 5 §B.4.5 / the StateMachine model**, and
`TransitionEventType` correctly has `HasSubtype`-inverse to `BaseEventType`
(i=2041). (Verified live: browsing i=2311 inverse `HasSubtype` returns
`BaseEventType`; browsing i=2311 forward `HasTypeDefinition` returns nothing.)

### Recommended CTT fix

Validate the `GeneratesEvent` **target** as a *type* node, not an *instance*.
Replace the `IsNodeOfTypeOrSubType` call with a direct subtype test of the target
against `BaseEventType`, e.g. the existing `IsSubTypeOfTypeHelper`:

```js
IsSubTypeOfTypeHelper.Execute( { ItemNodeId: referencesResults[rR].ReferenceNodeId, TypeNodeId: new UaNodeId( Identifier.BaseEventType ) } );
if( referencesResults[rR].ReferenceNodeId.equals( new UaNodeId( Identifier.BaseEventType ) ) || IsSubTypeOfTypeHelper.Response.IsSubTypeOf ) { /* pass */ }
```

(i.e. "the target *is* `BaseEventType` or a subtype of it", walking `HasSubtype`,
not `HasTypeDefinition`).

---

## 2. Historical Access Read Raw — `initialize.js` accesses `ArrayItems` without the guard used everywhere else

**Test:** `maintree/Historical Access/Historical Access Read Raw/Test Cases/initialize.js`, line 28
**Observed error:** *"Result of expression 'CUVariables.ArrayItems' [undefined] is not an object." (TypeError, lineNumber 28)* (surfaced in Historical Access Read Raw and, via the shared post-test handler, in other CUs)

### What the test does

`initialize.js` builds two monitored-item lists from settings:

```js
CUVariables.Items      = MonitoredItem.fromSettings( Settings.ServerTest.NodeIds.Static.HAProfile.Scalar.Settings ); // line 7
CUVariables.ArrayItems = MonitoredItem.fromSettings( Settings.ServerTest.NodeIds.Static.HAProfile.Arrays.OneD );     // line 8
```

It then registers a post-test reset handler:

```js
CUVariables.ResetItems = function() {
    for( var i=0; i<CUVariables.Items.length; i++ ) CUVariables.Items[i].ContinuationPoint = null;
    for( var i=0; i<CUVariables.ArrayItems.length; i++ ) CUVariables.ArrayItems[i].ContinuationPoint = null; // line 28
};
Test.PostTestFunctions[0] = CUVariables.ResetItems;
```

### Why this is a CTT defect

When the `HAProfile.Arrays.OneD` setting configures **no** 1-D array history
nodes, `MonitoredItem.fromSettings(...)` yields a value without a usable
`.length`, so `CUVariables.ArrayItems.length` at **line 28** throws a `TypeError`.
Every other use of `ArrayItems` in the same file **guards** this case:

```js
if( isDefined( CUVariables.ArrayItems.length ) && CUVariables.ArrayItems.length > 0 ) { ... } // lines 78, 85, 119
```

Only the `ResetItems` handler (line 28) omits the guard, so it is an internal
inconsistency in the script — the handler runs after **every** test in the CU
(and leaks into other CUs via `Test.PostTestFunctions`), producing a large,
misattributed error count. This is independent of the server: a server that
exposes no 1-D array historizing nodes is legal, and the CU already accounts for
that everywhere except line 28.

### Recommended CTT fix

Guard line 28 the same way lines 78/85/119 do:

```js
CUVariables.ResetItems = function() {
    for( var i=0; i<CUVariables.Items.length; i++ ) CUVariables.Items[i].ContinuationPoint = null;
    if( isDefined( CUVariables.ArrayItems ) && isDefined( CUVariables.ArrayItems.length ) ) {
        for( var i=0; i<CUVariables.ArrayItems.length; i++ ) CUVariables.ArrayItems[i].ContinuationPoint = null;
    }
};
```

---

## 3. HA Aggregate helper — `GetRequestEntry` dereferences a null `requestEntries` (defence-in-depth)

**Helper:** `library/ServiceBased/AttributeServiceSet/HistoryRead/HAAggregateHelper.js`, line 1791
**Observed error:** *"Result of expression 'requestEntries' [null] is not an object." (TypeError, lineNumber 1791)* — cascaded across **every** `Aggregate – *` unit (~1150 occurrences)

### What the test does

`GetRequestEntry` reads a cached raw-data entry:

```js
this.GetRequestEntry = function ( requestEntries, requestDefinition ) {
    ...
    if ( definition == this.AggregateRequestDefinition.StartRequest ) {
        requestEntry = requestEntries.StartEntry; // line 1791 — throws when requestEntries is null
    }
    ...
};
```

Callers pass `requestEntries` from the raw-data cache:

```js
var requestEntries = Test.AggregateTestData.RawDataCache.ItemMap.Get( itemName ); // may be null
var requestEntry   = this.GetRequestEntry( requestEntries, requestDefinition );
```

### Root cause and why a guard is still warranted

The **primary** cause of the null cache in run `NewCTT2` was a *server* defect
(the aggregate test node's `RolePermissions` denied the CTT's authenticated user
`HistoryRead`, so the initial raw read returned `BadUserAccessDenied` and the
cache was never populated) — that server bug has been fixed separately. However,
`GetRequestEntry` should still fail **gracefully**: if the raw-data cache lookup
yields `null`/`undefined` (for any reason — an unsupported item, an empty archive
window, a service error), the helper should emit a clear `addError`/`addWarning`
and return, rather than throwing an unhandled `TypeError` that aborts the whole
CU. A single missing bounding read currently masks the real result of ~40
aggregate units.

### Recommended CTT fix

Guard the argument at the top of `GetRequestEntry`:

```js
this.GetRequestEntry = function ( requestEntries, requestDefinition ) {
    if ( !isDefined( requestEntries ) ) {
        addError( "GetRequestEntry(): no raw-data cache entry available (check the initial HistoryRead result / permissions)." );
        return null;
    }
    ...
};
```

---

## Notes on items that are **not** CTT defects

* **Auditing Connections** / **WriteMask** / **Historical Access** / **Aggregates**
  `BadUserAccessDenied`: these were a *server* defect — the reference server's
  `Scalar_Static_Int32` exposed `RolePermissions` for Anonymous + SecurityAdmin
  only, denying the CTT's authenticated `user1`. Fixed server-side by granting
  `AuthenticatedUser` (Browse|Read|Write|ReadHistory|ReadRolePermissions).
* **`initialize.js:57` `Value.clone()`**: a symptom of the denied HistoryRead
  above (the item `.Value` was empty for denied nodes); expected to clear once the
  server permission fix lets the initial read succeed. Re-confirm on the next CTT
  run.
