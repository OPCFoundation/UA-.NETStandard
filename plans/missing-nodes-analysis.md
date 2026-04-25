# OPC UA Missing Nodes Analysis — Post Source Generator Fixes

**Date:** 2026-04-25 20:59:04
**Server:** `opc.tcp://localhost:48010/Quickstarts/ReferenceServer`
**Spec:** `Opc.Ua.NodeSet2.xml` v1.05.07 (2026-04-15)
**Method:** Export server address space via MCP ExportNodeSet, compare ns=0 node IDs

## Connection Details

| Session | SecurityMode | Auth | Result |
|---------|-------------|------|--------|
| Anonymous | None | Anonymous | Connected |
| Admin | None | Username (sysadmin/demo) | Connected |

## Summary

| Metric | Anonymous | Admin (sysadmin) |
|--------|-----------|------------------|
| Spec ns=0 nodes | 5,283 | 5,283 |
| Server ns=0 nodes | 5,908 | 6,006 |
| Missing from server | 1,325 | 1,233 |
| Extra on server | 1,950 | 1,956 |
| Filtered (expected missing) | 509 | 439 |
| **Truly missing** | **816** | **794** |
| Admin-only visible nodes | - | 98 |

## Comparison with Previous Results

| Metric | Previous | Current | Delta |
|--------|----------|---------|-------|
| Raw Missing (Anonymous) | 1,155 | 1,325 | +170 |
| Raw Missing (Admin) | 982 | 1,233 | +251 |

> **Note:** The raw missing count increased because the reference `Opc.Ua.NodeSet2.xml`
> was updated to v1.05.07 which contains additional nodes compared to the version used
> in the previous analysis. The previous analysis also did not distinguish between
> "truly missing" (after filtering) and "expected missing" (optional/placeholder nodes).

## Filtered Categories (Expected Missing)

These nodes are **correctly absent** from the server and should be filtered:

| Filter Category | Anonymous | Admin |
|-----------------|-----------|-------|
| Ancestor: Optional/Placeholder | 362 | 314 |
| Direct: MandatoryPlaceholder | 2 | 2 |
| Direct: Optional | 119 | 97 |
| Direct: OptionalPlaceholder | 8 | 8 |
| SKS Admin Role required | 18 | 18 |
| **Total Filtered** | **509** | **439** |

## Truly Missing by NodeClass

| NodeClass | Anonymous | Admin |
|-----------|-----------|-------|
| Object | 390 | 390 |
| Variable | 302 | 289 |
| Method | 118 | 109 |
| ObjectType | 6 | 6 |
| **Total** | **816** | **794** |

## Truly Missing by Subcategory (Admin)

| Subcategory | Count | Description |
|-------------|-------|-------------|
| encoding_nodes | 384 | Default Binary/XML/JSON encoding objects for DataTypes (no parent in XML) |
| role_instances | 172 | WellKnownRole object children (Identities, Applications, methods) |
| other_missing | 84 | Other missing nodes not fitting above categories |
| trust_list_and_cert | 36 | TrustList objects and TrustListType children |
| pubsub_and_sks | 31 | PubSub, SecurityKeyServer types and instances |
| session_and_server_diag | 30 | ServerDiagnostics and SessionDiagnostics variables |
| orphaned_type_children | 25 | Type-level properties with no parent ref (e.g. NodeVersion, Icon) |
| user_management | 10 | UserManagement type and instance nodes |
| server_config_and_certs | 8 | ServerConfigurationType and CertificateGroupType children |
| filesystem | 5 | FileSystem object and methods |
| ha_aggregate_config | 5 | HA AggregateConfiguration properties |
| program_state | 4 | ProgramStateMachineType optional properties |
| **Total** | **794** | |

### Encoding Nodes (384 nodes)

