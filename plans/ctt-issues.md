# CTT (Compliance Test Tool) conformance findings

This document records CTT script defects and server-side findings discovered while
working the ctt against the OPC Foundation .NET reference server (v2). Each
entry classifies the observed behavior, records the available evidence, and identifies
the appropriate CTT corrective action.

Paths below are relative to the CTT installation
(`.../Compliance Test Tool/ServerProjects/Standard/`). Line numbers refer to the
CTT build used for the run (UA 1.05.06 Script 1.05.513) .

---

## 1. Base Info State Machine Instance — `GeneratesEvent` target validation uses the wrong helper (https://mantis.opcfoundation.org/view.php?id=11248)

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

## 2. Historical Access Read Raw — `initialize.js` accesses `ArrayItems` without the guard used everywhere else (https://mantis.opcfoundation.org/view.php?id=11249)

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

## 5. HA Aggregate helper — multi-node path dereferences `possibleNodeId` without an `isDefined` guard (https://mantis.opcfoundation.org/view.php?id=11251)

`ServerProjects/Standard/library/ServiceBased/AttributeServiceSet/HistoryRead/HAAggregateHelper.js`,
`PerformMultipleNodeTest` (around line 1484), raises `possibleNodeId [undefined] is not an object`
(a JavaScript `TypeError`) roughly 100 times across the aggregate conformance units, aborting the
affected multi-node aggregate cases.

### What the test does

For the multi-node aggregate cases (`configObject.Items.length > 1`) the helper walks the raw-data
cache and, for every node referenced by a cached request entry, maps the cached position back to the
current test's variable list:

```js
for ( var nodeIndex = 0; nodeIndex < requestEntry.Nodes.length; nodeIndex++ ) {
    var originalItemIndex = requestEntry.Nodes[ nodeIndex ].Index;
    var possibleNodeId = variables.Items[ originalItemIndex ];   // may be undefined
    if ( itemLookup.Contains( possibleNodeId.NodeId.toString() ) ) {   // <-- throws here
        ...
    }
}
```

`originalItemIndex` is an index that was captured against the **full** variable set when the raw-data
cache was built, but `variables.Items` here is the **current** (potentially smaller / re-ordered)
per-configuration subset. When the cached index has no corresponding entry in `variables.Items`,
`possibleNodeId` is `undefined` and the immediate `possibleNodeId.NodeId.toString()` throws.

### Why this is a CTT defect

Every other place in the same helper that indexes `variables.Items` guards the lookup with
`isDefined(...)` before dereferencing (e.g. the single-node path). This one call site does not, so a
perfectly valid server address space (whose node ordering simply differs from the cache's captured
indices) makes the script throw instead of skipping the unmatched entry. The server returns no error
here — the failure is entirely inside the CTT script.

### Recommended CTT fix

Guard the dereference exactly as the sibling code paths already do:

```js
var possibleNodeId = variables.Items[ originalItemIndex ];
if ( isDefined( possibleNodeId ) && itemLookup.Contains( possibleNodeId.NodeId.toString() ) ) {
    ...
}
```

Alternatively, resolve the node through `itemLookup` by the cached NodeId rather than by positional
index, so the current-subset ordering is irrelevant.

---


