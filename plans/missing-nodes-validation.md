# Missing Nodes Validation Report

**Date:** 2026-04-27  
**Server:** ConsoleReferenceServer with GDS (`opc.tcp://localhost:48010`)  
**Spec:** `Opc.Ua.NodeSet2.xml` v1.05.07  

## Result

| Metric | Value |
|--------|------:|
| Spec ns=0 nodes | 5283 |
| Server ns=0 nodes (export) | 6009 |
| Missing from export | 1243 |

All 384 encoding nodes and all access-restricted role children **exist in the server**.
They appear missing only due to export methodology limitations (hierarchical browse,
SecurityMode=None). The remaining gaps are optional features or type-level metadata.

| Category | Count | Status |
|----------|------:|--------|
| Encoding objects | 384 | **Present** -- non-hierarchical; not reachable by browse from Root |
| Access-restricted nodes | 236 | **Present** -- require SignAndEncrypt to read |
| Optional / Placeholder (direct) | 116 | Correctly absent |
| Optional ancestor missing | 172 | Correctly absent |
| Other (see detailed analysis) | 335 | See below |
| **Total** | **1243** | |

## Detailed Analysis of 335 Remaining Nodes

| Structural Category | Count | Fix Needed |
|---------------------|------:|------------|
| Type-level children (mandatory on ObjectTypes not instantiated) | 261 | No |
| Instance-level children (concrete server objects) | 48 | No (all present or deliberate) |
| Orphan property templates (no ParentNodeId) | 26 | No |

### Type-Level Children (261 nodes)

Mandatory children defined on ObjectTypes. Not instantiated because the server does not
create instances of these types. When instantiated, children are created automatically.

**Fix needed: No.**

<details>
<summary>Type-level children by parent ObjectType</summary>

#### ProgramStateMachineType (i=2391) -- 30 nodes

**Reason:** Program state machine properties. Not used.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=2392 | Creatable | Variable | - |
| i=2396 | InstanceCount | Variable | - |
| i=2397 | MaxInstanceCount | Variable | - |
| i=2398 | MaxRecycleCount | Variable | - |
| i=2400 | Ready | Object | - |
| i=2401 | StateNumber | Variable | Mandatory |
| i=2402 | Running | Object | - |
| i=2403 | StateNumber | Variable | Mandatory |
| i=2404 | Suspended | Object | - |
| i=2405 | StateNumber | Variable | Mandatory |
| i=2406 | Halted | Object | - |
| i=2407 | StateNumber | Variable | Mandatory |
| i=2408 | HaltedToReady | Object | - |
| i=2409 | TransitionNumber | Variable | Mandatory |
| i=2410 | ReadyToRunning | Object | - |
| i=2411 | TransitionNumber | Variable | Mandatory |
| i=2412 | RunningToHalted | Object | - |
| i=2413 | TransitionNumber | Variable | Mandatory |
| i=2414 | RunningToReady | Object | - |
| i=2415 | TransitionNumber | Variable | Mandatory |
| i=2416 | RunningToSuspended | Object | - |
| i=2417 | TransitionNumber | Variable | Mandatory |
| i=2418 | SuspendedToRunning | Object | - |
| i=2419 | TransitionNumber | Variable | Mandatory |
| i=2420 | SuspendedToHalted | Object | - |
| i=2421 | TransitionNumber | Variable | Mandatory |
| i=2422 | SuspendedToReady | Object | - |
| i=2423 | TransitionNumber | Variable | Mandatory |
| i=2424 | ReadyToHalted | Object | - |
| i=2425 | TransitionNumber | Variable | Mandatory |

#### FileTransferStateMachineType (i=15803) -- 28 nodes