| NodeId | BrowseName | NodeClass | MR | Parent |
|--------|------------|-----------|-----|--------|
| i=11218 | Default XML | Object | - | - |
| i=11226 | Default Binary | Object | - | - |
| i=11949 | Default XML | Object | - | - |
| i=11950 | Default XML | Object | - | - |
| i=11957 | Default Binary | Object | - | - |
| i=11958 | Default Binary | Object | - | - |
| i=12081 | Default XML | Object | - | - |
| i=12082 | Default XML | Object | - | - |
| i=12089 | Default Binary | Object | - | - |
| i=12090 | Default Binary | Object | - | - |
| i=121 | Default Binary | Object | - | - |
| i=12173 | Default XML | Object | - | - |
| i=12174 | Default XML | Object | - | - |
| i=12181 | Default Binary | Object | - | - |
| i=12182 | Default Binary | Object | - | - |
| i=12195 | Default XML | Object | - | - |
| i=122 | Default Binary | Object | - | - |
| i=12207 | Default Binary | Object | - | - |
| i=123 | Default Binary | Object | - | - |
| i=124 | Default Binary | Object | - | - |
| i=125 | Default Binary | Object | - | - |
| i=126 | Default Binary | Object | - | - |
| i=12676 | Default XML | Object | - | - |
| i=12680 | Default Binary | Object | - | - |
| i=127 | Default Binary | Object | - | - |
| i=12757 | Default XML | Object | - | - |
| i=12758 | Default XML | Object | - | - |
| i=12765 | Default Binary | Object | - | - |
| i=12766 | Default Binary | Object | - | - |
| i=128 | Default Binary | Object | - | - |
| i=12892 | Default XML | Object | - | - |
| i=12893 | Default XML | Object | - | - |
| i=12900 | Default Binary | Object | - | - |
| i=12901 | Default Binary | Object | - | - |
| i=14319 | Default XML | Object | - | - |
| i=14323 | Default Binary | Object | - | - |
| i=14794 | Default XML | Object | - | - |
| i=14795 | Default XML | Object | - | - |
| i=14796 | Default XML | Object | - | - |
| i=14797 | Default XML | Object | - | - |
| i=14798 | Default XML | Object | - | - |
| i=14799 | Default XML | Object | - | - |
| i=14800 | Default XML | Object | - | - |
| i=14801 | Default XML | Object | - | - |
| i=14802 | Default XML | Object | - | - |
| i=14803 | Default XML | Object | - | - |
| i=14804 | Default XML | Object | - | - |
| i=14839 | Default Binary | Object | - | - |
| i=14844 | Default Binary | Object | - | - |
| i=14845 | Default Binary | Object | - | - |
| i=14846 | Default Binary | Object | - | - |
| i=14847 | Default Binary | Object | - | - |
| i=14848 | Default Binary | Object | - | - |
| i=15421 | Default Binary | Object | - | - |
| i=15422 | Default Binary | Object | - | - |
| i=15479 | Default Binary | Object | - | - |
| i=15529 | Default XML | Object | - | - |
| i=15531 | Default XML | Object | - | - |
| i=15579 | Default XML | Object | - | - |
| i=15589 | Default XML | Object | - | - |
| i=15590 | Default XML | Object | - | - |
| i=15671 | Default Binary | Object | - | - |
| i=15676 | Default Binary | Object | - | - |
| i=15677 | Default Binary | Object | - | - |
| i=15678 | Default Binary | Object | - | - |
| i=15679 | Default Binary | Object | - | - |
| i=15681 | Default Binary | Object | - | - |
| i=15682 | Default Binary | Object | - | - |
| i=15683 | Default Binary | Object | - | - |
| i=15688 | Default Binary | Object | - | - |
| i=15689 | Default Binary | Object | - | - |
| i=15691 | Default Binary | Object | - | - |
| i=15693 | Default Binary | Object | - | - |
| i=15694 | Default Binary | Object | - | - |
| i=15695 | Default Binary | Object | - | - |
| i=15701 | Default Binary | Object | - | - |
| i=15702 | Default Binary | Object | - | - |
| i=15703 | Default Binary | Object | - | - |
| i=15705 | Default Binary | Object | - | - |
| i=15706 | Default Binary | Object | - | - |
| i=15707 | Default Binary | Object | - | - |
| i=15712 | Default Binary | Object | - | - |
| i=15713 | Default Binary | Object | - | - |
| i=15715 | Default Binary | Object | - | - |
| i=15717 | Default Binary | Object | - | - |
| i=15718 | Default Binary | Object | - | - |
| i=15719 | Default Binary | Object | - | - |
| i=15724 | Default Binary | Object | - | - |
| i=15725 | Default Binary | Object | - | - |
| i=15727 | Default Binary | Object | - | - |
| i=15728 | Default XML | Object | - | - |
| i=15729 | Default Binary | Object | - | - |
| i=15733 | Default Binary | Object | - | - |
| i=15736 | Default Binary | Object | - | - |
| i=15949 | Default XML | Object | - | - |
| i=15950 | Default XML | Object | - | - |
| i=15951 | Default XML | Object | - | - |
| i=15952 | Default XML | Object | - | - |
| i=15953 | Default XML | Object | - | - |
| i=15954 | Default XML | Object | - | - |
| i=15955 | Default XML | Object | - | - |
| i=15956 | Default XML | Object | - | - |
| i=15987 | Default XML | Object | - | - |
| i=15988 | Default XML | Object | - | - |
| i=15990 | Default XML | Object | - | - |
| i=15991 | Default XML | Object | - | - |
| i=15992 | Default XML | Object | - | - |
| i=15993 | Default XML | Object | - | - |
| i=15995 | Default XML | Object | - | - |
| i=15996 | Default XML | Object | - | - |
| i=16007 | Default XML | Object | - | - |
| i=16008 | Default XML | Object | - | - |
| i=16009 | Default XML | Object | - | - |
| i=16010 | Default XML | Object | - | - |
| i=16011 | Default XML | Object | - | - |
| i=16012 | Default XML | Object | - | - |
| i=16014 | Default XML | Object | - | - |
| i=16015 | Default XML | Object | - | - |
| i=16016 | Default XML | Object | - | - |
| i=16017 | Default XML | Object | - | - |
| i=16018 | Default XML | Object | - | - |
| i=16019 | Default XML | Object | - | - |
| i=16021 | Default XML | Object | - | - |
| i=16022 | Default XML | Object | - | - |
| i=16023 | Default XML | Object | - | - |
| i=16126 | Default XML | Object | - | - |
| i=16538 | Default Binary | Object | - | - |
| i=16539 | Default Binary | Object | - | - |
| i=16540 | Default Binary | Object | - | - |
| i=16541 | Default Binary | Object | - | - |
| i=16543 | Default Binary | Object | - | - |
| i=16544 | Default Binary | Object | - | - |
| i=16545 | Default Binary | Object | - | - |
| i=16546 | Default Binary | Object | - | - |
| i=16547 | Default Binary | Object | - | - |
| i=16587 | Default XML | Object | - | - |
| i=16588 | Default XML | Object | - | - |
| i=16589 | Default XML | Object | - | - |
| i=16590 | Default XML | Object | - | - |
| i=16592 | Default XML | Object | - | - |
| i=16593 | Default XML | Object | - | - |
| i=16594 | Default XML | Object | - | - |
| i=16595 | Default XML | Object | - | - |
| i=16596 | Default XML | Object | - | - |
| i=17468 | Default Binary | Object | - | - |
| i=17472 | Default XML | Object | - | - |
| i=17537 | Default Binary | Object | - | - |
| i=17541 | Default XML | Object | - | - |
| i=17549 | Default Binary | Object | - | - |
| i=17553 | Default XML | Object | - | - |
| i=18598 | Default Binary | Object | - | - |
| i=18599 | Default Binary | Object | - | - |
| i=18600 | Default Binary | Object | - | - |
| i=18610 | Default XML | Object | - | - |
| i=18611 | Default XML | Object | - | - |
| i=18612 | Default XML | Object | - | - |
| i=18795 | Default Binary | Object | - | - |
| i=18815 | Default Binary | Object | - | - |
| i=18816 | Default Binary | Object | - | - |
| i=18817 | Default Binary | Object | - | - |
| i=18818 | Default Binary | Object | - | - |
| i=18819 | Default Binary | Object | - | - |
| i=18820 | Default Binary | Object | - | - |
| i=18821 | Default Binary | Object | - | - |
| i=18822 | Default Binary | Object | - | - |
| i=18823 | Default Binary | Object | - | - |
| i=18851 | Default XML | Object | - | - |
| i=18852 | Default XML | Object | - | - |
| i=18853 | Default XML | Object | - | - |
| i=18854 | Default XML | Object | - | - |
| i=18855 | Default XML | Object | - | - |
| i=18856 | Default XML | Object | - | - |
| i=18857 | Default XML | Object | - | - |
| i=18858 | Default XML | Object | - | - |
| i=18859 | Default XML | Object | - | - |
| i=18930 | Default Binary | Object | - | - |
| i=18937 | Default XML | Object | - | - |
| i=18938 | Default XML | Object | - | - |
| i=19079 | Default Binary | Object | - | - |
| i=19080 | Default Binary | Object | - | - |
| i=19081 | Default Binary | Object | - | - |
| i=19100 | Default XML | Object | - | - |
| i=19101 | Default XML | Object | - | - |
| i=19102 | Default XML | Object | - | - |
| i=19379 | Default Binary | Object | - | - |
| i=19383 | Default XML | Object | - | - |
| i=19753 | Default Binary | Object | - | - |
| i=19754 | Default Binary | Object | - | - |
| i=19755 | Default Binary | Object | - | - |
| i=19756 | Default Binary | Object | - | - |
| i=19773 | Default XML | Object | - | - |
| i=19774 | Default XML | Object | - | - |
| i=19775 | Default XML | Object | - | - |
| i=19776 | Default XML | Object | - | - |
| i=21150 | Default Binary | Object | - | - |
| i=21151 | Default Binary | Object | - | - |
| i=21152 | Default Binary | Object | - | - |
| i=21153 | Default Binary | Object | - | - |
| i=21154 | Default Binary | Object | - | - |
| i=21155 | Default Binary | Object | - | - |
| i=21174 | Default XML | Object | - | - |
| i=21175 | Default XML | Object | - | - |
| i=21176 | Default XML | Object | - | - |
| i=21177 | Default XML | Object | - | - |
| i=21178 | Default XML | Object | - | - |
| i=21179 | Default XML | Object | - | - |
| i=23499 | Default Binary | Object | - | - |
| i=23505 | Default XML | Object | - | - |
| i=23507 | Default Binary | Object | - | - |
| i=23520 | Default XML | Object | - | - |
| i=23725 | Default Binary | Object | - | - |
| i=23735 | Default XML | Object | - | - |
| i=23754 | Default Binary | Object | - | - |
| i=23755 | Default Binary | Object | - | - |
| i=23762 | Default XML | Object | - | - |
| i=23763 | Default XML | Object | - | - |
| i=23851 | Default Binary | Object | - | - |
| i=23852 | Default Binary | Object | - | - |
| i=23853 | Default Binary | Object | - | - |
| i=23854 | Default Binary | Object | - | - |
| i=23855 | Default Binary | Object | - | - |
| i=23856 | Default Binary | Object | - | - |
| i=23857 | Default Binary | Object | - | - |
| i=23860 | Default Binary | Object | - | - |
| i=23861 | Default Binary | Object | - | - |
| i=23864 | Default Binary | Object | - | - |
| i=23865 | Default Binary | Object | - | - |
| i=23866 | Default Binary | Object | - | - |
| i=23919 | Default XML | Object | - | - |
| i=23920 | Default XML | Object | - | - |
| i=23921 | Default XML | Object | - | - |
| i=23922 | Default XML | Object | - | - |
| i=23923 | Default XML | Object | - | - |
| i=23924 | Default XML | Object | - | - |
| i=23925 | Default XML | Object | - | - |
| i=23928 | Default XML | Object | - | - |
| i=23929 | Default XML | Object | - | - |
| i=23932 | Default XML | Object | - | - |
| i=23933 | Default XML | Object | - | - |
| i=23934 | Default XML | Object | - | - |
| i=24034 | Default Binary | Object | - | - |
| i=24038 | Default XML | Object | - | - |
| i=24108 | Default Binary | Object | - | - |
| i=24109 | Default Binary | Object | - | - |
| i=24110 | Default Binary | Object | - | - |
| i=24120 | Default XML | Object | - | - |
| i=24121 | Default XML | Object | - | - |
| i=24122 | Default XML | Object | - | - |
| i=24250 | Default Binary | Object | - | - |
| i=24262 | Default Binary | Object | - | - |
| i=24292 | Default Binary | Object | - | - |
| i=24296 | Default XML | Object | - | - |
| i=24338 | Default Binary | Object | - | - |
| i=24339 | Default Binary | Object | - | - |
| i=24352 | Default XML | Object | - | - |
| i=24353 | Default XML | Object | - | - |
| i=24354 | Default XML | Object | - | - |
| i=24355 | Default XML | Object | - | - |
| i=25239 | Default Binary | Object | - | - |
| i=25243 | Default XML | Object | - | - |
| i=25529 | Default Binary | Object | - | - |
| i=25530 | Default Binary | Object | - | - |
| i=25531 | Default Binary | Object | - | - |
| i=25532 | Default Binary | Object | - | - |
| i=25545 | Default XML | Object | - | - |
| i=25546 | Default XML | Object | - | - |
| i=25547 | Default XML | Object | - | - |
| i=25548 | Default XML | Object | - | - |
| i=297 | Default XML | Object | - | - |
| i=298 | Default Binary | Object | - | - |
| i=300 | Default XML | Object | - | - |
| i=301 | Default Binary | Object | - | - |
| i=305 | Default XML | Object | - | - |
| i=306 | Default Binary | Object | - | - |
| i=3062 | Default Binary | Object | - | - |
| i=3063 | Default XML | Object | - | - |
| i=309 | Default XML | Object | - | - |
| i=310 | Default Binary | Object | - | - |
| i=313 | Default XML | Object | - | - |
| i=314 | Default Binary | Object | - | - |
| i=317 | Default XML | Object | - | - |
| i=318 | Default Binary | Object | - | - |
| i=320 | Default XML | Object | - | - |
| i=321 | Default Binary | Object | - | - |
| i=323 | Default XML | Object | - | - |
| i=32382 | Default Binary | Object | - | - |
| i=32386 | Default XML | Object | - | - |
| i=324 | Default Binary | Object | - | - |
| i=32422 | Default Binary | Object | - | - |
| i=32426 | Default XML | Object | - | - |
| i=32560 | Default Binary | Object | - | - |
| i=32561 | Default Binary | Object | - | - |
| i=32562 | Default Binary | Object | - | - |
| i=32572 | Default XML | Object | - | - |
| i=32573 | Default XML | Object | - | - |
| i=32574 | Default XML | Object | - | - |
| i=326 | Default XML | Object | - | - |
| i=32661 | Default Binary | Object | - | - |
| i=32662 | Default Binary | Object | - | - |
| i=32669 | Default XML | Object | - | - |
| i=32670 | Default XML | Object | - | - |
| i=327 | Default Binary | Object | - | - |
| i=32825 | Default Binary | Object | - | - |
| i=32829 | Default XML | Object | - | - |
| i=332 | Default XML | Object | - | - |
| i=333 | Default Binary | Object | - | - |
| i=339 | Default XML | Object | - | - |
| i=340 | Default Binary | Object | - | - |
| i=345 | Default XML | Object | - | - |
| i=346 | Default Binary | Object | - | - |
| i=377 | Default XML | Object | - | - |
| i=378 | Default Binary | Object | - | - |
| i=380 | Default XML | Object | - | - |
| i=381 | Default Binary | Object | - | - |
| i=383 | Default XML | Object | - | - |
| i=384 | Default Binary | Object | - | - |
| i=386 | Default XML | Object | - | - |
| i=387 | Default Binary | Object | - | - |
| i=433 | Default XML | Object | - | - |
| i=434 | Default Binary | Object | - | - |
| i=457 | Default XML | Object | - | - |
| i=458 | Default Binary | Object | - | - |
| i=538 | Default XML | Object | - | - |
| i=539 | Default Binary | Object | - | - |
| i=541 | Default XML | Object | - | - |
| i=542 | Default Binary | Object | - | - |
| i=584 | Default XML | Object | - | - |
| i=585 | Default Binary | Object | - | - |
| i=587 | Default XML | Object | - | - |
| i=588 | Default Binary | Object | - | - |
| i=590 | Default XML | Object | - | - |
| i=591 | Default Binary | Object | - | - |
| i=593 | Default XML | Object | - | - |
| i=594 | Default Binary | Object | - | - |
| i=596 | Default XML | Object | - | - |
| i=597 | Default Binary | Object | - | - |
| i=599 | Default XML | Object | - | - |
| i=600 | Default Binary | Object | - | - |
| i=602 | Default XML | Object | - | - |
| i=603 | Default Binary | Object | - | - |
| i=660 | Default XML | Object | - | - |
| i=661 | Default Binary | Object | - | - |
| i=720 | Default XML | Object | - | - |
| i=721 | Default Binary | Object | - | - |
| i=726 | Default XML | Object | - | - |
| i=727 | Default Binary | Object | - | - |
| i=7616 | Default XML | Object | - | - |
| i=8251 | Default Binary | Object | - | - |
| i=854 | Default XML | Object | - | - |
| i=855 | Default Binary | Object | - | - |
| i=857 | Default XML | Object | - | - |
| i=858 | Default Binary | Object | - | - |
| i=860 | Default XML | Object | - | - |
| i=861 | Default Binary | Object | - | - |
| i=863 | Default XML | Object | - | - |
| i=864 | Default Binary | Object | - | - |
| i=866 | Default XML | Object | - | - |
| i=867 | Default Binary | Object | - | - |
| i=869 | Default XML | Object | - | - |
| i=870 | Default Binary | Object | - | - |
| i=872 | Default XML | Object | - | - |
| i=873 | Default Binary | Object | - | - |
| i=875 | Default XML | Object | - | - |
| i=876 | Default Binary | Object | - | - |
| i=878 | Default XML | Object | - | - |
| i=879 | Default Binary | Object | - | - |
| i=885 | Default XML | Object | - | - |
| i=886 | Default Binary | Object | - | - |
| i=888 | Default XML | Object | - | - |
| i=889 | Default Binary | Object | - | - |
| i=8913 | Default XML | Object | - | - |
| i=8917 | Default Binary | Object | - | - |
| i=892 | Default XML | Object | - | - |
| i=893 | Default Binary | Object | - | - |
| i=895 | Default XML | Object | - | - |
| i=896 | Default Binary | Object | - | - |
| i=898 | Default XML | Object | - | - |
| i=899 | Default Binary | Object | - | - |
| i=921 | Default XML | Object | - | - |
| i=922 | Default Binary | Object | - | - |
| i=939 | Default XML | Object | - | - |
| i=940 | Default Binary | Object | - | - |
| i=949 | Default XML | Object | - | - |
| i=950 | Default Binary | Object | - | - |

