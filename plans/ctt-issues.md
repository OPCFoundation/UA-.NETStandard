# CTT (Compliance Test Tool) conformance findings

This document lists CTT script defects, open questions and CTT project configuration notes
found while testing the OPC Foundation .NET reference server (`ConsoleReferenceServer --ctt`)
with the CTT. Each entry names the affected scripts, explains why the behavior is a CTT
defect (with specification references), and gives a recommended fix. The procedure for
running the CTT is in [ctt-testing.md](ctt-testing.md).

- Script paths are relative to a CTT server project (`maintree/…` test scripts,
  `library/…` helpers). Line numbers refer to UA 1.05.06, scripts 1.05.513.
- "Resolved / fixed" in Mantis means the fix is in the CTT script repository. It ships
  with a script build after 1.05.513, so an installed 1.05.513 still shows the failure.
- The aggregate oracle (the CTT's own expected-value calculation) is native code in
  `uacompliancetest.exe`. Scripts reach it through
  `library/Information/AggregateInfrastructure/ExecuteAggregateQueryCached.js` and
  `ExecuteAggregateQueryReadResults.js` (`session.executeAggregateQueryCached` /
  `executeAggregateQueryReadResults`). Oracle defects therefore name the calculation rule
  that is wrong rather than a script line.

## Overview

| # | Issue | Mantis | Status |
| --- | --- | --- | --- |
| 1 | Base Info State Machine Instance: `GeneratesEvent` target check | [11248](https://mantis.opcfoundation.org/view.php?id=11248) | Resolved / duplicate of [11125](https://mantis.opcfoundation.org/view.php?id=11125) (fixed) |
| 2 | Historical Access Read Raw: `initialize.js` `ArrayItems` guard | [11249](https://mantis.opcfoundation.org/view.php?id=11249) | Assigned / open |
| 3 | HA Aggregate helper: `possibleNodeId` guard | [11251](https://mantis.opcfoundation.org/view.php?id=11251) | Assigned / open |
| 4 | Aggregate `Err-004.js`: blank ProcessingInterval | [11252](https://mantis.opcfoundation.org/view.php?id=11252) | Assigned / open |
| 5 | AliasName Hierarchy `002.js`: undefined variable | [11262](https://mantis.opcfoundation.org/view.php?id=11262) | Resolved / fixed |
| 6 | Historical Access Read Raw `004.js`: reverse ordering | [11263](https://mantis.opcfoundation.org/view.php?id=11263) | Resolved / fixed |
| 7 | Historical Access Read Raw `014.js`: result index | [11264](https://mantis.opcfoundation.org/view.php?id=11264) | Assigned / open |
| 8 | Historical Access Read Raw `019.js`: harness bypass | [11265](https://mantis.opcfoundation.org/view.php?id=11265) | Assigned / open |
| 9 | Node Management AddNodes `Err-008.js`: duplicate NodeIds | [11266](https://mantis.opcfoundation.org/view.php?id=11266) | Acknowledged / open |
| 10 | Historical Access Read Raw `012.js`: `BadIndexRangeNoData` level | [11267](https://mantis.opcfoundation.org/view.php?id=11267) | Assigned / open |
| 11 | Attribute array helpers: `NodeId[]` and `StatusCode[]` | [11261](https://mantis.opcfoundation.org/view.php?id=11261), [11250](https://mantis.opcfoundation.org/view.php?id=11250) | Assigned / open |
| 12 | Base Info Core Structure 2: UA 1.04 reference model | [11268](https://mantis.opcfoundation.org/view.php?id=11268) | Resolved / fixed (message only) |
| 13 | Base Info Core Structure 2: `ConformanceUnits` as scalar | [11269](https://mantis.opcfoundation.org/view.php?id=11269) | Closed / duplicate of [11144](https://mantis.opcfoundation.org/view.php?id=11144) (fixed) |
| 14 | Base Info Core Structure 2: TransactionDiagnostics `BadOutOfService` | [11256](https://mantis.opcfoundation.org/view.php?id=11256) | New / open |
| 15 | Base Info SemanticChange `001.js`: `Changes` array | [11093](https://mantis.opcfoundation.org/view.php?id=11093) | Assigned / open |
| 16 | Historical Access Read Raw `013.js`: continuation points | [11257](https://mantis.opcfoundation.org/view.php?id=11257) | Resolved / fixed |
| 17 | Security User Name Password `015.js`: PolicyId uniqueness | [11258](https://mantis.opcfoundation.org/view.php?id=11258) | Resolved / no change required |
| 18 | Durable Subscription `008.js`: `MoreNotifications` | [11259](https://mantis.opcfoundation.org/view.php?id=11259) | Resolved / fixed |
| 19 | Subscription Minimum 02 `020.js`: unrelated audit events | [11260](https://mantis.opcfoundation.org/view.php?id=11260) | Assigned / open |
| C1 | Aggregates: harness always sends `UseServerCapabilitiesDefaults = TRUE` | — | Not filed |
| C2 | Aggregates: AnnotationCount counts raw values | — | Not filed |
| C3 | Aggregates: WorstQuality2 includes the end bound | — | Not filed |
| C4 | Aggregates: DurationInState status thresholds | — | Not filed |
| C5 | Aggregates: durations truncated to whole milliseconds | — | Not filed |
| C6 | Aggregates: DurationGood/PercentGood first region | — | Not filed |
| C7–C18 | Other unfiled script defects | — | Not filed |
| C19–C31 | GDS Application Directory / Query Applications script defects | — | Not filed |
| C32–C36 | Monitored Item, Node Management and Security script defects | — | Not filed |
| C37 | Session Base: secure test cases send CreateSession with the `opc.wss` EndpointUrl | — | Not filed |
| C38 | Subscription Durable `012.js`: denied diagnostics Browse and missing braces | — | Not filed |
| C39–C44 | Alarms and Conditions script defects | — | Not filed |
| C45–C47 | Reference server coverage script defects (#4479) | — | Not filed |
| C48 | Aggregates: Minimum/Maximum ignore Uncertain values beyond the Good extremum | — | Not filed |
| C49 | Aggregates: Min/MaxActualTime and Minimum keep Raw when non-Good values make the result Uncertain | — | Not filed |
| C50 | CloseSession on a Session with a running SessionThread: request timestamp taken before a ~550 ms thread stop | — | Not filed |

Mantis states were last checked on 2026-09-13.

---

## Filed issues

### 1. Base Info State Machine Instance — `GeneratesEvent` target validation uses the wrong helper

**Mantis:** [11248](https://mantis.opcfoundation.org/view.php?id=11248), resolved / duplicate of
[11125](https://mantis.opcfoundation.org/view.php?id=11125) (resolved / fixed: the script now uses
`IsSubTypeOfTypeHelper` with an `IncludeBaseType` option).

- **Test:** `maintree/Base Information/Base Info State Machine Instance/Test Cases/001.js`, line 39
- **Helper:** `library/Information/InformationModelUtilities.js` — `IsNodeOfTypeOrSubType` / `GetTypeDefinitionOfNode`
- **Error:** *"Step 1: TargetNode 'i=2311' of GeneratesEvent reference is not of type BaseEventType or a subtype."*

For every StateMachine instance, `001.js` validates the target of each `GeneratesEvent`
reference with `IsNodeOfTypeOrSubType`. That helper resolves the target's
**HasTypeDefinition** and checks whether that type is `BaseEventType` or a subtype.

The TargetNode of a `GeneratesEvent` reference is an `ObjectType` (Part 3 §7.15), and an
`ObjectType` has no `HasTypeDefinition` (Part 3 §7.2). The check therefore fails for every
spec-compliant reference. The reference server is correct: `StateMachineType` and
`FiniteStateMachineType` declare `GeneratesEvent → TransitionEventType (i=2311)`, which is a
`HasSubtype` descendant of `BaseEventType`.

**Recommended fix:** treat the target as a type node and walk `HasSubtype`:

```js
IsSubTypeOfTypeHelper.Execute( { ItemNodeId: referencesResults[rR].ReferenceNodeId, TypeNodeId: new UaNodeId( Identifier.BaseEventType ) } );
if( referencesResults[rR].ReferenceNodeId.equals( new UaNodeId( Identifier.BaseEventType ) ) || IsSubTypeOfTypeHelper.Response.IsSubTypeOf ) { /* pass */ }
```

### 2. Historical Access Read Raw — `initialize.js` accesses `ArrayItems` without its guard

**Mantis:** [11249](https://mantis.opcfoundation.org/view.php?id=11249), assigned / open.

- **Test:** `maintree/Historical Access/Historical Access Read Raw/Test Cases/initialize.js`, line 28
- **Error:** *"Result of expression 'CUVariables.ArrayItems' [undefined] is not an object."*, also
  surfacing in other CUs through the shared post-test handler.

`initialize.js` fills `CUVariables.ArrayItems` from `Settings.ServerTest.NodeIds.Static.HAProfile.Arrays.OneD`
and registers `CUVariables.ResetItems` as `Test.PostTestFunctions[0]`. When no 1-D array history
nodes are configured, `ArrayItems` has no usable `.length` and line 28 throws. Every other use in
the same file is guarded (`isDefined( CUVariables.ArrayItems.length )`, lines 78, 85, 119). A
server without 1-D array history nodes is legal.

**Recommended fix:**

```js
CUVariables.ResetItems = function() {
    for( var i=0; i<CUVariables.Items.length; i++ ) CUVariables.Items[i].ContinuationPoint = null;
    if( isDefined( CUVariables.ArrayItems ) && isDefined( CUVariables.ArrayItems.length ) ) {
        for( var i=0; i<CUVariables.ArrayItems.length; i++ ) CUVariables.ArrayItems[i].ContinuationPoint = null;
    }
};
```

### 3. HA Aggregate helper — multi-node path dereferences `possibleNodeId` without a guard

**Mantis:** [11251](https://mantis.opcfoundation.org/view.php?id=11251), assigned / open.

- **Helper:** `library/ServiceBased/AttributeServiceSet/HistoryRead/HAAggregateHelper.js`, `PerformMultipleNodeTest` (around line 1484)
- **Tests:** `maintree/Aggregates/Aggregate - Base/Test Cases/002-01.js` … `002-04.js`, in every Aggregate CU
- **Error:** *"Result of expression 'possibleNodeId' [undefined] is not an object"*

The helper maps each cached request-entry node back to the current variable list by
positional index:

```js
var originalItemIndex = requestEntry.Nodes[ nodeIndex ].Index;
var possibleNodeId = variables.Items[ originalItemIndex ];   // may be undefined
if ( itemLookup.Contains( possibleNodeId.NodeId.toString() ) ) {   // throws
```

The index was captured against the full variable set, but `variables.Items` is the current
(smaller or re-ordered) subset. Every other lookup in the helper guards with `isDefined(...)`.

**Recommended fix:** `if ( isDefined( possibleNodeId ) && itemLookup.Contains( possibleNodeId.NodeId.toString() ) )`,
or resolve the node through `itemLookup` by NodeId instead of by index.

### 4. Aggregate `Err-004.js` sends an equal-time request when ProcessingInterval is blank

**Mantis:** [11252](https://mantis.opcfoundation.org/view.php?id=11252), assigned / open. The OPC
Foundation recommends validating a non-zero value and giving the setting a default greater than zero.

- **Test:** `maintree/Aggregates/Aggregate - Base/Test Cases/Err-004.js`, line 25 (`PerformExpectedErrorTest`), in every Aggregate CU
- **Helper:** `HAAggregateHelper.js` (`PerformExpectedErrorTest`, `PerformMismatchTest`, error check around line 1764)

The helper builds the request range from the configured Aggregate `ProcessingInterval`. A blank
setting multiplied by ten becomes zero, so the request has `StartTime == EndTime`. Part 11
§6.5.4.2 requires `Bad_InvalidArgument` for equal times, as the per-node operation result with a
Good ServiceResult (Part 4 §5.11.3.2). The server returns exactly that, but `Err-004.js` rejects it
because its intended error condition is now combined with a second one.

**Recommended fix:**

```js
var interval = parseInt( Settings.ServerTest.NodeIds.Static.HAProfile.Aggregates.ProcessingInterval );
if ( isNaN( interval ) || interval <= 0 ) { interval = 1; }
```

Alternatively, skip the test with a configuration error. Apply the same guard in
`PerformMismatchTest`. Note that C1 also affects this test.

### 5. AliasName Hierarchy `002.js` references an undefined variable

**Mantis:** [11262](https://mantis.opcfoundation.org/view.php?id=11262), resolved / fixed (dedicated counter).

- **Test:** `maintree/AliasName/AliasName Hierarchy/Test Cases/002.js`, line 80

The success branch reads `TC_Variables.ListOfNodes.length`, but the results are stored in
`TC_Variables.OutputArguments` (line 37). **Recommended fix:** use `TC_Variables.OutputArguments.length`
or a running count.

### 6. Historical Access Read Raw `004.js` rejects correct reverse ordering

**Mantis:** [11263](https://mantis.opcfoundation.org/view.php?id=11263), resolved / fixed.

- **Test:** `maintree/Historical Access/Historical Access Read Raw/Test Cases/004.js`, lines 78, 91, 105

The reverse-read branches use `if (OPCF.HA.Analysis.Date.FlowsBackward(...)) result = false;`.
For a reverse read, flowing backward is the expected result (Part 11 §6.5.3.2).
**Recommended fix:** negate the predicate.

### 7. Historical Access Read Raw `014.js` indexes a nonexistent second node result

**Mantis:** [11264](https://mantis.opcfoundation.org/view.php?id=11264), assigned / open.

- **Test:** `maintree/Historical Access/Historical Access Read Raw/Test Cases/014.js`, lines 46 and 78

The test reads one node but inspects `Response.Results[1]`. The intended check is the second
DataValue of the first node. **Recommended fix:** validate `haItems[0].Value[1].StatusCode` with
length guards and describe it as record 2.

### 8. Historical Access Read Raw `019.js` bypasses the CTT test harness

**Mantis:** [11265](https://mantis.opcfoundation.org/view.php?id=11265), assigned / open.

- **Test:** `maintree/Historical Access/Historical Access Read Raw/Test Cases/019.js`

The script calls `readraw019()` directly; the `Test.Execute` wrapper is commented out.
**Recommended fix:** `Test.Execute({ Procedure: readraw019 });` so exceptions, result accounting,
setup and cleanup follow the normal path.

### 9. Node Management AddNodes `Err-008.js` tests duplicate NodeIds with client NodeIds disabled

**Mantis:** [11266](https://mantis.opcfoundation.org/view.php?id=11266), acknowledged / open. It is
waiting for a separate review of all Node Management test cases.

- **Test:** `maintree/Node Management Services/Node Management Add Node/Test Cases/Err-008.js`

The test sends the same AddNodes item twice and expects `BadNodeIdExists`. With
`/NodeManagement/RequestedNodeId` disabled, `CUVariables.RequestedNewNodeId()` returns a null
NodeId, so each call legitimately creates a new node and the second `Good` is correct.
**Recommended fix:** skip the test when client-specified NodeIds are disabled, or require a
configured NodeId in a writable namespace.

### 10. Historical Access Read Raw `012.js` expects `BadIndexRangeNoData` at the wrong level

**Mantis:** [11267](https://mantis.opcfoundation.org/view.php?id=11267), assigned / open. The related
issue [11273](https://mantis.opcfoundation.org/view.php?id=11273) (assigned / open) covers `012.js`
sizing every IndexRange from the first configured array.

- **Test:** `maintree/Historical Access/Historical Access Read Raw/Test Cases/012.js`, line 48

For an out-of-bounds IndexRange on historized arrays, the server returns a Good
`HistoryReadResult.StatusCode` and `BadIndexRangeNoData` on each DataValue. The test expects
`Results[0].StatusCode = BadIndexRangeNoData`. Part 11 §6.4 applies IndexRange independently to
each historical value. **Recommended fix:** require a Good per-node result, decode `HistoryData`, and
assert `BadIndexRangeNoData` on each affected DataValue.

### 11. Attribute array helpers omit `NodeId[]` (and `StatusCode[]`) support

**Mantis:** [11261](https://mantis.opcfoundation.org/view.php?id=11261) (`NodeId[]`) and
[11250](https://mantis.opcfoundation.org/view.php?id=11250) (`StatusCode[]`), both assigned / open. On
11250, the reporter suggests documenting that variant array tests cover only built-in types 1–15.

- **Tests:** Attribute Read `032.js`, `034.js`; Attribute Write Index `007.js` (NodeId); Attribute Read `026.js`, `036.js` (StatusCode)
- **Error:** *"Built in type not specified or detectable within the parameter: NodeId (17)"*

`UaNodeId.GuessType(...)` identifies the built-in type correctly, but the generic array
conversion/generation helper has no branch for it. **Recommended fix:** add both directions
(decode with the matching `to…Array()` accessor, generate a typed collection and set it with the
array Variant setter), or exclude unsupported built-in types before the test runs.

### 12. Base Info Core Structure 2 — error message cites the UA 1.04 reference model

**Mantis:** [11268](https://mantis.opcfoundation.org/view.php?id=11268), resolved / fixed.

The script already validates against the UA 1.05 NodeSet. One error message still said
*"…is not compliant with the UA 1.04 NodeSetFile"*, and it has been corrected.

### 13. Base Info Core Structure 2 — `ConformanceUnits` tested as a scalar

**Mantis:** [11269](https://mantis.opcfoundation.org/view.php?id=11269), closed / duplicate of
[11144](https://mantis.opcfoundation.org/view.php?id=11144) (resolved / fixed: QualifiedName and other
missing built-in types were added to `BuiltInType.StringToNodeId` in `UaB.js`).

`Server.ServerCapabilities.ConformanceUnits` (`i=24101`) is `QualifiedName[]` (ValueRank 1), but
the value was validated as a scalar.

### 14. Base Info Core Structure 2 — TransactionDiagnostics read before any transaction

**Mantis:** [11256](https://mantis.opcfoundation.org/view.php?id=11256), new / open. The OPC Foundation
suggests identifying `TransactionDiagnosticsType` and accepting `BadOutOfService` only there, or
covering transactions in a separate ConformanceUnit.

- **Test:** `maintree/Base Information/Base Info Core Structure 2/Test Cases/001.js`

The test reports `BadOutOfService` for `i=32337`…`i=32340` as a read failure. Part 12 §7.10.17:
*"If no transaction has started the values of all Variables have a status of Bad_OutOfService."*
**Recommended fix:** accept `BadOutOfService` for TransactionDiagnostics before the first
transaction, or complete a transaction first.

### 15. Base Info SemanticChange `001.js` decodes the `Changes` array as one ExtensionObject

**Mantis:** [11093](https://mantis.opcfoundation.org/view.php?id=11093), assigned / open.

- **Test:** `maintree/Base Information/Base Info SemanticChange/Test Cases/001.js`, line 275

The script calls `EventFields[0].toExtensionObject()`, but `SemanticChangeEventType.Changes` is
`SemanticChangeStructureDataType[]` (Part 5 Table 174). The scalar conversion returns null and the
script throws. **Recommended fix:** decode an ExtensionObject array and convert each element.

### 16. Historical Access Read Raw `013.js` reuses continuation points after changing IndexRange

**Mantis:** [11257](https://mantis.opcfoundation.org/view.php?id=11257), resolved / fixed (the
continuation point is now cleared after each HistoryRead). Scripts 1.05.513 still fail with
`BadContinuationPointInvalid`.

- **Test:** `maintree/Historical Access/Historical Access Read Raw/Test Cases/013.js`, line 39

The same `HistoryReadValueId` objects are reused for three IndexRanges without clearing the
continuation points from the previous call. A continuation point is opaque state for its original
request (Part 11 §6.4.3.3).

### 17. Security User Name Password `015.js` — PolicyId uniqueness across endpoints

**Mantis:** [11258](https://mantis.opcfoundation.org/view.php?id=11258), resolved / **no change required**.

The report argued that PolicyIds only need to be unique within one endpoint's `UserIdentityTokens`.
The Part 4 editors clarified that every distinct UserTokenPolicy configuration needs its own
unique PolicyId, including across endpoints. `015.js` already checks that, so this is **not** a
CTT defect. A server must not reuse a PolicyId for differently configured token policies.

### 18. Durable Subscription `008.js` misspells `MoreNotifications`

**Mantis:** [11259](https://mantis.opcfoundation.org/view.php?id=11259), resolved / fixed.

- **Test:** `maintree/Subscription Services/Subscription Durable/Test Cases/008.js`, lines 101–108

Line 101 uses `MoreNotifcations`, so the drain loop never runs, and lines 105–108 lack braces, which
makes `result = false` unconditional.

Scripts 1.05.513 still contain both defects. Against the reference server the test case passes
anyway (2026-09-14): all of its data changes fit into one Publish response, and `Test.Execute`
reports from `addError`, not from the return value.

### 19. Subscription Minimum 02 `020.js` accepts unrelated audit events

**Mantis:** [11260](https://mantis.opcfoundation.org/view.php?id=11260), assigned / open.

- **Test:** `maintree/Subscription Services/Subscription Minimum 02/Test Cases/020.js`

The event MonitoredItem has no WhereClause, so it receives every Server event, including the
`AuditWriteUpdateEvent` produced by the test's own Write. The script reports any event as
unexpected and does not drain `MoreNotifications`. **Recommended fix:** select EventType, filter
for the trigger event, and drain while `MoreNotifications` is true.

---

## Not yet filed: aggregate issues

All of these use the shared scripts in `maintree/Aggregates/Aggregate - Base/Test Cases/`, which
run in every `Aggregate – *` ConformanceUnit, together with
`library/ServiceBased/AttributeServiceSet/HistoryRead/HAAggregateHelper.js`
(`PerformSingleNodeTest`, `PerformMultipleNodeTest`, `PerformAggregateCheck`, `AggregateQuery`,
`CompareValues`, `CompareHistoryData`, `equals`).

### C1. Aggregate harness always sends `UseServerCapabilitiesDefaults = TRUE`

- **Tests:** `003-01.js` … `003-04.js`, `004-01.js` … `004-04.js` (all set a configuration) and `Err-004.js`
- **Helpers:** `HAAggregateHelper.js` `GetDefaultConfiguration` (line 2201), `MergeDefaultConfiguration` (lines 2213–2230), `GetItemConfiguration`/`TranslateConfiguration` (lines 477–497), `CreateProcessedDetailsRequest` (line 2283); `HAStructureHelpers.js` `UaAggregateConfiguration.New` (line 34)

What happens:
1. `003-0x.js`/`004-0x.js` set `configuration.UseDefaults = false`, a key that
   `GetDefaultConfiguration` does not define. `Err-004.js` sets
   `configuration.UseServerCapabilitiesDefaults = false`.
2. `MergeDefaultConfiguration` iterates only the keys of the node's HA configuration
   (`PercentDataBad`, `PercentDataGood`, `TreatUncertainAsBad`, `UseSlopedExtrapolation`,
   `Stepped`), so both flags are dropped.
3. `UaAggregateConfiguration.New` reads only `args.UseDefaults` and defaults
   `UseServerCapabilitiesDefaults` to `true`.

The server receives `useServerCapabilitiesDefaults = TRUE`. Per Part 4 §7.22.4 and Part 13 §5.2.2 it
must then ignore the other fields and use its own defaults. The native oracle, however, applies
the test values (for example TreatUncertainAsBad=false, PercentDataGood/Bad=50). Symptoms in
`003-02/03` and `004-02/03` include Uncertain vs Good on Average/Minimum/Maximum/Range/Count, and
DurationBad 13,800 vs 8,600 ms. DeltaBounds shows BadNoData vs 2.38 Uncertain, and
DurationInState* shows Uncertain vs Good. The effect is the largest single source of aggregate
mismatches, and the tests never exercise an explicit aggregate configuration.

**Recommended fix:**

```js
// HAStructureHelpers.js, UaAggregateConfiguration.New
if( isDefined( args.UseServerCapabilitiesDefaults ) ) uaObj.UseServerCapabilitiesDefaults = args.UseServerCapabilitiesDefaults;
else if( isDefined( args.UseDefaults ) ) uaObj.UseServerCapabilitiesDefaults = args.UseDefaults;
else uaObj.UseServerCapabilitiesDefaults = true;

// HAAggregateHelper.js, MergeDefaultConfiguration: carry test-only keys
[ "UseServerCapabilitiesDefaults", "UseDefaults" ].forEach( function ( parameter ) {
    if ( isDefined( testConfiguration[ parameter ] ) ) merged[ parameter ] = testConfiguration[ parameter ];
} );
```

Also make the test scripts use the key that `GetDefaultConfiguration` defines
(`UseServerCapabilitiesDefaults`). Pass the same effective configuration to the oracle.

### C2. AnnotationCount oracle counts raw values instead of Annotations

- **CU:** `Aggregate – AnnotationCount` (all Base test cases)

The server returns 0 in every interval for nodes without Annotations. The oracle returns the
number of raw values of any status: an interval holding two Good values and one Bad value gives 3,
and a whole-range request gives 24. Part 13 §5.4.3.20: AnnotationCount *"returns a count of all
Annotations in the interval"*. Annotations are separate from the raw values: they belong to the
node's Annotations Property (Part 11 §5.1.2) and are read with `ReadAnnotationDataDetails`
(§6.5.6) or `ReadRawModifiedDetails`.

**Recommended fix:** count Annotations for the node (from its Annotations property / annotation
history), not raw DataValues. Configure a node with known Annotations to get a non-zero test.

### C3. WorstQuality2 oracle also includes the end bound

- **CU:** `Aggregate – WorstQuality2`

Example: interval [142.8 s, 166.6 s) contains only Good raw values and has a Good start bound.
The oracle returns UncertainDataSubNormal; the only Uncertain source is the simple **end** bound
at 166.6 s (the next raw value is Bad). In other intervals, the oracle sets `MultipleValues` because
it counts the end bound as a second value. Part 13 §5.4.3.36 includes only the start bound
(*"always includes the start bound"*).

**Recommended fix:** evaluate the start bound plus the raw values inside the interval. Exclude the
end bound from both the worst-status selection and the `MultipleValues` count.

### C4. DurationInStateZero/NonZero reports Bad below PercentDataBad

- **CUs:** `Aggregate – DurationInStateZero`, `Aggregate – DurationInStateNonZero`

Example: interval [71.4 s, 95.2 s) with TreatUncertainAsBad=true and PercentDataBad=100. Bad time
is 13.8 of 23.8 s (58%) and Good time 42%. The server returns DurationInStateNonZero
`10000, UncertainDataSubNormal`; the oracle returns `null, Bad`. Part 13 §5.4.3.2.1: the interval is Bad only if the Bad ratio is at
least PercentDataBad, Good only if the Good ratio is at least PercentDataGood, and otherwise
Uncertain_DataSubNormal. Intervals with 6% or 20% Bad match, so the oracle appears to use a fixed
threshold of about 50%.

**Recommended fix:** use the time-based status rule of §5.4.3.2.1 with the configured
PercentDataBad/PercentDataGood, as the other time-weighted aggregates do.

### C5. Duration aggregates are truncated to whole milliseconds

- **CUs:** `Aggregate – DurationGood`, `DurationBad`, `PercentGood`, `PercentBad` (multi-node `002-0x.js`)

Raw source timestamps carry sub-millisecond ticks. For one-interval multi-node reads the server
returns, for example, DurationBad 40441.65524 and the oracle 40441, or DurationGood 197865.7998 vs
197865. The oracle truncates every region to whole milliseconds, so its regions add up to less
than the true interval width. Part 13 defines Duration as a Double number of milliseconds.

**Recommended fix:** calculate region durations from full-precision timestamps (DateTime ticks).
Alternatively, let `HAAggregateHelper.equals` (lines 2411–2428) accept a sub-millisecond difference
for Duration results; its current fallback accepts only an absolute difference below 0.01.

### C6. DurationGood/PercentGood first region ignores the raw value before the interval

- **CUs:** `Aggregate – DurationGood`, `Aggregate – PercentGood`

Example: interval [166.6 s, 190.4 s), TreatUncertainAsBad=true. The raw value before the start is
160 s (Good); the samples are 170 s Bad, 180 s Good, 190 s Uncertain. The server returns DurationGood
13,400 ms (3.4 + 10 s). The oracle returns 10,000 ms, and for the same interval DurationBad 10,400 ms,
so it treats 166.6–170 s as neither Good nor Bad. Part 13 §5.4.3.31/32: *"The status of the first
region is determined by finding the first data point at or before the start of the interval. If no
value exists, the first region is Bad."*

**Recommended fix:** take the first region's status from the raw value at or before the interval
start, not from the interpolated simple bound. Use the same rule for DurationBad and PercentBad.

### Known aggregate oracle differences (not yet filed)

- **Non-numeric nodes.** Status-only aggregates (DurationGood/Bad, PercentGood/Bad, WorstQuality2,
  DurationInState*) must not depend on the value type. The oracle nevertheless returns different
  results for Boolean/String nodes than for numeric nodes with the same status timeline, and it
  returns `BadNoData` for valid Boolean/String StartBound/EndBound (Part 13 §5.4.2.3, simple
  bounding values). **Fix:** evaluate status and bounds independently of the value type.
- **Int32 conversion.** Interpolative, TimeAverage, Total, DeltaBounds and StartBound/EndBound
  differ on Int32 nodes only (for example 24 vs 23). The server rounds interpolated values to the
  nearest integer; the oracle truncates. Part 13 does not mandate the conversion. **Fix:** accept
  either conversion, or document a rounding rule. MultipleValues differences in
  Minimum2/Maximum2 follow from the same bound value.
- **MinimumActualTime2/MaximumActualTime2.** With a sloped End bound the server returns the bound
  timestamped at EffectiveEndTime (Part 13 §§5.4.3.17–.18, §5.4.2.4); the oracle selects an earlier
  raw value. **Fix:** include the sloped End bound as a candidate.

## Not yet filed: other script defects

### C7. Historical Access Read Raw `Err-013.js` describes an operation error as a ServiceResult

Reusing a consumed continuation point produces a per-node `BadContinuationPointInvalid` while the
HistoryRead ServiceResult stays Good (Part 11 §6.3, Part 4 §5.11.3.2). **Fix:** word the message as
"expected a Good ServiceResult and `Results[0].StatusCode` `BadContinuationPointInvalid`".

### C8. Historical Access Read Raw `Err-019.js` uses an undefined loop variable

Lines 25 and 43 interpolate an undeclared `i` into error messages. **Fix:** use literal case
numbers or define a case index.

### C9. Monitor Value Change V2 `042.js` cannot identify the missing item

- **Test:** `maintree/Monitored Item Services/Monitor Value Change V2/Test Cases/042.js`

The test creates 19 matrix monitored items with IndexRange `1,1,…` and reports only the count
(`Expected 19 but got 18`), never the missing ClientHandle/NodeId. Line 243 computes
`indexValue` from itself before it is initialized. For very large Double/Float matrix elements
(about `-8.19E+24`, `-1.03E+33`), adding one does not change the value. **Fix:** initialize the
index, report missing ClientHandles, and write a representably different value.

### C10. Alarm `Test_002.js` evaluates Retain from the main branch only

Retain is derived from the main event's Active/Acked/Confirmed fields. Part 9 §5.5.2 requires
`Retain=true` while any ConditionBranch still needs operator input. **Fix:** include outstanding
branches in `ValidateRetain`. This also covers Confirm `Test_001.js`, depending on the alarm phase when the
confirmation event arrives: in the full run of 2026-09-15 it failed for all 14 alarm types (logged in a
reproduction: *"ValidateRetain failed retain = true Active State = false AckedState = true ConfirmedState =
Confirmed"*), while the same CU alone and in an Acknowledge/Alarm/Basic/Comment/Confirm run confirmed active
alarms and passed 14 of 14.

### C11. Alarm `Test_004.js` calls `ReadHelper` re-entrantly from the alarm callback

The global `ReadHelper` runs synchronously inside the alarm callback and fails client-side with
`BadInvalidState`. The server resolves and reads every AlarmCondition `InputNode` with Good.
**Fix:** queue the Read outside the callback, or use a helper/session valid on that thread.

### C12. Enable `Test_002.js` passes four arguments to a three-argument `AddMessage`

`collector.AddMessage(testCase, category, conditionId, reason)` drops `reason`. The result is empty
`Error: ns=...` entries that hide which check failed. **Fix:** combine `conditionId` and `reason`
into the third argument. With the reason logged, the failing check is the event `Time` comparison
against `GetCallTime()`, which is broken (C39).

### C13. Base Info Currency `004.js` drops the CurrencyUnit Exponent

The server's EUR CurrencyUnit is `NumericCode=978`, `Exponent=2`, `AlphabeticCode=EUR`,
`Currency=Euro`, encoded with the Int16 NumericCode followed by the SByte Exponent (prefix
`D2 03 02`). The script reports an empty Exponent, so the `toCurrencyUnitType()` conversion loses
the field. **Fix:** decode the SByte Exponent after NumericCode.

### C14. Auditing Connections cannot find entries by `ClientAuditEntryId` (withdrawn: server defect, fixed)

- **Helper:** `library/…/AuditValidationHelper.js`, line 346 (*"Unable to Find Entry for ClientAuditEntryId"*)

Not a CTT defect. The CTT audit subscription monitors `Server.EventNotifier` with QueueSize 1, and the
server took an event queueSize of 1 literally, so the audit events of one connect overwrote each other
(the CTT buffer showed EventQueueOverflowEventType events). Part 4 §7.21 makes 0 the server default and
1 the server minimum for event items; the server now revises both to its default queue size
([#4480](https://github.com/OPCFoundation/UA-.NETStandard/pull/4480)). In the full run of 2026-09-15
Auditing Connections `001.js`, `007.js`, `011.js`, `012.js` and `020.js` pass.

### C15. Base Info Core Structure 2 — `InfoFactory.js` Organizes check dereferences an undefined type

- **Test:** `maintree/Base Information/Base Info Core Structure 2/Test Cases/001.js`
- **Helper:** `library/Information/InfoFactory.js`, `Organizes` validator, lines 454–473
- **Error:** *"Result of expression 'sourceTypeNodeId' [undefined] is not an object"* (line 466), which aborts `001.js`

For a node with an `Organizes` reference, the validator looks for a `HasTypeDefinition` reference in
the same browse result to get `sourceTypeNodeId` (line 458), then calls `sourceTypeNodeId.equals(...)`
unconditionally (line 466). Line 464 explicitly allows the source to be a **View**, and View nodes
have no `HasTypeDefinition`, so `sourceTypeNodeId` stays undefined. The variable is also declared
inside the inner loop, so a value can leak from an earlier node. **Fix:** declare
`var sourceTypeNodeId = null;` before the inner loop, and run the FolderType check only when
`isDefined( sourceTypeNodeId )` (i.e. for Object sources).

### C16. Discovery Get Endpoints `003.js` rejects WebSocket transport profiles

- **Test:** `maintree/Discovery Services/Discovery Get Endpoints/Test Cases/003.js`, lines 22–27 and 39
- **Error:** *"Unexpected type: http://opcfoundation.org/UA-Profile/Transport/wss-uasc-uabinary"*

`AcceptedProfileUris` lists only UA TCP, SOAP/HTTP and HTTPS transport profiles. The reference
server also exposes an `opc.wss` endpoint with the valid Part 7 transport profile
`http://opcfoundation.org/UA-Profile/Transport/wss-uasc-uabinary`. **Fix:** add the WebSocket
profiles (`wss-uasc-uabinary`, `wss-uajson`) to `AcceptedProfileUris`.

### C17. Monitor Basic `039.js` calls `getMatrixValues` without including its library

- **Test:** `maintree/Monitored Item Services/Monitor Basic/Test Cases/039.js`, lines 45, 78, 84, 90
- **Error:** *"Can't find variable: getMatrixValues"* (ReferenceError, line 45)

`getMatrixValues` is defined in `library/Base/indexRangeRelatedUtilities.js`. Monitor Value Change V2
and Monitor Items Deadband Filter include that file in their `initialize.js`, but Monitor Basic
does not, so `039.js` only works when another CU has loaded the library earlier in the same run.
**Fix:** add `include( "./library/Base/indexRangeRelatedUtilities.js" );` to
`maintree/Monitored Item Services/Monitor Basic/Test Cases/initialize.js`.

### C18. A & C Acknowledge / Confirm cannot find recommended state texts for `en-US`

- **Tests:** A & C Acknowledge `Test_001.js`–`Test_003.js`, `Err_004.js`; A & C Confirm `Test_001.js`–`Test_003.js`
- **Warning:** *"CTT cannot retrieve recommended text for AckedState in the supplied locale en-US"* (also for
  ConfirmedState), about 130 times per run

The warning comes from the CTT's own lookup of the Part 9 recommended TwoStateVariable texts, not
from a server value, and appears for every condition type. Not yet pinpointed. **Fix direction:**
fall back from a specific locale (`en-US`) to its base language (`en`) when looking up the
recommended texts.

### C19. GDS Application Directory `060.js`, `067.js`, `069.js` register an ApplicationUri that is not a URI

- **Tests:** `maintree/GDS/GDS Application Directory/Test Cases/060.js` and `067.js` line 15
  (`urn:OPCFoundation:ServerApplicationWith%WildcardCharacter`), `069.js` line 15
  (`urn:OPCFoundation:ServerApplicationWith\BackslashCharacter`)
- **Error:** *"Call.Results[0].StatusCode incorrect. Received: BadInvalidArgument. Expected: Good"*, then
  *"Failed to register a temporary application record …"*

The patterns under test (`[%]`, `%\%%`, `%\\%`) are never sent. RegisterApplication rejects the
temporary record: a raw `%` that is not followed by two hex digits and a `\` are not allowed in a URI
(RFC 3986 §2.1, §3.3), and OPC 10000-12 §6.5.6 returns `Bad_InvalidArgument` when *"one of the
fields of the application record is not valid"* (§6.5.4 treats *"not a valid URI"* the same way).
Query Applications `011.js`/`018.js` avoid this by putting the `%` into the ApplicationName.
**Fix:** use a valid URI (the percent-encoded `…With%25WildcardCharacter` still contains a literal
`%`), or test `%` and `\` through the ApplicationName filter of QueryServers. `060.js` also has the
defect of C20.

### C20. GDS `[_]` / `[%]` patterns are evaluated as "contains"

- **Tests:** GDS Application Directory `065.js` line 10 (`[_]`), `060.js` line 10 (`[%]`); GDS Query
  Applications `011.js` line 10 (`[%]`), `016.js` line 10 (`[_]`)
- **Error:** *"Did not receive the expected list of servers"* / *"Received unexpected array length for
  OutputArgument 'applications'"*, *"Expected <2> but got <0>"*

The scripts expect every record whose ApplicationUri/ApplicationName *contains* `_` or `%`
(`cab:other_foundation:ClientAndServer`, *"… with % wildcard character"*). A Like pattern matches the
whole string: OPC 10000-4 §7.7.3 gives *"5[%] would match '5%'"*, and `main%` only matches strings
that start with `main`. `[_]` therefore only matches the one-character string `_`, and the GDS
correctly returns no record. **Fix:** use `%[_]%` and `%[%]%` (or `%\_%` and `%\%%`, which
`068.js`, `018.js` and `019.js` already test).

### C21. GDS `%[^f-h]%` / `%[^w-y]%` patterns are evaluated as "contains none of"

- **Tests:** GDS Application Directory `074.js` line 11 (`%[^f-h]%`); GDS Query Applications `025.js`
  line 11 (`%[^w-y]%`)
- **Error (with a spec-conformant matcher):** *"Expected <3> but got <5>"* / *"Expected <3> but got <4>"*

The scripts expect the pattern to exclude `cab:other_foundation:ClientAndServer` (contains `f` and
`h`) and *"Example_Vendor - ClientAndServer"* (contains `x`). Per OPC 10000-4 §7.7.3, `[^f-h]` matches
**one** character that is not in the list, and the surrounding `%` match anything, so the pattern
matches every string that has at least one character outside `f`–`h`, which is every registered
record. No Like pattern can express "contains none of these characters". Both tests passed before
2026-09-14 only because the GDS matcher (`ApplicationsDatabaseBase.SkipToNext`) special-cased `[^`.
**Fix:** test the negated list at a fixed position (`073.js`/`024.js` already do with `%[^q-s]`) and
drop these cases, or expect all records.

### C22. `callQueryServers()` dereferences the output arguments of a failed call

- **Tests:** GDS Application Directory `079.js` step 2 (`ServerCapabilities = [ "NA", "DA", "AC" ]`) and
  `078.js` (`%[a^j-l]%`, since the server rejects the invalid pattern); `078.js` lines 19–20
- **Helper:** `library/GDS/MethodCalls.js`, lines 279–286
- **Error:** *"Result of expression 'servers' [null] is not an object"* (TypeError, line 286), which aborts the test

The server returns the expected `BadInvalidArgument` (NA *"cannot be used in combination with any
other capability"*, Part 12 Annex D; an invalid Like pattern for `078.js`) with an empty
`OutputArguments` array. The helper's
`isDefined( OutputArguments[0] ) && isDefined( OutputArguments[1] )` guard does not detect the
empty array, and `toExtensionObjectArray()` of the empty variant returns null. `callQueryApplications()`
in the same file checks `applications.isEmpty()` first. With the helper fixed, `078.js` still aborts at
line 20 (*"Result of expression 'queryServersResult.Servers' [undefined] is not an object"*): it reads
`Servers` after the expected Bad result, and its condition is inverted
(`if( Assert.Equal( 0, … ) ) TC_Variables.Result = false;` fails the test when no record is returned).
**Fix:** only read the output arguments when
`Results[0].StatusCode.isGood()`, and check `isEmpty()` before `toExtensionObjectArray()`.

### C23. GDS Application Directory `018.js` selects `ActionTimestamp` instead of `ActionTimeStamp`

- **Test:** `maintree/GDS/GDS Application Directory/Test Cases/initialize.js` line 44
  (`ApplicationRegistrationChangedAuditEventType_Fields`), used by `018.js`
- **Error:** *"AuditEventType.ActionTimestamp should contain a valid timestamp that is somewhat current.
  Received: '0001-01-01T00:00:00Z'"*

The AuditEventType property's BrowseName is `ActionTimeStamp` (OPC 10000-5 §6.4.3), so the select
clause built from `"ActionTimestamp"` resolves to nothing and the event field is null. The
validator in `library/ClassBased/Events.js` line 78 also reads `args.ActionTimestamp`; the CTT's own
`library/__regressionTesting/_Events.js` passes `ActionTimeStamp`. In other runs
the same test instead reports *"Did not receive an ApplicationRegistrationChangedAuditEventType
event"*: the monitored item is created with QueueSize 1, and the server kept only the newest audit
event (server side, fixed by [#4480](https://github.com/OPCFoundation/UA-.NETStandard/pull/4480)).
With both names corrected and a larger queue in a copy of the scripts, the event is received, it
carries a current ActionTimeStamp (for example `2026-09-14T11:12:58.148Z`), and SourceNode,
SourceName, MethodId and InputArguments verify. The validator then aborts at `Events.js` line 85
(*"'this.ActionTimestamp.isNull' [undefined] is not a function"*) because it calls `isNull()` on the
event field Variant instead of a `UaDateTime`. The other audit tests of the CU (`011.js`, `028.js`) use
the same QueueSize 1 subscription and miss their event in some runs. **Fix:** use `"ActionTimeStamp"`
in the field list and in `Events.js`, convert the field with `toDateTime()`, and create the audit
monitored items with a queue size above 1.

### C24. GDS Application Directory `019.js` step 3 batch RegisterApplication never reaches the server

- **Test:** `maintree/GDS/GDS Application Directory/Test Cases/019.js`, line 53
- **Error:** *"Call the ErrorCode in the Error Message received doesn't match the expectation. Expected:
  Good but received: BadNotFound"*, then *"Step 3: Failed to register all ApplicationRecords in one call"*

`BadNotFound` is the CTT client's own status for `session.call()`. With `-l` logging, the server log
shows no `OnRegisterApplication` entry and no *"Service Fault Occurred"* for this request. The next
entries are the four individual registrations of the script's fallback path. The same four records
registered in one Call request succeed with four Good results (`GdsApplicationDirectoryTests.
RegisterApplicationBatchedInOneCallRequestAsync`). **Fix:** CTT client: find out why the Call request
with four `ApplicationRecordDataType` ExtensionObjects fails before it is sent.

### C25. GDS Application Directory `010.js` dereferences the ApplicationId of a rejected registration

- **Test:** `maintree/GDS/GDS Application Directory/Test Cases/010.js`, line 37
- **Error:** *"Result of expression 'registerApplicationResult.ApplicationId' [undefined] is not an object"*

RegisterApplication of an already registered ApplicationUri correctly returns `Bad_EntryExists`
(OPC 10000-12 §6.5.6) with no output arguments, so `callRegisterApplication()` does not set
`ApplicationId`, and line 37 calls `.clone()` on undefined before checking the StatusCode.
**Fix:** clone only when `isDefined( registerApplicationResult.ApplicationId )`.

### C26. GDS Application Directory `012.js` / `032.js` require ServerCapabilities for a Server

- **Tests:** `012.js` step 5 (line 66, RegisterApplication), `032.js` line 52 (UpdateApplication)
- **Error:** *"Call.Results[0].StatusCode incorrect. Received: Good. Expected: BadInvalidArgument"*

The scripts expect `Bad_InvalidArgument` when a Server record has an empty ServerCapabilities array.
OPC 10000-12 §6.5.5 (Table 7) and §6.5.6/§6.5.7 define no such requirement: the only ServerCapabilities
rules are the RCP and NA rules for Clients and ClientAndServer, and Annex D describes `NA` as *"No
capability information is available"* without making it mandatory. **Fix:** accept Good or
`Bad_InvalidArgument`, or ask for a Part 12 clarification that Servers shall register `NA`.

### C27. GDS Application Directory `027.js` changes a Server with a DiscoveryUrl into a Client

- **Test:** `maintree/GDS/GDS Application Directory/Test Cases/027.js`, line 39
  (`UaVariant.Increment` of the embedded server's ApplicationType)
- **Error:** *"Call.Results[0].StatusCode incorrect. Received: BadInvalidArgument. Expected: Good"*,
  *"Step 1: UpdateApplication call was not successful on iteration #0"*

The updated record is a Client with DiscoveryUrl `opc.tcp://…:4842` and ServerCapabilities `NA`. A
Client may only register DiscoveryUrls for reverse connect: *"all DiscoveryUrls shall begin with the
rcp+ prefix"* and ServerCapabilities *"shall include RCP"* (OPC 10000-12 §6.5.5), and OPC 10000-4 §7.2
requires an empty discoveryUrls list for a CLIENT. UpdateApplication returns `Bad_InvalidArgument` for
an invalid field (§6.5.7). **Fix:** when changing the type to Client, clear the DiscoveryUrls (or
change the type to ClientAndServer), or expect `Bad_InvalidArgument`.

### C28. GDS Application Directory `029.js`, `038.js`, `039.js` expect `BadInvalidArgument` for unknown ApplicationIds

- **Tests:** `029.js` line 16 (UpdateApplication with an empty record, ApplicationId null),
  `038.js` line 15 (GetApplication with a null NodeId), `039.js` line 18 (GetApplication with
  `Settings.Advanced.NodeIds.Invalid.NodeId1`)
- **Error:** *"Call.Results[0].StatusCode incorrect. Received: BadNotFound. Expected: BadInvalidArgument"*

The only result code OPC 10000-12 defines for an ApplicationId problem is `Bad_NotFound` *"The
ApplicationId is not known to the GDS"* (§6.5.7 UpdateApplication, §6.5.9 GetApplication);
`Bad_InvalidArgument` is not listed for GetApplication at all. A null or foreign NodeId is not known to
the GDS. **Fix:** expect `Bad_NotFound` (accept `Bad_InvalidArgument` as well for `029.js`, whose record
fields are also invalid).

### C29. GDS Application Directory `005.js` expects `BadInvalidArgument` for a string above MaxStringLength

- **Test:** `maintree/GDS/GDS Application Directory/Test Cases/005.js`, lines 20, 31, 37
- **Error:** *"Call.Results[0].StatusCode incorrect. Received: Good. Expected: BadInvalidArgument"* (second call)

The script builds the ApplicationUri from `String.fromCharCode( Math.floor( Math.random() * 256 ) )`,
first with MaxStringLength (1,048,576) characters and then 10 % more, and expects Good and then
`Bad_InvalidArgument`. Both calls returned Good with an empty result. A string above the server's
`MaxStringLength` cannot be decoded: `BinaryDecoder.ReadString` rejects it with
`Bad_EncodingLimitsExceeded` (limit from the transport quotas, `TcpTransportListener`), so the request
never reaches FindApplications. A copy of the script that uses printable characters (`urn:` + letters)
confirms this: the first call returns Good, and the second call gets a ServiceFault
`Bad_EncodingLimitsExceeded` (server log *"MaxStringLength 1048576 < 1153434"*). So the original random
string does not reach the server as generated; a likely cause is its `\0` and non-ASCII code points.
**Fix:** use printable ASCII characters, and expect the ServiceFault `Bad_EncodingLimitsExceeded` for
the oversized call.
### C30. GDS Query Applications `036.js` expects `rcp+` URLs the test never registered

- **Test:** `maintree/GDS/GDS Query Applications/Test Cases/036.js`, lines 23–28; records from
  `initialize.js`
- **Error:** *"Received DiscoveryUrl 'opc.tcp://ClientAndServer:12345' does not start with 'rcp+' prefix"*
  (and `:12346`)

The only registered application with the `RCP` capability is `cab:other_foundation:ClientAndServer`,
whose DiscoveryUrls are the plain `opc.tcp://ClientAndServer:12345/12346`. QueryApplications copies the
record's DiscoveryUrls unchanged (OPC 10000-12 §6.5.10 Table 13). For ClientAndServer, only *"DiscoveryUrls
that support reverse connect have the rcp+ prefix"* (§6.5.5), and *"DiscoveryUrls without the prefix are
used for forward connections"* (§4.4.3). **Fix:** register the RCP client or ClientAndServer with
`rcp+opc.tcp://…` URLs, and check only that each returned record has at least one `rcp+` URL.

### C31. GDS Query Applications `038.js` treats applicationType 3 as invalid

- **Test:** `maintree/GDS/GDS Query Applications/Test Cases/038.js`, lines 17 and 48 (step 5)
- **Error:** *"Call.Results[0].StatusCode incorrect. Received: Good. Expected: BadInvalidArgument"*

Step 6 (`applicationType = 0xFFFFFFFF`) already returns `Bad_InvalidArgument`; only step 5 fails.
QueryApplications' ApplicationType is *"A mask indicating what types of applications are returned. The
mask values are: 0x1 - Servers; 0x2 - Clients; If the mask is 0 then all applications are returned"*
(OPC 10000-12 §6.5.10). `3` is `Servers | Clients`, a valid mask, and the server returns all records.
The script's expectation (*"no records"*) matches neither reading. **Fix:** expect Good with all
records for `3`, and use a value with an undefined bit (for example `4`) for the invalid case.

### C45. Historical Access Read Raw `Err-025.js` expects `BadNotSupported`

- **Test:** `maintree/Historical Access/Historical Access Read Raw/Test Cases/Err-025.js`, line 32
- **Error:** *"Results[ 0].StatusCode did not match expected results. Received: BadHistoryOperationUnsupported"*

The script reads raw history of a Static Scalar node with `Historizing = FALSE` and the HistoryRead
access-level bit and accepts only `BadNotSupported`. Part 4 §5.11.3.4 (Table 52) defines
`Bad_HistoryOperationUnsupported` for *"The requested history operation is not supported for the
requested node"* and does not list `Bad_NotSupported`. The reference server returns
`Bad_HistoryOperationUnsupported` for `ns=2;s=Scalar_Static_NonHistorizing_Boolean`. Historical Access
Delete Value `dat-005.js`, `dat-Err-001.js` and `Err-004.js` already accept both codes. **Fix:** accept
`BadHistoryOperationUnsupported` as well.

### C46. Address Space Atomicity `001.js` only sees the first 10000 variables

- **Test:** `maintree/Address Space Model/Address Space Atomicity/Test Cases/001.js`, lines 10 and 42
- **Skip:** *"No node found that have the NonatomicRead or NonatomicWrite flag in the AccessLevelEx attribute set"*

`001.js` calls `FindObjectsOfType(BaseVariableType, MaxNodesToReturn: 10000)` on the CTT cache. The result
is sorted by the NodeId string (`i=10020`, `i=104`, …, `ns=10;…`, `ns=2;…`) and cut at 10000 entries. On
the reference server the 5000 `ns=2;s=Scalar_Simulation_Mass_*`/`Scalar_Static_Mass_*` variables fill the
list before any `ns=2;s=Scalar_Static_*` variable, so `Scalar_Static_NonatomicReadWrite` is never read.
Line 42 tests `value >> 8 & 3 !== 0`, which evaluates as `(value >> 8) & (3 !== 0)` and detects only
NonatomicRead. `/Advanced/Test Tool/Address Space Model/UaNodesToIgnore` is no workaround: entries match
as substrings, so `ns=2;s=Scalar_Static_Mass` also drops `ns=2;s=Scalar` and its subtree. The reference
server therefore also exposes `ns=2;s=AccessRights_AccessAll_NonatomicReadWrite`, which sorts before the
mass variables. **Fix:** filter by AccessLevelEx before limiting the result (or page through all
variables), test `((value >> 8) & 3) !== 0`, and match UaNodesToIgnore entries by NodeId.

### C47. View Basic 2 `015.js` compares browse results by position

- **Helper:** `library/ServiceBased/ViewServiceSet/Browse.js`, `AssertArrayContainsReferences` (line 387)

`015.js` compares the references of a Browse filtered by ReferenceType with the matching references of an
unfiltered Browse, index by index (*"Expected reference does not match browsed reference"*). Part 4 §5.9.2
does not define an order for the returned references. The reference server now returns filtered references
in the order of an unfiltered Browse, so the case passes. **Fix:** compare the reference sets without regard
to order, as `AssertNodeReferencesInListNotOrdered` does.
### C32. Monitor Basic `038.js` judges a RevisedSamplingInterval of 0 against a project setting

- **Test:** `maintree/Monitored Item Services/Monitor Basic/Test Cases/038.js`, lines 7 and 15–21
- **Warning:** *"Expected CreateMonitoredItems.Results[0].RevisedSamplingInterval to be different than the
  requested 0 value"*, plus a manual-verification entry

The script requests SamplingInterval 0 and warns when the server returns 0, unless
`/Server Test/Capabilities/Fastest Sampling Interval Supported` is 0. Part 4 §7.21 defines 0 as "the fastest
practical rate", and a Variable whose MinimumSamplingInterval is 0 is monitored continuously (Part 3
§5.6.2), so a revised interval of 0 is correct for exception-based items. The reference server's static
scalar nodes declare MinimumSamplingInterval 0 and report changes by exception, and
`Server.ServerCapabilities.MinSupportedSampleRate` is 0 (`SubscriptionManager.CalculateRevisedSamplingInterval`).
The project setting is 50 because `library/Base/Objects/monitoredItem.js` line 58 uses it as the default
sampling interval of every MonitoredItem, so setting it to 0 to silence `038.js` would change all other
Monitored Item test cases. **Fix:** compare with the node's MinimumSamplingInterval or the server's
`MinSupportedSampleRate` (both already read by `library/Base/serverCapabilities.js`), and accept 0 when
either is 0.

### C33. Monitor Value Change V2 `020.js` requires every ByteString element to be four bytes long

- **Test:** `maintree/Monitored Item Services/Monitor Value Change V2/Test Cases/020.js`, lines 34–42
- **Skip:** *"The byteString elements (0, 1, and 2) are too small and should be increased to 4-characters as a minimum."*

The message names elements 0–2, but lines 34–38 take the minimum length of **all** elements, and
IndexRange cases 2–4 (lines 48–50) use that minimum for the first and last three strings. A single
short element anywhere in the array skips the test. The reference server's static
`Scalar_Static_Arrays_ByteString` had a 1-byte and a 3-byte element at indexes 4 and 5; its sample value now
keeps every element at least four bytes long, and the test passes. **Fix:** compute the minimum over the
elements the index ranges select (the first and last three) and report the real requirement.

### C34. Node Management `RequestedNewNodeId()` ignores `RequestedNodeId_Namespace`

- **Helper:** `maintree/Node Management Services/Node Management Add Node/Test Cases/initialize.js`,
  `CUVariables.RequestedNewNodeId` (lines 131–153)
- **Effect:** with `/Server Test/NodeIds/NodeManagement/RequestedNodeId` enabled, every AddNodes item requests
  `ns=1;s=stringNNN`

Logged on 2026-09-14 in a project copy with `RequestedNodeId` = 2, `RequestedNodeId_IdString` = 2 and
`RequestedNodeId_Namespace` set to 2, and again to 3: the request always carried `ns=1;s=string001`. The
function assigns `n.NamespaceIndex` (line 136) before `n.setIdentifierString(...)` (line 140); the namespace
index does not survive into the request. Namespace 1 is the reference server's application URI namespace, which has no node manager, so
the server correctly answers `BadNodeIdRejected`, and Add Node `001.js`–`003.js`, `Err-003.js`, `Err-005.js`
and `Err-008.js` fail. The same server accepts `ns=2;s=string001` (checked in-process). Client-specified
NodeIds therefore cannot be enabled, and `Err-008.js` (issue 9) cannot test duplicates. **Fix:** set the
identifier first and the namespace index afterwards, or build the id with
`UaNodeId.fromString( "ns=" + ns + ";s=string" + n )`.

### C35. Security User Anonymous `initialize.js` selects the `opc.wss` endpoint

- **Test:** `maintree/Security User Token/Security User Anonymous/Test Cases/initialize.js`, lines 42–60;
  `002.js`, lines 10–11
- **Error:** *"OpenSecureChannel( MessageSecurityMode: SignAndEncrypt; RequestedSecurityPolicyUri: …Basic256Sha256 );
  Result = BadNotSupported"*

`initialize.js` skips only `http` endpoints and keeps the **last** SignAndEncrypt endpoint that allows
Anonymous. The reference server lists its `opc.wss://…:62543` endpoint after the `opc.tcp` ones, so
`epSecureEncrypt` is the WebSocket endpoint (logged: transport `wss-uasc-uabinary`), which the CTT client
cannot open; `BadNotSupported` comes from the CTT. **Fix:** filter endpoints by the transports the CTT supports
(`opc.tcp`), as `UaEndpointDescription.Find` should for WebSocket URLs too (see C16).

### C36. `UaEndpointDescription.FindTokenType` rewrites the cached endpoints, so Security User Name Password 2 `015.js` reports duplicate PolicyIds

- **Helper:** `library/ClassBased/UaE.js`, lines 54–56 (called from `UaEndpointDescription.Find`, line 105)
- **Test:** `maintree/Security User Token/Security User Name Password 2/Test Cases/015.js`, line 52
- **Error:** *"The PolicyId: 2, is used for multiple UserIdentityTokens … Difference found: SecurityUri: , and:
  http://opcfoundation.org/UA/SecurityPolicy#Basic256Sha256"* (four times)

When a UserTokenPolicy has an empty SecurityPolicyUri, `FindTokenType` assigns the endpoint's
SecurityPolicyUri to it **in `gServerCapabilities.Endpoints`** instead of to the clone it returns. The CU's
`initialize.js` calls `Find( { SecurityMode: SignAndEncrypt, TokenType: UserName } )`, so the UserName
policy (`2`) of every SignAndEncrypt endpoint now shows Basic256Sha256 while the same policy on the Sign
endpoint still shows an empty string. `015.js` compares these copies and reports a PolicyId reused for
different configurations. Logged on 2026-09-14: the server returned `2` with an empty SecurityPolicyUri on all
three Basic256Sha256 endpoints; only the SignAndEncrypt copies changed inside the CTT. An empty
SecurityPolicyUri means "use the endpoint's policy" (Part 4 §7.41), so the server's policies are also
equal in effect. Separately, `015.js` line 20 indexes `foundTokens[i]` with the endpoint index. **Fix:**
set the SecurityPolicyUri on the returned clone only, and index `foundTokens` with the token position.

### C37. Session Base secure test cases send CreateSession with the `opc.wss` EndpointUrl

- **Tests:** `maintree/Session Services/Session Base/Test Cases/Err-002.js`, `Err-005.js` and
  `Err-022.js`, line 14 (`Test.Session.Execute( { EndpointUrl: epSecureEncrypt.EndpointUrl } )`)
- **Helpers:** `maintree/Session Services/Session Base/Test Cases/initialize.js`, lines 27–37;
  `library/ClassBased/UaH.js`, line 64 (`HostnameFromUrl`)
- **Error:** *"Expected CreateSession.Response.ServerCertificate to contain valid information."*, with the
  warning *"UaPkiCertificate.IsValid(...) for Endpoint=opc.wss://…/Quickstarts/ReferenceServer/ Expected
  hostname in EndpointUrl ('') to match the Endpoint in the Server's Certificate"*

`initialize.js` skips endpoints whose URL starts with `http` and keeps the **last** SignAndEncrypt
endpoint in `epSecureEncrypt`. The reference server lists its `opc.wss` endpoints after the
`opc.tcp` ones, so the scripts open a UA TCP SecureChannel and then send that channel's CreateSession
with the `opc.wss://` EndpointUrl. The server accepts the request and returns its certificate.
`CreateSession.js` line 183 then checks the certificate against the request's EndpointUrl through
`UaPkiCertificate.IsValid`, and `HostnameFromUrl` only matches `opc.tcp` and `http(s)` URLs
(`^(?:opc.tcp|http)(?:s)?\://([^/]+):`). The host name is therefore empty and the certificate
check fails. The server certificate contains the machine's host name, and Session Base `004.js`
validates the same certificate successfully over `opc.tcp`. Related to C16 (WebSocket transport
profiles) and to C35 of #4486 (Security User Anonymous `initialize.js` also selects the `opc.wss` endpoint).
**Fix:** select `epSecureEncrypt` by `TransportProfileUri`
(`http://opcfoundation.org/UA-Profile/Transport/uatcp-uasc-uabinary`) or by the scheme of the channel
the test opens, and let `HostnameFromUrl` accept any `scheme://host:port` URL (`opc.wss`, `opc.https`).

### C38. Subscription Durable `012.js` does not handle a denied diagnostics Browse

- **Test:** `maintree/Subscription Services/Subscription Durable/Test Cases/012.js`, lines 23–37
- **Errors:** *"Browse.Results[0].StatusCode is: BadUserAccessDenied"* (line 23), then
  *"Read.Response.ResponseHeader.ServiceResult is Bad: BadNothingToDo"* (line 30)

Step 3 reads `Server.ServerDiagnostics.EnabledFlag` and, when it is TRUE, browses
`SubscriptionDiagnosticsArray` to find the durable subscription's `MaxLifetimeCount`. The CU session
uses the CTT's default SecurityMode None channel. The reference server only lets a SecurityAdmin over
SignAndEncrypt see server-wide subscription diagnostics (`DiagnosticsNodeManager.OnReadUserRolePermissions`
/ `HasApplicationSecureAdminAccess`), because they reveal other clients' subscriptions, so Browse
returns `Bad_UserAccessDenied`, a valid operation result (Part 4 §7.38.2). The script ignores the Browse
status and reads an empty node list, which the server correctly rejects with `Bad_NothingToDo`
(Part 4 §5.10.2). Line 37 has the same missing-braces pattern as issue 18
(`if( … ) addError( … ); result = false;`), and `diagsObject` is undefined when no entry matches.
Steps 4–6 (lifetime honoured after SetSubscriptionDurable, reset by ModifySubscription) pass.
**Fix:** check `BrowseHelper.Response.Results[0].StatusCode` and skip Step 3 with a message when it is
Bad or has no references, add braces on line 37, and guard `diagsObject`.

### C39. A & C `AlarmCollector.GetCallTime()` returns an unset time, so Comment skips every alarm type

- **Helper:** `library/AlarmsAndConditions/AlarmCollector.js`, lines 1839–1842
  (`return new UaDateTime( callHelper.ServerTimeOfCall )`)
- **Tests:** A & C Comment `Test_001.js`–`Test_004.js` (skip); A & C Enable `Test_002.js` (fails, C12)
- **Result:** *"0 tests passed 1 tests skipped (retry count 3)"* for every alarm type

`CallHelper.ServerTimeOfCall` is never assigned, so the call time is `0001-01-01T00:00:00Z`.
Comment `Test_001.js` line 86 accepts the comment event only if
`CommentTime.msecsTo( eventTime ) >= 0`; the 32-bit millisecond difference from year 1 is negative
(about −838,500,000 on 2026-09-14), so every comment event is treated as unrelated and
`RestartSkipped` (lines 2313–2335) gives up after three retries. Logged values from one run: call
at `00:25:54.805Z`, response timestamp `00:25:54.816Z`, comment event `Time` `00:25:54.811Z` carrying
the expected comment text, `CommentTime` `0001-01-01T00:00:00Z`. Enable `Test_002.js` lines 291 and
301 fail the same way ("Unexpected event time, differs by ..."). Acknowledge and Confirm call the
same helper but short-circuit the comparison (`IgnoreEventByCallTime` returns false, lines
2356–2358), so they are unaffected. **Fix:** set `ServerTimeOfCall` from the Call response (and
compare it with a tolerance, because the condition event is created before the response is sent),
or use the request time corrected by the device time differential.

### C40. A & C Limit/Level CUs create their filter subscriptions on a session that has timed out

- **Tests:** A & C Exclusive Limit, Exclusive Level, Non-Exclusive Limit and Non-Exclusive Level
  (all use `maintree/Alarms and Conditions/A and C Base/Limit/Test Cases/`), `Test_003.js`–`Test_006.js`
- **Helper:** `library/AlarmsAndConditions/ConformanceHelpers/limithelper.js`, lines 72–79
- **Error:** 5× *"CreateSubscription.Response.ResponseHeader.ServiceResult is Bad: BadSessionIdInvalid"*
  in `initialize.js`, then each test case runs to the maximum test time (3 × Alarm Cycle Time)

`initialize.js` connects the CU session (line 23) and creates the collector (line 37). When the
collector starts the alarm thread, `InitialEventCapture` waits one full Alarm Cycle Time on the alarm
thread's own session (`AlarmCollector.js` lines 305–319) while the CU session sends nothing.
`LimitHelper` (line 47) then creates its five filter subscriptions on that CU session. With the
default Alarm Cycle Time (60 s) and `/Server Test/Session/RequestedSessionTimeout` (60000 ms) the
server has already closed the session as required by Part 4 §5.7.2. The four filter test cases
find empty buffers and each waits 180 s, so every CU takes about 14 minutes when it is the first
A&C CU in the CTT process (in a whole-group run only the first A&C CU pays the capture, and the
Limit CUs take about 100 s). `Test_005.js` line 22 also sets `TestName = "Test_003"`. **Fix:** create
the `LimitHelper` before the initial capture, keep the CU session alive during the capture, or use
the alarm thread session for the filter subscriptions.

### C41. A & C Alarm `Test_002.js` always runs to the maximum test time

- **Test:** `maintree/Alarms and Conditions/A and C Alarm/Test Cases/Test_002.js`, line 38;
  `initialize.js`, lines 15–36

`CanRunTest` returns false for AcknowledgeableConditionType events (`CanRunAlarmCondition`, line 22–23)
and `Test_002.js` returns without touching a counter or calling `AddIgnoreSkips`. The collector picks
one condition per alarm type that sent an event (`AlarmCollector.GetConditionIdsToTest`), so a server
that exposes an AcknowledgeableCondition instance keeps a condition in `TestConditionIds` that never
gets a result, and `IsTestComplete` only ends the test at 3 × Alarm Cycle Time (180 s by default).
Line 22 also compares with `Identifier.ConditionId` where `Identifier.ConditionType` is meant.
**Fix:** increment `TestsSkipped` (or set IgnoreSkip) for types that `CanRunTest` rejects.

### C42. A & C Enable `Test_003.js` depends on all alarm types going active within cycle/10

- **Test:** `maintree/Alarms and Conditions/A and C Enable/Test Cases/Test_003.js`, lines 64–68, 89,
  129–132, 201–215, 233–237

A condition gets a test case only for an active event seen while `RefreshState` is still `Unknown`
(line 89). The first disable sets the ConditionRefresh time to Alarm Cycle Time / 10 later (6 s by
default, lines 64 and 129–132); after the refresh (line 207) no new test cases are created. A
server whose alarm types go active at different times therefore leaves some types without a result
and the test runs to 3 × Alarm Cycle Time. Against the reference server this happened in 6 of 7 runs while its boolean and analog alarm
sources used different periods, and still in 3 of 5 once they shared the limits but stepped on
timers that drifted up to one simulation tick apart, because the booleans then reported in a later
publish. With both sources stepping on the same interval boundaries it passed in 6 of 6 runs. The
conditions it disabled are only re-enabled when the RefreshEnd event arrives (line 237). **Fix:** keep accepting first active events
until the refresh is started for all non-ignored types, or mark types without an active event as
skipped when the refresh is issued.

### C43. A & C Enable `Err_004.js` feedback burst; the CTT alarm thread then drops received events

- **Test:** `maintree/Alarms and Conditions/A and C Enable/Test Cases/Err_004.js`, lines 25–40

For *every* event of a condition the test calls Disable, Disable and Enable on the alarm thread
session without keeping per-condition state. Each Disable/Enable raises a new condition event, which
triggers the same three calls again: runs recorded up to 364 passes per alarm type and 706–868
events within 15 s. In 8 of 28 Enable runs against the reference server the CTT alarm thread then
returned **no events at all** for the rest of the CU, so every following test case (`Err_004.js`,
`Err_005.js`) ran to 3 × Alarm Cycle Time.

The events are lost inside the CTT, after the server sent them and the CTT acknowledged them. One
stalled run was repeated against a server build that logs every notification message it returns
(per client handle) and every event offered to each event monitored item, with `addLog` counters in
`AlarmTester.WaitForEvents` of a project copy:

- From 09:19:05 to 09:21:49 the CTT buffers of both event items (client handles 1293 and 1294)
  returned zero events, while the data item AnalogSource on the same subscription kept growing by
  one value per second.
- In the same window the server sent 173 notification messages on that subscription carrying 435
  events for handle 1293 and 420 for handle 1294 (12–15 every 5 s, as before the burst). Each item
  received and queued every condition event (no duplicate, overflow or filter drops), and the
  Server-object event channel had no backlog.
- The subscription was deleted at the end with sequence number 365 and no unacknowledged messages,
  so the CTT received and acknowledged every message.

**Fix:** handle each condition once (`TestCaseMap`), like the other Enable test cases, which removes
the burst; and find why the alarm thread's event buffer stops filling after about 700 events arrive
within a few seconds.

### C44. A & C CertificateExpiration blocks a `--hidden` run on a modal dialog

- **Test:** `maintree/Alarms and Conditions/A and C CertificateExpiration/Test Cases/initialize.js`,
  lines 135–149

`initialize.js` opens a synchronous Yes/No message box (*"Is is possible to adjust the clock on the
server without a restart"*) before any test runs, and the test cases open further OK dialogs asking
the operator to change the server clock. In a `--close --hidden` run the dialog window
*"Certificate Expiration Operation"* is still created and waits for input, so the CTT never exits.
The CU needs an operator (and a server whose clock can be moved past a certificate's expiration
limit). **Fix:** skip dialogs in hidden/automated runs, or add a project setting that answers them.

### C48. Aggregate oracle: Minimum/Maximum ignore Uncertain values beyond the Good extremum

- **Tests:** Aggregate – Minimum, Maximum, MinimumActualTime, MaximumActualTime (`001-02.js`, `001-03.js`, …;
  24 readings per aggregate on the numeric HA nodes)
- **Error:** *"Query did not result in identical readings"*

Part 13 §5.4.3.10–§5.4.3.13: *"If Bad values exist then the Status is Uncertain_DataSubNormal. If an Uncertain value
is less than the minimum Good value the Status is Uncertain_DataSubNormal"* (greater than the maximum for
Maximum/MaximumActualTime). Since [#4477](https://github.com/OPCFoundation/UA-.NETStandard/pull/4477)
`MinMaxAggregateCalculator.ComputeMinMax` applies the second rule. In a logged run (2026-09-15, AGGDIAG project
copy, section 4 of ctt-testing.md) the server returned for `ns=2;s=Scalar_Static_Int32`, interval
`07:25:01.050Z`: value 8, `UncertainDataSubNormal` + Calculated (`0x40A40401`); the oracle expects the same value
and timestamp with `Good` (`0x00000401`). Value and timestamp agree in every differing reading; only the status
differs. **Fix:** apply the Uncertain-value rule in the oracle. Before #4477 the server ignored it too, so these
readings passed.

### C49. Aggregate oracle: ActualTime aggregates keep Raw when non-Good values make the result Uncertain

- **Tests:** Aggregate – MinimumActualTime and MaximumActualTime (228 readings each), Minimum (14 readings)
- **Error:** *"Query did not result in identical readings"*

The Calculated bit of MinimumActualTime/MaximumActualTime is *"Set Sometimes If the Status was set to
Uncertain_DataSubNormal because of non-Good values in the interval"* (§5.4.3.12/§5.4.3.13; Minimum and Maximum:
*"… if the Minimum value is not on the startTime of the interval or if the Status was set to
Uncertain_DataSubNormal because of non-Good values"*). The server (since #4477) returns
`UncertainDataSubNormal` + Calculated (`0x40A40401`, with MultipleValues `0x40A40405`) for intervals that contain a
Bad value; the oracle expects `UncertainDataSubNormal` with Raw bits (`0x40A40000` / `0x40A40404`). **Fix:** set the
Calculated bit whenever the status is Uncertain because of non-Good input.

### C50. CloseSession timestamps the request before the CTT stops the Session's SessionThread

- **Tests:** Security None `007.js`, Security Basic256Sha256 `005.js` (step 3); Subscription Publish Basic
  `cleanup.js` (its `initialize.js` starts a `SessionThread` on `Test.Session`)
- **Helpers:** `library/ServiceBased/SessionServiceSet/CloseSession.js` (`session.closeSession`),
  `library/Base/sessionThread.js`; the delay check is `library/ClassBased/UaR.js` line 239
- **Warning:** *"CloseSession.Response.ResponseHeader.Timestamp shows a delay in excess of 600ms"*

`UaR.js` compares the server's `ResponseHeader.Timestamp` with the request's `RequestHeader.Timestamp`. When a
`SessionThread` still runs on the Session, `closeSession` builds and stamps the request, stops the thread (about
550 ms) and only then sends it. Logged on 2026-09-15 (see *CloseSession latency* below): the server received each
request about 520 ms after its timestamp and answered within 2–5 ms; stopping the threads first made CloseSession
take 0–7 ms. In `007.js` the extra 41 s also let the idle channels time out on the server (see the server finding
below). **Fix:** stop the SessionThread before building the CloseSession request (or stamp the header when the
request is sent); in `007.js` stop `sessionThreads[i]` in step 3 as the cleanup branch already does.

## Needs clarification

### U1. NumberOfTransitions with TreatUncertainAsBad=true

For 24 monotonic samples (two Bad, two Uncertain), the server counts 22 transitions and the
oracle 20 (Uncertain values excluded). §5.4.3.24 excludes Bad values. §4.2.1.2 says
TreatUncertainAsBad=True makes Uncertain *"equivalent to Bad"*, which supports the oracle. The
server currently ignores TreatUncertainAsBad for this aggregate. Needs a decision before either
side changes.

### U2. Minimum2 Calculated bit on reverse reads

When the minimum is the chronologically first raw value of a reverse interval, the server sets
Calculated and the oracle does not. §5.4.3.15 sets Calculated *"unless the StartBound is the
Minimum"*. Part 11 §6.5.4.2 makes the later timestamp the start of a reverse interval, which
supports the server. Part 13 §5.4.2.2 says a reverse calculation equals the forward one, which
supports the oracle.

### U3. DurationInStateZero/NonZero with an Uncertain end bound

In an interval whose raw data is all Good but whose simple end bound is Uncertain, the server
returns Good and the oracle Uncertain, with equal values. §5.4.3.22 does not say whether an end
bound colors the region before it. The oracle is inconsistent: TimeAverage2 and Total2 over the
same interval match the server.

## Open server observations

Items in the reference server's aggregate calculators (`src/Opc.Ua.Server/Aggregates`) that the CTT
does not currently exercise:

- **AnnotationCount never returns `BadNoData` or sets `Partial`** (`CountAggregateCalculator`). The
  §5.4.3.20 table specifies BadNoData before/after the end of data; it is ambiguous whether "data"
  means Annotations or the raw archive.
- **Value-based status ignores TreatUncertainAsBad=false** (`AggregateCalculator.GetValueBasedStatusCode`).
- **DeltaBounds Uncertain-bound check is unreachable** (`StartEndAggregateCalculator`). An earlier
  `!IsGood` return means an Uncertain bound with TreatUncertainAsBad=false gives BadNoData instead of
  `UncertainDataSubNormal` (§5.4.3.30).

The last two only show once the CTT sends explicit aggregate configurations (C1). The Uncertain-value rule of
Minimum/Maximum is implemented since #4477 and shows as C48.

### Open server findings to investigate

Failures that are not explained by a known CTT defect yet. Each needs a focused reproduction before
it is classified as a server or CTT issue.

- **Auditing Connections cannot find audit events (fixed, #4480).** `011.js`/`012.js`
  (ClientAuditEntryId) and `001.js`/`007.js`/`020.js` (AuditOpenSecureChannel/CreateSession/ActivateSession
  event types) failed because event items with queueSize 1 kept only the last event (see C14). They pass
  in the full run of 2026-09-15.
- **A & C Refresh `Err_004.js` / `BadSubscriptionIdInvalid` cascade (investigated 2026-09-14, not
  reproduced).** In the failing run the ten subscriptions that `Err_004.js` creates on the alarm
  thread session, and the alarm thread subscription itself, were gone when the first
  ConditionRefresh was sent after the script's 30 s wait (CTT subscriptions: 250 ms publishing
  interval, lifetime count 62 = 15.5 s). That is what a subscription expiry looks like when the
  session's publish requests stop; there is no server log from that run. Nine further runs (Refresh
  alone, Refresh2 alone, Refresh + Refresh2 three times, one of them under build load, and four
  whole-group runs) passed all 28 cases,
  and an in-process client replaying the pattern (11 subscriptions, 5 Calls × 10 ConditionRefresh,
  30 s idle, sequential publishing) kept every subscription. If it recurs, start the server with
  `-c -l` and look for `Subscription ... EXPIRED`. The replay exposed a real server defect:
  `AlarmNodeManager.CallAsync` (sample) answered every ConditionRefresh/ConditionRefresh2 after the
  first in a Call with `BadRefreshInProgress`, even for other subscriptions, so only one of the ten
  subscriptions was ever refreshed. Fixed: the check is now keyed by subscription and monitored item
  (`AlarmsAndConditionsRefreshTests.ConditionRefresh*OfDifferentSubscriptionsInOneCallSucceedsAsync`).
- **A & C Comment skips 5 of 11 test cases.** CTT defect: `Test_001.js`–`Test_004.js` compare the
  comment event time with an unset call time (C39); the server delivers the comment event with the
  expected text. The fifth skip is `Err_006.js` (*"Unable to find event that does not support
  comments"*), a coverage gap rather than a failure.
- **Slow A & C units (resolved).** See [ctt-testing.md](ctt-testing.md#6-alarms-and-conditions) for the
  timing breakdown and the recommended run. Limit/Level CUs are slow only when they run first in a CTT
  process (C40); CertificateExpiration hangs on a modal dialog (C44); Alarm `Test_002.js` always and
  Enable `Test_003.js` often ran to 3 × Alarm Cycle Time (C41, C42), and Enable intermittently stops receiving events
  after `Err_004.js` bursts (C43). The reference server's boolean
  and analog alarm sources now change state in the same simulation pass; with that Enable
  `Test_003.js` passed in 6 of 6 runs.
- **GDS (triaged 2026-09-14).** The 60 GDS errors are classified below. Server defects fixed:
  - Like filters of QueryServers/QueryApplications (`ApplicationsDatabaseBase.Match`, now the shared
    `Opc.Ua.LikePattern`, OPC 10000-4 §7.7.3). The old tokenizer returned no records or all records
    for `%_erver%`, `%e_`, `%\_%`, `%\%%`, `%[q-s]`, `%[^q-s]` and `%_ompliance%`, and accepted the
    malformed `%[a^j-l]%`. Fixes Application Directory `062.js`, `066.js`, `068.js`, `071.js`,
    `073.js` and Query Applications `013.js`, `017.js`–`019.js`, `022.js`, `024.js`. `078.js` now
    gets the expected `BadInvalidArgument` but then aborts in the CTT helper (C22).
  - QueryServers RecordIds (`LinqApplicationsDatabase.QueryServers`). The application id was used as
    the RecordId of every DiscoveryUrl record, so `StartingRecordId` paging skipped the remaining
    DiscoveryUrls of an application (§6.5.11 Table 15 returns one record per DiscoveryUrl). Fixes
    Application Directory `045.js`, `075.js`.
  - FindApplications with an empty ApplicationUri returned every application (§6.5.4: array size 0 or
    1, `Bad_InvalidArgument` for an invalid URI). Fixes Application Directory `004.js`. Other strings
    that are not a registered ApplicationUri still return an empty array, which `003.js` (up to
    MaxStringLength `X` characters) expects.

  CTT GDS rerun with the fixes: 47 errors (baseline 60); 48 in a later run where `028.js` missed its
  audit event (C23). `074.js` and Query Applications `025.js` newly fail as described in C21. A run
  against a copy of the scripts with the recommended fixes of C19, C20, C22, C23, C25 and C29 applied
  leaves 41 errors: `010.js`, `060.js`, `065.js`, `067.js`, `078.js`, `079.js` and Query Applications
  `011.js`, `016.js` then pass, `018.js` receives a correct audit event, and `005.js` shows the expected
  `Bad_EncodingLimitsExceeded` ServiceFault. That fault carries RequestHandle 0 (Part 4 §7.33: the
  requestHandle *should* be echoed even for invalid requests), a transport-level observation outside
  the GDS. CTT defects: C19–C31. Not applicable to this server: GDS AliasName
  Discovery `001.js`, `002.js`,
  `004.js` (see *CTT project configuration notes*). Application Directory `018.js` also needs the
  event queue size fix of [#4480](https://github.com/OPCFoundation/UA-.NETStandard/pull/4480) (C23).
  Spec conflict, server unchanged: §6.5.10/§6.5.11 say QueryApplications/QueryServers *"shall not
  return records with a ServerCapabilities that includes NA"*, but the CTT registers its reference
  Servers with `NA` and expects them in the results (for example `066.js`, `079.js` step 1).
- **GDS AliasName Discovery.** `001.js` finds AliasName instances in the TagVariables (`i=23479`) and
  Topics (`i=23488`) folders although no server is registered yet. `002.js`/`004.js`: the aliases and
  custom categories of a registered server are not replicated to the GDS.
- **RevisedSamplingInterval 0 (resolved, not a server defect).** Monitor Basic `038.js` warns that a
  requested SamplingInterval of 0 is returned unchanged. The configured nodes declare MinimumSamplingInterval
  0 and are reported by exception, so 0 is the correct revised value; the script compares with a project
  setting instead of the server's capabilities (C32).
- **AddNodes latency (fixed 2026-09-14).** Node Management Delete Node `Err-002.js` warned that AddNodes
  responses arrived 400–800 ms after the request (tolerance 100 ms). The test adds 15,000 variables below
  `ns=2;s=CTT` in batches of 5,000 (`MaxNodesPerNodeManagement`). Every added node searched all children
  of the parent twice (BrowseName duplicate check and the NodeVersion lookup of the model change filter), so
  the batches took 0.8, 2.1 and 4.0 s in-process. `NodeState.FindChild` now uses a BrowseName index for large
  child lists; the batches take 141, 166 and 172 ms, the CTT logs 51–72 ms per 5,000-node AddNodes, and the
  warning is gone.
- **Create/DeleteMonitoredItems timestamp warnings (timing jitter, not a server defect).** Each Monitored Item
  run shows one or two *"… Timestamp shows a delay in excess of 200ms"* warnings, on different test cases
  every time: Monitor Value Change V2 `014.js`/`018.js` in one run, Monitor Basic `007.js`/`009.js` in the
  next, Monitor Basic `014.js` in a third. A run with every non-Publish response above 50 ms logged
  (`UaR.js`, `addLog`) found two of the roughly 2,000 Create/DeleteMonitoredItems calls of Monitor Basic
  `014.js`–`016.js` (about 30 items each) above 100 ms (123 and 153 ms), and none in a Monitor Value Change V2
  run. The CTT rounds the delay up to the next 100 ms, so 153 ms is reported as "in excess of 200ms". No
  service or test case is consistently slow; the first run also overlapped builds of other sessions.
- **OpenSecureChannel revocation errors (fixed 2026-09-14).** Security Certificate Validation `002.js` warned
  that the server returned `BadCertificateIssuerRevocationUnknown`. Part 4 §6.1.3 Table 106 requires
  `Bad_SecurityChecksFailed` to be reported for the revocation check and should be reported for a missing
  revocation list. `TcpServerChannel` masked only `BadCertificateRevoked`; it now also masks
  `BadCertificateIssuerRevoked`, `BadCertificateRevocationUnknown` and `BadCertificateIssuerRevocationUnknown`
  (the CreateSession path already did).
- **Session Services stopped accepting sessions after a session timeout (fixed 2026-09-14).** Session
  Base `002.js` lets a session time out and calls ActivateSession on it. `SessionManager.ActivateSessionAsync`
  found the expired session while holding the session-manager `SemaphoreSlim` and closed it through
  `IServerInternal.CloseSessionAsync`, which ends in `SessionManager.CloseSessionAsync` waiting for the same
  non-reentrant semaphore. The activation never returned (the CTT reported *"Good"* after its 20 s
  timeout), and every later CreateSession timed out with `BadTimeout`: 19 Session Base test cases, all of
  Session Change User and the `initialize.js` of Session Cancel and Session Multiple failed. The session is
  now closed after the lock is released and ActivateSession returns `Bad_SessionClosed`
  (`SessionManagerExpiryTests`). The session monitor only checks sessions every `MinSessionTimeout` ms, so an
  activation shortly after the timeout usually reaches the expired session before the monitor does.
- **Subscription Durable `004.js` received a keep-alive after TransferSubscriptions (fixed 2026-09-14).**
  The test disconnects with DeleteSubscriptions=FALSE, waits 10 s, reconnects, transfers the durable
  subscription and expects the first Publish to return the values buffered meanwhile
  (*"Didn't receive the data from the transferred subscription"*, line 78; passed in a CU run, failed in a
  group run). While a subscription is abandoned the publish timer keeps counting its keep-alive but only
  moves ready monitored items to the publish list when a Session owns it (`Subscription.PublishTimerExpired`,
  `Session != null`). After the transfer the first Publish found the keep-alive due and nothing to
  publish, and returned an empty keep-alive although notifications were available (Part 4 §5.14.1.1). The
  data only came one Publish later. `InnerPublish` now collects ready items before it sends a keep-alive
  (`SubscriptionTests.FirstPublishAfterTransferOfAbandonedSubscriptionReturnsQueuedDataAsync`).
- **AddNodes after a child rename returns `BadNodeIdExists` (found 2026-09-15, PR interaction #4477 × #4486).**
  With both PRs merged, `AsyncCustomNodeManagerNodeManagementTests.AddNodeAsync_DuplicateBrowseNameUnderParentWithManyChildren_ReturnsBadBrowseNameDuplicatedAsync`
  (added by #4486) fails at line 250: *"expected Good result; got BadNodeIdExists"*. AddNodes without a
  RequestedNewNodeId derives the NodeId from the parent and the BrowseName
  (`AsyncCustomNodeManager.AllocateNodeIdForAddNodes` → `CreateChildNodeId`). The test renames child `Child42` and
  adds a new `Child42`; the derived NodeId is the one the renamed node still owns. Before #4477 the node was
  registered with `PredefinedNodes.AddOrUpdate`, which silently replaced the renamed live node; #4477 reserves the
  id and uses `TryAdd`, so the add now fails. Neither behavior is right: the BrowseName is free, so the server
  should allocate a different NodeId (for example fall back to `m_nodeIdFactory.NextCounterNodeId()` when the
  derived id is already registered). No CTT test case renames nodes, so the CTT run does not show it.
- **CloseSession latency (investigated 2026-09-15: CTT client, not the server).** Subscription Publish Basic
  `cleanup.js`, Security None `007.js` and Security Basic256Sha256 `005.js` warn that CloseSession responses
  arrive 500–700 ms after the request. Measured with `007.js` step 3 (74 CloseSession calls) against a server
  build that logged the arrival of every request in `TcpServerChannel.HandleIncomingMessageAsync` and the
  phases of `StandardServer.CloseSessionAsync`: the server received each CloseSession about 520 ms after the
  CTT's `RequestHeader.Timestamp` and completed it in 2–5 ms (node managers, subscriptions, SessionManager). The
  CTT's own measurement (`clientMs`) was 505–614 ms. Every one of those sessions runs a CTT `SessionThread`
  (a Read of the server status about once per second). A copy of `007.js` that calls
  `sessionThreads[i].StopThread()` before `CloseSessionHelper.Execute` spent 516–619 ms in `StopThread` and then
  0–7 ms (average 1.7 ms) in CloseSession. The CTT stamps the request, waits for its SessionThread to stop and
  only then sends the request, so the delay and the warning are client artifacts (C50).
- **Security None `007.js` / Security Basic256Sha256 `005.js` fail: the server closes an idle SecureChannel
  before its SecurityToken expires (server finding, pre-existing).** Seen 2026-09-15 on origin/master and on the
  merge of #4477/#4482/#4485/#4486: *"CloseSecureChannel().Result received BadInvalidState, but expected … Good"*
  at `007.js` line 93. The test opens 74 SecureChannels with Sessions, adds five channels without Sessions 10 s
  apart (`Min Lifetime of SecureChannel`), closes the 74 Sessions (41 s because of C50) and then expects the
  newest idle channel to close with Good. That channel requested lifetime 0 and got a 60 s token
  (`TcpMessageLimits.MinSecurityTokenLifeTime`), but `TcpTransportListener.DetectInactiveChannels` closes every
  channel without traffic for longer than `TransportQuotas.ChannelLifetime` (30 s, checked every 15 s), so the
  channel was gone after about 51 s of silence; `BadInvalidState` is the CTT's result for a channel the server
  already closed. Part 4 §5.6.2.1: *"Each SecureChannel exists until it is explicitly closed or until the last
  token has expired and the overlap period has elapsed"*; the Server shall close the oldest unused Session-less
  SecureChannel *before reaching the maximum number* of SecureChannels (here 1000). With `ChannelLifetime` =
  120000 in `Ctt.ReferenceServer.Config.xml` (no code change) `007.js` passes with the same 41 s step 3.
  **Fix direction:** do not close an open channel for inactivity while its current SecurityToken (plus the 25 %
  overlap) is still valid, and keep the oldest-unused eviction for admission at MaxChannelCount; an idle
  timeout for channels that never completed OpenSecureChannel can stay.

## CTT project configuration notes

Tests skipped because of reference server sample-data gaps or missing CTT project settings are
tracked in [#4479](https://github.com/OPCFoundation/UA-.NETStandard/issues/4479).

- **Aggregate ProcessingInterval.** Set `/Server Test/NodeIds/Static/HA Profile/Aggregates/ProcessingInterval`
  to a positive value (see issue 4).
- **Bad data entries for aggregates.** `005-05.js`/`005-06.js` need an explicit Bad data entry. Without
  it, `HAAggregateHelper.GetRequestEntry` falls back to the start entry (*"Bad Data Entry no found,
  using start data"*) or throws (*"GetRequestEntry failed due to incorrect test configuration"*). The
  reference server seeds a deterministic pattern on every history node: index mod 10 = 7 is
  `BadDataUnavailable`, index mod 10 = 9 is `UncertainSubstituteValue`, the rest Good.
- **Reference server settings for #4479.** `samples/UAReferenceServer.ctt.xml` sets:
  - `/Server Test/NodeIds/Static/All Profiles/Scalar/Bool` = `ns=2;s=Scalar_Static_NonHistorizing_Boolean`.
    Historical Access Read Raw `Err-025.js` and Delete Value `dat-Err-001.js`/`Err-004.js` take the first
    Static Scalar node as the non-historizing node. The HA Profile and Aggregate Boolean settings stay on
    `Scalar_Static_Boolean`.
  - `/Server Test/NodeIds/References/Has References of a ReferenceType and SubType` = `i=2253`. The Server
    object has `HasComponent` and `HasAddIn` references. `References_HasReferenceTypeAndSubType` loses its
    hierarchical references in the source generator
    ([#4484](https://github.com/OPCFoundation/UA-.NETStandard/issues/4484)).
  - `/Server Test/NodeIds/NodeClasses/Object` = `i=2253`, so View Basic 2 `018.js` also sees Method
    references and covers every NodeClass.
- **Base Info Diagnostics `018-1.js`–`018-3.js`** write `Server.ServerDiagnostics.EnabledFlag`, which the
  reference server allows only for the SecurityAdmin or ConfigureAdmin role over SignAndEncrypt (the
  project's `sysadmin` user). `018-1.js` warns *"Session diagnostics not available"* for the session it
  creates while diagnostics are disabled; that is expected.
- **Node Management Add Node `002.js`** adds a Variable with every enabled
  `/Server Test/NodeIds/NodeManagement/SupportedReferences` entry. Enable only hierarchical
  references valid for a Variable target (typically `Organizes`, `HasProperty`, `HasComponent`).
  Non-hierarchical references and `HasSubtype` correctly return `BadReferenceNotAllowed`
  (Part 4 §5.8.2).
- **DI Base Model.** The 14 DI Base Model CUs (the `DI ITagNameplate`/`DI IVendorNameplate` units
  under `maintree/OPC UA FX`) skip entirely: the reference server has no
  `http://opcfoundation.org/UA/DI/` or `http://opcfoundation.org/UA/FX/Data/` namespace and no FxRoot
  folder. Testing them needs a server that loads the DI and UA FX models. The same applies to every
  *UAFX* group (AutomationComponent, Base, FunctionalEntity, FxAsset: 59 CUs): `initialize.js` reports the
  missing DI and FX/Data namespaces as errors, UAFX FxRoot `001.js` fails, and the rest skips.
- **PubSub Publisher UADP CUs.** *PubSub Publisher UADP chunking*, *Defined Ordering* and *Periodic Fixed
  Settings* abort in `initialize.js` (*"ConfigurePubSubTest(): Failed to upload PubSubConfiguration to server"*,
  `library/PubSub/PubSubUtilities.js` line 1232). The reference server keeps the standard `PublishSubscribe`
  object but implements no PubSub publisher: neither the PubSubConfiguration file (`Open` is not implemented) nor
  `AddConnection`/`AddPublishedDataItems`. Not applicable; deselect the PubSub General group.
- **UserDefinedCU.** The CTT's sample custom CU (`ProfileSet_Custom.xml`) logs *"Hello error"*/*"Hello warning"*
  by design. Deselect it.
- **Discovery.** Find Servers Filter `002.js` needs at least two servers known to FindServers (an LDS);
  Find Servers Self `010.js` and Get Endpoints `009.js` need a multi-homed host or several hostnames.
  Find Servers Filter `003.js`/`006.js` and Get Endpoints `002.js` warn that `de-DE` was requested
  but `en-US` returned, because the server has no `de-DE` ApplicationName.
- **Auditing.** Auditing Connections `002.js`, `003.js`, `008.js`, `010.js`, `014.js` skip when no other
  test case in the same run produces the audit event they look for. Run the Auditing group together
  with the service groups whose actions it audits.
- **GDS target server.** Application Directory and Query Applications exercise the same code
  (`ApplicationsNodeManager` + `LinqApplicationsDatabase` from `src/Opc.Ua.Gds.Server`) on the
  reference server in `--ctt` mode and on a dedicated GDS, so either target gives the same results.
  The reference server keeps its GDS database in memory (empty `DatabaseStorePath`), so every server
  start begins with an empty directory; a GDS with a JSON database (`DatabaseStorePath` set, as in the
  `opc.tcp://localhost:58810/GlobalDiscoveryServer` sample GDS project) keeps records of aborted
  earlier runs and changes the record counts every test expects. Delete that file before each run.
  Run the four GDS CUs in one run: the test cases within a CU depend on the records registered by
  its `initialize.js` and earlier test cases (`012.js` and `019.js` unregister and re-register them).
- **GDS AliasName Discovery.** Not applicable: this CU belongs to the *GDS AliasName Server Facet*
  (OPC 10000-17 Annex C.2: aggregate the AliasNames of registered Servers into TagVariables/Topics
  and add their ServerUri to ServerArray), which `Opc.Ua.Gds.Server` does not implement, so `002.js`
  and `004.js` fail on any GDS built from it. `001.js` additionally fails only against the reference
  server: it expects empty TagVariables (`i=23479`) and Topics (`i=23488`) folders on a GDS without
  registrations, but the reference server is itself an AliasName Server and exposes its own aliases
  there (`Devices.Heater_Power`, `TIC101_PV`, `ServerEvents`, …). Deselect the CU until the facet is
  implemented. `005.js`–`015.js` also need two or three AliasName sources configured
  (`/Server Test/GDS/AliasName Discovery/AliasName Source N URL`).
- **GDS LDS-ME Connectivity.** `initialize.js` skips the CU unless QueryApplications with
  `ServerCapabilities = ["LDS"]` returns a record: register an LDS/LDS-ME with the GDS first. The
  reference server does not include an LDS.
- **Node Management client NodeIds.** Leave `/Server Test/NodeIds/NodeManagement/RequestedNodeId` disabled.
  When enabled, scripts 1.05.513 request NodeIds in namespace 1 regardless of `RequestedNodeId_Namespace`
  (C34), and six Add Node test cases fail with `BadNodeIdRejected`. `Err-008.js` therefore keeps failing
  (issue 9). The setting's default namespace value (911) is meaningless.
- **Monitored Item Services manual test cases.** Monitor Basic `036.js`, Monitor Complex Value `001.js`–`003.js`,
  Monitor Events `002.js`/`003.js`, Monitor Queueing `013.js`/`014.js`, and the Monitor Complex Event Filter
  and Monitor QueueSize_ServerMax CUs are *Not Implemented* (manual or test-lab) in scripts 1.05.513. So are
  Node Management Add Ref and Delete Ref.
- **Security groups need the CTT PKI, not `-a`.** See [ctt-testing.md](ctt-testing.md#9-security-groups).
  With `-a` every negative certificate test fails spuriously.
- **Security General coverage.** In scripts 1.05.513, 50 of the 53 CUs contain only *Not Implemented* test
  cases (Push/Pull Model, No Application Authentication, Security Administration, Certificate Administration, Default ApplicationInstance
  Certificate, all Role and User Management CUs, TLS, Time Sync, KeyCredential, broker authentication,
  ECC, LegacySequenceNumber, Encryption/Signing/Policy Required, SecurityPolicy Support). Automated test
  cases exist only in Security Certificate Validation and Security None CreateSession ActivateSession
  (and its 1.0 variant).
- **Security Certificate Validation skips.** `004.js` skips because the CTT stack cannot send an empty
  client certificate. `049.js`/`050.js` need a Basic128Rsa15 endpoint for their SHA-1 certificates; the
  reference server does not offer that deprecated policy (not applicable).
- **Security User Token coverage.** Security User Anonymous `003.js` needs a secure endpoint without the
  Anonymous token; the CTT configuration offers Anonymous on every endpoint. Security User Name Password 2
  `002.js` needs a UserName token policy with SecurityPolicy `#None` on an encrypted endpoint (password sent
  unencrypted inside the channel); the reference server always encrypts passwords (not applicable).
  `012.js` passes only because `/Server Test/Session/LoginNameAccessDenied` (`username`) is not a known user
  and the server answers unknown credentials with `BadUserAccessDenied`; the reference server has no user
  that authenticates but is denied access. Security Invalid user token, the Kerberos, JWT, Authority Profile
  and Token Unencrypted CUs, and X509 `003.js`/`012.js`, are *Not Implemented*.
- **Monitor Value Change V2 `020.js`** needs **every** element of the configured ByteString array to be at
  least four bytes long (C33). The reference server's sample value satisfies that since 2026-09-14.
- **Alarms and Conditions coverage.** The single-case CUs (ConditionClasses, Condition Sub-Classes,
  Suppression by Operator, Silencing, OutOfService, On-Off Delay, Re-Alarming, First in Group Alarm,
  Audible Sound, Discrepancy, Trip, A&E Wrapper Mapping, Dialog) contain only manual
  (*Not Implemented*) test cases.
- **A & C Shelving coverage.** `/Server Test/Alarms and Conditions/Chattering Alarms` is empty, so
  `UseChatteringAlarms` sets IgnoreSkip on every type and `Test_003.js`–`Test_005.js`,
  `Test_007.js`–`Test_010.js` and `Err_001.js`–`Err_003.js` finish immediately without testing
  anything. The reference server has no alarm that stays active across transitions, so there is no
  condition to configure there yet.
- **A & C Alarm Cycle Time and session timeout.** `/Server Test/Alarms and Conditions/Alarm Cycle Time`
  sets the initial event capture (1 ×), the maximum time of every collector test case (3 ×) and the
  Enable `Test_003.js` refresh delay (1/10). Keep it below
  `/Server Test/Session/RequestedSessionTimeout` (ms) when a Limit/Level CU can be the first A&C CU of a
  run (C40).
- **Subscription Publish Min 05 `003.js`** creates 5 subscriptions in each of half the
  `/Server Test/Capabilities/Max Supported Sessions` sessions (75 → 38 sessions, 190 subscriptions). With
  `/Server Test/Capabilities/Max Supported Subscriptions` = 100 (the server's `MaxSubscriptionCount` in
  `Ctt.ReferenceServer.Config.xml`) it warns *"Not enough subscriptions for all sessions. Reducing session
  amount to 20"* and still passes. The warning is informational; raising both limits to 200 removes it.
- **Session and Subscription coverage.** Manual (*Not Implemented*) test cases: Subscription Basic `072.js`,
  `073.js`; Subscription Multiple `001.js`–`003.js`; Subscription Publish Basic `005.js`–`007.js`, `Err-001.js`;
  Subscription PublishRequest Queue Overflow `001.js`, `002.js`; Subscription Durable `013.js`. Subscription
  Durable StorageLevel High/Medium/Small and Subscription Retransmission Queue contain only
  `NoTestCaseDefined.js`. Skipped by the scripts: Subscription Basic `067.js` (under Working Group review),
  Subscription Durable `006.js` (server restart) and `009.js` (events), Subscription Transfer `Err-010.js`
  (no script), Session Base `Err-009.js` (the CTT has no Kerberos token) and `Err-023.js` (the server offers
  SecurityPolicy None, correct for `--ctt`).
- **Historical Access coverage.** Every Historical Access CU except *Read Raw* contains only
  `NoTestCaseDefined.js` in scripts 1.05.513. Insert/Replace/Update/Delete (values and events),
  Annotations, ServerTimestamp, Modified, Time Instance and Structured Data are not tested.
