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
branches in `ValidateRetain`. This also covers Confirm `Test_001.js` for Discrete, OffNormal and
SystemOffNormal alarms.

### C11. Alarm `Test_004.js` calls `ReadHelper` re-entrantly from the alarm callback

The global `ReadHelper` runs synchronously inside the alarm callback and fails client-side with
`BadInvalidState`. The server resolves and reads every AlarmCondition `InputNode` with Good.
**Fix:** queue the Read outside the callback, or use a helper/session valid on that thread.

### C12. Enable `Test_002.js` passes four arguments to a three-argument `AddMessage`

`collector.AddMessage(testCase, category, conditionId, reason)` drops `reason`. The result is empty
`Error: ns=...` entries that hide which check failed. **Fix:** combine `conditionId` and `reason`
into the third argument.

### C13. Base Info Currency `004.js` drops the CurrencyUnit Exponent

The server's EUR CurrencyUnit is `NumericCode=978`, `Exponent=2`, `AlphabeticCode=EUR`,
`Currency=Euro`, encoded with the Int16 NumericCode followed by the SByte Exponent (prefix
`D2 03 02`). The script reports an empty Exponent, so the `toCurrencyUnitType()` conversion loses
the field. **Fix:** decode the SByte Exponent after NumericCode.

### C14. Auditing Connections cannot find entries by `ClientAuditEntryId`

- **Helper:** `library/…/AuditValidationHelper.js`, line 346 (*"Unable to Find Entry for ClientAuditEntryId"*)

The server emits AuditOpenSecureChannel, AuditCreateSession, AuditActivateSession and
AuditCloseSession events carrying `ClientAuditEntryId` from `RequestHeader.AuditEntryId`. A
subscriber with the CTT's `AuthenticatedUser` role receives them (Part 3 §8.55). The CTT's
`Test.Audit` collection still does not find them. The cause lies in the CTT's audit subscription
parameters, the `FindEntryVerbose` WhereClause, the `ClientAuditEntryId` comparison or publish
timing; it has not been pinpointed to a line.

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
- **Uncertain values ignored in Minimum/Maximum** (`MinMaxAggregateCalculator`). §5.4.3.10/11 make
  the result Uncertain when an Uncertain value is below (above) the Good minimum (maximum).
- **Value-based status ignores TreatUncertainAsBad=false** (`AggregateCalculator.GetValueBasedStatusCode`).
- **DeltaBounds Uncertain-bound check is unreachable** (`StartEndAggregateCalculator`). An earlier
  `!IsGood` return means an Uncertain bound with TreatUncertainAsBad=false gives BadNoData instead of
  `UncertainDataSubNormal` (§5.4.3.30).

The last three only show once the CTT sends explicit aggregate configurations (C1).

### Open server findings to investigate

Failures that are not explained by a known CTT defect yet. Each needs a focused reproduction before
it is classified as a server or CTT issue.

- **Auditing Connections cannot find audit events.** `011.js`/`012.js` (ClientAuditEntryId) and
  `001.js`/`007.js`/`020.js` (AuditOpenSecureChannel/CreateSession/ActivateSession event types). See
  C14; a separate root-cause investigation is running.
- **A & C Refresh `Err_004.js` invalidates the alarm subscription.** The test adds 10 event
  subscriptions and calls ConditionRefresh five times near-simultaneously, expecting Good or
  `BadRefreshInProgress`. The call returns `BadSubscriptionIdInvalid`. Afterwards the CTT alarm
  subscription stays invalid, so A & C Refresh `cleanup.js` and A & C Refresh2 `initialize.js`,
  `Test_002.js`–`Test_004.js` fail with `BadSubscriptionIdInvalid`. Check per-session subscription
  limits and subscription handling under concurrent ConditionRefresh.
- **A & C Comment skips 5 of 11 test cases.** For every alarm type the CTT reports *"0 tests passed
  1 tests skipped (retry count 3)"*: the alarms did not reach the state the test needs within three
  retries. Check the CTT-mode alarm simulation timing.
- **Slow A & C units.** A & C Exclusive/Non-Exclusive Limit/Level (28 cases) and
  A & C CertificateExpiration did not finish within 15 minutes each and have no results yet; Shelving
  alone takes about 2.5 minutes, Comment about 4. Run them individually with a long timeout.