### Role Instances (172 nodes)

| NodeId | BrowseName | NodeClass | MR | Parent |
|--------|------------|-----------|-----|--------|
| i=15416 | ApplicationsExclude | Variable | - | Observer |
| i=15417 | EndpointsExclude | Variable | - | Observer |
| i=15418 | ApplicationsExclude | Variable | - | Operator |
| i=15423 | EndpointsExclude | Variable | - | Operator |
| i=15424 | ApplicationsExclude | Variable | - | Engineer |
| i=15425 | EndpointsExclude | Variable | - | Engineer |
| i=15426 | ApplicationsExclude | Variable | - | Supervisor |
| i=15427 | EndpointsExclude | Variable | - | Supervisor |
| i=15428 | ApplicationsExclude | Variable | - | ConfigureAdmin |
| i=15429 | EndpointsExclude | Variable | - | ConfigureAdmin |
| i=15430 | ApplicationsExclude | Variable | - | SecurityAdmin |
| i=15527 | EndpointsExclude | Variable | - | SecurityAdmin |
| i=15672 | AddIdentity | Method | - | Observer |
| i=15673 | InputArguments | Variable | - | AddIdentity |
| i=15674 | RemoveIdentity | Method | - | Observer |
| i=15675 | InputArguments | Variable | - | RemoveIdentity |
| i=15684 | AddIdentity | Method | - | Operator |
| i=15685 | InputArguments | Variable | - | AddIdentity |
| i=15686 | RemoveIdentity | Method | - | Operator |
| i=15687 | InputArguments | Variable | - | RemoveIdentity |
| i=15696 | AddIdentity | Method | - | Supervisor |
| i=15697 | InputArguments | Variable | - | AddIdentity |
| i=15698 | RemoveIdentity | Method | - | Supervisor |
| i=15699 | InputArguments | Variable | - | RemoveIdentity |
| i=15708 | AddIdentity | Method | - | SecurityAdmin |
| i=15709 | InputArguments | Variable | - | AddIdentity |
| i=15710 | RemoveIdentity | Method | - | SecurityAdmin |
| i=15711 | InputArguments | Variable | - | RemoveIdentity |
| i=15720 | AddIdentity | Method | - | ConfigureAdmin |
| i=15721 | InputArguments | Variable | - | AddIdentity |
| i=15722 | RemoveIdentity | Method | - | ConfigureAdmin |
| i=15723 | InputArguments | Variable | - | RemoveIdentity |
| i=15997 | AddRole | Method | Mandatory | RoleSetType |
| i=15998 | InputArguments | Variable | Mandatory | AddRole |
| i=15999 | OutputArguments | Variable | Mandatory | AddRole |
| i=16000 | RemoveRole | Method | Mandatory | RoleSetType |
| i=16001 | InputArguments | Variable | Mandatory | RemoveRole |
| i=16041 | AddIdentity | Method | - | Engineer |
| i=16042 | InputArguments | Variable | - | AddIdentity |
| i=16043 | RemoveIdentity | Method | - | Engineer |
| i=16044 | InputArguments | Variable | - | RemoveIdentity |
| i=16214 | Identities | Variable | - | Observer |
| i=16215 | Applications | Variable | - | Observer |
| i=16216 | Endpoints | Variable | - | Observer |
| i=16217 | AddApplication | Method | - | Observer |
| i=16218 | InputArguments | Variable | - | AddApplication |
| i=16219 | RemoveApplication | Method | - | Observer |
| i=16220 | InputArguments | Variable | - | RemoveApplication |
| i=16221 | AddEndpoint | Method | - | Observer |
| i=16222 | InputArguments | Variable | - | AddEndpoint |
| i=16223 | RemoveEndpoint | Method | - | Observer |
| i=16224 | InputArguments | Variable | - | RemoveEndpoint |
| i=16225 | Identities | Variable | - | Operator |
| i=16226 | Applications | Variable | - | Operator |
| i=16227 | Endpoints | Variable | - | Operator |
| i=16228 | AddApplication | Method | - | Operator |
| i=16229 | InputArguments | Variable | - | AddApplication |
| i=16230 | RemoveApplication | Method | - | Operator |
| i=16231 | InputArguments | Variable | - | RemoveApplication |
| i=16232 | AddEndpoint | Method | - | Operator |
| i=16233 | InputArguments | Variable | - | AddEndpoint |
| i=16234 | RemoveEndpoint | Method | - | Operator |
| i=16235 | InputArguments | Variable | - | RemoveEndpoint |
| i=16236 | Identities | Variable | - | Engineer |
| i=16237 | Applications | Variable | - | Engineer |
| i=16238 | Endpoints | Variable | - | Engineer |
| i=16239 | AddApplication | Method | - | Engineer |
| i=16240 | InputArguments | Variable | - | AddApplication |
| i=16241 | RemoveApplication | Method | - | Engineer |
| i=16242 | InputArguments | Variable | - | RemoveApplication |
| i=16243 | AddEndpoint | Method | - | Engineer |
| i=16244 | InputArguments | Variable | - | AddEndpoint |
| i=16245 | RemoveEndpoint | Method | - | Engineer |
| i=16246 | InputArguments | Variable | - | RemoveEndpoint |
| i=16247 | Identities | Variable | - | Supervisor |
| i=16248 | Applications | Variable | - | Supervisor |
| i=16249 | Endpoints | Variable | - | Supervisor |
| i=16250 | AddApplication | Method | - | Supervisor |
| i=16251 | InputArguments | Variable | - | AddApplication |
| i=16252 | RemoveApplication | Method | - | Supervisor |
| i=16253 | InputArguments | Variable | - | RemoveApplication |
| i=16254 | AddEndpoint | Method | - | Supervisor |
| i=16255 | InputArguments | Variable | - | AddEndpoint |
| i=16256 | RemoveEndpoint | Method | - | Supervisor |
| i=16257 | InputArguments | Variable | - | RemoveEndpoint |
| i=16258 | Identities | Variable | - | SecurityAdmin |
| i=16259 | Applications | Variable | - | SecurityAdmin |
| i=16260 | Endpoints | Variable | - | SecurityAdmin |
| i=16261 | AddApplication | Method | - | SecurityAdmin |
| i=16262 | InputArguments | Variable | - | AddApplication |
| i=16263 | RemoveApplication | Method | - | SecurityAdmin |
| i=16264 | InputArguments | Variable | - | RemoveApplication |
| i=16265 | AddEndpoint | Method | - | SecurityAdmin |
| i=16266 | InputArguments | Variable | - | AddEndpoint |
| i=16267 | RemoveEndpoint | Method | - | SecurityAdmin |
| i=16268 | InputArguments | Variable | - | RemoveEndpoint |
| i=16269 | Identities | Variable | - | ConfigureAdmin |
| i=16270 | Applications | Variable | - | ConfigureAdmin |
| i=16271 | Endpoints | Variable | - | ConfigureAdmin |
| i=16272 | AddApplication | Method | - | ConfigureAdmin |
| i=16273 | InputArguments | Variable | - | AddApplication |
| i=16274 | RemoveApplication | Method | - | ConfigureAdmin |
| i=16275 | InputArguments | Variable | - | RemoveApplication |
| i=16276 | AddEndpoint | Method | - | ConfigureAdmin |
| i=16277 | InputArguments | Variable | - | AddEndpoint |
| i=16278 | RemoveEndpoint | Method | - | ConfigureAdmin |
| i=16279 | InputArguments | Variable | - | RemoveEndpoint |
| i=16301 | AddRole | Method | - | RoleSet |
| i=16302 | InputArguments | Variable | - | AddRole |
| i=16303 | OutputArguments | Variable | - | AddRole |
| i=16304 | RemoveRole | Method | - | RoleSet |
| i=16305 | InputArguments | Variable | - | RemoveRole |
| i=24142 | CustomConfiguration | Variable | - | Observer |
| i=24143 | CustomConfiguration | Variable | - | Operator |
| i=24144 | CustomConfiguration | Variable | - | Engineer |
| i=24145 | CustomConfiguration | Variable | - | Supervisor |
| i=24146 | CustomConfiguration | Variable | - | ConfigureAdmin |
| i=24147 | CustomConfiguration | Variable | - | SecurityAdmin |
| i=25566 | Identities | Variable | - | SecurityKeyServerAdmin |
| i=25567 | ApplicationsExclude | Variable | - | SecurityKeyServerAdmin |
| i=25568 | Applications | Variable | - | SecurityKeyServerAdmin |
| i=25569 | EndpointsExclude | Variable | - | SecurityKeyServerAdmin |
| i=25570 | Endpoints | Variable | - | SecurityKeyServerAdmin |
| i=25571 | CustomConfiguration | Variable | - | SecurityKeyServerAdmin |
| i=25572 | AddIdentity | Method | - | SecurityKeyServerAdmin |
| i=25573 | InputArguments | Variable | - | AddIdentity |
| i=25574 | RemoveIdentity | Method | - | SecurityKeyServerAdmin |
| i=25575 | InputArguments | Variable | - | RemoveIdentity |
| i=25576 | AddApplication | Method | - | SecurityKeyServerAdmin |
| i=25577 | InputArguments | Variable | - | AddApplication |
| i=25578 | RemoveApplication | Method | - | SecurityKeyServerAdmin |
| i=25579 | InputArguments | Variable | - | RemoveApplication |
| i=25580 | AddEndpoint | Method | - | SecurityKeyServerAdmin |
| i=25581 | InputArguments | Variable | - | AddEndpoint |
| i=25582 | RemoveEndpoint | Method | - | SecurityKeyServerAdmin |
| i=25583 | InputArguments | Variable | - | RemoveEndpoint |
| i=25585 | Identities | Variable | - | SecurityKeyServerPush |
| i=25586 | ApplicationsExclude | Variable | - | SecurityKeyServerPush |
| i=25587 | Applications | Variable | - | SecurityKeyServerPush |
| i=25588 | EndpointsExclude | Variable | - | SecurityKeyServerPush |
| i=25589 | Endpoints | Variable | - | SecurityKeyServerPush |
| i=25590 | CustomConfiguration | Variable | - | SecurityKeyServerPush |
| i=25591 | AddIdentity | Method | - | SecurityKeyServerPush |
| i=25592 | InputArguments | Variable | - | AddIdentity |
| i=25593 | RemoveIdentity | Method | - | SecurityKeyServerPush |
| i=25594 | InputArguments | Variable | - | RemoveIdentity |
| i=25595 | AddApplication | Method | - | SecurityKeyServerPush |
| i=25596 | InputArguments | Variable | - | AddApplication |
| i=25597 | RemoveApplication | Method | - | SecurityKeyServerPush |
| i=25598 | InputArguments | Variable | - | RemoveApplication |
| i=25599 | AddEndpoint | Method | - | SecurityKeyServerPush |
| i=25600 | InputArguments | Variable | - | AddEndpoint |
| i=25601 | RemoveEndpoint | Method | - | SecurityKeyServerPush |
| i=25602 | InputArguments | Variable | - | RemoveEndpoint |
| i=25604 | Identities | Variable | - | SecurityKeyServerAccess |
| i=25605 | ApplicationsExclude | Variable | - | SecurityKeyServerAccess |
| i=25606 | Applications | Variable | - | SecurityKeyServerAccess |
| i=25607 | EndpointsExclude | Variable | - | SecurityKeyServerAccess |
| i=25608 | Endpoints | Variable | - | SecurityKeyServerAccess |
| i=25609 | CustomConfiguration | Variable | - | SecurityKeyServerAccess |
| i=25610 | AddIdentity | Method | - | SecurityKeyServerAccess |
| i=25611 | InputArguments | Variable | - | AddIdentity |
| i=25612 | RemoveIdentity | Method | - | SecurityKeyServerAccess |
| i=25613 | InputArguments | Variable | - | RemoveIdentity |
| i=25614 | AddApplication | Method | - | SecurityKeyServerAccess |
| i=25615 | InputArguments | Variable | - | AddApplication |
| i=25616 | RemoveApplication | Method | - | SecurityKeyServerAccess |
| i=25617 | InputArguments | Variable | - | RemoveApplication |
| i=25618 | AddEndpoint | Method | - | SecurityKeyServerAccess |
| i=25619 | InputArguments | Variable | - | AddEndpoint |
| i=25620 | RemoveEndpoint | Method | - | SecurityKeyServerAccess |
| i=25621 | InputArguments | Variable | - | RemoveEndpoint |

