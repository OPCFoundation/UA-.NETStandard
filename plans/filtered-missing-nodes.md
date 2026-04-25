# Filtered Missing Nodes Analysis

## Parameters

- **Connection**: sysadmin/demo, SignAndEncrypt, Basic256Sha256
- **Checked-in nodes (ns=0)**: 5283
- **Exported nodes (ns=0)**: 6574
- **Filtered checked-in** (excluding optional/placeholder/encoding): 4126
- **Missing after filtering**: 250

## Exclusions Applied

| Exclusion | Count |
|-----------|-------|
| Optional/None child of type | 551 |
| Encoding node | 384 |
| Child of placeholder | 217 |
| Placeholder node | 5 |

## Missing Nodes by Reason

| Reason | Count |
|--------|-------|
| ModellingRule=None variable | 94 |
| MANDATORY MISSING | 58 |
| Mandatory child of missing parent (cascade) | 39 |
| Optional (filtered out) | 36 |
| ModellingRule=None method | 18 |
| ModellingRule=None object | 5 |

## ⚠️ MANDATORY MISSING (58 nodes)

These nodes have ModellingRule=Mandatory, their parent is present, and they should exist.

| NodeId | BrowseName | NodeClass | Parent |
|--------|-----------|-----------|--------|
| i=15461 | AddSecurityGroup | Method | SecurityGroupFolderType |
| i=15464 | RemoveSecurityGroup | Method | SecurityGroupFolderType |
| i=15914 | AddSecurityGroup | Method | SecurityGroups |
| i=15917 | RemoveSecurityGroup | Method | SecurityGroups |
| i=25278 | AddPushTarget | Method | KeyPushTargets |
| i=25281 | RemovePushTarget | Method | KeyPushTargets |
| i=25366 | AddPushTarget | Method | PubSubKeyPushTargetFolderType |
| i=25369 | RemovePushTarget | Method | PubSubKeyPushTargetFolderType |
| i=25641 | ConnectSecurityGroups | Method | PubSubKeyPushTargetType |
| i=25644 | DisconnectSecurityGroups | Method | PubSubKeyPushTargetType |
| i=3876 | InputArguments | Variable | ConditionRefresh |
| i=12913 | InputArguments | Variable | ConditionRefresh2 |
| i=14480 | InputArguments | Variable | AddPublishedDataItems |
| i=14481 | OutputArguments | Variable | AddPublishedDataItems |
| i=14483 | InputArguments | Variable | AddPublishedEvents |
| i=14484 | OutputArguments | Variable | AddPublishedEvents |
| i=14486 | InputArguments | Variable | RemovePublishedDataSet |
| i=15455 | InputArguments | Variable | AddSecurityGroup |
| i=15456 | OutputArguments | Variable | AddSecurityGroup |
| i=15458 | InputArguments | Variable | RemoveSecurityGroup |
| i=16843 | InputArguments | Variable | AddPublishedDataItemsTemplate |
| i=16853 | OutputArguments | Variable | AddPublishedDataItemsTemplate |
| i=16882 | InputArguments | Variable | AddPublishedEventsTemplate |
| i=16883 | OutputArguments | Variable | AddPublishedEventsTemplate |
| i=16894 | InputArguments | Variable | AddDataSetFolder |
| i=16922 | OutputArguments | Variable | AddDataSetFolder |
| i=16924 | InputArguments | Variable | RemoveDataSetFolder |
| i=23798 | InputArguments | Variable | AddSubscribedDataSet |
| i=23799 | OutputArguments | Variable | AddSubscribedDataSet |
| i=23801 | InputArguments | Variable | RemoveSubscribedDataSet |
| i=23803 | InputArguments | Variable | AddDataSetFolder |
| i=23804 | OutputArguments | Variable | AddDataSetFolder |
| i=23806 | InputArguments | Variable | RemoveDataSetFolder |
| i=23931 | InputArguments | Variable | FindAliasVerbose |
| i=23935 | OutputArguments | Variable | FindAliasVerbose |
| i=23937 | InputArguments | Variable | AddAliasesToCategory |
| i=23959 | OutputArguments | Variable | AddAliasesToCategory |
| i=23961 | InputArguments | Variable | DeleteAliasesFromCategory |
| i=23962 | OutputArguments | Variable | DeleteAliasesFromCategory |
| i=25294 | InputArguments | Variable | AddSecurityGroupFolder |
| i=25295 | OutputArguments | Variable | AddSecurityGroupFolder |
| i=25297 | InputArguments | Variable | RemoveSecurityGroupFolder |
| i=25313 | InputArguments | Variable | AddSecurityGroupFolder |
| i=25314 | OutputArguments | Variable | AddSecurityGroupFolder |
| i=25316 | InputArguments | Variable | RemoveSecurityGroupFolder |
| i=25349 | InputArguments | Variable | AddPushTarget |
| i=25350 | OutputArguments | Variable | AddPushTarget |
| i=25352 | InputArguments | Variable | RemovePushTarget |
| i=25354 | InputArguments | Variable | AddPushTargetFolder |
| i=25355 | OutputArguments | Variable | AddPushTargetFolder |
| i=25357 | InputArguments | Variable | RemovePushTargetFolder |
| i=25372 | InputArguments | Variable | AddPushTargetFolder |
| i=25373 | OutputArguments | Variable | AddPushTargetFolder |
| i=25375 | InputArguments | Variable | RemovePushTargetFolder |
| i=25656 | InputArguments | Variable | ConnectSecurityGroups |
| i=25657 | OutputArguments | Variable | ConnectSecurityGroups |
| i=25659 | InputArguments | Variable | DisconnectSecurityGroups |
| i=25660 | OutputArguments | Variable | DisconnectSecurityGroups |