- **GDS QueryServers / QueryApplications Like filters.** Against the GDS node manager in CTT mode
  (`src/Opc.Ua.Gds.Server`, `ApplicationsDatabaseBase.IsMatchPattern`):
  - Application Directory `066.js`, `068.js`, `071.js`, `073.js`, `075.js` and Query Applications
    `011.js`–`024.js` return the wrong number of records for patterns such as `%_erver%` and `[%]`.
  - `067.js`/`069.js`: patterns containing an escaped `%` or `\` are rejected with
    `BadInvalidArgument` instead of Good.
  - `078.js` (`%[a^j-l]%`, an invalid `^` position) and Query Applications `038.js`
    (applicationType = max UInt32) are accepted with Good instead of `BadInvalidArgument`.
  - `036.js`: a registered reverse-connect client's DiscoveryUrl does not start with `rcp+`.
  - `079.js` then aborts in `library/GDS/MethodCalls.js:286` on a null `servers` result.
- **GDS AliasName Discovery.** `001.js` finds AliasName instances in the TagVariables (`i=23479`) and
  Topics (`i=23488`) folders although no server is registered yet. `002.js`/`004.js`: the aliases and
  custom categories of a registered server are not replicated to the GDS.
- **RevisedSamplingInterval 0.** Monitor Basic `038.js` warns that a requested SamplingInterval of 0 is
  returned unchanged. Part 4 says 0 means the fastest practical rate, and the revised value should
  report that rate.
- **AddNodes latency.** Node Management Delete Node `Err-002.js` reports AddNodes responses 300–600 ms
  after the request (tolerance 100 ms).

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
- **Historical Access Read Raw `Err-025.js`** is skipped unless a Static Scalar node with
  Historizing=FALSE and HistoryRead access is configured.
- **Historical Access `Err-012.js`** needs a historizing node that denies HistoryRead to the test
  identity; a non-historizing node yields `BadHistoryOperationUnsupported` instead of
  `BadUserAccessDenied`.
- **Node Management Add Node `002.js`** adds a Variable with every enabled
  `/Server Test/NodeIds/NodeManagement/SupportedReferences` entry. Enable only hierarchical
  references valid for a Variable target (typically `Organizes`, `HasProperty`, `HasComponent`).
  Non-hierarchical references and `HasSubtype` correctly return `BadReferenceNotAllowed`
  (Part 4 §5.8.2).
- **DI Base Model.** The 14 DI Base Model CUs (the `DI ITagNameplate`/`DI IVendorNameplate` units
  under `maintree/OPC UA FX`) skip entirely: the reference server has no
  `http://opcfoundation.org/UA/DI/` or `http://opcfoundation.org/UA/FX/Data/` namespace and no FxRoot
  folder. Testing them needs a server that loads the DI and UA FX models.
- **Discovery.** Find Servers Filter `002.js` needs at least two servers known to FindServers (an LDS);
  Find Servers Self `010.js` and Get Endpoints `009.js` need a multi-homed host or several hostnames.
  Find Servers Filter `003.js`/`006.js` and Get Endpoints `002.js` warn that `de-DE` was requested
  but `en-US` returned, because the server has no `de-DE` ApplicationName.
- **Auditing.** Auditing Connections `002.js`, `003.js`, `008.js`, `010.js`, `014.js` skip when no other
  test case in the same run produces the audit event they look for. Run the Auditing group together
  with the service groups whose actions it audits.
- **GDS AliasName Discovery.** `005.js`–`015.js` need two or three AliasName sources configured
  (`/Server Test/GDS/AliasName Discovery/AliasName Source N URL`).
- **Monitor Value Change V2 `020.js`** needs the ByteString elements 0–2 of its configured array to be at
  least 4 characters long.
- **Alarms and Conditions coverage.** The single-case CUs (ConditionClasses, Condition Sub-Classes,
  Suppression by Operator, Silencing, OutOfService, On-Off Delay, Re-Alarming, First in Group Alarm,
  Audible Sound, Discrepancy, Trip, A&E Wrapper Mapping, Dialog) contain only manual
  (*Not Implemented*) test cases.
- **Historical Access coverage.** Every Historical Access CU except *Read Raw* contains only
  `NoTestCaseDefined.js` in scripts 1.05.513. Insert/Replace/Update/Delete (values and events),
  Annotations, ServerTimestamp, Modified, Time Instance and Structured Data are not tested.
