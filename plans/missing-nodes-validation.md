# Missing Nodes Validation Report

**Date:** 2026-04-26  
**Server:** `opc.tcp://localhost:48010` (ConsoleReferenceServer)  
**Spec:** `Opc.Ua.NodeSet2.xml` v1.05.07

## Result

The previous analysis reported **794 truly missing nodes** (384 encoding, 172 role children, 238 other).
Live validation proves **all 384 encoding nodes and all 172 role children exist in the server**.
They appeared missing due to limitations of the ExportNodeSet comparison method:

| Category | Count | Status | Root Cause |
|----------|------:|--------|------------|
| Encoding objects | 384 | **Present** | No hierarchical parent; export uses hierarchical browse only |
| Role children (AccessRestrictions=3) | 236 | **Present** | Require SignAndEncrypt; export used SecurityMode=None |
| Optional / Placeholder (direct) | 129 | Correctly absent | ModellingRule is Optional or Placeholder |
| Optional ancestor missing | 284 | Correctly absent | Parent chain includes Optional/Placeholder node |
| Other | 292 | See below | Detailed analysis per node follows |
| **Total not in export** | **1,325** | | |

## Validation Steps

1. Read `plans/missing-nodes-analysis.md` and `plans/filtered-missing-nodes.md` (the two reports from a prior agent)
2. Connected to the running ConsoleReferenceServer (PID 25040) via OPC UA (Anonymous + Admin, SecurityMode=None)
3. **Read encoding node IDs** (i=121..i=32825) -- all 384 returned `Good` with correct BrowseNames
4. **Browsed DataType HasEncoding** (e.g. i=296 Argument) -- confirmed encoding objects are reachable via non-hierarchical refs
5. **Browsed from i=121** with all reference types -- confirmed only non-hierarchical refs exist (HasEncoding inverse, HasDescription, HasTypeDefinition)
6. **Browsed role parents** (Observer i=15668, RoleSetType i=15607) -- all children present including AddIdentity, Identities, AddRole, RemoveRole
7. **Read role children** as admin -- returned `BadSecurityModeInsufficient`, confirming they exist but require encrypted connection
8. **Exported server address space** via ExportNodeSet (hierarchical browse from Root) -- confirmed encoding and access-restricted nodes absent from export
9. **Checked spec XML** -- confirmed `AccessRestrictions="3"` on role children; confirmed encoding objects have no `ParentNodeId`
10. **Reviewed source generator** -- `CollectEncodingNodes()` at `NodeStateGenerator.cs:2336` correctly emits all 384 encoding objects into `AddOpcUa()`
11. **Verified generated output** -- `Opc.Ua.NodeStates.ex.g.cs` (21.7 MB) contains 1,931 nodes in `AddOpcUa()` including 1,074 encoding-related Create calls

## Remaining 292 Nodes -- Detailed Analysis

| Structural Category | Count | Fix Needed |
|---------------------|------:|------------|
| Type-level children (mandatory on ObjectTypes the server does not instantiate) | 212 | No |
| Instance-level children (concrete server objects) | 48 | 1 recommended |
| Orphan property templates (no ParentNodeId) | 32 | No |

### Type-Level Children (212 nodes)

These are Mandatory children defined on ObjectTypes in the spec. They are **not
instantiated** because the reference server does not create instances of these types.
When a server does instantiate one of these types, the children are created
automatically from the type definition.

**Fix needed: No.** These are type-definition metadata, not runtime nodes.

<details>
<summary>Type-level children by parent ObjectType</summary>

#### CertificateGroupType (i=12555) -- 32 nodes