### Other Missing (84 nodes)

| NodeId | BrowseName | NodeClass | MR | Parent |
|--------|------------|-----------|-----|--------|
| i=11208 | Stepped | Variable | - | HA Configuration |
| i=12544 | InputArguments | Variable | Mandatory | OpenWithMasks |
| i=12545 | OutputArguments | Variable | Mandatory | OpenWithMasks |
| i=12547 | OutputArguments | Variable | Mandatory | CloseAndUpdate |
| i=12549 | InputArguments | Variable | Mandatory | AddCertificate |
| i=12551 | InputArguments | Variable | Mandatory | RemoveCertificate |
| i=12617 | InputArguments | Variable | Mandatory | UpdateCertificate |
| i=12618 | OutputArguments | Variable | Mandatory | UpdateCertificate |
| i=12705 | InputArguments | Variable | Mandatory | CloseAndUpdate |
| i=12732 | InputArguments | Variable | Mandatory | CreateSigningRequest |
| i=12733 | OutputArguments | Variable | Mandatory | CreateSigningRequest |
| i=12776 | OutputArguments | Variable | Mandatory | GetRejectedList |
| i=12886 | RequestServerStateChange | Method | - | Server |
| i=12887 | InputArguments | Variable | - | RequestServerStateChange |
| i=12912 | ConditionRefresh2 | Method | - | ConditionType |
| i=12913 | InputArguments | Variable | Mandatory | ConditionRefresh2 |
| i=13606 | InputArguments | Variable | Mandatory | Open |
| i=13607 | OutputArguments | Variable | Mandatory | Open |
| i=13609 | InputArguments | Variable | Mandatory | Close |
| i=13611 | InputArguments | Variable | Mandatory | Read |
| i=13612 | OutputArguments | Variable | Mandatory | Read |
| i=13614 | InputArguments | Variable | Mandatory | Write |
| i=13616 | InputArguments | Variable | Mandatory | GetPosition |
| i=13617 | OutputArguments | Variable | Mandatory | GetPosition |
| i=13619 | InputArguments | Variable | Mandatory | SetPosition |
| i=13622 | InputArguments | Variable | Mandatory | OpenWithMasks |
| i=13623 | OutputArguments | Variable | Mandatory | OpenWithMasks |
| i=13625 | InputArguments | Variable | Mandatory | CloseAndUpdate |
| i=13626 | OutputArguments | Variable | Mandatory | CloseAndUpdate |
| i=13628 | InputArguments | Variable | Mandatory | AddCertificate |
| i=13630 | InputArguments | Variable | Mandatory | RemoveCertificate |
| i=14089 | TrustList | Object | - | DefaultHttpsGroup |
| i=14096 | InputArguments | Variable | - | Open |
| i=14097 | OutputArguments | Variable | - | Open |
| i=14099 | InputArguments | Variable | - | Close |
| i=14101 | InputArguments | Variable | - | Read |
| i=14102 | OutputArguments | Variable | - | Read |
| i=14104 | InputArguments | Variable | - | Write |
| i=14106 | InputArguments | Variable | - | GetPosition |
| i=14107 | OutputArguments | Variable | - | GetPosition |
| i=14109 | InputArguments | Variable | - | SetPosition |
| i=14112 | InputArguments | Variable | - | OpenWithMasks |
| i=14113 | OutputArguments | Variable | - | OpenWithMasks |
| i=14115 | InputArguments | Variable | - | CloseAndUpdate |
| i=14116 | OutputArguments | Variable | - | CloseAndUpdate |
| i=14118 | InputArguments | Variable | - | AddCertificate |
| i=14120 | InputArguments | Variable | - | RemoveCertificate |
| i=14121 | CertificateTypes | Variable | - | DefaultHttpsGroup |
| i=16173 | Identities | Variable | Mandatory | RoleType |
| i=16192 | Identities | Variable | - | Anonymous |
| i=16203 | Identities | Variable | - | AuthenticatedUser |
| i=16349 | InputArguments | Variable | - | CreateDirectory |
| i=16350 | OutputArguments | Variable | - | CreateDirectory |
| i=16352 | InputArguments | Variable | - | CreateFile |
| i=16353 | OutputArguments | Variable | - | CreateFile |
| i=16355 | InputArguments | Variable | - | Delete |
| i=16357 | InputArguments | Variable | - | MoveOrCopy |
| i=16358 | OutputArguments | Variable | - | MoveOrCopy |
| i=17528 | CreateCredential | Method | - | KeyCredentialConfiguration |
| i=17529 | InputArguments | Variable | - | CreateCredential |
| i=17530 | OutputArguments | Variable | - | CreateCredential |
| i=18626 | Identities | Variable | - | TrustedApplication |
| i=24270 | InputArguments | Variable | Mandatory | AddUser |
| i=24272 | InputArguments | Variable | Mandatory | ModifyUser |
| i=24274 | InputArguments | Variable | Mandatory | RemoveUser |
| i=24276 | InputArguments | Variable | Mandatory | ChangePassword |
| i=24305 | InputArguments | Variable | - | AddUser |
| i=24307 | InputArguments | Variable | - | ModifyUser |
| i=24309 | InputArguments | Variable | - | RemoveUser |
| i=24311 | InputArguments | Variable | - | ChangePassword |
| i=25367 | InputArguments | Variable | Mandatory | AddPushTarget |
| i=25368 | OutputArguments | Variable | Mandatory | AddPushTarget |
| i=25370 | InputArguments | Variable | Mandatory | RemovePushTarget |
| i=32060 | SupportsFilteredRetain | Variable | - | ConditionType |
| i=3875 | ConditionRefresh | Method | - | ConditionType |
| i=3876 | InputArguments | Variable | Mandatory | ConditionRefresh |
| i=9018 | TrueState | Variable | - | EnabledState |
| i=9019 | FalseState | Variable | - | EnabledState |
| i=9062 | TrueState | Variable | - | DialogState |
| i=9063 | FalseState | Variable | - | DialogState |
| i=9100 | TrueState | Variable | - | AckedState |
| i=9101 | FalseState | Variable | - | AckedState |
| i=9167 | TrueState | Variable | - | ActiveState |
| i=9168 | FalseState | Variable | - | ActiveState |