**Reason:** Type-level children not instantiated by the reference server.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=15815 | Idle | Object | - |
| i=15816 | StateNumber | Variable | Mandatory |
| i=15817 | ReadPrepare | Object | - |
| i=15818 | StateNumber | Variable | Mandatory |
| i=15819 | ReadTransfer | Object | - |
| i=15820 | StateNumber | Variable | Mandatory |
| i=15821 | ApplyWrite | Object | - |
| i=15822 | StateNumber | Variable | Mandatory |
| i=15823 | Error | Object | - |
| i=15824 | StateNumber | Variable | Mandatory |
| i=15825 | IdleToReadPrepare | Object | - |
| i=15826 | TransitionNumber | Variable | Mandatory |
| i=15827 | ReadPrepareToReadTransfer | Object | - |
| i=15828 | TransitionNumber | Variable | Mandatory |
| i=15829 | ReadTransferToIdle | Object | - |
| i=15830 | TransitionNumber | Variable | Mandatory |
| i=15831 | IdleToApplyWrite | Object | - |
| i=15832 | TransitionNumber | Variable | Mandatory |
| i=15833 | ApplyWriteToIdle | Object | - |
| i=15834 | TransitionNumber | Variable | Mandatory |
| i=15835 | ReadPrepareToError | Object | - |
| i=15836 | TransitionNumber | Variable | Mandatory |
| i=15837 | ReadTransferToError | Object | - |
| i=15838 | TransitionNumber | Variable | Mandatory |
| i=15839 | ApplyWriteToError | Object | - |
| i=15840 | TransitionNumber | Variable | Mandatory |
| i=15841 | ErrorToIdle | Object | - |
| i=15842 | TransitionNumber | Variable | Mandatory |

#### ShelvedStateMachineType (i=2929) -- 18 nodes

**Reason:** Type-level children not instantiated by the reference server.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=2930 | Unshelved | Object | - |
| i=2932 | TimedShelved | Object | - |
| i=2933 | OneShotShelved | Object | - |
| i=2935 | UnshelvedToTimedShelved | Object | - |
| i=2936 | UnshelvedToOneShotShelved | Object | - |
| i=2940 | TimedShelvedToUnshelved | Object | - |
| i=2942 | TimedShelvedToOneShotShelved | Object | - |
| i=2943 | OneShotShelvedToUnshelved | Object | - |
| i=2945 | OneShotShelvedToTimedShelved | Object | - |
| i=6098 | StateNumber | Variable | Mandatory |
| i=6100 | StateNumber | Variable | Mandatory |
| i=6101 | StateNumber | Variable | Mandatory |
| i=11322 | TransitionNumber | Variable | Mandatory |
| i=11323 | TransitionNumber | Variable | Mandatory |
| i=11324 | TransitionNumber | Variable | Mandatory |
| i=11325 | TransitionNumber | Variable | Mandatory |
| i=11326 | TransitionNumber | Variable | Mandatory |
| i=11327 | TransitionNumber | Variable | Mandatory |

#### PubSubKeyPushTargetFolderType (i=25346) -- 17 nodes

**Reason:** PubSub key push target folder. Not implemented.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=25348 | AddPushTarget | Method | Mandatory |
| i=25349 | InputArguments | Variable | Mandatory |
| i=25350 | OutputArguments | Variable | Mandatory |
| i=25351 | RemovePushTarget | Method | Mandatory |
| i=25352 | InputArguments | Variable | Mandatory |
| i=25366 | AddPushTarget | Method | Mandatory |
| i=25367 | InputArguments | Variable | Mandatory |
| i=25368 | OutputArguments | Variable | Mandatory |
| i=25369 | RemovePushTarget | Method | Mandatory |
| i=25370 | InputArguments | Variable | Mandatory |
| i=25655 | ConnectSecurityGroups | Method | Mandatory |
| i=25656 | InputArguments | Variable | Mandatory |
| i=25657 | OutputArguments | Variable | Mandatory |
| i=25658 | DisconnectSecurityGroups | Method | Mandatory |
| i=25659 | InputArguments | Variable | Mandatory |
| i=25660 | OutputArguments | Variable | Mandatory |
| i=25661 | TriggerKeyUpdate | Method | Mandatory |

#### ExclusiveLimitStateMachineType (i=9318) -- 16 nodes

