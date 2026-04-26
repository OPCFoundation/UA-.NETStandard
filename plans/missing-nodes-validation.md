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
| Other | 292 | Deliberate omissions | Server diagnostics, type-level templates, etc. |
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

<details>
<summary>Other missing nodes (292) -- deliberate omissions and type-level templates</summary>

| NodeId | BrowseName | NodeClass | Parent | ModellingRule |
|--------|------------|-----------|--------|--------------|
| i=2028 | SessionSecurityDiagnosticsArray | Variable | i=2026 | Mandatory |
| i=2031 | SessionSecurityDiagnostics | Variable | i=2029 | Mandatory |
| i=2275 | ServerDiagnosticsSummary | Variable | i=2274 | - |
| i=2276 | ServerViewCount | Variable | i=2275 | - |
| i=2277 | CurrentSessionCount | Variable | i=2275 | - |
| i=2278 | CumulatedSessionCount | Variable | i=2275 | - |
| i=2279 | SecurityRejectedSessionCount | Variable | i=2275 | - |
| i=2281 | SessionTimeoutCount | Variable | i=2275 | - |
| i=2282 | SessionAbortCount | Variable | i=2275 | - |
| i=2284 | PublishingIntervalCount | Variable | i=2275 | - |
| i=2285 | CurrentSubscriptionCount | Variable | i=2275 | - |
| i=2286 | CumulatedSubscriptionCount | Variable | i=2275 | - |
| i=2287 | SecurityRejectedRequestsCount | Variable | i=2275 | - |
| i=2288 | RejectedRequestsCount | Variable | i=2275 | - |
| i=2289 | SamplingIntervalDiagnosticsArray | Variable | i=2274 | - |
| i=2290 | SubscriptionDiagnosticsArray | Variable | i=2274 | - |
| i=2392 | Creatable | Variable | i=2391 | - |
| i=2396 | InstanceCount | Variable | i=2391 | - |
| i=2397 | MaxInstanceCount | Variable | i=2391 | - |
| i=2398 | MaxRecycleCount | Variable | i=2391 | - |
| i=2994 | Auditing | Variable | i=2253 | - |
| i=3067 | Icon | Variable | - | - |
| i=3068 | NodeVersion | Variable | - | - |
| i=3069 | LocalTime | Variable | - | - |
| i=3070 | AllowNulls | Variable | - | - |
| i=3071 | EnumValues | Variable | - | - |
| i=3072 | InputArguments | Variable | - | - |
| i=3073 | OutputArguments | Variable | - | - |
| i=3113 | SessionSecurityDiagnosticsArray | Variable | i=3111 | Mandatory |
| i=3130 | SessionSecurityDiagnosticsArray | Variable | i=2744 | Mandatory |
| i=3179 | SessionId | Variable | i=2031 | Mandatory |
| i=3180 | ClientUserIdOfSession | Variable | i=2031 | Mandatory |
| i=3181 | ClientUserIdHistory | Variable | i=2031 | Mandatory |
| i=3182 | AuthenticationMechanism | Variable | i=2031 | Mandatory |
| i=3183 | Encoding | Variable | i=2031 | Mandatory |
| i=3184 | TransportProtocol | Variable | i=2031 | Mandatory |
| i=3185 | SecurityMode | Variable | i=2031 | Mandatory |
| i=3186 | SecurityPolicyUri | Variable | i=2031 | Mandatory |
| i=3187 | ClientCertificate | Variable | i=2031 | Mandatory |
| i=3705 | RejectedSessionCount | Variable | i=2275 | - |
| i=3707 | SessionDiagnosticsArray | Variable | i=3706 | - |
| i=3875 | ConditionRefresh | Method | i=2782 | - |
| i=3876 | InputArguments | Variable | i=3875 | Mandatory |
| i=9018 | TrueState | Variable | i=9011 | - |
| i=9019 | FalseState | Variable | i=9011 | - |
| i=9062 | TrueState | Variable | i=9055 | - |
| i=9063 | FalseState | Variable | i=9055 | - |
| i=9100 | TrueState | Variable | i=9093 | - |
| i=9101 | FalseState | Variable | i=9093 | - |
| i=9109 | TrueState | Variable | i=9102 | - |
| i=9110 | FalseState | Variable | i=9102 | - |
| i=9167 | TrueState | Variable | i=9160 | - |
| i=9168 | FalseState | Variable | i=9160 | - |
| i=9176 | TrueState | Variable | i=9169 | - |
| i=9177 | FalseState | Variable | i=9169 | - |
| i=10027 | TrueState | Variable | i=10020 | - |
| i=10028 | FalseState | Variable | i=10020 | - |
| i=10036 | TrueState | Variable | i=10029 | - |
| i=10037 | FalseState | Variable | i=10029 | - |
| i=10045 | TrueState | Variable | i=10038 | - |
| i=10046 | FalseState | Variable | i=10038 | - |
| i=10054 | TrueState | Variable | i=10047 | - |
| i=10055 | FalseState | Variable | i=10047 | - |
| i=11202 | HA Configuration | Object | - | - |
| i=11203 | AggregateConfiguration | Object | i=11202 | - |
| i=11204 | TreatUncertainAsBad | Variable | i=11203 | - |
| i=11205 | PercentDataBad | Variable | i=11203 | - |
| i=11206 | PercentDataGood | Variable | i=11203 | - |
| i=11207 | UseSlopedExtrapolation | Variable | i=11203 | - |
| i=11208 | Stepped | Variable | i=11202 | - |
| i=11214 | Annotations | Variable | - | - |
| i=11215 | HistoricalEventFilter | Variable | - | - |
| i=11312 | CurrentServerId | Variable | - | - |
| i=11314 | ServerUriArray | Variable | - | - |
| i=11432 | EnumStrings | Variable | - | - |
| i=11433 | ValueAsText | Variable | - | - |
| i=11498 | MaxStringLength | Variable | - | - |
| i=11512 | MaxArrayLength | Variable | - | - |
| i=11513 | EngineeringUnits | Variable | - | - |
| i=12142 | SessionSecurityDiagnostics | Variable | i=12097 | Mandatory |
| i=12143 | SessionId | Variable | i=12142 | Mandatory |
| i=12144 | ClientUserIdOfSession | Variable | i=12142 | Mandatory |
| i=12145 | ClientUserIdHistory | Variable | i=12142 | Mandatory |
| i=12146 | AuthenticationMechanism | Variable | i=12142 | Mandatory |
| i=12147 | Encoding | Variable | i=12142 | Mandatory |
| i=12148 | TransportProtocol | Variable | i=12142 | Mandatory |
| i=12149 | SecurityMode | Variable | i=12142 | Mandatory |
| i=12150 | SecurityPolicyUri | Variable | i=12142 | Mandatory |
| i=12151 | ClientCertificate | Variable | i=12142 | Mandatory |
| i=12170 | ViewVersion | Variable | - | - |
| i=12522 | TrustListType | ObjectType | - | - |
| i=12542 | LastUpdateTime | Variable | i=12522 | Mandatory |
| i=12543 | OpenWithMasks | Method | i=12522 | Mandatory |
| i=12544 | InputArguments | Variable | i=12543 | Mandatory |
| i=12545 | OutputArguments | Variable | i=12543 | Mandatory |
| i=12546 | CloseAndUpdate | Method | i=12522 | Mandatory |
| i=12547 | OutputArguments | Variable | i=12546 | Mandatory |
| i=12548 | AddCertificate | Method | i=12522 | Mandatory |
| i=12549 | InputArguments | Variable | i=12548 | Mandatory |
| i=12550 | RemoveCertificate | Method | i=12522 | Mandatory |
| i=12551 | InputArguments | Variable | i=12550 | Mandatory |
| i=12555 | CertificateGroupType | ObjectType | - | - |
| i=12616 | UpdateCertificate | Method | i=12581 | Mandatory |
| i=12617 | InputArguments | Variable | i=12616 | Mandatory |
| i=12618 | OutputArguments | Variable | i=12616 | Mandatory |
| i=12705 | InputArguments | Variable | i=12546 | Mandatory |
| i=12731 | CreateSigningRequest | Method | i=12581 | Mandatory |
| i=12732 | InputArguments | Variable | i=12731 | Mandatory |
| i=12733 | OutputArguments | Variable | i=12731 | Mandatory |
| i=12734 | ApplyChanges | Method | i=12581 | Mandatory |
| i=12745 | OptionSetValues | Variable | - | - |
| i=12775 | GetRejectedList | Method | i=12581 | Mandatory |
| i=12776 | OutputArguments | Variable | i=12775 | Mandatory |
| i=12908 | MaxByteStringLength | Variable | - | - |
| i=12912 | ConditionRefresh2 | Method | i=2782 | - |
| i=12913 | InputArguments | Variable | i=12912 | Mandatory |
| i=13599 | TrustList | Object | i=12555 | Mandatory |
| i=13600 | Size | Variable | i=13599 | Mandatory |
| i=13601 | Writable | Variable | i=13599 | Mandatory |
| i=13602 | UserWritable | Variable | i=13599 | Mandatory |
| i=13603 | OpenCount | Variable | i=13599 | Mandatory |
| i=13605 | Open | Method | i=13599 | Mandatory |
| i=13606 | InputArguments | Variable | i=13605 | Mandatory |
| i=13607 | OutputArguments | Variable | i=13605 | Mandatory |
| i=13608 | Close | Method | i=13599 | Mandatory |
| i=13609 | InputArguments | Variable | i=13608 | Mandatory |
| i=13610 | Read | Method | i=13599 | Mandatory |
| i=13611 | InputArguments | Variable | i=13610 | Mandatory |
| i=13612 | OutputArguments | Variable | i=13610 | Mandatory |
| i=13613 | Write | Method | i=13599 | Mandatory |
| i=13614 | InputArguments | Variable | i=13613 | Mandatory |
| i=13615 | GetPosition | Method | i=13599 | Mandatory |
| i=13616 | InputArguments | Variable | i=13615 | Mandatory |
| i=13617 | OutputArguments | Variable | i=13615 | Mandatory |
| i=13618 | SetPosition | Method | i=13599 | Mandatory |
| i=13619 | InputArguments | Variable | i=13618 | Mandatory |
| i=13620 | LastUpdateTime | Variable | i=13599 | Mandatory |
| i=13621 | OpenWithMasks | Method | i=13599 | Mandatory |
| i=13622 | InputArguments | Variable | i=13621 | Mandatory |
| i=13623 | OutputArguments | Variable | i=13621 | Mandatory |
| i=13624 | CloseAndUpdate | Method | i=13599 | Mandatory |
| i=13625 | InputArguments | Variable | i=13624 | Mandatory |
| i=13626 | OutputArguments | Variable | i=13624 | Mandatory |
| i=13627 | AddCertificate | Method | i=13599 | Mandatory |
| i=13628 | InputArguments | Variable | i=13627 | Mandatory |
| i=13629 | RemoveCertificate | Method | i=13599 | Mandatory |
| i=13630 | InputArguments | Variable | i=13629 | Mandatory |
| i=13631 | CertificateTypes | Variable | i=12555 | Mandatory |
| i=14415 | ServerNetworkGroups | Variable | - | - |
| i=15002 | MaxCharacters | Variable | - | - |
| i=15046 | KeyLifetime | Variable | i=15471 | Mandatory |
| i=15047 | SecurityPolicyUri | Variable | i=15471 | Mandatory |
| i=15048 | MaxFutureKeyCount | Variable | i=15471 | Mandatory |
| i=15056 | MaxPastKeyCount | Variable | i=15471 | Mandatory |
| i=15452 | SecurityGroupFolderType | ObjectType | - | - |
| i=15461 | AddSecurityGroup | Method | i=15452 | Mandatory |
| i=15462 | InputArguments | Variable | i=15461 | Mandatory |
| i=15463 | OutputArguments | Variable | i=15461 | Mandatory |
| i=15464 | RemoveSecurityGroup | Method | i=15452 | Mandatory |
| i=15465 | InputArguments | Variable | i=15464 | Mandatory |
| i=15471 | SecurityGroupType | ObjectType | - | - |
| i=15472 | SecurityGroupId | Variable | i=15471 | Mandatory |
| i=15914 | AddSecurityGroup | Method | i=15913 | Mandatory |
| i=15915 | InputArguments | Variable | i=15914 | Mandatory |
| i=15916 | OutputArguments | Variable | i=15914 | Mandatory |
| i=15917 | RemoveSecurityGroup | Method | i=15913 | Mandatory |
| i=15918 | InputArguments | Variable | i=15917 | Mandatory |
| i=15997 | AddRole | Method | i=15607 | Mandatory |
| i=15998 | InputArguments | Variable | i=15997 | Mandatory |
| i=15999 | OutputArguments | Variable | i=15997 | Mandatory |
| i=16000 | RemoveRole | Method | i=15607 | Mandatory |
| i=16001 | InputArguments | Variable | i=16000 | Mandatory |
| i=16162 | Identities | Variable | i=15608 | Mandatory |
| i=16173 | Identities | Variable | i=15620 | Mandatory |
| i=16296 | AddRole | Method | i=16295 | Mandatory |
| i=16297 | InputArguments | Variable | i=16296 | Mandatory |
| i=16298 | OutputArguments | Variable | i=16296 | Mandatory |
| i=16299 | RemoveRole | Method | i=16295 | Mandatory |
| i=16300 | InputArguments | Variable | i=16299 | Mandatory |
| i=16314 | FileSystem | Object | - | - |
| i=16348 | CreateDirectory | Method | i=16314 | - |
| i=16349 | InputArguments | Variable | i=16348 | - |
| i=16350 | OutputArguments | Variable | i=16348 | - |
| i=16351 | CreateFile | Method | i=16314 | - |
| i=16352 | InputArguments | Variable | i=16351 | - |
| i=16353 | OutputArguments | Variable | i=16351 | - |
| i=16354 | Delete | Method | i=16314 | - |
| i=16355 | InputArguments | Variable | i=16354 | - |
| i=16356 | MoveOrCopy | Method | i=16314 | - |
| i=16357 | InputArguments | Variable | i=16356 | - |
| i=16358 | OutputArguments | Variable | i=16356 | - |
| i=16378 | TrueState | Variable | i=16371 | - |
| i=16379 | FalseState | Variable | i=16371 | - |
| i=16387 | TrueState | Variable | i=16380 | - |
| i=16388 | FalseState | Variable | i=16380 | - |
| i=16472 | TrueState | Variable | i=16465 | - |
| i=16473 | FalseState | Variable | i=16465 | - |
| i=17366 | AddConnection | Method | i=14443 | - |
| i=17367 | InputArguments | Variable | i=17366 | - |
| i=17368 | OutputArguments | Variable | i=17366 | - |
| i=17369 | RemoveConnection | Method | i=14443 | - |
| i=17370 | InputArguments | Variable | i=17369 | - |
| i=17421 | Reset | Method | i=17409 | - |
| i=17605 | DefaultInstanceBrowseName | Variable | - | - |
| i=17824 | Reset | Method | i=17812 | Mandatory |
| i=18197 | TrueState | Variable | i=18190 | - |
| i=18198 | FalseState | Variable | i=18190 | - |
| i=18533 | UpdateCertificate | Method | i=16663 | Mandatory |
| i=18534 | InputArguments | Variable | i=18533 | Mandatory |
| i=18535 | OutputArguments | Variable | i=18533 | Mandatory |
| i=18539 | ApplyChanges | Method | i=16663 | Mandatory |
| i=18541 | CreateSigningRequest | Method | i=16663 | Mandatory |
| i=18542 | InputArguments | Variable | i=18541 | Mandatory |
| i=18543 | OutputArguments | Variable | i=18541 | Mandatory |
| i=18544 | GetRejectedList | Method | i=16663 | Mandatory |
| i=18545 | OutputArguments | Variable | i=18544 | Mandatory |
| i=18727 | Reset | Method | i=18715 | Mandatory |
| i=19253 | Reset | Method | i=19241 | Mandatory |
| i=19562 | Reset | Method | i=19550 | Mandatory |
| i=19621 | Reset | Method | i=19609 | Mandatory |
| i=19689 | Reset | Method | i=19677 | Mandatory |
| i=21027 | Reset | Method | i=21015 | Mandatory |
| i=23501 | CurrencyUnit | Variable | - | - |
| i=23586 | TrueState | Variable | i=23579 | - |
| i=23587 | FalseState | Variable | i=23579 | - |
| i=24265 | Users | Variable | i=24264 | Mandatory |
| i=24269 | AddUser | Method | i=24264 | Mandatory |
| i=24270 | InputArguments | Variable | i=24269 | Mandatory |
| i=24271 | ModifyUser | Method | i=24264 | Mandatory |
| i=24272 | InputArguments | Variable | i=24271 | Mandatory |
| i=24273 | RemoveUser | Method | i=24264 | Mandatory |
| i=24274 | InputArguments | Variable | i=24273 | Mandatory |
| i=24275 | ChangePassword | Method | i=24264 | Mandatory |
| i=24276 | InputArguments | Variable | i=24275 | Mandatory |
| i=25278 | AddPushTarget | Method | i=25277 | Mandatory |
| i=25279 | InputArguments | Variable | i=25278 | Mandatory |
| i=25280 | OutputArguments | Variable | i=25278 | Mandatory |
| i=25281 | RemovePushTarget | Method | i=25277 | Mandatory |
| i=25282 | InputArguments | Variable | i=25281 | Mandatory |
| i=25337 | PubSubKeyPushTargetType | ObjectType | - | - |
| i=25340 | SecurityPolicyUri | Variable | i=25337 | Mandatory |
| i=25346 | PubSubKeyPushTargetFolderType | ObjectType | - | - |
| i=25366 | AddPushTarget | Method | i=25346 | Mandatory |
| i=25367 | InputArguments | Variable | i=25366 | Mandatory |
| i=25368 | OutputArguments | Variable | i=25366 | Mandatory |
| i=25369 | RemovePushTarget | Method | i=25346 | Mandatory |
| i=25370 | InputArguments | Variable | i=25369 | Mandatory |
| i=25426 | ReserveIds | Method | i=25403 | Mandatory |
| i=25427 | InputArguments | Variable | i=25426 | Mandatory |
| i=25428 | OutputArguments | Variable | i=25426 | Mandatory |
| i=25429 | CloseAndUpdate | Method | i=25403 | Mandatory |
| i=25430 | InputArguments | Variable | i=25429 | Mandatory |
| i=25431 | OutputArguments | Variable | i=25429 | Mandatory |
| i=25467 | Write | Method | i=25451 | - |
| i=25468 | InputArguments | Variable | i=25467 | - |
| i=25474 | ReserveIds | Method | i=25451 | - |
| i=25475 | InputArguments | Variable | i=25474 | - |
| i=25476 | OutputArguments | Variable | i=25474 | - |
| i=25477 | CloseAndUpdate | Method | i=25451 | - |
| i=25478 | InputArguments | Variable | i=25477 | - |
| i=25479 | OutputArguments | Variable | i=25477 | - |
| i=25505 | ReserveIds | Method | i=25482 | Mandatory |
| i=25506 | InputArguments | Variable | i=25505 | Mandatory |
| i=25507 | OutputArguments | Variable | i=25505 | Mandatory |
| i=25508 | CloseAndUpdate | Method | i=25482 | Mandatory |
| i=25509 | InputArguments | Variable | i=25508 | Mandatory |
| i=25510 | OutputArguments | Variable | i=25508 | Mandatory |
| i=25634 | ApplicationUri | Variable | i=25337 | Mandatory |
| i=25635 | EndpointUrl | Variable | i=25337 | Mandatory |
| i=25636 | UserTokenType | Variable | i=25337 | Mandatory |
| i=25637 | RequestedKeyCount | Variable | i=25337 | Mandatory |
| i=25638 | RetryInterval | Variable | i=25337 | Mandatory |
| i=25639 | LastPushExecutionTime | Variable | i=25337 | Mandatory |
| i=25640 | LastPushErrorTime | Variable | i=25337 | Mandatory |
| i=25641 | ConnectSecurityGroups | Method | i=25337 | Mandatory |
| i=25642 | InputArguments | Variable | i=25641 | Mandatory |
| i=25643 | OutputArguments | Variable | i=25641 | Mandatory |
| i=25644 | DisconnectSecurityGroups | Method | i=25337 | Mandatory |
| i=25645 | InputArguments | Variable | i=25644 | Mandatory |
| i=25646 | OutputArguments | Variable | i=25644 | Mandatory |
| i=25647 | TriggerKeyUpdate | Method | i=25337 | Mandatory |
| i=28005 | UpdateCertificate | Method | i=26878 | Mandatory |
| i=28006 | InputArguments | Variable | i=28005 | Mandatory |
| i=28007 | OutputArguments | Variable | i=28005 | Mandatory |
| i=28008 | ApplyChanges | Method | i=26878 | Mandatory |
| i=28010 | CreateSigningRequest | Method | i=26878 | Mandatory |
| i=28011 | InputArguments | Variable | i=28010 | Mandatory |
| i=28012 | OutputArguments | Variable | i=28010 | Mandatory |
| i=28013 | GetRejectedList | Method | i=26878 | Mandatory |
| i=28014 | OutputArguments | Variable | i=28013 | Mandatory |
| i=32060 | SupportsFilteredRetain | Variable | i=2782 | - |
| i=32750 | OptionSetLength | Variable | - | - |

</details>