### Trust List And Cert (36 nodes)

| NodeId | BrowseName | NodeClass | MR | Parent |
|--------|------------|-----------|-----|--------|
| i=12522 | TrustListType | ObjectType | - | - |
| i=12542 | LastUpdateTime | Variable | Mandatory | TrustListType |
| i=12543 | OpenWithMasks | Method | Mandatory | TrustListType |
| i=12546 | CloseAndUpdate | Method | Mandatory | TrustListType |
| i=12548 | AddCertificate | Method | Mandatory | TrustListType |
| i=12550 | RemoveCertificate | Method | Mandatory | TrustListType |
| i=13600 | Size | Variable | Mandatory | TrustList |
| i=13601 | Writable | Variable | Mandatory | TrustList |
| i=13602 | UserWritable | Variable | Mandatory | TrustList |
| i=13603 | OpenCount | Variable | Mandatory | TrustList |
| i=13605 | Open | Method | Mandatory | TrustList |
| i=13608 | Close | Method | Mandatory | TrustList |
| i=13610 | Read | Method | Mandatory | TrustList |
| i=13613 | Write | Method | Mandatory | TrustList |
| i=13615 | GetPosition | Method | Mandatory | TrustList |
| i=13618 | SetPosition | Method | Mandatory | TrustList |
| i=13620 | LastUpdateTime | Variable | Mandatory | TrustList |
| i=13621 | OpenWithMasks | Method | Mandatory | TrustList |
| i=13624 | CloseAndUpdate | Method | Mandatory | TrustList |
| i=13627 | AddCertificate | Method | Mandatory | TrustList |
| i=13629 | RemoveCertificate | Method | Mandatory | TrustList |
| i=14090 | Size | Variable | - | TrustList |
| i=14091 | Writable | Variable | - | TrustList |
| i=14092 | UserWritable | Variable | - | TrustList |
| i=14093 | OpenCount | Variable | - | TrustList |
| i=14095 | Open | Method | - | TrustList |
| i=14098 | Close | Method | - | TrustList |
| i=14100 | Read | Method | - | TrustList |
| i=14103 | Write | Method | - | TrustList |
| i=14105 | GetPosition | Method | - | TrustList |
| i=14108 | SetPosition | Method | - | TrustList |
| i=14110 | LastUpdateTime | Variable | - | TrustList |
| i=14111 | OpenWithMasks | Method | - | TrustList |
| i=14114 | CloseAndUpdate | Method | - | TrustList |
| i=14117 | AddCertificate | Method | - | TrustList |
| i=14119 | RemoveCertificate | Method | - | TrustList |