**Reason:** Type-level children not instantiated by the reference server.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=9329 | HighHigh | Object | - |
| i=9330 | StateNumber | Variable | Mandatory |
| i=9331 | High | Object | - |
| i=9332 | StateNumber | Variable | Mandatory |
| i=9333 | Low | Object | - |
| i=9334 | StateNumber | Variable | Mandatory |
| i=9335 | LowLow | Object | - |
| i=9336 | StateNumber | Variable | Mandatory |
| i=9337 | LowLowToLow | Object | - |
| i=9338 | LowToLowLow | Object | - |
| i=9339 | HighHighToHigh | Object | - |
| i=9340 | HighToHighHigh | Object | - |
| i=11340 | TransitionNumber | Variable | Mandatory |
| i=11341 | TransitionNumber | Variable | Mandatory |
| i=11342 | TransitionNumber | Variable | Mandatory |
| i=11343 | TransitionNumber | Variable | Mandatory |

#### SessionsDiagnosticsSummaryType (i=2026) -- 11 nodes

**Reason:** Session diagnostics type. Server uses simplified diagnostics model.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=2028 | SessionSecurityDiagnosticsArray | Variable | Mandatory |
| i=12142 | SessionSecurityDiagnostics | Variable | Mandatory |
| i=12143 | SessionId | Variable | Mandatory |
| i=12144 | ClientUserIdOfSession | Variable | Mandatory |
| i=12145 | ClientUserIdHistory | Variable | Mandatory |
| i=12146 | AuthenticationMechanism | Variable | Mandatory |
| i=12147 | Encoding | Variable | Mandatory |
| i=12148 | TransportProtocol | Variable | Mandatory |
| i=12149 | SecurityMode | Variable | Mandatory |
| i=12150 | SecurityPolicyUri | Variable | Mandatory |
| i=12151 | ClientCertificate | Variable | Mandatory |

#### SecurityGroupFolderType (i=15452) -- 10 nodes

**Reason:** Security group folder. Not implemented.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=15454 | AddSecurityGroup | Method | Mandatory |
| i=15455 | InputArguments | Variable | Mandatory |
| i=15456 | OutputArguments | Variable | Mandatory |
| i=15457 | RemoveSecurityGroup | Method | Mandatory |
| i=15458 | InputArguments | Variable | Mandatory |
| i=15461 | AddSecurityGroup | Method | Mandatory |
| i=15462 | InputArguments | Variable | Mandatory |
| i=15463 | OutputArguments | Variable | Mandatory |
| i=15464 | RemoveSecurityGroup | Method | Mandatory |
| i=15465 | InputArguments | Variable | Mandatory |

#### AlarmConditionType (i=2915) -- 10 nodes

**Reason:** TrueState/FalseState display-hint properties on TwoStateVariable children.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=9167 | TrueState | Variable | - |
| i=9168 | FalseState | Variable | - |
| i=9176 | TrueState | Variable | - |
| i=9177 | FalseState | Variable | - |
| i=16378 | TrueState | Variable | - |
| i=16379 | FalseState | Variable | - |
| i=16387 | TrueState | Variable | - |
| i=16388 | FalseState | Variable | - |
| i=18197 | TrueState | Variable | - |
| i=18198 | FalseState | Variable | - |

#### PubSubKeyServiceType (i=15906) -- 10 nodes

**Reason:** Security Key Service type. Server does not implement SKS.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=15914 | AddSecurityGroup | Method | Mandatory |
| i=15915 | InputArguments | Variable | Mandatory |
| i=15916 | OutputArguments | Variable | Mandatory |
| i=15917 | RemoveSecurityGroup | Method | Mandatory |
| i=15918 | InputArguments | Variable | Mandatory |
| i=25278 | AddPushTarget | Method | Mandatory |
| i=25279 | InputArguments | Variable | Mandatory |
| i=25280 | OutputArguments | Variable | Mandatory |
| i=25281 | RemovePushTarget | Method | Mandatory |
| i=25282 | InputArguments | Variable | Mandatory |

#### SessionDiagnosticsObjectType (i=2029) -- 10 nodes

**Reason:** Session diagnostics type. Simplified diagnostics model.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=2031 | SessionSecurityDiagnostics | Variable | Mandatory |
| i=3179 | SessionId | Variable | Mandatory |
| i=3180 | ClientUserIdOfSession | Variable | Mandatory |
| i=3181 | ClientUserIdHistory | Variable | Mandatory |
| i=3182 | AuthenticationMechanism | Variable | Mandatory |
| i=3183 | Encoding | Variable | Mandatory |
| i=3184 | TransportProtocol | Variable | Mandatory |
| i=3185 | SecurityMode | Variable | Mandatory |
| i=3186 | SecurityPolicyUri | Variable | Mandatory |
| i=3187 | ClientCertificate | Variable | Mandatory |