**Reason:** GDS/Push certificate management type. Server does not implement push certificate management.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=13599 | TrustList | Object | Mandatory |
| i=13600 | Size | Variable | Mandatory |
| i=13601 | Writable | Variable | Mandatory |
| i=13602 | UserWritable | Variable | Mandatory |
| i=13603 | OpenCount | Variable | Mandatory |
| i=13605 | Open | Method | Mandatory |
| i=13606 | InputArguments | Variable | Mandatory |
| i=13607 | OutputArguments | Variable | Mandatory |
| i=13608 | Close | Method | Mandatory |
| i=13609 | InputArguments | Variable | Mandatory |
| i=13610 | Read | Method | Mandatory |
| i=13611 | InputArguments | Variable | Mandatory |
| i=13612 | OutputArguments | Variable | Mandatory |
| i=13613 | Write | Method | Mandatory |
| i=13614 | InputArguments | Variable | Mandatory |
| i=13615 | GetPosition | Method | Mandatory |
| i=13616 | InputArguments | Variable | Mandatory |
| i=13617 | OutputArguments | Variable | Mandatory |
| i=13618 | SetPosition | Method | Mandatory |
| i=13619 | InputArguments | Variable | Mandatory |
| i=13620 | LastUpdateTime | Variable | Mandatory |
| i=13621 | OpenWithMasks | Method | Mandatory |
| i=13622 | InputArguments | Variable | Mandatory |
| i=13623 | OutputArguments | Variable | Mandatory |
| i=13624 | CloseAndUpdate | Method | Mandatory |
| i=13625 | InputArguments | Variable | Mandatory |
| i=13626 | OutputArguments | Variable | Mandatory |
| i=13627 | AddCertificate | Method | Mandatory |
| i=13628 | InputArguments | Variable | Mandatory |
| i=13629 | RemoveCertificate | Method | Mandatory |
| i=13630 | InputArguments | Variable | Mandatory |
| i=13631 | CertificateTypes | Variable | Mandatory |

#### PubSubKeyPushTargetType (i=25337) -- 15 nodes

**Reason:** PubSub security key push target type. Server does not implement SKS key push.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=25340 | SecurityPolicyUri | Variable | Mandatory |
| i=25634 | ApplicationUri | Variable | Mandatory |
| i=25635 | EndpointUrl | Variable | Mandatory |
| i=25636 | UserTokenType | Variable | Mandatory |
| i=25637 | RequestedKeyCount | Variable | Mandatory |
| i=25638 | RetryInterval | Variable | Mandatory |
| i=25639 | LastPushExecutionTime | Variable | Mandatory |
| i=25640 | LastPushErrorTime | Variable | Mandatory |
| i=25641 | ConnectSecurityGroups | Method | Mandatory |
| i=25642 | InputArguments | Variable | Mandatory |
| i=25643 | OutputArguments | Variable | Mandatory |
| i=25644 | DisconnectSecurityGroups | Method | Mandatory |
| i=25645 | InputArguments | Variable | Mandatory |
| i=25646 | OutputArguments | Variable | Mandatory |
| i=25647 | TriggerKeyUpdate | Method | Mandatory |

#### SessionsDiagnosticsSummaryType (i=2026) -- 11 nodes

**Reason:** Session diagnostics type. The server uses a simplified diagnostics model.

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

#### TrustListType (i=12522) -- 11 nodes

**Reason:** Trust list file type with Open/Close/Read/Write methods. Server does not expose TrustList management via this type.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=12542 | LastUpdateTime | Variable | Mandatory |
| i=12543 | OpenWithMasks | Method | Mandatory |
| i=12544 | InputArguments | Variable | Mandatory |
| i=12545 | OutputArguments | Variable | Mandatory |
| i=12546 | CloseAndUpdate | Method | Mandatory |
| i=12547 | OutputArguments | Variable | Mandatory |
| i=12548 | AddCertificate | Method | Mandatory |
| i=12549 | InputArguments | Variable | Mandatory |
| i=12550 | RemoveCertificate | Method | Mandatory |
| i=12551 | InputArguments | Variable | Mandatory |
| i=12705 | InputArguments | Variable | Mandatory |

#### SessionDiagnosticsObjectType (i=2029) -- 10 nodes

**Reason:** Session diagnostics type with SessionSecurityDiagnostics. Simplified diagnostics model.

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

#### AlarmConditionType (i=2915) -- 10 nodes

