# CTT (Compliance Test Tool) failing test cases

This file lists the test cases of CTT 1.05.06 (scripts 1.05.513) that fail, warn or skip against the OPC
Foundation .NET reference server (`ConsoleReferenceServer --ctt`) because of a CTT defect. Each entry has a
short abstract and the Mantis issue that tracks it; Mantis has the details. Defects that are not filed,
spec questions, follow-ups for the server and CTT project configuration notes come after the tables. The
procedure for running the CTT is in [ctt-testing.md](ctt-testing.md).

- **(fixed)** means the Mantis issue is resolved and the fix is in the CTT script repository. The fix ships
  in a script build after 1.05.513, so an installed 1.05.513 still shows the failure.
- The ids (1–19, C1–C66, U1–U6) are stable references for notes and commit messages. Missing ids were
  withdrawn or no longer fail.
- Mantis states were last checked on 2026-10-19.

## Filed in Mantis

### Aggregates

| Id | Failing test | Abstract | Mantis |
| --- | --- | --- | --- |
| 3 | Aggregate – Base `002-01.js`…`002-04.js` (every Aggregate CU) | The multi-node path of `HAAggregateHelper.js` dereferences `possibleNodeId` without a guard. | [11251](https://mantis.opcfoundation.org/view.php?id=11251) |
| 4 | Aggregate – Base `Err-004.js` | Sends an equal-time request when the ProcessingInterval setting is blank. | [11252](https://mantis.opcfoundation.org/view.php?id=11252) |
| C1 | Aggregate – Base `003-0x.js`, `004-0x.js`, `Err-004.js`; NumberOfTransitions `003-02.js`, `004-02.js` | The harness always sends `UseServerCapabilitiesDefaults = TRUE`, so the server uses its defaults while the CTT applies the test configuration. | [11420](https://mantis.opcfoundation.org/view.php?id=11420) (duplicate: [11466](https://mantis.opcfoundation.org/view.php?id=11466)) |
| C2 | Aggregate – AnnotationCount | Counts raw values instead of Annotations. | [11421](https://mantis.opcfoundation.org/view.php?id=11421) |
| C3 | Aggregate – WorstQuality2 | Also includes the end bound. | [11422](https://mantis.opcfoundation.org/view.php?id=11422) |
| C4 | Aggregate – DurationInStateZero / DurationInStateNonZero | Reports Bad below PercentDataBad. | [11423](https://mantis.opcfoundation.org/view.php?id=11423) |
| C5 | Aggregate – DurationGood/Bad, PercentGood/Bad `002-0x.js` | Truncates durations to whole milliseconds. | [11424](https://mantis.opcfoundation.org/view.php?id=11424) |
| C6 | Aggregate – DurationGood, PercentGood | Ignores the raw value before the interval for the status of the first region (answered, see U4). | [11425](https://mantis.opcfoundation.org/view.php?id=11425) |
| C48 | Aggregate – Minimum, Maximum, MinimumActualTime, MaximumActualTime, Range | Ignores Uncertain values beyond the Good extremum and expects Good instead of `UncertainDataSubNormal` (answered, see U4). | [11426](https://mantis.opcfoundation.org/view.php?id=11426) |
| C49 | Aggregate – Minimum, MinimumActualTime, MaximumActualTime | Does not set the Calculated bit when non-Good values make the status Uncertain (answered; see U4 for a conflict with 11426). | [11427](https://mantis.opcfoundation.org/view.php?id=11427) |
| U2 | Aggregate – Minimum2 (reverse reads) | Clears the Calculated bit when the minimum is the End bound at the early end of a reverse interval (Part 11 §6.5.4.2). | [11461](https://mantis.opcfoundation.org/view.php?id=11461) |
| U4 | Aggregate – DeltaBounds, StartBound, EndBound, WorstQuality2, TimeAverage2, Total2, Interpolative (TreatUncertainAsBad = True) | Applies TreatUncertainAsBad to bounding values, against the answer to [11462](https://mantis.opcfoundation.org/view.php?id=11462). | [11487](https://mantis.opcfoundation.org/view.php?id=11487) |
| C64 | Every Aggregate CU on the Int32 node (Interpolative, Start/EndBound, DeltaBounds, Minimum2/Maximum2/Range2, *ActualTime2, TimeAverage(2), Total(2); about 1400 readings) | Truncates interpolated bounds of integer Variables (23.8 → 23). Bounds are real (Part 13 §3.1.8); source-type results round by adding 0.5 and truncating (Part 4 §7.7.3), giving 24; Double results use the real bound (ramp 0…23.8 averages 11.9, the CTT expects 11.8866). In *ActualTime2 a truncated End bound ties an earlier raw value. | [11494](https://mantis.opcfoundation.org/view.php?id=11494); spec: [11499](https://mantis.opcfoundation.org/view.php?id=11499) |

### Historical Access

| Id | Failing test | Abstract | Mantis |
| --- | --- | --- | --- |
| 2 | Read Raw `initialize.js` | Accesses `CUVariables.ArrayItems` without its `isDefined` guard. | [11249](https://mantis.opcfoundation.org/view.php?id=11249) |
| 6 | Read Raw `004.js` | Rejects correct reverse ordering. | [11263](https://mantis.opcfoundation.org/view.php?id=11263) (fixed) |
| 7 | Read Raw `014.js` | Indexes a nonexistent second node result. | [11264](https://mantis.opcfoundation.org/view.php?id=11264) |
| 8 | Read Raw `019.js` | Bypasses the CTT test harness. | [11265](https://mantis.opcfoundation.org/view.php?id=11265) |
| 10 | Read Raw `012.js` | Expects `BadIndexRangeNoData` at the wrong level and sizes arrays from the first array item. | [11267](https://mantis.opcfoundation.org/view.php?id=11267), [11273](https://mantis.opcfoundation.org/view.php?id=11273) |
| 16 | Read Raw `013.js` | Reuses continuation points after changing the IndexRange. | [11257](https://mantis.opcfoundation.org/view.php?id=11257) (fixed) |
| C7 | Read Raw `Err-013.js` | The message calls the operation-level `BadContinuationPointInvalid` a ServiceResult. | [11428](https://mantis.opcfoundation.org/view.php?id=11428) |
| C8 | Read Raw `Err-019.js` | The messages at lines 25 and 43 use an undefined loop variable. | [11429](https://mantis.opcfoundation.org/view.php?id=11429) |
| C45 | Read Raw `Err-025.js` | Expects `BadNotSupported` for a non-historizing node; the test case will be removed. | [11347](https://mantis.opcfoundation.org/view.php?id=11347) (fixed), [11353](https://mantis.opcfoundation.org/view.php?id=11353) |

### Address Space, Base Information, Attribute and View Services

| Id | Failing test | Abstract | Mantis |
| --- | --- | --- | --- |
| 1 | Base Info State Machine Instance `001.js` | The `GeneratesEvent` target validation uses the wrong helper. | [11248](https://mantis.opcfoundation.org/view.php?id=11248) = [11125](https://mantis.opcfoundation.org/view.php?id=11125) (fixed) |
| 5 | AliasName Hierarchy `002.js` | References an undefined variable. | [11262](https://mantis.opcfoundation.org/view.php?id=11262) (fixed) |
| 11 | Attribute Read `026.js`, `032.js`, `034.js`, `036.js`; Attribute Write Index `007.js` | The array helpers support neither `NodeId[]` nor `StatusCode[]`. | [11261](https://mantis.opcfoundation.org/view.php?id=11261), [11250](https://mantis.opcfoundation.org/view.php?id=11250) |
| 12 | Base Info Core Structure 2 `001.js` | The error message cites the UA 1.04 reference model. | [11268](https://mantis.opcfoundation.org/view.php?id=11268) (fixed) |
| 13 | Base Info Core Structure 2 `001.js` | `ConformanceUnits` is tested as a scalar. | [11269](https://mantis.opcfoundation.org/view.php?id=11269) = [11144](https://mantis.opcfoundation.org/view.php?id=11144) (fixed) |
| 14 | Base Info Core Structure 2 `001.js` | Reads TransactionDiagnostics before any transaction and does not accept `BadOutOfService`. | [11256](https://mantis.opcfoundation.org/view.php?id=11256) (also [11509](https://mantis.opcfoundation.org/view.php?id=11509)) |
| C15 | Base Info Core Structure 2 `001.js` | The `Organizes` check in `InfoFactory.js` dereferences an undefined `sourceTypeNodeId` for View sources. | [11435](https://mantis.opcfoundation.org/view.php?id=11435) |
| C15b | Base Info Core Structure 2 `001.js` (every View validated by `InformationModelValidation`) | The View validator's `HierarchicalReferences` knows only i=33 and `Contains()` returns `undefined`, so every inverse reference to a View from a hierarchical subtype fails. | [11476](https://mantis.opcfoundation.org/view.php?id=11476) |
| 15 | Base Info SemanticChange `001.js` | Decodes the `Changes` array as one ExtensionObject, and the Range post-increment writes the old value. | [11093](https://mantis.opcfoundation.org/view.php?id=11093) |
| C13 | Base Info Currency `004.js` | The SByte `Exponent` of the `toCurrencyUnitType()` result does not convert to a number. | [11434](https://mantis.opcfoundation.org/view.php?id=11434) |
| C46 | Address Space Atomicity `001.js` (skips) | Examines only the first 10000 variables sorted by NodeId string (a literal in the script; `MaxNodesToReturn` is an Int16, so 32767 is the most that works), and its bit test detects only NonatomicRead. | [11446](https://mantis.opcfoundation.org/view.php?id=11446) |

### Monitored Item, Subscription and Session Services

| Id | Failing test | Abstract | Mantis |
| --- | --- | --- | --- |
| C9 | Monitor Value Change V2 `042.js` | Computes `indexValue` from itself; the message cannot identify the missing item. | [11430](https://mantis.opcfoundation.org/view.php?id=11430) |
| C17 | Monitor Basic `039.js` | Calls `getMatrixValues` without including its library. | [11097](https://mantis.opcfoundation.org/view.php?id=11097) (fixed) |
| 18 | Subscription Durable `008.js` | Misspells `MoreNotifications` and does not drain the queue; the DataChange count check rarely runs and the write spacing uses only `items[0]`'s sampling interval. | [11259](https://mantis.opcfoundation.org/view.php?id=11259) (fixed), [11475](https://mantis.opcfoundation.org/view.php?id=11475) |
| C38 | Subscription Durable `012.js` | Ignores a denied diagnostics Browse (`BadUserAccessDenied`) and then reads an empty node list. The server side matches 11445 ~0025750 since #4664: every Session may browse its own subscriptions in SubscriptionDiagnosticsArray, and reading the whole array stays administrator-only. | [11445](https://mantis.opcfoundation.org/view.php?id=11445) |
| 19 | Subscription Minimum 02 `020.js` | Accepts unrelated audit events and other server events (for example simulated alarms). | [11260](https://mantis.opcfoundation.org/view.php?id=11260) |
| C50 | Security None `007.js`, Security Basic 256 Sha256 `005.js`, Subscription Publish Basic `cleanup.js` (warnings) | CloseSession is stamped before the CTT stops the SessionThread (about 550 ms), so the delay warning blames the server. | [11454](https://mantis.opcfoundation.org/view.php?id=11454) |

### Node Management, Discovery and Security

| Id | Failing test | Abstract | Mantis |
| --- | --- | --- | --- |
| 9 | Node Management Add Node `Err-008.js` | Tests duplicate NodeIds while client-specified NodeIds are disabled. | [11266](https://mantis.opcfoundation.org/view.php?id=11266) |
| C34 | Node Management Add Node `001.js`–`003.js`, `Err-003.js`, `Err-005.js`, `Err-008.js` (with `RequestedNodeId` enabled) | `RequestedNewNodeId()` ignores `RequestedNodeId_Namespace` and always requests namespace 1. | [11443](https://mantis.opcfoundation.org/view.php?id=11443) |
| C16 | Discovery Get Endpoints `003.js` | `AcceptedProfileUris` omits the WebSocket transport profiles; the CTT will report them as unsupported endpoint types. | [11436](https://mantis.opcfoundation.org/view.php?id=11436) |
| C35 | Security User Anonymous `002.js` | `initialize.js` selects the `opc.wss` endpoint, which the CTT cannot open. | [11102](https://mantis.opcfoundation.org/view.php?id=11102) |
| C37 | Session Base `Err-002.js`, `Err-005.js`, `Err-022.js` | Send CreateSession with the `opc.wss` EndpointUrl, whose host name the CTT reads as empty. | [11102](https://mantis.opcfoundation.org/view.php?id=11102) |
| C36 | Security User Name Password 2 `015.js` | `FindTokenType` in `UaE.js` rewrites the cached endpoints, so identical UserTokenPolicies look different. | [11444](https://mantis.opcfoundation.org/view.php?id=11444) (follows [11258](https://mantis.opcfoundation.org/view.php?id=11258)) |

### GDS

| Id | Failing test | Abstract | Mantis |
| --- | --- | --- | --- |
| C19 | Application Directory `060.js`, `067.js`, `069.js` | Register an ApplicationUri that is not a valid URI. | [11438](https://mantis.opcfoundation.org/view.php?id=11438) |
| C20 | Application Directory `060.js`, `065.js`; Query Applications `011.js`, `016.js` | Evaluate the Like patterns `[_]` and `[%]` as "contains". | [11410](https://mantis.opcfoundation.org/view.php?id=11410) |
| C21 | Application Directory `074.js`; Query Applications `025.js` | Evaluate `%[^f-h]%` and `%[^w-y]%` as "contains none of". The OPC Foundation decided to test negated lists only at the start or end of a string until Part 4 clarifies the pattern; until then both test cases should be skipped. | [11411](https://mantis.opcfoundation.org/view.php?id=11411) |
| C22 | Application Directory `078.js`, `079.js` | `callQueryServers()` dereferences the output arguments of a failed Call. | [11407](https://mantis.opcfoundation.org/view.php?id=11407) |
| C23 | Application Directory `018.js` (sometimes `011.js`, `028.js`) | Selects `ActionTimestamp` instead of `ActionTimeStamp` and creates the audit item with queue size 1; `018.js` also passes Variants to the typed event classes and sends an empty AuditEntryId. | [11439](https://mantis.opcfoundation.org/view.php?id=11439) |
| C24 | Application Directory `019.js` | The batched RegisterApplication Call of step 3 never reaches the server: the native client stack fails a Call locally (`BadNotFound`) when a generic-structure ExtensionObject is in any request but the first. | [11409](https://mantis.opcfoundation.org/view.php?id=11409) |
| C25 | Application Directory `010.js` | Dereferences the ApplicationId of a rejected registration. | [11106](https://mantis.opcfoundation.org/view.php?id=11106) (fixed, still in 1.05.513), [11408](https://mantis.opcfoundation.org/view.php?id=11408) |
| C26 | Application Directory `012.js`, `032.js` | Require ServerCapabilities for a Server record. | [11441](https://mantis.opcfoundation.org/view.php?id=11441) |
| C27 | Application Directory `027.js` | Changes a Server that has a DiscoveryUrl into a Client. | [11442](https://mantis.opcfoundation.org/view.php?id=11442) |
| C29 | Application Directory `005.js` | The random ApplicationUri is cut at its first NUL, so both calls are identical; the oversized URI is then rejected at decode with `Bad_EncodingLimitsExceeded`, which the script does not accept. | [11406](https://mantis.opcfoundation.org/view.php?id=11406) |
| C30 | Query Applications `036.js` | Expects `rcp+` DiscoveryUrls the test never registered. | [11412](https://mantis.opcfoundation.org/view.php?id=11412) |
| C31 | Query Applications `038.js` | Treats applicationType 3 as invalid. | [11413](https://mantis.opcfoundation.org/view.php?id=11413) |
| C51 | Application Directory `066.js`, `079.js`; Query Applications `039.js` (pass only because the server returns NA records) | Register the reference Servers with ServerCapabilities `NA` and expect them in query results, which OPC 10000-12 §6.5.10/§6.5.11 exclude; with the exclusion 40 test cases fail. | [11458](https://mantis.opcfoundation.org/view.php?id=11458) |
| C57 | Application Directory `011.js`, `018.js`, `028.js` | Expect ApplicationRegistrationChangedAuditEvents on the DiscoveryAdmin Session, but the GDS NodeSet grants ReceiveEvents on that EventType only to SecurityAdmin (OPC 10000-3 §8.55). | [11482](https://mantis.opcfoundation.org/view.php?id=11482) |

### Alarms and Conditions

| Id | Failing test | Abstract | Mantis |
| --- | --- | --- | --- |
| C10 | A & C Alarm `Test_002.js`; A & C Confirm `Test_001.js` (depends on the alarm phase) | `ValidateRetain` ignores LatchedState: a latched alarm keeps Retain TRUE until it is reset (Part 9 §5.8.2). | [11431](https://mantis.opcfoundation.org/view.php?id=11431) |
| C11 | A & C Alarm `Test_004.js` | Reads the InputNode on the CU session, which has timed out (`BadSessionIdInvalid`) while the collector used only the alarm-thread session. | [11432](https://mantis.opcfoundation.org/view.php?id=11432) |
| C12 | A & C Enable `Test_002.js` | Passes four arguments to the three-argument `AddMessage`, so the reason text is lost. | [11433](https://mantis.opcfoundation.org/view.php?id=11433) |
| C18 | A & C Acknowledge, A & C Confirm (warnings) | The recommended TwoStateVariable texts are not found for locale `en-US`. | [11437](https://mantis.opcfoundation.org/view.php?id=11437) |
| C39 | A & C Comment `Test_001.js`–`Test_004.js` (skip); A & C Enable `Test_002.js` | `GetCallTime()` returns an unset call time; the wrapped 32-bit difference lets the Comment cases pass by accident for about 25 of every 50 days. | [11447](https://mantis.opcfoundation.org/view.php?id=11447) |
| C40 | A & C Exclusive/Non-Exclusive Limit/Level `Test_003.js`–`Test_006.js` | `initialize.js` creates the filter subscriptions on a CU session that timed out during the initial event capture. | [11448](https://mantis.opcfoundation.org/view.php?id=11448) |
| C41 | A & C Alarm `Test_002.js` (runs to the maximum test time) | AcknowledgeableConditionType never gets a result. | [11449](https://mantis.opcfoundation.org/view.php?id=11449) |
| C42 | A & C Enable `Test_003.js` (runs to the maximum test time) | Depends on all alarm types going active within one tenth of the Alarm Cycle Time. | [11450](https://mantis.opcfoundation.org/view.php?id=11450) |
| C44 | A & C CertificateExpiration (a `--close --hidden` run never exits) | `initialize.js` opens a modal dialog. | [11453](https://mantis.opcfoundation.org/view.php?id=11453) |
| C52 | A & C Refresh, Refresh2, Shelving (`BadSubscriptionIdInvalid` under CPU load) | Refresh `Err_004.js` and Refresh2 `Err_003.js` leave 10 subscriptions each on the shared alarm session, and `ShutdownItem()` leaves one per Refresh test case. | [11459](https://mantis.opcfoundation.org/view.php?id=11459) |
| C53 | A & C (alarm thread) | `StartThreadPublish.js` lines 28–29 default misspelled properties, so MaximumPublishCalls/MaximumOutstandingCalls are sent undefined (code cleanup only). | [11460](https://mantis.opcfoundation.org/view.php?id=11460) |

## Not filed

- **C28. GDS Application Directory `029.js`, `038.js`, `039.js`** expect `BadInvalidArgument` for a null or
  unknown ApplicationId. The server returns `BadNotFound`, the only code OPC 10000-12 §6.5.7/§6.5.9 define for
  an unknown ApplicationId. Held back until the GDS server behavior is reviewed.
- **C32. Monitor Basic `038.js`** warns when a requested SamplingInterval of 0 is revised to 0, unless
  *Fastest Sampling Interval Supported* is 0. The nodes declare MinimumSamplingInterval 0, so 0 is correct
  (Part 4 §7.21). The setting cannot be lowered because `monitoredItem.js` uses it as the default sampling
  interval. Held back: relevance unclear.
- **C43. A & C Enable `Err_004.js`** calls Disable, Disable and Enable for every event of a condition without
  per-condition state, which feeds itself (up to about 850 events in 15 s). In 8 of 28 runs the alarm thread
  then returned no further events. Held back: needs more investigation.
- **C54. GDS AliasName Discovery `002.js`, `004.js`, `006.js`, `007.js`, `009.js`, `011.js`, `012.js`,
  `014.js`, `015.js`.** `AssertAliasNamesReplicated` (`library/GDS/AliasNameDiscoveryHelpers.js` line 92)
  compares the AliasFor targets with `UaExpandedNodeId.equals()`. On the source the target is local
  (ServerIndex 0); the GDS must reference it with the source's ServerIndex and namespace URI (OPC 10000-17
  Annex C.1, OPC 10000-4 §7.16), so a conformant GDS never matches. Fix: compare identifier and namespace URI
  and check that the GDS ServerArray entry at the target's ServerIndex is the source's ApplicationUri. Draft
  ready, not filed.
- **C55. GDS AliasName Discovery `001.js`, `005.js`, `010.js`, `013.js`** check that no aliases of remote
  servers exist before registration, but fail on every AliasNameType instance, including the GDS's own aliases
  (ServerIndex 0). Fix: count only aliases with an AliasFor target whose ServerIndex is not 0. With the C54 and
  C55 fixes all 15 test cases pass. Draft ready, not filed.
- **C56. Monitored Items Deadband Filter `Err-006.js`** sets a PercentDeadband on a BaseDataVariable without
  EURange and accepts only `Good` or `Bad_MonitoredItemFilterUnsupported`. OPC 10000-8 §7.3.2 Table 61 defines
  `Bad_DeadbandFilterInvalid` for that case, which the server returns since #4629. Draft ready, not filed.
- **C58. UAFX FxRoot `001.js`** fails instead of skipping when the server has no UA FX Data namespace, unlike
  the other UAFX/DI CUs.
- **C59. A & C Refresh `Err_004.js`/`Test_006.js`, Refresh2 `Err_003.js`** wait 30/20/30 s without checking
  anything during the wait; 2–5 s are enough (about 87 s per A&C run).
- **C60. A & C Limit/Level `Test_003.js`–`Test_006.js`** (and any A&C CU that shares the alarm thread) fail with
  "Filtered Event counts are not equal" when the CTT's native publish thread stops sending Publish requests. A
  traced server answered all 413 Publish requests; afterwards the session sent only Reads, so its
  Subscriptions expired. Five Subscriptions on one publish thread with one outstanding Publish make it likely:
  with *Default Subscription Publish Interval* 150 ms the stall hit 6 of 9 runs on a GitHub-hosted runner,
  with 250 ms 0 of 3. Native CTT code, no script fix.
- **C61. A & C Enable `Test_002.js`** takes a second notification of the previous EnabledState as the answer
  to the next call.
- **C62. A & C Confirm `Err_004.js`** fails with `Bad_EventIdUnknown` when the Event reaches the second
  session late; the Confirm then uses an EventId the Condition has already replaced (Part 9 B.1.2 keeps no
  previous states), so the server is right.
- **C63. A & C** units reuse an alarm thread whose publish thread stalled (C60) at the end of the previous
  unit, so the whole next unit fails. Seen in the weekly run of 2026-10-18: Refresh2 `Test_002.js`,
  `Test_003.js` and `cleanup.js` failed with `BadSubscriptionIdInvalid`, and the unit stopped after
  `Test_004.js`. At 250 ms the stall still hits about 1 in 5 A&C runs on a GitHub-hosted runner, sometimes
  within a unit (Refresh2 `Err_003.js`). Workaround in the CTT CI: each unit checks at its start that the
  publish thread still receives keep-alives and otherwise starts on a new alarm thread. A part whose first
  attempt has regressions together with the C60 watchdog warning runs once more, and the second attempt is
  the result.
- **C65. Aggregate Bad data search reads forward.** `HAAggregateHelper.GetBadData` reads 120 raw values
  "backward" from the configured `StartOfBadData*` time with EndTime `new UaDateTime()`, the null DateTime. The
  server therefore reads forward (Part 11 §6.5.3), and the helper takes the last value as the earliest. The
  search then covers the 120 values after the configured time, and the final read spans from the end of that
  search to the 120th value after the first non-Good value it found. The Bad block must lie in that final read,
  not in the search window (see *Bad data entries for aggregates*).
  Proposed fix (EndTime `1601-01-01T00:00:00.001Z`) not tested yet. Related, different defect:
  [11505](https://mantis.opcfoundation.org/view.php?id=11505) (no contiguous Bad block → TypeError).
- **C66. Aggregates on Boolean/String nodes.** The CTT's expected values for StartBound/EndBound and
  DurationInState* on non-numeric nodes differ from the stepped Simple Bounding Values (Part 13 §3.1.9,
  §5.4.3.22–23, §5.4.3.28–29). The status-only aggregates are covered by C3, C4 and C6. Not filed: the CTT's own
  values were not recorded. Related: [11274](https://mantis.opcfoundation.org/view.php?id=11274). The server
  interpolates non-numeric values stepped whatever the Stepped property says (a String bound between "23" and
  "24" is "23").

## Spec questions

### U4. TreatUncertainAsBad and the aggregate definitions (answered)

The OPC Foundation (Paul Hunkar) answered in Mantis that **the aggregate definition wins over
TreatUncertainAsBad**; the general rules of §4.2.1.2 apply only where a definition says nothing else:

- [11462](https://mantis.opcfoundation.org/view.php?id=11462) ~0025840: TreatUncertainAsBad does not apply to
  bounding values; they follow §3.1.7–§3.1.9 only. An Uncertain neighbour makes a bound
  `UncertainDataSubNormal`, and only a Bad value after a Simple bound reverts to stepped. The CTT side is
  [11487](https://mantis.opcfoundation.org/view.php?id=11487). A follow-up question (~0025849) on §3.1.7's "in all
  cases the TreatUncertainAsBad flag is used" is unanswered.
- [11425](https://mantis.opcfoundation.org/view.php?id=11425) ~0025847 and
  [11495](https://mantis.opcfoundation.org/view.php?id=11495) ~0025874: DurationGood/DurationBad (and
  PercentGood/PercentBad) use the stepped Simple bound and its status for the first region; DurationGood
  divides into Good and non-Good regions, DurationBad into Bad and non-Bad regions, so Uncertain counts for
  neither.
- [11426](https://mantis.opcfoundation.org/view.php?id=11426) ~0025852: Minimum, Maximum, MinimumActualTime,
  MaximumActualTime and Range follow OPC Classic. Uncertain values are candidates as if Good; an Uncertain
  extremum is returned with `UncertainDataSubNormal`; otherwise the status is Good, `UncertainDataSubNormal` if
  Bad values exist, `BadNoData` if all are Bad. [11427](https://mantis.opcfoundation.org/view.php?id=11427)
  ~0025875 instead returns the Good minimum with `UncertainDataSubNormal` and the Calculated bit when a lower
  Uncertain value exists. The two answers conflict for that case; not raised yet. Minimum2/Maximum2 keep
  TreatUncertainAsBad, which §5.4.3.15 names explicitly.

The server follows 11462, 11425 and 11426 since #4702. With the patched scripts, Aggregates (p06) has 0 errors
against it.

### U5. TreatUncertainAsBad = False for Average, Count, Delta, StandardDeviation*, Variance* (answered)

Since C1 is fixed, the CTT sends TreatUncertainAsBad = False in `003-02.js`, `003-03.js`, `004-02.js` and
`004-03.js`, and 34 test cases differ. The server includes Uncertain values like Good ones (§4.2.1.2,
§5.4.3.2.1). The CTT excludes them because the definitions say "Good", and reports `UncertainDataSubNormal`.
[11495](https://mantis.opcfoundation.org/view.php?id=11495) ~0025874 (and earlier
[7975](https://mantis.opcfoundation.org/view.php?id=7975)) confirms the CTT for Count: non-Good values are never
counted, but they do affect the status. By the same rule the CTT is right for the other aggregates whose
definition says Good. The server follows this with [#4846](https://github.com/OPCFoundation/UA-.NETStandard/pull/4846).

### U6. NumberOfTransitions with TreatUncertainAsBad = True (open)

§5.4.3.24 compares "the earliest non-Bad value in the interval to the previous non-Bad value" (Table 72:
"Bound Uncertain: Use as value"). Under the U4 rule Uncertain values count, and the server counts them. The
CTT skips them when TreatUncertainAsBad = True (16 p06 errors). Not asked in Mantis yet.

## Server follow-ups from the answers

- **C48/C49:** if 11427 ~0025875 is confirmed over 11426 ~0025852, return the Good extremum (not the Uncertain
  one) with `UncertainDataSubNormal`.
- **C64:** if [11499](https://mantis.opcfoundation.org/view.php?id=11499) defines another conversion than Part 4
  §7.7.3 rounding, follow it for the source-type aggregates.

## CTT project configuration notes

Tests skipped because of reference server sample-data gaps or missing CTT project settings are tracked in
[#4479](https://github.com/OPCFoundation/UA-.NETStandard/issues/4479).

- **Aggregate ProcessingInterval.** `samples/UAReferenceServer.ctt.xml` sets
  `/Server Test/NodeIds/Static/HA Profile/Aggregates/ProcessingInterval` to 1; keep it positive (issue 4).
- **Bad data entries for aggregates.** `005-05.js`/`005-06.js` need an explicit Bad data entry; without it
  `HAAggregateHelper.GetRequestEntry` falls back to the start entry or throws.
  - `GetStartBadDataTime` reads `.../Aggregates/StartOfBadData<Name>` as an absolute time. The helper looks for a
    block of at least two consecutive non-Good values.
  - The reference server seeds every history node with 1001 values relative to its start time, 10 s apart:
    index mod 10 = 7 is `BadDataUnavailable`, index mod 10 = 9 is `UncertainSubstituteValue`, the rest are Good.
  - Every scalar history node also gets 400 values from `2026-01-01T00:00:00Z` (value = index, source timestamp
    + 1.234 s):
    - index 0–149 repeat the pattern (the first 120 are the start data of the other test cases);
    - 150–267 are Good, 268 is Bad and 269–274 are Good;
    - 275–279 are the Bad block;
    - 280–399 repeat the pattern.
  - All ten `StartOfBadData*` settings are `2026-01-01T00:25:01.234Z` (index 150). The layout follows the
    forward-reading search (C65): 120 values after index 150 must contain a non-Good value followed by a Good
    one (268), and the block must lie between index 269 and 387.
  - The `003-xx`/`004-xx` test cases that request "bad or start" data use this block too. StartOfArchive is the
    fixed date.
- **Reference server settings for #4479.** `samples/UAReferenceServer.ctt.xml` sets the following; copy them into
  projects created from an older template:
  - `/Server Test/NodeIds/Static/All Profiles/Scalar/Bool` = `ns=2;s=Scalar_Static_NonHistorizing_Boolean`, the
    non-historizing node of Historical Access Read Raw `Err-025.js` and Delete Value `dat-Err-001.js`/`Err-004.js`.
    The HA Profile and Aggregate Boolean settings stay on `Scalar_Static_Boolean`.
  - `/Server Test/NodeIds/References/Has References of a ReferenceType and SubType` = `i=2253` (the Server object
    has `HasComponent` and `HasAddIn`; `References_HasReferenceTypeAndSubType` loses its hierarchical references in
    the source generator, [#4484](https://github.com/OPCFoundation/UA-.NETStandard/issues/4484)).
  - `/Server Test/NodeIds/NodeClasses/Object` = `i=2253`, so View Basic 2 `018.js` also sees Method references.
- **Base Info Diagnostics `018-1.js`–`018-3.js`** write `Server.ServerDiagnostics.EnabledFlag`. The reference
  server allows that only for SecurityAdmin or ConfigureAdmin over SignAndEncrypt (the project's `sysadmin`
  user). The *"Session diagnostics not available"* warning of `018-1.js` is expected.
- **Node Management Add Node `002.js`** adds a Variable with every enabled
  `/Server Test/NodeIds/NodeManagement/SupportedReferences` entry. Enable only hierarchical references valid for
  a Variable target (`Organizes`, `HasProperty`, `HasComponent`); the others correctly return
  `BadReferenceNotAllowed` (Part 4 §5.8.2).
- **Node Management client NodeIds.** The template enables `RequestedNodeId` with `RequestedNodeId_Namespace` = 2.
  That needs the C34 fix; with unfixed scripts disable the setting (`Err-008.js` then keeps failing, issue 9).
- **DI Base Model and UAFX.** The 14 DI Base Model CUs and the UAFX groups (59 CUs) need the DI and UA FX models,
  which the reference server does not load. They skip, except that UAFX `initialize.js` reports the missing
  namespaces and FxRoot `001.js` fails (C58).
- **PubSub Publisher UADP CUs** (chunking, Defined Ordering, Periodic Fixed Settings) abort in `initialize.js`
  because the server implements no PubSub publisher. Deselect the PubSub General group.
- **UserDefinedCU** logs *"Hello error"*/*"Hello warning"* by design. Deselect it.
- **Discovery.**
  - Find Servers Filter `002.js` needs an LDS with at least two servers.
  - Find Servers Self `010.js` and Get Endpoints `009.js` need a multi-homed host.
- **Auditing.** Auditing Connections `002.js`, `003.js`, `008.js`, `010.js` and `014.js` skip unless another
  test case in the same run produces their audit event. Run Auditing together with the audited service groups.
- **GDS target server.**
  - The reference server in `--ctt` mode and a dedicated GDS run the same code, so both give the same results.
  - The reference server keeps its GDS database in memory (empty `DatabaseStorePath`).
  - A GDS with a JSON database keeps the records of aborted runs; delete that file before each run.
  - Run the four GDS CUs in one run: their test cases depend on the records registered by `initialize.js` and by
    earlier test cases.
- **GDS AliasName Discovery** tests the GDS AliasName Server Facet (OPC 10000-17 Annex C).
  - `Ctt.ReferenceServer.Config.xml` enables `EnableAliasNameAggregation`. `initialize.js` uses the CTT's
    embedded server as the first source.
  - `005.js`–`009.js` need a second and `010.js`–`015.js` a third AliasName Server
    (`.../AliasName Source 1 URL`, `... Source 2 URL`). The template leaves them empty because the repository
    has no suitable server.
  - Each source must offer Anonymous on an endpoint the CTT selects and trust the CTT and the GDS.
  - Its alias and category names must differ from those of the GDS and of the other sources.
  - It must expose no optional children (`FindAliasVerbose`, `AddAliasesToCategory`, `LastChange`) on its own
    categories.
  - `RegisterApplication` warns about the delay because it waits for the source to be read.
  - C54/C55 fail the remaining test cases.
- **GDS LDS-ME Connectivity** skips unless an LDS/LDS-ME is registered with the GDS; the reference server has none.
- **Monitored Item Services manual test cases.** These are *Not Implemented* in scripts 1.05.513:
  - Monitor Basic `036.js`, Monitor Complex Value `001.js`–`003.js`, Monitor Events `002.js`/`003.js` and Monitor
    Queueing `013.js`/`014.js`;
  - the Monitor Complex Event Filter and Monitor QueueSize_ServerMax CUs;
  - Node Management Add Ref and Delete Ref.
- **Security groups need the CTT PKI, not `-a`** ([ctt-testing.md](ctt-testing.md#9-security-groups)); with `-a`
  every negative certificate test fails spuriously.
- **SecureChannels without a Session.** Security None `007.js` and Security Basic 256 Sha256 `005.js` keep
  channels without a Session idle for about 51 s. The server closes such channels after `ChannelLifetime`
  (default 30 s); `Ctt.ReferenceServer.Config.xml` sets 120000, and both pass (`005.js` keeps the C50 warning).
- **Security coverage.**
  - 50 of the 53 Security General CUs contain only *Not Implemented* test cases.
  - Certificate Validation `004.js` skips because the CTT cannot send an empty client certificate.
  - `049.js`/`050.js` need the deprecated Basic128Rsa15 policy.
  - Security User Anonymous `003.js` needs a secure endpoint without Anonymous.
  - User Name Password 2 `002.js` needs an unencrypted password policy on an encrypted endpoint (not applicable).
  - `012.js` passes only because the configured denied login is an unknown user.
  - Kerberos, JWT, Authority Profile, Token Unencrypted, Invalid user token and X509 `003.js`/`012.js` are
    *Not Implemented*.
- **Alarms and Conditions coverage.**
  - The single-case CUs (ConditionClasses, Suppression by Operator, Silencing, OutOfService, On-Off Delay,
    Re-Alarming, First in Group, Audible Sound, Discrepancy, Trip, A&E Wrapper Mapping, Dialog) are manual.
  - `/Server Test/Alarms and Conditions/Chattering Alarms` is empty, so most Shelving test cases finish without
    testing anything. Testing them needs an alarm source that oscillates inside the alarm band.
- **A & C Alarm Cycle Time and session timeout.** The Alarm Cycle Time sets the initial event capture (1×), the
  maximum time of every collector test case (3×) and the Enable `Test_003.js` refresh delay (1/10). Keep it
  below `/Server Test/Session/RequestedSessionTimeout` when a Limit/Level CU can run first (C40). The template sets
  30 s against a 60 s session timeout.
- **Session and Subscription coverage.** These are manual (*Not Implemented*):
  - Subscription Basic `072.js`/`073.js`;
  - Subscription Multiple `001.js`–`003.js`;
  - Subscription Publish Basic `005.js`–`007.js` and `Err-001.js`;
  - Subscription PublishRequest Queue Overflow `001.js`/`002.js`;
  - Subscription Durable `013.js`.

  Durable StorageLevel High/Medium/Small and Retransmission Queue contain only `NoTestCaseDefined.js`. These are
  skipped by the scripts:
  - Subscription Basic `067.js`;
  - Subscription Durable `006.js` and `009.js`;
  - Subscription Transfer `Err-010.js`;
  - Session Base `Err-009.js` (Kerberos) and `Err-023.js` (the server offers SecurityPolicy None).
- **Historical Access coverage.** Every Historical Access CU except *Read Raw* contains only
  `NoTestCaseDefined.js` in scripts 1.05.513.