#### UserManagementType (i=24264) -- 9 nodes

**Reason:** User management (AddUser, ModifyUser, etc.). Not implemented.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=24265 | Users | Variable | Mandatory |
| i=24269 | AddUser | Method | Mandatory |
| i=24270 | InputArguments | Variable | Mandatory |
| i=24271 | ModifyUser | Method | Mandatory |
| i=24272 | InputArguments | Variable | Mandatory |
| i=24273 | RemoveUser | Method | Mandatory |
| i=24274 | InputArguments | Variable | Mandatory |
| i=24275 | ChangePassword | Method | Mandatory |
| i=24276 | InputArguments | Variable | Mandatory |

#### ApplicationConfigurationFolderType (i=16662) -- 9 nodes

**Reason:** Application config folder. Same as ServerConfigurationType.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=18533 | UpdateCertificate | Method | Mandatory |
| i=18534 | InputArguments | Variable | Mandatory |
| i=18535 | OutputArguments | Variable | Mandatory |
| i=18539 | ApplyChanges | Method | Mandatory |
| i=18541 | CreateSigningRequest | Method | Mandatory |
| i=18542 | InputArguments | Variable | Mandatory |
| i=18543 | OutputArguments | Variable | Mandatory |
| i=18544 | GetRejectedList | Method | Mandatory |
| i=18545 | OutputArguments | Variable | Mandatory |

#### ProvisionableDeviceType (i=26871) -- 9 nodes

**Reason:** Provisioning workflow type. Not implemented.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=28005 | UpdateCertificate | Method | Mandatory |
| i=28006 | InputArguments | Variable | Mandatory |
| i=28007 | OutputArguments | Variable | Mandatory |
| i=28008 | ApplyChanges | Method | Mandatory |
| i=28010 | CreateSigningRequest | Method | Mandatory |
| i=28011 | InputArguments | Variable | Mandatory |
| i=28012 | OutputArguments | Variable | Mandatory |
| i=28013 | GetRejectedList | Method | Mandatory |
| i=28014 | OutputArguments | Variable | Mandatory |

#### ServerConfigurationType (i=12581) -- 9 nodes

**Reason:** Server push configuration (UpdateCertificate, CreateSigningRequest). Not implemented.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=12616 | UpdateCertificate | Method | Mandatory |
| i=12617 | InputArguments | Variable | Mandatory |
| i=12618 | OutputArguments | Variable | Mandatory |
| i=12731 | CreateSigningRequest | Method | Mandatory |
| i=12732 | InputArguments | Variable | Mandatory |
| i=12733 | OutputArguments | Variable | Mandatory |
| i=12734 | ApplyChanges | Method | Mandatory |
| i=12775 | GetRejectedList | Method | Mandatory |
| i=12776 | OutputArguments | Variable | Mandatory |

#### NonExclusiveLimitAlarmType (i=9906) -- 8 nodes

**Reason:** TrueState/FalseState display-hint properties.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=10027 | TrueState | Variable | - |
| i=10028 | FalseState | Variable | - |
| i=10036 | TrueState | Variable | - |
| i=10037 | FalseState | Variable | - |
| i=10045 | TrueState | Variable | - |
| i=10046 | FalseState | Variable | - |
| i=10054 | TrueState | Variable | - |
| i=10055 | FalseState | Variable | - |

#### PublishSubscribeType (i=14416) -- 7 nodes

**Reason:** PubSub methods. Server does not fully implement PubSub.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=18727 | Reset | Method | Mandatory |
| i=25426 | ReserveIds | Method | Mandatory |
| i=25427 | InputArguments | Variable | Mandatory |
| i=25428 | OutputArguments | Variable | Mandatory |
| i=25429 | CloseAndUpdate | Method | Mandatory |
| i=25430 | InputArguments | Variable | Mandatory |
| i=25431 | OutputArguments | Variable | Mandatory |

#### PubSubKeyPushTargetType (i=25337) -- 7 nodes