**Reason:** TrueState/FalseState properties on TwoStateVariable children. These are display-hint properties, optional in practice.

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

#### ServerConfigurationType (i=12581) -- 9 nodes

**Reason:** Server push configuration type (UpdateCertificate, CreateSigningRequest). Not implemented by reference server.

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

#### ApplicationConfigurationFolderType (i=16662) -- 9 nodes

**Reason:** Application configuration folder with UpdateCertificate etc. Same as ServerConfigurationType.

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

#### UserManagementType (i=24264) -- 9 nodes

**Reason:** User management type (AddUser, ModifyUser, RemoveUser, ChangePassword). Server does not implement user management API.

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

#### ProvisionableDeviceType (i=26871) -- 9 nodes

**Reason:** Provisioning workflow type. Same certificate management pattern -- not implemented.

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

#### NonExclusiveLimitAlarmType (i=9906) -- 8 nodes

**Reason:** TrueState/FalseState display-hint properties on alarm state variables.

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

#### ConditionType (i=2782) -- 7 nodes

**Reason:** ConditionRefresh/ConditionRefresh2 and SupportsFilteredRetain. Server provides these on instances, not the type.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=3875 | ConditionRefresh | Method | - |
| i=3876 | InputArguments | Variable | Mandatory |
| i=9018 | TrueState | Variable | - |
| i=9019 | FalseState | Variable | - |
| i=12912 | ConditionRefresh2 | Method | - |
| i=12913 | InputArguments | Variable | Mandatory |
| i=32060 | SupportsFilteredRetain | Variable | - |

#### PublishSubscribeType (i=14416) -- 7 nodes

**Reason:** PubSub connection and diagnostic methods. Server does not fully implement PubSub.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=18727 | Reset | Method | Mandatory |
| i=25426 | ReserveIds | Method | Mandatory |
| i=25427 | InputArguments | Variable | Mandatory |
| i=25428 | OutputArguments | Variable | Mandatory |
| i=25429 | CloseAndUpdate | Method | Mandatory |
| i=25430 | InputArguments | Variable | Mandatory |
| i=25431 | OutputArguments | Variable | Mandatory |

#### RoleSetType (i=15607) -- 6 nodes

**Reason:** AddRole/RemoveRole methods. Present but only accessible with encrypted connection (AccessRestrictions=3).

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=15997 | AddRole | Method | Mandatory |
| i=15998 | InputArguments | Variable | Mandatory |
| i=15999 | OutputArguments | Variable | Mandatory |
| i=16000 | RemoveRole | Method | Mandatory |
| i=16001 | InputArguments | Variable | Mandatory |
| i=16162 | Identities | Variable | Mandatory |

#### PubSubConfigurationType (i=25482) -- 6 nodes

**Reason:** PubSub configuration file type with ReserveIds/CloseAndUpdate.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=25505 | ReserveIds | Method | Mandatory |
| i=25506 | InputArguments | Variable | Mandatory |
| i=25507 | OutputArguments | Variable | Mandatory |
| i=25508 | CloseAndUpdate | Method | Mandatory |
| i=25509 | InputArguments | Variable | Mandatory |
| i=25510 | OutputArguments | Variable | Mandatory |

#### SecurityGroupType (i=15471) -- 5 nodes

**Reason:** Security group management for PubSub. Server does not implement SKS security groups.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=15046 | KeyLifetime | Variable | Mandatory |
| i=15047 | SecurityPolicyUri | Variable | Mandatory |
| i=15048 | MaxFutureKeyCount | Variable | Mandatory |
| i=15056 | MaxPastKeyCount | Variable | Mandatory |
| i=15472 | SecurityGroupId | Variable | Mandatory |

#### SecurityGroupFolderType (i=15452) -- 5 nodes

**Reason:** Security group folder management for PubSub. Server does not implement SKS.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=15461 | AddSecurityGroup | Method | Mandatory |
| i=15462 | InputArguments | Variable | Mandatory |
| i=15463 | OutputArguments | Variable | Mandatory |
| i=15464 | RemoveSecurityGroup | Method | Mandatory |
| i=15465 | InputArguments | Variable | Mandatory |