## Mandatory child of missing parent (cascade) (39)

| NodeId | BrowseName | NodeClass | Parent |
|--------|-----------|-----------|--------|
| i=9185 | Id | Variable | LastTransition |
| i=9462 | Id | Variable | LastTransition |
| i=15462 | InputArguments | Variable | AddSecurityGroup |
| i=15463 | OutputArguments | Variable | AddSecurityGroup |
| i=15465 | InputArguments | Variable | RemoveSecurityGroup |
| i=15915 | InputArguments | Variable | AddSecurityGroup |
| i=15916 | OutputArguments | Variable | AddSecurityGroup |
| i=15918 | InputArguments | Variable | RemoveSecurityGroup |
| i=19900 | DiagnosticsLevel | Variable | SecurityTokenID |
| i=19902 | DiagnosticsLevel | Variable | TimeToNextTokenID |
| i=19955 | Active | Variable | ReceivedInvalidNetworkMessages |
| i=19956 | Classification | Variable | ReceivedInvalidNetworkMessages |
| i=19957 | DiagnosticsLevel | Variable | ReceivedInvalidNetworkMessages |
| i=19960 | Active | Variable | DecryptionErrors |
| i=19961 | Classification | Variable | DecryptionErrors |
| i=19962 | DiagnosticsLevel | Variable | DecryptionErrors |
| i=20020 | DiagnosticsLevel | Variable | MessageSequenceNumber |
| i=20022 | DiagnosticsLevel | Variable | StatusCode |
| i=20024 | DiagnosticsLevel | Variable | MajorVersion |
| i=20026 | DiagnosticsLevel | Variable | MinorVersion |
| i=20079 | Active | Variable | DecryptionErrors |
| i=20080 | Classification | Variable | DecryptionErrors |
| i=20081 | DiagnosticsLevel | Variable | DecryptionErrors |
| i=20084 | DiagnosticsLevel | Variable | MessageSequenceNumber |
| i=20086 | DiagnosticsLevel | Variable | StatusCode |
| i=20088 | DiagnosticsLevel | Variable | MajorVersion |
| i=20090 | DiagnosticsLevel | Variable | MinorVersion |
| i=20092 | DiagnosticsLevel | Variable | SecurityTokenID |
| i=20094 | DiagnosticsLevel | Variable | TimeToNextTokenID |
| i=25279 | InputArguments | Variable | AddPushTarget |
| i=25280 | OutputArguments | Variable | AddPushTarget |
| i=25282 | InputArguments | Variable | RemovePushTarget |
| i=25367 | InputArguments | Variable | AddPushTarget |
| i=25368 | OutputArguments | Variable | AddPushTarget |
| i=25370 | InputArguments | Variable | RemovePushTarget |
| i=25642 | InputArguments | Variable | ConnectSecurityGroups |
| i=25643 | OutputArguments | Variable | ConnectSecurityGroups |
| i=25645 | InputArguments | Variable | DisconnectSecurityGroups |
| i=25646 | OutputArguments | Variable | DisconnectSecurityGroups |