### Pubsub And Sks (31 nodes)

| NodeId | BrowseName | NodeClass | MR | Parent |
|--------|------------|-----------|-----|--------|
| i=15046 | KeyLifetime | Variable | Mandatory | SecurityGroupType |
| i=15047 | SecurityPolicyUri | Variable | Mandatory | SecurityGroupType |
| i=15048 | MaxFutureKeyCount | Variable | Mandatory | SecurityGroupType |
| i=15056 | MaxPastKeyCount | Variable | Mandatory | SecurityGroupType |
| i=15452 | SecurityGroupFolderType | ObjectType | - | - |
| i=15461 | AddSecurityGroup | Method | Mandatory | SecurityGroupFolderType |
| i=15462 | InputArguments | Variable | Mandatory | AddSecurityGroup |
| i=15463 | OutputArguments | Variable | Mandatory | AddSecurityGroup |
| i=15464 | RemoveSecurityGroup | Method | Mandatory | SecurityGroupFolderType |
| i=15465 | InputArguments | Variable | Mandatory | RemoveSecurityGroup |
| i=15471 | SecurityGroupType | ObjectType | - | - |
| i=15472 | SecurityGroupId | Variable | Mandatory | SecurityGroupType |
| i=25337 | PubSubKeyPushTargetType | ObjectType | - | - |
| i=25340 | SecurityPolicyUri | Variable | Mandatory | PubSubKeyPushTargetType |
| i=25346 | PubSubKeyPushTargetFolderType | ObjectType | - | - |
| i=25366 | AddPushTarget | Method | Mandatory | PubSubKeyPushTargetFolderType |
| i=25369 | RemovePushTarget | Method | Mandatory | PubSubKeyPushTargetFolderType |
| i=25634 | ApplicationUri | Variable | Mandatory | PubSubKeyPushTargetType |
| i=25635 | EndpointUrl | Variable | Mandatory | PubSubKeyPushTargetType |
| i=25636 | UserTokenType | Variable | Mandatory | PubSubKeyPushTargetType |
| i=25637 | RequestedKeyCount | Variable | Mandatory | PubSubKeyPushTargetType |
| i=25638 | RetryInterval | Variable | Mandatory | PubSubKeyPushTargetType |
| i=25639 | LastPushExecutionTime | Variable | Mandatory | PubSubKeyPushTargetType |
| i=25640 | LastPushErrorTime | Variable | Mandatory | PubSubKeyPushTargetType |
| i=25641 | ConnectSecurityGroups | Method | Mandatory | PubSubKeyPushTargetType |
| i=25642 | InputArguments | Variable | Mandatory | ConnectSecurityGroups |
| i=25643 | OutputArguments | Variable | Mandatory | ConnectSecurityGroups |
| i=25644 | DisconnectSecurityGroups | Method | Mandatory | PubSubKeyPushTargetType |
| i=25645 | InputArguments | Variable | Mandatory | DisconnectSecurityGroups |
| i=25646 | OutputArguments | Variable | Mandatory | DisconnectSecurityGroups |
| i=25647 | TriggerKeyUpdate | Method | Mandatory | PubSubKeyPushTargetType |