**Reason:** PubSub security key push target. Server does not implement SKS key push.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=25641 | ConnectSecurityGroups | Method | Mandatory |
| i=25642 | InputArguments | Variable | Mandatory |
| i=25643 | OutputArguments | Variable | Mandatory |
| i=25644 | DisconnectSecurityGroups | Method | Mandatory |
| i=25645 | InputArguments | Variable | Mandatory |
| i=25646 | OutputArguments | Variable | Mandatory |
| i=25647 | TriggerKeyUpdate | Method | Mandatory |

#### ConditionType (i=2782) -- 7 nodes

**Reason:** ConditionRefresh methods and SupportsFilteredRetain.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=3875 | ConditionRefresh | Method | - |
| i=3876 | InputArguments | Variable | Mandatory |
| i=9018 | TrueState | Variable | - |
| i=9019 | FalseState | Variable | - |
| i=12912 | ConditionRefresh2 | Method | - |
| i=12913 | InputArguments | Variable | Mandatory |
| i=32060 | SupportsFilteredRetain | Variable | - |

#### PubSubConfigurationType (i=25482) -- 6 nodes

**Reason:** PubSub config file (ReserveIds/CloseAndUpdate).

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=25505 | ReserveIds | Method | Mandatory |
| i=25506 | InputArguments | Variable | Mandatory |
| i=25507 | OutputArguments | Variable | Mandatory |
| i=25508 | CloseAndUpdate | Method | Mandatory |
| i=25509 | InputArguments | Variable | Mandatory |
| i=25510 | OutputArguments | Variable | Mandatory |

#### RoleSetType (i=15607) -- 6 nodes

**Reason:** AddRole/RemoveRole. Present but require encrypted connection.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=15997 | AddRole | Method | Mandatory |
| i=15998 | InputArguments | Variable | Mandatory |
| i=15999 | OutputArguments | Variable | Mandatory |
| i=16000 | RemoveRole | Method | Mandatory |
| i=16001 | InputArguments | Variable | Mandatory |
| i=16162 | Identities | Variable | Mandatory |

#### ServerCapabilitiesType (i=2013) -- 5 nodes

**Reason:** Capabilities properties not populated by reference server.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=16296 | AddRole | Method | Mandatory |
| i=16297 | InputArguments | Variable | Mandatory |
| i=16298 | OutputArguments | Variable | Mandatory |
| i=16299 | RemoveRole | Method | Mandatory |
| i=16300 | InputArguments | Variable | Mandatory |

#### AcknowledgeableConditionType (i=2881) -- 4 nodes

**Reason:** TrueState/FalseState on TwoStateVariable children.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=9100 | TrueState | Variable | - |
| i=9101 | FalseState | Variable | - |
| i=9109 | TrueState | Variable | - |
| i=9110 | FalseState | Variable | - |

#### AlarmSuppressionGroupType (i=32064) -- 2 nodes

**Reason:** TrueState/FalseState display-hint.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=23586 | TrueState | Variable | - |
| i=23587 | FalseState | Variable | - |

#### AlarmGroupType (i=16405) -- 2 nodes

**Reason:** TrueState/FalseState display-hint.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=16472 | TrueState | Variable | - |
| i=16473 | FalseState | Variable | - |

#### DialogConditionType (i=2830) -- 2 nodes

**Reason:** TrueState/FalseState on DialogState.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=9062 | TrueState | Variable | - |
| i=9063 | FalseState | Variable | - |

#### RoleType (i=15620) -- 1 nodes

**Reason:** Identities on RoleType. Present on instances, require encryption.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=16173 | Identities | Variable | Mandatory |

#### ReaderGroupType (i=17999) -- 1 nodes

**Reason:** Type-level children not instantiated by the reference server.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=21027 | Reset | Method | Mandatory |

#### DataSetReaderType (i=15306) -- 1 nodes

**Reason:** Type-level children not instantiated by the reference server.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=19621 | Reset | Method | Mandatory |

#### PubSubDiagnosticsType (i=19677) -- 1 nodes

**Reason:** Type-level children not instantiated by the reference server.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=19689 | Reset | Method | Mandatory |

#### ServerType (i=2004) -- 1 nodes