## ModellingRule=None method (18)

| NodeId | BrowseName | NodeClass | Parent |
|--------|-----------|-----------|--------|
| i=14095 | Open | Method | TrustList |
| i=14098 | Close | Method | TrustList |
| i=14100 | Read | Method | TrustList |
| i=14103 | Write | Method | TrustList |
| i=14105 | GetPosition | Method | TrustList |
| i=14108 | SetPosition | Method | TrustList |
| i=14111 | OpenWithMasks | Method | TrustList |
| i=14114 | CloseAndUpdate | Method | TrustList |
| i=14117 | AddCertificate | Method | TrustList |
| i=14119 | RemoveCertificate | Method | TrustList |
| i=15444 | AddSecurityGroup | Method | SecurityGroups |
| i=15447 | RemoveSecurityGroup | Method | SecurityGroups |
| i=16348 | CreateDirectory | Method | FileSystem |
| i=16351 | CreateFile | Method | FileSystem |
| i=16354 | Delete | Method | FileSystem |
| i=16356 | MoveOrCopy | Method | FileSystem |
| i=25441 | AddPushTarget | Method | KeyPushTargets |
| i=25444 | RemovePushTarget | Method | KeyPushTargets |

## ModellingRule=None object (5)

| NodeId | BrowseName | NodeClass | Parent |
|--------|-----------|-----------|--------|
| i=11202 | HA Configuration | Object |  |
| i=11203 | AggregateConfiguration | Object | HA Configuration |
| i=14088 | DefaultHttpsGroup | Object | CertificateGroups |
| i=14089 | TrustList | Object | DefaultHttpsGroup |
| i=16314 | FileSystem | Object |  |

## ModellingRule=None variable (94)