* **Auditing Connections** (`Unable to Find Entry for ClientAuditEntryId`,
  `AuditValidationHelper.js:346`, ~1690 occurrences): the **server side is
  verified correct**. The reference server emits `AuditOpenSecureChannel`,
  `AuditCreateSession`, `AuditActivateSession` and `AuditCloseSession` events,
  each carrying `ClientAuditEntryId` copied from the request's
  `RequestHeader.AuditEntryId`; and per **Part 3 §8.55** the events are delivered
  to a subscriber holding `ReceiveEvents` on the event type (an `ObjectType`,
  universally accessible) and the Server source node (granted to
  `AuthenticatedUser` in CTT mode). A regression test —
  `AuditingOperationTests.AuditEventDeliveredToAuthenticatedUserAsync` — proves an
  `AuthenticatedUser` (`user1`, the CTT's main-session role) subscription actually
  **receives** the audit event. Because the CTT's `Test.Audit` collection does not
  find the entries despite confirmed server-side delivery, the residual failure is
  in the CTT-side audit collection/matching (its audit-monitoring subscription
  parameters, the `FindEntryVerbose` `whereClause`, or `ClientAuditEntryId`
  comparison / publish timing) — the CTT team should re-verify that path. This was
  *not* pinpointed to a single line and is therefore not listed as a defect above.

  ---


## `AliasName Hierarchy/002.js:80` references an undefined variable.** (https://mantis.opcfoundation.org/view.php?id=11262)
After the per-alias loop the success branch reads `TC_Variables.ListOfNodes.length`, but `ListOfNodes` is never
  assigned in this test (the results were stored in `TC_Variables.OutputArguments`), raising
  `Result of expression 'TC_Variables.ListOfNodes' [undefined] is not an object`. **Recommended CTT
  fix:** use `TC_Variables.OutputArguments.length` (the array actually populated at line 37), or track a
  running count of returned aliases.

## 7. Aggregate `Err-004.js` creates an unintended equal-time request when ProcessingInterval is blank (https://mantis.opcfoundation.org/view.php?id=11252)

**Tests:** every Aggregate Conformance Unit reuses
`maintree/Aggregates/Aggregate - Base/Test Cases/Err-004.js`.

**Observed in run 14:** 552 errors:

- 276 generic `HistoryRead.js` / `assertions.js` errors reporting per-node
  `BadInvalidArgument`;
- 276 explicit `HAAggregateHelper.js:1764` errors rejecting the same
  `BadInvalidArgument`.

The normal Base `001-01` / `002-01` comparisons correctly accept this response (the console log
contains hundreds of `Server and CTT have status codes of BadInvalidArgument` confirmations).
Only `Err-004.js` rejects it.

### Why this is a CTT/configuration defect

`Err-004.js:25` calls `PerformExpectedErrorTest`. The helper constructs the request range from the
configured Aggregate `ProcessingInterval`, but the run's CTT configuration leaves that setting blank.
JavaScript coerces the blank value multiplied by ten to zero, so the helper sends
`StartTime == EndTime`.

OPC UA Part 11 §6.5.4.2 is explicit: when `StartTime` and `EndTime` are equal, the Server shall
return `Bad_InvalidArgument` because the request has no meaningful processed time domain. Per Part 4
§5.11.3.2, this is the per-node operation result while the HistoryRead ServiceResult remains Good.
The reference server therefore returns the required result; the error test accidentally combines its
intended invalid condition with a second invalid condition and then rejects the mandated status.

### Recommended CTT fix

Validate the setting before building the request in `HAAggregateHelper.js`:

```js
var interval = parseInt(
    Settings.ServerTest.NodeIds.Static.HAProfile.Aggregates.ProcessingInterval);
if (isNaN(interval) || interval <= 0) {
    interval = 1;
}
```

Alternatively, skip the test with a clear configuration error when a positive interval is missing.
Apply the same guard to `PerformMismatchTest`. The immediate configuration workaround is to set
Aggregate `ProcessingInterval` to a positive value.

## Historical Access Read Raw `004.js` rejects correct reverse ordering (https://mantis.opcfoundation.org/view.php?id=11263)

At lines 78, 91, and 105 the test uses:

```js
if (OPCF.HA.Analysis.Date.FlowsBackward(...)) result = false;
```

Those branches are specifically validating reverse reads, so `FlowsBackward(...) == true` is success,
not failure. Replace each predicate with:

```js
if (!OPCF.HA.Analysis.Date.FlowsBackward(...)) result = false;
```

Part 11 §6.5.3.2 requires raw values to be returned in the direction implied by StartTime/EndTime.

## Historical Access Read Raw `014.js` indexes a nonexistent second node result (https://mantis.opcfoundation.org/view.php?id=11264)

The test requests one node but lines 46 and 78 inspect `Response.Results[1]`. The intended check is the
second returned `DataValue` for the first node. Validate
`haItems[0].Value[1].StatusCode` (with length guards) and describe it as record 2, not result 2.

## Historical Access Read Raw `019.js` bypasses the CTT test harness (https://mantis.opcfoundation.org/view.php?id=11265)

The script invokes `readraw019()` directly while the normal `Test.Execute` wrapper is commented out.
Use:

```js
Test.Execute({ Procedure: readraw019 });
```

This ensures exceptions, result accounting, setup, and cleanup follow the same path as the other
Historical Access cases.

### `Err-013.js` describes an operation error as a ServiceResult

Reusing a consumed ContinuationPoint shall produce per-node
`BadContinuationPointInvalid`; the HistoryRead ServiceResult remains Good. Update the message to:

> HistoryRead test #3 expected a Good ServiceResult and
> `Results[0].StatusCode` `BadContinuationPointInvalid`.

This matches Part 11 §6.3 and Part 4 §5.11.3.2.

### `Err-019.js` uses an undefined loop variable in error messages

Lines 25 and 43 interpolate undeclared `i`. Use literal test numbers `#1` and `#2` (or define a
proper case index) so a failed assertion reports the actual case instead of throwing another
JavaScript error.


## 9. Node Management AddNodes — invalid reference and requested-NodeId CTT configuration


### `002.js` enables references that cannot add a Variable under the configured parent

`Node Management Add Node/Test Cases/002.js` loops every ReferenceType enabled under:

`/Server Test/NodeIds/NodeManagement/SupportedReferences`

and always adds a **Variable**, expecting `Good`. The project enabled all candidates. The 14 reported
`BadReferenceNotAllowed` results correspond to the 13 non-hierarchical references plus `HasSubtype`:

`HasModellingRule`, `HasEncoding`, `HasDescription`, `HasTypeDefinition`, `GeneratesEvent`,
`AlwaysGeneratesEvent`, `FromState`, `HasCause`, `HasEffect`, `HasSubStateMachine`,
`HasTrueSubState`, `HasFalseSubState`, `HasCondition`, and `HasSubtype`.

OPC UA Part 4 §5.8.2 requires the new Node to be the target of a **HierarchicalReference**.
`BadReferenceNotAllowed` is therefore correct. `HasSubtype` is hierarchical but is a type-system
reference whose source and target must be compatible Type Nodes; the server now correctly rejects it
for the Variable instance created by this script.

**Recommended CTT project fix:** restrict the configured set to reference types compatible with the
configured parent and a Variable target—typically `Organizes`, `HasProperty`, and `HasComponent`.
Do not mark every known ReferenceType as supported merely because the server supports that
ReferenceType elsewhere in its information model.

### Node Management AddNodes `Err-008.js` tests duplicate NodeIds while client-specified NodeIds are disabled (https://mantis.opcfoundation.org/view.php?id=11266)

`Err-008.js` sends the same AddNodes item twice and expects the second call to return
`BadNodeIdExists`. In this project `/NodeManagement/RequestedNodeId` is disabled, so
`CUVariables.RequestedNewNodeId()` returns a null NodeId. Each call legitimately asks the server to
allocate a fresh NodeId; the second `Good` result represents a different node and is spec-correct.

**Recommended CTT fix:** skip `Err-008.js` when client-specified NodeIds are disabled, or enable the
setting and configure a concrete NodeId in a writable namespace owned by a NodeManager that supports
NodeManagement before testing duplication.

## 10. Run 18 Historical Access and Attribute script/configuration defects

### Historical Access `012.js` expects `BadIndexRangeNoData` at the wrong level (https://mantis.opcfoundation.org/view.php?id=11267)

The test reads historized array values with a syntactically valid IndexRange that is outside the
array bounds. The server returns:

- `HistoryReadResult.StatusCode = Good`;
- each returned `DataValue.StatusCode = BadIndexRangeNoData`.

`012.js` instead expects `Results[0].StatusCode = BadIndexRangeNoData`, producing two errors.
OPC UA Part 11 §6.4 applies the IndexRange independently to each historical value; the HistoryRead
operation succeeds while values for which no indexed data exists carry `BadIndexRangeNoData`.

**Recommended CTT fix:** require the per-node result to be Good, decode `HistoryData`, and assert
`BadIndexRangeNoData` on each affected `DataValue.StatusCode`.

### Historical Access `Err-012.js` uses a non-historizing node for an access-denied test

The configured node does not support history, so the server returns
`BadHistoryOperationUnsupported` before any history authorization check can produce
`BadUserAccessDenied`.

**Recommended CTT project fix:** configure a node that is historizing and readable by an authorized
identity but explicitly denies HistoryRead to the identity used by this case. A test cannot validate
access denial with a node that has no supported history operation.

### Attribute array helpers omit `NodeId[]` conversion (https://mantis.opcfoundation.org/view.php?id=11261)

Run 18 records the same JavaScript exception for:

- Attribute Read `032.js`;
- Attribute Read `034.js`;
- Attribute Write Index `007.js`.

`UaNodeId.GuessType(...)` correctly identifies BuiltInType `NodeId (17)`, but the generic CTT array
conversion/generation helper has no NodeId branch and throws:

> Built in type not specified or detectable within the parameter: NodeId (17)

**Recommended CTT fix:** add NodeId-array support to both directions:

- decode with the appropriate `toNodeIdArray()` accessor;
- generate/populate a `UaNodeIds` collection and set it with the NodeId-array Variant setter.

As with the existing StatusCode-array defect (`026.js`/`036.js`), a generic built-in array test must
support every configured built-in type or explicitly exclude unsupported types before executing.


### The Core Structure comparison uses a UA 1.04 reference model for a UA 1.05 server (https://mantis.opcfoundation.org/view.php?id=11268)

The run identifies the server as UA 1.05.006 but compares its address space with a UA 1.04 `NodeSetFile`. That can produce false additions, removals, modelling-rule, DataType, and ValueRank errors for nodes introduced or changed after 1.04.

**Recommended CTT fix:** select a reference NodeSet whose specification version matches the server model under test. At minimum, the CTT must not report a 1.05 node as non-conformant solely because it differs from the bundled 1.04 reference.

### `ConformanceUnits` is tested as a scalar instead of `QualifiedName[]`(https://mantis.opcfoundation.org/view.php?id=11269)

The current standard node `i=24101` (`Server.ServerCapabilities.ConformanceUnits`) has `DataType=QualifiedName` and `ValueRank=1`; its value is a one-dimensional `QualifiedName` array. Run 19 expects a scalar QualifiedName and reports the conformant array value as an error.

**Recommended CTT fix:** update the expected ValueRank to one dimension and decode/compare a `QualifiedName[]` value using the matching UA 1.05 NodeSet definition.

### Monitor Value Change V2 `042.js` does not identify the missing item and does not guarantee its write changes the value

`042.js` creates 19 matrix monitored items with IndexRange `1,1,...`, writes each whole matrix, and only reports the aggregate count (`Expected 19 but got 18`). It never reports the missing ClientHandle/NodeId, so the result cannot identify which data type failed. The repository's `MatrixIndexRangeReportsEveryChangedTypeAsync` creates the same 19 configured monitored items, writes a representably different selected element for every matrix type, and receives every ClientHandle.

The script also computes `indexValue` from itself before initialization at line 243 (`var indexValue = Dimensions[u] * (indexValue + 1)`), so value verification is invalid once the count assertion passes. In addition, the configured deterministic Double and Float matrix elements can be very large (for example approximately `-8.19E+24` and `-1.03E+33`); adding one does not necessarily produce a representably different floating-point value. **Recommended CTT fix:** initialize the flat index, record and report missing ClientHandles, and verify that `UaVariant.Increment` actually changed each selected value (use the next representable floating-point value or a known different finite value).


### Remaining A&C script findings

* Alarm `Test_002.js` still evaluates Retain from only the main event's Active/Acked/Confirmed fields. The focused cycle confirms `Retain=true` after Confirm while the prior active branch remains outstanding; this is the Part 9 branch case already documented above, not a stale server value.
* Alarm `Test_004.js` invokes the global `ReadHelper` synchronously from its alarm callback and receives a client-side `BadInvalidState`. An independent client/server regression resolves every AlarmCondition `InputNode` from the event model and reads every referenced source with `Good`. **Recommended CTT fix:** queue the Read outside the callback or use a Read helper/session that is valid on the alarm thread.
* Enable `Test_002.js` calls `collector.AddMessage(testCase, category, conditionId, reason)` even though `AddMessage` accepts only three arguments. JavaScript drops the fourth argument, producing the empty `Error: ns=...` entries and hiding whether EnabledState, Retain, Event Time, or TransitionTime failed. The representative type- and instance-method disable/enable cycle passes with correct EnabledState and Retain. **Recommended CTT fix:** concatenate `conditionId` and `reason` into the third argument, then rerun before attributing the generic `Error validating variables for state ConditionDisabled`.

### Core Structure reads TransactionDiagnostics as if a transaction had already occurred (https://mantis.opcfoundation.org/view.php?id=11256)

Core Structure `001.js` reports `BadOutOfService` for `i=32337` through `i=32340` as a datatype/read failure. OPC UA Part 12 §7.10.17 explicitly states: *"If no transaction has started the values of all Variables have a status of Bad_OutOfService."* The server implements that requirement and `TransactionDiagnosticsReportBadOutOfServiceBeforeAnyTransactionAsync` verifies every TransactionDiagnostics variable. **Recommended CTT fix:** accept `BadOutOfService` while walking TransactionDiagnostics before the first transaction, or create a completed transaction before validating values.

### SemanticChange `001.js` decodes the `Changes` array as one ExtensionObject (https://mantis.opcfoundation.org/view.php?id=11093)

The test receives a SemanticChange event, then calls `EventFields[0].toExtensionObject()` at line 275. OPC UA Part 5 Table 174 defines `SemanticChangeEventType.Changes` as `SemanticChangeStructureDataType[]` with ValueRank 1, not a scalar ExtensionObject. The scalar conversion therefore returns null and the script throws before validating the event. **Recommended CTT fix:** decode the field as an ExtensionObject array and convert each element to `SemanticChangeStructureDataType`.

### Historical Access Read Raw `013.js` reuses continuation points after changing IndexRange (https://mantis.opcfoundation.org/view.php?id=11257)

`013.js` reuses the same `HistoryReadValueId` objects for three different IndexRanges and does not clear or consume the ContinuationPoints returned by the preceding call. With seven configured matrix nodes, the two later iterations produce the observed 14 `BadContinuationPointInvalid` results. A HistoryRead continuation point is opaque state for the original request (Part 11 §6.4.3.3); changing IndexRange while resubmitting it invalidates that state. **Recommended CTT fix:** fully drain/release every continuation point before the next IndexRange, or create fresh `HistoryReadValueId` objects with empty ContinuationPoints for each independent request.

### Security User Name Password `015.js` requires PolicyId uniqueness across unrelated endpoints (https://mantis.opcfoundation.org/view.php?id=11258)

The script flattens `UserIdentityTokens` from every `EndpointDescription` into one array and compares `PolicyId` globally. OPC UA Part 4 §7.36.2.2 requires each `UserTokenPolicy` to have a unique `PolicyId` within the `UserIdentityTokens` array of one `EndpointDescription`; it does not require global uniqueness across all endpoints. The current reference server's live endpoints have unique PolicyIds within every endpoint. The script also uses `foundTokens[i]` instead of the token it just appended inside the nested loop at line 20. **Recommended CTT fix:** reset the seen-PolicyId set for each endpoint and validate only that endpoint's array.

### Aggregate failures

The remaining evidenced value families primarily expose CTT configuration/oracle differences rather than proving more server changes:

* The log prints `Requested Bad Data Entry - Bad Data Entry no found, using start data` 1,815 times. The current reference-server seed does contain a deterministic mixed-quality pattern (index modulo 10: one `BadDataUnavailable`, one `UncertainSubstituteValue`, eight Good) on every configured historical node, but the CTT project does not identify a `BadDataEntry`; `HAAggregateHelper.GetRequestEntry` silently substitutes `StartEntry`. The related `BadDataStartRequest` cases have no fallback and produce the 74 existing configuration exceptions. The project needs explicit Bad/Uncertain entry metadata instead of silently changing the requested scenario.
* The same seeded status timeline is applied to Int32, Float, Double, Boolean, and String nodes. Status-only aggregates therefore return the same DurationGood/Bad and PercentGood/Bad values for each data type. The log shows the CTT cached oracle matching the numeric nodes, then producing different values only for the Boolean/String nodes (for example server `PercentGood=83.193277...` for every type while the CTT changes to `82.608771...` and then `0`). This proves a CTT cached-history decode/oracle defect for non-numeric values; the server calculation cannot legitimately depend on the raw value type.
* Every Interpolative mismatch is the Int32 conversion `24` versus `23`; Float and Double match exactly. The TimeAverage and Total mismatches are likewise confined to Int32 nodes while Float and Double match. The server rounds the interpolated source-typed bounds to nearest; the CTT truncates. Part 13 defines the interpolation and result type but does not mandate the integer conversion convention.
* StartBound and EndBound support all source data types under Part 13 §5.4.2.3 and use Simple Bounding Values. The CTT cached oracle repeatedly returns `BadNoData` for valid Boolean and String bounds while the server returns the nearest valid value. Numeric Float/Double bounds match, with only the same Int32 rounding convention above.
* MinimumActualTime2 and MaximumActualTime2 server results use an eligible sloped End bound and timestamp it at EffectiveEndTime, as required by Part 13 §§5.4.3.17-.18 and §5.4.2.4. The CTT comparisons instead select an earlier raw value in those cases. Related `*2` comparisons also contain the non-numeric oracle and Int32 conversion differences above.

Run 21 therefore justifies the NumberOfTransitions fix above, not broad compatibility changes to the other aggregate calculators. AnnotationCount, Count, DeltaBounds, DurationBad, and the beginning of DurationGood still need a non-truncated value-bearing log. Tail-of-range Start/End and WorstQuality comparisons that differ only between `BadDataUnavailable` and `BadNoData`, or by the Partial bit, also remain open pending a focused request/raw-data trace.

### Durable Subscription `008.js` misspells `MoreNotifications` and does not drain the queue (https://mantis.opcfoundation.org/view.php?id=11259)

The script correctly checks `PublishHelper.Response.MoreNotifications` at line 35, but line 101 uses the misspelled `MoreNotifcations`. The drain loop therefore does not run when additional notification responses are queued, and the final Publish sees a notification that the script incorrectly calls unexpected. OPC UA Part 4 §5.14.5.2 permits subsequent responses when `moreNotifications=TRUE`. Lines 105-108 also omit braces, leaving `result = false` unconditional. **Recommended CTT fix:** use `MoreNotifications`, drain until it is false, then perform the no-more-data assertion with braces around the failure branch.

### Subscription Minimum 02 `020.js` accepts unrelated audit events (https://mantis.opcfoundation.org/view.php?id=11260)

The event MonitoredItem has SelectClauses but no WhereClause, so it accepts every event emitted by the Server. The test's scalar Write generates an `AuditWriteUpdateEvent` when auditing is enabled, and a Server-root event subscriber is expected to receive it. The script then reports any event as unexpected; it may also leave a trigger event queued because the preceding step does not drain `MoreNotifications`.

**Recommended CTT fix:** select EventType, filter for only the trigger event the test is validating, and drain every response while `MoreNotifications` is true. Do not treat correctly emitted audit events as Subscription-Minimum failures.