#### ServerCapabilitiesType (i=2013) -- 5 nodes

**Reason:** Server capabilities properties the reference server does not populate.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=16296 | AddRole | Method | Mandatory |
| i=16297 | InputArguments | Variable | Mandatory |
| i=16298 | OutputArguments | Variable | Mandatory |
| i=16299 | RemoveRole | Method | Mandatory |
| i=16300 | InputArguments | Variable | Mandatory |

#### PubSubKeyPushTargetFolderType (i=25346) -- 5 nodes

**Reason:** PubSub key push target folder management. Server does not implement SKS.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=25366 | AddPushTarget | Method | Mandatory |
| i=25367 | InputArguments | Variable | Mandatory |
| i=25368 | OutputArguments | Variable | Mandatory |
| i=25369 | RemovePushTarget | Method | Mandatory |
| i=25370 | InputArguments | Variable | Mandatory |

#### ProgramStateMachineType (i=2391) -- 4 nodes

**Reason:** Program state machine properties (Creatable, InstanceCount). Not used by reference server.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=2392 | Creatable | Variable | - |
| i=2396 | InstanceCount | Variable | - |
| i=2397 | MaxInstanceCount | Variable | - |
| i=2398 | MaxRecycleCount | Variable | - |

#### AcknowledgeableConditionType (i=2881) -- 4 nodes

**Reason:** TrueState/FalseState on TwoStateVariable children.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=9100 | TrueState | Variable | - |
| i=9101 | FalseState | Variable | - |
| i=9109 | TrueState | Variable | - |
| i=9110 | FalseState | Variable | - |

#### DialogConditionType (i=2830) -- 2 nodes

**Reason:** TrueState/FalseState on DialogState.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=9062 | TrueState | Variable | - |
| i=9063 | FalseState | Variable | - |

#### AlarmGroupType (i=16405) -- 2 nodes

**Reason:** TrueState/FalseState display-hint on alarm group state variables.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=16472 | TrueState | Variable | - |
| i=16473 | FalseState | Variable | - |

#### AlarmSuppressionGroupType (i=32064) -- 2 nodes

**Reason:** TrueState/FalseState display-hint on alarm suppression state variables.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=23586 | TrueState | Variable | - |
| i=23587 | FalseState | Variable | - |

#### ServerType (i=2004) -- 1 nodes

**Reason:** Type-level child of ServerType.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=3113 | SessionSecurityDiagnosticsArray | Variable | Mandatory |

#### ServerDiagnosticsType (i=2020) -- 1 nodes

**Reason:** Diagnostics array that the server explicitly deletes (SamplingIntervalDiagnosticsArray).

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=3130 | SessionSecurityDiagnosticsArray | Variable | Mandatory |

#### RoleType (i=15620) -- 1 nodes

**Reason:** Identities variable on RoleType. Present on instances but only accessible with encryption.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=16173 | Identities | Variable | Mandatory |

#### WriterGroupType (i=17725) -- 1 nodes

**Reason:** Type-level children not instantiated by the reference server.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=17824 | Reset | Method | Mandatory |

#### PubSubConnectionType (i=14209) -- 1 nodes

**Reason:** Type-level children not instantiated by the reference server.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=19253 | Reset | Method | Mandatory |

#### DataSetWriterType (i=15298) -- 1 nodes

**Reason:** Type-level children not instantiated by the reference server.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=19562 | Reset | Method | Mandatory |

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

#### ReaderGroupType (i=17999) -- 1 nodes

**Reason:** Type-level children not instantiated by the reference server.

| NodeId | BrowseName | NodeClass | MR |
|--------|------------|-----------|-----|
| i=21027 | Reset | Method | Mandatory |

</details>

### Instance-Level Children (48 nodes)

These are children of concrete objects in the running server.

#### Server > ServerDiagnostics children (16 nodes)

Path: `Server > ServerDiagnostics > ServerDiagnosticsSummary > {sub-variables}`

