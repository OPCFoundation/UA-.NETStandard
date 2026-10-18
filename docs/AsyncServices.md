# Recoverable Service Execution (RSE) prototype

**Status: default-off prototype of OPC 10000-27, draft 0.2.0, dated 2026-10-16,
with provisional namespace-zero identifiers.** The source revision is Core
`8710615ed5765ff780fd54b7f58a6db884d315c8`. RSE is the publication name;
Async Service APIs, ASE capability/conformance identifiers and source paths
remain unchanged. This is not an adopted OPC UA standard, a production
durability guarantee or an independent-SDK interoperability claim.

The prototype separates one original Read, Write or Call from the exchanges
that collect and acknowledge its outcome. A lost response must not cause a
second mutation. [The protocol reference](AsyncServicesProtocol.md) records
the exact messages, identifiers, controls, lifetimes and authorization rules.
The initial proposal issue,
[#4813](https://github.com/OPCFoundation/UA-.NETStandard/issues/4813), is closed;
it is not an active approval record.

## Enable the Server explicitly

There is no extra build switch. Normal Servers do not advertise or execute
the experiment. With an existing configured Server host:

```csharp
services.AddOpcUa()
    .AddServer(options =>
    {
        options.ApplicationName = "RecoverableServicePrototype";
        // Configure the normal endpoints, certificate stores and authorization.
    })
    .WithExperimentalAsyncServices(options =>
    {
        options.MaxExecutionAllowance = 60_000;
        options.MaxResultRetentionTime = 60_000;
    });

services.AddExperimentalAsyncServiceClient();
```

Direct construction is also available:

```csharp
var server = new StandardServer(telemetry)
{
    AsyncServices = new AsyncServiceExecutionOptions
    {
        Enabled = true,
        MaxExecutionAllowance = 60_000,
        MaxResultRetentionTime = 60_000
    }
};
```

The host supplies the usual `ApplicationConfiguration`, finite
`TransportQuotas.MaxMessageSize`, Session limits and existing
[resource-isolation configuration](ServerScalability.md). The provider
reserves work, input/result capacity and history before dispatch. Disabling
isolation or configuring an unbounded message size is not permission to accept
an unbounded recovery obligation: admission fails with BadResourceUnavailable.
There is no second Async quota hierarchy or result-size policy Node.

Startup installs the embedded 67-node
`Opc.Ua.AsyncServices.NodeSet2.xml` overlay only when enabled. Root Objects use
the proposal's fixed IDs; their children receive application-namespace IDs.
Browse the typed model or namespace-qualified paths; never calculate child IDs
from type-member declaration IDs. Do not install the publication-only
`Opc.Ua.AsyncServices.Metadata.NodeSet2.xml` package.

`IAsyncServiceAuthorizationPolicy` adds current action-specific application
authorization to the mandatory Session, creator and node permission checks.
Register it in DI, or pass it to `DefaultAsyncServiceExecutionProviderFactory`
and assign `StandardServer.AsyncServiceExecutionProviderFactory`.
`IAsyncServiceExecutionProviderFactory` is the replacement-provider seam.
Each Server owns its provider and its shutdown; operation state is not shared
between Servers.

The default provider uses StandardServer's shared, validated Read/Write/Call
paths and ordinary node-manager callbacks. A derived Server's replacement
public Service override is not implicitly invoked as an owned continuation.
Such custom execution must be integrated through node managers or a provider
that preserves the same ownership and recovery contract. Ordinary Service
dispatch is unchanged.

Session/security lifecycle, subscription/monitored-item/Publish machinery,
ordinary Cancel, recursive wrapper Services and other unadvertised families
cannot be enclosed. A current False `SupportsRecoverableExecution` declaration
on an otherwise callable Method rejects the entire recoverable Call batch
before any item runs. Missing/True declarations still undergo normal admission
checks. Ordinary invalid or unauthorized items remain per-item Call failures;
wrapping is not an all-items-authorized transaction.

## Create a handle before submission

Use a stack `ManagedSession`. A classic/custom `ISession` cannot provide the
managed lifecycle proof and is rejected. The handle captures an explicitly
[recoverable same-Session context](Sessions.md#opt-in-same-session-recovery).
The retargeting `RedundantClientSession` facade also rejects raw Submit/Get/Control
calls: select the original ManagedSession explicitly rather than transferring
operation ownership to a new leader Session.

```csharp
var client = new AsyncServiceClient();

await using AsyncServiceOperation<ReadResponse> operation =
    await client.CreateReadAsync(managedSession, new ReadRequest
    {
        TimestampsToReturn = TimestampsToReturn.Neither,
        NodesToRead =
        [
            new ReadValueId
            {
                NodeId = VariableIds.Server_ServerStatus_State,
                AttributeId = Attributes.Value
            }
        ]
    }, ct);

Uuid clientId = operation.ClientOperationId; // Exists before submission.
AsyncServiceResult<ReadResponse> accepted = await operation.ExecuteAsync(
    executionAllowance: 10_000,
    resultRetentionTime: 30_000,
    initialWaitTime: 0,
    collectionWaitTime: 1000,
    ct: ct);

ReadResponse original = accepted.GetValueOrThrow();
StatusCode itemStatus = original.Results[0].StatusCode;
```

`CreateWriteAsync` and `CreateCallAsync` retain one complete original batch.
They do not split it into independently retained item operations. All durations
are milliseconds; the requested positive finite E/R are accepted exactly or
rejected, never silently revised.

`ExecuteAsync` submits once and performs bounded collection exchanges. On an
uncertain transport failure it requests shared managed recovery, then collects
by the already-created Client ID when the Server token is not yet known.
It never retries SubmitService. An Unknown lookup produces an honest local
BadNoData availability error, not proof of non-execution.

For application-controlled scheduling use `SubmitAsync`, `CollectAsync` and
`WaitForResultAsync` separately. `Completion` does not start a background
polling loop. Zero Submit/Get wait is a poll; cancelling a wait does not cancel
admitted work. Calling SubmitAsync a second time on the same handle is rejected,
including after an uncertain first exchange.

Each exchange is sent once on a physical transport. Fresh collection/control
can follow verified reactivation of the original Session, not merely channel
readiness. A replacement incarnation, changed creator/Client certificate,
policy/mode, transport mapping or unsafe encoding/namespace mapping invalidates
the binding. Existing strict contexts still invalidate as soon as recovery
begins.

## Use generated Method proxies

Construct the generated proxy with the callback's `ISessionClient`, not with
the outer managed Session:

```csharp
await using AsyncServiceOperation<ByteString> csr =
    await client.CreateMethodAsync(managedSession,
        (session, token) => new ServerConfigurationTypeClient(
            session, ObjectIds.ServerConfiguration, telemetry)
            .CreateSigningRequestAsync(
                ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup,
                ObjectTypeIds.RsaSha256ApplicationCertificateType,
                subjectName,
                true,
                entropy,
                token),
        ct);

ByteString request = (await csr.ExecuteAsync(60_000, 60_000, ct: ct))
    .GetValueOrThrow();
```

The callback must issue and await exactly one eligible original request.
Read-only proxy preparation is allowed before capture; another request after
capture is rejected. The normal proxy validates typed output before acceptance.
Its ordinary MethodId compatibility fallback is disabled for this one-request
capture, so a failure cannot quietly cause a second original Call. Ordinary
proxy usage retains its existing fallback.

The overload for a generated Method with no outputs returns
`AsyncServiceOperation<StatusCode>`. It is used for ApplyChanges in the
certificate demonstration; ApplyChanges is never a result-polling operation.

## Acceptance and acknowledged release

Default acceptance requires decoding, response/type validation and successful
acceptance into the handle's caller-facing `Completion`. Only then is ACK
attempted. An original ServiceFault or Method failure can be an accepted
outcome: receipt does not turn it into success. `GetValueOrThrow` preserves that
failure, while `OriginalResponse` retains its complete original header and
diagnostics.

Select policies **before** submission:

```csharp
operation.RequireExplicitAcknowledgement();
```

This still validates and exposes the outcome through Completion but sends no
automatic ACK. After application acceptance:

```csharp
ControlServiceExecutionResponse receipt = await operation.AcknowledgeAsync(ct);
try
{
    ServiceOperationControlResult disposition = receipt.Result;
    // Applied and AlreadyApplied confirm acknowledged release.
}
finally
{
    receipt.Reuse();
}
```

Alternatively attach a local storage/acceptance callback:

```csharp
operation.WithAcceptancePolicy(async (outcome, token) =>
{
    await acceptanceStore.SaveAsync(operation.ClientOperationId, outcome, token);
});
```

`acceptanceStore` is application-owned: there is no new wire storage policy.
A failed/cancelled callback leaves Completion unaccepted and sends no ACK;
collection can retry local acceptance while the Server still retains the
outcome. Closing the local completion context during acceptance also prevents
ACK. This is not persistence of the Server operation across process restart.

An ACK delivery/reply failure does not replace the accepted result.
Inspect `AcknowledgementFailure` and `LastAcknowledgement`; call
`AcknowledgeAsync` to retry **only ACK**. If history has ended, UnknownOperation
cannot prove that the earlier ACK was or was not applied.

`CancelAsync` requests stopping work where possible, without rollback.
`AbandonAsync` gives up recovery without requesting work cancellation.
Both require the recovered Server token. Dispose releases only local handle
resources; it does not send either control.

## Security and certificate rotation

References are Session-scoped. Foreign-Session IDs/tokens look unknown after
normal authorization. A known operation in the requesting Session still
returns BadUserAccessDenied for a changed creator or revoked permission.
Permissions are rechecked after waits and before disclosure.
Retained Value reads also recheck the node's effective current read access,
including `UserAccessLevel` callbacks. Revocation denies collection without
running the Value callback again, acknowledging or changing the stored outcome.

HTTPS UA JSON uses its real TLS transport protection without relabelling UA
MessageSecurityMode or policy. The native listener sets
`SecureChannelContext.HasTransportSecurity` from an established TLS handshake.
The .NET Framework binding uses Kestrel 2.x's TLS connection-adapter feature;
newer targets use the negotiated handshake feature.
Custom transports must supply truthful server-side evidence. A profile URI,
URL scheme or caller header alone is not evidence. The shared
`ProvidesEncryption`/`ProvidesIntegrity` checks are used by access restrictions
and Push Management; SecurityAdmin/application authorization remains separate.

The replacement-channel same-certificate guard pins the **Client** certificate.
A rotated peer **Server** certificate can be accepted through normal
CertificateManager trust validation against the intended Server identity plus
successful ActivateSession of the original Session. HTTPS transport-certificate
validation and OPC UA application authentication remain separate mapping
requirements. In the known ApplyChanges workflow the demonstration refreshes
endpoint discovery before reconnecting:

```csharp
await managedSession.ConfiguredEndpoint.UpdateFromServerAsync(
    clientConfiguration, telemetry, ct);
await managedSession.ReconnectAsync(null, null, ct);
```

The channel manager replaces a native transport that still holds the earlier
peer certificate. Existing recovery handles must still pass all Session,
identity, mapping and encoding guards. An endpoint refresh is not authorization
to downgrade a mapping or recreate an operation.
Before a recoverable request is sent, the binding checks the actual peer
certificate against its captured Server identity, rather than trusting a copied
ApplicationUri in refreshed discovery. UA-TCP ApplicationCertificates must
identify that application; HTTPS TLS certificates must identify the captured
transport host and need not contain the OPC UA application URI. Existing domain
validation requirements still apply. A mismatched or missing replacement
certificate invalidates the binding before the original request or collection
payload is dispatched.

An explicit change confined to current transport `MaxMessageSize` is delivery
capacity, not a new Session or a new encoding interpretation. The manager opens
a fresh transport with that capacity. A too-small response fails without ACK,
outcome replacement, submission replay or handle invalidation; restore a
suitable capacity and collect while the original R remains. The binding refuses
to use a transport above a captured finite safety ceiling, including a later
zero/unlimited setting, without permanently invalidating the handle. Other
encoder/decoder limits and factory/profile/namespace checks remain unchanged.

## Run the local two-hop demonstration

From the repository root with the pinned .NET SDK:

```powershell
dotnet test tests\Opc.Ua.Server.Tests\Opc.Ua.Server.Tests.csproj `
    -f net10.0 -p:CustomTestTarget=net10.0 `
    --filter FullyQualifiedName~CertificateCycleAcrossRecoverableGateway
```

The integration fixture hosts a local GDS-role Client, gateway and device with
separate stable management Sessions. Both hops use the typed recoverable
Client. It generates a real CSR and pending key through Push Management,
issues a certificate with the local test CA, stages UpdateCertificate, invokes
ApplyChanges once and verifies the installed private key by signing a challenge.
It then checks communication and the peer certificate after recovery.

The four cases cover both same-mapping and mixed-mapping combinations of
UA-TCP/Binary and HTTPS UA JSON. Initial CSR replies are physically lost on
both hops. The assertions require one device key generation, no gateway-local
replacement key, original Sessions and a pre-rotation handle that remains
collectible after the peer certificate changes. CertificateManager, trust-list
transactions, pending-key/secret stores and the normal Push transaction owner
are reused. PKI material and credentials are synthetic and created at run time;
isolated stores, listeners and Sessions are cleaned up.

`DrainPendingApplyChangesAsync` waits for actual deferred server-side apply
effects in the fixture. It does not invoke ApplyChanges again or substitute
ApplyChanges for GetServiceResult.

This demonstration uses a device Server that preserves its Sessions while
affected channels renegotiate after the ApplyChanges response. Part 12 also
permits a Server to shut down when applying changes, and a trust change may
prevent reconnection. Neither case extends this prototype's same-Session
guarantee: report lost recovery rather than recreating the operation. The
device's CSR, pending key and transaction do not become the gateway's own
application-certificate transaction.

## Evidence boundaries

| Selection | What it exercises |
| --- | --- |
| `AsyncServiceContractTests` | Generated Binary/JSON codecs, ordered fields and IDs; not a network test |
| `RecoverableSessionRequestContextTests`, `AsyncServiceClientTests` | Real managed lifecycle with scripted transport: activation proofs, no replay, payload validation and acceptance/ACK gates |
| `AsyncServiceExecutionTests` | Generated in-process endpoint, real handlers/isolation, injected elapsed time, exact encoder boundaries and release/accounting races |
| `AsyncServiceTransportTests` | Native free-port UA-TCP/Binary and TLS HTTPS UA JSON, observed Server encoding/protection, Read/Write/Call and physical reply loss |
| `CertificateCycleAcrossRecoverableGateway` | Actual four-combination two-hop CSR/CA/update/apply/verification cycle |
| `AsyncServicesAotTests` | Codec, DI, optional model and live typed Read paths; NativeAOT evidence requires publishing and executing the native binary, not merely a JIT test run |

Lost Submit/result/ACK replies are separate native test cases. Exact encoded
thresholds and reduced Session ceilings are in-process evidence. The separate
`NativeDeliveryCapacityCanShrinkAndRecoverWithinTheOriginalSession` test
reduces and restores actual native capacity with one original handle and
invocation: UACP HEL capacity for UA-TCP, and the bounded HTTP response-body
reader for HTTPS UA JSON. It does not pretend that HTTPS negotiates UACP HEL.
The prototype does not claim WebSocket/OpenAPI mapping support, independent
SDK interoperability, persistent operations, failover recovery or restart
durability.

The canonical generator inputs and synchronization command are documented in
[`tools\AsyncServices`](../tools/AsyncServices/README.md). Changes to the
contract belong there, not in generated C#. The model and protocol reference
must remain reconciled with the frozen proposal before publication.

### Evidence provenance

The original acceptance, NativeAOT and focused coverage records describe
prototype commit `66c3b8415d7d05291d7937a6d704e561ba80fab9` against Core
`70bf3762a8223a7382dc97a38358a6d053cabd8e`. The subsequent merge checks describe
`ecb1efe7ec5f20465fd0feebde420cda4560c7c4`. Adopting Core `8710615` does not
relabel those runs or establish new full-solution, NativeAOT or coverage results.
Its description and model-title updates change full JSON content and raw
NodeSet bytes, while leaving the schema/model semantics and NodeIds unchanged.
See the [source revision and fingerprints](../tools/AsyncServices/README.md#source-revision)
for the current snapshot.

## Scenario-to-test mapping

The ASE2 identifiers from the original acceptance baseline remain unchanged.
The mapping also covers the more explicit Method-eligibility, actual-mapping,
typed-acceptance, capacity-recovery and certificate-renewal stimuli in Core
`8710615`. It identifies executable checks, not new test-run results,
certification or a promise that every platform's full solution suite is green.
Record the exact command, TFM, source commit and outcome when reporting a run;
do not sum overlapping selections or replace the provenance above.

Class prefixes below are `E` = `AsyncServiceExecutionTests` (in-process
endpoint), `N` = `AsyncServiceTransportTests` (native transport), `C` =
`AsyncServiceClientTests` (scripted Client), `B` =
`RecoverableSessionRequestContextTests` (scripted managed lifecycle), and `X` =
`AsyncServiceContractTests` (codec/model).

| Scenario | Requirement | Test methods |
| --- | --- | --- |
| ASE2-001 | Default-off behaviour | E.`DisabledFeatureDoesNotAdvertiseOrExecute`; X.`StandardAddressSpaceExcludesExperimentalDefinitions` |
| ASE2-002 | Activated Session required | E.`WrapperWithoutSessionCannotExecuteOrDisclose` |
| ASE2-003 | Whole-request eligibility, including current False and absent/True Method hints | E.`ExcludedOriginalServiceRejectsBeforeDispatch`; N.`CurrentMethodEligibilityIsCheckedBeforeAnyBatchEffect`; N.`MethodEligibilityDoesNotReplaceOrdinaryPerItemPermissionFailures` |
| ASE2-004 | Exact supported E/R | E.`UnsupportedPositiveDurationRejectsBeforeDispatch` |
| ASE2-005 | Invalid durations | E.`InvalidDurationsRejectBeforeDispatch`; E.`InvalidAdmissionEnvelopeRejectsBeforeDispatch` |
| ASE2-006 | Exchange handle is not operation identity | E.`ReusedExchangeHandleDoesNotAliasRetainedOperations`; E.`ExchangeCancellationDoesNotCancelAdmittedWork` |
| ASE2-007 | Remembered duplicate IDs | E.`DuplicateClientIdRejectsEqualAndChangedInputs` |
| ASE2-008 | Lost submission reply | N.`LostReplyRecoversWithoutRepeatingTheMethod` (`submit`); C.`LostSubmissionReplyCollectsByClientIdWithoutResubmission` |
| ASE2-009 | Lookup overtakes admission | E.`LookupCanOvertakeAdmissionWithoutDecidingExecution` |
| ASE2-010 | Fast retained result | E.`FastReadRetainsOriginalOutcomeUntilAcknowledged` |
| ASE2-011 | Bounded and zero waiting | N.`BoundedWaitsDoNotCancelOrRestartOwnedWork`; E.`CurrentCollectionAuthorizationIsRecheckedAfterWait` |
| ASE2-012 | Read/Write/Call typing with observed Server encoding/protection | N.`ReadWriteAndDelayedCallRoundTripOverNativeTransport` |
| ASE2-013 | Original failure and diagnostics | E.`OriginalServiceFaultRetainsItsHeaderAndDiagnostics`; C.`OriginalServiceFaultOrDeadlineIsAcceptedAndAcknowledged`; X.`CollectionPreservesCompleteOriginalServiceFault` |
| ASE2-014 | Immutable result, fresh outer header | N.`LostReplyRecoversWithoutRepeatingTheMethod` (`result`); E.`InFlightCollectorsSurviveLogicalRelease` |
| ASE2-015 | Reject malformed envelope or typed Method output; no hidden original retry | C.`MalformedEnvelopeIsNotAcknowledged`; C.`GeneratedProxyValidatesTypedOutputBeforeAcknowledgement`; C.`GeneratedProxyDoesNotRetryAnOriginalMethodId` |
| ASE2-016 | Complete batch, not a transaction | N.`MixedCallBatchRetainsAllOriginalItemResults`; N.`MethodEligibilityDoesNotReplaceOrdinaryPerItemPermissionFailures` |
| ASE2-017 | Deadline wins over late completion | E.`LateCompletionCannotReplaceAvailableDeadlineOutcome` |
| ASE2-018 | Active identity/charges beyond public history | E.`DeadlineAndHistoryDoNotReleaseStillRunningWork` |
| ASE2-019 | R starts at readiness | E.`LateReadyOutcomeReceivesFullRetentionInterval` |
| ASE2-020 | Caller-context acceptance before ACK | C.`HandlePrecedesSubmissionAndAcceptedReadIsAcknowledged`; C.`DelayedAcceptanceIsNotAcknowledgedAfterDecoding`; C.`RejectedCallerCompletionContextDoesNotAcknowledge` |
| ASE2-021 | Lost ACK, ACK-only retry | N.`LostReplyRecoversWithoutRepeatingTheMethod` (`ack`); C.`LostAcknowledgementDoesNotReplaceAcceptedOutcome` |
| ASE2-022 | Manual/storage acceptance | C.`ManualAcceptanceDoesNotSendAutomaticAcknowledgement`; C.`FailedAcceptanceCanBeRetriedWithoutAcknowledgingOrRepeatingWork`; C.`CancelledAcceptanceCanBeRetriedWithoutAcknowledgement` |
| ASE2-023 | Receipt of failed outcomes | C.`OriginalServiceFaultOrDeadlineIsAcceptedAndAcknowledged`; E.`DeadlineAndHistoryDoNotReleaseStillRunningWork` |
| ASE2-024 | Abandon is not cancellation | E.`AbandonAndCancelHaveIndependentEffects` |
| ASE2-025 | Cancel applicability | E.`AbandonAndCancelHaveIndependentEffects` |
| ASE2-026 | Ordinary Cancel across admission | E.`ExchangeCancellationDoesNotCancelAdmittedWork`; E.`LookupCanOvertakeAdmissionWithoutDecidingExecution` |
| ASE2-027 | Release/send races | E.`CompetingReleaseActionsKeepOneWinningReason`; E.`InFlightCollectorsSurviveLogicalRelease` |
| ASE2-028 | Bounded concurrent collectors | E.`PendingCollectorsAreBoundedByExistingRequestQuota`; E.`InFlightCollectorsSurviveLogicalRelease` |
| ASE2-029 | Non-renewing terminal history | E.`PollingAndRepeatedAcknowledgementDoNotRenewWindows` |
| ASE2-030 | Current creator and permission | E.`CurrentCollectionAuthorizationIsRecheckedAfterWait`; E.`SameSessionWrongCreatorIsDeniedAndOriginalCreatorCanStillCollect`; E.`ControlAuthorizationIsActionSpecific` |
| ASE2-031 | Foreign Session is unknown | E.`ForeignSessionTokenHasTheSameUnknownLookupAndControlDisposition` |
| ASE2-032 | Verified same-Session recovery | N.`LostReplyRecoversWithoutRepeatingTheMethod`; B.`FreshRequestFollowsVerifiedSameSessionReactivation` |
| ASE2-033 | No replacement-Session adoption | N.`ReplacementNativeSessionDoesNotAdoptAnOperation`; B.`ReplacementSessionIsNeverAdopted` |
| ASE2-034 | Anonymous + Sign rejected | E.`AnonymousSignAdmissionIsRejectedBeforeDispatch` |
| ASE2-035 | Admission pressure and cleanup | E.`FullOperationQuotaStillPermitsRecoveryAndCleanup`; E.`ProviderShutdownWaitsForOwnedInvocationCleanup` |
| ASE2-036 | Real encoded size boundaries | E.`EncodedEnvelopeBoundaryIncludesWrapperOverhead`; N.`OversizedNativeReadRetainsFailureWithoutExecutingAgain` |
| ASE2-037 | Native capacity reduction/restoration with the same handle, Session and original R | N.`NativeDeliveryCapacityCanShrinkAndRecoverWithinTheOriginalSession`; E.`SmallerDeliveryLimitDoesNotRewriteRetainedOutcome` |
| ASE2-038 | Elapsed lifetimes and safe advice | E.`WallClockChangesDoNotRenewOrExpireElapsedLifetimes`; C.`MalformedEnvelopeIsNotAcknowledged` (`retry-after-retention`) |
| ASE2-039 | Privileged metadata diagnostics/audit | E.`OperationAuditingUsesExistingGateAndMetadataOnly`; E.`EnabledModelUsesApplicationIdsAndPrivilegedDiagnostics` |
| ASE2-040 | Two-hop certificate cycle, validated renewal and pre-rotation operation recovery | N.`CertificateCycleAcrossRecoverableGateway` (all four mapping combinations) |

Run the Server mapping, Client mapping and independent codec checks separately:

```powershell
dotnet test tests\Opc.Ua.Server.Tests\Opc.Ua.Server.Tests.csproj `
    -f net10.0 -p:CustomTestTarget=net10.0 `
    --filter FullyQualifiedName~AsyncServices

dotnet test tests\Opc.Ua.Client.Tests\Opc.Ua.Client.Tests.csproj `
    -f net10.0 -p:CustomTestTarget=net10.0 `
    --filter "FullyQualifiedName~AsyncServiceClientTests|FullyQualifiedName~RecoverableSessionRequestContextTests"

dotnet test tests\Opc.Ua.Core.Encoders.Tests\Opc.Ua.Core.Encoders.Tests.csproj `
    -f net10.0 -p:CustomTestTarget=net10.0 `
    --filter FullyQualifiedName~AsyncServiceContractTests
```

These selections supplement, rather than replace, the generator, encoder,
Core, Client, Server, Sessions, GDS, analyzer, coverage and platform regressions
described in the [developer guide](DeveloperGuide.md).