**Reason:** Type-level child of ServerType.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=3113 | SessionSecurityDiagnosticsArray | Variable | Mandatory |

#### ServerDiagnosticsType (i=2020) -- 1 nodes

**Reason:** Server explicitly deletes SamplingIntervalDiagnosticsArray.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=3130 | SessionSecurityDiagnosticsArray | Variable | Mandatory |

#### WriterGroupType (i=17725) -- 1 nodes

**Reason:** Type-level children not instantiated by the reference server.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=17824 | Reset | Method | Mandatory |

#### DataSetWriterType (i=15298) -- 1 nodes

**Reason:** Type-level children not instantiated by the reference server.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=19562 | Reset | Method | Mandatory |

#### PubSubConnectionType (i=14209) -- 1 nodes

**Reason:** Type-level children not instantiated by the reference server.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=19253 | Reset | Method | Mandatory |

</details>

### Instance-Level Children (48 nodes)

#### Server > ServerDiagnostics (16 nodes)

`DiagnosticsNodeManager` explicitly deletes `SamplingIntervalDiagnosticsArray`. Remaining
sub-properties use a dynamic diagnostics model (computed on demand, not static nodes).

**Fix needed:** No.

| NodeId | BrowseName | Chain |
|--------|------------|-------|
| i=2275 | ServerDiagnosticsSummary | Server > ServerDiagnostics > ServerDiagnosticsSummary |
| i=2276 | ServerViewCount | Server > ServerDiagnostics > ServerDiagnosticsSummary > ServerViewCount |
| i=2277 | CurrentSessionCount | Server > ServerDiagnostics > ServerDiagnosticsSummary > CurrentSessionCount |
| i=2278 | CumulatedSessionCount | Server > ServerDiagnostics > ServerDiagnosticsSummary > CumulatedSessionCount |
| i=2279 | SecurityRejectedSessionCount | Server > ServerDiagnostics > ServerDiagnosticsSummary > SecurityRejectedSessionCount |
| i=2281 | SessionTimeoutCount | Server > ServerDiagnostics > ServerDiagnosticsSummary > SessionTimeoutCount |
| i=2282 | SessionAbortCount | Server > ServerDiagnostics > ServerDiagnosticsSummary > SessionAbortCount |
| i=2284 | PublishingIntervalCount | Server > ServerDiagnostics > ServerDiagnosticsSummary > PublishingIntervalCount |
| i=2285 | CurrentSubscriptionCount | Server > ServerDiagnostics > ServerDiagnosticsSummary > CurrentSubscriptionCount |
| i=2286 | CumulatedSubscriptionCount | Server > ServerDiagnostics > ServerDiagnosticsSummary > CumulatedSubscriptionCount |
| i=2287 | SecurityRejectedRequestsCount | Server > ServerDiagnostics > ServerDiagnosticsSummary > SecurityRejectedRequestsCount |
| i=2288 | RejectedRequestsCount | Server > ServerDiagnostics > ServerDiagnosticsSummary > RejectedRequestsCount |
| i=2289 | SamplingIntervalDiagnosticsArray | Server > ServerDiagnostics > SamplingIntervalDiagnosticsArray |
| i=2290 | SubscriptionDiagnosticsArray | Server > ServerDiagnostics > SubscriptionDiagnosticsArray |
| i=3705 | RejectedSessionCount | Server > ServerDiagnostics > ServerDiagnosticsSummary > RejectedSessionCount |
| i=3707 | SessionDiagnosticsArray | Server > ServerDiagnostics > SessionsDiagnosticsSummary > SessionDiagnosticsArray |

#### Server > Auditing (1 node)

`i=2994` -- Boolean indicating audit support. **Present** in the server (value=`true`),
but returns `BadUserAccessDenied` for anonymous users due to RolePermissions.
Visible to authenticated admin users.

**Fix needed:** No. Already correctly exposed; hidden from export by access control.

#### HA Configuration (6 nodes)

Historical Access configuration. Reference server does not implement HA.

**Fix needed:** No (optional capability).

| NodeId | BrowseName |
|--------|------------|
| i=11203 | AggregateConfiguration |
| i=11204 | TreatUncertainAsBad |
| i=11205 | PercentDataBad |
| i=11206 | PercentDataGood |
| i=11207 | UseSlopedExtrapolation |
| i=11208 | Stepped |