### Session And Server Diag (30 nodes)

| NodeId | BrowseName | NodeClass | MR | Parent |
|--------|------------|-----------|-----|--------|
| i=2028 | SessionSecurityDiagnosticsArray | Variable | Mandatory | SessionsDiagnosticsSummaryType |
| i=2031 | SessionSecurityDiagnostics | Variable | Mandatory | SessionDiagnosticsObjectType |
| i=2275 | ServerDiagnosticsSummary | Variable | - | ServerDiagnostics |
| i=2276 | ServerViewCount | Variable | - | ServerDiagnosticsSummary |
| i=2277 | CurrentSessionCount | Variable | - | ServerDiagnosticsSummary |
| i=2278 | CumulatedSessionCount | Variable | - | ServerDiagnosticsSummary |
| i=2279 | SecurityRejectedSessionCount | Variable | - | ServerDiagnosticsSummary |
| i=2281 | SessionTimeoutCount | Variable | - | ServerDiagnosticsSummary |
| i=2282 | SessionAbortCount | Variable | - | ServerDiagnosticsSummary |
| i=2284 | PublishingIntervalCount | Variable | - | ServerDiagnosticsSummary |
| i=2285 | CurrentSubscriptionCount | Variable | - | ServerDiagnosticsSummary |
| i=2286 | CumulatedSubscriptionCount | Variable | - | ServerDiagnosticsSummary |
| i=2287 | SecurityRejectedRequestsCount | Variable | - | ServerDiagnosticsSummary |
| i=2288 | RejectedRequestsCount | Variable | - | ServerDiagnosticsSummary |
| i=2289 | SamplingIntervalDiagnosticsArray | Variable | - | ServerDiagnostics |
| i=2290 | SubscriptionDiagnosticsArray | Variable | - | ServerDiagnostics |
| i=3113 | SessionSecurityDiagnosticsArray | Variable | Mandatory | SessionsDiagnosticsSummary |
| i=3130 | SessionSecurityDiagnosticsArray | Variable | Mandatory | SessionsDiagnosticsSummary |
| i=3179 | SessionId | Variable | Mandatory | SessionSecurityDiagnostics |
| i=3180 | ClientUserIdOfSession | Variable | Mandatory | SessionSecurityDiagnostics |
| i=3181 | ClientUserIdHistory | Variable | Mandatory | SessionSecurityDiagnostics |
| i=3182 | AuthenticationMechanism | Variable | Mandatory | SessionSecurityDiagnostics |
| i=3183 | Encoding | Variable | Mandatory | SessionSecurityDiagnostics |
| i=3184 | TransportProtocol | Variable | Mandatory | SessionSecurityDiagnostics |
| i=3185 | SecurityMode | Variable | Mandatory | SessionSecurityDiagnostics |
| i=3186 | SecurityPolicyUri | Variable | Mandatory | SessionSecurityDiagnostics |
| i=3187 | ClientCertificate | Variable | Mandatory | SessionSecurityDiagnostics |
| i=3705 | RejectedSessionCount | Variable | - | ServerDiagnosticsSummary |
| i=3707 | SessionDiagnosticsArray | Variable | - | SessionsDiagnosticsSummary |
| i=3708 | SessionSecurityDiagnosticsArray | Variable | - | SessionsDiagnosticsSummary |