**Reason:** The `DiagnosticsNodeManager` explicitly deletes `SamplingIntervalDiagnosticsArray`
(`DiagnosticsNodeManager.cs:156-161`). The remaining `ServerDiagnosticsSummary` sub-properties
(ServerViewCount, CurrentSessionCount, etc.) and `SubscriptionDiagnosticsArray` use a dynamic
diagnostics model where values are computed on demand rather than stored as static nodes.

**Fix needed:** No. Deliberate simplification; the reference server uses dynamic diagnostics.

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

`i=2994` -- Boolean variable indicating whether the server supports auditing.

**Reason:** The reference server does not enable the auditing subsystem.

**Fix needed:** Recommended. Per Part 5 section 6.3.3, even non-auditing servers should
expose this variable with value `false`. One-line addition in `DiagnosticsNodeManager`.

#### HA Configuration (6 nodes)

Path: `HA Configuration > AggregateConfiguration > {TreatUncertainAsBad, PercentDataBad, ...}`

**Reason:** The reference server does not implement Historical Access.

**Fix needed:** No. Historical Access is an optional server capability.

| NodeId | BrowseName |
|--------|------------|
| i=11203 | AggregateConfiguration |
| i=11204 | TreatUncertainAsBad |
| i=11205 | PercentDataBad |
| i=11206 | PercentDataGood |
| i=11207 | UseSlopedExtrapolation |
| i=11208 | Stepped |

#### FileSystem (11 nodes)

Path: `FileSystem > {CreateDirectory, CreateFile, Delete, MoveOrCopy}`

**Reason:** The reference server does not expose filesystem access.

**Fix needed:** No. FileSystem is an optional server feature.

#### PublishSubscribe instance children (14 nodes)

Path: `PublishSubscribe > {AddConnection, RemoveConnection, Diagnostics.Reset, PubSubConfiguration.*}`

**Reason:** The PublishSubscribe singleton exists but AddConnection/RemoveConnection methods,
diagnostic Reset, and PubSubConfiguration file methods (Write, ReserveIds, CloseAndUpdate)
are not implemented.

**Fix needed:** No, unless full PubSub support is a goal. This is a large feature effort.

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

### Orphan Properties (32 nodes)

Variable nodes with **no `ParentNodeId`**. These are canonical type-level property definitions
(e.g., `NodeVersion`, `Icon`, `InputArguments`, `EnumStrings`) that serve as templates.
They are referenced by types via ModellingRules but are not standalone runtime nodes.

**Fix needed: No.** Design-time property templates, not runtime address-space nodes.

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
| i=12522 | TrustListType | ObjectType |
| i=12555 | CertificateGroupType | ObjectType |
| i=12745 | OptionSetValues | Variable |
| i=12908 | MaxByteStringLength | Variable |
| i=14415 | ServerNetworkGroups | Variable |
| i=15002 | MaxCharacters | Variable |
| i=15452 | SecurityGroupFolderType | ObjectType |
| i=15471 | SecurityGroupType | ObjectType |
| i=16314 | FileSystem | Object |
| i=17605 | DefaultInstanceBrowseName | Variable |
| i=23501 | CurrencyUnit | Variable |
| i=25337 | PubSubKeyPushTargetType | ObjectType |
| i=25346 | PubSubKeyPushTargetFolderType | ObjectType |
| i=32750 | OptionSetLength | Variable |

## Fix Summary

| Category | Count | Fix Needed | Effort |
|----------|------:|------------|--------|
| Type-level children (ObjectType definitions) | 212 | No | - |
| Server diagnostics sub-variables | 16 | No (deliberate simplification) | - |
| Server.Auditing | 1 | **Recommended** | Trivial |
| HA Configuration | 6 | No (optional capability) | - |
| FileSystem | 11 | No (optional capability) | - |
| PublishSubscribe children | 14 | No (optional capability) | Large |
| Orphan property templates | 32 | No (design-time only) | - |
| **Total** | **292** | **1 recommended** | |