| NodeId | BrowseName | NodeClass | Parent |
|--------|-----------|-----------|--------|
| i=2289 | SamplingIntervalDiagnosticsArray | Variable | ServerDiagnostics |
| i=3067 | Icon | Variable |  |
| i=3068 | NodeVersion | Variable |  |
| i=3069 | LocalTime | Variable |  |
| i=3070 | AllowNulls | Variable |  |
| i=3071 | EnumValues | Variable |  |
| i=3072 | InputArguments | Variable |  |
| i=3073 | OutputArguments | Variable |  |
| i=9018 | TrueState | Variable | EnabledState |
| i=9019 | FalseState | Variable | EnabledState |
| i=9062 | TrueState | Variable | DialogState |
| i=9063 | FalseState | Variable | DialogState |
| i=9100 | TrueState | Variable | AckedState |
| i=9101 | FalseState | Variable | AckedState |
| i=9109 | TrueState | Variable | ConfirmedState |
| i=9110 | FalseState | Variable | ConfirmedState |
| i=9167 | TrueState | Variable | ActiveState |
| i=9168 | FalseState | Variable | ActiveState |
| i=9176 | TrueState | Variable | SuppressedState |
| i=9177 | FalseState | Variable | SuppressedState |
| i=10027 | TrueState | Variable | HighHighState |
| i=10028 | FalseState | Variable | HighHighState |
| i=10036 | TrueState | Variable | HighState |
| i=10037 | FalseState | Variable | HighState |
| i=10045 | TrueState | Variable | LowState |
| i=10046 | FalseState | Variable | LowState |
| i=10054 | TrueState | Variable | LowLowState |
| i=10055 | FalseState | Variable | LowLowState |
| i=11204 | TreatUncertainAsBad | Variable | AggregateConfiguration |
| i=11205 | PercentDataBad | Variable | AggregateConfiguration |
| i=11206 | PercentDataGood | Variable | AggregateConfiguration |
| i=11207 | UseSlopedExtrapolation | Variable | AggregateConfiguration |
| i=11208 | Stepped | Variable | HA Configuration |
| i=11214 | Annotations | Variable |  |
| i=11215 | HistoricalEventFilter | Variable |  |
| i=11312 | CurrentServerId | Variable |  |
| i=11314 | ServerUriArray | Variable |  |
| i=11432 | EnumStrings | Variable |  |
| i=11433 | ValueAsText | Variable |  |
| i=11498 | MaxStringLength | Variable |  |
| ... | *54 more* | | |

## Optional (filtered out) (36)

| NodeId | BrowseName | NodeClass | Parent |
|--------|-----------|-----------|--------|
| i=9015 | EffectiveDisplayName | Variable | EnabledState |
| i=9016 | TransitionTime | Variable | EnabledState |
| i=9017 | EffectiveTransitionTime | Variable | EnabledState |
| i=9060 | TransitionTime | Variable | DialogState |
| i=9098 | TransitionTime | Variable | AckedState |
| i=9107 | TransitionTime | Variable | ConfirmedState |
| i=9164 | EffectiveDisplayName | Variable | ActiveState |
| i=9165 | TransitionTime | Variable | ActiveState |
| i=9166 | EffectiveTransitionTime | Variable | ActiveState |
| i=9174 | TransitionTime | Variable | SuppressedState |
| i=9184 | LastTransition | Variable | ShelvingState |
| i=9188 | TransitionTime | Variable | LastTransition |
| i=9461 | LastTransition | Variable | LimitState |
| i=9465 | TransitionTime | Variable | LastTransition |
| i=10025 | TransitionTime | Variable | HighHighState |
| i=10034 | TransitionTime | Variable | HighState |
| i=10043 | TransitionTime | Variable | LowState |
| i=10052 | TransitionTime | Variable | LowLowState |
| i=16376 | TransitionTime | Variable | OutOfServiceState |
| i=16385 | TransitionTime | Variable | SilenceState |
| i=18195 | TransitionTime | Variable | LatchedState |
| i=19899 | SecurityTokenID | Variable | LiveValues |
| i=19901 | TimeToNextTokenID | Variable | LiveValues |
| i=19954 | ReceivedInvalidNetworkMessages | Variable | Counters |
| i=19959 | DecryptionErrors | Variable | Counters |
| i=20019 | MessageSequenceNumber | Variable | LiveValues |
| i=20021 | StatusCode | Variable | LiveValues |
| i=20023 | MajorVersion | Variable | LiveValues |
| i=20025 | MinorVersion | Variable | LiveValues |
| i=20078 | DecryptionErrors | Variable | Counters |
| i=20083 | MessageSequenceNumber | Variable | LiveValues |
| i=20085 | StatusCode | Variable | LiveValues |
| i=20087 | MajorVersion | Variable | LiveValues |
| i=20089 | MinorVersion | Variable | LiveValues |
| i=20091 | SecurityTokenID | Variable | LiveValues |
| i=20093 | TimeToNextTokenID | Variable | LiveValues |