### Orphaned Type Children (25 nodes)

| NodeId | BrowseName | NodeClass | MR | Parent |
|--------|------------|-----------|-----|--------|
| i=11202 | HA Configuration | Object | - | - |
| i=11214 | Annotations | Variable | - | - |
| i=11215 | HistoricalEventFilter | Variable | - | - |
| i=11312 | CurrentServerId | Variable | - | - |
| i=11314 | ServerUriArray | Variable | - | - |
| i=11432 | EnumStrings | Variable | - | - |
| i=11433 | ValueAsText | Variable | - | - |
| i=11498 | MaxStringLength | Variable | - | - |
| i=11512 | MaxArrayLength | Variable | - | - |
| i=11513 | EngineeringUnits | Variable | - | - |
| i=12170 | ViewVersion | Variable | - | - |
| i=12745 | OptionSetValues | Variable | - | - |
| i=12908 | MaxByteStringLength | Variable | - | - |
| i=14415 | ServerNetworkGroups | Variable | - | - |
| i=15002 | MaxCharacters | Variable | - | - |
| i=17605 | DefaultInstanceBrowseName | Variable | - | - |
| i=23501 | CurrencyUnit | Variable | - | - |
| i=3067 | Icon | Variable | - | - |
| i=3068 | NodeVersion | Variable | - | - |
| i=3069 | LocalTime | Variable | - | - |
| i=3070 | AllowNulls | Variable | - | - |
| i=3071 | EnumValues | Variable | - | - |
| i=3072 | InputArguments | Variable | - | - |
| i=3073 | OutputArguments | Variable | - | - |
| i=32750 | OptionSetLength | Variable | - | - |

### User Management (10 nodes)

| NodeId | BrowseName | NodeClass | MR | Parent |
|--------|------------|-----------|-----|--------|
| i=24265 | Users | Variable | Mandatory | UserManagementType |
| i=24269 | AddUser | Method | Mandatory | UserManagementType |
| i=24271 | ModifyUser | Method | Mandatory | UserManagementType |
| i=24273 | RemoveUser | Method | Mandatory | UserManagementType |
| i=24275 | ChangePassword | Method | Mandatory | UserManagementType |
| i=24301 | Users | Variable | - | UserManagement |
| i=24304 | AddUser | Method | - | UserManagement |
| i=24306 | ModifyUser | Method | - | UserManagement |
| i=24308 | RemoveUser | Method | - | UserManagement |
| i=24310 | ChangePassword | Method | - | UserManagement |

### Server Config And Certs (8 nodes)

| NodeId | BrowseName | NodeClass | MR | Parent |
|--------|------------|-----------|-----|--------|
| i=12555 | CertificateGroupType | ObjectType | - | - |
| i=12616 | UpdateCertificate | Method | Mandatory | ServerConfigurationType |
| i=12731 | CreateSigningRequest | Method | Mandatory | ServerConfigurationType |
| i=12734 | ApplyChanges | Method | Mandatory | ServerConfigurationType |
| i=12775 | GetRejectedList | Method | Mandatory | ServerConfigurationType |
| i=13599 | TrustList | Object | Mandatory | CertificateGroupType |
| i=13631 | CertificateTypes | Variable | Mandatory | CertificateGroupType |
| i=14088 | DefaultHttpsGroup | Object | - | CertificateGroups |

### Filesystem (5 nodes)

| NodeId | BrowseName | NodeClass | MR | Parent |
|--------|------------|-----------|-----|--------|
| i=16314 | FileSystem | Object | - | - |
| i=16348 | CreateDirectory | Method | - | FileSystem |
| i=16351 | CreateFile | Method | - | FileSystem |
| i=16354 | Delete | Method | - | FileSystem |
| i=16356 | MoveOrCopy | Method | - | FileSystem |

### Ha Aggregate Config (5 nodes)

| NodeId | BrowseName | NodeClass | MR | Parent |
|--------|------------|-----------|-----|--------|
| i=11203 | AggregateConfiguration | Object | - | HA Configuration |
| i=11204 | TreatUncertainAsBad | Variable | - | AggregateConfiguration |
| i=11205 | PercentDataBad | Variable | - | AggregateConfiguration |
| i=11206 | PercentDataGood | Variable | - | AggregateConfiguration |
| i=11207 | UseSlopedExtrapolation | Variable | - | AggregateConfiguration |

### Program State (4 nodes)

| NodeId | BrowseName | NodeClass | MR | Parent |
|--------|------------|-----------|-----|--------|
| i=2392 | Creatable | Variable | - | ProgramStateMachineType |
| i=2396 | InstanceCount | Variable | - | ProgramStateMachineType |
| i=2397 | MaxInstanceCount | Variable | - | ProgramStateMachineType |
| i=2398 | MaxRecycleCount | Variable | - | ProgramStateMachineType |

## Source Generator Fix Assessment

### Fix 1: Encoding Nodes (Default Binary/XML/JSON)
- **384 encoding nodes** still missing (192 Binary + 192 XML)
- These are encoding objects with **no parent reference** in the spec XML
- They are "free-floating" objects that map DataTypes to their encodings
- The source generator fix emits encoding nodes for types it generates,
  but these 384 are for types that have encoding definitions but may not
  be actively generated in the current model

### Fix 2: EventNotifier Auto-Inference
- This fix corrects **attribute values**, not node presence
- Cannot be measured by missing node count

### Fix 3: Type Definition RBAC (AccessRestrictions/RolePermissions)
- Fixed: type nodes no longer incorrectly inherit instance-level RBAC
- Impact: reduces false "missing" from permission-denied reads

### Fix 4: ModellingRule=None Object Children on Types
- **172 role instance nodes** still missing
- **25 orphaned type children** still missing
- These are children with no ModellingRule (MR=None) on type definitions
  that should be emitted as concrete instances
