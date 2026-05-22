# OPC UA Conformance Test Coverage

Last updated: 2026-05-09

## Summary

| Metric | Count |
|--------|------:|
| Source scripts mapped (distinct (CU, Tag) pairs) | 1,720 |
| NUnit tests mapped (carry both `ConformanceUnit` and `Tag`) | 3,252 |
| NUnit tests additional coverage (no `ConformanceUnit`/`Tag`) | 5 |
| Total NUnit tests | 3,257 |
| Skipped at runtime (`Assert.Ignore` calls) | 538 |
| Skipped at startup (`[Ignore]` attribute) | 1 |
| Skipped by filter (`[Property("Limitation", …)]`) | 21 |
| Failed | 0 |

> Counts are derived by static analysis of the test files. The 21
> `Limitation`-tagged tests are 18 `RequiresKerberos`, 2 `Sha1NotSupported`,
> and 1 `RequiresMulticast` (see "Filtering tests by limitation" below).
> `Assert.Ignore` is the project's primary spec-gap marker — issues #3719b,
> #3719c, and others surface this way; #3720 was resolved in the most recent
> commit (StateMachine GeneratesEvent runtime injection).

## In-process response mutation hook (RequiresServerMock replacement)

Conformance tests that need to inject service-result error codes or
mutate response fields use the in-process mock controller:

```csharp
// Service-result injection (one-shot — fires on the next BrowseResponse):
using IDisposable handle = MockController.ExpectNextResponse<BrowseResponse>(
    r => r.ResponseHeader.ServiceResult = StatusCodes.BadNothingToDo);

ServiceResultException ex = Assert.ThrowsAsync<ServiceResultException>(
    async () => await Session.BrowseAsync(...));
Assert.That(ex.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadNothingToDo));
```

```csharp
// Recurring expectation matched on every CreateSession round trip
// (used because Session.OpenAsync retries CreateSession without the
// client certificate when the first attempt fails):
using IDisposable handle = MockController.WhenRequest<CreateSessionRequest, CreateSessionResponse>(
    (req, resp) => resp.ResponseHeader.ServiceResult = StatusCodes.BadSecureChannelIdInvalid);

Assert.ThrowsAsync<ServiceResultException>(
    async () => await OpenAuxSessionAsync());
```

The hook is exposed on `IServerBase.ResponseMutator` and is invoked
from `EndpointBase.EndpointIncomingRequest.CallAsync` immediately
after the service has produced the response and before the response
is dispatched. Production servers leave it null. The
`MockResponseController` is reset between tests in
`TestFixture.[SetUp]` so each test starts from a clean state.

## Filtering tests by limitation

Some conformance tests still require setup that's impractical for
an in-process fixture:

```
dotnet test Tests/Opc.Ua.Conformance.Tests/Opc.Ua.Conformance.Tests.csproj \
    -c Release -f net10.0 \
    --filter "Limitation!=RequiresKerberos & Limitation!=Sha1NotSupported & Limitation!=RequiresMulticast"
```

Currently 18 tests are tagged `RequiresKerberos` (in `SecurityUserTokenDepthTests`)
— these test Kerberos token policy and require a real Key Distribution Center,
which is impractical for an in-process test fixture.

Currently 2 tests are tagged `Sha1NotSupported` (`CertValidation049`,
`CertValidation050`) — modern .NET refuses to sign certificates with
SHA1 entirely. The server's expected behaviour for SHA1 client certs
is also rejection, so the no-op skip is consistent with spec intent.

Currently 1 test is tagged `RequiresMulticast`
(`LdsMeMulticastAnnouncementAsync` in `LdsMeConformanceTests`) — it
exercises mDNS multicast announcements, which are flaky on CI loopback
and contention-prone on developer machines that already run a local
LDS. The non-multicast LDS-ME paths are covered by the other tests in
the same fixture.

Filter combinations:

```
--filter "Limitation!=RequiresKerberos & Limitation!=Sha1NotSupported & Limitation!=RequiresMulticast"
```

## Tag conventions

`Tag` values mirror the upstream CTT JavaScript file naming, so different
conformance units use different conventions:

| Pattern | Used by | Example |
|---|---|---|
| `NNN` (3 digits) | majority of CUs | `001`, `017` |
| `Err-NNN` (hyphen) | error-path tests in most CUs | `Err-011`, `Err-046` |
| `Test_NNN` (underscore) | Alarms & Conditions positive-path tests | `Test_001` |
| `Err_NNN` (underscore) | Alarms & Conditions error-path tests | `Err_005` |
| `N/A` | upstream script has no tag (rare) | "A and C Base Refresh" |

Do not normalise these tags — they are deliberate parity markers back to
the upstream CTT files.

## Coverage by Category

| Category | Conformance Units | Mapped Tests | Additional | Pass Rate |
|----------|------------------:|-------------:|-----------:|----------:|
| Address Space Model | 7 | 134 | 0 | 94% |
| Alarms & Conditions | 40 | 105 | 31 | 42% |
| Alarms & Events | 1 | 0 | 1 | 0% |
| Alias Names | 4 | 37 | 8 | 80% |
| Attribute Services | 6 | 183 | 0 | 98% |
| Auditing | 6 | 91 | 0 | 87% |
| Best Practices | 3 | 23 | 1 | 100% |
| Data Access | 6 | 99 | 25 | 96% |
| Discovery Services | 4 | 124 | 0 | 80% |
| GDS | 5 | 256 | 0 | 80% |
| Historical Access | 9 | 148 | 27 | 54% |
| Information Model | 80 | 426 | 0 | 79% |
| Method Services | 1 | 23 | 0 | 100% |
| Miscellaneous | 7 | 0 | 0 | 0% |
| Monitored Item Services | 7 | 264 | 0 | 94% |
| Node Management | 4 | 27 | 0 | 81% |
| Security | 36 | 487 | 33 | 65% |
| Session Services | 5 | 114 | 0 | 89% |
| Subscription Services | 10 | 343 | 0 | 85% |
| View Services | 5 | 200 | 0 | 95% |

## Detailed Coverage

### Address Space Model

<details>
<summary>Address Space Model / Address Space Atomicity ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|

</details>

<details>
<summary>Address Space Model / Address Space Base ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | AccessLevelExOnVariableAsync | [AddressSpaceModelExtendedTests](AddressSpaceModel/AddressSpaceModelExtendedTests.cs) | ✅ |
| 001 | AccessLevelExReadableAsync | [AddressSpaceModelExtendedTests](AddressSpaceModel/AddressSpaceModelExtendedTests.cs) | ✅ |
| 001 | AddInForwardRefsFromServerAsync | [AddressSpaceModelExtendedTests](AddressSpaceModel/AddressSpaceModelExtendedTests.cs) | ✅ |
| 001 | AddInInstanceBrowseNameNotEmptyAsync | [AddressSpaceModelExtendedTests](AddressSpaceModel/AddressSpaceModelExtendedTests.cs) | ✅ |
| 001 | AddInInverseRefExistsAsync | [AddressSpaceModelExtendedTests](AddressSpaceModel/AddressSpaceModelExtendedTests.cs) | ✅ |
| 001 | AddInTargetIsObjectAsync | [AddressSpaceModelExtendedTests](AddressSpaceModel/AddressSpaceModelExtendedTests.cs) | ✅ |
| 001 | ArrayVarValueRankIsOneDimAsync | [AddressSpaceModelExtendedTests](AddressSpaceModel/AddressSpaceModelExtendedTests.cs) | ✅ |
| 001 | AtomicBitsAccessLevelExAsync | [AddressSpaceModelExtendedTests](AddressSpaceModel/AddressSpaceModelExtendedTests.cs) | ✅ |
| 001 | BaseInterfaceIsSubtypeOfBaseObjectAsync | [AddressSpaceModelExtendedTests](AddressSpaceModel/AddressSpaceModelExtendedTests.cs) | ✅ |
| 001 | BaseInterfaceTypeExistsAsync | [AddressSpaceModelExtendedTests](AddressSpaceModel/AddressSpaceModelExtendedTests.cs) | ✅ |
| 001 | BaseObjectTypeExistsAsync | [AddressSpaceBaseTests](AddressSpaceModel/AddressSpaceBaseTests.cs) | ✅ |
| 001 | BaseVariableTypeExistsAsync | [AddressSpaceBaseTests](AddressSpaceModel/AddressSpaceBaseTests.cs) | ✅ |
| 001 | BooleanDataTypeExistsAsync | [AddressSpaceReferenceTypeTests](AddressSpaceModel/AddressSpaceReferenceTypeTests.cs) | ✅ |
| 000 | ConditionTypeExistsAsync | [AddressSpaceModelExtendedTests](AddressSpaceModel/AddressSpaceModelExtendedTests.cs) | ✅ |
| 001 | DefinitionContainsStructDefAsync | [AddressSpaceModelExtendedTests](AddressSpaceModel/AddressSpaceModelExtendedTests.cs) | ✅ |
| 001 | DefinitionFieldsHaveNamesAsync | [AddressSpaceModelExtendedTests](AddressSpaceModel/AddressSpaceModelExtendedTests.cs) | ✅ |
| 001 | DictEntryIsSubtypeOfBaseAsync | [AddressSpaceModelExtendedTests](AddressSpaceModel/AddressSpaceModelExtendedTests.cs) | ✅ |
| 001 | DictEntryTypeExistsAsync | [AddressSpaceModelExtendedTests](AddressSpaceModel/AddressSpaceModelExtendedTests.cs) | ✅ |
| 001 | DictFolderExistsAsync | [AddressSpaceModelExtendedTests](AddressSpaceModel/AddressSpaceModelExtendedTests.cs) | ✅ |
| 002 | EnumDataTypeHasDefinitionAsync | [AddressSpaceModelExtendedTests](AddressSpaceModel/AddressSpaceModelExtendedTests.cs) | ✅ |
| 001 | HasAddInIsSubtypeOfHasComponentAsync | [AddressSpaceModelExtendedTests](AddressSpaceModel/AddressSpaceModelExtendedTests.cs) | ✅ |
| 001 | HasAddInRefTypeExistsAsync | [AddressSpaceModelExtendedTests](AddressSpaceModel/AddressSpaceModelExtendedTests.cs) | ✅ |
| 001 | HasComponentReferenceTypeExistsAsync | [AddressSpaceReferenceTypeTests](AddressSpaceModel/AddressSpaceReferenceTypeTests.cs) | ✅ |
| 001 | HasDictEntryIsNonHierarchicalAsync | [AddressSpaceModelExtendedTests](AddressSpaceModel/AddressSpaceModelExtendedTests.cs) | ✅ |
| 001 | HasDictEntryRefTypeExistsAsync | [AddressSpaceModelExtendedTests](AddressSpaceModel/AddressSpaceModelExtendedTests.cs) | ✅ |
| 000 | HasEventSourceIsSubtypeOfHierarchicalAsync | [AddressSpaceModelExtendedTests](AddressSpaceModel/AddressSpaceModelExtendedTests.cs) | ✅ |
| 000 | HasEventSourceRefExistsAsync | [AddressSpaceModelExtendedTests](AddressSpaceModel/AddressSpaceModelExtendedTests.cs) | ✅ |
| 001 | HasInterfaceRefTypeExistsAsync | [AddressSpaceModelExtendedTests](AddressSpaceModel/AddressSpaceModelExtendedTests.cs) | ✅ |
| 001 | HasPropertyReferenceTypeExistsAsync | [AddressSpaceReferenceTypeTests](AddressSpaceModel/AddressSpaceReferenceTypeTests.cs) | ✅ |
| 001 | HasSubtypeReferenceTypeExistsAsync | [AddressSpaceReferenceTypeTests](AddressSpaceModel/AddressSpaceReferenceTypeTests.cs) | ✅ |
| 001 | HasTypeDefinitionReferenceTypeExistsAsync | [AddressSpaceReferenceTypeTests](AddressSpaceModel/AddressSpaceReferenceTypeTests.cs) | ✅ |
| 001 | Int32DataTypeExistsAsync | [AddressSpaceReferenceTypeTests](AddressSpaceModel/AddressSpaceReferenceTypeTests.cs) | ✅ |
| 001 | IrdiDictEntryTypeExistsAsync | [AddressSpaceModelExtendedTests](AddressSpaceModel/AddressSpaceModelExtendedTests.cs) | ✅ |
| 001 | IrdiDictIsSubtypeOfEntryAsync | [AddressSpaceModelExtendedTests](AddressSpaceModel/AddressSpaceModelExtendedTests.cs) | ✅ |
| 001 | MethodHasArgDescRefAsync | [AddressSpaceModelExtendedTests](AddressSpaceModel/AddressSpaceModelExtendedTests.cs) | ⏭️ |
| 004 | MethodInputArgsValueRankIsArrayAsync | [AddressSpaceModelExtendedTests](AddressSpaceModel/AddressSpaceModelExtendedTests.cs) | ✅ |
| 002 | MethodMetaDataTargetIsVariableAsync | [AddressSpaceModelExtendedTests](AddressSpaceModel/AddressSpaceModelExtendedTests.cs) | ✅ |
| 002 | MethodOutputArgsIsArgArrayAsync | [AddressSpaceModelExtendedTests](AddressSpaceModel/AddressSpaceModelExtendedTests.cs) | ✅ |
| 001 | NonVolatileBitInAccessLevelAsync | [AddressSpaceModelExtendedTests](AddressSpaceModel/AddressSpaceModelExtendedTests.cs) | ✅ |
| 001 | NonVolatileBitInAccessLevelExAsync | [AddressSpaceModelExtendedTests](AddressSpaceModel/AddressSpaceModelExtendedTests.cs) | ✅ |
| 001 | NotifierHierarchyNoLoopsAsync | [AddressSpaceModelExtendedTests](AddressSpaceModel/AddressSpaceModelExtendedTests.cs) | ✅ |
| 001 | OrganizesReferenceTypeExistsAsync | [AddressSpaceReferenceTypeTests](AddressSpaceModel/AddressSpaceReferenceTypeTests.cs) | ✅ |
| 001 | ReferenceTypeHierarchyExistsAsync | [AddressSpaceBaseTests](AddressSpaceModel/AddressSpaceBaseTests.cs) | ✅ |
| 001 | ScalarVarValueRankIsScalarAsync | [AddressSpaceModelExtendedTests](AddressSpaceModel/AddressSpaceModelExtendedTests.cs) | ✅ |
| 001 | ServerCapabilitiesExistsAsync | [AddressSpaceBaseTests](AddressSpaceModel/AddressSpaceBaseTests.cs) | ✅ |
| 001 | ServerHasNotifierRefsAsync | [AddressSpaceModelExtendedTests](AddressSpaceModel/AddressSpaceModelExtendedTests.cs) | ✅ |
| 001 | ServerStatusExistsAsync | [AddressSpaceBaseTests](AddressSpaceModel/AddressSpaceBaseTests.cs) | ✅ |
| 001 | ServerStatusHasRequiredVariablesAsync | [AddressSpaceBaseTests](AddressSpaceModel/AddressSpaceBaseTests.cs) | ✅ |
| 000 | SourceHierarchyNoLoopsAsync | [AddressSpaceModelExtendedTests](AddressSpaceModel/AddressSpaceModelExtendedTests.cs) | ✅ |
| 001 | StringDataTypeExistsAsync | [AddressSpaceReferenceTypeTests](AddressSpaceModel/AddressSpaceReferenceTypeTests.cs) | ✅ |
| 001 | StructureDataTypeHasDefinitionAsync | [AddressSpaceModelExtendedTests](AddressSpaceModel/AddressSpaceModelExtendedTests.cs) | ✅ |
| 000 | SystemEventTypeExistsAsync | [AddressSpaceModelExtendedTests](AddressSpaceModel/AddressSpaceModelExtendedTests.cs) | ✅ |
| 000 | TransitionEventTypeExistsAsync | [AddressSpaceModelExtendedTests](AddressSpaceModel/AddressSpaceModelExtendedTests.cs) | ✅ |
| 001 | UriDictEntryTypeExistsAsync | [AddressSpaceModelExtendedTests](AddressSpaceModel/AddressSpaceModelExtendedTests.cs) | ✅ |
| 001 | UriDictIsSubtypeOfEntryAsync | [AddressSpaceModelExtendedTests](AddressSpaceModel/AddressSpaceModelExtendedTests.cs) | ✅ |
| 002 | UserAccessLevelHistoryReadBitAsync | [AddressSpaceModelExtendedTests](AddressSpaceModel/AddressSpaceModelExtendedTests.cs) | ✅ |
| 002 | UserAccessLevelHistoryWriteBitAsync | [AddressSpaceModelExtendedTests](AddressSpaceModel/AddressSpaceModelExtendedTests.cs) | ✅ |
| 005 | UserWriteMaskOnObjectNodeAsync | [AddressSpaceModelExtendedTests](AddressSpaceModel/AddressSpaceModelExtendedTests.cs) | ✅ |
| 005 | UserWriteMaskOnObjectTypeNodeAsync | [AddressSpaceModelExtendedTests](AddressSpaceModel/AddressSpaceModelExtendedTests.cs) | ✅ |
| 001 | VariableNodeHasValueRankAttributeAsync | [AddressSpaceBaseTests](AddressSpaceModel/AddressSpaceBaseTests.cs) | ✅ |
| 001 | WriteMaskOnMethodNodeAsync | [AddressSpaceModelExtendedTests](AddressSpaceModel/AddressSpaceModelExtendedTests.cs) | ✅ |
| 001 | WriteMaskOnObjectNodeAsync | [AddressSpaceModelExtendedTests](AddressSpaceModel/AddressSpaceModelExtendedTests.cs) | ✅ |
| 001 | WriteMaskOnObjectTypeNodeAsync | [AddressSpaceModelExtendedTests](AddressSpaceModel/AddressSpaceModelExtendedTests.cs) | ✅ |
| 002 | ObjectsFolderExistsAsync | [AddressSpaceBaseTests](AddressSpaceModel/AddressSpaceBaseTests.cs) | ✅ |
| 002 | RootFolderExistsAsync | [AddressSpaceBaseTests](AddressSpaceModel/AddressSpaceBaseTests.cs) | ✅ |
| 002 | ServerObjectExistsAsync | [AddressSpaceBaseTests](AddressSpaceModel/AddressSpaceBaseTests.cs) | ✅ |
| 002 | ServerObjectHasRequiredChildrenAsync | [AddressSpaceBaseTests](AddressSpaceModel/AddressSpaceBaseTests.cs) | ✅ |
| 002 | TypesFolderContainsSubfoldersAsync | [AddressSpaceBaseTests](AddressSpaceModel/AddressSpaceBaseTests.cs) | ✅ |
| 002 | TypesFolderExistsAsync | [AddressSpaceBaseTests](AddressSpaceModel/AddressSpaceBaseTests.cs) | ✅ |
| 002 | ViewsFolderExistsAsync | [AddressSpaceBaseTests](AddressSpaceModel/AddressSpaceBaseTests.cs) | ✅ |
| 003 | DataTypeHierarchyNumberToInt32Async | [AddressSpaceBaseTests](AddressSpaceModel/AddressSpaceBaseTests.cs) | ✅ |
| 003 | VariableNodeHasDataTypeAttributeAsync | [AddressSpaceBaseTests](AddressSpaceModel/AddressSpaceBaseTests.cs) | ✅ |

</details>

<details>
<summary>Address Space Model / Address Space Events ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 000 | AuditEventTypeExistsAsync | [AddressSpaceEventsTests](AddressSpaceModel/AddressSpaceEventsTests.cs) | ✅ |
| 000 | AuditEventTypeIsSubtypeOfBaseEventTypeAsync | [AddressSpaceEventsTests](AddressSpaceModel/AddressSpaceEventsTests.cs) | ✅ |
| 000 | BaseEventTypeExistsAsync | [AddressSpaceEventsTests](AddressSpaceModel/AddressSpaceEventsTests.cs) | ✅ |
| 000 | BaseEventTypeHasMandatoryPropertiesAsync | [AddressSpaceEventsTests](AddressSpaceModel/AddressSpaceEventsTests.cs) | ✅ |
| 000 | ObjectsFolderHasEventNotifierAttributeAsync | [AddressSpaceEventsTests](AddressSpaceModel/AddressSpaceEventsTests.cs) | ✅ |
| 000 | ServerObjectHasEventNotifierAsync | [AddressSpaceEventsTests](AddressSpaceModel/AddressSpaceEventsTests.cs) | ✅ |

</details>

<details>
<summary>Address Space Model / Address Space Method ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | MethodExecutableIsTrueAsync | [AddressSpaceMethodTests](AddressSpaceModel/AddressSpaceMethodTests.cs) | ✅ |
| 001 | MethodHasComponentReferenceFromParentAsync | [AddressSpaceMethodTests](AddressSpaceModel/AddressSpaceMethodTests.cs) | ✅ |
| 001 | MethodInputArgumentsHaveCorrectDataTypeAsync | [AddressSpaceMethodTests](AddressSpaceModel/AddressSpaceMethodTests.cs) | ✅ |
| 001 | MethodNodeClassIsMethodAsync | [AddressSpaceMethodTests](AddressSpaceModel/AddressSpaceMethodTests.cs) | ✅ |
| 001 | MethodNodeHasExecutableAttributeAsync | [AddressSpaceMethodTests](AddressSpaceModel/AddressSpaceMethodTests.cs) | ✅ |
| 001 | MethodNodeHasInputArgumentsAsync | [AddressSpaceMethodTests](AddressSpaceModel/AddressSpaceMethodTests.cs) | ✅ |
| 001 | MethodNodeHasOutputArgumentsAsync | [AddressSpaceMethodTests](AddressSpaceModel/AddressSpaceMethodTests.cs) | ✅ |
| 001 | MethodNodeHasUserExecutableAttributeAsync | [AddressSpaceMethodTests](AddressSpaceModel/AddressSpaceMethodTests.cs) | ✅ |

</details>

<details>
<summary>Address Space Model / Address Space Notifier Hierarchy ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | ArrayVariableHasArrayDimensionsAsync | [AddressSpaceHierarchyTests](AddressSpaceModel/AddressSpaceHierarchyTests.cs) | ✅ |
| 001 | ArrayVariableHasCorrectValueRankAsync | [AddressSpaceHierarchyTests](AddressSpaceModel/AddressSpaceHierarchyTests.cs) | ✅ |
| 001 | BaseDataTypeExistsAsync | [AddressSpaceHierarchyTests](AddressSpaceModel/AddressSpaceHierarchyTests.cs) | ✅ |
| 001 | BaseObjectTypeExistsAsync | [AddressSpaceHierarchyTests](AddressSpaceModel/AddressSpaceHierarchyTests.cs) | ✅ |
| 001 | BaseVariableTypeExistsAsync | [AddressSpaceHierarchyTests](AddressSpaceModel/AddressSpaceHierarchyTests.cs) | ✅ |
| 001 | BrowseForInterfaceTypesAsync | [AddressSpaceHierarchyTests](AddressSpaceModel/AddressSpaceHierarchyTests.cs) | ✅ |
| 001 | BrowseHasEventSourceFromServerAsync | [AddressSpaceHierarchyTests](AddressSpaceModel/AddressSpaceHierarchyTests.cs) | ✅ |
| 001 | BrowseHasNotifierFromServerAsync | [AddressSpaceHierarchyTests](AddressSpaceModel/AddressSpaceHierarchyTests.cs) | ✅ |
| 001 | BrowseTypeDefinitionOfObjectInstanceMatchesDeclaredTypeAsync | [AddressSpaceHierarchyTests](AddressSpaceModel/AddressSpaceHierarchyTests.cs) | ✅ |
| 001 | BrowseTypeDefinitionOfVariableAsync | [AddressSpaceHierarchyTests](AddressSpaceModel/AddressSpaceHierarchyTests.cs) | ✅ |
| 001 | DataTypeFolderExistsAsync | [AddressSpaceHierarchyTests](AddressSpaceModel/AddressSpaceHierarchyTests.cs) | ✅ |
| 001 | EventSourceNodesHaveEventNotifierAttributeAsync | [AddressSpaceHierarchyTests](AddressSpaceModel/AddressSpaceHierarchyTests.cs) | ✅ |
| 001 | InstanceDeclarationsHaveModellingRulesAsync | [AddressSpaceHierarchyTests](AddressSpaceModel/AddressSpaceHierarchyTests.cs) | ✅ |
| 001 | ObjectsFolderChildrenHaveTypeDefinitionAsync | [AddressSpaceHierarchyTests](AddressSpaceModel/AddressSpaceHierarchyTests.cs) | ✅ |
| 001 | ReadAccessLevelOnReadOnlyPropertyAsync | [AddressSpaceHierarchyTests](AddressSpaceModel/AddressSpaceHierarchyTests.cs) | ✅ |
| 001 | ReadUserAccessLevelOnReadableNodeAsync | [AddressSpaceHierarchyTests](AddressSpaceModel/AddressSpaceHierarchyTests.cs) | ✅ |
| 001 | ReadUserAccessLevelOnWritableNodeAsync | [AddressSpaceHierarchyTests](AddressSpaceModel/AddressSpaceHierarchyTests.cs) | ✅ |
| 001 | ReferenceTypeFolderExistsAsync | [AddressSpaceHierarchyTests](AddressSpaceModel/AddressSpaceHierarchyTests.cs) | ✅ |
| 001 | ScalarVariableHasBaseDataVariableTypeAsync | [AddressSpaceHierarchyTests](AddressSpaceModel/AddressSpaceHierarchyTests.cs) | ✅ |
| 001 | ServerCapabilitiesHasTypeDefinitionAsync | [AddressSpaceHierarchyTests](AddressSpaceModel/AddressSpaceHierarchyTests.cs) | ✅ |
| 001 | ServerObjectHasTypeDefinitionAsync | [AddressSpaceHierarchyTests](AddressSpaceModel/AddressSpaceHierarchyTests.cs) | ✅ |
| 001 | ServerStatusHasTypeDefinitionAsync | [AddressSpaceHierarchyTests](AddressSpaceModel/AddressSpaceHierarchyTests.cs) | ✅ |
| 001 | VerifyAccessRestrictionsAttributeOnNodesAsync | [AddressSpaceHierarchyTests](AddressSpaceModel/AddressSpaceHierarchyTests.cs) | ⏭️ |
| 001 | VerifyBaseObjectTypeToFolderTypeSubtypeChainAsync | [AddressSpaceHierarchyTests](AddressSpaceModel/AddressSpaceHierarchyTests.cs) | ✅ |
| 001 | VerifyBaseVariableTypeSubtypesAsync | [AddressSpaceHierarchyTests](AddressSpaceModel/AddressSpaceHierarchyTests.cs) | ✅ |
| 001 | VerifyHasInterfaceReferencesAsync | [AddressSpaceHierarchyTests](AddressSpaceModel/AddressSpaceHierarchyTests.cs) | ✅ |
| 001 | VerifyNotifierHierarchyReachesEventSourcesAsync | [AddressSpaceHierarchyTests](AddressSpaceModel/AddressSpaceHierarchyTests.cs) | ✅ |
| 001 | VerifyNumberToIntegerDataTypeHierarchyAsync | [AddressSpaceHierarchyTests](AddressSpaceModel/AddressSpaceHierarchyTests.cs) | ✅ |
| 001 | VerifyServerMandatoryComponentsAsync | [AddressSpaceHierarchyTests](AddressSpaceModel/AddressSpaceHierarchyTests.cs) | ✅ |

</details>

<details>
<summary>Address Space Model / Address Space UserWriteMask ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|

</details>

<details>
<summary>Address Space Model / Address Space WriteMask ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | ReadAccessLevelOnWritableVariableAsync | [AddressSpaceWriteMaskTests](AddressSpaceModel/AddressSpaceWriteMaskTests.cs) | ✅ |
| 001 | ReadHistorizingOnVariableAsync | [AddressSpaceWriteMaskTests](AddressSpaceModel/AddressSpaceWriteMaskTests.cs) | ✅ |
| 001 | ReadMinimumSamplingIntervalOnVariableAsync | [AddressSpaceWriteMaskTests](AddressSpaceModel/AddressSpaceWriteMaskTests.cs) | ✅ |
| 001 | ReadUserAccessLevelOnWritableVariableAsync | [AddressSpaceWriteMaskTests](AddressSpaceModel/AddressSpaceWriteMaskTests.cs) | ✅ |
| 001 | ReadUserWriteMaskOnVariableAsync | [AddressSpaceWriteMaskTests](AddressSpaceModel/AddressSpaceWriteMaskTests.cs) | ✅ |
| 001 | ReadWriteMaskOnVariableAsync | [AddressSpaceWriteMaskTests](AddressSpaceModel/AddressSpaceWriteMaskTests.cs) | ✅ |
| 002 | ReadAccessLevelOnServerStateVariableAsync | [AddressSpaceWriteMaskTests](AddressSpaceModel/AddressSpaceWriteMaskTests.cs) | ✅ |

</details>

### Alarms & Conditions

<details>
<summary>Alarms & Conditions / A and C Acknowledge ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| Err_004 | ErrAcknowledgeOnDisabledConditionAsync | [AlarmsAndConditionsAcknowledgeTests](AlarmsAndConditions/AlarmsAndConditionsAcknowledgeTests.cs) | ✅ |
| Err_005 | ErrAcknowledgeWithBadNodeIdAsync | [AlarmsAndConditionsAcknowledgeTests](AlarmsAndConditions/AlarmsAndConditionsAcknowledgeTests.cs) | ✅ |
| Err_006 | ErrAcknowledgeWithInvalidMethodArgsAsync | [AlarmsAndConditionsAcknowledgeTests](AlarmsAndConditions/AlarmsAndConditionsAcknowledgeTests.cs) | ✅ |
| Err_007 | ErrAcknowledgeAlreadyAcknowledgedAsync | [AlarmsAndConditionsAcknowledgeTests](AlarmsAndConditions/AlarmsAndConditionsAcknowledgeTests.cs) | ✅ |
| Err_008 | ErrAcknowledgeWithNullEventIdAsync | [AlarmsAndConditionsAcknowledgeTests](AlarmsAndConditions/AlarmsAndConditionsAcknowledgeTests.cs) | ✅ |
| Err_009 | ErrAcknowledgeWithEmptyCommentAsync | [AlarmsAndConditionsAcknowledgeTests](AlarmsAndConditions/AlarmsAndConditionsAcknowledgeTests.cs) | ✅ |
| Test_001 | AcknowledgeableConditionTypeHasAckedStateAsync | [AlarmsAndConditionsAcknowledgeTests](AlarmsAndConditions/AlarmsAndConditionsAcknowledgeTests.cs) | ✅ |
| Test_001 | AcknowledgeableConditionTypeHasAcknowledgeMethodAsync | [AlarmsAndConditionsAcknowledgeTests](AlarmsAndConditions/AlarmsAndConditionsAcknowledgeTests.cs) | ✅ |
| Test_002 | AcknowledgeConditionSetsAckedStateTrueAsync | [AlarmsAndConditionsAcknowledgeTests](AlarmsAndConditions/AlarmsAndConditionsAcknowledgeTests.cs) | ✅ |

</details>

<details>
<summary>Alarms & Conditions / A and C Alarm ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| Test_000 | AlarmConditionIsSubtypeOfAcknowledgeableAsync | [AlarmsAndConditionsAlarmTests](AlarmsAndConditions/AlarmsAndConditionsAlarmTests.cs) | ✅ |
| Test_000 | AlarmConditionTypeExistsAsync | [AlarmsAndConditionsAlarmTests](AlarmsAndConditions/AlarmsAndConditionsAlarmTests.cs) | ✅ |
| Test_000 | AlarmGroupTypeExistsAsync | [AlarmsAndConditionsAlarmTests](AlarmsAndConditions/AlarmsAndConditionsAlarmTests.cs) | ✅ |
| Test_000 | AlarmGroupTypeIsSubtypeOfFolderTypeAsync | [AlarmsAndConditionsAlarmTests](AlarmsAndConditions/AlarmsAndConditionsAlarmTests.cs) | ✅ |
| Test_000 | AlarmSuppressionGroupTypeExistsAsync | [AlarmsAndConditionsAlarmTests](AlarmsAndConditions/AlarmsAndConditionsAlarmTests.cs) | ✅ |

</details>

<details>
<summary>Alarms & Conditions / A and C Alarm Metrics — 1 additional ⏭️</summary>

**Additional coverage** (not mapped to specific source scripts):

| NUnit Test | Fixture | Status |
|-----------|---------|--------|
| AlarmMetricsPlaceholder | [AlarmsAndConditionsPlaceholderTests](AlarmsAndConditions/AlarmsAndConditionsPlaceholderTests.cs) | ⏭️ |

</details>

<details>
<summary>Alarms & Conditions / A and C Audible Sound — 1 additional ⏭️</summary>

**Additional coverage** (not mapped to specific source scripts):

| NUnit Test | Fixture | Status |
|-----------|---------|--------|
| AudibleSoundPlaceholder | [AlarmsAndConditionsPlaceholderTests](AlarmsAndConditions/AlarmsAndConditionsPlaceholderTests.cs) | ⏭️ |

</details>

<details>
<summary>Alarms & Conditions / A and C Base Discrete ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| Test_001 | DiscreteAlarmTypeExistsAsync | [AlarmsAndConditionsBaseTests](AlarmsAndConditions/AlarmsAndConditionsBaseTests.cs) | ✅ |
| Test_001 | OffNormalAlarmTypeExistsAsync | [AlarmsAndConditionsBaseTests](AlarmsAndConditions/AlarmsAndConditionsBaseTests.cs) | ✅ |
| Test_002 | DiscreteAlarmTypeIsSubtypeOfAlarmConditionAsync | [AlarmsAndConditionsBaseTests](AlarmsAndConditions/AlarmsAndConditionsBaseTests.cs) | ✅ |

</details>

<details>
<summary>Alarms & Conditions / A and C Base Limit ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| Test_001 | ExclusiveAndNonExclusiveLimitAlarmTypesExistAsync | [AlarmsAndConditionsBaseTests](AlarmsAndConditions/AlarmsAndConditionsBaseTests.cs) | ✅ |
| Test_001 | LimitAlarmTypeExistsAsync | [AlarmsAndConditionsBaseTests](AlarmsAndConditions/AlarmsAndConditionsBaseTests.cs) | ✅ |
| Test_001 | LimitAlarmTypeHasHighHighLimitAsync | [AlarmsAndConditionsBaseTests](AlarmsAndConditions/AlarmsAndConditionsBaseTests.cs) | ✅ |
| Test_001 | LimitAlarmTypeHasHighLimitAsync | [AlarmsAndConditionsBaseTests](AlarmsAndConditions/AlarmsAndConditionsBaseTests.cs) | ✅ |
| Test_001 | LimitAlarmTypeHasLowLimitAsync | [AlarmsAndConditionsBaseTests](AlarmsAndConditions/AlarmsAndConditionsBaseTests.cs) | ✅ |
| Test_001 | LimitAlarmTypeHasLowLowLimitAsync | [AlarmsAndConditionsBaseTests](AlarmsAndConditions/AlarmsAndConditionsBaseTests.cs) | ✅ |
| Test_002 | LimitAlarmTypeIsSubtypeOfAlarmConditionTypeAsync | [AlarmsAndConditionsBaseTests](AlarmsAndConditions/AlarmsAndConditionsBaseTests.cs) | ✅ |

</details>

<details>
<summary>Alarms & Conditions / A and C Base Refresh , 2 additional ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| Test_001 | ConditionTypeExistsAsync | [AlarmsAndConditionsBaseTests](AlarmsAndConditions/AlarmsAndConditionsBaseTests.cs) | ✅ |
| Test_001 | ConditionTypeHasBranchIdAsync | [AlarmsAndConditionsBaseTests](AlarmsAndConditions/AlarmsAndConditionsBaseTests.cs) | ✅ |
| Test_001 | ConditionTypeHasConditionNameAsync | [AlarmsAndConditionsBaseTests](AlarmsAndConditions/AlarmsAndConditionsBaseTests.cs) | ✅ |
| Test_001 | ConditionTypeHasConditionRefresh2MethodAsync | [AlarmsAndConditionsBaseTests](AlarmsAndConditions/AlarmsAndConditionsBaseTests.cs) | ✅ |
| Test_001 | ConditionTypeHasConditionRefreshMethodAsync | [AlarmsAndConditionsBaseTests](AlarmsAndConditions/AlarmsAndConditionsBaseTests.cs) | ✅ |
| Test_001 | ConditionTypeHasEnabledStateAsync | [AlarmsAndConditionsBaseTests](AlarmsAndConditions/AlarmsAndConditionsBaseTests.cs) | ✅ |

**Additional coverage** (not mapped to specific source scripts):

| NUnit Test | Fixture | Status |
|-----------|---------|--------|
| ConditionRefreshReturnsEvents | [AlarmsAndConditionsBaseTests](AlarmsAndConditions/AlarmsAndConditionsBaseTests.cs) | ⏭️ |
| ConditionRefreshSubscriptionEventTest | [AlarmsAndConditionsBaseTests](AlarmsAndConditions/AlarmsAndConditionsBaseTests.cs) | ⏭️ |

</details>

<details>
<summary>Alarms & Conditions / A and C Basic ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| Test_001 | AcknowledgeableConditionTypeHasAckedStateAsync | [AlarmsAndConditionsBasicTests](AlarmsAndConditions/AlarmsAndConditionsBasicTests.cs) | ✅ |
| Test_001 | AlarmConditionTypeExistsInAddressSpaceAsync | [AlarmsAndConditionsBasicTests](AlarmsAndConditions/AlarmsAndConditionsBasicTests.cs) | ✅ |
| Test_001 | ConditionTypeExistsInAddressSpaceAsync | [AlarmsAndConditionsBasicTests](AlarmsAndConditions/AlarmsAndConditionsBasicTests.cs) | ✅ |
| Test_002 | AlarmConditionTypeHasActiveAndSuppressedStateAsync | [AlarmsAndConditionsBasicTests](AlarmsAndConditions/AlarmsAndConditionsBasicTests.cs) | ✅ |

</details>

<details>
<summary>Alarms & Conditions / A and C Branch ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| Test_001 | ConditionTypeHasBranchIdPropertyAsync | [AlarmsAndConditionsBranchTests](AlarmsAndConditions/AlarmsAndConditionsBranchTests.cs) | ✅ |
| Test_001 | ConditionTypeHasRetainAsync | [AlarmsAndConditionsBranchTests](AlarmsAndConditions/AlarmsAndConditionsBranchTests.cs) | ✅ |
| Test_002 | BranchCreatedOnStateChangeAsync | [AlarmsAndConditionsBranchTests](AlarmsAndConditions/AlarmsAndConditionsBranchTests.cs) | ✅ |
| Test_003 | AcknowledgeBranchAsync | [AlarmsAndConditionsBranchTests](AlarmsAndConditions/AlarmsAndConditionsBranchTests.cs) | ✅ |
| Test_004 | BranchHasRetainPropertyAsync | [AlarmsAndConditionsBranchTests](AlarmsAndConditions/AlarmsAndConditionsBranchTests.cs) | ✅ |
| Test_006 | BranchHasNonNullBranchIdAsync | [AlarmsAndConditionsBranchTests](AlarmsAndConditions/AlarmsAndConditionsBranchTests.cs) | ✅ |
| Test_007 | ConfirmBranchAsync | [AlarmsAndConditionsBranchTests](AlarmsAndConditions/AlarmsAndConditionsBranchTests.cs) | ✅ |

</details>

<details>
<summary>Alarms & Conditions / A and C CertificateExpiration ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| Test_000 | CertificateExpirationAlarmTypeExistsAsync | [AlarmsAndConditionsCertificateExpirationTests](AlarmsAndConditions/AlarmsAndConditionsCertificateExpirationTests.cs) | ✅ |
| Test_000 | CertificateExpirationHasCertificateTypeAsync | [AlarmsAndConditionsCertificateExpirationTests](AlarmsAndConditions/AlarmsAndConditionsCertificateExpirationTests.cs) | ✅ |
| Test_000 | CertificateExpirationHasExpirationDateAsync | [AlarmsAndConditionsCertificateExpirationTests](AlarmsAndConditions/AlarmsAndConditionsCertificateExpirationTests.cs) | ✅ |
| Test_000 | CertificateExpirationHasExpirationLimitAsync | [AlarmsAndConditionsCertificateExpirationTests](AlarmsAndConditions/AlarmsAndConditionsCertificateExpirationTests.cs) | ✅ |
| Test_000 | CertificateExpirationIsSubtypeOfSystemOffNormalAsync | [AlarmsAndConditionsCertificateExpirationTests](AlarmsAndConditions/AlarmsAndConditionsCertificateExpirationTests.cs) | ✅ |

</details>

<details>
<summary>Alarms & Conditions / A and C Comment , 1 additional ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| Err_002 | ErrAddCommentWithBadEventIdAsync | [AlarmsAndConditionsCommentTests](AlarmsAndConditions/AlarmsAndConditionsCommentTests.cs) | ✅ |
| Err_003 | ErrAddCommentWithInvalidMethodArgsAsync | [AlarmsAndConditionsCommentTests](AlarmsAndConditions/AlarmsAndConditionsCommentTests.cs) | ✅ |
| Err_004 | ErrAddCommentWithBadNodeIdAsync | [AlarmsAndConditionsCommentTests](AlarmsAndConditions/AlarmsAndConditionsCommentTests.cs) | ✅ |
| Err_005 | ErrAddCommentWithWrongObjectIdAsync | [AlarmsAndConditionsCommentTests](AlarmsAndConditions/AlarmsAndConditionsCommentTests.cs) | ✅ |
| Err_006 | ErrAddCommentWithNullEventIdAsync | [AlarmsAndConditionsCommentTests](AlarmsAndConditions/AlarmsAndConditionsCommentTests.cs) | ✅ |
| Test_000 | ConditionTypeHasAddCommentMethodAsync | [AlarmsAndConditionsCommentTests](AlarmsAndConditions/AlarmsAndConditionsCommentTests.cs) | ✅ |
| Test_000 | ConditionTypeHasClientUserIdAsync | [AlarmsAndConditionsCommentTests](AlarmsAndConditions/AlarmsAndConditionsCommentTests.cs) | ✅ |
| Test_000 | ConditionTypeHasCommentPropertyAsync | [AlarmsAndConditionsCommentTests](AlarmsAndConditions/AlarmsAndConditionsCommentTests.cs) | ✅ |
| Test_000 | ConditionTypeHasLastSeverityAsync | [AlarmsAndConditionsCommentTests](AlarmsAndConditions/AlarmsAndConditionsCommentTests.cs) | ✅ |
| Test_000 | ConditionTypeHasQualityAsync | [AlarmsAndConditionsCommentTests](AlarmsAndConditions/AlarmsAndConditionsCommentTests.cs) | ✅ |

**Additional coverage** (not mapped to specific source scripts):

| NUnit Test | Fixture | Status |
|-----------|---------|--------|
| ErrAddCommentOnDisabledCondition | [AlarmsAndConditionsCommentTests](AlarmsAndConditions/AlarmsAndConditionsCommentTests.cs) | ⏭️ |

</details>

<details>
<summary>Alarms & Conditions / A and C Condition Sub-Classes — 1 additional ⏭️</summary>

**Additional coverage** (not mapped to specific source scripts):

| NUnit Test | Fixture | Status |
|-----------|---------|--------|
| ConditionSubClassesPlaceholder | [AlarmsAndConditionsPlaceholderTests](AlarmsAndConditions/AlarmsAndConditionsPlaceholderTests.cs) | ⏭️ |

</details>

<details>
<summary>Alarms & Conditions / A and C ConditionClasses — 1 additional ⏭️</summary>

**Additional coverage** (not mapped to specific source scripts):

| NUnit Test | Fixture | Status |
|-----------|---------|--------|
| ConditionClassesPlaceholder | [AlarmsAndConditionsPlaceholderTests](AlarmsAndConditions/AlarmsAndConditionsPlaceholderTests.cs) | ⏭️ |

</details>

<details>
<summary>Alarms & Conditions / A and C Confirm ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| Err_004 | ErrConfirmOnDisabledConditionAsync | [AlarmsAndConditionsConfirmTests](AlarmsAndConditions/AlarmsAndConditionsConfirmTests.cs) | ✅ |
| Err_005 | ErrConfirmWithBadNodeIdAsync | [AlarmsAndConditionsConfirmTests](AlarmsAndConditions/AlarmsAndConditionsConfirmTests.cs) | ✅ |
| Err_006 | ErrConfirmWithInvalidMethodArgsAsync | [AlarmsAndConditionsConfirmTests](AlarmsAndConditions/AlarmsAndConditionsConfirmTests.cs) | ✅ |
| Err_007 | ErrConfirmAlreadyConfirmedAsync | [AlarmsAndConditionsConfirmTests](AlarmsAndConditions/AlarmsAndConditionsConfirmTests.cs) | ✅ |
| Err_008 | ErrConfirmWithNullEventIdAsync | [AlarmsAndConditionsConfirmTests](AlarmsAndConditions/AlarmsAndConditionsConfirmTests.cs) | ✅ |
| Err_009 | ErrConfirmWithEmptyCommentAsync | [AlarmsAndConditionsConfirmTests](AlarmsAndConditions/AlarmsAndConditionsConfirmTests.cs) | ✅ |
| Test_001 | AcknowledgeableConditionTypeHasConfirmMethodAsync | [AlarmsAndConditionsConfirmTests](AlarmsAndConditions/AlarmsAndConditionsConfirmTests.cs) | ✅ |
| Test_001 | AcknowledgeableConditionTypeHasConfirmedStateAsync | [AlarmsAndConditionsConfirmTests](AlarmsAndConditions/AlarmsAndConditionsConfirmTests.cs) | ✅ |
| Test_002 | ConfirmConditionSetsConfirmedStateTrueAsync | [AlarmsAndConditionsConfirmTests](AlarmsAndConditions/AlarmsAndConditionsConfirmTests.cs) | ✅ |

</details>

<details>
<summary>Alarms & Conditions / A and C Dialog — 1 additional ⏭️</summary>

**Additional coverage** (not mapped to specific source scripts):

| NUnit Test | Fixture | Status |
|-----------|---------|--------|
| DialogPlaceholder | [AlarmsAndConditionsPlaceholderTests](AlarmsAndConditions/AlarmsAndConditionsPlaceholderTests.cs) | ⏭️ |

</details>

<details>
<summary>Alarms & Conditions / A and C Discrepancy — 1 additional ⏭️</summary>

**Additional coverage** (not mapped to specific source scripts):

| NUnit Test | Fixture | Status |
|-----------|---------|--------|
| DiscrepancyPlaceholder | [AlarmsAndConditionsPlaceholderTests](AlarmsAndConditions/AlarmsAndConditionsPlaceholderTests.cs) | ⏭️ |

</details>

<details>
<summary>Alarms & Conditions / A and C Discrete — 1 additional ⏭️</summary>

**Additional coverage** (not mapped to specific source scripts):

| NUnit Test | Fixture | Status |
|-----------|---------|--------|
| DiscretePlaceholder | [AlarmsAndConditionsPlaceholderTests](AlarmsAndConditions/AlarmsAndConditionsPlaceholderTests.cs) | ⏭️ |

</details>

<details>
<summary>Alarms & Conditions / A and C Enable , 2 additional ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| Err_004 | ErrDisableAlreadyDisabledAsync | [AlarmsAndConditionsEnableTests](AlarmsAndConditions/AlarmsAndConditionsEnableTests.cs) | ✅ |
| Err_005 | ErrEnableAlreadyEnabledAsync | [AlarmsAndConditionsEnableTests](AlarmsAndConditions/AlarmsAndConditionsEnableTests.cs) | ✅ |
| Test_001 | ConditionTypeHasEnableAndDisableMethodsAsync | [AlarmsAndConditionsEnableTests](AlarmsAndConditions/AlarmsAndConditionsEnableTests.cs) | ✅ |
| Test_001 | ConditionTypeHasEnabledStateAsync | [AlarmsAndConditionsEnableTests](AlarmsAndConditions/AlarmsAndConditionsEnableTests.cs) | ✅ |
| Test_002 | DisableConditionSetsEnabledStateFalseAsync | [AlarmsAndConditionsEnableTests](AlarmsAndConditions/AlarmsAndConditionsEnableTests.cs) | ✅ |
| Test_002 | EnableConditionSetsEnabledStateTrueAsync | [AlarmsAndConditionsEnableTests](AlarmsAndConditions/AlarmsAndConditionsEnableTests.cs) | ✅ |

**Additional coverage** (not mapped to specific source scripts):

| NUnit Test | Fixture | Status |
|-----------|---------|--------|
| ErrDisableWithBadNodeId | [AlarmsAndConditionsEnableTests](AlarmsAndConditions/AlarmsAndConditionsEnableTests.cs) | ⏭️ |
| ErrEnableWithBadNodeId | [AlarmsAndConditionsEnableTests](AlarmsAndConditions/AlarmsAndConditionsEnableTests.cs) | ⏭️ |

</details>

<details>
<summary>Alarms & Conditions / A and C Exclusive Deviation — 1 additional ⏭️</summary>

**Additional coverage** (not mapped to specific source scripts):

| NUnit Test | Fixture | Status |
|-----------|---------|--------|
| ExclusiveDeviationPlaceholder | [AlarmsAndConditionsPlaceholderTests](AlarmsAndConditions/AlarmsAndConditionsPlaceholderTests.cs) | ⏭️ |

</details>

<details>
<summary>Alarms & Conditions / A and C Exclusive Level — 1 additional ⏭️</summary>

**Additional coverage** (not mapped to specific source scripts):

| NUnit Test | Fixture | Status |
|-----------|---------|--------|
| ExclusiveLevelPlaceholder | [AlarmsAndConditionsPlaceholderTests](AlarmsAndConditions/AlarmsAndConditionsPlaceholderTests.cs) | ⏭️ |

</details>

<details>
<summary>Alarms & Conditions / A and C Exclusive Limit — 1 additional ⏭️</summary>

**Additional coverage** (not mapped to specific source scripts):

| NUnit Test | Fixture | Status |
|-----------|---------|--------|
| ExclusiveLimitPlaceholder | [AlarmsAndConditionsPlaceholderTests](AlarmsAndConditions/AlarmsAndConditionsPlaceholderTests.cs) | ⏭️ |

</details>

<details>
<summary>Alarms & Conditions / A and C Exclusive RateOfChange — 1 additional ⏭️</summary>

**Additional coverage** (not mapped to specific source scripts):

| NUnit Test | Fixture | Status |
|-----------|---------|--------|
| ExclusiveRateOfChangePlaceholder | [AlarmsAndConditionsPlaceholderTests](AlarmsAndConditions/AlarmsAndConditionsPlaceholderTests.cs) | ⏭️ |

</details>

<details>
<summary>Alarms & Conditions / A and C First in Group Alarm — 1 additional ⏭️</summary>

**Additional coverage** (not mapped to specific source scripts):

| NUnit Test | Fixture | Status |
|-----------|---------|--------|
| FirstInGroupAlarmPlaceholder | [AlarmsAndConditionsPlaceholderTests](AlarmsAndConditions/AlarmsAndConditionsPlaceholderTests.cs) | ⏭️ |

</details>

<details>
<summary>Alarms & Conditions / A and C Instances ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| Test_001 | AlarmConditionTypeExistsAsync | [AlarmsAndConditionsInstancesTests](AlarmsAndConditions/AlarmsAndConditionsInstancesTests.cs) | ✅ |
| Test_001 | AlarmConditionTypeHasActiveStateAsync | [AlarmsAndConditionsInstancesTests](AlarmsAndConditions/AlarmsAndConditionsInstancesTests.cs) | ✅ |
| Test_001 | AlarmConditionTypeHasInputNodeAsync | [AlarmsAndConditionsInstancesTests](AlarmsAndConditions/AlarmsAndConditionsInstancesTests.cs) | ✅ |
| Test_001 | AlarmInstanceHasCorrectTypeDefinitionAsync | [AlarmsAndConditionsInstancesTests](AlarmsAndConditions/AlarmsAndConditionsInstancesTests.cs) | ✅ |
| Test_001 | AlarmInstancesExistInAddressSpace | [AlarmsAndConditionsInstancesTests](AlarmsAndConditions/AlarmsAndConditionsInstancesTests.cs) | ✅ |
| Test_002 | AlarmInstanceHasSourceNodeAsync | [AlarmsAndConditionsInstancesTests](AlarmsAndConditions/AlarmsAndConditionsInstancesTests.cs) | ✅ |

</details>

<details>
<summary>Alarms & Conditions / A and C Non-Exclusive Deviation — 1 additional ⏭️</summary>

**Additional coverage** (not mapped to specific source scripts):

| NUnit Test | Fixture | Status |
|-----------|---------|--------|
| NonExclusiveDeviationPlaceholder | [AlarmsAndConditionsPlaceholderTests](AlarmsAndConditions/AlarmsAndConditionsPlaceholderTests.cs) | ⏭️ |

</details>

<details>
<summary>Alarms & Conditions / A and C Non-Exclusive Level — 1 additional ⏭️</summary>

**Additional coverage** (not mapped to specific source scripts):

| NUnit Test | Fixture | Status |
|-----------|---------|--------|
| NonExclusiveLevelPlaceholder | [AlarmsAndConditionsPlaceholderTests](AlarmsAndConditions/AlarmsAndConditionsPlaceholderTests.cs) | ⏭️ |

</details>

<details>
<summary>Alarms & Conditions / A and C Non-Exclusive Limit — 1 additional ⏭️</summary>

**Additional coverage** (not mapped to specific source scripts):

| NUnit Test | Fixture | Status |
|-----------|---------|--------|
| NonExclusiveLimitPlaceholder | [AlarmsAndConditionsPlaceholderTests](AlarmsAndConditions/AlarmsAndConditionsPlaceholderTests.cs) | ⏭️ |

</details>

<details>
<summary>Alarms & Conditions / A and C Non-Exclusive RateOfChange — 1 additional ⏭️</summary>

**Additional coverage** (not mapped to specific source scripts):

| NUnit Test | Fixture | Status |
|-----------|---------|--------|
| NonExclusiveRateOfChangePlaceholder | [AlarmsAndConditionsPlaceholderTests](AlarmsAndConditions/AlarmsAndConditionsPlaceholderTests.cs) | ⏭️ |

</details>

<details>
<summary>Alarms & Conditions / A and C OffNormal — 1 additional ⏭️</summary>

**Additional coverage** (not mapped to specific source scripts):

| NUnit Test | Fixture | Status |
|-----------|---------|--------|
| OffNormalPlaceholder | [AlarmsAndConditionsPlaceholderTests](AlarmsAndConditions/AlarmsAndConditionsPlaceholderTests.cs) | ⏭️ |

</details>

<details>
<summary>Alarms & Conditions / A and C On-Off Delay — 1 additional ⏭️</summary>

**Additional coverage** (not mapped to specific source scripts):

| NUnit Test | Fixture | Status |
|-----------|---------|--------|
| OnOffDelayPlaceholder | [AlarmsAndConditionsPlaceholderTests](AlarmsAndConditions/AlarmsAndConditionsPlaceholderTests.cs) | ⏭️ |

</details>

<details>
<summary>Alarms & Conditions / A and C Out Of Service — 1 additional ⏭️</summary>

**Additional coverage** (not mapped to specific source scripts):

| NUnit Test | Fixture | Status |
|-----------|---------|--------|
| OutOfServicePlaceholder | [AlarmsAndConditionsPlaceholderTests](AlarmsAndConditions/AlarmsAndConditionsPlaceholderTests.cs) | ⏭️ |

</details>

<details>
<summary>Alarms & Conditions / A and C Re-Alarming — 1 additional ⏭️</summary>

**Additional coverage** (not mapped to specific source scripts):

| NUnit Test | Fixture | Status |
|-----------|---------|--------|
| ReAlarmingPlaceholder | [AlarmsAndConditionsPlaceholderTests](AlarmsAndConditions/AlarmsAndConditionsPlaceholderTests.cs) | ⏭️ |

</details>

<details>
<summary>Alarms & Conditions / A and C Refresh , 1 additional ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| Err_003 | ErrConditionRefreshWithBadSubscriptionIdAsync | [AlarmsAndConditionsRefreshTests](AlarmsAndConditions/AlarmsAndConditionsRefreshTests.cs) | ✅ |
| Err_004 | ErrConditionRefreshConcurrentAsync | [AlarmsAndConditionsRefreshTests](AlarmsAndConditions/AlarmsAndConditionsRefreshTests.cs) | ✅ |
| Err_005 | ErrConditionRefreshWithInvalidArgsAsync | [AlarmsAndConditionsRefreshTests](AlarmsAndConditions/AlarmsAndConditionsRefreshTests.cs) | ✅ |
| Test_002 | ConditionRefreshReturnsCurrentStateAsync | [AlarmsAndConditionsRefreshTests](AlarmsAndConditions/AlarmsAndConditionsRefreshTests.cs) | ✅ |

**Additional coverage** (not mapped to specific source scripts):

| NUnit Test | Fixture | Status |
|-----------|---------|--------|
| ConditionRefreshMethodExists | [AlarmsAndConditionsRefreshTests](AlarmsAndConditions/AlarmsAndConditionsRefreshTests.cs) | ✅ |

</details>

<details>
<summary>Alarms & Conditions / A and C Refresh2 , 1 additional ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| Err_002 | ErrConditionRefresh2WithBadSubscriptionIdAsync | [AlarmsAndConditionsRefreshTests](AlarmsAndConditions/AlarmsAndConditionsRefreshTests.cs) | ✅ |
| Err_003 | ErrConditionRefresh2ConcurrentAsync | [AlarmsAndConditionsRefreshTests](AlarmsAndConditions/AlarmsAndConditionsRefreshTests.cs) | ✅ |
| Err_004 | ErrConditionRefresh2WithBadMonitoredItemIdAsync | [AlarmsAndConditionsRefreshTests](AlarmsAndConditions/AlarmsAndConditionsRefreshTests.cs) | ✅ |
| Err_006 | ErrConditionRefresh2WithInvalidArgsAsync | [AlarmsAndConditionsRefreshTests](AlarmsAndConditions/AlarmsAndConditionsRefreshTests.cs) | ✅ |
| Err_007 | ErrConditionRefresh2OnNonEventItemAsync | [AlarmsAndConditionsRefreshTests](AlarmsAndConditions/AlarmsAndConditionsRefreshTests.cs) | ✅ |
| Test_002 | ConditionRefresh2ReturnsCurrentStateAsync | [AlarmsAndConditionsRefreshTests](AlarmsAndConditions/AlarmsAndConditionsRefreshTests.cs) | ✅ |

**Additional coverage** (not mapped to specific source scripts):

| NUnit Test | Fixture | Status |
|-----------|---------|--------|
| ConditionRefresh2MethodExists | [AlarmsAndConditionsRefreshTests](AlarmsAndConditions/AlarmsAndConditionsRefreshTests.cs) | ✅ |

</details>

<details>
<summary>Alarms & Conditions / A and C Shelving ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| Err_001 | ErrTimedShelveWithBadNodeIdAsync | [AlarmsAndConditionsShelvingTests](AlarmsAndConditions/AlarmsAndConditionsShelvingTests.cs) | ✅ |
| Err_002 | ErrTimedShelveWithZeroDurationAsync | [AlarmsAndConditionsShelvingTests](AlarmsAndConditions/AlarmsAndConditionsShelvingTests.cs) | ✅ |
| Err_003 | ErrUnshelveWhenNotShelvedAsync | [AlarmsAndConditionsShelvingTests](AlarmsAndConditions/AlarmsAndConditionsShelvingTests.cs) | ✅ |
| Test_000 | AlarmConditionTypeHasShelvingStateAsync | [AlarmsAndConditionsShelvingTests](AlarmsAndConditions/AlarmsAndConditionsShelvingTests.cs) | ✅ |
| Test_000 | ShelvedStateMachineHasOneShotShelveMethodAsync | [AlarmsAndConditionsShelvingTests](AlarmsAndConditions/AlarmsAndConditionsShelvingTests.cs) | ✅ |
| Test_000 | ShelvedStateMachineHasTimedShelveMethodAsync | [AlarmsAndConditionsShelvingTests](AlarmsAndConditions/AlarmsAndConditionsShelvingTests.cs) | ✅ |
| Test_000 | ShelvedStateMachineHasUnshelveMethodAsync | [AlarmsAndConditionsShelvingTests](AlarmsAndConditions/AlarmsAndConditionsShelvingTests.cs) | ✅ |
| Test_000 | ShelvedStateMachineHasUnshelveTimeAsync | [AlarmsAndConditionsShelvingTests](AlarmsAndConditions/AlarmsAndConditionsShelvingTests.cs) | ✅ |
| Test_000 | ShelvedStateMachineTypeExistsAsync | [AlarmsAndConditionsShelvingTests](AlarmsAndConditions/AlarmsAndConditionsShelvingTests.cs) | ✅ |
| Test_002 | TimedShelveTransitionsToTimedShelvedAsync | [AlarmsAndConditionsShelvingTests](AlarmsAndConditions/AlarmsAndConditionsShelvingTests.cs) | ✅ |
| Test_003 | OneShotShelveTransitionsToOneShotShelvedAsync | [AlarmsAndConditionsShelvingTests](AlarmsAndConditions/AlarmsAndConditionsShelvingTests.cs) | ✅ |
| Test_004 | UnshelveTransitionsToUnshelvedAsync | [AlarmsAndConditionsShelvingTests](AlarmsAndConditions/AlarmsAndConditionsShelvingTests.cs) | ✅ |
| Test_005 | TimedShelveWithDurationAsync | [AlarmsAndConditionsShelvingTests](AlarmsAndConditions/AlarmsAndConditionsShelvingTests.cs) | ✅ |
| Test_006 | ShelveGeneratesEventAsync | [AlarmsAndConditionsShelvingTests](AlarmsAndConditions/AlarmsAndConditionsShelvingTests.cs) | ✅ |

</details>

<details>
<summary>Alarms & Conditions / A and C Silencing — 1 additional ⏭️</summary>

**Additional coverage** (not mapped to specific source scripts):

| NUnit Test | Fixture | Status |
|-----------|---------|--------|
| SilencingPlaceholder | [AlarmsAndConditionsPlaceholderTests](AlarmsAndConditions/AlarmsAndConditionsPlaceholderTests.cs) | ⏭️ |

</details>

<details>
<summary>Alarms & Conditions / A and C Suppression ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| Test_001 | AlarmConditionTypeHasMaxTimeShelvedAsync | [AlarmsAndConditionsSuppressionTests](AlarmsAndConditions/AlarmsAndConditionsSuppressionTests.cs) | ✅ |
| Test_001 | AlarmConditionTypeHasSuppressedOrShelvedAsync | [AlarmsAndConditionsSuppressionTests](AlarmsAndConditions/AlarmsAndConditionsSuppressionTests.cs) | ✅ |
| Test_001 | AlarmConditionTypeHasSuppressedStateAsync | [AlarmsAndConditionsSuppressionTests](AlarmsAndConditions/AlarmsAndConditionsSuppressionTests.cs) | ✅ |
| Test_002 | SuppressionStateTransitionAsync | [AlarmsAndConditionsSuppressionTests](AlarmsAndConditions/AlarmsAndConditionsSuppressionTests.cs) | ✅ |

</details>

<details>
<summary>Alarms & Conditions / A and C Suppression by Operator — 1 additional ⏭️</summary>

**Additional coverage** (not mapped to specific source scripts):

| NUnit Test | Fixture | Status |
|-----------|---------|--------|
| SuppressionByOperatorPlaceholder | [AlarmsAndConditionsPlaceholderTests](AlarmsAndConditions/AlarmsAndConditionsPlaceholderTests.cs) | ⏭️ |

</details>

<details>
<summary>Alarms & Conditions / A and C SystemOffNormal — 1 additional ⏭️</summary>

**Additional coverage** (not mapped to specific source scripts):

| NUnit Test | Fixture | Status |
|-----------|---------|--------|
| SystemOffNormalPlaceholder | [AlarmsAndConditionsPlaceholderTests](AlarmsAndConditions/AlarmsAndConditionsPlaceholderTests.cs) | ⏭️ |

</details>

<details>
<summary>Alarms & Conditions / A and C Trip — 1 additional ⏭️</summary>

**Additional coverage** (not mapped to specific source scripts):

| NUnit Test | Fixture | Status |
|-----------|---------|--------|
| TripPlaceholder | [AlarmsAndConditionsPlaceholderTests](AlarmsAndConditions/AlarmsAndConditionsPlaceholderTests.cs) | ⏭️ |

</details>

### Alarms & Events

<details>
<summary>Alarms & Events / A and E Wrapper Mapping — 1 additional ⏭️</summary>

**Additional coverage** (not mapped to specific source scripts):

| NUnit Test | Fixture | Status |
|-----------|---------|--------|
| WrapperMappingPlaceholder | [AlarmsAndConditionsPlaceholderTests](AlarmsAndConditions/AlarmsAndConditionsPlaceholderTests.cs) | ⏭️ |

</details>

### Alias Names

<details>
<summary>Alias Names / AliasName Base , 8 additional ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | AliasCatIsSubtypeOfFolderTypeAsync | [AliasNameExtendedTests](AliasName/AliasNameExtendedTests.cs) | ✅ |
| 001 | AliasCatTranslateFromTypesAsync | [AliasNameExtendedTests](AliasName/AliasNameExtendedTests.cs) | ✅ |
| 001 | AliasCatTypeBrowseNameValidAsync | [AliasNameExtendedTests](AliasName/AliasNameExtendedTests.cs) | ✅ |
| 001 | AliasForRefTypeExistsAsync | [AliasNameExtendedTests](AliasName/AliasNameExtendedTests.cs) | ⏭️ |
| 001 | AliasNameTypeBrowseNameValidAsync | [AliasNameExtendedTests](AliasName/AliasNameExtendedTests.cs) | ✅ |
| 001 | AliasNameTypeIsSubtypeOfBaseAsync | [AliasNameExtendedTests](AliasName/AliasNameExtendedTests.cs) | ✅ |
| 001 | HasAliasIsNonHierarchicalAsync | [AliasNameExtendedTests](AliasName/AliasNameExtendedTests.cs) | ⏭️ |
| 001 | VerifyAliasNameCategoryTypeExistsAsync | [AliasNameTests](AliasName/AliasNameTests.cs) | ✅ |
| 001 | VerifyAliasNameTypeExistsAsync | [AliasNameTests](AliasName/AliasNameTests.cs) | ✅ |
| 001 | VerifyAliasForReferenceTypeExistsAsync | [AliasNameTests](AliasName/AliasNameTests.cs) | ✅ |
| 002 | BrowseServerForAliasesAsync | [AliasNameTests](AliasName/AliasNameTests.cs) | ✅ |
| 003 | AliasCatBrowseForComponentsAsync | [AliasNameExtendedTests](AliasName/AliasNameExtendedTests.cs) | ✅ |

**Additional coverage** (not mapped to specific source scripts):

| NUnit Test | Fixture | Status |
|-----------|---------|--------|
| AliasNameFindServersNotRequired | [AliasNameExtendedTests](AliasName/AliasNameExtendedTests.cs) | ⏭️ |
| AliasNameRegisterNotRequired | [AliasNameExtendedTests](AliasName/AliasNameExtendedTests.cs) | ⏭️ |
| AliasNameSecurityAdminNotRequired | [AliasNameExtendedTests](AliasName/AliasNameExtendedTests.cs) | ⏭️ |
| TranslateBrowsePathForNamespaceArray | [AliasNameTests](AliasName/AliasNameTests.cs) | ✅ |
| TranslateBrowsePathForServerState | [AliasNameTests](AliasName/AliasNameTests.cs) | ✅ |
| TranslateBrowsePathForServerStatus | [AliasNameTests](AliasName/AliasNameTests.cs) | ✅ |
| TranslateBrowsePathForWellKnownNode | [AliasNameTests](AliasName/AliasNameTests.cs) | ✅ |
| TranslateBrowsePathInvalidPath | [AliasNameTests](AliasName/AliasNameTests.cs) | ✅ |

</details>

<details>
<summary>Alias Names / AliasName Category Tags ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | VerifyTagVariablesObjectExistsAsync | [AliasNameTests](AliasName/AliasNameTests.cs) | ⏭️ |

</details>

<details>
<summary>Alias Names / AliasName Category Topics ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|

</details>

<details>
<summary>Alias Names / AliasName Hierarchy ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|

</details>

### Attribute Services

<details>
<summary>Attribute Services / Attribute Read ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | AttributeRead001SingleNodeValueAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| 001 | ReadComplexStructureValueAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| 001 | ReadServerStatusStructureAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| 002 | AttributeRead002MultipleNodesValueAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| 003 | AttributeRead008AllAttributesAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| 003 | AttributeRead023ReadAllAttributesOfVariableAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| 004 | AttributeRead011MaxAgeZeroAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| 007 | AttributeRead009TimestampsSourceAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| 008 | AttributeRead010TimestampsServerAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| 009 | AttributeRead029TimestampsNoneAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| 010 | AttributeRead004BrowseNameAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| 012 | AttributeRead022ReadAllAttributesOfObjectAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| 012 | AttributeRead024ReadAllAttributesOfMethodAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| 012 | AttributeRead025ReadAllAttributesOfReferenceTypeAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| 012 | ReadAllAttributesOfViewsFolderAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| 012 | ReadIsAbstractOnObjectTypeAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| 014 | AttributeRead014BatchReadMultipleNodesAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| 017 | AttributeRead003DisplayNameAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| 017 | AttributeRead006NodeClassAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| 017 | AttributeRead007DataTypeAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| 017 | AttributeRead016ReadMinimumSamplingIntervalAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| 017 | AttributeRead017ReadHistorizingAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| 017 | AttributeRead018ReadAccessLevelAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| 017 | AttributeRead019ReadUserAccessLevelAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| 017 | AttributeRead020ReadValueRankAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| 017 | AttributeRead021ReadValueRankArrayAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| 017 | AttributeRead026ReadDescriptionAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| 017 | AttributeRead027ReadWriteMaskAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| 017 | AttributeRead028ReadUserWriteMaskAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| 017 | ReadAccessLevelOfInt32VariableAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| 017 | ReadDataTypeOfInt32VariableAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| 017 | ReadDescriptionOfServerStatusAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| 017 | ReadDisplayNameOfServerAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| 017 | ReadHistorizingAttributeAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| 017 | ReadMinimumSamplingIntervalAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| 018 | AttributeRead031ReadServerStatusCurrentTimeAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| 022 | AttributeRead012ReadArrayValueAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| 023 | AttributeRead032ReadServerArrayAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| 022 | AttributeRead033ReadNamespaceArrayAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| 022 | ReadArrayVariableAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| 024 | AttributeRead013ReadWithIndexRangeAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| 027 | AttributeRead030ReadEventNotifierAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| 037 | ReadWithDefaultBinaryEncodingAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| 037 | ReadWithDefaultJsonEncodingAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| 037 | ReadWithDefaultXmlEncodingAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| Err-001 | AttributeReadErr002InvalidAttributeIdAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| Err-001 | AttributeReadErr003AttributeNotValidForNodeClassAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| Err-001 | AttributeReadErr006ReadValueFromObjectNodeAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| Err-001 | AttributeReadErr007ReadExecutableFromVariableAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| Err-001 | ReadValueOfDataTypeNodeReturnsNullAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| Err-001 | ReadValueOfReferenceTypeNodeReturnsErrorAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| Err-004 | AttributeReadErr001InvalidNodeIdAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| Err-005 | AttributeReadErr004ReadNullNodeIdAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |
| Err-008 | AttributeReadErr005MixOfValidAndInvalidNodesAsync | [AttributeReadTests](AttributeServices/AttributeReadTests.cs) | ✅ |

</details>

<details>
<summary>Attribute Services / Attribute Read Complex ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | ReadArrayOfExtensionObjectsAsync | [AttributeReadComplexTests](AttributeServices/AttributeReadComplexTests.cs) | ⏭️ |
| 001 | ReadDataTypeDefinitionAttributeAsync | [AttributeReadComplexTests](AttributeServices/AttributeReadComplexTests.cs) | ✅ |
| 001 | ReadDataTypeOfVariableAsync | [AttributeReadComplexTests](AttributeServices/AttributeReadComplexTests.cs) | ✅ |
| 001 | ReadExtensionObjectValueAsync | [AttributeReadComplexTests](AttributeServices/AttributeReadComplexTests.cs) | ✅ |
| 001 | ReadNestedStructureValueAsync | [AttributeReadComplexTests](AttributeServices/AttributeReadComplexTests.cs) | ✅ |
| 002 | ReadAllAttributesOfVariableNodeAsync | [AttributeReadComplexTests](AttributeServices/AttributeReadComplexTests.cs) | ✅ |
| 002 | ReadWithDataEncodingDefaultBinaryAsync | [AttributeReadComplexTests](AttributeServices/AttributeReadComplexTests.cs) | ✅ |
| 003 | ReadAllAttributesOfObjectNodeAsync | [AttributeReadComplexTests](AttributeServices/AttributeReadComplexTests.cs) | ✅ |
| 003 | ReadArrayDimensionsOnArrayNodeAsync | [AttributeReadComplexTests](AttributeServices/AttributeReadComplexTests.cs) | ✅ |
| 003 | ReadWithInvalidDataEncodingAsync | [AttributeReadComplexTests](AttributeServices/AttributeReadComplexTests.cs) | ✅ |
| 004 | ReadAccessLevelExAttributeAsync | [AttributeReadComplexTests](AttributeServices/AttributeReadComplexTests.cs) | ✅ |
| 004 | ReadRolePermissionsAttributeAsync | [AttributeReadComplexTests](AttributeServices/AttributeReadComplexTests.cs) | ✅ |
| 004 | ReadUserRolePermissionsAttributeAsync | [AttributeReadComplexTests](AttributeServices/AttributeReadComplexTests.cs) | ✅ |
| 004 | ReadWithDataEncodingDefaultXmlAsync | [AttributeReadComplexTests](AttributeServices/AttributeReadComplexTests.cs) | ✅ |
| 005 | ReadEnumerationValueAsync | [AttributeReadComplexTests](AttributeServices/AttributeReadComplexTests.cs) | ✅ |

</details>

<details>
<summary>Attribute Services / Attribute Write Index ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | ReadBackAfterIndexWriteVerifyOthersPreservedAsync | [AttributeWriteIndexTests](AttributeServices/AttributeWriteIndexTests.cs) | ✅ |
| 001 | ReadBackAfterIndexWriteVerifyTargetChangedAsync | [AttributeWriteIndexTests](AttributeServices/AttributeWriteIndexTests.cs) | ✅ |
| 001 | WriteArrayElementAtIndexTwoAsync | [AttributeWriteIndexTests](AttributeServices/AttributeWriteIndexTests.cs) | ✅ |
| 001 | WriteArrayElementAtIndexZeroAsync | [AttributeWriteIndexTests](AttributeServices/AttributeWriteIndexTests.cs) | ✅ |
| 001 | WriteIndexRangeOnBooleanArrayAsync | [AttributeWriteIndexTests](AttributeServices/AttributeWriteIndexTests.cs) | ✅ |
| 001 | WriteWithIndexRangeOnStringValueAsync | [AttributeWriteIndexTests](AttributeServices/AttributeWriteIndexTests.cs) | ✅ |
| 002 | WriteArraySubsetWithRangeAsync | [AttributeWriteIndexTests](AttributeServices/AttributeWriteIndexTests.cs) | ✅ |
| 003 | WriteWithIndexRangeSubsetVerifyPreservationAsync | [AttributeWriteIndexTests](AttributeServices/AttributeWriteIndexTests.cs) | ✅ |
| 005 | WriteFullArrayWithoutIndexRangeAsync | [AttributeWriteIndexTests](AttributeServices/AttributeWriteIndexTests.cs) | ✅ |
| Err-001 | WriteWithIndexRangeOnScalarNodeFailsAsync | [AttributeWriteIndexTests](AttributeServices/AttributeWriteIndexTests.cs) | ✅ |
| 006 | WriteWithIndexRangeOutOfBoundsAsync | [AttributeWriteIndexTests](AttributeServices/AttributeWriteIndexTests.cs) | ✅ |
| Err-003 | WriteWithInvalidIndexRangeFormatAsync | [AttributeWriteIndexTests](AttributeServices/AttributeWriteIndexTests.cs) | ✅ |

</details>

<details>
<summary>Attribute Services / Attribute Write StatusCode & TimeStamp ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|

</details>

<details>
<summary>Attribute Services / Attribute Write Values ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | AttributeWrite001WriteSingleValueAsync | [AttributeWriteTests](AttributeServices/AttributeWriteTests.cs) | ✅ |
| 001 | AttributeWrite003WriteBooleanAsync | [AttributeWriteTests](AttributeServices/AttributeWriteTests.cs) | ✅ |
| 001 | AttributeWrite004WriteInt32Async | [AttributeWriteTests](AttributeServices/AttributeWriteTests.cs) | ✅ |
| 001 | AttributeWrite005WriteDoubleAsync | [AttributeWriteTests](AttributeServices/AttributeWriteTests.cs) | ✅ |
| 001 | AttributeWrite006WriteStringAsync | [AttributeWriteTests](AttributeServices/AttributeWriteTests.cs) | ✅ |
| 001 | AttributeWrite007WriteDateTimeAsync | [AttributeWriteTests](AttributeServices/AttributeWriteTests.cs) | ✅ |
| 001 | AttributeWrite009WriteFloatAsync | [AttributeWriteTests](AttributeServices/AttributeWriteTests.cs) | ✅ |
| 001 | AttributeWrite010WriteSByteAsync | [AttributeWriteTests](AttributeServices/AttributeWriteTests.cs) | ✅ |
| 001 | AttributeWrite011WriteByteAsync | [AttributeWriteTests](AttributeServices/AttributeWriteTests.cs) | ✅ |
| 001 | AttributeWrite012WriteInt16Async | [AttributeWriteTests](AttributeServices/AttributeWriteTests.cs) | ✅ |
| 001 | AttributeWrite013WriteUInt16Async | [AttributeWriteTests](AttributeServices/AttributeWriteTests.cs) | ✅ |
| 001 | AttributeWrite014WriteInt64Async | [AttributeWriteTests](AttributeServices/AttributeWriteTests.cs) | ✅ |
| 001 | AttributeWrite015WriteUInt64Async | [AttributeWriteTests](AttributeServices/AttributeWriteTests.cs) | ✅ |
| 001 | AttributeWrite016WriteGuidAsync | [AttributeWriteTests](AttributeServices/AttributeWriteTests.cs) | ✅ |
| 002 | AttributeWrite002WriteMultipleValuesAsync | [AttributeWriteTests](AttributeServices/AttributeWriteTests.cs) | ✅ |
| 003 | AttributeWrite017WriteAndReadBackMultipleTypesAsync | [AttributeWriteTests](AttributeServices/AttributeWriteTests.cs) | ✅ |
| 004 | AttributeWriteReadBackTimestampAsync | [AttributeWriteTests](AttributeServices/AttributeWriteTests.cs) | ✅ |
| 004 | AttributeWriteStatusCodeOverrideToUncertainAsync | [AttributeWriteTests](AttributeServices/AttributeWriteTests.cs) | ✅ |
| 004 | AttributeWriteValueWithMinDateTimeAsync | [AttributeWriteTests](AttributeServices/AttributeWriteTests.cs) | ✅ |
| 004 | AttributeWriteWithBothTimestampsAsync | [AttributeWriteTests](AttributeServices/AttributeWriteTests.cs) | ✅ |
| 004 | AttributeWriteWithServerTimestampAsync | [AttributeWriteTests](AttributeServices/AttributeWriteTests.cs) | ✅ |
| 004 | AttributeWriteWithSourceTimestampAsync | [AttributeWriteTests](AttributeServices/AttributeWriteTests.cs) | ✅ |
| 004 | AttributeWriteWithSourceTimestampInFutureAsync | [AttributeWriteTests](AttributeServices/AttributeWriteTests.cs) | ✅ |
| 004 | AttributeWriteWithSourceTimestampInPastAsync | [AttributeWriteTests](AttributeServices/AttributeWriteTests.cs) | ✅ |
| 004 | AttributeWriteWithStatusCodeBadAsync | [AttributeWriteTests](AttributeServices/AttributeWriteTests.cs) | ✅ |
| 004 | AttributeWriteWithStatusCodeGoodAsync | [AttributeWriteTests](AttributeServices/AttributeWriteTests.cs) | ✅ |
| 007 | AttributeWrite008WriteByteStringAsync | [AttributeWriteTests](AttributeServices/AttributeWriteTests.cs) | ✅ |
| 018 | AttributeWrite018WriteArrayValueAsync | [AttributeWriteTests](AttributeServices/AttributeWriteTests.cs) | ✅ |
| Err-002 | AttributeWriteErr001WriteToInvalidNodeIdAsync | [AttributeWriteTests](AttributeServices/AttributeWriteTests.cs) | ✅ |
| Err-003 | AttributeWriteErr003WriteBadNodeIdUnknownAsync | [AttributeWriteTests](AttributeServices/AttributeWriteTests.cs) | ✅ |
| Err-008 | AttributeWriteErr002WriteWrongDataTypeAsync | [AttributeWriteTests](AttributeServices/AttributeWriteTests.cs) | ✅ |

</details>

### Auditing

<details>
<summary>Auditing / Auditing Connections ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 006 | AuditActivateSessionEventTypeExistsAsync | [AuditingTests](Auditing/AuditingTests.cs) | ✅ |
| 006 | AuditActivateSessionEventTypeHasPropertiesAsync | [AuditingExtendedTests](Auditing/AuditingExtendedTests.cs) | ✅ |
| 006 | AuditActivateSessionHasSecureChannelIdAsync | [AuditingConnectionTests](Auditing/AuditingConnectionTests.cs) | ✅ |
| 006 | AuditActivateSessionHasSoftwareCertificatesAsync | [AuditingConnectionTests](Auditing/AuditingConnectionTests.cs) | ⏭️ |
| 006 | AuditActivateSessionHasUserIdentityTokenAsync | [AuditingConnectionTests](Auditing/AuditingConnectionTests.cs) | ✅ |
| 006 | AuditCertificateDataMismatchExistsAsync | [AuditingConnectionTests](Auditing/AuditingConnectionTests.cs) | ✅ |
| 006 | AuditCertificateEventTypeExistsAsync | [AuditingConnectionTests](Auditing/AuditingConnectionTests.cs) | ✅ |
| 006 | AuditCertificateExpiredExistsAsync | [AuditingConnectionTests](Auditing/AuditingConnectionTests.cs) | ✅ |
| 006 | AuditCertificateInvalidExistsAsync | [AuditingConnectionTests](Auditing/AuditingConnectionTests.cs) | ✅ |
| 006 | AuditCertificateRevokedExistsAsync | [AuditingConnectionTests](Auditing/AuditingConnectionTests.cs) | ✅ |
| 006 | AuditCertificateUntrustedExistsAsync | [AuditingConnectionTests](Auditing/AuditingConnectionTests.cs) | ✅ |
| 006 | AuditCreateSessionEventTypeExistsAsync | [AuditingOperationTests](Auditing/AuditingOperationTests.cs) | ✅ |
| 006 | AuditCreateSessionEventTypeHasMandatoryPropertiesAsync | [AuditingExtendedTests](Auditing/AuditingExtendedTests.cs) | ✅ |
| 006 | AuditCreateSessionHasClientCertificateAsync | [AuditingConnectionTests](Auditing/AuditingConnectionTests.cs) | ✅ |
| 006 | AuditCreateSessionHasClientCertificateThumbprintAsync | [AuditingConnectionTests](Auditing/AuditingConnectionTests.cs) | ✅ |
| 006 | AuditCreateSessionHasRevisedSessionTimeoutAsync | [AuditingConnectionTests](Auditing/AuditingConnectionTests.cs) | ✅ |
| 006 | AuditCreateSessionHasSecureChannelIdAsync | [AuditingConnectionTests](Auditing/AuditingConnectionTests.cs) | ✅ |
| 006 | AuditCreateSessionIsSubtypeOfAuditSessionAsync | [AuditingConnectionTests](Auditing/AuditingConnectionTests.cs) | ✅ |
| 006 | AuditEventTypeHasActionTimeStampAsync | [AuditingConnectionTests](Auditing/AuditingConnectionTests.cs) | ✅ |
| 006 | AuditEventTypeHasClientAuditEntryIdAsync | [AuditingConnectionTests](Auditing/AuditingConnectionTests.cs) | ✅ |
| 006 | AuditEventTypeHasClientUserIdAsync | [AuditingConnectionTests](Auditing/AuditingConnectionTests.cs) | ✅ |
| 006 | AuditEventTypeHasServerIdAsync | [AuditingConnectionTests](Auditing/AuditingConnectionTests.cs) | ✅ |
| 006 | AuditEventTypeHasStatusAsync | [AuditingConnectionTests](Auditing/AuditingConnectionTests.cs) | ✅ |
| 006 | AuditOpenSecureChannelHasClientCertThumbprintAsync | [AuditingConnectionTests](Auditing/AuditingConnectionTests.cs) | ✅ |
| 006 | AuditOpenSecureChannelHasClientCertificateAsync | [AuditingConnectionTests](Auditing/AuditingConnectionTests.cs) | ✅ |
| 006 | AuditOpenSecureChannelHasRequestTypeAsync | [AuditingConnectionTests](Auditing/AuditingConnectionTests.cs) | ✅ |
| 006 | AuditOpenSecureChannelHasRequestedLifetimeAsync | [AuditingConnectionTests](Auditing/AuditingConnectionTests.cs) | ✅ |
| 006 | AuditOpenSecureChannelHasSecurityModeAsync | [AuditingConnectionTests](Auditing/AuditingConnectionTests.cs) | ✅ |
| 006 | AuditOpenSecureChannelHasSecurityPolicyUriAsync | [AuditingConnectionTests](Auditing/AuditingConnectionTests.cs) | ✅ |
| 006 | AuditSessionEventTypeExistsAsync | [AuditingTests](Auditing/AuditingTests.cs) | ✅ |
| 006 | AuditUpdateMethodHasInputArgumentsAsync | [AuditingConnectionTests](Auditing/AuditingConnectionTests.cs) | ✅ |
| 006 | AuditUpdateMethodHasMethodIdAsync | [AuditingConnectionTests](Auditing/AuditingConnectionTests.cs) | ✅ |
| 006 | AuditUrlMismatchEventTypeExistsAsync | [AuditingTests](Auditing/AuditingTests.cs) | ✅ |
| 006 | AuditWriteUpdateHasAttributeIdAsync | [AuditingConnectionTests](Auditing/AuditingConnectionTests.cs) | ✅ |
| 006 | AuditWriteUpdateHasIndexRangeAsync | [AuditingConnectionTests](Auditing/AuditingConnectionTests.cs) | ✅ |
| 006 | AuditWriteUpdateHasNewValueAsync | [AuditingConnectionTests](Auditing/AuditingConnectionTests.cs) | ✅ |
| 006 | AuditWriteUpdateHasOldValueAsync | [AuditingConnectionTests](Auditing/AuditingConnectionTests.cs) | ✅ |
| 006 | AuditWriteUpdateIsSubtypeOfAuditUpdateAsync | [AuditingConnectionTests](Auditing/AuditingConnectionTests.cs) | ✅ |
| 006 | SessionDiagnosticsArrayIsReadableAsync | [AuditingOperationTests](Auditing/AuditingOperationTests.cs) | ⏭️ |
| 007 | AuditEventAfterCreateSessionIsIgnoredAsync | [AuditingOperationTests](Auditing/AuditingOperationTests.cs) | ⏭️ |
| 011 | AuditEventAfterActivateSessionIsIgnoredAsync | [AuditingOperationTests](Auditing/AuditingOperationTests.cs) | ⏭️ |
| 020 | AuditEventAfterCloseSessionIsIgnoredAsync | [AuditingOperationTests](Auditing/AuditingOperationTests.cs) | ⏭️ |

</details>

<details>
<summary>Auditing / Auditing History Services ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | AuditHistoryEventUpdateEventTypeExistsAsync | [AuditingExtendedTests](Auditing/AuditingExtendedTests.cs) | ✅ |
| 001 | AuditHistoryEventUpdateHasPropertyAsync | [AuditingOperationTests](Auditing/AuditingOperationTests.cs) | ✅ |
| 001 | AuditHistoryUpdateEventTypeExistsAsync | [AuditingOperationTests](Auditing/AuditingOperationTests.cs) | ✅ |
| 001 | AuditHistoryValueUpdateEventTypeExistsAsync | [AuditingOperationTests](Auditing/AuditingOperationTests.cs) | ✅ |
| 002 | AuditHistoryDeleteEventTypeExistsOrFailAsync | [AuditingOperationTests](Auditing/AuditingOperationTests.cs) | ✅ |
| 002 | AuditHistoryRawModifyDeleteExistsOrFailAsync | [AuditingOperationTests](Auditing/AuditingOperationTests.cs) | ✅ |

</details>

<details>
<summary>Auditing / Auditing Method ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | AuditConditionAcknowledgeEventTypeExistsAsync | [AuditingExtendedTests](Auditing/AuditingExtendedTests.cs) | ✅ |
| 001 | AuditConditionCommentEventTypeExistsAsync | [AuditingExtendedTests](Auditing/AuditingExtendedTests.cs) | ✅ |
| 001 | AuditConditionCommentHasCommentAsync | [AuditingOperationTests](Auditing/AuditingOperationTests.cs) | ✅ |
| 001 | AuditConditionEnableEventTypeExistsAsync | [AuditingExtendedTests](Auditing/AuditingExtendedTests.cs) | ✅ |
| 001 | AuditConditionEventTypeExistsAsync | [AuditingTests](Auditing/AuditingTests.cs) | ✅ |
| 001 | AuditConditionRespondExistsOrFailAsync | [AuditingOperationTests](Auditing/AuditingOperationTests.cs) | ✅ |
| 001 | AuditConditionShelvingExistsOrFailAsync | [AuditingOperationTests](Auditing/AuditingOperationTests.cs) | ✅ |
| 001 | AuditUpdateMethodEventTypeExistsAsync | [AuditingTests](Auditing/AuditingTests.cs) | ✅ |

</details>

<details>
<summary>Auditing / Auditing NodeManagement ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | AuditAddNodesEventTypeExistsAsync | [AuditingExtendedTests](Auditing/AuditingExtendedTests.cs) | ✅ |
| 001 | AuditAddNodesHasNodesToAddAsync | [AuditingOperationTests](Auditing/AuditingOperationTests.cs) | ✅ |
| 001 | AuditNodeManagementEventTypeExistsAsync | [AuditingTests](Auditing/AuditingTests.cs) | ✅ |
| 003 | AuditAddReferencesEventTypeExistsAsync | [AuditingExtendedTests](Auditing/AuditingExtendedTests.cs) | ✅ |
| 003 | AuditAddReferencesHasReferencesToAddAsync | [AuditingOperationTests](Auditing/AuditingOperationTests.cs) | ✅ |
| 007 | AuditDeleteNodesEventTypeExistsAsync | [AuditingExtendedTests](Auditing/AuditingExtendedTests.cs) | ✅ |
| 007 | AuditDeleteNodesHasNodesToDeleteAsync | [AuditingOperationTests](Auditing/AuditingOperationTests.cs) | ✅ |
| 009 | AuditDeleteReferencesEventTypeExistsAsync | [AuditingExtendedTests](Auditing/AuditingExtendedTests.cs) | ✅ |
| 009 | AuditDeleteReferencesHasReferencesToDeleteAsync | [AuditingOperationTests](Auditing/AuditingOperationTests.cs) | ✅ |

</details>

<details>
<summary>Auditing / Auditing Secure Communication ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 004 | AuditCancelEventTypeExistsAsync | [AuditingTests](Auditing/AuditingTests.cs) | ✅ |
| 004 | AuditCancelHasRequestHandleAsync | [AuditingOperationTests](Auditing/AuditingOperationTests.cs) | ✅ |
| 004 | AuditChannelEventTypeExistsAsync | [AuditingTests](Auditing/AuditingTests.cs) | ✅ |
| 004 | AuditEventTypeExistsAsync | [AuditingTests](Auditing/AuditingTests.cs) | ✅ |
| 004 | AuditEventTypeExistsInAddressSpaceAsync | [AuditingOperationTests](Auditing/AuditingOperationTests.cs) | ✅ |
| 004 | AuditOpenSecureChannelEventTypeExistsAsync | [AuditingTests](Auditing/AuditingTests.cs) | ✅ |
| 004 | AuditSecurityEventTypeExistsAsync | [AuditingTests](Auditing/AuditingTests.cs) | ✅ |
| 004 | BaseEventTypeExistsAsync | [AuditingTests](Auditing/AuditingTests.cs) | ✅ |
| 004 | BaseEventTypeHasEventIdAsync | [AuditingOperationTests](Auditing/AuditingOperationTests.cs) | ✅ |
| 004 | BaseEventTypeHasSourceNodeAsync | [AuditingOperationTests](Auditing/AuditingOperationTests.cs) | ✅ |
| 004 | BaseEventTypeHasTimeAsync | [AuditingOperationTests](Auditing/AuditingOperationTests.cs) | ✅ |
| 004 | ProgramTransitionAuditEventTypeExistsOrFailAsync | [AuditingOperationTests](Auditing/AuditingOperationTests.cs) | ✅ |
| 004 | ReadServerAuditingPropertyAsync | [AuditingTests](Auditing/AuditingTests.cs) | ✅ |
| 004 | ServerAuditingDataTypeIsBooleanAsync | [AuditingOperationTests](Auditing/AuditingOperationTests.cs) | ✅ |
| 004 | ServerAuditingIsBooleanAsync | [AuditingTests](Auditing/AuditingTests.cs) | ✅ |
| 004 | ServerAuditingPropertyIsBoolAsync | [AuditingOperationTests](Auditing/AuditingOperationTests.cs) | ✅ |
| 004 | ServerCurrentTimeIsRecentAsync | [AuditingOperationTests](Auditing/AuditingOperationTests.cs) | ✅ |
| 004 | ServerEventNotifierHasSubscribeBitAsync | [AuditingTests](Auditing/AuditingTests.cs) | ✅ |
| 004 | ServerObjectEventNotifierBitIsSetAsync | [AuditingOperationTests](Auditing/AuditingOperationTests.cs) | ✅ |
| 004 | ServerObjectHasEventNotifierAsync | [AuditingTests](Auditing/AuditingTests.cs) | ✅ |
| 004 | ServerObjectSupportsEventsAsync | [AuditingTests](Auditing/AuditingTests.cs) | ✅ |
| 004 | VerifyAuditEventSourceIsServerAsync | [AuditingTests](Auditing/AuditingTests.cs) | ✅ |

</details>

<details>
<summary>Auditing / Auditing Write ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | AuditWriteUpdateEventTypeExistsAsync | [AuditingTests](Auditing/AuditingTests.cs) | ✅ |

</details>

### Best Practices

<details>
<summary>Best Practices / Best Practice - Administrative Access ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | ReadServiceLevelAsync | [MiscellaneousTests](Miscellaneous/MiscellaneousTests.cs) | ✅ |
| 001 | VerifyServerStateIsRunningAsync | [MiscellaneousTests](Miscellaneous/MiscellaneousTests.cs) | ✅ |

</details>

<details>
<summary>Best Practices / Best Practice - Strict Message Handling ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | ReadInvalidAttributeIdReturnsBadAttributeIdInvalidAsync | [MiscellaneousTests](Miscellaneous/MiscellaneousTests.cs) | ✅ |
| 001 | ReadNamespaceArrayAsync | [MiscellaneousTests](Miscellaneous/MiscellaneousTests.cs) | ✅ |
| 001 | ReadNonExistentNodeReturnsBadNodeIdUnknownAsync | [MiscellaneousTests](Miscellaneous/MiscellaneousTests.cs) | ✅ |
| 001 | ReadServerArrayAsync | [MiscellaneousTests](Miscellaneous/MiscellaneousTests.cs) | ✅ |
| 001 | ReadWithMaxAgeMaxReturnsCacheAsync | [MiscellaneousExtendedTests](Miscellaneous/MiscellaneousExtendedTests.cs) | ✅ |
| 001 | ReadWithMaxAgeZeroReturnsDeviceValueAsync | [MiscellaneousExtendedTests](Miscellaneous/MiscellaneousExtendedTests.cs) | ✅ |
| 001 | ResponseHeaderHasTimestampAsync | [MiscellaneousTests](Miscellaneous/MiscellaneousTests.cs) | ✅ |
| 001 | ServerTimestampsAreUtcAsync | [MiscellaneousTests](Miscellaneous/MiscellaneousTests.cs) | ✅ |
| 001 | StatusCodeBadNodeIdUnknownIsCorrect | [MiscellaneousTests](Miscellaneous/MiscellaneousTests.cs) | ✅ |
| 001 | StatusCodeGoodIsZero | [MiscellaneousTests](Miscellaneous/MiscellaneousTests.cs) | ✅ |
| 001 | VerifyLocaleIdArrayAsync | [MiscellaneousTests](Miscellaneous/MiscellaneousTests.cs) | ✅ |
| 001 | VerifyNoDiagnosticsWhenNotRequestedAsync | [MiscellaneousExtendedTests](Miscellaneous/MiscellaneousExtendedTests.cs) | ✅ |
| 001 | VerifyResponseRequestHandleEchoedAsync | [MiscellaneousExtendedTests](Miscellaneous/MiscellaneousExtendedTests.cs) | ✅ |
| 001 | VerifyServerCurrentTimeUpdatesAsync | [MiscellaneousTests](Miscellaneous/MiscellaneousTests.cs) | ✅ |
| 001 | WriteAndReadBackValueAsync | [MiscellaneousTests](Miscellaneous/MiscellaneousTests.cs) | ✅ |
| 003 | BrowseManyNodesInSingleCallAsync | [MiscellaneousTests](Miscellaneous/MiscellaneousTests.cs) | ✅ |
| 003 | ReadManyNodesInSingleCallAsync | [MiscellaneousTests](Miscellaneous/MiscellaneousTests.cs) | ✅ |
| 003 | VerifyMaxNodesPerReadAsync | [MiscellaneousTests](Miscellaneous/MiscellaneousTests.cs) | ✅ |
| 003 | WriteManyNodesInSingleCallAsync | [MiscellaneousTests](Miscellaneous/MiscellaneousTests.cs) | ✅ |

</details>

<details>
<summary>Best Practices / Best Practice - Timeouts , 1 additional ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | RapidConnectDisconnectAsync | [MiscellaneousTests](Miscellaneous/MiscellaneousTests.cs) | ✅ |
| 002 | ConcurrentSessionsAsync | [MiscellaneousTests](Miscellaneous/MiscellaneousTests.cs) | ✅ |

**Additional coverage** (not mapped to specific source scripts):

| NUnit Test | Fixture | Status |
|-----------|---------|--------|
| VerifyServerHandlesReadWithinAcceptableTime | [MiscellaneousExtendedTests](Miscellaneous/MiscellaneousExtendedTests.cs) | ✅ |

</details>

### Data Access

<details>
<summary>Data Access / Data Access Analog , 8 additional ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 000 | ReadAnalogItemHasTypeDefinitionAsync | [DataAccessAnalogTests](DataAccess/DataAccessAnalogTests.cs) | ✅ |
| 001 | ReadAnalogItemDoubleValueAsync | [DataAccessAnalogTests](DataAccess/DataAccessAnalogTests.cs) | ✅ |
| 002 | ReadAnalogItemArrayValueAsync | [DataAccessAnalogTests](DataAccess/DataAccessAnalogTests.cs) | ✅ |
| 002 | ReadAnalogItemEngineeringUnitsAsync | [DataAccessAnalogTests](DataAccess/DataAccessAnalogTests.cs) | ✅ |
| 002 | ReadAnalogItemInt32ValueAsync | [DataAccessAnalogTests](DataAccess/DataAccessAnalogTests.cs) | ✅ |
| 003 | ReadAnalogItemEURangeAsync | [DataAccessAnalogTests](DataAccess/DataAccessAnalogTests.cs) | ✅ |
| 006 | WriteAnalogItemWithinEURangeSucceedsAsync | [DataAccessAnalogTests](DataAccess/DataAccessAnalogTests.cs) | ✅ |

**Additional coverage** (not mapped to specific source scripts):

| NUnit Test | Fixture | Status |
|-----------|---------|--------|
| ReadAnalogItemDefinitionProperty | [DataAccessAnalogTests](DataAccess/DataAccessAnalogTests.cs) | ⏭️ |
| ReadMultiStateDiscreteEnumStrings | [DataAccessAnalogTests](DataAccess/DataAccessAnalogTests.cs) | ✅ |
| ReadMultiStateDiscreteValue | [DataAccessAnalogTests](DataAccess/DataAccessAnalogTests.cs) | ✅ |
| ReadTwoStateDiscreteFalseState | [DataAccessAnalogTests](DataAccess/DataAccessAnalogTests.cs) | ✅ |
| ReadTwoStateDiscreteTrueState | [DataAccessAnalogTests](DataAccess/DataAccessAnalogTests.cs) | ✅ |
| ReadTwoStateDiscreteValue | [DataAccessAnalogTests](DataAccess/DataAccessAnalogTests.cs) | ✅ |
| WriteMultiStateDiscreteValidIndex | [DataAccessAnalogTests](DataAccess/DataAccessAnalogTests.cs) | ✅ |
| WriteTwoStateDiscreteToggle | [DataAccessAnalogTests](DataAccess/DataAccessAnalogTests.cs) | ✅ |

</details>

<details>
<summary>Data Access / Data Access DataItems ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 000 | DataItems000BrowseDataItemTypeDefinitionAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| 001 | DataItems001TranslateBrowsePathForInt32Async | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| 002 | DataItems002TranslateBrowsePathForDoubleAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| 003 | DataItems003ReadValueAttributeAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| 003 | ReadAllScalarStaticNodesSucceedsAsync | [DataAccessTests](DataAccess/DataAccessTests.cs) | ✅ |
| 003 | ReadBooleanArrayValueAsync | [DataAccessTests](DataAccess/DataAccessTests.cs) | ✅ |
| 003 | ReadInt32ArrayValueAsync | [DataAccessTests](DataAccess/DataAccessTests.cs) | ✅ |
| 003 | ReadScalarDoubleValueAsync | [DataAccessTests](DataAccess/DataAccessTests.cs) | ✅ |
| 003 | ReadScalarInt32ValueAsync | [DataAccessTests](DataAccess/DataAccessTests.cs) | ✅ |
| 003 | ReadScalarStringValueAsync | [DataAccessTests](DataAccess/DataAccessTests.cs) | ✅ |
| 004 | DataItems004ReadDisplayNameAttributeAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| 004 | WriteAndReadBackScalarDoubleAsync | [DataAccessTests](DataAccess/DataAccessTests.cs) | ✅ |
| 004 | WriteAndReadBackScalarInt32Async | [DataAccessTests](DataAccess/DataAccessTests.cs) | ✅ |
| 004 | WriteAndReadBackStringAsync | [DataAccessTests](DataAccess/DataAccessTests.cs) | ✅ |
| 005 | DataItems005ReadBrowseNameAttributeAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| 006 | DataItems006ReadNodeClassAttributeAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| 007 | DataItems007ReadDataTypeAttributeAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| 008 | DataItems008ReadAccessLevelAttributeAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| 009 | DataItems009ReadValueRankAttributeAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| 010 | DataItems010WriteInt32ValueAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| 011 | DataItems011WriteDoubleValueAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| 012 | DataItems012WriteStringValueAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| 013 | DataItems013WriteBooleanValueAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| 014 | DataItems014BatchReadMultipleNodesAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| 015 | DataItems015BatchWriteMultipleNodesAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| 016 | DataItems016ReadArrayWithIndexRangeAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| 017 | DataItems017WriteArrayWithIndexRangeAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| 018 | DataItems018ReadDefinitionPropertyAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ⏭️ |
| 019 | DataItems019ReadValuePrecisionPropertyAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ⏭️ |
| 020 | DataItems020ReadWithDifferentTimestampsAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| Err-001 | DataItemsErr001WriteWrongTypeAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| Err-002 | DataItemsErr002ReadInvalidNodeAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |

</details>

<details>
<summary>Data Access / Data Access MultiState , 5 additional ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 000 | VerifyAnalogItemTypeHasTypeDefinitionAsync | [DataAccessMultiStateTests](DataAccess/DataAccessMultiStateTests.cs) | ✅ |
| 000 | VerifyDataAccessVariableTypeExistsAsync | [DataAccessMultiStateTests](DataAccess/DataAccessMultiStateTests.cs) | ✅ |
| 000 | VerifyMultiStateDiscreteHasTypeDefinitionAsync | [DataAccessMultiStateTests](DataAccess/DataAccessMultiStateTests.cs) | ✅ |
| 001 | ReadMultiStateDiscreteValueAsync | [DataAccessMultiStateTests](DataAccess/DataAccessMultiStateTests.cs) | ✅ |
| 003 | ReadMultiStateValueAfterWriteAsync | [DataAccessMultiStateTests](DataAccess/DataAccessMultiStateTests.cs) | ✅ |
| 003 | WriteValidMultiStateIndexAsync | [DataAccessMultiStateTests](DataAccess/DataAccessMultiStateTests.cs) | ✅ |
| 006 | ReadMultiStateDiscreteEnumStringsAsync | [DataAccessMultiStateTests](DataAccess/DataAccessMultiStateTests.cs) | ✅ |

**Additional coverage** (not mapped to specific source scripts):

| NUnit Test | Fixture | Status |
|-----------|---------|--------|
| ReadAnalogItemDoubleEURangeHighGreaterThanLow | [DataAccessMultiStateTests](DataAccess/DataAccessMultiStateTests.cs) | ✅ |
| ReadAnalogItemInstrumentRange | [DataAccessMultiStateTests](DataAccess/DataAccessMultiStateTests.cs) | ✅ |
| ReadDataAccessTwoStateDiscreteValue | [DataAccessMultiStateTests](DataAccess/DataAccessMultiStateTests.cs) | ✅ |
| ReadTwoStateDiscreteFalseState | [DataAccessMultiStateTests](DataAccess/DataAccessMultiStateTests.cs) | ✅ |
| WriteAndReadBackAnalogItemInt32 | [DataAccessMultiStateTests](DataAccess/DataAccessMultiStateTests.cs) | ✅ |

</details>

<details>
<summary>Data Access / Data Access PercentDeadBand ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | PercentDeadBand001ReadEuRangeAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| 002 | PercentDeadBand002ReadInstrumentRangeAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| 003 | PercentDeadBand003ReadEngineeringUnitsAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| 004 | PercentDeadBand004CreateSubscriptionForAnalogAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| 005 | PercentDeadBand005MonitorWithAbsoluteDeadbandAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| 006 | PercentDeadBand006MonitorWithPercentDeadbandAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| 007 | PercentDeadBand007PercentDeadbandZeroAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| 008 | PercentDeadBand008PercentDeadbandHundredAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| 009 | PercentDeadBand009ModifyMonitoredItemDeadbandAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| 010 | PercentDeadBand010DeleteMonitoredItemAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| 011 | PercentDeadBand011StatusChangeTriggerAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| 012 | PercentDeadBand012StatusValueTimestampTriggerAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| 013 | PercentDeadBand013MultipleMonitoredItemsAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| 014 | PercentDeadBand014AbsoluteDeadbandSmallValueAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| 015 | PercentDeadBand015MonitorWithNoDeadbandAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| 016 | PercentDeadBand016ModifySubscriptionIntervalAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| 017 | PercentDeadBand017SetPublishingModeDisableAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| 018 | PercentDeadBand018DeadbandWithQueueSizeOneAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| Err-001 | PercentDeadBandErr001NegativeDeadbandValueAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| Err-002 | PercentDeadBandErr002PercentDeadbandExceedsHundredAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| Err-003 | PercentDeadBandErr003InvalidDeadbandTypeAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| Err-004 | PercentDeadBandErr004DeadbandOnNonAnalogNodeAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| Err-005 | PercentDeadBandErr005MonitorInvalidNodeAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |

</details>

<details>
<summary>Data Access / Data Access Semantic Changes — 12 additional ✅</summary>

**Additional coverage** (not mapped to specific source scripts):

| NUnit Test | Fixture | Status |
|-----------|---------|--------|
| AnalogItemHasEngineeringUnits | [DataAccessSemanticTests](DataAccess/DataAccessSemanticTests.cs) | ✅ |
| AnalogItemTypeDefinitionHasEURange | [DataAccessSemanticTests](DataAccess/DataAccessSemanticTests.cs) | ✅ |
| AnalogTypeHasTypeDefinition | [DataAccessSemanticTests](DataAccess/DataAccessSemanticTests.cs) | ✅ |
| DataItemHasDefinitionProperty | [DataAccessSemanticTests](DataAccess/DataAccessSemanticTests.cs) | ✅ |
| DataItemHasTypeDefinition | [DataAccessSemanticTests](DataAccess/DataAccessSemanticTests.cs) | ✅ |
| ReadAnalogArrayItemValue | [DataAccessSemanticTests](DataAccess/DataAccessSemanticTests.cs) | ✅ |
| ReadDataItemValue | [DataAccessSemanticTests](DataAccess/DataAccessSemanticTests.cs) | ✅ |
| ReadMultiStateValueDiscreteEnumValues | [DataAccessSemanticTests](DataAccess/DataAccessSemanticTests.cs) | ✅ |
| WriteAnalogAndReadBack | [DataAccessSemanticTests](DataAccess/DataAccessSemanticTests.cs) | ✅ |
| WriteInvalidMultiStateValue | [DataAccessSemanticTests](DataAccess/DataAccessSemanticTests.cs) | ✅ |
| WriteOutsideEURangeHandledGracefully | [DataAccessSemanticTests](DataAccess/DataAccessSemanticTests.cs) | ✅ |
| WriteValidMultiStateValue | [DataAccessSemanticTests](DataAccess/DataAccessSemanticTests.cs) | ✅ |

</details>

<details>
<summary>Data Access / Data Access TwoState ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 000 | TwoState000ReadBooleanValueAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| 001 | TwoState001ReadBooleanDisplayNameAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| 001 | TwoStateDiscreteIsSubtypeOfDiscreteItemAsync | [BaseInfoSingleCuTests](InformationModel/BaseInfoSingleCuTests.cs) | ✅ |
| 001 | TwoStateDiscreteTypeExistsAsync | [BaseInfoSingleCuTests](InformationModel/BaseInfoSingleCuTests.cs) | ✅ |
| 002 | TwoState002ReadBooleanBrowseNameAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| 003 | TwoState003ReadBooleanNodeClassAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| 004 | TwoState004ReadBooleanDataTypeAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| 005 | TwoState005WriteTrueValueAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| 006 | TwoState006WriteFalseValueAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| 007 | TwoState007ToggleBooleanValueAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| 008 | TwoState008ReadAccessLevelAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| 009 | TwoState009ReadValueRankAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| 6.6-001 | TwoState66001CreateSubscriptionForBooleanAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| 6.6-002 | TwoState66002MonitorBooleanValueChangesAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| 6.6-003 | TwoState66003MonitorWithStatusTriggerAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| 6.6-004 | TwoState66004MonitorWithStatusValueTriggerAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| 6.6-005 | TwoState66005ModifyMonitoredItemSamplingIntervalAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| 6.6-006 | TwoState66006DeleteMonitoredItemAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| 6.6-007 | TwoState66007DeleteSubscriptionAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| Err-001 | TwoStateErr001WriteWrongTypeAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| Err-002 | TwoStateErr002ReadInvalidNodeAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| Err-003 | TwoStateErr003WriteInvalidNodeAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| Err-004 | TwoStateErr004WriteInvalidAttributeAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| Err-005 | TwoStateErr005ReadInvalidAttributeAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| Err-006 | TwoStateErr006MonitorInvalidNodeAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| Err-007 | TwoStateErr007MonitorInvalidAttributeAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| Err-008 | TwoStateErr008WriteInt32ToBooleanNodeAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| Err-009 | TwoStateErr009WriteDoubleToBooleanNodeAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| Err-010 | TwoStateErr010BatchReadWithOneInvalidAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |
| Err-011 | TwoStateErr011BatchWriteWithOneInvalidAsync | [DataAccessDepthTests](DataAccess/DataAccessDepthTests.cs) | ✅ |

</details>

### Discovery Services

<details>
<summary>Discovery Services / Discovery Find Servers Filter ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | FindServersFilteredByServerUriAsync | [DiscoveryFindServersFilterTests](DiscoveryServices/DiscoveryFindServersFilterTests.cs) | ✅ |
| 002 | FindServersFilteredByMultipleServerUrisAsync | [DiscoveryFindServersFilterTests](DiscoveryServices/DiscoveryFindServersFilterTests.cs) | ✅ |
| 003 | FindServersWithMixedSupportedAndUnsupportedLocalesAsync | [DiscoveryFindServersFilterTests](DiscoveryServices/DiscoveryFindServersFilterTests.cs) | ✅ |
| 004 | FindServersFilteredByUnknownServerUriAsync | [DiscoveryFindServersFilterTests](DiscoveryServices/DiscoveryFindServersFilterTests.cs) | ✅ |
| 005 | FindServersWithUnsupportedLocaleIdAsync | [DiscoveryFindServersFilterTests](DiscoveryServices/DiscoveryFindServersFilterTests.cs) | ✅ |
| 006 | FindServersWithSupportedLocalesAsync | [DiscoveryFindServersFilterTests](DiscoveryServices/DiscoveryFindServersFilterTests.cs) | ✅ |
| 007 | FindServersRepeatedHundredTimesWithinTenSecondsAsync | [DiscoveryFindServersFilterTests](DiscoveryServices/DiscoveryFindServersFilterTests.cs) | ✅ |
| 008 | FindServersRepeatedTenTimesWithinThirtySecondsAsync | [DiscoveryFindServersFilterTests](DiscoveryServices/DiscoveryFindServersFilterTests.cs) | ✅ |

</details>

<details>
<summary>Discovery Services / Discovery Find Servers Self ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | FindServers001NoFilterAsync | [FindServersTests](DiscoveryServices/FindServersTests.cs) | ✅ |
| 001 | FindServers002MatchingServerUriAsync | [FindServersTests](DiscoveryServices/FindServersTests.cs) | ✅ |
| 001 | FindServers004VerifyApplicationDescriptionAsync | [FindServersTests](DiscoveryServices/FindServersTests.cs) | ✅ |
| 001 | FindServersAfterGetEndpointsAsync | [SessionlessExtendedTests](SessionServices/SessionlessExtendedTests.cs) | ✅ |
| 001 | FindServersAppNameNotEmptyAsync | [SessionlessExtendedTests](SessionServices/SessionlessExtendedTests.cs) | ✅ |
| 001 | FindServersProductUriNotEmptyAsync | [SessionlessExtendedTests](SessionServices/SessionlessExtendedTests.cs) | ✅ |
| 001 | FindServersReturnsDiscoveryUrlsAsync | [SessionlessExtendedTests](SessionServices/SessionlessExtendedTests.cs) | ✅ |
| 002 | FindServers003NonMatchingUriAsync | [FindServersTests](DiscoveryServices/FindServersTests.cs) | ✅ |
| 002 | FindServersWithUnknownHostnameAsync | [DiscoveryFindServersSelfTests](DiscoveryServices/DiscoveryFindServersSelfTests.cs) | ✅ |
| 004 | ConcurrentFindServersAsync | [SessionlessExtendedTests](SessionServices/SessionlessExtendedTests.cs) | ✅ |
| 004 | FindServersRepeatedConsistentAsync | [SessionlessExtendedTests](SessionServices/SessionlessExtendedTests.cs) | ✅ |
| 005 | FindServersWithNonRfc3066LocalesAsync | [DiscoveryFindServersSelfTests](DiscoveryServices/DiscoveryFindServersSelfTests.cs) | ✅ |
| 008 | FindServersWithInvalidEndpointUrlAsync | [DiscoveryFindServersSelfTests](DiscoveryServices/DiscoveryFindServersSelfTests.cs) | ✅ |
| 009 | FindServersRepeatedHundredTimesWithinTenSecondsAsync | [DiscoveryFindServersSelfTests](DiscoveryServices/DiscoveryFindServersSelfTests.cs) | ✅ |
| 010 | FindServersOnMultiHomedPcAsync | [DiscoveryFindServersSelfTests](DiscoveryServices/DiscoveryFindServersSelfTests.cs) | ✅ |
| Err-001 | FindServersWithNullEndpointUrlAsync | [DiscoveryFindServersSelfTests](DiscoveryServices/DiscoveryFindServersSelfTests.cs) | ✅ |
| Err-002 | FindServersWithAuthenticationTokenInRequestHeaderAsync | [DiscoveryFindServersSelfTests](DiscoveryServices/DiscoveryFindServersSelfTests.cs) | ✅ |

</details>

<details>
<summary>Discovery Services / Discovery Get Endpoints ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | AllEndpointsHaveNonEmptyUrlAsync | [DiscoveryDepthTests](DiscoveryServices/DiscoveryDepthTests.cs) | ✅ |
| 001 | AllEndpointsHaveServerDescriptionAsync | [DiscoveryDepthTests](DiscoveryServices/DiscoveryDepthTests.cs) | ✅ |
| 001 | AllEndpointsHaveTransportProfileUriAsync | [DiscoveryDepthTests](DiscoveryServices/DiscoveryDepthTests.cs) | ✅ |
| 001 | AllEndpointsHaveValidSecurityModeAsync | [DiscoveryDepthTests](DiscoveryServices/DiscoveryDepthTests.cs) | ✅ |
| 001 | AllEndpointsHaveValidUrlAsync | [DiscoveryFilterTests](DiscoveryServices/DiscoveryFilterTests.cs) | ✅ |
| 001 | DiscoveryEndpointAccessibleWithoutAuthAsync | [DiscoveryFilterTests](DiscoveryServices/DiscoveryFilterTests.cs) | ✅ |
| 001 | EndpointNoneHasAnonymousTokenAsync | [SessionlessExtendedTests](SessionServices/SessionlessExtendedTests.cs) | ✅ |
| 001 | EndpointSecurityLevelIsSetAsync | [SessionlessExtendedTests](SessionServices/SessionlessExtendedTests.cs) | ✅ |
| 001 | EndpointSecurityPolicyUriIsValidAsync | [SessionlessExtendedTests](SessionServices/SessionlessExtendedTests.cs) | ✅ |
| 001 | EndpointTransportProfileUriIsValidAsync | [SessionlessExtendedTests](SessionServices/SessionlessExtendedTests.cs) | ✅ |
| 001 | EndpointUserTokenPoliciesExistAsync | [SessionlessExtendedTests](SessionServices/SessionlessExtendedTests.cs) | ✅ |
| 001 | FindServersNoFilterReturnsAtLeastOneAsync | [DiscoveryDepthTests](DiscoveryServices/DiscoveryDepthTests.cs) | ✅ |
| 001 | FindServersReturnsDiscoveryUrlsAsync | [DiscoveryDepthTests](DiscoveryServices/DiscoveryDepthTests.cs) | ✅ |
| 001 | FindServersReturnsServerApplicationTypeAsync | [DiscoveryDepthTests](DiscoveryServices/DiscoveryDepthTests.cs) | ✅ |
| 001 | FindServersWithDefaultFilterReturnsResultsAsync | [DiscoveryDepthTests](DiscoveryServices/DiscoveryDepthTests.cs) | ✅ |
| 001 | GetEndpoints001DefaultParametersAsync | [GetEndpointsTests](DiscoveryServices/GetEndpointsTests.cs) | ✅ |
| 001 | GetEndpoints004VerifyEndpointFieldsAsync | [GetEndpointsTests](DiscoveryServices/GetEndpointsTests.cs) | ✅ |
| 001 | GetEndpoints007VerifyEndpointUrlAsync | [GetEndpointsTests](DiscoveryServices/GetEndpointsTests.cs) | ✅ |
| 001 | GetEndpoints008SecurityNoneAvailableAsync | [GetEndpointsTests](DiscoveryServices/GetEndpointsTests.cs) | ✅ |
| 001 | GetEndpointsAfterFindServersAsync | [SessionlessExtendedTests](SessionServices/SessionlessExtendedTests.cs) | ✅ |
| 001 | GetEndpointsDefaultReturnsAtLeastOneAsync | [DiscoveryDepthTests](DiscoveryServices/DiscoveryDepthTests.cs) | ✅ |
| 001 | GetEndpointsHasSecurityModeNoneAsync | [SessionlessExtendedTests](SessionServices/SessionlessExtendedTests.cs) | ✅ |
| 001 | GetEndpointsHasSignAndEncryptAsync | [SessionlessExtendedTests](SessionServices/SessionlessExtendedTests.cs) | ✅ |
| 001 | GetEndpointsReturnsConsistentUrlAsync | [DiscoveryFilterTests](DiscoveryServices/DiscoveryFilterTests.cs) | ✅ |
| 001 | GetEndpointsServerNameNotEmptyAsync | [SessionlessExtendedTests](SessionServices/SessionlessExtendedTests.cs) | ✅ |
| 001 | GetEndpointsVerifyAnonymousTokenAvailableAsync | [DiscoveryEndpointTests](DiscoveryServices/DiscoveryEndpointTests.cs) | ✅ |
| 001 | GetEndpointsVerifyApplicationUriAsync | [DiscoveryEndpointTests](DiscoveryServices/DiscoveryEndpointTests.cs) | ✅ |
| 001 | GetEndpointsVerifyAtLeastOneSecureEndpointAsync | [DiscoveryEndpointTests](DiscoveryServices/DiscoveryEndpointTests.cs) | ✅ |
| 001 | GetEndpointsVerifySecurityPolicyUriAsync | [DiscoveryEndpointTests](DiscoveryServices/DiscoveryEndpointTests.cs) | ✅ |
| 001 | GetEndpointsVerifyTransportProfileUriAsync | [DiscoveryEndpointTests](DiscoveryServices/DiscoveryEndpointTests.cs) | ✅ |
| 001 | GetEndpointsVerifyUsernameTokenAvailableAsync | [DiscoveryEndpointTests](DiscoveryServices/DiscoveryEndpointTests.cs) | ✅ |
| 001 | GetEndpointsWithDefaultProfileReturnsResultsAsync | [DiscoveryDepthTests](DiscoveryServices/DiscoveryDepthTests.cs) | ✅ |
| 001 | SecureEndpointsHaveNonEmptyCertAsync | [DiscoveryDepthTests](DiscoveryServices/DiscoveryDepthTests.cs) | ✅ |
| 001 | SessionlessDiscoveryNoAuthAsync | [SessionlessExtendedTests](SessionServices/SessionlessExtendedTests.cs) | ✅ |
| 001 | VerifyEachEndpointHasUserIdentityTokensAsync | [DiscoveryFilterTests](DiscoveryServices/DiscoveryFilterTests.cs) | ✅ |
| 001 | VerifyEndpointSecurityLevelConsistencyAsync | [DiscoveryFilterTests](DiscoveryServices/DiscoveryFilterTests.cs) | ✅ |
| 002 | FindServersWithLocaleIdFilterAsync | [DiscoveryFilterTests](DiscoveryServices/DiscoveryFilterTests.cs) | ✅ |
| 002 | GetEndpoints002WithLocalesAsync | [GetEndpointsTests](DiscoveryServices/GetEndpointsTests.cs) | ✅ |
| 002 | GetEndpointsWithLocaleFilterEnglishAsync | [DiscoveryFilterTests](DiscoveryServices/DiscoveryFilterTests.cs) | ✅ |
| 002 | GetEndpointsWithMultipleLocaleIdsAsync | [DiscoveryFilterTests](DiscoveryServices/DiscoveryFilterTests.cs) | ✅ |
| 002 | GetEndpointsWithSupportedLocalesAsync | [DiscoveryGetEndpointsTests](DiscoveryServices/DiscoveryGetEndpointsTests.cs) | ✅ |
| 003 | GetEndpoints003DifferentUrlAsync | [GetEndpointsTests](DiscoveryServices/GetEndpointsTests.cs) | ✅ |
| 003 | GetEndpoints005TransportProfileAsync | [GetEndpointsTests](DiscoveryServices/GetEndpointsTests.cs) | ✅ |
| 003 | GetEndpointsWithHttpsProfileFilterAsync | [DiscoveryFilterTests](DiscoveryServices/DiscoveryFilterTests.cs) | ⏭️ |
| 003 | GetEndpointsWithHttpsProfileFilterOrIgnoreAsync | [DiscoveryDepthTests](DiscoveryServices/DiscoveryDepthTests.cs) | ⏭️ |
| 003 | GetEndpointsWithServerUriFilterAsync | [DiscoveryFilterTests](DiscoveryServices/DiscoveryFilterTests.cs) | ✅ |
| 003 | GetEndpointsWithTcpProfileFilterAsync | [DiscoveryFilterTests](DiscoveryServices/DiscoveryFilterTests.cs) | ✅ |
| 003 | GetEndpointsWithTransportProfileFilterAsync | [DiscoveryEndpointTests](DiscoveryServices/DiscoveryEndpointTests.cs) | ✅ |
| 003 | GetEndpointsWithUaTcpProfileFilterAsync | [DiscoveryDepthTests](DiscoveryServices/DiscoveryDepthTests.cs) | ✅ |
| 003 | GetEndpointsWithTransportProfileUrisFilterAsync | [DiscoveryGetEndpointsTests](DiscoveryServices/DiscoveryGetEndpointsTests.cs) | ✅ |
| 004 | GetEndpointsWithUnknownLocaleFallsBackToDefaultAsync | [DiscoveryFilterTests](DiscoveryServices/DiscoveryFilterTests.cs) | ✅ |
| 004 | GetEndpointsWithMixedSupportedAndUnsupportedLocalesAsync | [DiscoveryGetEndpointsTests](DiscoveryServices/DiscoveryGetEndpointsTests.cs) | ✅ |
| 005 | GetEndpointsWithNonRfc3066LocalesAsync | [DiscoveryGetEndpointsTests](DiscoveryServices/DiscoveryGetEndpointsTests.cs) | ✅ |
| 007 | ConcurrentGetEndpointsAsync | [SessionlessExtendedTests](SessionServices/SessionlessExtendedTests.cs) | ✅ |
| 007 | SessionlessClientNoLeakAsync | [SessionlessExtendedTests](SessionServices/SessionlessExtendedTests.cs) | ✅ |
| 008 | GetEndpointsWithUnknownHostnameReturnsDefaultAsync | [DiscoveryGetEndpointsTests](DiscoveryServices/DiscoveryGetEndpointsTests.cs) | ✅ |
| 009 | GetEndpointsWithMultipleHostnamesInCertificateAsync | [DiscoveryGetEndpointsTests](DiscoveryServices/DiscoveryGetEndpointsTests.cs) | ✅ |
| 010 | GetEndpointsWithInvalidEndpointUrlAsync | [DiscoveryGetEndpointsTests](DiscoveryServices/DiscoveryGetEndpointsTests.cs) | ✅ |
| 011 | FindServersNonMatchingUriReturnsEmptyAsync | [DiscoveryDepthTests](DiscoveryServices/DiscoveryDepthTests.cs) | ✅ |
| 011 | GetEndpointsErr001InvalidTransportProfileAsync | [GetEndpointsTests](DiscoveryServices/GetEndpointsTests.cs) | ✅ |
| 011 | GetEndpointsWithUnsupportedProfileUriAsync | [DiscoveryGetEndpointsTests](DiscoveryServices/DiscoveryGetEndpointsTests.cs) | ✅ |
| 012 | GetEndpointsRepeatedConsistentAsync | [SessionlessExtendedTests](SessionServices/SessionlessExtendedTests.cs) | ✅ |
| 013 | FindServersMatchingUriReturnsServerAsync | [DiscoveryDepthTests](DiscoveryServices/DiscoveryDepthTests.cs) | ✅ |
| 013 | FindServersNonMatchingUriReturnsEmptyAsync | [DiscoveryFilterTests](DiscoveryServices/DiscoveryFilterTests.cs) | ✅ |
| 013 | FindServersReturnsNonEmptyApplicationUriAsync | [DiscoveryDepthTests](DiscoveryServices/DiscoveryDepthTests.cs) | ✅ |
| 013 | FindServersReturnsServerOrClientAndServerAsync | [DiscoveryFilterTests](DiscoveryServices/DiscoveryFilterTests.cs) | ✅ |
| 013 | FindServersSameAppUriAsEndpointsAsync | [SessionlessExtendedTests](SessionServices/SessionlessExtendedTests.cs) | ✅ |
| 013 | FindServersVerifyApplicationTypeAsync | [DiscoveryEndpointTests](DiscoveryServices/DiscoveryEndpointTests.cs) | ✅ |
| 013 | FindServersVerifyDiscoveryUrlsContainPortAsync | [DiscoveryFilterTests](DiscoveryServices/DiscoveryFilterTests.cs) | ✅ |
| 013 | FindServersWithServerUriFilterAsync | [DiscoveryFilterTests](DiscoveryServices/DiscoveryFilterTests.cs) | ✅ |
| 013 | GetEndpoints006VerifyServerApplicationDescriptionAsync | [GetEndpointsTests](DiscoveryServices/GetEndpointsTests.cs) | ✅ |
| 013 | GetEndpointsReturnsApplicationUriAsync | [SessionlessExtendedTests](SessionServices/SessionlessExtendedTests.cs) | ✅ |
| 013 | GetEndpointsReturnsSameAppUriAsync | [SessionlessExtendedTests](SessionServices/SessionlessExtendedTests.cs) | ✅ |
| Err-001 | GetEndpointsWithNullEndpointUrlAsync | [DiscoveryGetEndpointsTests](DiscoveryServices/DiscoveryGetEndpointsTests.cs) | ✅ |
| Err-002 | GetEndpointsErr002SecureEndpointHasCertificateAsync | [GetEndpointsTests](DiscoveryServices/GetEndpointsTests.cs) | ✅ |
| Err-002 | GetEndpointsReturnsServerCertAsync | [SessionlessExtendedTests](SessionServices/SessionlessExtendedTests.cs) | ✅ |

</details>

<details>
<summary>Discovery Services / Discovery Register ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | RegisterServerWithIsOnlineTrueAsync | [DiscoveryRegisterTests](Discovery/DiscoveryRegisterTestsImpl.cs) | ✅ |
| 002 | RegisterServerWithIsOnlineFalseAsync | [DiscoveryRegisterTests](Discovery/DiscoveryRegisterTestsImpl.cs) | ✅ |
| 003 | RegisterServerWithGatewayServerUriAsync | [DiscoveryRegisterTests](Discovery/DiscoveryRegisterTestsImpl.cs) | ✅ |
| 004 | RegisterServerWithMultipleDiscoveryUrlsAsync | [DiscoveryRegisterTests](Discovery/DiscoveryRegisterTestsImpl.cs) | ✅ |
| 005 | RegisterServerWithSemaphoreFilePathAndIsOnlineTrueAsync | [DiscoveryRegisterTests](Discovery/DiscoveryRegisterTestsImpl.cs) | ✅ |
| 006 | RegisterServerWithSemaphoreFilePathAndIsOnlineFalseAsync | [DiscoveryRegisterTests](Discovery/DiscoveryRegisterTestsImpl.cs) | ✅ |
| 007 | RegisterServerWithMissingSemaphoreFilePathAndIsOnlineTrueAsync | [DiscoveryRegisterTests](Discovery/DiscoveryRegisterTestsImpl.cs) | ✅ |
| 008 | RegisterServerMultipleTimesFromDifferentSecureChannelsAsync | [DiscoveryRegisterTests](Discovery/DiscoveryRegisterTestsImpl.cs) | ✅ |
| 009 | RegisterServerMultipleServersWithMixedIsOnlineAsync | [DiscoveryRegisterTests](Discovery/DiscoveryRegisterTestsImpl.cs) | ✅ |
| 010 | RegisterServerMultipleTimesWithVariedSemaphoreFilePathAsync | [DiscoveryRegisterTests](Discovery/DiscoveryRegisterTestsImpl.cs) | ✅ |
| 011 | RegisterServerMultipleTimesWithIsOnlineFalseAsync | [DiscoveryRegisterTests](Discovery/DiscoveryRegisterTestsImpl.cs) | ✅ |
| 012 | RegisterServerRepeatedlyWithIsOnlineFalseAsync | [DiscoveryRegisterTests](Discovery/DiscoveryRegisterTestsImpl.cs) | ✅ |
| 013 | RegisterServerMultipleWithSingleSemaphoreFilePathAsync | [DiscoveryRegisterTests](Discovery/DiscoveryRegisterTestsImpl.cs) | ✅ |
| 014 | RegisterServerMultipleWithSingleSemaphoreFilePathRepeatedAsync | [DiscoveryRegisterTests](Discovery/DiscoveryRegisterTestsImpl.cs) | ✅ |
| 017 | RegisterServerWithMultipleServerNamesVaryingLocaleAsync | [DiscoveryRegisterTests](Discovery/DiscoveryRegisterTestsImpl.cs) | ✅ |
| 018 | RegisterMultipleServersWithUniqueUrisAndFilterByServerUriAsync | [DiscoveryRegisterTests](Discovery/DiscoveryRegisterTestsImpl.cs) | ✅ |
| Err-001 | RegisterServerOverInsecureChannelReturnsErrorAsync | [DiscoveryRegisterTests](Discovery/DiscoveryRegisterTestsImpl.cs) | ✅ |
| Err-002 | RegisterServerWithEmptyServerUriReturnsBadServerUriInvalidAsync | [DiscoveryRegisterTests](Discovery/DiscoveryRegisterTestsImpl.cs) | ✅ |
| Err-003 | RegisterServerWithEmptyServerNamesReturnsBadServerNameMissingAsync | [DiscoveryRegisterTests](Discovery/DiscoveryRegisterTestsImpl.cs) | ✅ |
| Err-004 | RegisterServerWithEmptyDiscoveryUrlsReturnsBadDiscoveryUrlMissingAsync | [DiscoveryRegisterTests](Discovery/DiscoveryRegisterTestsImpl.cs) | ✅ |
| Err-005 | RegisterServerWithMismatchedApplicationUriReturnsBadServerUriInvalidAsync | [DiscoveryRegisterTests](Discovery/DiscoveryRegisterTestsImpl.cs) | ✅ |
| Err-006 | RegisterServerWithClientServerTypeReturnsBadInvalidArgumentAsync | [DiscoveryRegisterTests](Discovery/DiscoveryRegisterTestsImpl.cs) | ✅ |
| Err-007 | RegisterServerWithInvalidServerTypeReturnsBadInvalidArgumentAsync | [DiscoveryRegisterTests](Discovery/DiscoveryRegisterTestsImpl.cs) | ✅ |

</details>

### GDS

<details>
<summary>GDS / GDS AliasName Discovery ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | AliasNameBrowseDirectoryAfterRegisterAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 002 | AliasNameBrowseDirectoryAfterUnregisterAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 003 | AliasNameRegisterServerAndBrowseAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 004 | AliasNameRegisterClientAndBrowseAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 005 | AliasNameRegisterClientServerAndBrowseAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 006 | AliasNameMultipleRegisterAndBrowseAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 007 | AliasNameUnregisterOneOfMultipleAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 008 | AliasNameDirectoryMethodsExistAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 009 | AliasNameBrowseDirectoryHasCorrectNodeClassAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 010 | AliasNameReregisterSameUriAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 011 | AliasNameBrowseAfterUpdateApplicationAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 012 | AliasNameBrowseWithHierarchicalReferencesAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 013 | AliasNameBrowseCertificateGroupsExistAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 014 | AliasNameRegisterDiscoveryServerAndBrowseAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 015 | AliasNameDirectoryNodeIdIsValidAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |

</details>

<details>
<summary>GDS / GDS Application Directory ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 000 | AppDirBrowseAddressSpaceAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 000 | BrowseCertificateGroupTypeDefinitionAsync | [GdsCertificateManagementTests](GDS/GdsCertificateManagementTests.cs) | ✅ |
| 000 | BrowseCertificateGroupsOnDirectoryAsync | [GdsCertificateManagementTests](GDS/GdsCertificateManagementTests.cs) | ✅ |
| 000 | BrowseDefaultApplicationGroupExistsAsync | [GdsCertificateManagementTests](GDS/GdsCertificateManagementTests.cs) | ✅ |
| 000 | BrowseDirectoryCertificateGroupsExistAsync | [GdsApplicationDirectoryTests](GDS/GdsApplicationDirectoryTests.cs) | ✅ |
| 000 | BrowseDirectoryHasApplicationsFolderAsync | [GdsApplicationDirectoryTests](GDS/GdsApplicationDirectoryTests.cs) | ✅ |
| 000 | BrowseDirectoryHasFindApplicationsMethodAsync | [GdsApplicationDirectoryTests](GDS/GdsApplicationDirectoryTests.cs) | ✅ |
| 000 | BrowseDirectoryHasGetApplicationMethodAsync | [GdsApplicationDirectoryTests](GDS/GdsApplicationDirectoryTests.cs) | ✅ |
| 000 | BrowseDirectoryHasQueryApplicationsMethodAsync | [GdsApplicationDirectoryTests](GDS/GdsApplicationDirectoryTests.cs) | ✅ |
| 000 | BrowseDirectoryHasRegisterApplicationMethodAsync | [GdsApplicationDirectoryTests](GDS/GdsApplicationDirectoryTests.cs) | ✅ |
| 000 | BrowseDirectoryHasUnregisterApplicationMethodAsync | [GdsApplicationDirectoryTests](GDS/GdsApplicationDirectoryTests.cs) | ✅ |
| 000 | BrowseDirectoryTrustListNodesAsync | [GdsApplicationDirectoryTests](GDS/GdsApplicationDirectoryTests.cs) | ✅ |
| 000 | BrowseServerDirectoryFolderExistsAsync | [GdsApplicationDirectoryTests](GDS/GdsApplicationDirectoryTests.cs) | ✅ |
| 000 | ReadDefaultApplicationGroupCertificateTypesAsync | [GdsCertificateManagementTests](GDS/GdsCertificateManagementTests.cs) | ✅ |
| 000 | ReadDefaultApplicationGroupExistsAsync | [GdsApplicationDirectoryTests](GDS/GdsApplicationDirectoryTests.cs) | ✅ |
| 000 | ReadDefaultApplicationGroupHasCertificateTypesAsync | [GdsApplicationDirectoryTests](GDS/GdsApplicationDirectoryTests.cs) | ✅ |
| 000 | ReadTrustListFromDefaultApplicationGroupAsync | [GdsCertificateManagementTests](GDS/GdsCertificateManagementTests.cs) | ✅ |
| 000 | ReadTrustListSizePropertyAsync | [GdsCertificateManagementTests](GDS/GdsCertificateManagementTests.cs) | ✅ |
| 000 | VerifyDefaultHttpsGroupExistsIfSupportedAsync | [GdsCertificateManagementTests](GDS/GdsCertificateManagementTests.cs) | ✅ |
| 000 | VerifyTrustListHasOpenCloseReadWriteMethodsAsync | [GdsApplicationDirectoryTests](GDS/GdsApplicationDirectoryTests.cs) | ✅ |
| 000 | VerifyTrustListOpenCloseMethodsExistAsync | [GdsCertificateManagementTests](GDS/GdsCertificateManagementTests.cs) | ✅ |
| 001 | AppDirFindApplicationsValidUriAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 001 | FindApplicationsWithMatchingUriReturnsRegisteredAppAsync | [GdsApplicationDirectoryTests](GDS/GdsApplicationDirectoryTests.cs) | ✅ |
| 001 | GetCertificateGroupsForRegisteredApplicationAsync | [GdsCertificateManagementTests](GDS/GdsCertificateManagementTests.cs) | ⏭️ |
| 001 | GetCertificateStatusReturnsBooleanAsync | [GdsCertificateManagementTests](GDS/GdsCertificateManagementTests.cs) | ⏭️ |
| 001 | GetTrustListForCertificateGroupAsync | [GdsCertificateManagementTests](GDS/GdsCertificateManagementTests.cs) | ⏭️ |
| 001 | RegisterApplicationAsClientTypeAsync | [GdsApplicationDirectoryTests](GDS/GdsApplicationDirectoryTests.cs) | ✅ |
| 001 | RegisterApplicationAsServerTypeAsync | [GdsApplicationDirectoryTests](GDS/GdsApplicationDirectoryTests.cs) | ✅ |
| 001 | RegisterApplicationReturnsValidNodeIdAsync | [GdsApplicationDirectoryTests](GDS/GdsApplicationDirectoryTests.cs) | ✅ |
| 001 | RegisterApplicationTwiceWithSameUriReturnsSameIdAsync | [GdsApplicationDirectoryTests](GDS/GdsApplicationDirectoryTests.cs) | ✅ |
| 001 | RegisterApplicationWithValidDescriptionReturnsGoodAsync | [GdsApplicationDirectoryTests](GDS/GdsApplicationDirectoryTests.cs) | ✅ |
| 001 | StartSigningRequestAndFinishRequestAsync | [GdsCertificateManagementTests](GDS/GdsCertificateManagementTests.cs) | ⏭️ |
| 001 | UnregisterApplicationReturnsGoodAsync | [GdsApplicationDirectoryTests](GDS/GdsApplicationDirectoryTests.cs) | ✅ |
| 002 | AppDirFindApplicationsNonExistentUriAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 002 | FindApplicationsWithNonMatchingUriReturnsEmptyAsync | [GdsApplicationDirectoryTests](GDS/GdsApplicationDirectoryTests.cs) | ✅ |
| 003 | AppDirFindApplicationsEmptyUriAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 004 | AppDirFindApplicationsAfterMultipleRegistrationsAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 005 | AppDirFindApplicationsServerTypeAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 006 | AppDirRegisterServerReturnsNodeIdAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 007 | AppDirRegisterClientReturnsNodeIdAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 008 | AppDirRegisterClientAndServerTypeAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 009 | AppDirRegisterDiscoveryServerTypeAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 010 | AppDirRegisterWithCapabilitiesAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 011 | AppDirRegisterAuditEventGeneratedAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ⏭️ |
| 012 | AppDirRegisterSameUriReturnsSameIdAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 013 | AppDirRegisterWithoutAdminRoleAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 014 | AppDirRegisterWithInsufficientPrivilegesAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 015 | AppDirRegisterAnonymousUserDeniedAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 016 | AppDirRegisterReadOnlyUserDeniedAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 017 | AppDirUnregisterReturnsGoodAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 018 | AppDirUnregisterAuditEventGeneratedAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ⏭️ |
| 019 | AppDirUnregisterInvalidIdThrows | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 020 | AppDirUnregisterTwiceThrowsAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 020 | UnregisterApplicationWithInvalidIdThrowsBadNotFound | [GdsApplicationDirectoryTests](GDS/GdsApplicationDirectoryTests.cs) | ✅ |
| 021 | AppDirUnregisterThenFindReturnsEmptyAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 022 | AppDirUnregisterWithoutAdminRoleAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 023 | AppDirUnregisterWithInsufficientPrivilegesAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 024 | AppDirUnregisterAnonymousUserDeniedAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 025 | AppDirUnregisterReadOnlyUserDeniedAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 026 | AppDirGetApplicationValidIdAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 027 | AppDirGetApplicationInvalidIdThrows | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 028 | AppDirGetApplicationReturnsCorrectNameAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 029 | AppDirGetApplicationReturnsCorrectProductUriAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 030 | AppDirGetApplicationReturnsCorrectTypeAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 031 | AppDirGetApplicationWithoutAdminRoleAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 031 | UpdateApplicationModifiesDescriptionAsync | [GdsApplicationDirectoryTests](GDS/GdsApplicationDirectoryTests.cs) | ✅ |
| 032 | AppDirGetApplicationWithInsufficientPrivilegesAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 033 | AppDirGetApplicationAnonymousUserDeniedAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 034 | AppDirGetApplicationReadOnlyUserDeniedAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 035 | AppDirUpdateApplicationChangesProductUriAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 036 | AppDirUpdateApplicationChangesNameAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 037 | AppDirUpdateApplicationChangesCapabilitiesAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 038 | AppDirUpdateApplicationChangesDiscoveryUrlsAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 039 | AppDirUpdatePreservesApplicationUriAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 040 | AppDirUpdateAuditEventGeneratedAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ⏭️ |
| 040 | GetApplicationWithValidIdReturnsDescriptionAsync | [GdsApplicationDirectoryTests](GDS/GdsApplicationDirectoryTests.cs) | ✅ |
| 040 | VerifyApplicationHasServerCapabilitiesAsync | [GdsApplicationDirectoryTests](GDS/GdsApplicationDirectoryTests.cs) | ✅ |
| 040 | VerifyApplicationRecordDataTypeFieldsAsync | [GdsApplicationDirectoryTests](GDS/GdsApplicationDirectoryTests.cs) | ✅ |
| 041 | AppDirUpdateWithInvalidIdThrows | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 042 | AppDirUpdateMultipleFieldsAtOnceAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 042 | GetApplicationWithInvalidIdThrowsBadNotFound | [GdsApplicationDirectoryTests](GDS/GdsApplicationDirectoryTests.cs) | ✅ |
| 043 | AppDirUpdateWithoutAdminRoleAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 044 | AppDirUpdateWithInsufficientPrivilegesAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 045 | AppDirUpdateAnonymousUserDeniedAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 046 | AppDirUpdateReadOnlyUserDeniedAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 047 | AppDirQueryServersReturnsResultsAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 048 | AppDirQueryServersWithNameFilterAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 049 | AppDirQueryServersWithUriFilterAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 050 | AppDirQueryServersWithTypeFilterAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 051 | AppDirQueryServersWithProductUriFilterAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 052 | AppDirQueryServersZeroMaxRecordsAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 053 | AppDirQueryServersReturnsPaginationAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 054 | AppDirQueryServersReturnsLastCounterResetTimeAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 055 | AppDirQueryServersNoMatchReturnsEmptyAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 056 | AppDirDirectoryHasUnregisterApplicationMethodAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 057 | AppDirDirectoryHasUpdateApplicationMethodAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 058 | AppDirDirectoryHasGetApplicationMethodAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 059 | AppDirDirectoryHasQueryApplicationsMethodAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 060 | AppDirRegisterAndGetRoundTripAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 061 | AppDirRegisterUpdateAndGetRoundTripAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 062 | AppDirRegisterFindAndUnregisterCycleAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 063 | AppDirMultipleAppsIndependentAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 064 | AppDirGetApplicationIdFieldSetAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 065 | AppDirGetApplicationUriNotEmptyAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 066 | AppDirDirectoryNodeDisplayNameIsDirectoryAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 067 | AppDirDefaultApplicationGroupExistsAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 068 | AppDirRegisterMultipleDiscoveryUrlsAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 069 | AppDirRegisterWithMultipleCapabilitiesAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 070 | AppDirFindApplicationsClientTypeAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 071 | AppDirUpdateDoesNotChangeApplicationIdAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 072 | AppDirQueryWithCapabilitiesFilterAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 073 | AppDirRegisterPreservesApplicationNamesAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 074 | AppDirFindReturnsAllFieldsAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 075 | AppDirQueryServersWithCapabilitiesFilterAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 076 | AppDirRegisterWithEmptyCapabilitiesAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 077 | AppDirQueryPaginationSecondPageAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 078 | AppDirBrowseDirectoryNodeClassIsObjectAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 079 | AppDirGetAfterUnregisterThrowsAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |

</details>

<details>
<summary>GDS / GDS LDS-ME Connectivity ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 000 | LdsMeConnectToLdsMeAsync | [LdsMeConformanceTests](Discovery/LdsMeConformanceTests.cs) | ✅ |
| 001 | LdsMeRegisterServerWithLdsMeAsync | [LdsMeConformanceTests](Discovery/LdsMeConformanceTests.cs) | ✅ |
| 002 | LdsMeUnregisterServerFromLdsMeAsync | [LdsMeConformanceTests](Discovery/LdsMeConformanceTests.cs) | ✅ |
| 003 | LdsMeFindServersOnNetworkAsync | [LdsMeConformanceTests](Discovery/LdsMeConformanceTests.cs) | ✅ |
| 004 | LdsMeQueryServersOnNetworkAsync | [LdsMeConformanceTests](Discovery/LdsMeConformanceTests.cs) | ✅ |
| 005 | LdsMePeriodicReregistrationAsync | [LdsMeConformanceTests](Discovery/LdsMeConformanceTests.cs) | ✅ |
| 006 | LdsMeServerCapabilitiesOnNetworkAsync | [LdsMeConformanceTests](Discovery/LdsMeConformanceTests.cs) | ✅ |
| 007 | LdsMeDiscoveryUrlsOnNetworkAsync | [LdsMeConformanceTests](Discovery/LdsMeConformanceTests.cs) | ✅ |
| 008 | LdsMeMulticastAnnouncementAsync | [LdsMeConformanceTests](Discovery/LdsMeConformanceTests.cs) | ⏭️ |
| 009 | LdsMeServerOnNetworkTimeoutAsync | [LdsMeConformanceTests](Discovery/LdsMeConformanceTests.cs) | ✅ |
| 010 | LdsMeFilterByCapabilitiesAsync | [LdsMeConformanceTests](Discovery/LdsMeConformanceTests.cs) | ✅ |
| 011 | LdsMeFilterByServerNameAsync | [LdsMeConformanceTests](Discovery/LdsMeConformanceTests.cs) | ✅ |
| 012 | LdsMeSecureConnectionAsync | [LdsMeConformanceTests](Discovery/LdsMeConformanceTests.cs) | ✅ |
| 013 | LdsMeRecoveryAfterDisconnectAsync | [LdsMeConformanceTests](Discovery/LdsMeConformanceTests.cs) | ✅ |

</details>

<details>
<summary>GDS / GDS Query Applications ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | QueryApplicationsAfterUnregisterAppNotInResultsAsync | [GdsQueryApplicationsTests](GDS/GdsQueryApplicationsTests.cs) | ✅ |
| 001 | QueryApplicationsVerifyLastCounterResetTimeAsync | [GdsQueryApplicationsTests](GDS/GdsQueryApplicationsTests.cs) | ✅ |
| 001 | QueryApplicationsWithApplicationUriFilterAsync | [GdsQueryApplicationsTests](GDS/GdsQueryApplicationsTests.cs) | ✅ |
| 001 | QueryApplicationsWithNoFilterReturnsAllRegisteredAsync | [GdsQueryApplicationsTests](GDS/GdsQueryApplicationsTests.cs) | ✅ |
| 001 | QueryApplicationsWithProductUriFilterAsync | [GdsQueryApplicationsTests](GDS/GdsQueryApplicationsTests.cs) | ✅ |
| 001 | QueryApplicationsWithServerCapabilityFilterAsync | [GdsQueryApplicationsTests](GDS/GdsQueryApplicationsTests.cs) | ✅ |
| 001 | QueryAppsBasicCallAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 001 | RegisterMultipleAppsThenQueryAllReturnedAsync | [GdsQueryApplicationsTests](GDS/GdsQueryApplicationsTests.cs) | ✅ |
| 002 | QueryAppsReturnsRegisteredAppAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 003 | QueryAppsNoMatchReturnsEmptyAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 004 | QueryApplicationsContinuationWithNextRecordIdAsync | [GdsQueryApplicationsTests](GDS/GdsQueryApplicationsTests.cs) | ✅ |
| 004 | QueryApplicationsWithPaginationMaxRecordsAsync | [GdsQueryApplicationsTests](GDS/GdsQueryApplicationsTests.cs) | ✅ |
| 004 | QueryAppsFilterByNameAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 005 | QueryAppsFilterByUriAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 006 | QueryAppsFilterByServerTypeAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 007 | QueryAppsFilterByClientTypeAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 008 | QueryApplicationsWithApplicationNameFilterAsync | [GdsQueryApplicationsTests](GDS/GdsQueryApplicationsTests.cs) | ✅ |
| 008 | QueryAppsFilterByProductUriAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 009 | QueryAppsFilterByCapabilitiesAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 010 | QueryAppsPaginationMaxOneRecordAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 011 | QueryAppsPaginationContinuationAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 012 | QueryAppsZeroMaxRecordsAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 013 | QueryAppsReturnsLastCounterResetTimeAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 014 | QueryAppsReturnsNextRecordIdAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 015 | QueryAppsCombinedNameAndUriAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 016 | QueryAppsCombinedUriAndTypeAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 017 | QueryAppsCombinedTypeAndCapabilitiesAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 018 | QueryAppsCombinedAllFiltersAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 019 | QueryAppsAfterUnregisterExcludesAppAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 020 | QueryAppsAfterUpdateReflectsChangesAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 021 | QueryAppsMultipleRegistrationsAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 022 | QueryAppsEmptyNameFilterAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 023 | QueryAppsEmptyUriFilterAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 024 | QueryAppsEmptyProductUriFilterAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 025 | QueryAppsEmptyCapabilitiesFilterAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 026 | QueryAppsTypeZeroReturnsAllAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 027 | QueryAppsLargeMaxRecordsAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 028 | QueryApplicationsWithApplicationTypeFilterServerAsync | [GdsQueryApplicationsTests](GDS/GdsQueryApplicationsTests.cs) | ✅ |
| 028 | QueryAppsHighStartingRecordIdAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 029 | QueryAppsDiscoveryServerTypeAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 030 | QueryAppsClientAndServerTypeAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 031 | QueryAppsMultipleCapabilitiesAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 032 | QueryAppsReturnedFieldsArePopulatedAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 033 | QueryAppsPaginationFullIterationAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 034 | QueryAppsFilterNamePartialMatchAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 035 | QueryAppsFilterProductUriSpecificAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 036 | QueryAppsConsistentResetTimeAcrossCallsAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 037 | QueryAppsNextRecordIdAdvancesAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 038 | QueryAppsNullCapabilitiesReturnsAllAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 039 | QueryAppsCombinedNameAndTypeAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 040 | QueryAppsCombinedUriAndProductUriAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |
| 041 | QueryAppsCombinedNameUriTypeProductCapsAsync | [GdsDepthTests](GDS/GdsDepthTests.cs) | ✅ |

</details>

<details>
<summary>GDS / Push Model for Global Certificate and TrustList Management ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | BrowseServerConfigurationExistsAsync | [PushCertManagementTests](Security/PushCertManagementTests.cs) | ✅ |
| 002 | BrowseCertificateGroupsExistsAsync | [PushCertManagementTests](Security/PushCertManagementTests.cs) | ✅ |
| 002 | CertificateGroupTypeDefinitionExistsAsync | [PushCertManagementDepthTests](Security/PushCertManagementDepthTests.cs) | ✅ |
| 003 | BrowseDefaultApplicationGroupExistsAsync | [PushCertManagementTests](Security/PushCertManagementTests.cs) | ✅ |
| 003 | BrowseDefaultApplicationGroupHasCertificateTypesAsync | [PushCertManagementTests](Security/PushCertManagementTests.cs) | ✅ |
| 003 | BrowseDefaultApplicationGroupHasTrustListAsync | [PushCertManagementTests](Security/PushCertManagementTests.cs) | ✅ |
| 003 | DefaultApplicationGroupHasCertificateAsync | [PushCertManagementDepthTests](Security/PushCertManagementDepthTests.cs) | ✅ |
| 004 | BrowseDefaultHttpsGroupIfExistsAsync | [PushCertManagementTests](Security/PushCertManagementTests.cs) | ✅ |
| 004 | BrowseDefaultUserTokenGroupIfExistsAsync | [PushCertManagementTests](Security/PushCertManagementTests.cs) | ✅ |
| 004 | HttpsGroupExistsOrIsAbsentAsync | [PushCertManagementDepthTests](Security/PushCertManagementDepthTests.cs) | ✅ |
| 004 | UserTokenGroupExistsOrIsAbsentAsync | [PushCertManagementDepthTests](Security/PushCertManagementDepthTests.cs) | ✅ |
| 005 | BrowseServerConfigurationMethodsAsync | [PushCertManagementTests](Security/PushCertManagementTests.cs) | ✅ |
| 005 | VerifyPushModelMethodsExistOnTypeDefinitionAsync | [PushCertManagementTests](Security/PushCertManagementTests.cs) | ✅ |
| 006 | ReadCertificateTypesFromDefaultApplicationGroupAsync | [PushCertManagementTests](Security/PushCertManagementTests.cs) | ✅ |
| 006 | ReadMaxTrustListSizeAsync | [PushCertManagementTests](Security/PushCertManagementTests.cs) | ✅ |
| 006 | ReadMulticastDnsEnabledAsync | [PushCertManagementTests](Security/PushCertManagementTests.cs) | ✅ |
| 006 | ReadServerCapabilitiesAsync | [PushCertManagementTests](Security/PushCertManagementTests.cs) | ✅ |
| 006 | ReadSupportedPrivateKeyFormatsAsync | [PushCertManagementTests](Security/PushCertManagementTests.cs) | ✅ |
| 008 | CreateSigningRequestMethodExistsAsync | [PushCertManagementDepthTests](Security/PushCertManagementDepthTests.cs) | ✅ |
| 008 | CreateSigningRequestWithRsaKeyTypeAsync | [PushCertManagementDepthTests](Security/PushCertManagementDepthTests.cs) | ✅ |
| 008 | CreateSigningRequestWithValidParametersAsync | [PushCertManagementTests](Security/PushCertManagementTests.cs) | ⏭️ |
| 010 | AdminCanReadTrustListAsync | [PushCertManagementDepthTests](Security/PushCertManagementDepthTests.cs) | ✅ |
| 010 | TrustListCloseAndReopenSucceedsAsync | [PushCertManagementDepthTests](Security/PushCertManagementDepthTests.cs) | ✅ |
| 010 | TrustListNodeExistsAsync | [PushCertManagementDepthTests](Security/PushCertManagementDepthTests.cs) | ✅ |
| 010 | TrustListOpenCloseMultipleTimesAsync | [PushCertManagementDepthTests](Security/PushCertManagementDepthTests.cs) | ✅ |
| 010 | TrustListOpenReadCloseAsync | [PushCertManagementTests](Security/PushCertManagementTests.cs) | ⏭️ |
| 010 | TrustListReadReturnsValidDataAsync | [PushCertManagementDepthTests](Security/PushCertManagementDepthTests.cs) | ✅ |
| 011 | TrustListSizePropertyAsync | [PushCertManagementTests](Security/PushCertManagementTests.cs) | ✅ |
| 011 | TrustListSizePropertyExistsAsync | [PushCertManagementDepthTests](Security/PushCertManagementDepthTests.cs) | ✅ |
| 012 | TrustListGetPositionSetPositionAsync | [PushCertManagementTests](Security/PushCertManagementTests.cs) | ⏭️ |
| 013 | TrustListOpenMaskAllAsync | [PushCertManagementDepthTests](Security/PushCertManagementDepthTests.cs) | ✅ |
| 013 | TrustListOpenMaskIssuerCertificatesAsync | [PushCertManagementDepthTests](Security/PushCertManagementDepthTests.cs) | ✅ |
| 013 | TrustListOpenMaskTrustedCertificatesAsync | [PushCertManagementDepthTests](Security/PushCertManagementDepthTests.cs) | ✅ |
| 015 | AdminCanCallApplyChangesAsync | [PushCertManagementDepthTests](Security/PushCertManagementDepthTests.cs) | ✅ |
| 015 | ApplyChangesIdempotentAsync | [PushCertManagementDepthTests](Security/PushCertManagementDepthTests.cs) | ✅ |
| 015 | ApplyChangesSucceedsAsync | [PushCertManagementTests](Security/PushCertManagementTests.cs) | ⏭️ |
| 016 | AdminCanCallGetCertificatesAsync | [PushCertManagementDepthTests](Security/PushCertManagementDepthTests.cs) | ✅ |
| 016 | GetCertificatesForDefaultApplicationGroupAsync | [PushCertManagementTests](Security/PushCertManagementTests.cs) | ⏭️ |
| 016 | GetCertificatesReturnsProperStructureAsync | [PushCertManagementTests](Security/PushCertManagementTests.cs) | ⏭️ |
| 017 | GetCertificateStatusIfPresentAsync | [PushCertManagementTests](Security/PushCertManagementTests.cs) | ⏭️ |
| 017 | GetCertificateStatusMethodExistsAsync | [PushCertManagementDepthTests](Security/PushCertManagementDepthTests.cs) | ⏭️ |
| 019 | AdminCanCallGetRejectedListAsync | [PushCertManagementDepthTests](Security/PushCertManagementDepthTests.cs) | ✅ |
| 019 | GetRejectedListReturnsResultAsync | [PushCertManagementTests](Security/PushCertManagementTests.cs) | ⏭️ |
| Err-001 | CreateSigningRequestWithInvalidGroupFailsAsync | [PushCertManagementTests](Security/PushCertManagementTests.cs) | ✅ |
| Err-002 | UpdateCertificateWithEmptyCertFailsAsync | [PushCertManagementTests](Security/PushCertManagementTests.cs) | ✅ |
| 010 | TrustListOpenWithReadModeAsync | [PushCertManagementDepthTests](Security/PushCertManagementDepthTests.cs) | ✅ |
| Err-003 | UpdateCertificateWithInvalidCertFailsAsync | [PushCertManagementTests](Security/PushCertManagementTests.cs) | ✅ |
| Err-004 | NonAdminCannotCallCreateSigningRequestAsync | [PushCertManagementTests](Security/PushCertManagementTests.cs) | ✅ |
| Err-005 | NonAdminCannotCallGetRejectedListAsync | [PushCertManagementTests](Security/PushCertManagementTests.cs) | ✅ |
| Err-006 | NonAdminCannotCallApplyChangesAsync | [PushCertManagementDepthTests](Security/PushCertManagementDepthTests.cs) | ✅ |
| Err-006 | NonAdminCannotCallApplyChangesAsync | [PushCertManagementTests](Security/PushCertManagementTests.cs) | ✅ |
| Err-007 | NonAdminCannotOpenTrustListForWriteAsync | [PushCertManagementDepthTests](Security/PushCertManagementDepthTests.cs) | ✅ |
| Err-008 | NonAdminCannotUpdateCertificateAsync | [PushCertManagementDepthTests](Security/PushCertManagementDepthTests.cs) | ✅ |

</details>

### Historical Access

<details>
<summary>Historical Access / Aggregate - Base ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|

</details>

<details>
<summary>Historical Access / HA Aggregate — 20 additional ✅</summary>

**Additional coverage** (not mapped to specific source scripts):

| NUnit Test | Fixture | Status |
|-----------|---------|--------|
| BrowseAggregateFunctionsFolderContainsNodes | [AggregateTests](HistoricalAccess/AggregateTests.cs) | ✅ |
| HistoryReadOnNonHistorizingVariableReturnsBadStatus | [AggregateTests](HistoricalAccess/AggregateTests.cs) | ✅ |
| HistoryReadProcessedOnNonHistorizingVariableReturnsBadStatus | [AggregateTests](HistoricalAccess/AggregateTests.cs) | ✅ |
| HistoryReadProcessedWithAverageAggregate | [AggregateTests](HistoricalAccess/AggregateTests.cs) | ✅ |
| HistoryReadProcessedWithCountAggregate | [AggregateTests](HistoricalAccess/AggregateTests.cs) | ✅ |
| HistoryReadProcessedWithInterpolativeAggregate | [AggregateTests](HistoricalAccess/AggregateTests.cs) | ✅ |
| HistoryReadProcessedWithMaxAggregate | [AggregateTests](HistoricalAccess/AggregateTests.cs) | ✅ |
| HistoryReadProcessedWithMinAggregate | [AggregateTests](HistoricalAccess/AggregateTests.cs) | ✅ |
| HistoryReadProcessedWithProcessingInterval | [AggregateTests](HistoricalAccess/AggregateTests.cs) | ✅ |
| HistoryReadProcessedWithUnsupportedAggregate | [AggregateTests](HistoricalAccess/AggregateTests.cs) | ✅ |
| HistoryReadRawOnInt32Variable | [AggregateTests](HistoricalAccess/AggregateTests.cs) | ✅ |
| HistoryReadRawReturnsValuesForHistorizingVariable | [AggregateTests](HistoricalAccess/AggregateTests.cs) | ✅ |
| HistoryReadRawReturnsValuesOrderedByTimestamp | [AggregateTests](HistoricalAccess/AggregateTests.cs) | ✅ |
| HistoryReadRawWithContinuationPointPagination | [AggregateTests](HistoricalAccess/AggregateTests.cs) | ✅ |
| HistoryReadRawWithNumValuesPerNodeLimit | [AggregateTests](HistoricalAccess/AggregateTests.cs) | ✅ |
| HistoryReadRawWithTimeRangeFilters | [AggregateTests](HistoricalAccess/AggregateTests.cs) | ✅ |
| HistoryReadWithStartTimeAfterEndTimeReturnsResult | [AggregateTests](HistoricalAccess/AggregateTests.cs) | ✅ |
| HistoryReadWithTimestampsToReturnSource | [AggregateTests](HistoricalAccess/AggregateTests.cs) | ✅ |
| ReadAccessLevelIncludesHistoryReadBit | [AggregateTests](HistoricalAccess/AggregateTests.cs) | ✅ |
| ReadHistorizingAttributeOnHistoricalVariable | [AggregateTests](HistoricalAccess/AggregateTests.cs) | ✅ |

</details>

<details>
<summary>Historical Access / Historical Access Data Max Nodes Read Continuation Point ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 000 | MaxNodesReadCp000ReadSingleNodeWithNumValuesOneAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| 001 | MaxNodesReadCp001FollowContinuationPointAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| 002 | MaxNodesReadCp002ReleaseContinuationPointAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |

</details>

<details>
<summary>Historical Access / Historical Access Delete Value ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 000 | DeleteValue000DeleteWithTimeRangeAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| 001 | DeleteValue001DeleteNarrowRangeAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| 002 | DeleteValue002DeleteWideRangeAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| 003 | DeleteValue003DeleteEqualStartEndAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| 004 | DeleteValue004DeleteStartAfterEndAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| 005 | DeleteValue005DeleteFutureRangeAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| 006 | DeleteValue006DeleteAndVerifyEmptyAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| 007 | DeleteValue007DeleteWithMinStartTimeAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| 008 | DeleteValue008DeleteModifiedFalseAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| 010 | DeleteValue010DeleteModifiedTrueAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ⏭️ |
| Err-001 | DeleteValueErr001InvalidNodeIdAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| Err-002 | DeleteValueErr002NullNodeIdAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| Err-003 | DeleteValueErr003NonHistoricalNodeAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ⏭️ |
| Err-004 | DeleteValueErr004ObjectNodeAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ⏭️ |
| Err-005 | DeleteValueErr005EmptyExtensionObjectsAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| dat-000 | DeleteValueDat000DeleteSingleTimestampAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| dat-001 | DeleteValueDat001DeleteMultipleTimestampsAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| dat-002 | DeleteValueDat002DeleteFutureTimestampAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| dat-003 | DeleteValueDat003DeleteMinTimestampAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| dat-004 | DeleteValueDat004DeleteMaxTimestampAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| dat-005 | DeleteValueDat005DeleteAndReadBackAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| dat-006 | DeleteValueDat006DeleteEmptyTimestampsAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| dat-Err-001 | DeleteValueDatErr001InvalidNodeIdAtTimeAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| dat-Err-002 | DeleteValueDatErr002NullNodeIdAtTimeAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| dat-Err-003 | DeleteValueDatErr003NonHistoricalNodeAtTimeAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ⏭️ |
| dat-Err-004 | DeleteValueDatErr004ObjectNodeAtTimeAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ⏭️ |
| dat-Err-005 | DeleteValueDatErr005EmptyReqTimesAtTimeAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |

</details>

<details>
<summary>Historical Access / Historical Access Insert Value ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 000 | InsertValue000InsertSingleValueAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| 001 | InsertValue001InsertMultipleValuesAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| 002 | InsertValue002InsertAndReadBackAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| 003 | InsertValue003InsertWithGoodStatusAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| 004 | InsertValue004InsertWithUncertainStatusAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| 005 | InsertValue005InsertWithBadStatusAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| 006 | InsertValue006InsertFutureTimestampAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| 007 | InsertValue007InsertMinTimestampAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| 008 | InsertValue008InsertLargeValueAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| 009 | InsertValue009InsertNegativeValueAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| 010 | InsertValue010InsertZeroValueAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| 011 | InsertValue011InsertNaNValueAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| 012 | InsertValue012InsertInfinityValueAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| 014 | InsertValue014InsertDuplicateTimestampAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| 015 | InsertValue015InsertOutOfOrderTimestampsAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| 016 | InsertValue016InsertWithServerTimestampAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| 017 | InsertValue017InsertEmptyValuesAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| 019 | InsertValue019InsertMultipleNodesSequentiallyAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| Err-001 | InsertValueErr001InvalidNodeIdAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| Err-002 | InsertValueErr002NullNodeIdAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| Err-005 | InsertValueErr005NonHistoricalNodeAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ⏭️ |
| Err-006 | InsertValueErr006ObjectNodeAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ⏭️ |
| Err-007 | InsertValueErr007MethodNodeAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ⏭️ |
| Err-008 | InsertValueErr008EmptyUpdateDetailsAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| Err-009 | InsertValueErr009NullExtensionObjectAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ⏭️ |

</details>

<details>
<summary>Historical Access / Historical Access Modified Values ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | ModifiedValues001ReadModifiedValuesAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |

</details>

<details>
<summary>Historical Access / Historical Access Read Raw , 7 additional ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | HistoryReadRawDataReturnsResultAsync | [HistoricalAccessTests](HistoricalAccess/HistoricalAccessTests.cs) | ✅ |
| 001 | HistoryReadWithReadRawModifiedDetailsVerifyStructureAsync | [HistoricalAccessTests](HistoricalAccess/HistoricalAccessTests.cs) | ✅ |
| 001 | ReadRaw001ReadWithTimeRangeAndNumValuesAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| 002 | HistoryReadWithStartTimeAfterEndTimeAsync | [HistoricalAccessTests](HistoricalAccess/HistoricalAccessTests.cs) | ✅ |
| 002 | HistoryReadWithTimeRangeAsync | [HistoricalAccessTests](HistoricalAccess/HistoricalAccessTests.cs) | ✅ |
| 002 | ReadRaw002ReadWithStartTimeOnlyAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| 003 | ReadRaw003ReadWithEndTimeOnlyAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| 004 | ReadRaw004ReadWithNumValuesOnlyAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ⏭️ |
| 005 | ReadRaw005ReadWithReturnBoundsTrueAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| 006 | ReadRaw006ReadWithReturnBoundsFalseAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| 007 | ReadRaw007ReadSingleValueAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| 008 | HistoryReadWithContinuationPointAsync | [HistoricalAccessTests](HistoricalAccess/HistoricalAccessTests.cs) | ✅ |
| 008 | ReadRaw008ReadWithContinuationPointAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| 009 | ReadRaw009ReadWithStartTimeEqualsEndTimeAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| 010 | ReadRaw010ReadWithStartAfterEndAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| 011 | ReadRaw011ReadWithLargeNumValuesAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| 012 | ReadRaw012ReadWithTimestampsToReturnSourceAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| 013 | ReadRaw013ReadWithTimestampsToReturnServerAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| 014 | ReadRaw014ReadWithTimestampsToReturnNeitherAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| 015 | ReadRaw015ReadWithBoundsAndNumValuesAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| 016 | HistoryReadWithMaxValuesAsync | [HistoricalAccessTests](HistoricalAccess/HistoricalAccessTests.cs) | ✅ |
| 016 | HistoryReadWithNumValuesPerNodeLimitAsync | [HistoricalAccessTests](HistoricalAccess/HistoricalAccessTests.cs) | ✅ |
| 016 | ReadRaw016ReadWithNarrowTimeRangeAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| 017 | ReadRaw017ReadWithWideTimeRangeAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| 018 | ReadRaw018ReadWithIndexRangeAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| 019 | ReadRaw019ReadWithDataEncodingAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| 020 | ReadRaw020ReadMultipleNodesAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| 022 | ReadRaw022ReadWithGoodDataQualityAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| 023 | ReadRaw023ReadReleaseContinuationPointAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| Err-001 | ReadRawErr001InvalidNodeIdAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| Err-002 | ReadRawErr002NullNodeIdAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| Err-003 | ReadRawErr003InvalidTimestampsToReturnAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| Err-004 | ReadRawErr004EmptyNodesToReadAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| Err-005 | ReadRawErr005NullHistoryReadDetailsAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| Err-006 | ReadRawErr006BadIndexRangeAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| Err-007 | ReadRawErr007BadDataEncodingAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| Err-008 | HistoryReadNonExistentNodeAsync | [HistoricalAccessTests](HistoricalAccess/HistoricalAccessTests.cs) | ✅ |
| Err-008 | ReadRawErr008NodeIdOfNonHistoricalNodeAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ⏭️ |
| Err-009 | ReadRawErr009ReleasedContinuationPointReuseAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ⏭️ |
| Err-010 | ReadRawErr010InvalidContinuationPointAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ⏭️ |
| Err-011 | ReadRawErr011Obsoleted | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| Err-012 | ReadRawErr012NumericNodeIdInvalidNamespaceAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| Err-013 | ReadRawErr013StringNodeIdInvalidNamespaceAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| Err-014 | ReadRawErr014OpaqueNodeIdInvalidNamespaceAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| Err-015 | ReadRawErr015GuidNodeIdInvalidNamespaceAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| Err-016 | ReadRawErr016MaxNodesPerHistoryReadExceededAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| Err-017 | ReadRawErr017MixValidAndInvalidNodesAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| Err-018 | ReadRawErr018NoTimeRangeNoNumValuesAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ⏭️ |
| Err-019 | ReadRawErr019ObjectNodeIdAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| Err-021 | ReadRawErr021ReadWithFutureTimeRangeAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| Err-022 | ReadRawErr022ReadMethodNodeIdAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| Err-023 | ReadRawErr023ReadViewNodeIdAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| Err-024 | ReadRawErr024ReadDataTypeNodeIdAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| Err-025 | ReadRawErr025ReadReferenceTypeNodeIdAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| Err-026 | ReadRawErr026ReadObjectTypeNodeIdAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| Err-027 | ReadRawErr027ReadVariableTypeNodeIdAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |

**Additional coverage** (not mapped to specific source scripts):

| NUnit Test | Fixture | Status |
|-----------|---------|--------|
| HistoryReadMultipleNodesAtOnce | [HistoricalAccessTests](HistoricalAccess/HistoricalAccessTests.cs) | ✅ |
| HistoryReadServerCurrentTime | [HistoricalAccessTests](HistoricalAccess/HistoricalAccessTests.cs) | ✅ |
| HistoryReadWithIsReadModifiedTrue | [HistoricalAccessTests](HistoricalAccess/HistoricalAccessTests.cs) | ✅ |
| HistoryUpdateDelete | [HistoricalAccessTests](HistoricalAccess/HistoricalAccessTests.cs) | ⏭️ |
| HistoryUpdateInsert | [HistoricalAccessTests](HistoricalAccess/HistoricalAccessTests.cs) | ⏭️ |
| HistoryUpdateWithDeleteRawModifiedDetails | [HistoricalAccessTests](HistoricalAccess/HistoricalAccessTests.cs) | ⏭️ |
| HistoryUpdateWithUpdateDataDetails | [HistoricalAccessTests](HistoricalAccess/HistoricalAccessTests.cs) | ⏭️ |

</details>

<details>
<summary>Historical Access / Historical Access ServerTimestamp ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | ServerTimestamp001ReadWithServerTimestampAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| 002 | ServerTimestamp002ReadWithServerTimestampAndBoundsAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |

</details>

<details>
<summary>Historical Access / Historical Access Update Value ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | UpdateValue001UpdateSingleValueAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |
| 002 | UpdateValue002UpdateMultipleValuesAsync | [HistoricalAccessDepthTests](HistoricalAccess/HistoricalAccessDepthTests.cs) | ✅ |

</details>

### Information Model

<details>
<summary>Information Model / Base Info AssociatedWith ⏭️</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | AssociatedWithIsSubtypeOfNonHierarchicalReferencesAsync | [BaseInfoReferenceTypeTests](InformationModel/BaseInfoReferenceTypeTests.cs) | ⏭️ |

</details>

<details>
<summary>Information Model / Base Info Audio Type ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | AudioDataTypeExistsAsync | [BaseInfoDataTypeTests](InformationModel/BaseInfoDataTypeTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info Base Types ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 003 | AssociatedWithAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ⏭️ |
| 003 | AudioTypeAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | AvailableStatesTransitionsAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | BaseDataVariableTypeExistsAsync | [BaseInfoSingleCuTests](InformationModel/BaseInfoSingleCuTests.cs) | ✅ |
| 003 | BitFieldMaskDataTypeAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | CapsSubsMaxMIAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | CapsSubsMaxSubsAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | ContentFilterAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | ControlsAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ⏭️ |
| 003 | CoreStructure2Async | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | CurrencyAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | DateDataTypesAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | DecimalDataTypeAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | DecimalStringDataTypeAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | DeprecatedInformationAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | DeviceFailureAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | EUInformationAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | EngineeringUnitsAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | EstimatedReturnTimeAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | EventQueueOverflowEventTypeAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | EventsCapabilitiesAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | ExportFileFormatAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | FiniteStateMachineInstanceAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | GetMonitoredItemsBrowseNameAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | GetMonitoredItemsExistsAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | GetMonitoredItemsInputArgsAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | GetMonitoredItemsOutputArgsAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | HandleDataTypeAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | HasAttachedComponentAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ⏭️ |
| 003 | HasContainedComponentAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ⏭️ |
| 003 | HasOrderedComponentAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | HasPhysicalComponentAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ⏭️ |
| 003 | HistoryReadCapabilitiesAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | HistoryReadDataCapabilitiesAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | HistoryReadEventsCapabilitiesAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | HistoryUpdateDataCapabilitiesAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | HistoryUpdateEventsCapabilitiesAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | ImageDataTypesAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | ImportFileFormatAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | IsExecutableOnAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ⏭️ |
| 003 | IsExecutingOnAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ⏭️ |
| 003 | IsHostedByAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ⏭️ |
| 003 | IsPhysicallyConnectedToAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ⏭️ |
| 003 | KeyValuePairAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | LocalTimeAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | MaxMonitoredItemsQueueSizeAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | MethodArgumentDataTypeAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | MethodCapabilitiesAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | NamespaceMetadataChildrenAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | NamespaceMetadataFolderAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | NamespaceMetadataUriAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | NodeManagementCapabilitiesAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | NormalizedStringDataTypeAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | OptionSetAccessLevelExAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | OptionSetDataTypeAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | OptionSetEventNotifierAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | OptionSetUserWriteMaskAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | OptionSetWriteMaskAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | OrderedListAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | PlaceholderMandatoryAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | PlaceholderOptionalAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | PortableIDsAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | ProgressEventsExistsAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | ProgressEventsIsSubtypeAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | ProgressEventsPropertiesAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | PropertyTypeExistsAsync | [BaseInfoSingleCuTests](InformationModel/BaseInfoSingleCuTests.cs) | ✅ |
| 003 | QueryCapabilitiesAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | RangeDataTypeAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | RationalNumberAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ⏭️ |
| 003 | ReferenceDescriptionAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ⏭️ |
| 003 | RepresentsSameEntityAsAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ⏭️ |
| 003 | RepresentsSameFunctionalityAsAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ⏭️ |
| 003 | RepresentsSameHardwareAsAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | RequestServerStateChangeAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ⏭️ |
| 003 | RequiresAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ⏭️ |
| 003 | ResendDataBrowseNameAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | ResendDataExistsAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | ResendDataInputArgsAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | SecurityRoleCapabilitiesAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | SelectionListDescriptionsAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | SelectionListExistsAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | SelectionListSelectionsAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | SemanticChangeAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | SemanticVersionStringAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | ServerCaps2ConformanceUnitsAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | ServerCaps2LocaleIdArrayAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | ServerCaps2MaxArrayLengthAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | ServerCaps2MaxBrowseCPsAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | ServerCaps2MaxByteStringLengthAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | ServerCaps2MaxHistoryCPsAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | ServerCaps2MaxMIPerSubAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | ServerCaps2MaxQueryCPsAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | ServerCaps2MaxSessionsAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | ServerCaps2MaxStringLengthAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | ServerCaps2MaxSubsPerSessionAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | ServerCaps2MinSampleRateAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | ServerCaps2OperationLimitsAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | ServerCaps2ProfileArrayAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | ServerCaps2SoftwareCertsAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | ServerTypeAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | SpatialDataCartesianAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | SpatialDataThreeDAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | StateMachineInstanceAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | StatusResultDataTypeAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | SubvariablesOfStructuresAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ⏭️ |
| 003 | SystemStatusCurrentTimeAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | SystemStatusStartTimeAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | SystemStatusStateAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | SystemStatusUnderlyingAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | TrimmedStringAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ⏭️ |
| 003 | TypeInformationAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | UaBinaryFileAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | UriStringAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ✅ |
| 003 | UtilizesAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ⏭️ |
| 003 | ValueAsTextAsync | [BaseInfoParityTests](InformationModel/BaseInfoParityTests.cs) | ⏭️ |

</details>

<details>
<summary>Information Model / Base Info BitFieldMaskDataType ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | BitFieldMaskDataTypeIsSubtypeOfUInt64Async | [BaseInfoDataTypeTests](InformationModel/BaseInfoDataTypeTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info Choice States ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | ChoiceStateTypeExistsAsync | [BaseInfoServerTests](InformationModel/BaseInfoServerTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info ContentFilter ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | ContentFilterElementExistsAsync | [BaseInfoDataTypeTests](InformationModel/BaseInfoDataTypeTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info Controls ⏭️</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | ControlsIsSubtypeOfHierarchicalReferencesAsync | [BaseInfoReferenceTypeTests](InformationModel/BaseInfoReferenceTypeTests.cs) | ⏭️ |

</details>

<details>
<summary>Information Model / Base Info Core Structure 2 ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | StructureDataTypeExistsAndHasChildrenAsync | [BaseInfoDataTypeTests](InformationModel/BaseInfoDataTypeTests.cs) | ✅ |
| 002 | StructureHasUnionAndOptionalFieldsSubtypesAsync | [BaseInfoDataTypeTests](InformationModel/BaseInfoDataTypeTests.cs) | ⏭️ |

</details>

<details>
<summary>Information Model / Base Info Core Types Folders ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | BrowseTypesFolderSubfoldersAsync | [BaseInfoServerTests](InformationModel/BaseInfoServerTests.cs) | ✅ |
| 001 | FolderTypeExistsAsync | [BaseInfoSingleCuTests](InformationModel/BaseInfoSingleCuTests.cs) | ✅ |
| 001 | FolderTypeHasCorrectReferencesAsync | [BaseInfoSingleCuTests](InformationModel/BaseInfoSingleCuTests.cs) | ✅ |
| 002 | VerifyTypeFolderContentsAsync | [BaseInfoServerTests](InformationModel/BaseInfoServerTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info Core Views Folder ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | BrowseViewsFolderExistsAsync | [BaseInfoServerTests](InformationModel/BaseInfoServerTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info Currency ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | CurrencyUnitTypeExistsAsync | [BaseInfoDataTypeTests](InformationModel/BaseInfoDataTypeTests.cs) | ✅ |
| 002 | CurrencyUnitTypeHasAlphabeticCodeAsync | [BaseInfoDataTypeTests](InformationModel/BaseInfoDataTypeTests.cs) | ⏭️ |
| 003 | CurrencyUnitTypeHasCurrencyAsync | [BaseInfoDataTypeTests](InformationModel/BaseInfoDataTypeTests.cs) | ⏭️ |
| 004 | CurrencyUnitTypeHasExponentAsync | [BaseInfoDataTypeTests](InformationModel/BaseInfoDataTypeTests.cs) | ⏭️ |

</details>

<details>
<summary>Information Model / Base Info Custom Type System ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | BrowseDataTypesFolderAsync | [BaseInfoServerTests](InformationModel/BaseInfoServerTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info Date DataTypes ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | DateDataTypesExistUnderStringAsync | [BaseInfoDataTypeTests](InformationModel/BaseInfoDataTypeTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info Decimal DataType ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | DecimalDataTypeExistsAsync | [BaseInfoDataTypeTests](InformationModel/BaseInfoDataTypeTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info DecimalString DataType ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | DecimalStringIsSubtypeOfStringAsync | [BaseInfoDataTypeTests](InformationModel/BaseInfoDataTypeTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info Deprecated Information ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | DeprecatedPropertyExistsAsync | [BaseInfoServerTests](InformationModel/BaseInfoServerTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info Device Failure ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 000 | DeviceFailure000BrowseSubtypesAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |
| 000 | VerifyDeviceFailureEventTypeExistsAsync | [BaseInfoMiscTests](InformationModel/BaseInfoMiscTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info Diagnostics ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 000 | Diagnostics000ReadEnabledFlagAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |
| 001 | Diagnostics001ReadServerDiagnosticsSummaryAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |
| 001 | ReadServerDiagnosticsEnabledFlagAsync | [BaseInfoMiscTests](InformationModel/BaseInfoMiscTests.cs) | ✅ |
| 001 | ReadServerDiagnosticsEnabledFlagAsync | [DiagnosticsTests](InformationModel/DiagnosticsTests.cs) | ✅ |
| 002 | Diagnostics002ReadServerViewCountAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |
| 002 | ReadDiagnosticsSummaryCumulatedSessionCountAsync | [DiagnosticsTests](InformationModel/DiagnosticsTests.cs) | ✅ |
| 002 | ReadDiagnosticsSummaryCurrentSessionCountAsync | [DiagnosticsTests](InformationModel/DiagnosticsTests.cs) | ✅ |
| 002 | ReadDiagnosticsSummaryCurrentSubscriptionCountAsync | [DiagnosticsTests](InformationModel/DiagnosticsTests.cs) | ✅ |
| 002 | ReadDiagnosticsSummaryServerViewCountAsync | [DiagnosticsTests](InformationModel/DiagnosticsTests.cs) | ✅ |
| 002 | ReadServerCurrentTimeIsRecentAsync | [DiagnosticsTests](InformationModel/DiagnosticsTests.cs) | ✅ |
| 002 | ReadServerDiagnosticsSummaryAsync | [DiagnosticsTests](InformationModel/DiagnosticsTests.cs) | ✅ |
| 002 | ReadServerStateIsRunningAsync | [DiagnosticsTests](InformationModel/DiagnosticsTests.cs) | ✅ |
| 002 | ReadSessionDiagnosticsSummaryNodeAsync | [BaseInfoMiscTests](InformationModel/BaseInfoMiscTests.cs) | ✅ |
| 002 | ReadSessionsDiagnosticsSummaryAsync | [DiagnosticsTests](InformationModel/DiagnosticsTests.cs) | ✅ |
| 002 | ServerDiagnosticsNodeBrowseHasChildrenAsync | [DiagnosticsTests](InformationModel/DiagnosticsTests.cs) | ✅ |
| 003 | Diagnostics003ReadCurrentSessionCountAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |
| 004 | Diagnostics004ReadCumulatedSessionCountAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |
| 004 | SamplingIntervalDiagnosticsArrayExistsAsync | [BaseInfoSingleCuTests](InformationModel/BaseInfoSingleCuTests.cs) | ⏭️ |
| 005 | Diagnostics005ReadSecurityRejectedSessionCountAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |
| 005 | SubscriptionDiagnosticsArrayExistsAsync | [BaseInfoSingleCuTests](InformationModel/BaseInfoSingleCuTests.cs) | ✅ |
| 006 | Diagnostics006ReadSessionAbortCountAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |
| 007 | Diagnostics007ReadPublishingIntervalCountAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |
| 008 | Diagnostics008ReadCurrentSubscriptionCountAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |
| 009 | Diagnostics009ReadCumulatedSubscriptionCountAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |
| 010 | Diagnostics010ReadSecurityRejectedRequestsCountAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |
| 011 | Diagnostics011ReadSamplingIntervalDiagnosticsArrayAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ⏭️ |
| 012 | Diagnostics012ReadSubscriptionDiagnosticsArrayAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |
| 013 | Diagnostics013ReadSessionDiagnosticsArrayAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |
| 014 | Diagnostics014ReadSessionSecurityDiagnosticsArrayAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |
| 015 | Diagnostics015VerifyEnabledFlagToggleAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ⏭️ |
| 016 | Diagnostics016ReadRejectedSessionCountAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |
| 017 | Diagnostics017ReadRejectedRequestsCountAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |
| 018-1 | Diagnostics0181BrowseSessionDiagnosticsAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |
| 018-2 | Diagnostics0182BrowseSessionSecurityDiagnosticsAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ⏭️ |
| 018-3 | Diagnostics0183BrowseSubscriptionDiagnosticsAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ⏭️ |
| 019 | Diagnostics019ReadServerStatusAfterDiagnosticsAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |
| 019 | SessionDiagnosticsArrayExistsAsync | [BaseInfoSingleCuTests](InformationModel/BaseInfoSingleCuTests.cs) | ✅ |
| 023 | Diagnostics023EnabledFlagIsBoolAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |
| 024 | Diagnostics024SummaryAggregatesSessionDiagnosticsAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ⏭️ |

</details>

<details>
<summary>Information Model / Base Info EUInformation ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | EUInformationExistsAsync | [BaseInfoDataTypeTests](InformationModel/BaseInfoDataTypeTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info Engineering Units ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | EUInformationStructureExistsAsync | [BaseInfoServerTests](InformationModel/BaseInfoServerTests.cs) | ✅ |
| 002 | BrowseAnalogItemTypeForEngineeringUnitsAsync | [BaseInfoServerTests](InformationModel/BaseInfoServerTests.cs) | ⏭️ |
| 003 | ReadEngineeringUnitsValueAsync | [BaseInfoServerTests](InformationModel/BaseInfoServerTests.cs) | ⏭️ |
| 004 | ReadEURangeAndVerifyStructureAsync | [BaseInfoServerTests](InformationModel/BaseInfoServerTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info Estimated Return Time ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | BrowseServerTypeForEstimatedReturnTimeAsync | [BaseInfoServerTests](InformationModel/BaseInfoServerTests.cs) | ✅ |
| 002 | ReadEstimatedReturnTimeValueAsync | [BaseInfoServerTests](InformationModel/BaseInfoServerTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info EventQueueOverflow EventType ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | EventQueueOverflow001TypeExistsAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |
| 002 | EventQueueOverflow002IsSubtypeOfBaseEventAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |
| 003 | EventQueueOverflow003StandardEventFieldsAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info Events Capabilities ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | BrowseServerCapabilitiesForEventPropertiesAsync | [BaseInfoServerTests](InformationModel/BaseInfoServerTests.cs) | ✅ |
| 001 | ConditionTypeExistsAsync | [BaseInfoSingleCuTests](InformationModel/BaseInfoSingleCuTests.cs) | ✅ |
| 001 | DialogConditionTypeExistsAsync | [BaseInfoSingleCuTests](InformationModel/BaseInfoSingleCuTests.cs) | ✅ |
| 001 | ExclusiveLimitAlarmTypeExistsAsync | [BaseInfoSingleCuTests](InformationModel/BaseInfoSingleCuTests.cs) | ✅ |
| 001 | VerifyAuditEventTypeExistsAsync | [BaseInfoMiscTests](InformationModel/BaseInfoMiscTests.cs) | ✅ |
| 001 | VerifyBaseEventTypeExistsAsync | [BaseInfoMiscTests](InformationModel/BaseInfoMiscTests.cs) | ✅ |
| 001 | VerifyBaseEventTypeHasEventIdAsync | [BaseInfoMiscTests](InformationModel/BaseInfoMiscTests.cs) | ✅ |
| 001 | VerifyBaseEventTypeHasEventTypeAsync | [BaseInfoMiscTests](InformationModel/BaseInfoMiscTests.cs) | ✅ |
| 001 | VerifyBaseEventTypeHasMessageAsync | [BaseInfoMiscTests](InformationModel/BaseInfoMiscTests.cs) | ✅ |
| 001 | VerifyBaseEventTypeHasSeverityAsync | [BaseInfoMiscTests](InformationModel/BaseInfoMiscTests.cs) | ✅ |
| 001 | VerifyBaseEventTypeHasSourceNameAsync | [BaseInfoMiscTests](InformationModel/BaseInfoMiscTests.cs) | ✅ |
| 001 | VerifyBaseEventTypeHasSourceNodeAsync | [BaseInfoMiscTests](InformationModel/BaseInfoMiscTests.cs) | ✅ |
| 001 | VerifyBaseEventTypeHasTimeAsync | [BaseInfoMiscTests](InformationModel/BaseInfoMiscTests.cs) | ✅ |
| 001 | VerifySystemEventTypeExistsAsync | [BaseInfoMiscTests](InformationModel/BaseInfoMiscTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info Export File Format ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | ExportNamespaceMethodExistsAsync | [BaseInfoServerTests](InformationModel/BaseInfoServerTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info Fixed SamplingInterval ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | ReadMinSupportedSampleRateFixedAsync | [BaseInfoServerTests](InformationModel/BaseInfoServerTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info GetMonitoredItems Method ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | GetMonitoredItems001BrowseMethodAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |
| 002 | GetMonitoredItems002CallWithValidSubscriptionAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |
| 003 | GetMonitoredItems003EmptySubscriptionAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |
| 004 | GetMonitoredItems004MultipleSubscriptionsAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |
| Err-001 | GetMonitoredItemsErr001InvalidSubscriptionIdAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |
| Err-003 | GetMonitoredItemsErr003CrossSessionReturnsBadStatusAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info Handle DataType ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | HandleIsSubtypeOfUInt32Async | [BaseInfoDataTypeTests](InformationModel/BaseInfoDataTypeTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info HasAttachedComponent ⏭️</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | HasAttachedComponentIsSubtypeOfHasPhysicalComponentAsync | [BaseInfoReferenceTypeTests](InformationModel/BaseInfoReferenceTypeTests.cs) | ⏭️ |

</details>

<details>
<summary>Information Model / Base Info HasContainedComponent ⏭️</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | HasContainedComponentIsSubtypeOfHasPhysicalComponentAsync | [BaseInfoReferenceTypeTests](InformationModel/BaseInfoReferenceTypeTests.cs) | ⏭️ |

</details>

<details>
<summary>Information Model / Base Info HasOrderedComponent ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | HasOrderedComponentIsSubtypeOfHasComponentAsync | [BaseInfoReferenceTypeTests](InformationModel/BaseInfoReferenceTypeTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info HasPhysicalComponent ⏭️</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | HasPhysicalComponentIsSubtypeOfHasComponentAsync | [BaseInfoReferenceTypeTests](InformationModel/BaseInfoReferenceTypeTests.cs) | ⏭️ |

</details>

<details>
<summary>Information Model / Base Info Image DataTypes ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | ImageDataTypesExistAsync | [BaseInfoDataTypeTests](InformationModel/BaseInfoDataTypeTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info Import File Format ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | ImportNamespaceMethodExistsAsync | [BaseInfoServerTests](InformationModel/BaseInfoServerTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info IsExecutableOn ⏭️</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | IsExecutableOnIsSubtypeOfNonHierarchicalReferencesAsync | [BaseInfoReferenceTypeTests](InformationModel/BaseInfoReferenceTypeTests.cs) | ⏭️ |

</details>

<details>
<summary>Information Model / Base Info IsExecutingOn ⏭️</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | IsExecutingOnIsSubtypeOfUtilizesAsync | [BaseInfoReferenceTypeTests](InformationModel/BaseInfoReferenceTypeTests.cs) | ⏭️ |

</details>

<details>
<summary>Information Model / Base Info IsHostedBy ⏭️</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | IsHostedByIsSubtypeOfUtilizesAsync | [BaseInfoReferenceTypeTests](InformationModel/BaseInfoReferenceTypeTests.cs) | ⏭️ |

</details>

<details>
<summary>Information Model / Base Info IsPhysicallyConnectedTo ⏭️</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | IsPhysicallyConnectedToIsSubtypeOfNonHierarchicalReferencesAsync | [BaseInfoReferenceTypeTests](InformationModel/BaseInfoReferenceTypeTests.cs) | ⏭️ |

</details>

<details>
<summary>Information Model / Base Info KeyValuePair ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | KeyValuePairStructureExistsAsync | [BaseInfoDataTypeTests](InformationModel/BaseInfoDataTypeTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info LocalTime ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | TimeZoneDataTypeExistsAsync | [BaseInfoServerTests](InformationModel/BaseInfoServerTests.cs) | ✅ |
| 002 | ReadServerStatusCurrentTimeAndCheckTimeZoneAsync | [BaseInfoServerTests](InformationModel/BaseInfoServerTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info LocalTime Events ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | VerifyLocalTimeEventFieldAvailableAsync | [BaseInfoServerTests](InformationModel/BaseInfoServerTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info Locations Object ⏭️</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | BrowseServerForLocationsObjectAsync | [BaseInfoServerTests](InformationModel/BaseInfoServerTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info Method Argument DataType ⏭️</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | ArgumentDataTypeExistsAsync | [BaseInfoDataTypeTests](InformationModel/BaseInfoDataTypeTests.cs) | ⏭️ |

</details>

<details>
<summary>Information Model / Base Info Method Capabilities ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | ReadMaxNodesPerMethodCallAsync | [BaseInfoServerTests](InformationModel/BaseInfoServerTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info Model Change ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | VerifyModelChangeStructureDataTypeExistsAsync | [BaseInfoServerTests](InformationModel/BaseInfoServerTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info Model Change General ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | VerifyGeneralModelChangeEventTypeExistsAsync | [BaseInfoServerTests](InformationModel/BaseInfoServerTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info Namespace Metadata ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | BrowseNamespacesAndReadUaMetadataAsync | [BaseInfoServerTests](InformationModel/BaseInfoServerTests.cs) | ✅ |
| 002 | BrowseNamespacesFolderTypeIsNamespacesTypeAsync | [BaseInfoServerTests](InformationModel/BaseInfoServerTests.cs) | ✅ |
| 003 | BrowseNamespaceEntriesHaveNamespaceUriAsync | [BaseInfoServerTests](InformationModel/BaseInfoServerTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info NormalizedString DataType ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | NormalizedStringIsSubtypeOfStringAsync | [BaseInfoDataTypeTests](InformationModel/BaseInfoDataTypeTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info OptionSet ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | AccessLevelBitsAreConsistentAsync | [BaseInfoOptionSetTests](InformationModel/BaseInfoOptionSetTests.cs) | ✅ |
| 001 | BrowseOptionSetTypeChildrenAsync | [BaseInfoOptionSetTests](InformationModel/BaseInfoOptionSetTests.cs) | ✅ |
| 001 | OptionSet001ReadAccessLevelExOnServerStatusStateAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |
| 001 | ReadAccessLevelAttributeAsOptionSetAsync | [BaseInfoOptionSetTests](InformationModel/BaseInfoOptionSetTests.cs) | ✅ |
| 001 | ReadAccessLevelContainsCurrentReadBitAsync | [BaseInfoOptionSetTests](InformationModel/BaseInfoOptionSetTests.cs) | ✅ |
| 001 | ReadAccessLevelContainsCurrentWriteBitAsync | [BaseInfoOptionSetTests](InformationModel/BaseInfoOptionSetTests.cs) | ✅ |
| 001 | ReadEventNotifierAttributeAsync | [BaseInfoOptionSetTests](InformationModel/BaseInfoOptionSetTests.cs) | ✅ |
| 001 | ReadUserAccessLevelAttributeAsync | [BaseInfoOptionSetTests](InformationModel/BaseInfoOptionSetTests.cs) | ✅ |
| 001 | ReadUserWriteMaskAttributeExistsAsync | [BaseInfoOptionSetTests](InformationModel/BaseInfoOptionSetTests.cs) | ✅ |
| 001 | ReadWriteMaskAttributeExistsAsync | [BaseInfoOptionSetTests](InformationModel/BaseInfoOptionSetTests.cs) | ✅ |
| 001 | VerifyAccessLevelExTypeAttributeAsync | [BaseInfoOptionSetTests](InformationModel/BaseInfoOptionSetTests.cs) | ✅ |
| 001 | VerifyOptionSetTypeExistsInTypeHierarchyAsync | [BaseInfoOptionSetTests](InformationModel/BaseInfoOptionSetTests.cs) | ⏭️ |
| 001 | WriteMaskDecodedAsUInt32Async | [BaseInfoOptionSetTests](InformationModel/BaseInfoOptionSetTests.cs) | ✅ |
| 002 | OptionSet002ReadWriteMaskOnServerAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |
| 003 | OptionSet003ReadUserWriteMaskOnServerAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |
| 004 | OptionSet004ReadEventNotifierOnServerAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |
| 005 | OptionSet005BrowseServerCapabilitiesForAccessRestrictionsAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |
| 006 | OptionSet006ReadAccessRestrictionsAttributeAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ⏭️ |
| 007 | OptionSet007ReadRolePermissionsOnServerAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ⏭️ |
| 008 | OptionSet008ReadUserRolePermissionsAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ⏭️ |
| 009 | OptionSet009BrowseDataTypeDefinitionEnumerationAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |
| 010 | OptionSet010ReadDataTypeDefinitionStructureAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |
| 011 | OptionSet011ReadAccessLevelExOnWritableVariableAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |
| 012 | OptionSet012VerifyWriteMaskBitsAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |
| 013 | OptionSet013VerifyUserWriteMaskBitsAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |
| 014 | OptionSet014VerifyAccessLevelOnVariableAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |
| 015 | OptionSet015VerifyUserAccessLevelOnVariableAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info OptionSet DataType ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | OptionSetDataTypeExistsAsync | [BaseInfoDataTypeTests](InformationModel/BaseInfoDataTypeTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info OrderedList ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | OrderedList001TypeExistsAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |
| 002 | OrderedList002IOrderedObjectTypeExistsAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info Placeholder Modelling Rules ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | ModellingRuleMandatoryExistsAsync | [BaseInfoSingleCuTests](InformationModel/BaseInfoSingleCuTests.cs) | ✅ |
| 001 | ModellingRuleMandatoryPlaceholderExistsAsync | [BaseInfoSingleCuTests](InformationModel/BaseInfoSingleCuTests.cs) | ✅ |
| 001 | ModellingRuleOptionalExistsAsync | [BaseInfoSingleCuTests](InformationModel/BaseInfoSingleCuTests.cs) | ✅ |
| 001 | ModellingRuleOptionalPlaceholderExistsAsync | [BaseInfoSingleCuTests](InformationModel/BaseInfoSingleCuTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info Portable IDs ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | PortableNodeIdAndQualifiedNameExistAsync | [BaseInfoDataTypeTests](InformationModel/BaseInfoDataTypeTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info Progress Events ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | ProgressEvents001TypeExistsAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |
| 001 | VerifyProgressEventTypeExistsAsync | [BaseInfoMiscTests](InformationModel/BaseInfoMiscTests.cs) | ✅ |
| 002 | ProgressEvents002VerifyPropertiesAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |
| 003 | ProgressEvents003IsSubtypeOfBaseEventAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info Query Capabilities ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | ReadMaxQueryContinuationPointsQueryAsync | [BaseInfoServerTests](InformationModel/BaseInfoServerTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info Range DataType ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | RangeDataTypeExistsAsync | [BaseInfoDataTypeTests](InformationModel/BaseInfoDataTypeTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info Rational Number ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | RationalNumberTypeHasComponentsAsync | [BaseInfoDataTypeTests](InformationModel/BaseInfoDataTypeTests.cs) | ⏭️ |
| 002 | RationalNumberDataTypeExistsAsync | [BaseInfoDataTypeTests](InformationModel/BaseInfoDataTypeTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info ReferenceDescription ⏭️</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | ReferenceDescriptionDataTypeExistsAsync | [BaseInfoDataTypeTests](InformationModel/BaseInfoDataTypeTests.cs) | ⏭️ |

</details>

<details>
<summary>Information Model / Base Info RepresentsSameEntityAs ⏭️</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | RepresentsSameEntityAsIsSubtypeOfNonHierarchicalReferencesAsync | [BaseInfoReferenceTypeTests](InformationModel/BaseInfoReferenceTypeTests.cs) | ⏭️ |

</details>

<details>
<summary>Information Model / Base Info RepresentsSameFunctionalityAs ⏭️</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | RepresentsSameFunctionalityAsIsSubtypeOfRepresentsSameEntityAsAsync | [BaseInfoReferenceTypeTests](InformationModel/BaseInfoReferenceTypeTests.cs) | ⏭️ |

</details>

<details>
<summary>Information Model / Base Info RepresentsSameHardwareAs ⏭️</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | RepresentsSameHardwareAsIsSubtypeOfRepresentsSameEntityAsAsync | [BaseInfoReferenceTypeTests](InformationModel/BaseInfoReferenceTypeTests.cs) | ⏭️ |

</details>

<details>
<summary>Information Model / Base Info RequestServerStateChange Method ⏭️</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 000 | RequestServerStateChange000MethodExistsAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ⏭️ |

</details>

<details>
<summary>Information Model / Base Info Requires ⏭️</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | RequiresIsSubtypeOfHierarchicalReferencesAsync | [BaseInfoReferenceTypeTests](InformationModel/BaseInfoReferenceTypeTests.cs) | ⏭️ |

</details>

<details>
<summary>Information Model / Base Info ResendData Method ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 000 | ResendData000BrowseMethodAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |
| 001 | ResendData001CallWithReportingItemsAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |
| 002 | ResendData002 | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ⏭️ |
| 003 | ResendData003 | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ⏭️ |
| 004 | ResendData004 | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ⏭️ |
| 005 | ResendData005 | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ⏭️ |
| 006 | ResendData006 | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ⏭️ |
| 007 | ResendData007 | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ⏭️ |
| 008 | ResendData008 | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ⏭️ |
| 009 | ResendData009 | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ⏭️ |
| 010 | ResendData010 | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ⏭️ |
| Err-001 | ResendDataErr001NonexistentSubscriptionAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |
| Err-002 | ResendDataErr002CrossSession | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ⏭️ |
| Err-003 | ResendDataErr003NoSubscriptionsAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info Security Role Capabilities ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 000 | RoleSetExistsAsync | [BaseInfoSingleCuTests](InformationModel/BaseInfoSingleCuTests.cs) | ✅ |
| 000 | SecurityRoles000RoleSetExistsAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |
| 001 | SecurityRoles001BrowseRoleSetChildrenAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |
| 001 | WellKnownRolesAnonymousExistsAsync | [BaseInfoSingleCuTests](InformationModel/BaseInfoSingleCuTests.cs) | ✅ |
| 001 | WellKnownRolesAuthenticatedUserExistsAsync | [BaseInfoSingleCuTests](InformationModel/BaseInfoSingleCuTests.cs) | ✅ |
| 001 | WellKnownRolesConfigureAdminExistsAsync | [BaseInfoSingleCuTests](InformationModel/BaseInfoSingleCuTests.cs) | ✅ |
| 001 | WellKnownRolesEngineerExistsAsync | [BaseInfoSingleCuTests](InformationModel/BaseInfoSingleCuTests.cs) | ✅ |
| 001 | WellKnownRolesObserverExistsAsync | [BaseInfoSingleCuTests](InformationModel/BaseInfoSingleCuTests.cs) | ✅ |
| 001 | WellKnownRolesOperatorExistsAsync | [BaseInfoSingleCuTests](InformationModel/BaseInfoSingleCuTests.cs) | ✅ |
| 001 | WellKnownRolesSecurityAdminExistsAsync | [BaseInfoSingleCuTests](InformationModel/BaseInfoSingleCuTests.cs) | ✅ |
| 001 | WellKnownRolesSupervisorExistsAsync | [BaseInfoSingleCuTests](InformationModel/BaseInfoSingleCuTests.cs) | ✅ |
| 002 | RoleHasIdentitiesPropertyAsync | [BaseInfoSingleCuTests](InformationModel/BaseInfoSingleCuTests.cs) | ⏭️ |
| 002 | SecurityRoles002BrowseRoleTypeInstanceAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |
| 003 | SecurityRoles003AllRolesHaveRequiredPropertiesAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info Selection List ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | SelectionList001SelectionsPropertyExistsAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |
| 002 | SelectionList002RestrictToListExistsAsync | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ✅ |
| 003 | SelectionList003 | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ⏭️ |
| 004 | SelectionList004 | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ⏭️ |
| 005 | SelectionList005 | [BaseInfoBehavioralTests](InformationModel/BaseInfoBehavioralTests.cs) | ⏭️ |

</details>

<details>
<summary>Information Model / Base Info SemanticChange ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | SemanticChangeEventTypeExistsAsync | [BaseInfoServerTests](InformationModel/BaseInfoServerTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info SemanticChange Bit ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | VerifySemanticChangeBitAsync | [BaseInfoServerTests](InformationModel/BaseInfoServerTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info SemanticVersionString ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | SemanticVersionStringIsSubtypeOfStringAsync | [BaseInfoDataTypeTests](InformationModel/BaseInfoDataTypeTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info Server Capabilities 2 ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | BrowseServerCapabilitiesOperationLimitsAsync | [BaseInfoCapabilitiesTests](InformationModel/BaseInfoCapabilitiesTests.cs) | ✅ |
| 001 | ReadBuildInfoProductNameAsync | [ServerCapabilitiesTests](InformationModel/ServerCapabilitiesTests.cs) | ✅ |
| 001 | ReadBuildInfoSoftwareVersionAsync | [ServerCapabilitiesTests](InformationModel/ServerCapabilitiesTests.cs) | ✅ |
| 001 | ReadLocaleIdArrayAsync | [ServerCapabilitiesTests](InformationModel/ServerCapabilitiesTests.cs) | ✅ |
| 001 | ReadMaxHistoryContinuationPointsAsync | [ServerCapabilitiesTests](InformationModel/ServerCapabilitiesTests.cs) | ✅ |
| 001 | ReadMaxQueryContinuationPointsAsync | [ServerCapabilitiesTests](InformationModel/ServerCapabilitiesTests.cs) | ✅ |
| 001 | ReadNamespaceArrayAsync | [ServerCapabilitiesTests](InformationModel/ServerCapabilitiesTests.cs) | ✅ |
| 001 | ReadRedundancySupportAsync | [BaseInfoCapabilitiesTests](InformationModel/BaseInfoCapabilitiesTests.cs) | ✅ |
| 001 | ReadServerArrayAsync | [ServerCapabilitiesTests](InformationModel/ServerCapabilitiesTests.cs) | ✅ |
| 001 | ReadServerNamespacesFolderAsync | [BaseInfoCapabilitiesTests](InformationModel/BaseInfoCapabilitiesTests.cs) | ✅ |
| 001 | ReadServerProfileArrayAsync | [BaseInfoServerTests](InformationModel/BaseInfoServerTests.cs) | ✅ |
| 001 | ReadServerProfileArrayAsync | [ServerCapabilitiesTests](InformationModel/ServerCapabilitiesTests.cs) | ✅ |
| 001 | ReadServerStatusCurrentTimeAsync | [ServerCapabilitiesTests](InformationModel/ServerCapabilitiesTests.cs) | ✅ |
| 001 | ReadServerStatusStartTimeAsync | [ServerCapabilitiesTests](InformationModel/ServerCapabilitiesTests.cs) | ✅ |
| 001 | ReadServerStatusStateAsync | [ServerCapabilitiesTests](InformationModel/ServerCapabilitiesTests.cs) | ✅ |
| 001 | VerifyModellingRuleMandatoryExistsAsync | [BaseInfoCapabilitiesTests](InformationModel/BaseInfoCapabilitiesTests.cs) | ✅ |
| 001 | VerifyModellingRuleOptionalExistsAsync | [BaseInfoCapabilitiesTests](InformationModel/BaseInfoCapabilitiesTests.cs) | ✅ |
| 001 | VerifyRolesFolderExistsAsync | [BaseInfoCapabilitiesTests](InformationModel/BaseInfoCapabilitiesTests.cs) | ✅ |
| 001 | VerifyServerRedundancyExistsAsync | [BaseInfoCapabilitiesTests](InformationModel/BaseInfoCapabilitiesTests.cs) | ✅ |
| 002 | ReadLocaleIdArrayAsync | [BaseInfoServerTests](InformationModel/BaseInfoServerTests.cs) | ✅ |
| 002 | ReadMinSupportedSampleRateAsync | [ServerCapabilitiesTests](InformationModel/ServerCapabilitiesTests.cs) | ✅ |
| 003 | ReadMaxBrowseContinuationPointsAsync | [ServerCapabilitiesTests](InformationModel/ServerCapabilitiesTests.cs) | ✅ |
| 003 | ReadMinSupportedSampleRateAsync | [BaseInfoServerTests](InformationModel/BaseInfoServerTests.cs) | ✅ |
| 004 | ReadMaxBrowseContinuationPointsAsync | [BaseInfoServerTests](InformationModel/BaseInfoServerTests.cs) | ✅ |
| 005 | ReadMaxQueryContinuationPointsAsync | [BaseInfoServerTests](InformationModel/BaseInfoServerTests.cs) | ✅ |
| 006 | ReadMaxHistoryContinuationPointsAsync | [BaseInfoServerTests](InformationModel/BaseInfoServerTests.cs) | ✅ |
| 007 | ReadSoftwareCertificatesAsync | [BaseInfoServerTests](InformationModel/BaseInfoServerTests.cs) | ✅ |
| 008 | ReadMaxArrayLengthAsync | [BaseInfoServerTests](InformationModel/BaseInfoServerTests.cs) | ✅ |
| 009 | ReadMaxStringLengthAsync | [BaseInfoServerTests](InformationModel/BaseInfoServerTests.cs) | ✅ |
| 010 | ReadMaxByteStringLengthAsync | [BaseInfoServerTests](InformationModel/BaseInfoServerTests.cs) | ✅ |
| 011 | ReadOperationLimitsMaxNodesPerHistoryReadDataAsync | [BaseInfoCapabilitiesTests](InformationModel/BaseInfoCapabilitiesTests.cs) | ✅ |
| 011 | ReadOperationLimitsMaxNodesPerHistoryReadEventsAsync | [BaseInfoCapabilitiesTests](InformationModel/BaseInfoCapabilitiesTests.cs) | ✅ |
| 011 | ReadOperationLimitsMaxNodesPerNodeManagementAsync | [BaseInfoCapabilitiesTests](InformationModel/BaseInfoCapabilitiesTests.cs) | ✅ |
| 011 | ReadOperationLimitsMaxNodesPerReadAsync | [ServerCapabilitiesTests](InformationModel/ServerCapabilitiesTests.cs) | ✅ |
| 011 | ReadOperationLimitsObjectExistsAsync | [BaseInfoServerTests](InformationModel/BaseInfoServerTests.cs) | ✅ |
| 012 | ReadMaxSessionsAsync | [BaseInfoServerTests](InformationModel/BaseInfoServerTests.cs) | ✅ |
| 012 | ReadOperationLimitsMaxNodesPerHistoryUpdateDataAsync | [BaseInfoCapabilitiesTests](InformationModel/BaseInfoCapabilitiesTests.cs) | ✅ |
| 012 | ReadOperationLimitsMaxNodesPerHistoryUpdateEventsAsync | [BaseInfoCapabilitiesTests](InformationModel/BaseInfoCapabilitiesTests.cs) | ✅ |
| 012 | ReadOperationLimitsMaxNodesPerWriteAsync | [ServerCapabilitiesTests](InformationModel/ServerCapabilitiesTests.cs) | ✅ |
| 013 | ReadMaxSubscriptionsPerSessionAsync | [BaseInfoServerTests](InformationModel/BaseInfoServerTests.cs) | ✅ |
| 013 | ReadOperationLimitsMaxNodesPerBrowseAsync | [ServerCapabilitiesTests](InformationModel/ServerCapabilitiesTests.cs) | ✅ |
| 014 | ReadMaxMonitoredItemsPerSubscriptionAsync | [BaseInfoServerTests](InformationModel/BaseInfoServerTests.cs) | ✅ |
| 015 | ReadConformanceUnitsAsync | [BaseInfoServerTests](InformationModel/BaseInfoServerTests.cs) | ✅ |
| 015 | ReadMaxMonitoredItemsPerSubscriptionAsync | [BaseInfoCapabilitiesTests](InformationModel/BaseInfoCapabilitiesTests.cs) | ✅ |
| 015 | ReadMaxMonitoredItemsQueueSizeAsync | [BaseInfoCapabilitiesTests](InformationModel/BaseInfoCapabilitiesTests.cs) | ✅ |
| 015 | ReadMaxSubscriptionsPerSessionAsync | [BaseInfoCapabilitiesTests](InformationModel/BaseInfoCapabilitiesTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info ServerType ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | BrowseServerTypeChildrenAsync | [BaseInfoServerTests](InformationModel/BaseInfoServerTests.cs) | ✅ |
| 001 | CurrentServerIdExistsIfRedundancyEnabledAsync | [RedundancyModelTests](InformationModel/RedundancyModelTests.cs) | ⏭️ |
| 001 | ReadServerServiceLevelAsync | [BaseInfoMiscTests](InformationModel/BaseInfoMiscTests.cs) | ✅ |
| 001 | RedundancySupportHasCorrectDataTypeAsync | [RedundancyModelTests](InformationModel/RedundancyModelTests.cs) | ✅ |
| 001 | RedundancySupportIsValidEnumAsync | [RedundancyModelTests](InformationModel/RedundancyModelTests.cs) | ✅ |
| 001 | ServerObjectHasServerRedundancyChildAsync | [RedundancyModelTests](InformationModel/RedundancyModelTests.cs) | ✅ |
| 001 | ServerRedundancyHasTypeDefinitionAsync | [RedundancyModelTests](InformationModel/RedundancyModelTests.cs) | ✅ |
| 001 | ServerUriArrayIsReadableAsync | [RedundancyModelTests](InformationModel/RedundancyModelTests.cs) | ⏭️ |

</details>

<details>
<summary>Information Model / Base Info Spatial Data ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | SpatialDataCoordinateTypesExistAsync | [BaseInfoDataTypeTests](InformationModel/BaseInfoDataTypeTests.cs) | ✅ |
| 002 | SpatialDataStructuresExistAsync | [BaseInfoDataTypeTests](InformationModel/BaseInfoDataTypeTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info State Machine Instance ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | BrowseStateMachineTypeForCurrentStateAsync | [BaseInfoServerTests](InformationModel/BaseInfoServerTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info StatusResult DataType ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | StatusResultDataTypeExistsAsync | [BaseInfoDataTypeTests](InformationModel/BaseInfoDataTypeTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info System Status ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | ReadServerStatusStateIsRunningAsync | [BaseInfoServerTests](InformationModel/BaseInfoServerTests.cs) | ✅ |
| 002 | ReadServerStatusStartTimeAndCurrentTimeAsync | [BaseInfoServerTests](InformationModel/BaseInfoServerTests.cs) | ✅ |
| 003 | ReadServerStatusSecondsTillShutdownAsync | [BaseInfoServerTests](InformationModel/BaseInfoServerTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info TrimmedString ⏭️</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | TrimmedStringIsSubtypeOfStringAsync | [BaseInfoDataTypeTests](InformationModel/BaseInfoDataTypeTests.cs) | ⏭️ |

</details>

<details>
<summary>Information Model / Base Info Type Information ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | ReadBuildInfoBuildDateExistsAsync | [BaseInformationTests](InformationModel/BaseInformationTests.cs) | ✅ |
| 001 | ReadBuildInfoBuildNumberExistsAsync | [BaseInformationTests](InformationModel/BaseInformationTests.cs) | ✅ |
| 001 | ReadBuildInfoManufacturerNameExistsAsync | [BaseInformationTests](InformationModel/BaseInformationTests.cs) | ✅ |
| 001 | ReadBuildInfoProductNameNotEmptyAsync | [BaseInformationTests](InformationModel/BaseInformationTests.cs) | ✅ |
| 001 | ReadBuildInfoSoftwareVersionExistsAsync | [BaseInformationTests](InformationModel/BaseInformationTests.cs) | ✅ |
| 001 | ReadConformanceUnitsExistsAsync | [BaseInformationTests](InformationModel/BaseInformationTests.cs) | ✅ |
| 001 | ReadDiagnosticsEnabledFlagExistsAsync | [BaseInformationTests](InformationModel/BaseInformationTests.cs) | ✅ |
| 001 | ReadMaxBrowseContinuationPointsPositiveAsync | [BaseInformationTests](InformationModel/BaseInformationTests.cs) | ✅ |
| 001 | ReadMaxHistoryContinuationPointsExistsAsync | [BaseInformationTests](InformationModel/BaseInformationTests.cs) | ✅ |
| 001 | ReadMaxQueryContinuationPointsExistsAsync | [BaseInformationTests](InformationModel/BaseInformationTests.cs) | ✅ |
| 001 | ReadNamespaceArrayContainsOpcUaAsync | [BaseInformationTests](InformationModel/BaseInformationTests.cs) | ✅ |
| 001 | ReadOperationLimitsMaxMonitoredItemsPerCallExistsAsync | [BaseInformationTests](InformationModel/BaseInformationTests.cs) | ✅ |
| 001 | ReadOperationLimitsMaxNodesPerBrowseExistsAsync | [BaseInformationTests](InformationModel/BaseInformationTests.cs) | ✅ |
| 001 | ReadOperationLimitsMaxNodesPerMethodCallExistsAsync | [BaseInformationTests](InformationModel/BaseInformationTests.cs) | ✅ |
| 001 | ReadOperationLimitsMaxNodesPerReadPositiveAsync | [BaseInformationTests](InformationModel/BaseInformationTests.cs) | ✅ |
| 001 | ReadOperationLimitsMaxNodesPerRegisterNodesExistsAsync | [BaseInformationTests](InformationModel/BaseInformationTests.cs) | ✅ |
| 001 | ReadOperationLimitsMaxNodesPerTranslateBrowsePathsExistsAsync | [BaseInformationTests](InformationModel/BaseInformationTests.cs) | ✅ |
| 001 | ReadOperationLimitsMaxNodesPerWriteExistsAsync | [BaseInformationTests](InformationModel/BaseInformationTests.cs) | ✅ |
| 001 | ReadServerArrayContainsServerUriAsync | [BaseInformationTests](InformationModel/BaseInformationTests.cs) | ✅ |
| 001 | ReadServerAuditingPropertyExistsAsync | [BaseInformationTests](InformationModel/BaseInformationTests.cs) | ✅ |
| 001 | ReadServerServiceLevelAsync | [BaseInformationTests](InformationModel/BaseInformationTests.cs) | ✅ |
| 001 | ReadServerStatusSecondsTillShutdownZeroAsync | [BaseInformationTests](InformationModel/BaseInformationTests.cs) | ✅ |
| 001 | ReadServerStatusStartTimeBeforeCurrentTimeAsync | [BaseInformationTests](InformationModel/BaseInformationTests.cs) | ✅ |
| 001 | ReadServerTypeDefinitionAsync | [BaseInformationTests](InformationModel/BaseInformationTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info UaBinary File ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | DataTypeEncodingTypeExistsAsync | [BaseInfoDataTypeTests](InformationModel/BaseInfoDataTypeTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info UriString ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | UriStringIsSubtypeOfStringAsync | [BaseInfoDataTypeTests](InformationModel/BaseInfoDataTypeTests.cs) | ✅ |

</details>

<details>
<summary>Information Model / Base Info Utilizes ⏭️</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | UtilizesIsSubtypeOfNonHierarchicalReferencesAsync | [BaseInfoReferenceTypeTests](InformationModel/BaseInfoReferenceTypeTests.cs) | ⏭️ |

</details>

<details>
<summary>Information Model / Base Info ValueAsText ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | BrowseMultiStateDiscreteTypeForValueAsTextAsync | [BaseInfoServerTests](InformationModel/BaseInfoServerTests.cs) | ✅ |
| 002 | VerifyMultiStateValueDiscreteTypeEnumValuesAsync | [BaseInfoServerTests](InformationModel/BaseInfoServerTests.cs) | ✅ |

</details>

### Method Services

<details>
<summary>Method Services / Method Call ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | MethodCall001CallVoidMethodAsync | [MethodCallTests](MethodServices/MethodCallTests.cs) | ✅ |
| 008 | MethodCall004CallMultiplyMethodAsync | [MethodCallTests](MethodServices/MethodCallTests.cs) | ✅ |
| 009 | MethodCall007CallInputOnlyMethodAsync | [MethodCallTests](MethodServices/MethodCallTests.cs) | ✅ |
| 003 | MethodCall006CallOutputOnlyMethodAsync | [MethodCallTests](MethodServices/MethodCallTests.cs) | ✅ |
| 004 | MethodCall002CallAddMethodAsync | [MethodCallTests](MethodServices/MethodCallTests.cs) | ✅ |
| 007 | MethodCall003CallHelloMethodAsync | [MethodCallTests](MethodServices/MethodCallTests.cs) | ✅ |
| 005 | MethodCall005CallMultipleMethodsInOneRequestAsync | [MethodCallTests](MethodServices/MethodCallTests.cs) | ✅ |
| 016 | MethodCall008VerifyMethodNodeClassIsMethodAsync | [MethodCallTests](MethodServices/MethodCallTests.cs) | ✅ |
| Err-006 | MethodCallErr002CallWithWrongObjectIdAsync | [MethodCallTests](MethodServices/MethodCallTests.cs) | ✅ |
| Err-005 | MethodCallErr001CallNonExistentMethodAsync | [MethodCallTests](MethodServices/MethodCallTests.cs) | ✅ |
| Err-003 | MethodCallErr003CallWithMissingArgumentsAsync | [MethodCallTests](MethodServices/MethodCallTests.cs) | ✅ |
| Err-004 | MethodCallErr004CallWithTooManyArgumentsAsync | [MethodCallTests](MethodServices/MethodCallTests.cs) | ✅ |

</details>

### Monitored Item Services

<details>
<summary>Monitored Item Services / Monitor Basic ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | CreateMonitoredItemOnScalarVariableAsync | [MonitoredItemTests](MonitoredItemServices/MonitoredItemTests.cs) | ✅ |
| 001 | CreateMonitoredItemVerifyRevisedSamplingIntervalAsync | [MonitoredItemTests](MonitoredItemServices/MonitoredItemTests.cs) | ✅ |
| 001 | DataChangeFilterDefaultTriggerIsStatusValueAsync | [MonitorValueChangeTests](MonitoredItemServices/MonitorValueChangeTests.cs) | ✅ |
| 001 | DataChangeFilterStatusOnlyNoNotifyOnValueChangeAsync | [MonitorValueChangeTests](MonitoredItemServices/MonitorValueChangeTests.cs) | ✅ |
| 001 | DataChangeFilterStatusValueNotifyOnValueChangeAsync | [MonitorValueChangeTests](MonitoredItemServices/MonitorValueChangeTests.cs) | ✅ |
| 001 | DataChangeOnScalarTypeAsync | [MonitorValueChangeTests](MonitoredItemServices/MonitorValueChangeTests.cs) | ✅ |
| 001 | DeleteMonitoredItemsAsync | [MonitoredItemTests](MonitoredItemServices/MonitoredItemTests.cs) | ✅ |
| 001 | MonitorWithSamplingIntervalMinusOneUsesSubscriptionIntervalAsync | [MonitoredItemDepthTests](MonitoredItemServices/MonitoredItemDepthTests.cs) | ✅ |
| 001 | MonitoredItemRevisedSamplingIntervalReturnedAsync | [MonitoredItemTests](MonitoredItemServices/MonitoredItemTests.cs) | ✅ |
| 001 | WriteDifferentValueAlwaysNotifiesAsync | [MonitorValueChangeTests](MonitoredItemServices/MonitorValueChangeTests.cs) | ✅ |
| 002 | CreateMonitoredItemInitialValueReturnedAsync | [MonitoredItemTests](MonitoredItemServices/MonitoredItemTests.cs) | ✅ |
| 002 | CreateMonitoredItemsDisabledModeServerTimestampAsync | [MonitorBasicTests](MonitoredItemServices/MonitorBasicTests.cs) | ✅ |
| 002 | MonitorServerStatusNodeGetsPeriodicUpdatesAsync | [MonitoredItemDepthTests](MonitoredItemServices/MonitoredItemDepthTests.cs) | ✅ |
| 002 | PublishReceivesDataChangeNotificationAsync | [MonitoredItemTests](MonitoredItemServices/MonitoredItemTests.cs) | ✅ |
| 002 | PublishReturnsCorrectMonitoredItemClientHandleAsync | [MonitoredItemTests](MonitoredItemServices/MonitoredItemTests.cs) | ✅ |
| 003 | ModifyMonitoredItemChangeClientHandleAsync | [MonitorBasicTests](MonitoredItemServices/MonitorBasicTests.cs) | ✅ |
| 003 | WriteToOneOfMultipleMonitoredItemsOnlyThatOneNotifiesAsync | [MonitoredItemTests](MonitoredItemServices/MonitoredItemTests.cs) | ✅ |
| 004 | CreateMonitoredItemVerifyRevisedQueueSizeAsync | [MonitoredItemTests](MonitoredItemServices/MonitoredItemTests.cs) | ✅ |
| 004 | CreateMonitoredItemWithDiscardOldestFalseAsync | [MonitoredItemTests](MonitoredItemServices/MonitoredItemTests.cs) | ✅ |
| 004 | CreateMonitoredItemWithDiscardOldestTrueAsync | [MonitoredItemTests](MonitoredItemServices/MonitoredItemTests.cs) | ✅ |
| 004 | DiscardOldestDefaultIsTrueAsync | [MonitorQueueingTests](MonitoredItemServices/MonitorQueueingTests.cs) | ✅ |
| 004 | DiscardOldestFalseBehaviorAsync | [MonitoredItemTests](MonitoredItemServices/MonitoredItemTests.cs) | ✅ |
| 004 | DiscardOldestFalseDropsNewestEnqueuedAsync | [MonitorQueueingTests](MonitoredItemServices/MonitorQueueingTests.cs) | ✅ |
| 004 | DiscardOldestTrueBehaviorAsync | [MonitoredItemTests](MonitoredItemServices/MonitoredItemTests.cs) | ✅ |
| 004 | DiscardOldestTrueDropsFirstEnqueuedAsync | [MonitorQueueingTests](MonitoredItemServices/MonitorQueueingTests.cs) | ✅ |
| 004 | ModifyMonitoredItemTimestampsToSourceAsync | [MonitorBasicTests](MonitoredItemServices/MonitorBasicTests.cs) | ✅ |
| 004 | QueueOverflowCountMatchesDroppedItemsAsync | [MonitorQueueingTests](MonitoredItemServices/MonitorQueueingTests.cs) | ✅ |
| 004 | QueueOverflowSetsOverflowBitInStatusCodeAsync | [MonitorQueueingTests](MonitoredItemServices/MonitorQueueingTests.cs) | ✅ |
| 004 | QueueOverflowWithSingleItemQueueAsync | [MonitorQueueingTests](MonitoredItemServices/MonitorQueueingTests.cs) | ✅ |
| 004 | QueueSizeFiveAccumulatesUpToFiveValuesAsync | [MonitorQueueingTests](MonitoredItemServices/MonitorQueueingTests.cs) | ✅ |
| 004 | QueueSizeFiveRapidWritesAsync | [MonitoredItemTests](MonitoredItemServices/MonitoredItemTests.cs) | ✅ |
| 004 | QueueSizeFiveWriteThreeGetAllInSinglePublishAsync | [MonitoredItemDepthTests](MonitoredItemServices/MonitoredItemDepthTests.cs) | ✅ |
| 004 | QueueSizeOneOnlyLatestValueDeliveredAsync | [MonitorQueueingTests](MonitoredItemServices/MonitorQueueingTests.cs) | ✅ |
| 004 | QueueSizeOneWriteFiveGetOnlyLatestAsync | [MonitoredItemDepthTests](MonitoredItemServices/MonitoredItemDepthTests.cs) | ✅ |
| 004 | QueueSizeTenWithFewerChangesProvidesAllChangesAsync | [MonitorQueueingTests](MonitoredItemServices/MonitorQueueingTests.cs) | ✅ |
| 005 | BatchModifyFiftyMonitoredItemsAsync | [MonitoredItemTests](MonitoredItemServices/MonitoredItemTests.cs) | ✅ |
| 005 | CreateMonitoredItemWithQueueSizeZeroServerRevisesToOneAsync | [MonitoredItemTests](MonitoredItemServices/MonitoredItemTests.cs) | ✅ |
| 005 | ModifyItemDiscardOldestChangedAsync | [MonitorQueueingTests](MonitoredItemServices/MonitorQueueingTests.cs) | ✅ |
| 005 | ModifyMonitoredItemAddDataChangeFilterAsync | [MonitoredItemDepthTests](MonitoredItemServices/MonitoredItemDepthTests.cs) | ✅ |
| 005 | ModifyMonitoredItemChangeFilterAsync | [MonitoredItemTests](MonitoredItemServices/MonitoredItemTests.cs) | ✅ |
| 005 | ModifyMonitoredItemChangeQueueSizeAsync | [MonitoredItemTests](MonitoredItemServices/MonitoredItemTests.cs) | ✅ |
| 005 | ModifyMonitoredItemChangeSamplingIntervalAsync | [MonitoredItemTests](MonitoredItemServices/MonitoredItemTests.cs) | ✅ |
| 005 | ModifyMonitoredItemTimestampsToServerAsync | [MonitorBasicTests](MonitoredItemServices/MonitorBasicTests.cs) | ✅ |
| 005 | QueueSizeOneOnlyLatestValueAsync | [MonitoredItemTests](MonitoredItemServices/MonitoredItemTests.cs) | ✅ |
| 005 | QueueSizePreservedAfterModifyAsync | [MonitorQueueingTests](MonitoredItemServices/MonitorQueueingTests.cs) | ✅ |
| 005 | QueueSizeZeroRevisedToOneAsync | [MonitoredItemTests](MonitoredItemServices/MonitoredItemTests.cs) | ✅ |
| 005 | QueueSizeZeroRevisedToOneAsync | [MonitorQueueingTests](MonitoredItemServices/MonitorQueueingTests.cs) | ✅ |
| 005 | VerifyModifyMonitoredItemRevisedQueueSizeAsync | [MonitoredItemDepthTests](MonitoredItemServices/MonitoredItemDepthTests.cs) | ✅ |
| 005 | VerifyModifyMonitoredItemRevisedSamplingIntervalAsync | [MonitoredItemDepthTests](MonitoredItemServices/MonitoredItemDepthTests.cs) | ✅ |
| 005 | VeryLargeQueueSizeRevisedDownwardAsync | [MonitorQueueingTests](MonitoredItemServices/MonitorQueueingTests.cs) | ✅ |
| 005 | VeryLargeQueueSizeServerRevisesAsync | [MonitoredItemDepthTests](MonitoredItemServices/MonitoredItemDepthTests.cs) | ✅ |
| 006 | CreateEventMonitoredItemWithEventFilterAsync | [MonitoredItemTests](MonitoredItemServices/MonitoredItemTests.cs) | ✅ |
| 006 | CreateMonitoredItemForBrowseNameAttributeAsync | [MonitoredItemTests](MonitoredItemServices/MonitoredItemTests.cs) | ✅ |
| 006 | CreateMonitoredItemForDisplayNameAttributeAsync | [MonitoredItemTests](MonitoredItemServices/MonitoredItemTests.cs) | ✅ |
| 006 | EventFilterWithWhereClauseAsync | [MonitoredItemTests](MonitoredItemServices/MonitoredItemTests.cs) | ✅ |
| 006 | ModifyMonitoredItemTimestampsToNeitherAsync | [MonitorBasicTests](MonitoredItemServices/MonitorBasicTests.cs) | ✅ |
| 006 | MonitorAccessLevelAttributeAsync | [MonitoredItemTests](MonitoredItemServices/MonitoredItemTests.cs) | ✅ |
| 006 | MonitorArrayVariableNotificationContainsFullArrayAsync | [MonitoredItemDepthTests](MonitoredItemServices/MonitoredItemDepthTests.cs) | ✅ |
| 006 | MonitorDataTypeAttributeAcceptedAsync | [MonitoredItemDepthTests](MonitoredItemServices/MonitoredItemDepthTests.cs) | ✅ |
| 006 | MonitorEventNotifierAttributeAsync | [MonitoredItemTests](MonitoredItemServices/MonitoredItemTests.cs) | ✅ |
| 006 | MonitorNodeClassAttributeAcceptedAsync | [MonitoredItemDepthTests](MonitoredItemServices/MonitoredItemDepthTests.cs) | ✅ |
| 007 | MonitorSimulationNodeReceivesChangingValuesAsync | [MonitoredItemDepthTests](MonitoredItemServices/MonitoredItemDepthTests.cs) | ✅ |
| 007 | OverflowBitSetOnQueueOverflowAsync | [MonitorValueChangeTests](MonitoredItemServices/MonitorValueChangeTests.cs) | ✅ |
| 007 | WriteValueAndPublishVerifyNotificationContainsNewValueAsync | [MonitoredItemTests](MonitoredItemServices/MonitoredItemTests.cs) | ✅ |
| 008 | CreateMonitoredItemWithSamplingIntervalZeroServerRevisesAsync | [MonitoredItemTests](MonitoredItemServices/MonitoredItemTests.cs) | ✅ |
| 008 | VeryFastSamplingIntervalRevisedAsync | [MonitoredItemTests](MonitoredItemServices/MonitoredItemTests.cs) | ✅ |
| 010 | ModifyMultipleItemsSamplingIntervalsAsync | [MonitorBasicTests](MonitoredItemServices/MonitorBasicTests.cs) | ✅ |
| 011 | ModifyMonitoredItemQueueSizeZeroAsync | [MonitorBasicTests](MonitoredItemServices/MonitorBasicTests.cs) | ✅ |
| 012 | ModifyMonitoredItemQueueSizeMaxUInt32Async | [MonitorBasicTests](MonitoredItemServices/MonitorBasicTests.cs) | ✅ |
| 013 | CreateMonitoredItemInDisabledModeAsync | [MonitoredItemTests](MonitoredItemServices/MonitoredItemTests.cs) | ✅ |
| 014 | CreateAndDeleteItemRepeatedlyAsync | [MonitorValueChangeTests](MonitoredItemServices/MonitorValueChangeTests.cs) | ✅ |
| 014 | CreateMonitoredItemInSamplingModeAsync | [MonitoredItemTests](MonitoredItemServices/MonitoredItemTests.cs) | ✅ |
| 015 | ModifyMonitoredItemDisabledBackToReportingResumesNotificationsAsync | [MonitoredItemDepthTests](MonitoredItemServices/MonitoredItemDepthTests.cs) | ✅ |
| 015 | SetMonitoringModeDisabledThenReportingAsync | [MonitoredItemTests](MonitoredItemServices/MonitoredItemTests.cs) | ✅ |
| 015 | SetMonitoringModeOnDeletedItemReturnsBadIdAsync | [MonitorBasicTests](MonitoredItemServices/MonitorBasicTests.cs) | ✅ |
| 015 | SetMonitoringModeReportingAsync | [MonitoredItemTests](MonitoredItemServices/MonitoredItemTests.cs) | ✅ |
| 016 | ModifyMonitoredItemReportingToDisabledNoMoreNotificationsAsync | [MonitoredItemDepthTests](MonitoredItemServices/MonitoredItemDepthTests.cs) | ✅ |
| 016 | SetMonitoringModeDisabledAsync | [MonitoredItemTests](MonitoredItemServices/MonitoredItemTests.cs) | ✅ |
| 016 | SetMonitoringModeOnMixDeletedAndValidItemsAsync | [MonitorBasicTests](MonitoredItemServices/MonitorBasicTests.cs) | ✅ |
| 017 | AddFiveLinkedItemsToOneTriggerAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ✅ |
| 017 | BatchCreateAndImmediatelyDeleteAllItemsAsync | [MonitoredItemDepthTests](MonitoredItemServices/MonitoredItemDepthTests.cs) | ✅ |
| 017 | BatchCreateMonitoredItemsOnFiftyDifferentNodesAsync | [MonitoredItemDepthTests](MonitoredItemServices/MonitoredItemDepthTests.cs) | ✅ |
| 017 | BatchCreateOneHundredMonitoredItemsAsync | [MonitoredItemTests](MonitoredItemServices/MonitoredItemTests.cs) | ✅ |
| 017 | BatchDeleteFiftyMonitoredItemsAsync | [MonitoredItemTests](MonitoredItemServices/MonitoredItemTests.cs) | ✅ |
| 017 | ChainTriggerATriggersB_BTriggersCAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ⏭️ |
| 017 | ChainTriggerOnlyDirectLinksHonoredAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ⏭️ |
| 017 | ChainTriggerRemoveMiddleLinkBreaksChainAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ⏭️ |
| 017 | ChainTriggerThreeLevelsDeepAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ⏭️ |
| 017 | ChangeLinkedModeFromSamplingToDisabledStopsTriggeringAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ✅ |
| 017 | CreateMonitoredItemsOnMultipleNodesAsync | [MonitoredItemTests](MonitoredItemServices/MonitoredItemTests.cs) | ✅ |
| 017 | DeleteLinkedItemTriggerStillWorksAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ⏭️ |
| 017 | DeleteMonitoredItemsWhileSubscriptionActiveRemainingItemsWorkAsync | [MonitoredItemDepthTests](MonitoredItemServices/MonitoredItemDepthTests.cs) | ✅ |
| 017 | DeleteMultipleMonitoredItemsAsync | [MonitoredItemTests](MonitoredItemServices/MonitoredItemTests.cs) | ✅ |
| 017 | DeleteTriggerItemLinksAutomaticallyRemovedAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ✅ |
| 017 | MonitorMultipleItemsSameSubscriptionAllGetInitialValuesAsync | [MonitoredItemDepthTests](MonitoredItemServices/MonitoredItemDepthTests.cs) | ✅ |
| 017 | MultipleTriggersSameLinkedItemAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ⏭️ |
| 017 | OneTriggerMultipleLinkedItemsAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ⏭️ |
| 017 | QueueSizeDifferentPerItemAsync | [MonitorQueueingTests](MonitoredItemServices/MonitorQueueingTests.cs) | ✅ |
| 017 | RemoveAllLinksAtOnceAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ✅ |
| 017 | RemoveLinkThenReAddAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ✅ |
| 017 | RemoveLinksFromInvalidTriggerReturnsBadAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ✅ |
| 017 | RemoveLinksOneByOneAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ✅ |
| 017 | RemoveNonExistentLinkReturnsBadAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ✅ |
| 017 | RemoveOneOfMultipleLinkedItemsRestRemainAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ⏭️ |
| 017 | SetLinkedItemToReportingStillTriggerableAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ✅ |
| 017 | SetMonitoringModeOnMultipleItemsAtOnceAsync | [MonitoredItemDepthTests](MonitoredItemServices/MonitoredItemDepthTests.cs) | ✅ |
| 017 | SetMonitoringModeSamplingAsync | [MonitoredItemTests](MonitoredItemServices/MonitoredItemTests.cs) | ✅ |
| 017 | SetTriggeringAddMultipleLinksAtOnceAsync | [MonitoredItemDepthTests](MonitoredItemServices/MonitoredItemDepthTests.cs) | ✅ |
| 017 | SetTriggeringChainATriggersBTriggersCAsync | [MonitoredItemDepthTests](MonitoredItemServices/MonitoredItemDepthTests.cs) | ✅ |
| 017 | SetTriggeringLinkTriggeringToTriggeredItemAsync | [MonitoredItemTests](MonitoredItemServices/MonitoredItemTests.cs) | ✅ |
| 017 | SetTriggeringRemoveLinkAsync | [MonitoredItemTests](MonitoredItemServices/MonitoredItemTests.cs) | ✅ |
| 017 | SetTriggeringSameItemAsTriggerAndLinkedAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ✅ |
| 017 | SimpleTriggerAddLinkAfterCreationAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ✅ |
| 017 | SimpleTriggerBothItemsInSameNotificationAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ✅ |
| 017 | SimpleTriggerLinkedItemOnlyReportsWhenTriggerFiresAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ⏭️ |
| 017 | SimpleTriggerRemoveLinkStopsTriggeringAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ✅ |
| 017 | SimpleTriggerReportingTriggersScanningAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ✅ |
| 017 | SimpleTriggerWriteToLinkedItemNoNotificationAloneAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ⏭️ |
| 017 | TriggerItemDisabledStopsTriggeringAllAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ⏭️ |
| 017 | TriggerItemSamplingNoAutoReportingAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ✅ |
| 017 | TriggerPreservedAfterModifyMonitoredItemAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ✅ |
| 017 | TriggerWithDataChangeFilterOnLinkedItemAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ✅ |
| 017 | TriggerWithDifferentSamplingIntervalsAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ✅ |
| 017 | TriggerWithDisabledLinkedItemAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ✅ |
| 017 | TriggerWithTenLinkedItemsAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ✅ |
| 017 | TriggeredItemOnlyReportsWhenTriggeringItemChangesAsync | [MonitoredItemDepthTests](MonitoredItemServices/MonitoredItemDepthTests.cs) | ✅ |
| 018 | CreateItemSamplingIntervalZeroReportingAsync | [MonitorBasicTests](MonitoredItemServices/MonitorBasicTests.cs) | ✅ |
| 019 | CreateMonitoredItemWithDataChangeFilterStatusValueAsync | [MonitoredItemTests](MonitoredItemServices/MonitoredItemTests.cs) | ✅ |
| 019 | CreateMonitoredItemWithDataChangeFilterStatusValueTimestampAsync | [MonitoredItemTests](MonitoredItemServices/MonitoredItemTests.cs) | ✅ |
| 019 | DataChangeFilterTriggerStatusOnlyNotifyOnStatusChangeAsync | [MonitoredItemDepthTests](MonitoredItemServices/MonitoredItemDepthTests.cs) | ✅ |
| 019 | DataChangeFilterTriggerStatusValueAsync | [MonitoredItemDepthTests](MonitoredItemServices/MonitoredItemDepthTests.cs) | ✅ |
| 019 | DataChangeFilterTriggerStatusValueTimestampAsync | [MonitoredItemDepthTests](MonitoredItemServices/MonitoredItemDepthTests.cs) | ✅ |
| 019 | ItemsWithDifferentClientHandlesAsync | [MonitorValueChangeTests](MonitoredItemServices/MonitorValueChangeTests.cs) | ✅ |
| 019 | MonitorWithIndexRangeOnArrayAsync | [MonitoredItemDepthTests](MonitoredItemServices/MonitoredItemDepthTests.cs) | ✅ |
| 020 | MonitorAllArrayTypesInitialNotificationAsync | [MonitoredItemDepthTests](MonitoredItemServices/MonitoredItemDepthTests.cs) | ✅ |
| 020 | SetMonitoringModeDisabledToDisabledAsync | [MonitorBasicTests](MonitoredItemServices/MonitorBasicTests.cs) | ✅ |
| 021 | MonitorAllNineteenScalarTypesInitialNotificationAsync | [MonitoredItemDepthTests](MonitoredItemServices/MonitoredItemDepthTests.cs) | ✅ |
| 021 | SetMonitoringModeDisabledToSamplingAsync | [MonitorBasicTests](MonitoredItemServices/MonitorBasicTests.cs) | ✅ |
| 022 | SetMonitoringModeDisabledToReportingAsync | [MonitorBasicTests](MonitoredItemServices/MonitorBasicTests.cs) | ✅ |
| 023 | SetMonitoringModeSamplingToDisabledAsync | [MonitorBasicTests](MonitoredItemServices/MonitorBasicTests.cs) | ✅ |
| 024 | SetMonitoringModeSamplingToSamplingAsync | [MonitorBasicTests](MonitoredItemServices/MonitorBasicTests.cs) | ✅ |
| 025 | SetMonitoringModeSamplingToReportingAsync | [MonitorBasicTests](MonitoredItemServices/MonitorBasicTests.cs) | ✅ |
| 026 | SetMonitoringModeReportingToDisabledAsync | [MonitorBasicTests](MonitoredItemServices/MonitorBasicTests.cs) | ✅ |
| 027 | SetMonitoringModeReportingToSamplingAsync | [MonitorBasicTests](MonitoredItemServices/MonitorBasicTests.cs) | ✅ |
| 028 | SetMonitoringModeReportingToReportingAsync | [MonitorBasicTests](MonitoredItemServices/MonitorBasicTests.cs) | ✅ |
| 034 | CreateMonitoredItemsForAllAttributesAsync | [MonitorBasicTests](MonitoredItemServices/MonitorBasicTests.cs) | ✅ |
| 036 | CreateMonitoredItemDataEncodingVariationsAsync | [MonitorBasicTests](MonitoredItemServices/MonitorBasicTests.cs) | ✅ |
| 037 | CreateMonitoredItemsDisabledModeServerTimestampDuplicateAsync | [MonitorBasicTests](MonitoredItemServices/MonitorBasicTests.cs) | ✅ |
| 038 | CreateItemSamplingIntervalZeroVerifyRevisedAsync | [MonitorBasicTests](MonitoredItemServices/MonitorBasicTests.cs) | ✅ |
| 039 | CreateMonitoredItemsMultiDimensionalArrayAsync | [MonitorBasicTests](MonitoredItemServices/MonitorBasicTests.cs) | ✅ |
| Err-001 | CreateMonitoredItemWithInvalidNodeIdReturnsBadNodeIdUnknownAsync | [MonitoredItemTests](MonitoredItemServices/MonitoredItemTests.cs) | ✅ |
| Err-002 | CreateMonitoredItemWithWrongAttributeIdReturnsBadAttributeIdInvalidAsync | [MonitoredItemTests](MonitoredItemServices/MonitoredItemTests.cs) | ✅ |
| Err-006 | AbsoluteDeadbandWriteOutsideDeadbandNotificationAsync | [MonitoredItemDepthTests](MonitoredItemServices/MonitoredItemDepthTests.cs) | ✅ |
| Err-006 | AbsoluteDeadbandWriteWithinDeadbandNoNotificationAsync | [MonitoredItemDepthTests](MonitoredItemServices/MonitoredItemDepthTests.cs) | ✅ |
| Err-006 | CreateMonitoredItemWithAbsoluteDeadbandFilterAsync | [MonitoredItemTests](MonitoredItemServices/MonitoredItemTests.cs) | ✅ |
| Err-007 | CreateMonitoredItemWithPercentDeadbandFilterAsync | [MonitoredItemTests](MonitoredItemServices/MonitoredItemTests.cs) | ✅ |
| Err-007 | PercentDeadbandCreationAcceptedAsync | [MonitoredItemDepthTests](MonitoredItemServices/MonitoredItemDepthTests.cs) | ✅ |
| Err-011 | SetTriggeringWithInvalidTriggeringItemReturnsBadAsync | [MonitoredItemDepthTests](MonitoredItemServices/MonitoredItemDepthTests.cs) | ✅ |
| Err-013 | DeleteMonitoredItemWithInvalidIdReturnsBadMonitoredItemIdInvalidAsync | [MonitoredItemTests](MonitoredItemServices/MonitoredItemTests.cs) | ✅ |
| Err-015 | ModifyMonitoredItemWithInvalidIdReturnsBadMonitoredItemIdInvalidAsync | [MonitoredItemTests](MonitoredItemServices/MonitoredItemTests.cs) | ✅ |
| Err-026 | SetTriggeringInvalidLinkedItemIdAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ✅ |
| Err-026 | SetTriggeringInvalidTriggeringItemIdAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ✅ |
| Err-029 | SetTriggeringInvalidSubscriptionIdAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ✅ |
| Err-029 | SetTriggeringOnDeletedSubscriptionAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ✅ |
| Err-030 | SetTriggeringEmptyAddAndRemoveArraysAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ✅ |

</details>

<details>
<summary>Monitored Item Services / Monitor Complex Value ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | MonitorComplexDataTypeValueAsync | [MonitorComplexValueTests](MonitoredItemServices/MonitorComplexValueTests.cs) | ✅ |
| 002 | MonitorNestedComplexDataTypeValueAsync | [MonitorComplexValueTests](MonitoredItemServices/MonitorComplexValueTests.cs) | ✅ |
| 003 | MonitorComplexDataTypeDataEncodingVariationsAsync | [MonitorComplexValueTests](MonitoredItemServices/MonitorComplexValueTests.cs) | ✅ |

</details>

<details>
<summary>Monitored Item Services / Monitor Events ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | MonitorServerEventsWithSeverityFilterAsync | [MonitorEventsTests](MonitoredItemServices/MonitorEventsTests.cs) | ✅ |
| 002 | MonitorEventsWithSelectClauseDisplayNameAsync | [MonitorEventsTests](MonitoredItemServices/MonitorEventsTests.cs) | ✅ |
| 003 | MonitorEventsWithWhereClauseSeverityAsync | [MonitorEventsTests](MonitoredItemServices/MonitorEventsTests.cs) | ✅ |

</details>

<details>
<summary>Monitored Item Services / Monitor Items 2 ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | BatchDeleteThenVerifyNoMoreNotificationsAsync | [MonitorItemsBatchTests](MonitoredItemServices/MonitorItemsBatchTests.cs) | ✅ |
| 001 | TwoItemsOnSameNodeBothNotifyAsync | [MonitorValueChangeTests](MonitoredItemServices/MonitorValueChangeTests.cs) | ✅ |
| 003 | BatchCreateTenItemsOnDifferentNodesAsync | [MonitorItemsBatchTests](MonitoredItemServices/MonitorItemsBatchTests.cs) | ✅ |
| 003 | BatchCreateTenItemsOnSameNodeAsync | [MonitorItemsBatchTests](MonitoredItemServices/MonitorItemsBatchTests.cs) | ✅ |
| 003 | BatchCreateWithVaryingSamplingIntervalsAsync | [MonitorItemsBatchTests](MonitoredItemServices/MonitorItemsBatchTests.cs) | ✅ |
| 003 | BatchDeleteTenItemsAsync | [MonitorItemsBatchTests](MonitoredItemServices/MonitorItemsBatchTests.cs) | ✅ |
| 003 | BatchModifyTenItemsQueueSizeAsync | [MonitorItemsBatchTests](MonitoredItemServices/MonitorItemsBatchTests.cs) | ✅ |
| 003 | BatchModifyTenItemsSamplingIntervalAsync | [MonitorItemsBatchTests](MonitoredItemServices/MonitorItemsBatchTests.cs) | ✅ |
| 003 | CreateMonitoredItemDataEncodingVariationsAsync | [MonitorItems2Tests](MonitoredItemServices/MonitorItems2Tests.cs) | ✅ |
| 004 | BatchCreateMixOfValidAndInvalidNodesAsync | [MonitorItemsBatchTests](MonitoredItemServices/MonitorItemsBatchTests.cs) | ✅ |
| 004 | BatchDeleteMixOfValidAndInvalidIdsAsync | [MonitorItemsBatchTests](MonitoredItemServices/MonitorItemsBatchTests.cs) | ✅ |
| 004 | BatchModifyMixOfValidAndInvalidIdsAsync | [MonitorItemsBatchTests](MonitoredItemServices/MonitorItemsBatchTests.cs) | ✅ |
| 004 | ModifyMultipleItemsVaryingParametersAsync | [MonitorItems2Tests](MonitoredItemServices/MonitorItems2Tests.cs) | ✅ |

</details>

<details>
<summary>Monitored Item Services / Monitor Items Deadband Filter ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | AbsoluteDeadbandExactlyAtBoundaryAsync | [MonitorDeadbandFilterTests](MonitoredItemServices/MonitorDeadbandFilterTests.cs) | ✅ |
| 001 | DisabledModeAbsoluteDeadbandZeroAsync | [MonitorDeadbandFilterTests](MonitoredItemServices/MonitorDeadbandFilterTests.cs) | ✅ |
| 002 | AbsoluteDeadbandOnFloatAnalogNodeAsync | [MonitorDeadbandFilterTests](MonitoredItemServices/MonitorDeadbandFilterTests.cs) | ✅ |
| 002 | SamplingModeAbsoluteDeadbandZeroAsync | [MonitorDeadbandFilterTests](MonitoredItemServices/MonitorDeadbandFilterTests.cs) | ✅ |
| 003 | AbsoluteDeadbandLargeThresholdAsync | [MonitorDeadbandFilterTests](MonitoredItemServices/MonitorDeadbandFilterTests.cs) | ✅ |
| 003 | AbsoluteDeadbandOnDoubleAnalogNodeAsync | [MonitorDeadbandFilterTests](MonitoredItemServices/MonitorDeadbandFilterTests.cs) | ⏭️ |
| 003 | SamplingModeAbsoluteDeadbandZeroQueueZeroAsync | [MonitorDeadbandFilterTests](MonitoredItemServices/MonitorDeadbandFilterTests.cs) | ✅ |
| 004 | AbsoluteDeadbandSmallThresholdAsync | [MonitorDeadbandFilterTests](MonitoredItemServices/MonitorDeadbandFilterTests.cs) | ✅ |
| 004 | ReportingModeAbsoluteDeadbandZeroQueueOneAsync | [MonitorDeadbandFilterTests](MonitoredItemServices/MonitorDeadbandFilterTests.cs) | ✅ |
| 005 | AbsoluteDeadbandZeroNotifiesOnAnyChangeAsync | [MonitorDeadbandFilterTests](MonitoredItemServices/MonitorDeadbandFilterTests.cs) | ✅ |
| 005 | ReportingModeAbsoluteDeadbandZeroQueueZeroAsync | [MonitorDeadbandFilterTests](MonitoredItemServices/MonitorDeadbandFilterTests.cs) | ✅ |
| 006 | AbsoluteDeadbandMaxDoubleValueAsync | [MonitorDeadbandFilterTests](MonitoredItemServices/MonitorDeadbandFilterTests.cs) | ✅ |
| 006 | DeadbandOnNonValueAttributesRejectedAsync | [MonitorDeadbandFilterTests](MonitoredItemServices/MonitorDeadbandFilterTests.cs) | ✅ |
| 007 | AbsoluteDeadbandOnInt32NodeAsync | [MonitorDeadbandFilterTests](MonitoredItemServices/MonitorDeadbandFilterTests.cs) | ✅ |
| 007 | AbsoluteDeadbandWritePublishThresholdTwoAsync | [MonitorDeadbandFilterTests](MonitoredItemServices/MonitorDeadbandFilterTests.cs) | ✅ |
| 008 | AbsoluteDeadbandOnInt16NodeAsync | [MonitorDeadbandFilterTests](MonitoredItemServices/MonitorDeadbandFilterTests.cs) | ✅ |
| 008 | AbsoluteDeadbandWritePublishThresholdOneAsync | [MonitorDeadbandFilterTests](MonitoredItemServices/MonitorDeadbandFilterTests.cs) | ✅ |
| 009 | AbsoluteDeadbandLargeThresholdNewSubscriptionAsync | [MonitorDeadbandFilterTests](MonitoredItemServices/MonitorDeadbandFilterTests.cs) | ⏭️ |
| 009 | AbsoluteDeadbandOnUInt32NodeAsync | [MonitorDeadbandFilterTests](MonitoredItemServices/MonitorDeadbandFilterTests.cs) | ✅ |
| 010 | AbsoluteDeadbandOnByteNodeAsync | [MonitorDeadbandFilterTests](MonitoredItemServices/MonitorDeadbandFilterTests.cs) | ✅ |
| 010 | ArrayDeadbandFirstElementAsync | [MonitorDeadbandFilterTests](MonitoredItemServices/MonitorDeadbandFilterTests.cs) | ✅ |
| 011 | ArrayDeadbandIndexRangeOneTwoAsync | [MonitorDeadbandFilterTests](MonitoredItemServices/MonitorDeadbandFilterTests.cs) | ✅ |
| 011 | PercentDeadbandTenPercentOnAnalogNodeAsync | [MonitorDeadbandFilterTests](MonitoredItemServices/MonitorDeadbandFilterTests.cs) | ✅ |
| 012 | ArrayDeadbandMiddleIndexRangeAsync | [MonitorDeadbandFilterTests](MonitoredItemServices/MonitorDeadbandFilterTests.cs) | ✅ |
| 012 | PercentDeadbandFiftyPercentAsync | [MonitorDeadbandFilterTests](MonitoredItemServices/MonitorDeadbandFilterTests.cs) | ✅ |
| 013 | ArrayDeadbandIndexRangeOneThreeAsync | [MonitorDeadbandFilterTests](MonitoredItemServices/MonitorDeadbandFilterTests.cs) | ✅ |
| 013 | PercentDeadbandHundredPercentOnlyExtremeChangesAsync | [MonitorDeadbandFilterTests](MonitoredItemServices/MonitorDeadbandFilterTests.cs) | ✅ |
| 014 | ArrayDeadbandFullRangeAsync | [MonitorDeadbandFilterTests](MonitoredItemServices/MonitorDeadbandFilterTests.cs) | ✅ |
| 014 | PercentDeadbandOnDoubleAnalogNodeAsync | [MonitorDeadbandFilterTests](MonitoredItemServices/MonitorDeadbandFilterTests.cs) | ✅ |
| 015 | DeadbandOnArrayDimensionsAttributeAsync | [MonitorDeadbandFilterTests](MonitoredItemServices/MonitorDeadbandFilterTests.cs) | ✅ |
| 015 | PercentDeadbandZeroNotifiesOnAnyChangeAsync | [MonitorDeadbandFilterTests](MonitoredItemServices/MonitorDeadbandFilterTests.cs) | ✅ |
| 016 | ArrayDeadbandFullRangeWriteSequenceAsync | [MonitorDeadbandFilterTests](MonitoredItemServices/MonitorDeadbandFilterTests.cs) | ✅ |
| 018 | ArrayDeadbandQueueSizeOneNoIndexRangeAsync | [MonitorDeadbandFilterTests](MonitoredItemServices/MonitorDeadbandFilterTests.cs) | ✅ |
| 021 | ModifyItemToAddDeadbandFilterAsync | [MonitorDeadbandFilterTests](MonitoredItemServices/MonitorDeadbandFilterTests.cs) | ✅ |
| 021 | ModifyItemToRemoveDeadbandFilterAsync | [MonitorDeadbandFilterTests](MonitoredItemServices/MonitorDeadbandFilterTests.cs) | ✅ |
| Err-002 | PercentDeadbandNegativeRejectedAsync | [MonitorDeadbandFilterTests](MonitoredItemServices/MonitorDeadbandFilterTests.cs) | ✅ |
| Err-004 | AbsoluteDeadbandNegativeValueRejectedAsync | [MonitorDeadbandFilterTests](MonitoredItemServices/MonitorDeadbandFilterTests.cs) | ✅ |
| Err-005 | DeadbandOnBooleanNodeReturnsBadFilterNotAllowedAsync | [MonitorDeadbandFilterTests](MonitoredItemServices/MonitorDeadbandFilterTests.cs) | ✅ |
| Err-005 | PercentDeadbandOnNonAnalogNodeReturnsBadFilterNotAllowedAsync | [MonitorDeadbandFilterTests](MonitoredItemServices/MonitorDeadbandFilterTests.cs) | ✅ |
| Err-006 | DeadbandOnStringNodeReturnsBadFilterNotAllowedAsync | [MonitorDeadbandFilterTests](MonitoredItemServices/MonitorDeadbandFilterTests.cs) | ✅ |

</details>

<details>
<summary>Monitored Item Services / Monitor Queueing ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | QueueSizeOneDiscardOldestDeliversLatestAsync | [MonitorQueueingTests](MonitoredItemServices/MonitorQueueingTests.cs) | ✅ |
| 002 | QueueSizeFiveAccumulatesFiveValuesAsync | [MonitorQueueingTests](MonitoredItemServices/MonitorQueueingTests.cs) | ✅ |
| 003 | QueueSizeTenFewerChangesAllDeliveredAsync | [MonitorQueueingTests](MonitoredItemServices/MonitorQueueingTests.cs) | ✅ |
| 004 | QueueSizeZeroRevisedToAtLeastOneAsync | [MonitorQueueingTests](MonitoredItemServices/MonitorQueueingTests.cs) | ✅ |
| 005 | DiscardOldestTrueKeepsNewestAsync | [MonitorQueueingTests](MonitoredItemServices/MonitorQueueingTests.cs) | ✅ |
| 006 | DiscardOldestFalseKeepsOldestAsync | [MonitorQueueingTests](MonitoredItemServices/MonitorQueueingTests.cs) | ✅ |
| 007 | DefaultDiscardOldestBehavesAsTrueAsync | [MonitorQueueingTests](MonitoredItemServices/MonitorQueueingTests.cs) | ✅ |
| 008 | ModifyDiscardOldestFromTrueToFalseAsync | [MonitorQueueingTests](MonitoredItemServices/MonitorQueueingTests.cs) | ✅ |
| 009 | QueueOverflowMaySetOverflowBitAsync | [MonitorQueueingTests](MonitoredItemServices/MonitorQueueingTests.cs) | ✅ |
| 010 | QueueOverflowSizeOneBoundedCountAsync | [MonitorQueueingTests](MonitoredItemServices/MonitorQueueingTests.cs) | ✅ |
| 011 | QueueOverflowCountBoundedByQueueSizeAsync | [MonitorQueueingTests](MonitoredItemServices/MonitorQueueingTests.cs) | ✅ |
| 005 | VeryLargeQueueSizeRevisedDownwardAsync | [MonitorQueueingTests](MonitoredItemServices/MonitorQueueingTests.cs) | ✅ |
| 005 | QueueSizePreservedAfterModifyAsync | [MonitorQueueingTests](MonitoredItemServices/MonitorQueueingTests.cs) | ✅ |
| 014 | TwoItemsDifferentQueueSizesAsync | [MonitorQueueingTests](MonitoredItemServices/MonitorQueueingTests.cs) | ✅ |

</details>

<details>
<summary>Monitored Item Services / Monitor Triggering ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | BasicAddSingleLinkAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ✅ |
| 002 | AddMultipleLinksAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ✅ |
| 003 | AddOneLinkThenRemoveAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ✅ |
| 004 | AddMultipleLinksThenRemoveAllAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ✅ |
| 005 | ReplaceLinksAddAndRemoveInOneCallAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ✅ |
| 006 | TriggerWithDeadbandFilterAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ✅ |
| 007 | CircularTriggerBothItemsAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ✅ |
| 008 | MixedAddRemoveSubsequentCallsAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ✅ |
| 009 | TriggerReportingLinksMixedModesAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ⏭️ |
| 010 | TriggerReportingLinkedReportingAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ✅ |
| 011 | TriggerReportingFourLinksMixedModesAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ✅ |
| 012 | SameItemInAddAndRemoveAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ✅ |
| 013 | TriggerSamplingLinkSamplingAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ✅ |
| 014 | TriggerSamplingLinksReportingAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ⏭️ |
| 015 | SameNodeIdTriggerAndLinkAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ✅ |
| 016 | DisabledTriggerSamplingLinkKeepAliveAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ✅ |
| 017 | DisabledTriggerFourLinksMixedModesAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ⏭️ |
| 018 | DisabledTriggerSameNodeLinkReportingAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ✅ |
| 019 | DisabledTriggerDisabledLinkNoNotificationsAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ✅ |
| 020 | DeadbandAbsoluteOnTriggerSamplingLinksAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ✅ |
| 021 | DeleteLinkedItemThenRemoveExpectsBadAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ✅ |
| 022 | DeleteTriggerItemCleanupAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ✅ |
| 023 | DeleteTriggerWritePublishNoDataAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ✅ |
| 024 | RemoveAlreadyDeletedLinkExpectsBadAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ✅ |
| 025 | NonNumericTriggerAndLinkAsync | [MonitorTriggeringTests](MonitoredItemServices/MonitorTriggeringTests.cs) | ✅ |

</details>

### Node Management

<details>
<summary>Node Management / Node Management Add Node ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | AddNodeThenBrowseVerifyVisibleAsync | [NodeManagementTests](NodeManagement/NodeManagementTests.cs) | ✅ |
| 001 | AddNodeThenDeleteNodeAsync | [NodeManagementTests](NodeManagement/NodeManagementTests.cs) | ✅ |
| 001 | AddNodesHandledGracefullyAsync | [NodeManagementTests](NodeManagement/NodeManagementTests.cs) | ✅ |
| 002 | AddReferenceThenBrowseVerifyVisibleAsync | [NodeManagementTests](NodeManagement/NodeManagementTests.cs) | ✅ |
| 003 | AddObjectNodeHandledGracefullyAsync | [NodeManagementTests](NodeManagement/NodeManagementTests.cs) | ✅ |
| 003 | AddReferencesHandledGracefullyAsync | [NodeManagementTests](NodeManagement/NodeManagementTests.cs) | ✅ |
| Err-008 | AddNodeWithDuplicateBrowseNameAsync | [NodeManagementTests](NodeManagement/NodeManagementTests.cs) | ✅ |

</details>

<details>
<summary>Node Management / Node Management Add Ref ⏭️</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|

</details>

<details>
<summary>Node Management / Node Management Delete Node ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| Err-001 | DeleteNodesHandledGracefullyAsync | [NodeManagementTests](NodeManagement/NodeManagementTests.cs) | ✅ |
| Err-001 | DeleteNonExistentNodeReturnsErrorAsync | [NodeManagementTests](NodeManagement/NodeManagementTests.cs) | ✅ |
| Err-002 | DeleteReferencesHandledGracefullyAsync | [NodeManagementTests](NodeManagement/NodeManagementTests.cs) | ✅ |

</details>

<details>
<summary>Node Management / Node Management Delete Ref ⏭️</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|

</details>

### Security

<details>
<summary>Security / Security - No Application Authentication ⏭️</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|

</details>

<details>
<summary>Security / Security Administration , 1 additional ⏭️</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|

**Additional coverage** (not mapped to specific source scripts):

| NUnit Test | Fixture | Status |
|-----------|---------|--------|
| RoleMethodsRequireSecurityAdmin | [SecurityRoleServerTests](Security/SecurityRoleServerTests.cs) | ⏭️ |

</details>

<details>
<summary>Security / Security Aes128 Sha256 ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|

</details>

<details>
<summary>Security / Security Aes256 Sha256 ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|

</details>

<details>
<summary>Security / Security Basic 128Rsa15 ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|

</details>

<details>
<summary>Security / Security Basic 256 ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|

</details>

<details>
<summary>Security / Security Basic256Sha256 ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|

</details>

<details>
<summary>Security / Security Certificate Validation , 3 additional ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | Aes128Sha256RsaOaepPolicyExistsOrFailAsync | [SecurityCertValidationDepthTests](Security/SecurityCertValidationDepthTests.cs) | ✅ |
| 001 | Aes256Sha256RsaPssPolicyExistsOrFailAsync | [SecurityCertValidationDepthTests](Security/SecurityCertValidationDepthTests.cs) | ✅ |
| 001 | AllEndpointUrlsAreNotEmptyAsync | [SecurityCertValidationDepthTests](Security/SecurityCertValidationDepthTests.cs) | ✅ |
| 001 | AllSecurePoliciesHaveEndpointsAsync | [SecurityCertValidationDepthTests](Security/SecurityCertValidationDepthTests.cs) | ✅ |
| 001 | Basic256Sha256PolicyExistsAsync | [SecurityCertValidationDepthTests](Security/SecurityCertValidationDepthTests.cs) | ✅ |
| 001 | Basic256Sha256UsesSha256SignaturesAsync | [SecurityCertValidationTests](Security/SecurityCertValidationTests.cs) | ✅ |
| 001 | CertHasNonEmptyCommonNameAsync | [SecurityCertValidationDepthTests](Security/SecurityCertValidationDepthTests.cs) | ✅ |
| 001 | CertHasRsaPublicKeyAsync | [SecurityCertValidationDepthTests](Security/SecurityCertValidationDepthTests.cs) | ✅ |
| 001 | CertKeyUsageFlagsArePresentAsync | [SecurityCertValidationDepthTests](Security/SecurityCertValidationDepthTests.cs) | ✅ |
| 001 | CertPublicKeyIsAccessibleAsync | [SecurityCertValidationDepthTests](Security/SecurityCertValidationDepthTests.cs) | ✅ |
| 001 | CertSerialNumberIsNonEmptyAsync | [SecurityCertValidationDepthTests](Security/SecurityCertValidationDepthTests.cs) | ✅ |
| 001 | CertSignatureAlgorithmIsSha256OrBetterAsync | [SecurityCertValidationDepthTests](Security/SecurityCertValidationDepthTests.cs) | ✅ |
| 001 | CertThumbprintIsNonEmptyAsync | [SecurityCertValidationDepthTests](Security/SecurityCertValidationDepthTests.cs) | ✅ |
| 001 | CertValidation001CreateSessionValidateCertAsync | [SecurityCertValidationTests](Security/SecurityCertValidationTests.cs) | ✅ |
| 001 | CertValiditySpanIsPositiveAsync | [SecurityCertValidationDepthTests](Security/SecurityCertValidationDepthTests.cs) | ✅ |
| 001 | ConnectToEachAdvertisedSecurityPolicyAsync | [SecurityCertValidationTests](Security/SecurityCertValidationTests.cs) | ✅ |
| 001 | ConnectWithAes128Sha256RsaOaepIfAdvertisedAsync | [SecurityCertValidationTests](Security/SecurityCertValidationTests.cs) | ✅ |
| 001 | ConnectWithAes256Sha256RsaPssIfAdvertisedAsync | [SecurityCertValidationTests](Security/SecurityCertValidationTests.cs) | ✅ |
| 001 | ConnectWithSecurityModeNoneSucceeds | [SecurityCertValidationTests](Security/SecurityCertValidationTests.cs) | ✅ |
| 001 | ConnectWithSecurityModeSignAndEncryptSucceedsAsync | [SecurityCertValidationTests](Security/SecurityCertValidationTests.cs) | ✅ |
| 001 | ConnectWithSecurityModeSignSucceedsAsync | [SecurityCertValidationTests](Security/SecurityCertValidationTests.cs) | ✅ |
| 001 | CrossModeSessionIdsAreDifferentAsync | [SecurityCertValidationDepthTests](Security/SecurityCertValidationDepthTests.cs) | ✅ |
| 001 | EachSecureEndpointHasNonEmptyCertificateAsync | [SecurityCertValidationTests](Security/SecurityCertValidationTests.cs) | ✅ |
| 001 | EachSecureEndpointHasRecognizedPolicyAsync | [SecurityCertificateTests](Security/SecurityCertificateTests.cs) | ✅ |
| 001 | EndpointCertByteRoundtripAsync | [SecurityCertValidationDepthTests](Security/SecurityCertValidationDepthTests.cs) | ✅ |
| 001 | EndpointCertThumbprintMatchesParsedCertAsync | [SecurityCertValidationDepthTests](Security/SecurityCertValidationDepthTests.cs) | ✅ |
| 001 | EndpointCertificatesCanBeParsedAsync | [SecurityCertValidationTests](Security/SecurityCertValidationTests.cs) | ✅ |
| 001 | EndpointsWithSamePolicyUseSameCertAsync | [SecurityCertValidationTests](Security/SecurityCertValidationTests.cs) | ✅ |
| 001 | InvalidSecurityPolicyFailsAsync | [SecurityCertValidationDepthTests](Security/SecurityCertValidationDepthTests.cs) | ⏭️ |
| 001 | NonceIsNotAllZerosOnSecureSessionAsync | [SecurityCertValidationDepthTests](Security/SecurityCertValidationDepthTests.cs) | ✅ |
| 001 | NonceIsValidOnSignAsync | [SecurityCertValidationDepthTests](Security/SecurityCertValidationDepthTests.cs) | ✅ |
| 001 | NonceIsValidOnSignAndEncryptAsync | [SecurityCertValidationDepthTests](Security/SecurityCertValidationDepthTests.cs) | ✅ |
| 001 | NoncesAreUniqueAcrossFiveSessionsAsync | [SecurityCertValidationDepthTests](Security/SecurityCertValidationDepthTests.cs) | ✅ |
| 001 | NoneEndpointHasNoRequiredCertAsync | [SecurityCertValidationDepthTests](Security/SecurityCertValidationDepthTests.cs) | ✅ |
| 001 | NoneEndpointNonceMayBeEmptyAsync | [SecurityCertValidationDepthTests](Security/SecurityCertValidationDepthTests.cs) | ✅ |
| 001 | NonePolicyExistsAsync | [SecurityCertValidationDepthTests](Security/SecurityCertValidationDepthTests.cs) | ✅ |
| 001 | NoneSecurityLevelIsZeroAsync | [SecurityCertValidationDepthTests](Security/SecurityCertValidationDepthTests.cs) | ✅ |
| 001 | ReconnectYieldsNewSessionIdAsync | [SecurityCertValidationDepthTests](Security/SecurityCertValidationDepthTests.cs) | ✅ |
| 001 | SecureConnectionCanReadServerStatusAsync | [SecurityCertValidationDepthTests](Security/SecurityCertValidationDepthTests.cs) | ✅ |
| 001 | SecureEndpointCertIsPemExportableAsync | [SecurityCertValidationDepthTests](Security/SecurityCertValidationDepthTests.cs) | ✅ |
| 001 | SecureEndpointCertificateKeyIsAdequateAsync | [SecurityCertificateTests](Security/SecurityCertificateTests.cs) | ✅ |
| 001 | SecureEndpointSecurityLevelIsPositiveAsync | [SecurityCertValidationDepthTests](Security/SecurityCertValidationDepthTests.cs) | ✅ |
| 001 | SecureEndpointUrlIsNotEmptyAsync | [SecurityCertValidationDepthTests](Security/SecurityCertValidationDepthTests.cs) | ✅ |
| 001 | ServerCertAppUriMatchesEndpointAppUriAsync | [SecurityCertValidationTests](Security/SecurityCertValidationTests.cs) | ✅ |
| 001 | ServerCertHasClientAuthEkuAsync | [SecurityCertValidationTests](Security/SecurityCertValidationTests.cs) | ✅ |
| 001 | ServerCertHasDataEnciphermentKeyUsageAsync | [SecurityCertValidationTests](Security/SecurityCertValidationTests.cs) | ✅ |
| 001 | ServerCertHasDigitalSignatureKeyUsageAsync | [SecurityCertValidationTests](Security/SecurityCertValidationTests.cs) | ✅ |
| 001 | ServerCertHasServerAuthEkuAsync | [SecurityCertValidationTests](Security/SecurityCertValidationTests.cs) | ✅ |
| 001 | ServerCertIsSelfSignedOrHasValidIssuerAsync | [SecurityCertValidationTests](Security/SecurityCertValidationTests.cs) | ✅ |
| 001 | ServerCertIsVersionV3Async | [SecurityCertValidationTests](Security/SecurityCertValidationTests.cs) | ✅ |
| 001 | ServerCertKeyLengthAtLeast2048ForRsaAsync | [SecurityCertValidationTests](Security/SecurityCertValidationTests.cs) | ✅ |
| 001 | ServerCertNotAfterIsInFutureAsync | [SecurityCertValidationTests](Security/SecurityCertValidationTests.cs) | ✅ |
| 001 | ServerCertNotBeforeIsInPastAsync | [SecurityCertValidationTests](Security/SecurityCertValidationTests.cs) | ✅ |
| 001 | ServerCertSanContainsApplicationUriAsync | [SecurityCertValidationTests](Security/SecurityCertValidationTests.cs) | ✅ |
| 001 | ServerCertSanContainsHostnameOrIpAsync | [SecurityCertValidationTests](Security/SecurityCertValidationTests.cs) | ✅ |
| 001 | ServerCertSerialNumberIsNonEmptyAsync | [SecurityCertValidationTests](Security/SecurityCertValidationTests.cs) | ✅ |
| 001 | ServerCertificateHasValidDatesAsync | [SecurityCertificateTests](Security/SecurityCertificateTests.cs) | ✅ |
| 001 | ServerCertificateSanContainsApplicationUriAsync | [SecurityCertificateTests](Security/SecurityCertificateTests.cs) | ✅ |
| 001 | ServerCertificateSanContainsHostnameAsync | [SecurityCertificateTests](Security/SecurityCertificateTests.cs) | ✅ |
| 001 | ServerHasAtLeastOneSecureEndpointAsync | [SecurityCertificateTests](Security/SecurityCertificateTests.cs) | ✅ |
| 001 | ServerNonceChangesBetweenSessionsAsync | [SecurityCertValidationTests](Security/SecurityCertValidationTests.cs) | ✅ |
| 001 | ServerNonceIs32BytesOnSecureConnectionAsync | [SecurityCertValidationTests](Security/SecurityCertValidationTests.cs) | ✅ |
| 001 | SessionCertMatchesEndpointCertAsync | [SecurityCertValidationTests](Security/SecurityCertValidationTests.cs) | ✅ |
| 001 | SessionSecurityModeIsNone | [SecurityCertValidationDepthTests](Security/SecurityCertValidationDepthTests.cs) | ✅ |
| 001 | SessionTimeoutIsPositiveAsync | [SecurityCertValidationDepthTests](Security/SecurityCertValidationDepthTests.cs) | ✅ |
| 001 | VerifyEndpointServerCertificateOnSecureEndpointAsync | [SecurityTests](Security/SecurityTests.cs) | ✅ |
| 001 | VerifyEndpointsHaveConsistentServerCertificateAsync | [SecurityTests](Security/SecurityTests.cs) | ✅ |
| 001 | VerifyMinimumKeyLengthOnCertificatesAsync | [SecurityTests](Security/SecurityTests.cs) | ✅ |
| 001 | VerifyMinimumKeySizePerPolicyAsync | [SecurityCertValidationTests](Security/SecurityCertValidationTests.cs) | ✅ |
| 001 | VerifySecureEndpointsHaveNonEmptyServerCertificateAsync | [SecurityTests](Security/SecurityTests.cs) | ✅ |
| 001 | VerifyServerCertificateSubjectDNAsync | [SecurityTests](Security/SecurityTests.cs) | ✅ |
| 001 | VerifySignatureAlgorithmMatchesPolicyAsync | [SecurityCertValidationTests](Security/SecurityCertValidationTests.cs) | ✅ |
| 002 | CertValidation002ConnectCertSignedByKnownUntrustedCAAsync | [SecurityCertValidationTests](Security/SecurityCertValidationTests.cs) | ✅ |
| 004 | CertValidation004EmptyClientCertificateAsync | [SecurityCertValidationTests](Security/SecurityCertValidationTests.cs) | ✅ |
| 005 | CertErrorUntrustedIsIgnoredAsync | [SecurityCertValidationDepthTests](Security/SecurityCertValidationDepthTests.cs) | ⏭️ |
| 005 | CertValidation005UntrustedCertificateAsync | [SecurityCertValidationTests](Security/SecurityCertValidationTests.cs) | ✅ |
| 007 | CertErrorExpiredIsIgnoredAsync | [SecurityCertValidationDepthTests](Security/SecurityCertValidationDepthTests.cs) | ⏭️ |
| 007 | CertValidation007ExpiredTrustedCertificate | [SecurityCertValidationTests](Security/SecurityCertValidationTests.cs) | ⏭️ |
| 008 | CertErrorNotYetValidIsIgnoredAsync | [SecurityCertValidationDepthTests](Security/SecurityCertValidationDepthTests.cs) | ⏭️ |
| 008 | CertValidation008NotYetValidCertificate | [SecurityCertValidationTests](Security/SecurityCertValidationTests.cs) | ⏭️ |
| 009 | CertValidation009CertFromUnknownCA | [SecurityCertValidationTests](Security/SecurityCertValidationTests.cs) | ⏭️ |
| 010 | CertValidation010InvalidSignature | [SecurityCertValidationTests](Security/SecurityCertValidationTests.cs) | ⏭️ |
| 029 | CertBasicConstraintsIsNotCaAsync | [SecurityCertValidationDepthTests](Security/SecurityCertValidationDepthTests.cs) | ✅ |
| 029 | CertBasicConstraintsPathLengthIsZeroOrAbsentAsync | [SecurityCertValidationDepthTests](Security/SecurityCertValidationDepthTests.cs) | ✅ |
| 029 | CertValidation029CACertificateNotAppInstance | [SecurityCertValidationTests](Security/SecurityCertValidationTests.cs) | ⏭️ |
| 033 | CertValidation033ExpiredCertNotTrusted | [SecurityCertValidationTests](Security/SecurityCertValidationTests.cs) | ⏭️ |
| 037 | CertValidation037IssuedCertificate | [SecurityCertValidationTests](Security/SecurityCertValidationTests.cs) | ⏭️ |
| 038 | CertErrorRevokedIsIgnoredAsync | [SecurityCertValidationDepthTests](Security/SecurityCertValidationDepthTests.cs) | ⏭️ |
| 038 | CertValidation038RevokedCertificate | [SecurityCertValidationTests](Security/SecurityCertValidationTests.cs) | ⏭️ |
| 042 | CertValidation042TrustedIssuedCertNoRevocationList | [SecurityCertValidationTests](Security/SecurityCertValidationTests.cs) | ⏭️ |
| 043 | CertValidation043UntrustedIssuedCertNoRevocationList | [SecurityCertValidationTests](Security/SecurityCertValidationTests.cs) | ⏭️ |
| 044 | CertValidation044TrustedIssuedCertCANotTrusted | [SecurityCertValidationTests](Security/SecurityCertValidationTests.cs) | ⏭️ |
| 045 | CertValidation045UntrustedIssuedCertCANotTrusted | [SecurityCertValidationTests](Security/SecurityCertValidationTests.cs) | ⏭️ |
| 046 | CertValidation046UntrustedCertFromUnknownCA | [SecurityCertValidationTests](Security/SecurityCertValidationTests.cs) | ⏭️ |
| 047 | CertValidation047RevokedCertNotTrusted | [SecurityCertValidationTests](Security/SecurityCertValidationTests.cs) | ⏭️ |
| 048 | CertIssuerEqualsSubjectForSelfSignedAsync | [SecurityCertValidationDepthTests](Security/SecurityCertValidationDepthTests.cs) | ✅ |
| 048 | CertValidation048ConnectWithTrustedClientCertAsync | [SecurityCertValidationTests](Security/SecurityCertValidationTests.cs) | ✅ |
| 048 | ConnectWithTrustedCertSucceedsAsync | [SecurityCertificateTests](Security/SecurityCertificateTests.cs) | ✅ |
| 048 | SelfSignedCertificateIsAcceptedAsync | [SecurityCertValidationDepthTests](Security/SecurityCertValidationDepthTests.cs) | ✅ |
| 049 | CertValidation049TrustedClientCertSha1_1024 | [SecurityCertValidationTests](Security/SecurityCertValidationTests.cs) | ⏭️ |
| 050 | CertValidation050TrustedClientCertSha1_2048 | [SecurityCertValidationTests](Security/SecurityCertValidationTests.cs) | ⏭️ |
| 051 | CertValidation051TrustedClientCertSha2_2048 | [SecurityCertValidationTests](Security/SecurityCertValidationTests.cs) | ⏭️ |
| 052 | CertValidation052TrustedClientCertSha2_4096 | [SecurityCertValidationTests](Security/SecurityCertValidationTests.cs) | ⏭️ |

**Additional coverage** (not mapped to specific source scripts):

| NUnit Test | Fixture | Status |
|-----------|---------|--------|
| CertErrorHostnameMismatchIsIgnored | [SecurityCertValidationDepthTests](Security/SecurityCertValidationDepthTests.cs) | ⏭️ |
| CertErrorKeyTooShortIsIgnored | [SecurityCertValidationDepthTests](Security/SecurityCertValidationDepthTests.cs) | ⏭️ |
| CertErrorUriMismatchIsIgnored | [SecurityCertValidationDepthTests](Security/SecurityCertValidationDepthTests.cs) | ⏭️ |

</details>

<details>
<summary>Security / Security Default ApplicationInstance Certificate ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | DefaultCert001CheckInitialCertificateStateAsync | [SecurityCertificateTests](Security/SecurityCertificateTests.cs) | ✅ |
| 002 | DefaultCert002EstablishCommunicationAsync | [SecurityCertificateTests](Security/SecurityCertificateTests.cs) | ✅ |
| 003 | DefaultCert003EnsureCurrentCertIsValidAsync | [SecurityCertificateTests](Security/SecurityCertificateTests.cs) | ✅ |
| 003 | VerifyEndpointApplicationUriMatchesServerAsync | [SecurityTests](Security/SecurityTests.cs) | ✅ |

</details>

<details>
<summary>Security / Security Encryption Required ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | ConnectWithSignAndEncryptSecurityModeAsync | [SecurityTests](Security/SecurityTests.cs) | ✅ |

</details>

<details>
<summary>Security / Security Invalid user token ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | ActivateWithEmptyUsernameIsRejectedAsync | [SecurityUserTokenTests](Security/SecurityUserTokenTests.cs) | ✅ |
| 001 | ActivateWithSpecialCharsInUsernameIsRejectedAsync | [SecurityUserTokenTests](Security/SecurityUserTokenTests.cs) | ✅ |
| 001 | ConnectWithEmptyPasswordRejectedAsync | [SecurityUserTokenTests](Security/SecurityUserTokenTests.cs) | ✅ |
| 002 | ActivateWithUnicodePasswordIsRejectedAsync | [SecurityUserTokenTests](Security/SecurityUserTokenTests.cs) | ✅ |
| 002 | ActivateWithVeryLongPasswordIsRejectedAsync | [SecurityUserTokenTests](Security/SecurityUserTokenTests.cs) | ✅ |
| 002 | ActivateWithVeryLongUsernameIsRejectedAsync | [SecurityUserTokenTests](Security/SecurityUserTokenTests.cs) | ✅ |

</details>

<details>
<summary>Security / Security None ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|

</details>

<details>
<summary>Security / Security None CreateSession ActivateSession ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | ConnectWithSecurityModeNone | [SecurityTests](Security/SecurityTests.cs) | ✅ |
| 001 | SessionSecurityModeIsNone | [SecurityTests](Security/SecurityTests.cs) | ✅ |
| 004 | VerifyNoneEndpointHasZeroSecurityLevelAsync | [SecurityTests](Security/SecurityTests.cs) | ✅ |

</details>

<details>
<summary>Security / Security None CreateSession ActivateSession 1.0 ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 002 | NoneSession002ClientSpecifiesExpiredCert | [SecurityNoneSession10Tests](Security/SecurityNoneSession10Tests.cs) | ⏭️ |
| 003 | NoneSession003ClientSpecifiesCertForAnotherComputer | [SecurityNoneSession10Tests](Security/SecurityNoneSession10Tests.cs) | ⏭️ |
| 004 | NoneSession004ClientSpecifiesCorruptedCert | [SecurityNoneSession10Tests](Security/SecurityNoneSession10Tests.cs) | ⏭️ |

</details>

<details>
<summary>Security / Security Policy Required ⏭️</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|

</details>

<details>
<summary>Security / Security Role Server ApplicationManagement ⏭️</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 005 | AppMgmt005RemoveAllApplicationsAsync | [SecurityRoleServerAppMgmtTests](Security/SecurityRoleServerAppMgmtTests.cs) | ⏭️ |

</details>

<details>
<summary>Security / Security Role Server Authorization ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | Auth001RestrictAccessByRoleAsync | [SecurityRoleServerAuthTests](Security/SecurityRoleServerAuthTests.cs) | ⏭️ |
| 002 | Auth002UnmappedUserCannotLoginAsync | [SecurityRoleServerAuthTests](Security/SecurityRoleServerAuthTests.cs) | ✅ |

</details>

<details>
<summary>Security / Security Role Server Base 2 ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 002 | Base2VerifyNamespaceMetadataInstance002Async | [SecurityRoleServerBase2Tests](Security/SecurityRoleServerBase2Tests.cs) | ✅ |
| 004 | Base2DefaultRolePermissions004Async | [SecurityRoleServerBase2Tests](Security/SecurityRoleServerBase2Tests.cs) | ✅ |
| 005 | Base2DefaultUserRolePermissions005Async | [SecurityRoleServerBase2Tests](Security/SecurityRoleServerBase2Tests.cs) | ✅ |
| 006 | Base2DefaultAccessRestrictions006Async | [SecurityRoleServerBase2Tests](Security/SecurityRoleServerBase2Tests.cs) | ✅ |
| 007 | Base2FindRolePermissions007Async | [SecurityRoleServerBase2Tests](Security/SecurityRoleServerBase2Tests.cs) | ⏭️ |
| 008 | Base2FindUserRolePermissions008Async | [SecurityRoleServerBase2Tests](Security/SecurityRoleServerBase2Tests.cs) | ⏭️ |
| 009 | Base2FindAccessRestrictions009Async | [SecurityRoleServerBase2Tests](Security/SecurityRoleServerBase2Tests.cs) | ⏭️ |

</details>

<details>
<summary>Security / Security Role Server Base Eventing ⏭️</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 002 | Eventing002IdentityChangeAuditEventAsync | [SecurityRoleServerEventingTests](Security/SecurityRoleServerEventingTests.cs) | ⏭️ |

</details>

<details>
<summary>Security / Security Role Server EndpointManagement ⏭️</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|

</details>

<details>
<summary>Security / Security Role Server IdentityManagement ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | MapUsernameIdentityToRoleAsync | [SecurityRoleServerTests](Security/SecurityRoleServerTests.cs) | ⏭️ |
| 001 | RoleHasAddIdentityMethodAsync | [RoleManagementTests](Security/RoleManagementTests.cs) | ✅ |
| 002 | MapCertificateIdentityToRoleAsync | [SecurityRoleServerTests](Security/SecurityRoleServerTests.cs) | ⏭️ |
| 002 | RoleHasRemoveIdentityMethodAsync | [RoleManagementTests](Security/RoleManagementTests.cs) | ⏭️ |
| 003 | AddIdentityToObserverRoleSucceedsAsync | [RoleManagementTests](Security/RoleManagementTests.cs) | ⏭️ |
| 003 | RemoveUsernameIdentityMappingAsync | [SecurityRoleServerTests](Security/SecurityRoleServerTests.cs) | ⏭️ |
| 004 | ReadObserverIdentitiesAfterAddAsync | [RoleManagementTests](Security/RoleManagementTests.cs) | ⏭️ |
| 004 | RemoveCertificateIdentityMappingAsync | [SecurityRoleServerTests](Security/SecurityRoleServerTests.cs) | ⏭️ |
| 005 | AddMultipleIdentitiesToSameRoleAsync | [SecurityRoleServerTests](Security/SecurityRoleServerTests.cs) | ⏭️ |
| 005 | RemoveIdentityFromObserverRoleSucceedsAsync | [RoleManagementTests](Security/RoleManagementTests.cs) | ⏭️ |
| 006 | ReadIdentitiesReflectsMultipleEntriesAsync | [SecurityRoleServerTests](Security/SecurityRoleServerTests.cs) | ⏭️ |
| 006 | ReadObserverIdentitiesAfterRemoveAsync | [RoleManagementTests](Security/RoleManagementTests.cs) | ⏭️ |
| 007 | AddIdentityToAnonymousRoleAsync | [SecurityRoleServerTests](Security/SecurityRoleServerTests.cs) | ⏭️ |
| 007 | AddIdentityWithUserNameCriteriaAsync | [RoleManagementTests](Security/RoleManagementTests.cs) | ⏭️ |
| 008 | AddIdentityToSecurityAdminRoleAsync | [SecurityRoleServerTests](Security/SecurityRoleServerTests.cs) | ⏭️ |
| 008 | AddIdentityWithThumbprintCriteriaAsync | [RoleManagementTests](Security/RoleManagementTests.cs) | ⏭️ |
| 009 | AddIdentityDuplicateIsIdempotentAsync | [RoleManagementTests](Security/RoleManagementTests.cs) | ⏭️ |
| 009 | IdentityWithGroupIdCriteriaAsync | [SecurityRoleServerTests](Security/SecurityRoleServerTests.cs) | ⏭️ |
| 010 | IdentityWithApplicationCriteriaAsync | [SecurityRoleServerTests](Security/SecurityRoleServerTests.cs) | ⏭️ |
| 010 | RemoveNonExistentIdentityReturnsNoMatchAsync | [RoleManagementTests](Security/RoleManagementTests.cs) | ✅ |
| 011 | AddIdentityWithoutSecurityAdminFailsAsync | [RoleManagementTests](Security/RoleManagementTests.cs) | ⏭️ |
| 011 | AllRolesHaveAddIdentityMethodAsync | [SecurityRoleServerTests](Security/SecurityRoleServerTests.cs) | ✅ |
| 012 | AllRolesHaveRemoveIdentityMethodAsync | [SecurityRoleServerTests](Security/SecurityRoleServerTests.cs) | ✅ |
| 012 | RemoveIdentityWithoutSecurityAdminFailsAsync | [RoleManagementTests](Security/RoleManagementTests.cs) | ⏭️ |
| 013 | AddIdentityWithNoArgumentsFailsAsync | [RoleManagementTests](Security/RoleManagementTests.cs) | ✅ |
| 014 | AddIdentityWithEmptyCriteriaFailsAsync | [RoleManagementTests](Security/RoleManagementTests.cs) | ✅ |

</details>

<details>
<summary>Security / Security Role Server Management , 23 additional ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 004 | AddIdentityWithAnonymousCriteriaAsync | [RoleManagementDepthTests](Security/RoleManagementDepthTests.cs) | ⏭️ |
| 002 | AddIdentityWithGroupCriteriaAsync | [RoleManagementDepthTests](Security/RoleManagementDepthTests.cs) | ✅ |
| 001 | AddIdentityWithThumbprintCriteriaAsync | [RoleManagementDepthTests](Security/RoleManagementDepthTests.cs) | ⏭️ |
| 001 | AddRoleMethodExistsOnRoleSetAsync | [SecurityRoleServerTests](Security/SecurityRoleServerTests.cs) | ⏭️ |
| 001 | RoleSetHasAddRoleMethodAsync | [RoleManagementTests](Security/RoleManagementTests.cs) | ⏭️ |
| 002 | RemoveRoleMethodExistsOnRoleSetAsync | [SecurityRoleServerTests](Security/SecurityRoleServerTests.cs) | ⏭️ |
| 003 | MultipleMethodCallsInSingleRequestAsync | [SecurityRoleServerTests](Security/SecurityRoleServerTests.cs) | ⏭️ |
| 004 | RoleChangesArePersistentWithinSessionAsync | [SecurityRoleServerTests](Security/SecurityRoleServerTests.cs) | ⏭️ |
| 005 | AddMultipleIdentitiesAsync | [RoleManagementDepthTests](Security/RoleManagementDepthTests.cs) | ✅ |
| 006 | ReadIdentitiesAfterAddAsync | [RoleManagementDepthTests](Security/RoleManagementDepthTests.cs) | ✅ |
| 007 | RemoveOneIdentityAsync | [RoleManagementDepthTests](Security/RoleManagementDepthTests.cs) | ⏭️ |
| 008 | RemoveAllIdentitiesAsync | [RoleManagementDepthTests](Security/RoleManagementDepthTests.cs) | ✅ |
| 010 | AllWellKnownRolesExistAsync | [RoleManagementDepthTests](Security/RoleManagementDepthTests.cs) | ✅ |
| 011 | EmptyIdentitiesPropertyAsync | [RoleManagementDepthTests](Security/RoleManagementDepthTests.cs) | ✅ |
| 014 | AddValidApplicationUriAsync | [RoleManagementDepthTests](Security/RoleManagementDepthTests.cs) | ✅ |
| 012 | ZeroCriteriaTypeHandled | [RoleManagementDepthTests](Security/RoleManagementDepthTests.cs) | ✅ |

**Additional coverage** (not mapped to specific source scripts):

| NUnit Test | Fixture | Status |
|-----------|---------|--------|
| AddMultipleApplicationUris | [RoleManagementDepthTests](Security/RoleManagementDepthTests.cs) | ✅ |
| AddMultipleEndpoints | [RoleManagementDepthTests](Security/RoleManagementDepthTests.cs) | ✅ |
| AddValidEndpointUrl | [RoleManagementDepthTests](Security/RoleManagementDepthTests.cs) | ⏭️ |
| AllRolesHaveApplicationMethods | [RoleManagementDepthTests](Security/RoleManagementDepthTests.cs) | ✅ |
| ApplicationAndEndpointOnSameRole | [RoleManagementDepthTests](Security/RoleManagementDepthTests.cs) | ✅ |
| CannotRemoveWellKnownRole | [RoleManagementDepthTests](Security/RoleManagementDepthTests.cs) | ⏭️ |
| ClearAllRestrictions | [RoleManagementDepthTests](Security/RoleManagementDepthTests.cs) | ✅ |
| DuplicateApplicationUri | [RoleManagementDepthTests](Security/RoleManagementDepthTests.cs) | ✅ |
| DuplicateEndpointUrl | [RoleManagementDepthTests](Security/RoleManagementDepthTests.cs) | ✅ |
| EmptyApplicationUri | [RoleManagementDepthTests](Security/RoleManagementDepthTests.cs) | ✅ |
| NoApplicationsConfiguredByDefault | [RoleManagementDepthTests](Security/RoleManagementDepthTests.cs) | ✅ |
| ReadAfterRestrictions | [RoleManagementDepthTests](Security/RoleManagementDepthTests.cs) | ✅ |
| ReadApplicationsAfterAdd | [RoleManagementDepthTests](Security/RoleManagementDepthTests.cs) | ⏭️ |
| ReadEndpointsAfterAdd | [RoleManagementDepthTests](Security/RoleManagementDepthTests.cs) | ✅ |
| RemoveAllApplications | [RoleManagementDepthTests](Security/RoleManagementDepthTests.cs) | ✅ |
| RemoveAllEndpoints | [RoleManagementDepthTests](Security/RoleManagementDepthTests.cs) | ✅ |
| RemoveIdentityFromOneRoleOnly | [RoleManagementDepthTests](Security/RoleManagementDepthTests.cs) | ✅ |
| RemoveOneApplication | [RoleManagementDepthTests](Security/RoleManagementDepthTests.cs) | ✅ |
| RemoveOneEndpoint | [RoleManagementDepthTests](Security/RoleManagementDepthTests.cs) | ✅ |
| SameIdentityToMultipleRoles | [RoleManagementDepthTests](Security/RoleManagementDepthTests.cs) | ✅ |

</details>

<details>
<summary>Security / Security Role Server Restrict Applications ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | AddApplicationRestrictionAsync | [SecurityRoleServerTests](Security/SecurityRoleServerTests.cs) | ⏭️ |
| 001 | RoleHasApplicationsPropertyAsync | [RoleManagementTests](Security/RoleManagementTests.cs) | ⏭️ |
| 002 | ReadApplicationRestrictionAsync | [SecurityRoleServerTests](Security/SecurityRoleServerTests.cs) | ⏭️ |
| 002 | ReadApplicationsExcludePropertyAsync | [RoleManagementTests](Security/RoleManagementTests.cs) | ✅ |
| 003 | AddApplicationToRoleSucceedsAsync | [RoleManagementTests](Security/RoleManagementTests.cs) | ⏭️ |
| 003 | AddMultipleApplicationsAsync | [SecurityRoleServerTests](Security/SecurityRoleServerTests.cs) | ⏭️ |
| 004 | ReadApplicationsAfterAddAsync | [RoleManagementTests](Security/RoleManagementTests.cs) | ⏭️ |
| 004 | RemoveApplicationRestrictionAsync | [SecurityRoleServerTests](Security/SecurityRoleServerTests.cs) | ⏭️ |
| 005 | ApplicationsExcludeDefaultValueAsync | [SecurityRoleServerTests](Security/SecurityRoleServerTests.cs) | ✅ |
| 005 | RemoveApplicationFromRoleSucceedsAsync | [RoleManagementTests](Security/RoleManagementTests.cs) | ⏭️ |
| 006 | AddApplicationWithoutAdminFailsAsync | [RoleManagementTests](Security/RoleManagementTests.cs) | ⏭️ |
| 006 | RemoveLastApplicationClearsAsync | [SecurityRoleServerTests](Security/SecurityRoleServerTests.cs) | ⏭️ |
| 007 | AddApplicationDuplicateIsIdempotentAsync | [SecurityRoleServerTests](Security/SecurityRoleServerTests.cs) | ⏭️ |
| 008 | RemoveNonExistentApplicationFailsAsync | [SecurityRoleServerTests](Security/SecurityRoleServerTests.cs) | ✅ |
| 009 | AddApplicationToObserverRoleAsync | [SecurityRoleServerTests](Security/SecurityRoleServerTests.cs) | ⏭️ |
| 010 | ReadObserverApplicationsAfterAddAsync | [SecurityRoleServerTests](Security/SecurityRoleServerTests.cs) | ⏭️ |
| 011 | RemoveApplicationFromObserverRoleAsync | [SecurityRoleServerTests](Security/SecurityRoleServerTests.cs) | ⏭️ |
| 012 | AddApplicationToMultipleRolesAsync | [SecurityRoleServerTests](Security/SecurityRoleServerTests.cs) | ⏭️ |
| 013 | AddApplicationWithoutAdminFailsAsync | [SecurityRoleServerTests](Security/SecurityRoleServerTests.cs) | ⏭️ |
| 014 | RemoveApplicationWithoutAdminFailsAsync | [SecurityRoleServerTests](Security/SecurityRoleServerTests.cs) | ⏭️ |

</details>

<details>
<summary>Security / Security Role Server Restrict Endpoints ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | RoleHasEndpointsPropertyAsync | [RoleManagementTests](Security/RoleManagementTests.cs) | ✅ |
| 002 | ReadEndpointRestrictionAfterAddAsync | [SecurityRoleServerTests](Security/SecurityRoleServerTests.cs) | ⏭️ |
| 002 | ReadEndpointsExcludePropertyAsync | [RoleManagementTests](Security/RoleManagementTests.cs) | ⏭️ |
| 003 | AddEndpointToRoleSucceedsAsync | [RoleManagementTests](Security/RoleManagementTests.cs) | ⏭️ |
| 003 | AddMultipleEndpointsAsync | [SecurityRoleServerTests](Security/SecurityRoleServerTests.cs) | ✅ |
| 004 | ReadEndpointsAfterAddAsync | [RoleManagementTests](Security/RoleManagementTests.cs) | ⏭️ |
| 004 | RemoveEndpointRestrictionAsync | [SecurityRoleServerTests](Security/SecurityRoleServerTests.cs) | ⏭️ |
| 005 | RemoveEndpointFromRoleSucceedsAsync | [RoleManagementTests](Security/RoleManagementTests.cs) | ⏭️ |
| 005 | RemoveLastEndpointClearsAsync | [SecurityRoleServerTests](Security/SecurityRoleServerTests.cs) | ⏭️ |
| 006 | AddEndpointWithoutAdminFailsAsync | [RoleManagementTests](Security/RoleManagementTests.cs) | ⏭️ |
| 006 | EndpointsExcludeDefaultIsFalseAsync | [SecurityRoleServerTests](Security/SecurityRoleServerTests.cs) | ⏭️ |
| 007 | AddEndpointWithEmptyUrlFailsAsync | [SecurityRoleServerTests](Security/SecurityRoleServerTests.cs) | ✅ |
| 008 | AddEndpointDuplicateIsIdempotentAsync | [SecurityRoleServerTests](Security/SecurityRoleServerTests.cs) | ⏭️ |
| 009 | RemoveNonExistentEndpointReturnsNoMatchAsync | [SecurityRoleServerTests](Security/SecurityRoleServerTests.cs) | ✅ |
| 010 | AddEndpointWithoutAdminFailsAsync | [SecurityRoleServerTests](Security/SecurityRoleServerTests.cs) | ⏭️ |

</details>

<details>
<summary>Security / Security Role Well Known ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | RoleSetBrowseReturnsAllWellKnownRolesAsync | [SecurityRoleServerTests](Security/SecurityRoleServerTests.cs) | ✅ |
| 002 | ConfigureAdminRoleExistsAsync | [RoleManagementTests](Security/RoleManagementTests.cs) | ✅ |
| 002 | EngineerRoleExistsAsync | [RoleManagementTests](Security/RoleManagementTests.cs) | ✅ |
| 002 | ObserverRoleExistsAsync | [RoleManagementTests](Security/RoleManagementTests.cs) | ✅ |
| 002 | OperatorRoleExistsAsync | [RoleManagementTests](Security/RoleManagementTests.cs) | ✅ |
| 002 | RoleSetContainsWellKnownRolesAsync | [RoleManagementTests](Security/RoleManagementTests.cs) | ✅ |
| 002 | SecurityAdminRoleExistsAsync | [RoleManagementTests](Security/RoleManagementTests.cs) | ✅ |
| 002 | SupervisorRoleExistsAsync | [RoleManagementTests](Security/RoleManagementTests.cs) | ✅ |
| 003 | AnonymousRoleHasIdentitiesPropertyAsync | [RoleManagementTests](Security/RoleManagementTests.cs) | ⏭️ |
| 003 | AnonymousRoleNodeClassIsObjectAsync | [RoleManagementTests](Security/RoleManagementTests.cs) | ✅ |
| 003 | AuthenticatedUserRoleHasCorrectNodeClassAsync | [RoleManagementTests](Security/RoleManagementTests.cs) | ✅ |
| 003 | ReadAnonymousIdentitiesAsync | [RoleManagementTests](Security/RoleManagementTests.cs) | ⏭️ |
| 003 | RoleHasTypeDefinitionRoleTypeAsync | [RoleManagementTests](Security/RoleManagementTests.cs) | ✅ |

</details>

<details>
<summary>Security / Security Signing Required ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | ConnectWithSignSecurityModeAsync | [SecurityTests](Security/SecurityTests.cs) | ✅ |

</details>

<details>
<summary>Security / Security User Anonymous ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | AnonymousCanReadNodesAsync | [SecurityUserTokenTests](Security/SecurityUserTokenTests.cs) | ✅ |
| 001 | AnonymousTokenTypeAsync | [SecurityUserTokenDepthTests](Security/SecurityUserTokenDepthTests.cs) | ✅ |
| 001 | NonceIsUniquePerSessionAsync | [SecurityUserTokenDepthTests](Security/SecurityUserTokenDepthTests.cs) | ✅ |
| 001 | SessionKeepAliveAsync | [SecurityUserTokenDepthTests](Security/SecurityUserTokenDepthTests.cs) | ✅ |
| 001 | SessionTimeoutBehaviorAsync | [SecurityUserTokenDepthTests](Security/SecurityUserTokenDepthTests.cs) | ✅ |
| 001 | VerifyAnonymousUserTokenOnEndpointAsync | [SecurityTests](Security/SecurityTests.cs) | ✅ |
| 002 | EndpointsAdvertiseUsernameTokenAsync | [SecurityUserTokenDepthTests](Security/SecurityUserTokenDepthTests.cs) | ✅ |
| 002 | IssuedTokenTypeForUsernameAsync | [SecurityUserTokenDepthTests](Security/SecurityUserTokenDepthTests.cs) | ⏭️ |
| 002 | MultipleEndpointsWithDifferentTokensAsync | [SecurityUserTokenDepthTests](Security/SecurityUserTokenDepthTests.cs) | ✅ |
| 002 | SecurityLevelValueAsync | [SecurityUserTokenDepthTests](Security/SecurityUserTokenDepthTests.cs) | ✅ |
| 002 | UsernameTokenHasSecurityPolicyAsync | [SecurityUserTokenDepthTests](Security/SecurityUserTokenDepthTests.cs) | ✅ |
| 002 | UsernameTokenPolicyIdPresentAsync | [SecurityUserTokenDepthTests](Security/SecurityUserTokenDepthTests.cs) | ✅ |
| 003 | ConnectWithUsernamePasswordAsync | [SecurityUserTokenDepthTests](Security/SecurityUserTokenDepthTests.cs) | ✅ |
| 003 | KerberosAuthorizationDataIgnored | [SecurityUserTokenDepthTests](Security/SecurityUserTokenDepthTests.cs) | ⏭️ |
| 003 | KerberosClaimMappingIgnored | [SecurityUserTokenDepthTests](Security/SecurityUserTokenDepthTests.cs) | ⏭️ |
| 003 | KerberosConnectionIgnored | [SecurityUserTokenDepthTests](Security/SecurityUserTokenDepthTests.cs) | ⏭️ |
| 003 | KerberosCredentialCachingIgnored | [SecurityUserTokenDepthTests](Security/SecurityUserTokenDepthTests.cs) | ⏭️ |
| 003 | KerberosDelegationIgnored | [SecurityUserTokenDepthTests](Security/SecurityUserTokenDepthTests.cs) | ⏭️ |
| 003 | KerberosEncryptionIgnored | [SecurityUserTokenDepthTests](Security/SecurityUserTokenDepthTests.cs) | ⏭️ |
| 003 | KerberosErrorHandlingIgnored | [SecurityUserTokenDepthTests](Security/SecurityUserTokenDepthTests.cs) | ⏭️ |
| 003 | KerberosGroupMembershipIgnored | [SecurityUserTokenDepthTests](Security/SecurityUserTokenDepthTests.cs) | ⏭️ |
| 003 | KerberosIntegrityCheckIgnored | [SecurityUserTokenDepthTests](Security/SecurityUserTokenDepthTests.cs) | ⏭️ |
| 003 | KerberosMultiAuthIgnored | [SecurityUserTokenDepthTests](Security/SecurityUserTokenDepthTests.cs) | ⏭️ |
| 003 | KerberosPreAuthIgnored | [SecurityUserTokenDepthTests](Security/SecurityUserTokenDepthTests.cs) | ⏭️ |
| 003 | KerberosRealmHandlingIgnored | [SecurityUserTokenDepthTests](Security/SecurityUserTokenDepthTests.cs) | ⏭️ |
| 003 | KerberosServicePrincipalIgnored | [SecurityUserTokenDepthTests](Security/SecurityUserTokenDepthTests.cs) | ⏭️ |
| 003 | KerberosSessionCachingIgnored | [SecurityUserTokenDepthTests](Security/SecurityUserTokenDepthTests.cs) | ⏭️ |
| 003 | KerberosTimeSkewIgnored | [SecurityUserTokenDepthTests](Security/SecurityUserTokenDepthTests.cs) | ⏭️ |
| 003 | KerberosTokenAdvertisementIgnored | [SecurityUserTokenDepthTests](Security/SecurityUserTokenDepthTests.cs) | ⏭️ |
| 003 | KerberosTokenRefreshIgnored | [SecurityUserTokenDepthTests](Security/SecurityUserTokenDepthTests.cs) | ⏭️ |
| 003 | KerberosTokenStructureIgnored | [SecurityUserTokenDepthTests](Security/SecurityUserTokenDepthTests.cs) | ⏭️ |
| 004 | ActivateWithAnonymousIdentityAsync | [SecurityTests](Security/SecurityTests.cs) | ✅ |
| 004 | ChangeIdentityBetweenSessionsAsync | [SecurityUserTokenDepthTests](Security/SecurityUserTokenDepthTests.cs) | ✅ |
| 004 | SwitchFromUserNameToAnonymousMidSessionAsync | [SecurityUserTokenTests](Security/SecurityUserTokenTests.cs) | ✅ |

</details>

<details>
<summary>Security / Security User Management Server ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | UserDatabaseIsAvailable | [UserManagementTests](Security/UserManagementTests.cs) | ✅ |
| 002 | DefaultUsersExistInDatabase | [UserManagementTests](Security/UserManagementTests.cs) | ✅ |
| 003 | SysadminHasSecurityAdminRole | [UserManagementTests](Security/UserManagementTests.cs) | ✅ |
| 004 | RegularUserHasAuthenticatedRole | [UserManagementTests](Security/UserManagementTests.cs) | ✅ |
| 005 | AddUserWithValidNameAndPassword | [UserManagementTests](Security/UserManagementTests.cs) | ✅ |
| 006 | AddUserThenCheckCredentials | [UserManagementTests](Security/UserManagementTests.cs) | ✅ |
| 007 | AddUserWithDuplicateNameUpdatesUser | [UserManagementTests](Security/UserManagementTests.cs) | ✅ |
| 008 | AddUserWithEmptyNameThrows | [UserManagementTests](Security/UserManagementTests.cs) | ✅ |
| 009 | AddUserWithEmptyPasswordThrows | [UserManagementTests](Security/UserManagementTests.cs) | ✅ |
| 010 | AddUserWithSpecificRoles | [UserManagementTests](Security/UserManagementTests.cs) | ✅ |
| 011 | AddUserWithMaxLengthPassword | [UserManagementTests](Security/UserManagementTests.cs) | ✅ |
| 012 | AddUserWithSpecialCharactersInNameAndPassword | [UserManagementTests](Security/UserManagementTests.cs) | ✅ |
| 013 | RemoveUserSucceeds | [UserManagementTests](Security/UserManagementTests.cs) | ✅ |
| 014 | RemoveUserVerifyCanNoLongerAuthenticate | [UserManagementTests](Security/UserManagementTests.cs) | ✅ |
| 015 | RemoveNonExistentUserReturnsFalse | [UserManagementTests](Security/UserManagementTests.cs) | ✅ |
| 016 | ChangePasswordSucceeds | [UserManagementTests](Security/UserManagementTests.cs) | ✅ |
| 017 | ChangePasswordVerifyOldNoLongerWorks | [UserManagementTests](Security/UserManagementTests.cs) | ✅ |
| 018 | ChangePasswordVerifyNewWorks | [UserManagementTests](Security/UserManagementTests.cs) | ✅ |
| 019 | ChangePasswordWithWrongOldPasswordFails | [UserManagementTests](Security/UserManagementTests.cs) | ✅ |
| 020 | ChangePasswordForNonExistentUserFails | [UserManagementTests](Security/UserManagementTests.cs) | ✅ |
| 021 | AddUserThenConnectWithNewCredentialsAsync | [UserManagementTests](Security/UserManagementTests.cs) | ✅ |
| 022 | RemoveUserThenConnectionFailsAsync | [UserManagementTests](Security/UserManagementTests.cs) | ✅ |
| 023 | ChangePasswordThenReconnectWithNewPasswordAsync | [UserManagementTests](Security/UserManagementTests.cs) | ✅ |
| 024 | AddUserDisconnectReconnectAsync | [UserManagementTests](Security/UserManagementTests.cs) | ✅ |
| 025 | AdminCanStillConnectAfterUserOperationsAsync | [UserManagementTests](Security/UserManagementTests.cs) | ✅ |
| 026 | MultipleAddRemoveCycles | [UserManagementTests](Security/UserManagementTests.cs) | ✅ |
| 027 | GetUserRolesForNonExistentUserThrows | [UserManagementTests](Security/UserManagementTests.cs) | ✅ |
| 028 | ChangePasswordWithEmptyOldPasswordThrows | [UserManagementTests](Security/UserManagementTests.cs) | ✅ |
| 029 | ChangePasswordWithEmptyNewPasswordThrows | [UserManagementTests](Security/UserManagementTests.cs) | ✅ |
| 030 | CheckCredentialsWithWrongPasswordReturnsFalse | [UserManagementTests](Security/UserManagementTests.cs) | ✅ |
| 031 | CheckCredentialsWithNonExistentUserReturnsFalse | [UserManagementTests](Security/UserManagementTests.cs) | ✅ |
| 032 | AddUserWithMinNameLength | [UserManagementDepthTests](Security/UserManagementDepthTests.cs) | ✅ |
| 033 | AddUserWithMaxNameLength | [UserManagementDepthTests](Security/UserManagementDepthTests.cs) | ✅ |
| 034 | AddUserWithUnicodeName | [UserManagementDepthTests](Security/UserManagementDepthTests.cs) | ✅ |
| 035 | AddUserWithNullNameThrows | [UserManagementDepthTests](Security/UserManagementDepthTests.cs) | ✅ |
| 036 | AddUserWithWhitespaceName | [UserManagementDepthTests](Security/UserManagementDepthTests.cs) | ✅ |
| 037 | VerifyCredentialsAfterCreation | [UserManagementDepthTests](Security/UserManagementDepthTests.cs) | ✅ |
| 038 | RemoveNonExistentUserReturnsFalse | [UserManagementDepthTests](Security/UserManagementDepthTests.cs) | ✅ |
| 039 | RemoveUserTwiceSecondReturnsFalse | [UserManagementDepthTests](Security/UserManagementDepthTests.cs) | ✅ |
| 040 | UpdatePasswordSucceeds | [UserManagementDepthTests](Security/UserManagementDepthTests.cs) | ⏭️ |
| 041 | CheckRolesAfterCreation | [UserManagementDepthTests](Security/UserManagementDepthTests.cs) | ✅ |
| 042 | CreateUserWithEmptyRoles | [UserManagementDepthTests](Security/UserManagementDepthTests.cs) | ✅ |
| 043 | InvalidRoleFails | [UserManagementDepthTests](Security/UserManagementDepthTests.cs) | ✅ |
| 044 | CreateUserDuplicateNameOverwrites | [UserManagementDepthTests](Security/UserManagementDepthTests.cs) | ✅ |
| 045 | MinPasswordLengthAccepted | [UserManagementDepthTests](Security/UserManagementDepthTests.cs) | ✅ |
| 046 | MaxPasswordLengthAccepted | [UserManagementDepthTests](Security/UserManagementDepthTests.cs) | ✅ |
| 047 | UnicodePasswordAccepted | [UserManagementDepthTests](Security/UserManagementDepthTests.cs) | ✅ |
| 048 | SequentialPasswordChanges | [UserManagementDepthTests](Security/UserManagementDepthTests.cs) | ✅ |
| 049 | OldPasswordFailsAfterChange | [UserManagementDepthTests](Security/UserManagementDepthTests.cs) | ✅ |
| 051 | AddTenUsersSequentially | [UserManagementDepthTests](Security/UserManagementDepthTests.cs) | ✅ |
| 052 | RemoveTenUsersSequentially | [UserManagementDepthTests](Security/UserManagementDepthTests.cs) | ✅ |
| 053 | RapidAddRemoveCycle | [UserManagementDepthTests](Security/UserManagementDepthTests.cs) | ✅ |
| 054 | ThreeSimultaneousSessionsSucceedAsync | [UserManagementDepthTests](Security/UserManagementDepthTests.cs) | ✅ |
| 055 | ConnectThenDeleteUserSessionStillActiveAsync | [UserManagementDepthTests](Security/UserManagementDepthTests.cs) | ✅ |
| 056 | ReconnectAfterDeletionFailsAsync | [UserManagementDepthTests](Security/UserManagementDepthTests.cs) | ✅ |
| 057 | ChangePasswordActiveSessionAsync | [UserManagementDepthTests](Security/UserManagementDepthTests.cs) | ✅ |
| 058 | NewSessionNeedsNewPasswordAsync | [UserManagementDepthTests](Security/UserManagementDepthTests.cs) | ✅ |
| 060 | AllRolesAssignableToUser | [UserManagementDepthTests](Security/UserManagementDepthTests.cs) | ✅ |
| 062 | CaseSensitiveUserName | [UserManagementDepthTests](Security/UserManagementDepthTests.cs) | ✅ |
| 063 | EmptyPasswordHandled | [UserManagementDepthTests](Security/UserManagementDepthTests.cs) | ✅ |
| 064 | SpecialCharactersInPassword | [UserManagementDepthTests](Security/UserManagementDepthTests.cs) | ✅ |

</details>

<details>
<summary>Security / Security User Name Password 2 ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | ActivateCorrectCredentialsOnNoneAsync | [SecurityUserTokenTests](Security/SecurityUserTokenTests.cs) | ✅ |
| 001 | ConnectWithSysadminCredentialsAsync | [SecurityTests](Security/SecurityTests.cs) | ✅ |
| 001 | SysadminCanReadNodeAsync | [SecurityUserTokenTests](Security/SecurityUserTokenTests.cs) | ✅ |
| 001 | SysadminCanWriteNodeAsync | [SecurityUserTokenTests](Security/SecurityUserTokenTests.cs) | ✅ |
| 002 | ActivateCorrectCredentialsOnSignAsync | [SecurityUserTokenTests](Security/SecurityUserTokenTests.cs) | ✅ |
| 002 | ConnectSysadminOnSignEndpointAsync | [SecurityUserTokenTests](Security/SecurityUserTokenTests.cs) | ✅ |
| 002 | ConnectWithAppuserCredentialsAsync | [SecurityTests](Security/SecurityTests.cs) | ⏭️ |
| 003 | ConnectWithEmptyUsernameReturnsBadIdentityTokenInvalidAsync | [SecurityTests](Security/SecurityTests.cs) | ✅ |
| 007 | ConnectWithWrongPasswordReturnsBadIdentityTokenRejectedAsync | [SecurityTests](Security/SecurityTests.cs) | ✅ |
| 009 | ConnectWithSpecialCharsUsernameAsync | [SecurityTests](Security/SecurityTests.cs) | ✅ |
| 009 | SwitchFromAnonymousToUserNameMidSessionAsync | [SecurityUserTokenTests](Security/SecurityUserTokenTests.cs) | ✅ |
| 009 | SysadminCanReadAdminRestrictedNodeAsync | [SecurityUserTokenTests](Security/SecurityUserTokenTests.cs) | ✅ |
| 011 | ActivateCorrectCredentialsOnSignAndEncryptAsync | [SecurityUserTokenTests](Security/SecurityUserTokenTests.cs) | ✅ |
| 011 | ConnectSysadminOnSignAndEncryptEndpointAsync | [SecurityUserTokenTests](Security/SecurityUserTokenTests.cs) | ✅ |
| 012 | AppuserWriteDeniedAsync | [SecurityUserTokenTests](Security/SecurityUserTokenTests.cs) | ⏭️ |
| 012 | AppuserWriteToAdminNodeDeniedAsync | [SecurityUserTokenTests](Security/SecurityUserTokenTests.cs) | ⏭️ |
| 012 | ConnectAppuserVerifyLimitedAccessAsync | [SecurityUserTokenTests](Security/SecurityUserTokenTests.cs) | ⏭️ |
| 012 | ConnectWithSysadminWriteToNodeAsync | [SecurityTests](Security/SecurityTests.cs) | ✅ |
| 012 | SwitchFromOneUserToAnotherMidSessionAsync | [SecurityUserTokenTests](Security/SecurityUserTokenTests.cs) | ⏭️ |
| 013 | EachSessionHasUniqueSessionIdAsync | [SecurityUserTokenTests](Security/SecurityUserTokenTests.cs) | ✅ |
| 013 | VerifySecurityLevelOrderingAsync | [SecurityUserTokenTests](Security/SecurityUserTokenTests.cs) | ✅ |
| 014 | ConnectWithEachSecurityPolicyAsync | [SecurityUserTokenTests](Security/SecurityUserTokenTests.cs) | ✅ |
| 014 | SessionTimeoutIsPositiveAsync | [SecurityUserTokenTests](Security/SecurityUserTokenTests.cs) | ✅ |
| 015 | UserNameTokenPolicyIdMatchesAdvertisedAsync | [SecurityUserTokenTests](Security/SecurityUserTokenTests.cs) | ✅ |
| 015 | VerifyUsernameUserTokenOnEndpointAsync | [SecurityTests](Security/SecurityTests.cs) | ✅ |

</details>

<details>
<summary>Security / Security User X509 , 6 additional ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | ActivateWithSignAndEncryptX509SucceedsAsync | [SecurityX509UserTests](Security/SecurityX509UserTests.cs) | ✅ |
| 001 | ActivateWithValidX509CertOnSecureEndpointAsync | [SecurityX509UserTests](Security/SecurityX509UserTests.cs) | ✅ |
| 001 | ActivateX509OnSecurityModeNoneAsync | [SecurityX509UserTests](Security/SecurityX509UserTests.cs) | ✅ |
| 001 | AtLeastOneEndpointAdvertisesCertificateTokenAsync | [SecurityX509UserTests](Security/SecurityX509UserTests.cs) | ✅ |
| 001 | CertificateTokenHasSecurityPolicyAsync | [SecurityUserX509DepthTests](Security/SecurityUserX509DepthTests.cs) | ✅ |
| 001 | CertificateTokenIssuedTokenTypeAsync | [SecurityUserX509DepthTests](Security/SecurityUserX509DepthTests.cs) | ✅ |
| 001 | CertificateTokenPolicyUriAsync | [SecurityUserX509DepthTests](Security/SecurityUserX509DepthTests.cs) | ✅ |
| 001 | EndpointsAdvertiseCertificateTokenAsync | [SecurityUserX509DepthTests](Security/SecurityUserX509DepthTests.cs) | ✅ |
| 001 | SessionDiagnosticsShowsX509AuthAsync | [SecurityX509UserTests](Security/SecurityX509UserTests.cs) | ✅ |
| 001 | SwitchFromAnonymousToX509Async | [SecurityX509UserTests](Security/SecurityX509UserTests.cs) | ✅ |
| 001 | SwitchFromX509ToAnonymousAsync | [SecurityX509UserTests](Security/SecurityX509UserTests.cs) | ✅ |
| 001 | TwoSessionsWithSameX509CertAsync | [SecurityX509UserTests](Security/SecurityX509UserTests.cs) | ✅ |
| 001 | X509TokenIncludesCertificateDataAsync | [SecurityX509UserTests](Security/SecurityX509UserTests.cs) | ✅ |
| 001 | X509UserCanReadNodeAsync | [SecurityX509UserTests](Security/SecurityX509UserTests.cs) | ✅ |
| 001 | X509UserWriteBehaviorAsync | [SecurityX509UserTests](Security/SecurityX509UserTests.cs) | ✅ |
| 002 | ActivateWithUntrustedX509CertIsRejectedAsync | [SecurityX509UserTests](Security/SecurityX509UserTests.cs) | ✅ |
| 005 | ActivateWithExpiredX509CertIsRejectedAsync | [SecurityX509UserTests](Security/SecurityX509UserTests.cs) | ✅ |
| 007 | X509CertWithAppUriInSanBehaviorAsync | [SecurityX509UserTests](Security/SecurityX509UserTests.cs) | ✅ |
| 009 | X509CertWithWrongKeyUsageBehaviorAsync | [SecurityX509UserTests](Security/SecurityX509UserTests.cs) | ✅ |
| 010 | RootCertificateTrustNotEstablishedAsync | [SecurityUserX509DepthTests](Security/SecurityUserX509DepthTests.cs) | ✅ |
| 011 | CertificateChainValidationDepthAsync | [SecurityUserX509DepthTests](Security/SecurityUserX509DepthTests.cs) | ✅ |
| 013 | IntermediateCertificateHandlingAsync | [SecurityUserX509DepthTests](Security/SecurityUserX509DepthTests.cs) | ✅ |

**Additional coverage** (not mapped to specific source scripts):

| NUnit Test | Fixture | Status |
|-----------|---------|--------|

</details>

<details>
<summary>Security / SecurityPolicy Support ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | AnonymousTokenTypeAvailableAsync | [SecurityPolicyDepthTests](Security/SecurityPolicyDepthTests.cs) | ✅ |
| 001 | AtLeastOneSecureEndpointAsync | [SecurityPolicyDepthTests](Security/SecurityPolicyDepthTests.cs) | ✅ |
| 001 | BasicSecurityPoliciesIfSupportedAsync | [SecurityPolicyDepthTests](Security/SecurityPolicyDepthTests.cs) | ✅ |
| 001 | ConnectSecondSessionVerifyIndependentSecurityAsync | [SecurityTests](Security/SecurityTests.cs) | ✅ |
| 001 | ConnectWithNonePolicyAsync | [SecurityPolicyDepthTests](Security/SecurityPolicyDepthTests.cs) | ✅ |
| 001 | EachEndpointHasValidModeAsync | [SecurityPolicyDepthTests](Security/SecurityPolicyDepthTests.cs) | ✅ |
| 001 | EachTokenPolicyHasIssuedTokenTypeAsync | [SecurityPolicyDepthTests](Security/SecurityPolicyDepthTests.cs) | ✅ |
| 001 | EachTokenPolicyHasSecurityPolicyUriAsync | [SecurityPolicyDepthTests](Security/SecurityPolicyDepthTests.cs) | ✅ |
| 001 | EndpointsAdvertiseSecurityPoliciesAsync | [SecurityPolicyDepthTests](Security/SecurityPolicyDepthTests.cs) | ✅ |
| 001 | EndpointsAdvertiseUserTokenPoliciesAsync | [SecurityPolicyDepthTests](Security/SecurityPolicyDepthTests.cs) | ✅ |
| 001 | NoneModeAlwaysSupportedAsync | [SecurityPolicyDepthTests](Security/SecurityPolicyDepthTests.cs) | ✅ |
| 001 | NoneSecurityPolicyPresentAsync | [SecurityPolicyDepthTests](Security/SecurityPolicyDepthTests.cs) | ✅ |
| 001 | SecurityModeMatchesPolicyConsistencyAsync | [SecurityPolicyDepthTests](Security/SecurityPolicyDepthTests.cs) | ✅ |
| 001 | SecurityPolicyUriFormatAsync | [SecurityPolicyDepthTests](Security/SecurityPolicyDepthTests.cs) | ✅ |
| 001 | SessionSecurityDetailsRecordedAsync | [SecurityPolicyDepthTests](Security/SecurityPolicyDepthTests.cs) | ✅ |
| 001 | SignAndEncryptModeIfSupportedAsync | [SecurityPolicyDepthTests](Security/SecurityPolicyDepthTests.cs) | ✅ |
| 001 | SignOnlyModeIfSupportedAsync | [SecurityPolicyDepthTests](Security/SecurityPolicyDepthTests.cs) | ✅ |
| 001 | UsernameTokenTypeIfAvailableAsync | [SecurityPolicyDepthTests](Security/SecurityPolicyDepthTests.cs) | ✅ |
| 001 | VerifyEndpointListsUserIdentityTokenTypesAsync | [SecurityTests](Security/SecurityTests.cs) | ✅ |
| 001 | VerifyEndpointSecurityLevelOrderingAsync | [SecurityTests](Security/SecurityTests.cs) | ✅ |
| 001 | VerifyEndpointServerDescriptionIsServerAsync | [SecurityTests](Security/SecurityTests.cs) | ✅ |
| 001 | VerifySecureEndpointHasUserTokenPoliciesAsync | [SecurityTests](Security/SecurityTests.cs) | ✅ |
| 001 | VerifySecurityLevelHigherForMoreSecureAsync | [SecurityTests](Security/SecurityTests.cs) | ✅ |
| 001 | VerifySecurityPolicyUriIsValidAsync | [SecurityTests](Security/SecurityTests.cs) | ✅ |
| 001 | VerifySecurityPolicyUriStartsWithOpcFoundationAsync | [SecurityTests](Security/SecurityTests.cs) | ✅ |
| 001 | VerifyServerAdvertisesSecureEndpointsAsync | [SecurityTests](Security/SecurityTests.cs) | ✅ |
| 001 | VerifyTransportProfileUriAsync | [SecurityTests](Security/SecurityTests.cs) | ✅ |

</details>

### Session Services

<details>
<summary>Session Services / Session Base ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | CreateSessionWithRequestedTimeout | [SessionBaseTests](SessionServices/SessionBaseTests.cs) | ✅ |
| 001 | Session007SessionTimeout | [SessionTests](SessionServices/SessionTests.cs) | ✅ |
| 001 | SessionTimeoutIsRevisedByServer | [SessionBaseTests](SessionServices/SessionBaseTests.cs) | ✅ |
| 001 | CreateSessionWithZeroTimeoutReturnsRevisedTimeoutAsync | [SessionBaseTests](SessionServices/SessionBaseTests.cs) | ✅ |
| 002 | SessionKeepaliveVerifySessionStaysActiveAsync | [SessionBaseTests](SessionServices/SessionBaseTests.cs) | ✅ |
| 002 | CreateSessionStallsBeyondTimeoutPeriodAsync | [SessionBaseTests](SessionServices/SessionBaseTests.cs) | ✅ |
| 003 | ReadCurrentSubscriptionsCountMatchesOursAsync | [SessionDiagnosticsTests](SessionServices/SessionDiagnosticsTests.cs) | ✅ |
| 003 | ReadMaxBrowseContinuationPointsAsync | [SessionDiagnosticsTests](SessionServices/SessionDiagnosticsTests.cs) | ✅ |
| 003 | ReadRejectedRequestCountAsync | [SessionDiagnosticsTests](SessionServices/SessionDiagnosticsTests.cs) | ✅ |
| 003 | ReadRejectedSessionCountAsync | [SessionBaseTests](SessionServices/SessionBaseTests.cs) | ✅ |
| 003 | ReadServerDiagnosticsSummaryAsync | [SessionDiagnosticsTests](SessionServices/SessionDiagnosticsTests.cs) | ✅ |
| 003 | ReadServerStateIsRunningAsync | [SessionDiagnosticsTests](SessionServices/SessionDiagnosticsTests.cs) | ✅ |
| 003 | ReadServerViewCountAsync | [SessionDiagnosticsTests](SessionServices/SessionDiagnosticsTests.cs) | ✅ |
| 003 | ReadSessionDiagnosticsArrayFindOurSessionAsync | [SessionBaseTests](SessionServices/SessionBaseTests.cs) | ✅ |
| 003 | ReadSessionDiagnosticsArrayFindsCurrentSessionAsync | [SessionDiagnosticsTests](SessionServices/SessionDiagnosticsTests.cs) | ⏭️ |
| 003 | ReadSessionDiagnosticsCumulatedSessionCountAsync | [SessionBaseTests](SessionServices/SessionBaseTests.cs) | ✅ |
| 003 | ReadSessionDiagnosticsCurrentSessionCountAsync | [SessionBaseTests](SessionServices/SessionBaseTests.cs) | ✅ |
| 003 | ReadSessionDiagnosticsCurrentSubscriptionsCountAsync | [SessionBaseTests](SessionServices/SessionBaseTests.cs) | ✅ |
| 003 | ReadSessionDiagnosticsSecurityRejectedRequestsCountAsync | [SessionBaseTests](SessionServices/SessionBaseTests.cs) | ✅ |
| 003 | ReadSessionSecurityDiagnosticsAsync | [SessionBaseTests](SessionServices/SessionBaseTests.cs) | ✅ |
| 003 | ReadTotalRequestCountGreaterThanZeroAsync | [SessionDiagnosticsTests](SessionServices/SessionDiagnosticsTests.cs) | ✅ |
| 003 | ReadUnauthorizedRequestCountZeroForSuccessfulSessionAsync | [SessionDiagnosticsTests](SessionServices/SessionDiagnosticsTests.cs) | ✅ |
| 003 | Session012ServerDiagnosticsAsync | [SessionTests](SessionServices/SessionTests.cs) | ✅ |
| 003 | CreateSessionAppearsInServerDiagnosticsAsync | [SessionBaseTests](SessionServices/SessionBaseTests.cs) | ✅ |
| 003 | VerifySessionDiagnosticsEndpointUrl | [SessionDiagnosticsTests](SessionServices/SessionDiagnosticsTests.cs) | ✅ |
| 003 | VerifySessionDiagnosticsServerUriAsync | [SessionDiagnosticsTests](SessionServices/SessionDiagnosticsTests.cs) | ✅ |
| 003 | VerifySessionDiagnosticsSessionName | [SessionDiagnosticsTests](SessionServices/SessionDiagnosticsTests.cs) | ✅ |
| 004 | ActivateMultipleTimesOnSameSessionAsync | [SessionBaseTests](SessionServices/SessionBaseTests.cs) | ✅ |
| 004 | ActivateSessionWithAnonymousIdentityAsync | [SessionBaseTests](SessionServices/SessionBaseTests.cs) | ✅ |
| 004 | CreateSessionAndReadServerStateAsync | [SessionBaseTests](SessionServices/SessionBaseTests.cs) | ✅ |
| 004 | CreateSessionWithSpecificName | [SessionBaseTests](SessionServices/SessionBaseTests.cs) | ✅ |
| 004 | DiscoveryClientCreatedAndDisposedAsync | [SessionlessInvocationTests](SessionServices/SessionlessInvocationTests.cs) | ✅ |
| 004 | FindServersApplicationTypeIsServerOrBothAsync | [SessionlessInvocationTests](SessionServices/SessionlessInvocationTests.cs) | ✅ |
| 004 | FindServersMultipleCallsInSequenceAsync | [SessionlessInvocationTests](SessionServices/SessionlessInvocationTests.cs) | ✅ |
| 004 | FindServersReturnsDiscoveryUrlsAsync | [SessionlessInvocationTests](SessionServices/SessionlessInvocationTests.cs) | ✅ |
| 004 | FindServersReturnsValidApplicationDescriptionAsync | [SessionlessInvocationTests](SessionServices/SessionlessInvocationTests.cs) | ✅ |
| 004 | FindServersWithEndpointUrlAsync | [SessionlessInvocationTests](SessionServices/SessionlessInvocationTests.cs) | ✅ |
| 004 | FindServersWithoutSessionAsync | [SessionlessInvocationTests](SessionServices/SessionlessInvocationTests.cs) | ✅ |
| 004 | GetEndpointsMultipleCallsInSequenceAsync | [SessionlessInvocationTests](SessionServices/SessionlessInvocationTests.cs) | ✅ |
| 004 | GetEndpointsReturnsDifferentSecurityModesAsync | [SessionlessInvocationTests](SessionServices/SessionlessInvocationTests.cs) | ✅ |
| 004 | GetEndpointsReturnsTransportProfileUriAsync | [SessionlessInvocationTests](SessionServices/SessionlessInvocationTests.cs) | ✅ |
| 004 | GetEndpointsReturnsValidEndpointsAsync | [SessionlessInvocationTests](SessionServices/SessionlessInvocationTests.cs) | ✅ |
| 004 | GetEndpointsWithProfileFilterAsync | [SessionlessInvocationTests](SessionServices/SessionlessInvocationTests.cs) | ✅ |
| 004 | GetEndpointsWithoutSessionAsync | [SessionlessInvocationTests](SessionServices/SessionlessInvocationTests.cs) | ✅ |
| 004 | ReadMaxResponseMessageSize | [SessionBaseTests](SessionServices/SessionBaseTests.cs) | ✅ |
| 004 | ServerTimestampIsRecentAsync | [SessionlessExtendedTests](SessionServices/SessionlessExtendedTests.cs) | ✅ |
| 004 | ServiceResultGoodForValidReadAsync | [SessionlessExtendedTests](SessionServices/SessionlessExtendedTests.cs) | ✅ |
| 004 | Session001VerifySessionConnected | [SessionTests](SessionServices/SessionTests.cs) | ✅ |
| 004 | Session002ReadServerStatusAsync | [SessionTests](SessionServices/SessionTests.cs) | ✅ |
| 004 | Session003SessionId | [SessionTests](SessionServices/SessionTests.cs) | ✅ |
| 004 | Session004SessionName | [SessionTests](SessionServices/SessionTests.cs) | ✅ |
| 004 | Session008ServerUri | [SessionTests](SessionServices/SessionTests.cs) | ✅ |
| 004 | Session009NamespaceUris | [SessionTests](SessionServices/SessionTests.cs) | ✅ |
| 004 | Session010ReadOperationLimitsAsync | [SessionTests](SessionServices/SessionTests.cs) | ✅ |
| 004 | Session011VerifyEndpointUrl | [SessionTests](SessionServices/SessionTests.cs) | ✅ |
| 004 | SessionEndpointHasTransportProfileUri | [SessionBaseTests](SessionServices/SessionBaseTests.cs) | ✅ |
| 004 | SessionlessGetEndpointsReturnsSameResultsOnRepeatedCallsAsync | [SessionlessInvocationTests](SessionServices/SessionlessInvocationTests.cs) | ✅ |
| 004 | SessionlessGetEndpointsWithEmptyProfileFilterAsync | [SessionlessInvocationTests](SessionServices/SessionlessInvocationTests.cs) | ✅ |
| 004 | ActivateSessionWithDefaultParametersAsync | [SessionBaseTests](SessionServices/SessionBaseTests.cs) | ✅ |
| 004 | VerifySessionEndpointDescription | [SessionBaseTests](SessionServices/SessionBaseTests.cs) | ✅ |
| 005 | GetEndpointsWithLocaleIdsAsync | [SessionlessInvocationTests](SessionServices/SessionlessInvocationTests.cs) | ✅ |
| 005 | CreateSessionWithRankedLocaleIdsAsync | [SessionBaseTests](SessionServices/SessionBaseTests.cs) | ✅ |
| 005 | VerifySessionPreferredLocales | [SessionBaseTests](SessionServices/SessionBaseTests.cs) | ✅ |
| 008 | ActivateSessionTransferredToAnotherChannelAsync | [SessionBaseTests](SessionServices/SessionBaseTests.cs) | ✅ |
| 009 | SessionIdentityToken | [SessionBaseTests](SessionServices/SessionBaseTests.cs) | ✅ |
| 009 | CreateSessionWithNoSoftwareCertificatesAsync | [SessionBaseTests](SessionServices/SessionBaseTests.cs) | ✅ |
| 010 | CloseSessionAndVerifyDisconnectedAsync | [SessionBaseTests](SessionServices/SessionBaseTests.cs) | ✅ |
| 010 | CloseSessionWithDeleteSubscriptionsFalseAsync | [SessionBaseTests](SessionServices/SessionBaseTests.cs) | ✅ |
| 010 | CloseSessionWithDeleteSubscriptionsTrueAsync | [SessionBaseTests](SessionServices/SessionBaseTests.cs) | ✅ |
| 010 | Session005CreateAndCloseAdditionalSessionAsync | [SessionTests](SessionServices/SessionTests.cs) | ✅ |
| 010 | CloseSessionWithDefaultParametersAsync | [SessionBaseTests](SessionServices/SessionBaseTests.cs) | ✅ |
| 011 | ActivateSessionWithNoSoftwareCertificatesAsync | [SessionBaseTests](SessionServices/SessionBaseTests.cs) | ✅ |
| 012 | DiscoveryClientConnectsWithoutCredentialsAsync | [SessionlessInvocationTests](SessionServices/SessionlessInvocationTests.cs) | ✅ |
| 012 | GetEndpointsReturnsServerCertificateAsync | [SessionlessInvocationTests](SessionServices/SessionlessInvocationTests.cs) | ✅ |
| 012 | SessionlessCallsDoNotRequireAuthenticationAsync | [SessionlessInvocationTests](SessionServices/SessionlessInvocationTests.cs) | ✅ |
| 012 | CreateSessionWithUntrustedCertificateAndNoneSecurityAsync | [SessionBaseTests](SessionServices/SessionBaseTests.cs) | ✅ |
| 012 | VerifySessionServerCertificate | [SessionBaseTests](SessionServices/SessionBaseTests.cs) | ✅ |
| 013 | CreateSessionWithEmptyNameAsync | [SessionBaseTests](SessionServices/SessionBaseTests.cs) | ✅ |
| 013 | CreateSessionWithLongNameAsync | [SessionBaseTests](SessionServices/SessionBaseTests.cs) | ✅ |
| 013 | CreateSessionWithoutSessionNameAsync | [SessionBaseTests](SessionServices/SessionBaseTests.cs) | ✅ |
| 015 | GetEndpointsContainsNoneSecurityModeAsync | [SessionlessInvocationTests](SessionServices/SessionlessInvocationTests.cs) | ✅ |
| 015 | ActivateSessionWithEmptyClientSignatureOnNonSecureChannelAsync | [SessionBaseTests](SessionServices/SessionBaseTests.cs) | ✅ |
| 015 | VerifySessionSecurityModeMatchesConnection | [SessionDiagnosticsTests](SessionServices/SessionDiagnosticsTests.cs) | ✅ |
| 015 | VerifySessionSecurityPolicyUriMatchesConnection | [SessionDiagnosticsTests](SessionServices/SessionDiagnosticsTests.cs) | ✅ |
| Err-001 | CreateAndVerifyMultipleSessionsAsync | [SessionBaseTests](SessionServices/SessionBaseTests.cs) | ✅ |
| Err-001 | CreateSessionVerifySessionIdIsUniqueAsync | [SessionBaseTests](SessionServices/SessionBaseTests.cs) | ✅ |
| Err-004 | BrowseInvalidNodeIdReturnsBadAsync | [SessionlessExtendedTests](SessionServices/SessionlessExtendedTests.cs) | ✅ |
| Err-004 | ReadInvalidNodeIdReturnsBadAsync | [SessionlessExtendedTests](SessionServices/SessionlessExtendedTests.cs) | ✅ |
| Err-004 | ServerHandlesEmptyReadListAsync | [SessionlessExtendedTests](SessionServices/SessionlessExtendedTests.cs) | ✅ |
| Err-004 | WriteInvalidNodeIdReturnsBadAsync | [SessionlessExtendedTests](SessionServices/SessionlessExtendedTests.cs) | ✅ |
| Err-019 | Session006MultipleParallelSessionsAsync | [SessionTests](SessionServices/SessionTests.cs) | ✅ |

</details>

<details>
<summary>Session Services / Session Cancel ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | CancelInFlightRequestReturnsCountAsync | [SessionCancelTests](SessionServices/SessionCancelTests.cs) | ✅ |
| 003 | CancelCompletedRequestReturnsZeroAsync | [SessionCancelTests](SessionServices/SessionCancelTests.cs) | ✅ |
| 004 | CancelUnknownRequestHandleReturnsZeroAsync | [SessionCancelTests](SessionServices/SessionCancelTests.cs) | ✅ |
| Err-001 | CancelWithInjectedBadNothingToDoAsync | [SessionCancelTests](SessionServices/SessionCancelTests.cs) | ✅ |
| Err-002 | CancelWithInjectedZeroCancelCountAsync | [SessionCancelTests](SessionServices/SessionCancelTests.cs) | ✅ |
| Err-003 | CancelWithInjectedDecrementedCancelCountAsync | [SessionCancelTests](SessionServices/SessionCancelTests.cs) | ✅ |
| Err-004 | CancelWithInjectedIncrementedCancelCountAsync | [SessionCancelTests](SessionServices/SessionCancelTests.cs) | ✅ |

</details>

<details>
<summary>Session Services / Session Change User ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|

</details>

<details>
<summary>Session Services / Session General Service Behaviour ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|

</details>

<details>
<summary>Session Services / Session Multiple ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|

</details>

### Subscription Services

<details>
<summary>Subscription Services / Subscription Basic ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | CreateSubscriptionDefaultParamsAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 001 | CreateSubscriptionWithAllDefaultParametersAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| 001 | CreateSubscriptionWithDefaultParamsAsync | [SubscriptionTests](SubscriptionServices/SubscriptionTests.cs) | ✅ |
| 001 | CreateSubscriptionWithMaxPriorityAsync | [SubscriptionTests](SubscriptionServices/SubscriptionTests.cs) | ✅ |
| 001 | CreateSubscriptionWithPriorityZeroAsync | [SubscriptionTests](SubscriptionServices/SubscriptionTests.cs) | ✅ |
| 001 | SubscriptionVeryLargeMaxNotificationsServerRevisesAsync | [SubscriptionDepthTests](SubscriptionServices/SubscriptionDepthTests.cs) | ✅ |
| 002 | CreateSubscriptionPublishingIntervalOneAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 002 | CreateSubscriptionReturnsRevisedPublishingIntervalAsync | [SubscriptionTests](SubscriptionServices/SubscriptionTests.cs) | ✅ |
| 003 | CreateSubscriptionPublishingIntervalMaxDoubleAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| 003 | CreateSubscriptionPublishingIntervalZeroRevisedAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 003 | CreateSubscriptionWithZeroIntervalServerRevisesToMinimumAsync | [SubscriptionTests](SubscriptionServices/SubscriptionTests.cs) | ✅ |
| 004 | CreateSubscriptionPublishingIntervalMaxDoubleAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 004 | CreateSubscriptionWithSmallIntervalRevisesUpwardAsync | [SubscriptionTests](SubscriptionServices/SubscriptionTests.cs) | ✅ |
| 004 | SubscriptionRevisedIntervalIsAtLeastServerMinimumAsync | [SubscriptionDepthTests](SubscriptionServices/SubscriptionDepthTests.cs) | ✅ |
| 005 | CreateSubscriptionLifetimeZeroKeepAliveZeroAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 005 | KeepAliveCountZeroRevisedToMinimumAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| 005 | SubscriptionLifetimeCountRevisedWhenZeroAsync | [SubscriptionDepthTests](SubscriptionServices/SubscriptionDepthTests.cs) | ✅ |
| 006 | CreateSubscriptionLifetimeThreeKeepAliveOneAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 006 | CreateSubscriptionVerifyLifetimeGreaterOrEqualThreeTimesKeepAliveAsync | [SubscriptionTests](SubscriptionServices/SubscriptionTests.cs) | ✅ |
| 006 | KeepAliveCountOneMinimumKeepalivesAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| 007 | CreateSubscriptionLifetimeEqualKeepAliveAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 007 | SubscriptionLifetimeWithVerySmallValuesAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| 008 | CreateSubscriptionLifetimeLessThanKeepAliveAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 008 | SubscriptionRevisesKeepAliveCountIfLifetimeTooSmallAsync | [SubscriptionDepthTests](SubscriptionServices/SubscriptionDepthTests.cs) | ✅ |
| 009 | CreateSubscriptionLifetimeLessThanThreeTimesKeepAliveAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 009 | CreateSubscriptionLifetimeRevisedWhenLessThanThreeTimesKeepAliveAsync | [SubscriptionTests](SubscriptionServices/SubscriptionTests.cs) | ✅ |
| 009 | SubscriptionLifetimeCountMustBeThreeTimesKeepAliveAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| 010 | CreateSubscriptionLifetimeMaxKeepAliveMaxDivThreeAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 010 | CreateSubscriptionWithLargeLifetimeRevisesDownwardAsync | [SubscriptionTests](SubscriptionServices/SubscriptionTests.cs) | ✅ |
| 011 | CreateSubscriptionLifetimeMaxKeepAliveMaxDivTwoAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 012 | CreateSubscriptionLifetimeHalfMaxKeepAliveMaxAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 012 | SubscriptionLifetimeWithMaxValuesAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| 013 | CreateSubscriptionLifetimeMaxKeepAliveMaxAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 014 | CreateSubscriptionPublishingDisabledAtCreationAsync | [SubscriptionTests](SubscriptionServices/SubscriptionTests.cs) | ✅ |
| 014 | CreateSubscriptionPublishingDisabledNoDataChangeAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 015 | CreateAddDeleteItemsNoMoreNotificationsAsync | [SubscriptionDepthTests](SubscriptionServices/SubscriptionDepthTests.cs) | ✅ |
| 015 | CreateSubscriptionNoItemsPublishKeepAliveAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 015 | KeepAliveHasEmptyNotificationDataAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| 015 | KeepAliveReceivedWithinExpectedIntervalAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| 015 | KeepAliveSequenceNumberProgressesAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| 015 | SubscriptionKeepAliveReceivedBeforeTimeoutAsync | [SubscriptionDepthTests](SubscriptionServices/SubscriptionDepthTests.cs) | ✅ |
| 015 | SubscriptionWithZeroMonitoredItemsOnlyKeepAliveAsync | [SubscriptionDepthTests](SubscriptionServices/SubscriptionDepthTests.cs) | ✅ |
| 016 | CreateSubscriptionLifetimeNotExpiredBeforeExpectedTimeAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 016 | SubscriptionLifetimeResetByPublishAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| 017 | CreateSubscriptionPublishTwiceKeepAliveSequenceNumberOneAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 018 | CreateSubscriptionInterval3000KeepAlive3PublishTwiceAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 019 | CreateSubscriptionDelayedPublishImmediateResponseAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 020 | CreateSubscriptionDisabledPublishTwiceKeepAliveOnlyAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 021 | CreateSubscriptionDisabledWaitHalfKeepAlivePublishTwiceAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 022 | CreateSubscriptionWithItemWritePublishThenKeepAliveAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 023 | ModifySubscriptionChangeAllParametersAsync | [SubscriptionTests](SubscriptionServices/SubscriptionTests.cs) | ✅ |
| 023 | ModifySubscriptionChangePriorityAsync | [SubscriptionDepthTests](SubscriptionServices/SubscriptionDepthTests.cs) | ✅ |
| 023 | ModifySubscriptionChangesIntervalAsync | [SubscriptionTests](SubscriptionServices/SubscriptionTests.cs) | ✅ |
| 023 | ModifySubscriptionDefaultParamsAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 024 | ModifySubscriptionIntervalHigherBySevenMsAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 025 | ModifySubscriptionIntervalLowerBySevenMsAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 026 | ModifySubscriptionIntervalMatchesRevisedFromCreateAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 027 | ModifySubscriptionIntervalOneFastestSupportedAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 028 | ModifySubscriptionIntervalZeroRevisedAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 029 | ModifySubscriptionIntervalMaxFloatRevisedAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 029 | ModifySubscriptionThenPublishStillWorksAsync | [SubscriptionDepthTests](SubscriptionServices/SubscriptionDepthTests.cs) | ✅ |
| 029 | ModifySubscriptionToShorterIntervalAcceptedAsync | [SubscriptionDepthTests](SubscriptionServices/SubscriptionDepthTests.cs) | ✅ |
| 029 | SubscriptionLifetimePreservedAcrossModifyAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| 030 | ModifyKeepAliveCountAndVerifyTimingAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| 030 | ModifySubscriptionIncreaseKeepAliveCountAsync | [SubscriptionDepthTests](SubscriptionServices/SubscriptionDepthTests.cs) | ✅ |
| 030 | ModifySubscriptionLifetimeCountAcceptedAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| 030 | ModifySubscriptionReturnsRevisedKeepAliveCountAsync | [SubscriptionTests](SubscriptionServices/SubscriptionTests.cs) | ✅ |
| 030 | ModifySubscriptionVariousLifetimeKeepAliveCombinationsAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 031 | ModifySubscriptionLifetimeRevisedToMatchKeepAliveConstraintAsync | [SubscriptionTests](SubscriptionServices/SubscriptionTests.cs) | ✅ |
| 031 | ModifySubscriptionLifetimeZeroKeepAliveZeroAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 032 | ModifySubscriptionLifetimeThreeKeepAliveOneAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 033 | ModifySubscriptionLifetimeEqualsKeepAliveAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 034 | ModifySubscriptionLifetimeLessThanKeepAliveAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 035 | ModifySubscriptionLifetimeLessThanThreeTimesKeepAliveAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 036 | ModifySubscriptionLifetimeMaxKeepAliveMaxDivTwoAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 037 | ModifySubscriptionLifetimeMaxKeepAliveMaxDivThreeAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 038 | ModifySubscriptionLifetimeHalfMaxKeepAliveMaxAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 039 | ModifySubscriptionLifetimeCountBelowMinRevisedAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| 039 | ModifySubscriptionLifetimeMaxKeepAliveMaxAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 040 | MaxNotificationsOneOnlyOneItemPerPublishAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| 040 | MaxNotificationsPerPublishWithQueuedItemsAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| 040 | ModifyMaxNotificationsPerPublishAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| 040 | ModifySubscriptionMaxNotificationsPerPublishToOneAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 040 | MoreNotificationsFlagSetWhenLimitedAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| 040 | SubscriptionMaxNotificationsPerPublishLimitAsync | [SubscriptionDepthTests](SubscriptionServices/SubscriptionDepthTests.cs) | ✅ |
| 041 | MaxNotificationsLargerThanItemCountAllDeliveredAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| 041 | MaxNotificationsZeroMeansNoLimitAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| 041 | ModifySubscriptionMaxNotificationsPerPublishToTenAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 041 | MoreNotificationsFlagClearWhenNotLimitedAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| 041 | SubscriptionMaxNotificationsPerPublishZeroMeansUnlimitedAsync | [SubscriptionDepthTests](SubscriptionServices/SubscriptionDepthTests.cs) | ✅ |
| 042 | RepublishOutOfOrderAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 043 | SetPublishingModeDisableAsync | [SubscriptionTests](SubscriptionServices/SubscriptionTests.cs) | ✅ |
| 043 | SetPublishingModeDisableEnabledAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 043 | SetPublishingModeDisableMultipleThenReEnableAsync | [SubscriptionDepthTests](SubscriptionServices/SubscriptionDepthTests.cs) | ✅ |
| 043 | SetPublishingModeEnableThenDisableAsync | [SubscriptionTests](SubscriptionServices/SubscriptionTests.cs) | ✅ |
| 043 | SetPublishingModeToggleNotificationFlowStartsStopsAsync | [SubscriptionDepthTests](SubscriptionServices/SubscriptionDepthTests.cs) | ✅ |
| 044 | SetPublishingModeEnableAsync | [SubscriptionTests](SubscriptionServices/SubscriptionTests.cs) | ✅ |
| 044 | SetPublishingModeEnableDisabledAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 045 | SetPublishingModeReEnableAlreadyEnabledAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 046 | SetPublishingModeDisableAlreadyDisabledAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 047 | SetPublishingModeEnableDuplicateIdsFiveTimesAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 047 | SetPublishingModeOnMultipleSubscriptionsAsync | [SubscriptionTests](SubscriptionServices/SubscriptionTests.cs) | ✅ |
| 048 | PublishDefaultParamsFirstSequenceNumberOneAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 048 | PublishOnDisabledSubscriptionReturnsKeepAliveAsync | [SubscriptionTests](SubscriptionServices/SubscriptionTests.cs) | ✅ |
| 048 | PublishingDisabledAtCreationOnlyKeepAliveAsync | [SubscriptionDepthTests](SubscriptionServices/SubscriptionDepthTests.cs) | ✅ |
| 049 | AvailableSequenceNumbersAfterUnacknowledgedAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| 049 | KeepAliveOnlyWhenNoDataChangesAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| 049 | KeepAliveSubIdMatchesSubscriptionAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| 049 | MultiplePublishRequestsQueuedAndServicedSequentiallyAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| 049 | MultiplePublishesWithoutAcknowledgementSucceedAsync | [SubscriptionDepthTests](SubscriptionServices/SubscriptionDepthTests.cs) | ✅ |
| 049 | NotificationPublishTimeIsValidUtcAsync | [SubscriptionDepthTests](SubscriptionServices/SubscriptionDepthTests.cs) | ✅ |
| 049 | NotificationSequenceNumberMonotonicallyIncreasingAsync | [SubscriptionDepthTests](SubscriptionServices/SubscriptionDepthTests.cs) | ✅ |
| 049 | PublishAcknowledgeValidSequenceNumberAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 049 | PublishRequestDequeuedInOrderAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| 049 | PublishRequestOnePerSubscriptionServicedAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| 049 | PublishRequestQueuedBeforeSubscriptionHasDataAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| 049 | PublishResponseContainsCorrectSubscriptionIdAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| 049 | PublishResponseForFastSubscriptionIsQuickAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| 049 | PublishResponsePublishTimeIsReasonableAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| 049 | PublishResponseTimingRelativeToIntervalAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| 049 | PublishReturnsAvailableSequenceNumbersAsync | [SubscriptionDepthTests](SubscriptionServices/SubscriptionDepthTests.cs) | ✅ |
| 049 | PublishVerifyNotificationMessageTimestampAsync | [SubscriptionTests](SubscriptionServices/SubscriptionTests.cs) | ✅ |
| 049 | PublishWithDataChangeAfterWriteAsync | [SubscriptionTests](SubscriptionServices/SubscriptionTests.cs) | ✅ |
| 049 | SequenceNumberGapDetectionAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| 049 | SequenceNumberStartsAtOneForNewSubscriptionAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| 049 | SequenceNumberWraparoundAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| 050 | AcknowledgeReducesAvailableSequenceNumbersAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| 050 | PublishAcknowledgeMultipleValidSequenceNumbersAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 050 | PublishVerifySequenceNumberIncrementsAsync | [SubscriptionTests](SubscriptionServices/SubscriptionTests.cs) | ✅ |
| 050 | PublishWithAcknowledgementAsync | [SubscriptionTests](SubscriptionServices/SubscriptionTests.cs) | ✅ |
| 051 | PublishAcknowledgeFromMultipleSubscriptionsAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 052 | PublishAcknowledgeMixedValidAndInvalidAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 053 | PublishAcknowledgeAlternatingValidAndInvalidAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 054 | PublishAcknowledgeWithCallbackCountAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 055 | PublishAcknowledgeAlternatingFromValidSubscriptionAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 056 | RepublishDefaultParamsAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 056 | RepublishValidSequenceReturnsOriginalMessageAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| 056 | RepublishWithValidSequenceNumberAsync | [SubscriptionTests](SubscriptionServices/SubscriptionTests.cs) | ✅ |
| 057 | RepublishLastThreeUpdatesCompareAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 057 | RepublishMultipleTimesReturnsSameMessageAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| 058 | RepublishAfterKeepAliveIntervalNoAcksAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 059 | RepublishMissingThirdNotificationAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 060 | CreateDeleteCreateSubscriptionIdsUniqueAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| 060 | CreateSubscriptionThenImmediatelyDeleteAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| 060 | DeleteSingleSubscriptionAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 060 | DeleteSubscriptionAsync | [SubscriptionTests](SubscriptionServices/SubscriptionTests.cs) | ✅ |
| 060 | DeleteSubscriptionCausesStatusChangeNotificationAsync | [SubscriptionDepthTests](SubscriptionServices/SubscriptionDepthTests.cs) | ✅ |
| 060 | DeleteSubscriptionWhilePublishOutstandingSucceedsAsync | [SubscriptionDepthTests](SubscriptionServices/SubscriptionDepthTests.cs) | ✅ |
| 061 | DeleteMultipleSubscriptionsAtOnceAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| 061 | DeleteMultipleSubscriptionsInSingleCallAsync | [SubscriptionTests](SubscriptionServices/SubscriptionTests.cs) | ✅ |
| 061 | DeleteSubscriptionThenModifyReturnsBadIdAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 062 | RepublishSequenceGreaterThanCurrentReturnsBadMessageAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 063 | SubscriptionLifetimeExtendedByNonPublishCallsAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 067 | PublishRequestTimeoutBehaviorAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| 067 | PublishTimeoutSmallerThanKeepAliveAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 067 | PublishTooManyOutstandingRequestsHandledGracefullyAsync | [SubscriptionDepthTests](SubscriptionServices/SubscriptionDepthTests.cs) | ✅ |
| 070 | AcknowledgeSequenceNumbersOutOfOrderAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 071 | CreateFiveSubscriptionsAllUniqueIdsAsync | [SubscriptionTests](SubscriptionServices/SubscriptionTests.cs) | ✅ |
| 071 | CreateMultipleSubscriptionsAsync | [SubscriptionTests](SubscriptionServices/SubscriptionTests.cs) | ✅ |
| 071 | MaxNotificationsWithMultipleSubscriptionsAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| 071 | MultipleSessionsOneSubscriptionPerSessionAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 071 | MultipleSubscriptionsWithDifferentPrioritiesBothServicedAsync | [SubscriptionDepthTests](SubscriptionServices/SubscriptionDepthTests.cs) | ✅ |
| 071 | SubscriptionsWithSameIntervalBothServicedAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| 071 | ThreeSubsDifferentIntervalsAllServicedAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| 071 | TransferSubscriptionsToNewSessionAsync | [SubscriptionTests](SubscriptionServices/SubscriptionTests.cs) | ✅ |
| 072 | PublishTimeoutSmallerThanKeepAliveDescriptionOnlyAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| 072 | TenSubscriptionsAllReceivePublishResponsesAsync | [SubscriptionDepthTests](SubscriptionServices/SubscriptionDepthTests.cs) | ✅ |
| 072 | TwentySubscriptionsAllCreateSuccessfullyAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| 073 | CreateSubscriptionPublishRepublishLoopAsync | [SubscriptionBasicTests](SubscriptionServices/SubscriptionBasicTests.cs) | ✅ |
| Err-001 | SubscriptionNegativeIntervalRevisedToMinimumAsync | [SubscriptionDepthTests](SubscriptionServices/SubscriptionDepthTests.cs) | ✅ |
| Err-004 | SubscriptionLifetimeExpiryDetectedAsync | [SubscriptionDepthTests](SubscriptionServices/SubscriptionDepthTests.cs) | ✅ |
| Err-004 | SubscriptionLifetimeExpiryWithNoPublishRequestsAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| Err-010 | SetPublishingModeWithInvalidIdReturnsBadSubscriptionIdInvalidAsync | [SubscriptionTests](SubscriptionServices/SubscriptionTests.cs) | ✅ |
| Err-012 | PublishAfterAllSubscriptionsDeletedReturnsErrorAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| Err-012 | PublishAfterSessionRecreatedNoSubscriptionsAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| Err-012 | PublishWithZeroSubscriptionsReturnsErrorAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| Err-017 | AcknowledgeInvalidSequenceReturnsErrorAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| Err-017 | PublishWithBadAcknowledgementReturnsResultAsync | [SubscriptionDepthTests](SubscriptionServices/SubscriptionDepthTests.cs) | ✅ |
| Err-022 | RepublishInvalidSequenceReturnsBadAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| Err-022 | RepublishWithInvalidSequenceNumberReturnsBadMessageNotAvailableAsync | [SubscriptionTests](SubscriptionServices/SubscriptionTests.cs) | ✅ |
| Err-023 | RepublishAfterAcknowledgeReturnsBadAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |
| Err-025 | DeleteMixedValidAndInvalidSubscriptionIdsAsync | [SubscriptionTests](SubscriptionServices/SubscriptionTests.cs) | ✅ |
| Err-025 | DeleteNonExistentSubscriptionReturnsBadSubscriptionIdInvalidAsync | [SubscriptionTests](SubscriptionServices/SubscriptionTests.cs) | ✅ |
| Err-026 | DeleteSameSubscriptionTwiceSecondReturnsBadSubscriptionIdInvalidAsync | [SubscriptionDepthTests](SubscriptionServices/SubscriptionDepthTests.cs) | ✅ |
| Err-028 | DeleteEmptySubscriptionListReturnsErrorAsync | [SubscriptionBasicDepthTests](SubscriptionServices/SubscriptionBasicDepthTests.cs) | ✅ |

</details>

<details>
<summary>Subscription Services / Subscription Durable ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 000 | DurableSubscriptionNotAvailableIgnoredAsync | [SubscriptionDurableTests](SubscriptionServices/SubscriptionDurableTests.cs) | ⏭️ |
| 001 | DurableSubDisabledNoNotificationsAsync | [SubscriptionDurableTests](SubscriptionServices/SubscriptionDurableTests.cs) | ✅ |
| 001 | DurableSubSetPublishingModeDisableAsync | [SubscriptionDurableTests](SubscriptionServices/SubscriptionDurableTests.cs) | ✅ |
| 001 | DurableSubSetPublishingModeReEnableAsync | [SubscriptionDurableTests](SubscriptionServices/SubscriptionDurableTests.cs) | ✅ |
| 001 | DurableSubscriptionCreatedWithPublishingEnabledAsync | [SubscriptionDurableTests](SubscriptionServices/SubscriptionDurableTests.cs) | ✅ |
| 002 | DurableSetLifetimeMaxUint32Async | [SubscriptionDurableTests](SubscriptionServices/SubscriptionDurableTests.cs) | ⏭️ |
| 003 | DurableSetLifetimeZeroRevisedGreaterThanZeroAsync | [SubscriptionDurableTests](SubscriptionServices/SubscriptionDurableTests.cs) | ⏭️ |
| 004 | DurableSubReEnableAfterTransferAsync | [SubscriptionDurableTests](SubscriptionServices/SubscriptionDurableTests.cs) | ⏭️ |
| 004 | DurableSubTransferWithInitialFalseAsync | [SubscriptionDurableTests](SubscriptionServices/SubscriptionDurableTests.cs) | ⏭️ |
| 004 | DurableSubTransferWithInitialTrueAsync | [SubscriptionDurableTests](SubscriptionServices/SubscriptionDurableTests.cs) | ⏭️ |
| 004 | DurableSubscriptionDeleteBeforeReconnectAsync | [SubscriptionDurableTests](SubscriptionServices/SubscriptionDurableTests.cs) | ⏭️ |
| 004 | DurableSubscriptionSurvivesSessionCloseAsync | [SubscriptionDurableTests](SubscriptionServices/SubscriptionDurableTests.cs) | ⏭️ |
| 006 | DurableSubscriptionTransferAfterReconnectAsync | [SubscriptionDurableTests](SubscriptionServices/SubscriptionDurableTests.cs) | ⏭️ |
| 005 | DurableSubWithMultipleMonitoredItemsAsync | [SubscriptionDurableTests](SubscriptionServices/SubscriptionDurableTests.cs) | ✅ |
| 006 | DurableSubPublishingModePreservedAfterTransferAsync | [SubscriptionDurableTests](SubscriptionServices/SubscriptionDurableTests.cs) | ⏭️ |
| 008 | DurableSubSeqNumbersPreservedAsync | [SubscriptionDurableTests](SubscriptionServices/SubscriptionDurableTests.cs) | ⏭️ |
| 009 | DurableSubscriptionWithServerNotifierEventsAsync | [SubscriptionDurableTests](SubscriptionServices/SubscriptionDurableTests.cs) | ⏭️ |
| 010 | DurableSubCreateMultipleSubsAsync | [SubscriptionDurableTests](SubscriptionServices/SubscriptionDurableTests.cs) | ✅ |
| 011 | DurableSubModifyIntervalAsync | [SubscriptionDurableTests](SubscriptionServices/SubscriptionDurableTests.cs) | ✅ |
| 011 | DurableSubModifyKeepAliveCountAsync | [SubscriptionDurableTests](SubscriptionServices/SubscriptionDurableTests.cs) | ✅ |
| 011 | DurableSubModifyLifetimeCountAsync | [SubscriptionDurableTests](SubscriptionServices/SubscriptionDurableTests.cs) | ✅ |
| 011 | DurableSubModifyMaxNotificationsAsync | [SubscriptionDurableTests](SubscriptionServices/SubscriptionDurableTests.cs) | ✅ |
| 011 | DurableSubModifyPriorityAsync | [SubscriptionDurableTests](SubscriptionServices/SubscriptionDurableTests.cs) | ✅ |
| 011 | DurableWithZeroMonitoredItemsThenRepeatCallWithDifferentParamsAsync | [SubscriptionDurableTests](SubscriptionServices/SubscriptionDurableTests.cs) | ⏭️ |
| 012 | DurableShortLivedSubscriptionModifyResetsStateAsync | [SubscriptionDurableTests](SubscriptionServices/SubscriptionDurableTests.cs) | ⏭️ |
| 013 | DurableDeleteSubscriptionRemovesDurableStateAsync | [SubscriptionDurableTests](SubscriptionServices/SubscriptionDurableTests.cs) | ⏭️ |

</details>

<details>
<summary>Subscription Services / Subscription Minimum 02 ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | AllRevisedValuesReturnedInResponseAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 001 | CreateSubscriptionAllBelowMinimumAllRevisedAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 001 | CreateTwoEqualPrioritySubsPublishBothAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 001 | KeepAliveCountRevisedValueIsPositiveAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 001 | LifetimeCountRevisedValueIsPositiveAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 001 | MinimumKeepAliveCountMaxUint32RevisedAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 001 | MinimumKeepAliveCountOneAcceptedOrRevisedAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 001 | MinimumKeepAliveCountZeroRevisedAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 001 | MinimumLifetimeCountExactlyThreeTimesKeepAliveAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 001 | MinimumLifetimeCountLessThanThreeTimesRevisedAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 001 | MinimumLifetimeCountMaxUint32RevisedAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 001 | MinimumLifetimeCountOneRevisedAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 001 | MinimumLifetimeCountTwoRevisedAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 001 | MinimumLifetimeCountZeroRevisedAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 001 | MinimumPublishingIntervalFiftyAcceptedOrRevisedAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 001 | MinimumPublishingIntervalFromServerCapabilitiesAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 001 | MinimumPublishingIntervalNegativeRevisedUpAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 001 | MinimumPublishingIntervalOneMillisecondRevisedAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 001 | MinimumPublishingIntervalTenMillisecondsAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 001 | MinimumPublishingIntervalVerySmallRevisedUpAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 001 | MinimumPublishingIntervalZeroRevisedUpAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 001 | RevisedLifetimeAlwaysThreeTimesKeepAliveAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 001 | RevisedPublishingIntervalGreaterThanZeroAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 001 | RevisedValuesDoNotExceedServerMaximumsAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 002 | CreateTwoSubsWithItemsPublishCallbacksAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 003 | CreateSubsMonitorWritePublishCleanupAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 004 | CreateSubsWritePublishVerifyNotificationsAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 005 | KeepAliveCountRevisionConsistentAcrossCreatesAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 005 | MinimumPublishingIntervalConsistentAcrossCreatesAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 005 | ModifySubRaisePriorityPublishVerifyAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 006 | ModifySubLowerPriorityPublishVerifyAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 006 | RevisedValuesConsistentWithinSameSessionAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 007 | ModifySubRaiseThenLowerPriorityAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 007 | ModifySubscriptionKeepAliveCountZeroRevisedAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 007 | ModifySubscriptionRevisesAllParametersAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 008 | ModifySubSettingsWritePublishAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 008 | ModifySubscriptionKeepAliveCountRevisedAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 009 | SetPublishingModeToggleOnTwoSubsAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 010 | SetPublishingModeDisableOneOfTwoAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 011 | SetPublishingModeEnableDisabledSubAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 012 | SetPublishingModeDisableBothSubsAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 013 | SetPublishingModeReEnableBothSubsAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 014 | SetPublishingModeDisableOneVerifyOtherContinuesAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 015 | SetPublishingModeToggleVerifyStopsReportingAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 016 | DeleteMultipleValidSubscriptionsAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 017 | CreateSubsWithItemsPublishThenDeleteAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 018 | PublishRepublishVerifyRetransmissionAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 019 | CreateSubsLifecycleCleanupAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 020 | CreateSubsWriteVerifyNotificationDeliveryAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 022 | FiveSubsWithPrioritiesHighestDominatesAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 023 | TwoSubsPublishBothReceiveCallbacksAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 024 | FiveSubsDisabledThenEnablePublishAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 025 | FiveSubsDisableEvenNumberedVerifyOddContinueAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 026 | ThreeSubsWithPriorities1And125And255Async | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ⏭️ |
| 027 | FiveSamePrioritySubsRoundRobinFairnessAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |

</details>

<details>
<summary>Subscription Services / Subscription Minimum 05 ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | FiveSubsPriority1And200HighestDominatesAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 002 | DeleteFiveValidSubscriptionsAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 003 | MultiSessionMultiSubPublishCallbacksAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 004 | FiveSubsEnableAfterDisabledReceiveDataAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 005 | FiveSubsDisableSubsetVerifyOthersContinueAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 006 | ThreeSubsPriorities1And125And255OrderingAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |
| 007 | SamePrioritySubsEachServicedOncePerLoopAsync | [SubscriptionMinimumTests](SubscriptionServices/SubscriptionMinimumTests.cs) | ✅ |

</details>

<details>
<summary>Subscription Services / Subscription Multiple ⏭️</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | CreateMaxSubscriptionsPerSessionWithItemsAsync | [SubscriptionMultipleTests](SubscriptionServices/SubscriptionMultipleTests.cs) | ⏭️ |
| 002 | MaxSubscriptionsAcrossMultipleSessionsAsync | [SubscriptionMultipleTests](SubscriptionServices/SubscriptionMultipleTests.cs) | ⏭️ |
| 003 | CreateSubsWithDataItemsWritePublishVerifyAsync | [SubscriptionMultipleTests](SubscriptionServices/SubscriptionMultipleTests.cs) | ⏭️ |

</details>

<details>
<summary>Subscription Services / Subscription Publish Basic ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | PublishBasicTimeoutHintSmallerThanLifetimeCausesBadTimeoutAsync | [SubscriptionPublishTests](SubscriptionServices/SubscriptionPublishTests.cs) | ✅ |
| 001 | PublishNotificationMessageHasValidTimestampAsync | [PublishTests](SubscriptionServices/PublishTests.cs) | ✅ |
| 001 | PublishNotificationSequenceNumberIsPositiveAsync | [PublishTests](SubscriptionServices/PublishTests.cs) | ✅ |
| 001 | PublishReturnsCorrectClientHandleAsync | [PublishTests](SubscriptionServices/PublishTests.cs) | ✅ |
| 001 | PublishReturnsDataChangeNotificationAfterWriteAsync | [PublishTests](SubscriptionServices/PublishTests.cs) | ✅ |
| 001 | PublishWithAcknowledgementOfPreviousSequenceNumberAsync | [PublishTests](SubscriptionServices/PublishTests.cs) | ✅ |
| 002 | MultipleSubscriptionsPublishReturnsNotificationsFromEachAsync | [PublishTests](SubscriptionServices/PublishTests.cs) | ✅ |
| 002 | PublishBasicQueueTwoPublishCallsWithinSessionAsync | [SubscriptionPublishTests](SubscriptionServices/SubscriptionPublishTests.cs) | ✅ |
| 003 | PublishBasicResponseTimingAtPublishingIntervalAsync | [SubscriptionPublishTests](SubscriptionServices/SubscriptionPublishTests.cs) | ✅ |
| 003 | RepublishValidSequenceNumberReturnsNotificationAsync | [PublishTests](SubscriptionServices/PublishTests.cs) | ✅ |
| 004 | PublishBasicRepublishRetrievesQueuedNotificationsAsync | [SubscriptionPublishTests](SubscriptionServices/SubscriptionPublishTests.cs) | ✅ |
| 005 | PublishBasicOutstandingPublishRequestQueueSizeAsync | [SubscriptionPublishTests](SubscriptionServices/SubscriptionPublishTests.cs) | ✅ |
| 005 | PublishReturnsKeepAliveWhenNoChangesAsync | [PublishTests](SubscriptionServices/PublishTests.cs) | ✅ |
| 006 | PublishBasicMinimumRetransmissionQueueSizeAsync | [SubscriptionPublishTests](SubscriptionServices/SubscriptionPublishTests.cs) | ✅ |
| 007 | PublishBasicAsyncPublishQueueBasedOnMaxSubscriptionsAsync | [SubscriptionPublishTests](SubscriptionServices/SubscriptionPublishTests.cs) | ✅ |

</details>

<details>
<summary>Subscription Services / Subscription Publish Min 05 ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | PublishMin05AsyncPublishFiveConcurrentAsync | [SubscriptionPublishTests](SubscriptionServices/SubscriptionPublishTests.cs) | ✅ |
| 003 | PublishMin05MultipleSessionsWithFiveSubscriptionsAsync | [SubscriptionPublishTests](SubscriptionServices/SubscriptionPublishTests.cs) | ✅ |
| 005 | PublishMin05RepublishQueueSizeFiveAsync | [SubscriptionPublishTests](SubscriptionServices/SubscriptionPublishTests.cs) | ✅ |
| 006 | PublishMin05AsyncPublishFiveConcurrentWithDataChangesAsync | [SubscriptionPublishTests](SubscriptionServices/SubscriptionPublishTests.cs) | ✅ |

</details>

<details>
<summary>Subscription Services / Subscription Publish Min 10 ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | PublishMin10CreateTenSubscriptionsWithCallbacksAsync | [SubscriptionPublishTests](SubscriptionServices/SubscriptionPublishTests.cs) | ✅ |
| 002 | PublishMin10AsyncPublishTenConcurrentAsync | [SubscriptionPublishTests](SubscriptionServices/SubscriptionPublishTests.cs) | ✅ |
| 003 | PublishMin10SetPublishingModeDisableFiveOfTenAsync | [SubscriptionPublishTests](SubscriptionServices/SubscriptionPublishTests.cs) | ✅ |

</details>

<details>
<summary>Subscription Services / Subscription PublishRequest Queue Overflow ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | PublishCountExceedsSubscriptionCountAsync | [SubscriptionPublishTooManyTests](SubscriptionServices/SubscriptionPublishTooManyTests.cs) | ✅ |
| 001 | PublishQueueOverflowReturnsGoodOrErrorAsync | [SubscriptionPublishTooManyTests](SubscriptionServices/SubscriptionPublishTooManyTests.cs) | ✅ |
| 001 | QueueOverflowOlderPublishRequestDiscardedAsync | [SubscriptionPublishTests](SubscriptionServices/SubscriptionPublishTests.cs) | ✅ |
| 001 | RapidPublishRequestsAllReturnValidResponsesAsync | [SubscriptionPublishTooManyTests](SubscriptionServices/SubscriptionPublishTooManyTests.cs) | ✅ |
| 001 | TooManyPublishRequestsHandledGracefullyAsync | [SubscriptionPublishTooManyTests](SubscriptionServices/SubscriptionPublishTooManyTests.cs) | ✅ |
| 002 | PublishOverflowDoesNotAffectExistingSubscriptionsAsync | [SubscriptionPublishTooManyTests](SubscriptionServices/SubscriptionPublishTooManyTests.cs) | ✅ |
| 002 | QueueOverflowExceedsSupportedPublishRequestsBadTooManyAsync | [SubscriptionPublishTests](SubscriptionServices/SubscriptionPublishTests.cs) | ⏭️ |

</details>

<details>
<summary>Subscription Services / Subscription Transfer ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | TransferItemCountPreservedAsync | [SubscriptionTransferDepthTests](SubscriptionServices/SubscriptionTransferDepthTests.cs) | ✅ |
| 001 | TransferSubscriptionIdPreservedAsync | [SubscriptionTransferDepthTests](SubscriptionServices/SubscriptionTransferDepthTests.cs) | ✅ |
| 001 | TransferSubscriptionNewSessionCanPublishAsync | [SubscriptionTransferDepthTests](SubscriptionServices/SubscriptionTransferDepthTests.cs) | ✅ |
| 001 | TransferSubscriptionOriginalSessionCannotPublishAsync | [SubscriptionTransferDepthTests](SubscriptionServices/SubscriptionTransferDepthTests.cs) | ✅ |
| 001 | TransferSubscriptionPreservesMonitoredItemsAsync | [SubscriptionTransferDepthTests](SubscriptionServices/SubscriptionTransferDepthTests.cs) | ✅ |
| 001 | TransferSubscriptionToNewSessionSucceedsAsync | [SubscriptionTransferDepthTests](SubscriptionServices/SubscriptionTransferDepthTests.cs) | ✅ |
| 001 | TransferThenDeleteOnNewSessionAsync | [SubscriptionTransferDepthTests](SubscriptionServices/SubscriptionTransferDepthTests.cs) | ✅ |
| 001 | TransferWithDataChangeFilterAsync | [SubscriptionTransferDepthTests](SubscriptionServices/SubscriptionTransferDepthTests.cs) | ✅ |
| 001 | TransferWithMultipleMonitoredItemsAsync | [SubscriptionTransferDepthTests](SubscriptionServices/SubscriptionTransferDepthTests.cs) | ✅ |
| 001 | TransferredSubContinuesPeriodicNotificationsAsync | [SubscriptionTransferDepthTests](SubscriptionServices/SubscriptionTransferDepthTests.cs) | ✅ |
| 001 | TransferredSubKeepAliveOnNewSessionAsync | [SubscriptionTransferDepthTests](SubscriptionServices/SubscriptionTransferDepthTests.cs) | ✅ |
| 001 | TransferredSubSequenceNumberContinuesAsync | [SubscriptionTransferDepthTests](SubscriptionServices/SubscriptionTransferDepthTests.cs) | ✅ |
| 001 | TransferredSubWriteTriggerNotificationAsync | [SubscriptionTransferDepthTests](SubscriptionServices/SubscriptionTransferDepthTests.cs) | ✅ |
| 002 | TransferAfterSessionCloseWithDeleteSubscriptionsTrueAsync | [SubscriptionTransferTests](SubscriptionServices/SubscriptionTransferTests.cs) | ✅ |
| 008 | TransferSubscriptionReturnsAvailableSeqNumsAsync | [SubscriptionTransferDepthTests](SubscriptionServices/SubscriptionTransferDepthTests.cs) | ✅ |
| 008 | TransferWithQueuedNotificationsAsync | [SubscriptionTransferDepthTests](SubscriptionServices/SubscriptionTransferDepthTests.cs) | ✅ |
| 010 | TransferSendInitialRespectsMonitoringModeAsync | [SubscriptionTransferDepthTests](SubscriptionServices/SubscriptionTransferDepthTests.cs) | ✅ |
| 009 | TransferWithDisabledItemAsync | [SubscriptionTransferDepthTests](SubscriptionServices/SubscriptionTransferDepthTests.cs) | ✅ |
| 009 | TransferWithSamplingItemAsync | [SubscriptionTransferDepthTests](SubscriptionServices/SubscriptionTransferDepthTests.cs) | ✅ |
| 009 | TransferWithSendInitialTrueGetsDataAsync | [SubscriptionTransferDepthTests](SubscriptionServices/SubscriptionTransferDepthTests.cs) | ✅ |
| 010 | TransferWithSendInitialFalseNoImmediateDataAsync | [SubscriptionTransferDepthTests](SubscriptionServices/SubscriptionTransferDepthTests.cs) | ✅ |
| 012 | TransferMultipleSubscriptionsAtOnceAsync | [SubscriptionTransferDepthTests](SubscriptionServices/SubscriptionTransferDepthTests.cs) | ✅ |
| 011 | TransferWithSendInitialTrueAllItemsReportAsync | [SubscriptionTransferDepthTests](SubscriptionServices/SubscriptionTransferDepthTests.cs) | ✅ |
| 012 | TransferSendInitialFalseStaticNodeNoDataAsync | [SubscriptionTransferDepthTests](SubscriptionServices/SubscriptionTransferDepthTests.cs) | ✅ |
| 014 | TransferMixedValidInvalidPartialResultsAsync | [SubscriptionTransferDepthTests](SubscriptionServices/SubscriptionTransferDepthTests.cs) | ✅ |
| 014 | TransferMixedValidAndInvalidSubscriptionIdsAsync | [SubscriptionTransferTests](SubscriptionServices/SubscriptionTransferTests.cs) | ✅ |
| 015 | TransferToSameSessionBehaviorAsync | [SubscriptionTransferDepthTests](SubscriptionServices/SubscriptionTransferDepthTests.cs) | ✅ |
| 017 | TransferWithAnonymousUserTokenSucceedsAsync | [SubscriptionTransferTests](SubscriptionServices/SubscriptionTransferTests.cs) | ✅ |
| 018 | TransferReturnsGoodSubscriptionTransferredOnOldSessionAsync | [SubscriptionTransferTests](SubscriptionServices/SubscriptionTransferTests.cs) | ✅ |
| 019 | TransferWithAnonymousUserDifferentSecurityPoliciesAsync | [SubscriptionTransferTests](SubscriptionServices/SubscriptionTransferTests.cs) | ✅ |
| Err-001 | TransferAlreadyTransferredFailsAsync | [SubscriptionTransferDepthTests](SubscriptionServices/SubscriptionTransferDepthTests.cs) | ✅ |
| Err-005 | TransferEmptyListBehaviorAsync | [SubscriptionTransferDepthTests](SubscriptionServices/SubscriptionTransferDepthTests.cs) | ✅ |
| Err-007 | TransferDeletedSubscriptionReturnsBadAsync | [SubscriptionTransferDepthTests](SubscriptionServices/SubscriptionTransferDepthTests.cs) | ✅ |
| Err-007 | TransferNonExistentSubscriptionReturnsBadAsync | [SubscriptionTransferDepthTests](SubscriptionServices/SubscriptionTransferDepthTests.cs) | ✅ |

</details>

### View Services

<details>
<summary>View Services / View Basic 2 ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | Browse001DirectionBothAsync | [BrowseTests](ViewServices/BrowseTests.cs) | ✅ |
| 001 | Browse006ObjectsFolderContainsServerAsync | [BrowseTests](ViewServices/BrowseTests.cs) | ✅ |
| 001 | Browse008RootNodeChildrenAsync | [BrowseTests](ViewServices/BrowseTests.cs) | ✅ |
| 001 | Browse018BrowseTypesFolderAsync | [BrowseTests](ViewServices/BrowseTests.cs) | ✅ |
| 001 | BrowseBothDirectionOnServerReturnsRefsAsync | [ViewDepthTests](ViewServices/ViewDepthTests.cs) | ✅ |
| 001 | BrowseRootFolderAsync | [BrowseTests](ViewServices/BrowseTests.cs) | ✅ |
| 001 | BrowseServerDiagnosticsAsync | [BrowseTests](ViewServices/BrowseTests.cs) | ✅ |
| 001 | BrowseTypesFolderHasChildrenAsync | [ViewDepthTests](ViewServices/ViewDepthTests.cs) | ✅ |
| 001 | BrowseViewsFolderSucceedsAsync | [ViewDepthTests](ViewServices/ViewDepthTests.cs) | ✅ |
| 001 | BrowseWithDefaultViewDescSucceedsAsync | [ViewDepthTests](ViewServices/ViewDepthTests.cs) | ✅ |
| 001 | BrowseWithNullViewSucceedsAsync | [ViewDepthTests](ViewServices/ViewDepthTests.cs) | ✅ |
| 002 | Browse002DirectionForwardAsync | [BrowseTests](ViewServices/BrowseTests.cs) | ✅ |
| 002 | BrowseForwardOnObjectsFolderReturnsChildrenAsync | [ViewDepthTests](ViewServices/ViewDepthTests.cs) | ✅ |
| 003 | Browse003DirectionInverseAsync | [BrowseTests](ViewServices/BrowseTests.cs) | ✅ |
| 003 | BrowseInverseFromObjectsFolderAsync | [BrowseTests](ViewServices/BrowseTests.cs) | ✅ |
| 003 | BrowseInverseOnRootReturnsNoRefsAsync | [ViewDepthTests](ViewServices/ViewDepthTests.cs) | ✅ |
| 004 | Browse004ReferenceTypeFilterAsync | [BrowseTests](ViewServices/BrowseTests.cs) | ✅ |
| 004 | Browse020BrowseHasPropertyReferencesAsync | [BrowseTests](ViewServices/BrowseTests.cs) | ✅ |
| 004 | Browse021BrowseHasComponentReferencesAsync | [BrowseTests](ViewServices/BrowseTests.cs) | ✅ |
| 004 | BrowseNodeWithManyReferencesAsync | [BrowseTests](ViewServices/BrowseTests.cs) | ✅ |
| 005 | Browse014BrowseIncludeSubtypesTrueAsync | [BrowseTests](ViewServices/BrowseTests.cs) | ✅ |
| 005 | BrowseWithMaxRefsPerNodeOneGetsContinuationPointAsync | [BrowseTests](ViewServices/BrowseTests.cs) | ✅ |
| 006 | Browse005NodeClassMaskFilterAsync | [BrowseTests](ViewServices/BrowseTests.cs) | ✅ |
| 006 | Browse022BrowseNodeClassMaskVariableAsync | [BrowseTests](ViewServices/BrowseTests.cs) | ✅ |
| 006 | Browse023BrowseNodeClassMaskMethodAsync | [BrowseTests](ViewServices/BrowseTests.cs) | ✅ |
| 006 | BrowseNextWithValidContinuationPointAsync | [BrowseTests](ViewServices/BrowseTests.cs) | ✅ |
| 006 | BrowseWithNodeClassMaskObjectsOnlyAsync | [BrowseTests](ViewServices/BrowseTests.cs) | ✅ |
| 007 | Browse007ContinuationPointWithBrowseNextAsync | [BrowseTests](ViewServices/BrowseTests.cs) | ✅ |
| 007 | Browse009MultipleNodesAsync | [BrowseTests](ViewServices/BrowseTests.cs) | ✅ |
| 007 | Browse024BrowseNextMultipleContinuationPointsAsync | [BrowseTests](ViewServices/BrowseTests.cs) | ✅ |
| 007 | BrowseMultipleWithMaxRefsOneAsync | [ViewDepthTests](ViewServices/ViewDepthTests.cs) | ✅ |
| 007 | BrowseObjectsFolderAndServerReturnDifferentAsync | [ViewDepthTests](ViewServices/ViewDepthTests.cs) | ✅ |
| 007 | BrowseThreeNodesReturnsThreeResultsAsync | [ViewDepthTests](ViewServices/ViewDepthTests.cs) | ✅ |
| 007 | BrowseTwoNodesSimultaneouslyAsync | [ViewDepthTests](ViewServices/ViewDepthTests.cs) | ✅ |
| 007 | MultipleConcurrentBrowsesWithContinuationPointsAsync | [BrowseTests](ViewServices/BrowseTests.cs) | ✅ |
| 009 | BrowseNextUntilAllReferencesReturnedAsync | [BrowseTests](ViewServices/BrowseTests.cs) | ✅ |
| 009 | BrowseWithMaxRefsZeroReturnsAllAsync | [BrowseTests](ViewServices/BrowseTests.cs) | ✅ |
| 009 | ContinuationPointWithMaxRefs0ReturnsAllAsync | [ViewDepthTests](ViewServices/ViewDepthTests.cs) | ✅ |
| 010 | Browse010BrowseNextReleaseContinuationPointAsync | [BrowseTests](ViewServices/BrowseTests.cs) | ✅ |
| 010 | Browse011BrowseWithResultMaskBrowseNameOnlyAsync | [BrowseTests](ViewServices/BrowseTests.cs) | ✅ |
| 010 | Browse012BrowseWithResultMaskDisplayNameOnlyAsync | [BrowseTests](ViewServices/BrowseTests.cs) | ✅ |
| 010 | Browse013BrowseWithResultMaskNoneAsync | [BrowseTests](ViewServices/BrowseTests.cs) | ✅ |
| 010 | BrowseNextReleaseContinuationPointAsync | [BrowseTests](ViewServices/BrowseTests.cs) | ✅ |
| 010 | BrowseWithResultMaskBrowseNameOnlyAsync | [BrowseTests](ViewServices/BrowseTests.cs) | ✅ |
| 010 | ResultMaskAllReturnsFullAsync | [ViewDepthTests](ViewServices/ViewDepthTests.cs) | ✅ |
| 010 | ResultMaskBrowseNameOnlyAsync | [ViewDepthTests](ViewServices/ViewDepthTests.cs) | ✅ |
| 010 | ResultMaskDisplayNameOnlyAsync | [ViewDepthTests](ViewServices/ViewDepthTests.cs) | ✅ |
| 010 | ResultMaskIsForwardOnlyAsync | [ViewDepthTests](ViewServices/ViewDepthTests.cs) | ✅ |
| 010 | ResultMaskNodeClassOnlyAsync | [ViewDepthTests](ViewServices/ViewDepthTests.cs) | ✅ |
| 010 | ResultMaskNoneReturnsReferencesAsync | [ViewDepthTests](ViewServices/ViewDepthTests.cs) | ✅ |
| 010 | ResultMaskReferenceTypeOnlyAsync | [ViewDepthTests](ViewServices/ViewDepthTests.cs) | ✅ |
| 010 | ResultMaskTypeDefinitionOnlyAsync | [ViewDepthTests](ViewServices/ViewDepthTests.cs) | ✅ |
| 015 | Browse015BrowseIncludeSubtypesFalseAsync | [BrowseTests](ViewServices/BrowseTests.cs) | ✅ |
| 017 | BrowseWithViewAsync | [BrowseTests](ViewServices/BrowseTests.cs) | ✅ |
| 018 | Browse016BrowseServerNodeMandatoryChildrenAsync | [BrowseTests](ViewServices/BrowseTests.cs) | ✅ |
| 018 | Browse017BrowseServerStatusChildrenAsync | [BrowseTests](ViewServices/BrowseTests.cs) | ✅ |
| 018 | BrowseNamespacesArrayExistsAsync | [ViewDepthTests](ViewServices/ViewDepthTests.cs) | ✅ |
| 018 | BrowseServerCapabilitiesExistsAsync | [ViewDepthTests](ViewServices/ViewDepthTests.cs) | ✅ |
| 018 | ServerStatusNodeExistsAsync | [ViewDepthTests](ViewServices/ViewDepthTests.cs) | ✅ |
| 019 | ResultMaskBrowseAndDisplayNameAsync | [ViewDepthTests](ViewServices/ViewDepthTests.cs) | ✅ |
| 027 | BrowseServerDiagnosticsHasSessionArrayAsync | [ViewDepthTests](ViewServices/ViewDepthTests.cs) | ✅ |
| 027 | ServerDiagnosticsSummaryExistsAsync | [ViewDepthTests](ViewServices/ViewDepthTests.cs) | ✅ |
| Err-002 | Browse019BrowseInvalidNodeAsync | [BrowseTests](ViewServices/BrowseTests.cs) | ✅ |
| Err-009 | BrowseNextInvalidContinuationPointAsync | [BrowseTests](ViewServices/BrowseTests.cs) | ✅ |
| Err-014 | BrowseNextWithEmptyCpReturnsErrorAsync | [ViewDepthTests](ViewServices/ViewDepthTests.cs) | ✅ |

</details>

<details>
<summary>View Services / View Minimum Continuation Point 01 ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | BrowseMultipleNodesWithContinuationPointsAsync | [BrowseContinuationPointTests](ViewServices/BrowseContinuationPointTests.cs) | ✅ |
| 001 | BrowseRootWithMaxRefsOneAsync | [BrowseContinuationPointTests](ViewServices/BrowseContinuationPointTests.cs) | ✅ |
| 001 | BrowseServerNodeWithMaxRefsOneGetsContinuationPointAsync | [BrowseContinuationPointTests](ViewServices/BrowseContinuationPointTests.cs) | ✅ |
| 001 | BrowseTypesWithContinuationPointAsync | [BrowseContinuationPointTests](ViewServices/BrowseContinuationPointTests.cs) | ✅ |
| 001 | ContinuationPointWithMaxRefs1Async | [ViewDepthTests](ViewServices/ViewDepthTests.cs) | ✅ |
| 005 | BrowseNextWithReleaseTrueReturnsNoReferencesAsync | [BrowseContinuationPointTests](ViewServices/BrowseContinuationPointTests.cs) | ✅ |
| 005 | ReleaseContinuationPointSucceedsAsync | [ViewDepthTests](ViewServices/ViewDepthTests.cs) | ✅ |
| 007 | BrowseNextUntilDoneCollectsAllReferencesAsync | [BrowseContinuationPointTests](ViewServices/BrowseContinuationPointTests.cs) | ✅ |
| 007 | VerifyAllReferencesAreUniqueAcrossPagesAsync | [BrowseContinuationPointTests](ViewServices/BrowseContinuationPointTests.cs) | ✅ |
| 009 | BrowseNodeWithFewReferencesNoContinuationNeededAsync | [BrowseContinuationPointTests](ViewServices/BrowseContinuationPointTests.cs) | ✅ |
| 009 | BrowseObjectsFolderInverseWithContinuationPointAsync | [BrowseContinuationPointTests](ViewServices/BrowseContinuationPointTests.cs) | ✅ |
| 010 | BrowseWithMaxRefsZeroReturnsAllAsync | [BrowseContinuationPointTests](ViewServices/BrowseContinuationPointTests.cs) | ✅ |
| 013 | BrowseNextMultipleContinuationPointsSimultaneouslyAsync | [BrowseContinuationPointTests](ViewServices/BrowseContinuationPointTests.cs) | ✅ |
| 014 | BrowseAllRefsWithMaxRefs1MatchesUnlimitedAsync | [ViewDepthTests](ViewServices/ViewDepthTests.cs) | ✅ |
| 014 | BrowseAllRefsWithMaxRefs3MatchesUnlimitedAsync | [ViewDepthTests](ViewServices/ViewDepthTests.cs) | ✅ |
| 014 | BrowseNextTotalMatchesBrowseAllAsync | [BrowseContinuationPointTests](ViewServices/BrowseContinuationPointTests.cs) | ✅ |
| 014 | BrowseWithMaxRefsTwoReturnsTwoPerBatchAsync | [BrowseContinuationPointTests](ViewServices/BrowseContinuationPointTests.cs) | ✅ |
| 014 | ContinuationPointWithMaxRefs10Async | [ViewDepthTests](ViewServices/ViewDepthTests.cs) | ✅ |
| 014 | ContinuationPointWithMaxRefs2Async | [ViewDepthTests](ViewServices/ViewDepthTests.cs) | ✅ |
| 014 | ContinuationPointWithMaxRefs5Async | [ViewDepthTests](ViewServices/ViewDepthTests.cs) | ✅ |
| Err-003 | BrowseNextReleaseThenUseCpFailsAsync | [ViewDepthTests](ViewServices/ViewDepthTests.cs) | ✅ |
| Err-003 | BrowseNextReleaseThenUseReturnsErrorAsync | [BrowseContinuationPointTests](ViewServices/BrowseContinuationPointTests.cs) | ✅ |
| Err-003 | BrowseNextWithInvalidCpReturnsErrorAsync | [ViewDepthTests](ViewServices/ViewDepthTests.cs) | ✅ |
| Err-003 | BrowseNextWithMultipleInvalidCpsFailAsync | [ViewDepthTests](ViewServices/ViewDepthTests.cs) | ✅ |
| Err-006 | BrowseNextReleaseAlreadyReleasedCpFailsAsync | [ViewDepthTests](ViewServices/ViewDepthTests.cs) | ⏭️ |
| Err-006 | ReleaseContinuationPointTwiceReturnsErrorAsync | [BrowseContinuationPointTests](ViewServices/BrowseContinuationPointTests.cs) | ✅ |

</details>

<details>
<summary>View Services / View Minimum Continuation Point 05 ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|

</details>

<details>
<summary>View Services / View RegisterNodes ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | ReadUsingRegisteredNodeIdsAsync | [RegisterNodesTests](ViewServices/RegisterNodesTests.cs) | ✅ |
| 001 | RegisterSingleNodeReturnsGoodAsync | [RegisterNodesTests](ViewServices/RegisterNodesTests.cs) | ✅ |
| 001 | WriteUsingRegisteredNodeIdAsync | [RegisterNodesTests](ViewServices/RegisterNodesTests.cs) | ✅ |
| 002 | RegisterAndReadMultipleRegisteredNodesAsync | [RegisterNodesTests](ViewServices/RegisterNodesTests.cs) | ✅ |
| 002 | RegisterMultipleNodesReturnsGoodAsync | [RegisterNodesTests](ViewServices/RegisterNodesTests.cs) | ✅ |
| 006 | RegisterSameNodeTwiceReturnsResultsAsync | [RegisterNodesTests](ViewServices/RegisterNodesTests.cs) | ✅ |
| 011 | UnregisterNodesReturnsGoodAsync | [RegisterNodesTests](ViewServices/RegisterNodesTests.cs) | ✅ |
| Err-005 | RegisterNodesWithInvalidNodeIdStillSucceedsAsync | [RegisterNodesTests](ViewServices/RegisterNodesTests.cs) | ✅ |

</details>

<details>
<summary>View Services / View TranslateBrowsePath ✅</summary>

| Tag | NUnit Test | Fixture | Status |
|----------|-----------|---------|--------|
| 001 | TranslateBrowsePath001SingleElementPathAsync | [TranslateBrowsePathTests](ViewServices/TranslateBrowsePathTests.cs) | ✅ |
| 001 | TranslateBrowsePath004PathToViewsFolderAsync | [TranslateBrowsePathTests](ViewServices/TranslateBrowsePathTests.cs) | ✅ |
| 001 | TranslateBrowsePath007PathFromObjectsToServerAsync | [TranslateBrowsePathTests](ViewServices/TranslateBrowsePathTests.cs) | ✅ |
| 002 | TranslateBrowsePath002MultiElementPathAsync | [TranslateBrowsePathTests](ViewServices/TranslateBrowsePathTests.cs) | ✅ |
| 002 | TranslateBrowsePath003PathToTypesFolderAsync | [TranslateBrowsePathTests](ViewServices/TranslateBrowsePathTests.cs) | ✅ |
| 003 | TranslateBrowsePath006DeepPathAsync | [TranslateBrowsePathTests](ViewServices/TranslateBrowsePathTests.cs) | ✅ |
| 012 | TranslateBrowsePath005MultiplePathsInOneCallAsync | [TranslateBrowsePathTests](ViewServices/TranslateBrowsePathTests.cs) | ✅ |
| Err-001 | TranslateBrowsePathErr001InvalidStartingNodeAsync | [TranslateBrowsePathTests](ViewServices/TranslateBrowsePathTests.cs) | ✅ |
| Err-003 | TranslateBrowsePathErr002EmptyBrowsePathAsync | [TranslateBrowsePathTests](ViewServices/TranslateBrowsePathTests.cs) | ✅ |
| Err-006 | TranslateBrowsePathErr003InvalidTargetNameAsync | [TranslateBrowsePathTests](ViewServices/TranslateBrowsePathTests.cs) | ✅ |

</details>