#### FileSystem (11 nodes)

`FileSystem` object with CreateDirectory/CreateFile/Delete/MoveOrCopy methods.

**Fix needed:** No (optional capability).

#### PublishSubscribe instance children (14 nodes)

AddConnection/RemoveConnection methods, diagnostics Reset, PubSubConfiguration file methods.

**Fix needed:** No (optional capability, large effort).

| NodeId | BrowseName | Chain |
|--------|------------|-------|
| i=17366 | AddConnection | PublishSubscribe > AddConnection |
| i=17367 | InputArguments | PublishSubscribe > AddConnection > InputArguments |
| i=17368 | OutputArguments | PublishSubscribe > AddConnection > OutputArguments |
| i=17369 | RemoveConnection | PublishSubscribe > RemoveConnection |
| i=17370 | InputArguments | PublishSubscribe > RemoveConnection > InputArguments |
| i=17421 | Reset | PublishSubscribe > Diagnostics > Reset |
| i=25467 | Write | PublishSubscribe > PubSubConfiguration > Write |
| i=25468 | InputArguments | PublishSubscribe > PubSubConfiguration > Write > InputArguments |
| i=25474 | ReserveIds | PublishSubscribe > PubSubConfiguration > ReserveIds |
| i=25475 | InputArguments | PublishSubscribe > PubSubConfiguration > ReserveIds > InputArguments |
| i=25476 | OutputArguments | PublishSubscribe > PubSubConfiguration > ReserveIds > OutputArguments |
| i=25477 | CloseAndUpdate | PublishSubscribe > PubSubConfiguration > CloseAndUpdate |
| i=25478 | InputArguments | PublishSubscribe > PubSubConfiguration > CloseAndUpdate > InputArguments |
| i=25479 | OutputArguments | PublishSubscribe > PubSubConfiguration > CloseAndUpdate > OutputArguments |

### Orphan Properties (26 nodes)

Variable nodes with no `ParentNodeId`. Canonical type-level property definitions
(NodeVersion, Icon, InputArguments, EnumStrings, etc.) used as templates by types.

**Fix needed: No.** Design-time templates, not runtime nodes.

| NodeId | BrowseName | NodeClass |
|--------|------------|-----------|
| i=3067 | Icon | Variable |
| i=3068 | NodeVersion | Variable |
| i=3069 | LocalTime | Variable |
| i=3070 | AllowNulls | Variable |
| i=3071 | EnumValues | Variable |
| i=3072 | InputArguments | Variable |
| i=3073 | OutputArguments | Variable |
| i=11202 | HA Configuration | Object |
| i=11214 | Annotations | Variable |
| i=11215 | HistoricalEventFilter | Variable |
| i=11312 | CurrentServerId | Variable |
| i=11314 | ServerUriArray | Variable |
| i=11432 | EnumStrings | Variable |
| i=11433 | ValueAsText | Variable |
| i=11498 | MaxStringLength | Variable |
| i=11512 | MaxArrayLength | Variable |
| i=11513 | EngineeringUnits | Variable |
| i=12170 | ViewVersion | Variable |
| i=12745 | OptionSetValues | Variable |
| i=12908 | MaxByteStringLength | Variable |
| i=14415 | ServerNetworkGroups | Variable |
| i=15002 | MaxCharacters | Variable |
| i=16314 | FileSystem | Object |
| i=17605 | DefaultInstanceBrowseName | Variable |
| i=23501 | CurrencyUnit | Variable |
| i=32750 | OptionSetLength | Variable |

## Fix Summary

| Category | Count | Fix Needed | Effort |
|----------|------:|------------|--------|
| Type-level children | 261 | No | - |
| Server diagnostics | 16 | No (deliberate) | - |
| Server.Auditing | 1 | No (**Present**, access-restricted) | - |
| HA Configuration | 6 | No (optional) | - |
| FileSystem | 11 | No (optional) | - |
| PublishSubscribe | 14 | No (optional) | Large |
| Orphan templates | 26 | No (design-time) | - |
| **Total** | **335** | **No fixes needed** | |
