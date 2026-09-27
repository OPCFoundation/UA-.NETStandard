# CTT (Compliance Test Tool) failing test cases

Test cases of CTT 1.05.06 (scripts 1.05.513) that fail, warn or skip against the OPC Foundation .NET
reference server (`ConsoleReferenceServer --ctt`) because of a CTT defect, with a short abstract and the
Mantis issue that tracks it. The details are in Mantis. Defects that are not filed yet, open questions and
CTT project configuration notes follow the tables. The procedure for running the CTT is in
[ctt-testing.md](ctt-testing.md).

- "Resolved / fixed" in Mantis means the fix is in the CTT script repository. It ships with a script
  build after 1.05.513, so an installed 1.05.513 still shows the failure.
- The ids (1–19, C1–C53, U1–U4) are stable references for notes and commit messages; missing ids were
  withdrawn or no longer fail against the reference server.
- Mantis states were last checked on 2026-09-25.

## Filed in Mantis

### Aggregates

| Id | Failing test | Abstract | Mantis |
| --- | --- | --- | --- |
| 3 | Aggregate – Base `002-01.js`…`002-04.js` (every Aggregate CU) | The multi-node path of `HAAggregateHelper.js` dereferences `possibleNodeId` without a guard. | [11251](https://mantis.opcfoundation.org/view.php?id=11251) |
| 4 | Aggregate – Base `Err-004.js` | Sends an equal-time request when the ProcessingInterval setting is blank. | [11252](https://mantis.opcfoundation.org/view.php?id=11252) |
| C1 | Aggregate – Base `003-01.js`…`003-04.js`, `004-01.js`…`004-04.js`, `Err-004.js` | The harness always sends `UseServerCapabilitiesDefaults = TRUE`, so the server uses its defaults while the oracle applies the test configuration. | [11420](https://mantis.opcfoundation.org/view.php?id=11420) |
| C2 | Aggregate – AnnotationCount | The oracle counts raw values instead of Annotations. | [11421](https://mantis.opcfoundation.org/view.php?id=11421) |
| C3 | Aggregate – WorstQuality2 | The oracle also includes the end bound. | [11422](https://mantis.opcfoundation.org/view.php?id=11422) |
| C4 | Aggregate – DurationInStateZero / DurationInStateNonZero | The oracle reports Bad below PercentDataBad. | [11423](https://mantis.opcfoundation.org/view.php?id=11423) |
| C5 | Aggregate – DurationGood/Bad, PercentGood/Bad `002-01.js`…`002-04.js` | The oracle truncates durations to whole milliseconds. | [11424](https://mantis.opcfoundation.org/view.php?id=11424) |
| C6 | Aggregate – DurationGood, PercentGood | The oracle ignores the raw value before the interval for the status of the first region. | [11425](https://mantis.opcfoundation.org/view.php?id=11425) |
| C48 | Aggregate – Minimum, Maximum, MinimumActualTime, MaximumActualTime `001-02.js`… | The oracle ignores Uncertain values beyond the Good extremum and expects Good instead of UncertainDataSubNormal. | [11426](https://mantis.opcfoundation.org/view.php?id=11426) |
| C49 | Aggregate – Minimum, MinimumActualTime, MaximumActualTime | The oracle does not set the Calculated bit when non-Good values make the status Uncertain. | [11427](https://mantis.opcfoundation.org/view.php?id=11427) |
| U2 | Aggregate – Minimum2 (reverse reads) | The oracle clears the Calculated bit when the minimum is the End bound at the early end of a reverse interval (Part 11 §6.5.4.2). | [11461](https://mantis.opcfoundation.org/view.php?id=11461) |

### Historical Access

| Id | Failing test | Abstract | Mantis |
| --- | --- | --- | --- |
| 2 | Historical Access Read Raw `initialize.js` | Accesses `CUVariables.ArrayItems` without its `isDefined` guard. | [11249](https://mantis.opcfoundation.org/view.php?id=11249) |
| 6 | Historical Access Read Raw `004.js` | Rejects correct reverse ordering. | [11263](https://mantis.opcfoundation.org/view.php?id=11263) |
| 7 | Historical Access Read Raw `014.js` | Indexes a nonexistent second node result. | [11264](https://mantis.opcfoundation.org/view.php?id=11264) |
| 8 | Historical Access Read Raw `019.js` | Bypasses the CTT test harness. | [11265](https://mantis.opcfoundation.org/view.php?id=11265) |
| 10 | Historical Access Read Raw `012.js` | Expects `BadIndexRangeNoData` at the wrong level and uses the first array item to calculate array sizes. | [11267](https://mantis.opcfoundation.org/view.php?id=11267), [11273](https://mantis.opcfoundation.org/view.php?id=11273) |
| 16 | Historical Access Read Raw `013.js` | Reuses continuation points after changing the IndexRange. | [11257](https://mantis.opcfoundation.org/view.php?id=11257) |
| C7 | Historical Access Read Raw `Err-013.js` | The message describes the operation-level `BadContinuationPointInvalid` as a ServiceResult. | [11428](https://mantis.opcfoundation.org/view.php?id=11428) |
| C8 | Historical Access Read Raw `Err-019.js` | The messages at lines 25 and 43 use an undefined loop variable. | [11429](https://mantis.opcfoundation.org/view.php?id=11429) |
| C45 | Historical Access Read Raw `Err-025.js` | Expects `BadNotSupported` for a non-historizing node; the test case is obsolete and will be removed. | [11347](https://mantis.opcfoundation.org/view.php?id=11347), [11353](https://mantis.opcfoundation.org/view.php?id=11353) |

### Address Space, Base Information, Attribute and View Services

| Id | Failing test | Abstract | Mantis |
| --- | --- | --- | --- |
| 1 | Base Info State Machine Instance `001.js` | The `GeneratesEvent` target validation uses the wrong helper. | [11248](https://mantis.opcfoundation.org/view.php?id=11248) (duplicate of [11125](https://mantis.opcfoundation.org/view.php?id=11125)) |
| 5 | AliasName Hierarchy `002.js` | References an undefined variable. | [11262](https://mantis.opcfoundation.org/view.php?id=11262) |
| 11 | Attribute Read `026.js`, `032.js`, `034.js`, `036.js`; Attribute Write Index `007.js` | The array helpers support neither `NodeId[]` nor `StatusCode[]`. | [11261](https://mantis.opcfoundation.org/view.php?id=11261), [11250](https://mantis.opcfoundation.org/view.php?id=11250) |
| 12 | Base Info Core Structure 2 `001.js` | The error message cites the UA 1.04 reference model. | [11268](https://mantis.opcfoundation.org/view.php?id=11268) |
| 13 | Base Info Core Structure 2 `001.js` | `ConformanceUnits` is tested as a scalar. | [11269](https://mantis.opcfoundation.org/view.php?id=11269) (duplicate of [11144](https://mantis.opcfoundation.org/view.php?id=11144)) |
| 14 | Base Info Core Structure 2 `001.js` | Reads TransactionDiagnostics before any transaction and does not accept `BadOutOfService`. | [11256](https://mantis.opcfoundation.org/view.php?id=11256) |
| C15 | Base Info Core Structure 2 `001.js` | The `Organizes` check in `InfoFactory.js` dereferences an undefined `sourceTypeNodeId` for View sources. | [11435](https://mantis.opcfoundation.org/view.php?id=11435) |
| 15 | Base Info SemanticChange `001.js` | Decodes the `Changes` array as one ExtensionObject. | [11093](https://mantis.opcfoundation.org/view.php?id=11093) |
| C13 | Base Info Currency `004.js` | `toCurrencyUnitType()` drops the CurrencyUnit Exponent. | [11434](https://mantis.opcfoundation.org/view.php?id=11434) |
| C46 | Address Space Atomicity `001.js` (skips) | Examines only the first 10000 variables sorted by NodeId string, and its bit test detects only NonatomicRead. | [11446](https://mantis.opcfoundation.org/view.php?id=11446) |

### Monitored Item, Subscription and Session Services

| Id | Failing test | Abstract | Mantis |
| --- | --- | --- | --- |
| C9 | Monitor Value Change V2 `042.js` | Computes `indexValue` from itself, and the failure message cannot identify the missing item. | [11430](https://mantis.opcfoundation.org/view.php?id=11430) |
| C17 | Monitor Basic `039.js` | Calls `getMatrixValues` without including its library. | [11097](https://mantis.opcfoundation.org/view.php?id=11097) |
| 18 | Subscription Durable `008.js` | Misspells `MoreNotifications` and does not drain the queue. | [11259](https://mantis.opcfoundation.org/view.php?id=11259) |
| C38 | Subscription Durable `012.js` | Ignores a denied diagnostics Browse (`BadUserAccessDenied`) and then reads an empty node list. | [11445](https://mantis.opcfoundation.org/view.php?id=11445) |
| 19 | Subscription Minimum 02 `020.js` | Accepts unrelated audit events. | [11260](https://mantis.opcfoundation.org/view.php?id=11260) |
| C50 | Security None `007.js`, Security Basic 256 Sha256 `005.js`, Subscription Publish Basic `cleanup.js` (warnings) | CloseSession stamps the request before the CTT stops the Session's SessionThread (about 550 ms), so the delay warning blames the server. | [11454](https://mantis.opcfoundation.org/view.php?id=11454) |

### Node Management, Discovery and Security

| Id | Failing test | Abstract | Mantis |
| --- | --- | --- | --- |
| 9 | Node Management Add Node `Err-008.js` | Tests duplicate NodeIds while client-specified NodeIds are disabled. | [11266](https://mantis.opcfoundation.org/view.php?id=11266) |
| C34 | Node Management Add Node `001.js`–`003.js`, `Err-003.js`, `Err-005.js`, `Err-008.js` (with `RequestedNodeId` enabled) | `RequestedNewNodeId()` ignores `RequestedNodeId_Namespace` and always requests namespace 1. | [11443](https://mantis.opcfoundation.org/view.php?id=11443) |
| C16 | Discovery Get Endpoints `003.js` | `AcceptedProfileUris` omits the WebSocket transport profiles. | [11436](https://mantis.opcfoundation.org/view.php?id=11436) |
| C35 | Security User Anonymous `002.js` | `initialize.js` selects the `opc.wss` endpoint, which the CTT cannot open. | [11102](https://mantis.opcfoundation.org/view.php?id=11102) |
| C37 | Session Base `Err-002.js`, `Err-005.js`, `Err-022.js` | Send CreateSession with the `opc.wss` EndpointUrl, whose host name the CTT reads as empty. | [11102](https://mantis.opcfoundation.org/view.php?id=11102) |
| C36 | Security User Name Password 2 `015.js` | `FindTokenType` in `UaE.js` rewrites the cached endpoints, so identical UserTokenPolicies look different. | [11444](https://mantis.opcfoundation.org/view.php?id=11444) (follows [11258](https://mantis.opcfoundation.org/view.php?id=11258)) |

### GDS

| Id | Failing test | Abstract | Mantis |
| --- | --- | --- | --- |
| C19 | Application Directory `060.js`, `067.js`, `069.js` | Register an ApplicationUri that is not a valid URI. | [11438](https://mantis.opcfoundation.org/view.php?id=11438) |
| C20 | Application Directory `060.js`, `065.js`; Query Applications `011.js`, `016.js` | Evaluate the Like patterns `[_]` and `[%]` as "contains". | [11410](https://mantis.opcfoundation.org/view.php?id=11410) |
| C21 | Application Directory `074.js`; Query Applications `025.js` | Evaluate `%[^f-h]%` and `%[^w-y]%` as "contains none of". | [11411](https://mantis.opcfoundation.org/view.php?id=11411) |
| C22 | Application Directory `078.js`, `079.js` | `callQueryServers()` dereferences the output arguments of a failed Call. | [11407](https://mantis.opcfoundation.org/view.php?id=11407) |
| C23 | Application Directory `018.js` (sometimes `011.js`, `028.js`) | Selects `ActionTimestamp` instead of `ActionTimeStamp` and creates the audit item with queue size 1. | [11439](https://mantis.opcfoundation.org/view.php?id=11439) |
| C24 | Application Directory `019.js` | The batched RegisterApplication Call of step 3 never reaches the server. | [11409](https://mantis.opcfoundation.org/view.php?id=11409) |
| C25 | Application Directory `010.js` | Dereferences the ApplicationId of a rejected registration. | [11106](https://mantis.opcfoundation.org/view.php?id=11106), [11408](https://mantis.opcfoundation.org/view.php?id=11408) |
| C26 | Application Directory `012.js`, `032.js` | Require ServerCapabilities for a Server record. | [11441](https://mantis.opcfoundation.org/view.php?id=11441) |
| C27 | Application Directory `027.js` | Changes a Server that has a DiscoveryUrl into a Client. | [11442](https://mantis.opcfoundation.org/view.php?id=11442) |
| C29 | Application Directory `005.js` | The random ApplicationUri is cut at its first NUL, so both calls are identical. | [11406](https://mantis.opcfoundation.org/view.php?id=11406) |
| C30 | Query Applications `036.js` | Expects `rcp+` DiscoveryUrls the test never registered. | [11412](https://mantis.opcfoundation.org/view.php?id=11412) |
| C31 | Query Applications `038.js` | Treats applicationType 3 as invalid. | [11413](https://mantis.opcfoundation.org/view.php?id=11413) |
| C51 | Application Directory `066.js`, `079.js`; Query Applications `039.js` (pass only because the server returns NA records) | Register the reference Servers with ServerCapabilities `NA` and expect them in query results, which OPC 10000-12 §6.5.10/§6.5.11 excludes; with the exclusion 40 test cases fail. | [11458](https://mantis.opcfoundation.org/view.php?id=11458) |

### Alarms and Conditions

| Id | Failing test | Abstract | Mantis |
| --- | --- | --- | --- |
| C10 | A & C Alarm `Test_002.js`; A & C Confirm `Test_001.js` (depends on the alarm phase) | `ValidateRetain` evaluates Retain from the main branch only. | [11431](https://mantis.opcfoundation.org/view.php?id=11431) |
| C11 | A & C Alarm `Test_004.js` | Calls `ReadHelper` re-entrantly from the alarm callback, which fails client-side with `BadInvalidState`. | [11432](https://mantis.opcfoundation.org/view.php?id=11432) |
| C12 | A & C Enable `Test_002.js` | Passes four arguments to the three-argument `AddMessage`, so the reason text is lost. | [11433](https://mantis.opcfoundation.org/view.php?id=11433) |
| C18 | A & C Acknowledge, A & C Confirm (warnings) | The recommended TwoStateVariable texts are not found for locale `en-US`. | [11437](https://mantis.opcfoundation.org/view.php?id=11437) |
| C39 | A & C Comment `Test_001.js`–`Test_004.js` (skip); A & C Enable `Test_002.js` | `GetCallTime()` returns an unset call time. The wrapped 32-bit difference lets the Comment cases pass by accident for about 25 of every 50 days (2026-09-23 to about 2026-10-18). | [11447](https://mantis.opcfoundation.org/view.php?id=11447) |
| C40 | A & C Exclusive/Non-Exclusive Limit/Level `Test_003.js`–`Test_006.js` | `initialize.js` creates the filter subscriptions on a CU session that timed out during the initial event capture. | [11448](https://mantis.opcfoundation.org/view.php?id=11448) |
| C41 | A & C Alarm `Test_002.js` (runs to the maximum test time) | AcknowledgeableConditionType never gets a result. | [11449](https://mantis.opcfoundation.org/view.php?id=11449) |
| C42 | A & C Enable `Test_003.js` (runs to the maximum test time) | Depends on all alarm types going active within one tenth of the Alarm Cycle Time. | [11450](https://mantis.opcfoundation.org/view.php?id=11450) |
| C44 | A & C CertificateExpiration (a `--close --hidden` run never exits) | `initialize.js` opens a modal dialog. | [11453](https://mantis.opcfoundation.org/view.php?id=11453) |
| C52 | A & C Refresh, Refresh2, Shelving (`BadSubscriptionIdInvalid` when the CPU is saturated) | Refresh `Err_004.js` and Refresh2 `Err_003.js` leave 10 subscriptions each on the shared alarm session, and `ShutdownItem()` leaves one per Refresh test case. | [11459](https://mantis.opcfoundation.org/view.php?id=11459) |
| C53 | A & C (alarm thread) | `StartThreadPublish.js` lines 28–29 default misspelled properties, so MaximumPublishCalls/MaximumOutstandingCalls are sent undefined. | [11460](https://mantis.opcfoundation.org/view.php?id=11460) |

## Not filed

- **C28. GDS Application Directory `029.js`, `038.js`, `039.js`** expect `BadInvalidArgument` for a null or unknown
  ApplicationId; the server returns `BadNotFound`, the only code OPC 10000-12 §6.5.7/§6.5.9 define for an unknown
  ApplicationId. Held back: the GDS server behavior is to be reviewed first.
- **C32. Monitor Basic `038.js`** warns when a requested SamplingInterval of 0 is revised to 0, unless the project
  setting *Fastest Sampling Interval Supported* is 0. The nodes declare MinimumSamplingInterval 0 and
  `MinSupportedSampleRate` is 0, so 0 is correct (Part 4 §7.21). The setting cannot be lowered because
  `monitoredItem.js` uses it as the default sampling interval of every MonitoredItem. Held back: relevance unclear.
- **C43. A & C Enable `Err_004.js`** calls Disable, Disable and Enable for every event of a condition without
  per-condition state, which feeds itself (up to about 850 events in 15 s). In 8 of 28 Enable runs the CTT alarm
  thread then returned no events for the rest of the CU, although the server sent and the CTT acknowledged them,
  and the remaining test cases ran to 3 × Alarm Cycle Time. Held back: needs more investigation.
- **Aggregate oracle differences.**
  - Non-numeric nodes: status-only aggregates (DurationGood/Bad, PercentGood/Bad, WorstQuality2, DurationInState*)
    differ on Boolean/String nodes from numeric nodes with the same status timeline, and the oracle returns
    `BadNoData` for valid Boolean/String StartBound/EndBound. Related: [11274](https://mantis.opcfoundation.org/view.php?id=11274).
  - Int32 conversion: Interpolative, TimeAverage, Total, DeltaBounds and StartBound/EndBound differ on Int32 nodes
    only (for example 24 vs 23): the server rounds interpolated values, the oracle truncates.
  - MinimumActualTime2/MaximumActualTime2: with a sloped End bound the server returns the bound at EffectiveEndTime
    (Part 13 §§5.4.3.17–.18); the oracle selects an earlier raw value.

## Needs clarification

### U4. DeltaBounds: does TreatUncertainAsBad make an Uncertain bound Bad?

§5.4.3.30: *"If one or both values are Bad the return status will be Bad_NoData. If one or both values are
Uncertain the status will be Uncertain_DataSubNormal."* A logged run (AGGDIAG project copy) of Aggregate –
DeltaBounds `003-01.js`…`008-01.js` on the Double and Float nodes differs in 200 readings; the timestamps always
agree:

| Readings | Server | Oracle | Cause |
| --- | --- | --- | --- |
| 80 | value 24, Good | value 23, Good | Int32 rounding (see *Aggregate oracle differences*) |
| 72 | `BadNoData` | value, `UncertainDataSubNormal` | TreatUncertainAsBad=false requested, but C1 makes the server use its default true; the Uncertain raw value before the bound then counts as Bad and no bound exists |
| 48 | value, `UncertainDataSubNormal` | `BadNoData` | TreatUncertainAsBad=true; the raw value after the bound is Bad, so §3.1.9 makes the bound Uncertain, and the oracle then treats the Uncertain bound as Bad |

§3.1.9 (Simple Bounding Values) and Table 78 never mention TreatUncertainAsBad, §4.2.1.2 applies it to every
aggregate calculation *"unless the Aggregate definition says otherwise"*, and its note (*"still treated as
Uncertain when the StatusCode for the result is calculated"*) contradicts §5.4.3.2.1. Whether TreatUncertainAsBad
applies to the raw values that form a bound (server), to the resulting bound (oracle) or not at all is open. A
spec clarification request is filed as [11462](https://mantis.opcfoundation.org/view.php?id=11462); neither side changes
before the answer.

## CTT project configuration notes

Tests skipped because of reference server sample-data gaps or missing CTT project settings are
tracked in [#4479](https://github.com/OPCFoundation/UA-.NETStandard/issues/4479).

- **Aggregate ProcessingInterval.** `samples/UAReferenceServer.ctt.xml` sets
  `/Server Test/NodeIds/Static/HA Profile/Aggregates/ProcessingInterval` to 1; keep it positive (see issue 4).
- **Bad data entries for aggregates.** `005-05.js`/`005-06.js` need an explicit Bad data entry. Without
  it, `HAAggregateHelper.GetRequestEntry` falls back to the start entry (*"Bad Data Entry no found,
  using start data"*) or throws (*"GetRequestEntry failed due to incorrect test configuration"*). The
  reference server seeds a deterministic pattern on every history node: index mod 10 = 7 is
  `BadDataUnavailable`, index mod 10 = 9 is `UncertainSubstituteValue`, the rest Good. No project setting
  can supply the entry: `GetStartBadDataTime` (`HAAggregateHelper.js` line 1023) reads
  `.../Aggregates/StartOfBadData<Name>` as an absolute time, the server seeds its history relative to its
  start time, and the pattern never has the two consecutive non-Good values the helper looks for. It needs a
  history seed anchored to a fixed date with a longer Bad block, plus that date in the template.
- **Reference server settings for #4479.** `samples/UAReferenceServer.ctt.xml` sets the following; a project
  created from an older template keeps the old values, so copy them over:
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
- **SecureChannels without a Session.** Security None `007.js` and Security Basic 256 Sha256 `005.js` keep
  channels without a Session idle for about 51 s and expect the newest one to close with Good. The server closes
  such channels after `ChannelLifetime` (default 30 s), although Part 4 §5.6.2.1 lets a SecureChannel live until
  its last token expires; keeping them open would let an unauthenticated client hold a channel for up to the
  `SecurityTokenLifetime`. `Ctt.ReferenceServer.Config.xml` sets `ChannelLifetime` to 120000 instead, and both
  test cases pass (`005.js` keeps the C50 warning).
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
  Anonymous token; the CTT configuration offers Anonymous on every endpoint. UserTokenPolicies are server-wide
  in the configuration, so this needs another SignAndEncrypt endpoint (for example `Aes128_Sha256_RsaOaep`) whose
  policies `ReferenceServer.GetUserTokenPolicies` filters; not done, because every CU that iterates the endpoints
  would then see it. Security User Name Password 2
  `002.js` needs a UserName token policy with SecurityPolicy `#None` on an encrypted endpoint (password sent
  unencrypted inside the channel); the reference server always encrypts passwords (not applicable).
  `012.js` passes only because `/Server Test/Session/LoginNameAccessDenied` (`username`) is not a known user
  and the server answers unknown credentials with `BadUserAccessDenied`; the reference server has no user
  that authenticates but is denied access. Security Invalid user token, the Kerberos, JWT, Authority Profile
  and Token Unencrypted CUs, and X509 `003.js`/`012.js`, are *Not Implemented*.
- **Alarms and Conditions coverage.** The single-case CUs (ConditionClasses, Condition Sub-Classes,
  Suppression by Operator, Silencing, OutOfService, On-Off Delay, Re-Alarming, First in Group Alarm,
  Audible Sound, Discrepancy, Trip, A&E Wrapper Mapping, Dialog) contain only manual
  (*Not Implemented*) test cases.
- **A & C Shelving coverage.** `/Server Test/Alarms and Conditions/Chattering Alarms` is empty, so
  `UseChatteringAlarms` sets IgnoreSkip on every type and `Test_003.js`–`Test_005.js`,
  `Test_007.js`–`Test_010.js` and `Err_001.js`–`Err_003.js` finish immediately without testing
  anything. The reference server has no alarm that stays active across transitions, so there is no
  condition to configure there yet. It would need an alarm source in the sample `AlarmNodeManager` that oscillates
  inside the alarm band (for example between 75 and 95 around a HighHigh limit of 90), with its ConditionId in that
  setting; the other A & C CUs would then see a condition that never clears.
- **A & C Alarm Cycle Time and session timeout.** `/Server Test/Alarms and Conditions/Alarm Cycle Time`
  sets the initial event capture (1 ×), the maximum time of every collector test case (3 ×) and the
  Enable `Test_003.js` refresh delay (1/10). Keep it below
  `/Server Test/Session/RequestedSessionTimeout` (ms) when a Limit/Level CU can be the first A&C CU of a
  run (C40). The template sets 30 s against a 60 s session timeout.
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
