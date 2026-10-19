# Recoverable Service Execution (RSE) and Async Services

**Status: default-off prototype of OPC 10000-27, draft 0.2.0, dated 2026-10-16,
with provisional namespace-zero identifiers.** The source revision is Core
`8710615ed5765ff780fd54b7f58a6db884d315c8`. RSE is the publication name;
Async Service APIs, ASE capability/conformance identifiers and source paths
remain unchanged. This is not an adopted OPC UA standard, a production
durability guarantee or an independent-SDK interoperability claim.

The prototype separates one original Read, Write or Call from the exchanges
that collect and acknowledge its outcome. A lost response must not cause a
second mutation. [The protocol reference](#protocol-reference) records
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

## Use ordinary ISession calls

Select prefer-recoverable execution when creating a managed session. It is off
by default and does not add required members to `ISession`:

```csharp
ISession session = await new ManagedSessionBuilder(configuration, telemetry)
    .UseEndpoint(endpoint)
    .PreferRecoverableExecution()
    .ConnectAsync(ct);

int value = await session.ReadValueAsync<int>(nodeId, ct);
```

The existing asynchronous Read, Write and Call methods, convenience reads and
generated Method proxies keep their signatures and result types. The Client
performs capability discovery and the submission/collection exchanges
internally. A regular session uses its existing ordinary behavior.

Direct construction selects the same implementation:

```csharp
ISession session = await ManagedSession.CreateAsync(
    new ManagedSessionOptions
    {
        Endpoint = endpoint,
        RecoverableExecution = new RecoverableSessionOptions()
    },
    configuration,
    new DefaultSessionFactory(telemetry),
    telemetry: telemetry,
    ct: ct);
```

For DI, configure the session options before obtaining a session from
`IManagedSessionFactory`:

```csharp
services.AddOpcUa().AddClient(options =>
{
    options.Session = options.Session with
    {
        RecoverableExecution = new RecoverableSessionOptions()
    };
});

ISession session = await serviceProvider.GetRequiredService<IManagedSessionFactory>()
    .ConnectAsync(endpoint, ct);
```

The Client uses the Server's advertised execution and retention defaults.
Optional `ExecutionAllowance` and `ResultRetentionTime` settings select exact
positive finite milliseconds. They are independent of the original
`RequestHeader.TimeoutHint` and caller cancellation. Overrides are not clamped,
and missing or invalid required defaults are errors.

Ordinary fallback is allowed only when unsupported recovery and non-admission
are established before original execution. An absent capability root or a
decoded outer Submit rejection can establish this. Resource, lifetime,
validation, authorization and security failures do not trigger fallback.
Neither a timeout, lost reply, Unknown lookup, collection/control failure nor
an embedded original failure proves non-admission. The original request is
never replayed to resolve uncertainty.

Session maintenance and capability discovery stay on the internal ordinary
path; they do not create application receipt obligations or invoke application
acceptance callbacks. Other Service families and obsolete sync/APM entry points
remain ordinary. The mode is not enabled on arbitrary/custom sessions or
silently applied to an existing connection.

Generated Method proxies must use the current generator's typed completion
integration. Older generated proxies still work on ordinary sessions, but
cannot silently claim typed recoverable acceptance: incompatible proxy calls
on an opted-in session fail before dispatch and require regeneration.

## Coordinate acknowledgement with an external system

An optional operation-local scope defers ACK while leaving the operation call
and result unchanged:

```csharp
await using var acknowledgement = session.BeginAcknowledgementScope(
    requireRecoverableReceipt: true);

ReadResponse response = await session.ReadAsync(
    null, 0, TimestampsToReturn.Both, nodesToRead, ct);

await resultStore.SaveAsync(response, ct);
// Or wait for the selected MQTT/RPC handoff to complete.
await acknowledgement.AcknowledgeAsync(ct);
```

The scope is local to the current asynchronous flow and belongs to one logical
caller operation. Independent concurrent operations use separate scopes;
accidental reuse fails rather than mixing receipts. Nest scopes in disposal
order. There is no session-wide "last receipt".

`Receipt` is an opaque receipt bound to the original Session. It is null when
no recoverable request was admitted, including ordinary fallback. Calling
`AcknowledgeAsync` without a receipt fails; it is not a successful no-op.
`requireRecoverableReceipt: true` rejects unsupported recovery or an excluded
application Service before ordinary dispatch. A composed convenience operation
can involve several original requests; its receipt does not make them an
atomic transaction.

An asynchronous acceptance callback may be configured in
`RecoverableSessionOptions.AcceptAsync` or on the scope:

```csharp
await using var acknowledgement = session.BeginAcknowledgementScope(
    requireRecoverableReceipt: true,
    acceptAsync: async (acceptance, token) =>
    {
        if (!acceptance.TryGetResult<ReadResponse>(out var response))
        {
            throw new InvalidOperationException("Expected the ordinary Read response.");
        }
        await resultStore.SaveAsync(response, token);
    });

ReadResponse response = await session.ReadAsync(
    null, 0, TimestampsToReturn.Both, nodesToRead, ct);
await acknowledgement.AcknowledgeAsync(ct);
```

Callbacks receive the final SDK result after typed decoding. A legitimate
failed original outcome is exposed through `Failure` instead of a fabricated
typed result. Without an explicit scope, successful callback and caller
completion are followed by automatic ACK. Use an explicit scope when receipt
ownership and ACK retry must outlive the ordinary call.

Callback failure/cancellation and scope disposal never acknowledge, abandon,
resubmit or extend the remote operation. Disposal stops local waiting and
releases local resources only. A callback cannot reenter the same session or
acknowledge before caller acceptance. An ACK failure leaves the accepted
original result intact; retry only the receipt's ACK.

The original E/R and same-Session restrictions still apply while an external
system processes the result. This does not provide restart durability,
cross-system transactions or an MQTT delivery guarantee.

<a id="create-a-handle-before-submission"></a>

## Advanced: create a handle before submission

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

<a id="use-generated-method-proxies"></a>

## Advanced: capture generated Method proxies

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

<a id="acceptance-and-acknowledged-release"></a>

## Advanced handle acceptance and acknowledged release

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

## Run the long-running operation sample

The [RecoverableServiceDemo console sample](../samples/AsyncServices/README.md)
hosts a local Server and Client and invokes a five-second Method through the
ordinary generated proxy. With `--network-outage`, it closes the actual TCP
connections and rejects reconnects while the Server finishes its work. After
connectivity returns, the original call receives its retained result without
replaying the Method. It verifies one invocation, one increment, the original
Session and explicit acknowledgement after a sample acceptance-file write.

```powershell
dotnet run --project samples\AsyncServices\RecoverableServiceDemo -- --network-outage
```

This is a temporary connection outage with the Server process and Session still
alive, not a restart or failover demonstration. The sample README includes the
no-fault run, expected output, safety settings and the server-restart boundary.

## Run the local two-hop demonstration

The certificate demonstration lives in
[`CertificateCycleAcrossRecoverableGateway`](../tests/Opc.Ua.Server.Tests/AsyncServices/AsyncServiceTransportTests.cs),
with the downstream adapter in
[`AsyncCertificateForwarder.cs`](../tests/Opc.Ua.Server.Tests/AsyncServices/AsyncCertificateForwarder.cs).
It is an executable integration fixture rather than a separate interactive
certificate-management application.

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
| `RecoverableManagedSessionTests` | Unchanged caller interfaces, options/builder/DI, unsupported-only fallback, exact budgets, typed acceptance, concurrent receipt scopes and local failure semantics |
| `RecoverableSessionRequestContextTests`, `AsyncServiceClientTests` | Real managed lifecycle with scripted transport: activation proofs, no replay, payload validation and acceptance/ACK gates |
| `AsyncServiceExecutionTests` | Generated in-process endpoint, real handlers/isolation, injected elapsed time, exact encoder boundaries and release/accounting races |
| `AsyncServiceTransportTests` | Native free-port UA-TCP/Binary and TLS HTTPS UA JSON, observed Server encoding/protection, Read/Write/Call and physical reply loss |
| `CertificateCycleAcrossRecoverableGateway` | Actual four-combination two-hop CSR/CA/update/apply/verification cycle |
| `RecoverableServiceDemoTests` | Runnable sample: five-second typed Method, real loopback TCP outage, no replay and explicit acknowledgement after local file acceptance |
| `AsyncServicesAotTests` | Codec, DI, optional model, live explicit-handle and transparent `ISession` Read/receipt paths; NativeAOT evidence requires publishing and executing the native binary, not merely a JIT test run |

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
    --filter "FullyQualifiedName~RecoverableManagedSessionTests|FullyQualifiedName~AsyncServiceClientTests|FullyQualifiedName~RecoverableSessionRequestContextTests"

dotnet test tests\Opc.Ua.Core.Encoders.Tests\Opc.Ua.Core.Encoders.Tests.csproj `
    -f net10.0 -p:CustomTestTarget=net10.0 `
    --filter FullyQualifiedName~AsyncServiceContractTests
```

These selections supplement, rather than replace, the generator, encoder,
Core, Client, Server, Sessions, GDS, analyzer, coverage and platform regressions
described in the [developer guide](DeveloperGuide.md).

## Protocol reference

The canonical JSON contract defines the message layout and provisional
allocations for the OPC 10000-27 revision identified above. This section
explains that contract; it does not define another set of DTOs or identifiers.
Neither the closed proposal issue nor this implementation establishes
maintainer approval, certification, or adoption into the OPC UA standard.

### Why there are three Services

An ordinary request can finish after its Client has stopped waiting. If the
response is lost, sending the original request again can repeat its effects.
This proposal separates starting work from obtaining its result.

| Service | Purpose |
| --- | --- |
| `SubmitService` | Admit and start one eligible original request |
| `GetServiceResult` | Look up that operation and collect its retained result |
| `ControlServiceExecution` | Request work cancellation, acknowledge a received outcome, or give up result recovery |

The original request is sent once. A failed submission exchange is followed by
lookup using the Client operation ID, not transparent replay of that request.
A Server that cannot provide the requested recovery contract rejects admission
rather than executing through a non-recoverable fallback.
An SDK retry using a different MethodId is another original request, not
collection of the admitted operation. The typed Client disables that fallback
for captured Methods.

Ordinary Service APIs retain their existing behaviour. Support must be
explicitly enabled; language-level asynchronous execution alone does not
provide this protocol.

#### Interface choice and alternatives

The prototype implements the selected three-Service interface, not an
alternative manager Object. A generic manager could expose submission,
collection and control as Methods over Call and reuse more existing dispatch
and generator code. It would still need send-once recovery, owned execution,
typed acceptance, authorization, retained results and bounded accounting.
It would also need rules for the outer Call status, manager Method status and
multiple manager invocations in one Call. No equivalent manager prototype or
total integration-cost comparison has been produced.

For a certificate-only application that can change its Method interface,
ticket-based Methods may be a simpler choice. Part 12's pull
StartSigningRequest/FinishRequest workflow is not a drop-in replacement for push
CreateSigningRequest. A push ticket API would still have to define recovery
and receipt rules while preserving the device's pending-key and transaction
ownership. UpdateCertificate and ApplyChanges remain separate installation and
application steps; ApplyChanges is not result collection.

Subscriptions and Events address notification delivery, while Programs expose
an application's execution lifecycle. Neither automatically retains an
unchanged original Service response. These alternatives are design rationale,
not additional conformance modes of this prototype.

### Namespace and provisional identities

All declarations use namespace URI `http://opcfoundation.org/UA/`, namespace
index zero. The overlay model version is `1.05.07-ase-draft.0.2.0`; the contract
revision advertised by an enabled Server is `0.2.0`. These are experimental
identifiers, not final OPC Foundation assignments.

| Structure | DataType | Default Binary | Default XML | Default JSON |
| --- | --- | --- | --- | --- |
| ServiceOperationInfoDataType | 1200010 | 1202010 | 1203010 | 1204010 |
| SubmitServiceRequest | 1200020 | 1202020 | 1203020 | 1204020 |
| SubmitServiceResponse | 1200021 | 1202021 | 1203021 | 1204021 |
| GetServiceResultRequest | 1200022 | 1202022 | 1203022 | 1204022 |
| GetServiceResultResponse | 1200023 | 1202023 | 1203023 | 1204023 |
| ControlServiceExecutionRequest | 1200024 | 1202024 | 1203024 | 1204024 |
| ControlServiceExecutionResponse | 1200025 | 1202025 | 1203025 | 1204025 |

For example, the proposed Binary encoding of SubmitServiceRequest is
`i=1202020`. UA JSON's `UaTypeId` identifies the DataType, `i=1200020`, rather
than its allocated Default JSON encoding Object.

`SupportedServices` and `ServiceOperationInfoDataType.ServiceId` identify
original request DataTypes, not encoding Objects. The three wrapper Services
are not eligible original requests.

### Ordered fields

Order below is declaration and wire order. `Structure` denotes an
ExtensionObject-valued field, not permission to submit an arbitrary Structure.
The standard headers retain their existing layouts.

| Structure | Ordered field/type pairs |
| --- | --- |
| ServiceOperationInfoDataType | `ClientOperationId:Guid`, `ServerToken:ByteString`, `ServiceId:NodeId`, `State:ServiceOperationState`, `OutcomeKind:ServiceOperationOutcome`, `OutcomeStatus:StatusCode`, `ExecutionAllowance:Duration`, `ResultRetentionTime:Duration`, `ExecutionRemaining:Duration`, `ResultRemaining:Duration`, `HistoryRemaining:Duration`, `CancellationRequested:Boolean`, `RetryAfter:Duration` |
| SubmitServiceRequest | `RequestHeader:RequestHeader`, `ClientOperationId:Guid`, `Request:Structure`, `ExecutionAllowance:Duration`, `ResultRetentionTime:Duration`, `WaitTime:Duration` |
| GetServiceResultRequest | `RequestHeader:RequestHeader`, `ClientOperationId:Guid`, `ServerToken:ByteString`, `WaitTime:Duration` |
| ControlServiceExecutionRequest | `RequestHeader:RequestHeader`, `ServerToken:ByteString`, `Action:ServiceOperationControlAction` |
| SubmitServiceResponse | `ResponseHeader:ResponseHeader`, `Operation:ServiceOperationInfoDataType`, `Result:Structure` |
| GetServiceResultResponse | `ResponseHeader:ResponseHeader`, `Operation:ServiceOperationInfoDataType`, `Result:Structure` |
| ControlServiceExecutionResponse | `ResponseHeader:ResponseHeader`, `Result:ServiceOperationControlResult`, `Operation:ServiceOperationInfoDataType` |

The response fields named `Result` have different types. Submit/Get return an
optional original response payload. Control returns a control-result
enumeration and never transfers that payload.

### Enumerations

| Enumeration | DataType | EnumStrings | Values |
| --- | --- | --- | --- |
| ServiceOperationState | 1200000 | 1205000 | `Unknown=0`, `Pending=1`, `OutcomeAvailable=2`, `Acknowledged=3`, `Abandoned=4`, `Expired=5` |
| ServiceOperationOutcome | 1200001 | 1205001 | `None=0`, `ServiceResponse=1`, `ExecutionDeadlineExceeded=2`, `ResultUnavailable=3` |
| ServiceOperationControlAction | 1200002 | 1205002 | `Unspecified=0`, `Cancel=1`, `Acknowledge=2`, `Abandon=3` |
| ServiceOperationControlResult | 1200003 | 1205003 | `Unspecified=0`, `Applied=1`, `AlreadyApplied=2`, `NotApplicable=3`, `UnknownOperation=4` |
| ServiceOperationAuditAction | 1200004 | 1205004 | `Admitted=0`, `OutcomeCommitted=1`, `CancellationRequested=2`, `Acknowledged=3`, `Abandoned=4`, `Expired=5`, `Rejected=6`, `Denied=7`, `Collected=8`, `SessionClosed=9`, `HistoryForgotten=10`, `InvocationEnded=11` |

Unspecified is invalid as a requested control action or returned control
result. Unknown is a lookup result: it does not prove that the original work
never ran, cannot still be admitted, or is safe to repeat.

### Identity and ownership

The Client chooses a nonzero random Guid before submission. An admitted
operation also has an opaque Server token of 1 to 64 bytes. This prototype uses
32 random bytes. Neither value is a bearer credential.

GetServiceResult accepts exactly one selector. A nonzero ClientOperationId
with a **null** ServerToken recovers a lost submission reply. Once the token is
known, use it with a null Guid. ControlServiceExecution requires the token.
RequestHeader.requestHandle identifies the individual exchange.

A repeated submission using a remembered Client ID is rejected even if its
inputs are identical. This is not input-fingerprint-based idempotent
submission. Do not reuse an ID for another logical operation or automatically
resend original work after an uncertain result.

The issuing Session, its validated creator and current authorization govern
collection and control. A new Session is not a continuation merely because
the application, user or an identifier is the same. Authorization is checked
again before delayed delivery. The enclosed request cannot select a different
Session through its own header.

Recovery follows existing activation and trust rules. It does not authorize
weaker security settings or a new identity. The opt-in shared Client binding
is distinct from the strict request-context API.

Replacement-channel activation preserves the Client certificate, creator,
SecurityPolicy and MessageSecurityMode required by Part 4; it does not apply a
"same or stronger" substitution. The continuity check does not pin the old
Server certificate. An authorized renewal can be accepted only after validating
the new certificate against the intended Server identity and the mapping's
trust requirements, followed by reactivation of the original Session.

Protection is evaluated from the established transport. TLS can provide HTTPS
integrity and confidentiality without changing UA MessageSecurityMode or policy.
A claimed profile URI, URL scheme or request header is not proof of TLS.
Transport protection does not grant application or user permissions.
HTTPS transport certificates and OPC UA ApplicationCertificates keep their
separate mapping-defined roles.
The recoverable Client binding verifies the transport peer's certificate against
the captured application identity or HTTPS host before sending an exchange;
discovery metadata alone cannot establish that identity.

### Reading a response

The outer ResponseHeader describes the current Submit/Get exchange. The
embedded original response includes its own header, results and diagnostics.
Its contents are fixed once committed. A later collection uses a new outer
header without rewriting the embedded one.

An original response payload is present only when State is OutcomeAvailable
and OutcomeKind is ServiceResponse. Its type must match the submitted Service,
with the normal original ServiceFault exception. OutcomeStatus then equals
the original header's ServiceResult. A Good ServiceResult does not imply that
all items in a batch succeeded.

ExecutionDeadlineExceeded and ResultUnavailable carry no original response.
These outcomes can occur after original work has had effects; they are not
successful replacements for the original Service response.

Unknown replies have no Server token or ServiceId, zero lifetime values,
OutcomeKind None, OutcomeStatus Good, CancellationRequested false, RetryAfter
zero and no Result. A Client-ID lookup echoes the requested ID; a token-only
unknown lookup has a null Guid. Good in this metadata is not business success.

### Time and resource limits

Durations are milliseconds. ExecutionAllowance and ResultRetentionTime must be
finite and positive; WaitTime must be finite and nonnegative. Zero wait is a
poll. The Server accepts requested lifetimes unchanged or rejects them before
dispatch.

ExecutionAllowance, E, is the requested execution budget starting at admission;
admission accepts that exact positive finite budget or rejects it.
ResultRetentionTime, R, starts when the final outcome is available. A fast result
in SubmitService still has the same retention and acknowledgement requirements
as a later result.

When E expires first, the operation records an execution-deadline outcome and
requests cancellation where possible. A late original response cannot replace
it. Invocation resources stay charged until the invocation actually ends; a
protocol deadline does not establish that external effects stopped.

ACK, abandonment or result expiry releases the payload. Terminal history then
lasts another R from release. Polling and repeated controls renew neither
window. Still-running work can require minimal identity and accounting beyond
the public intervals.

Remaining durations are elapsed-time measurements, not a new lease starting
when the Client receives a reply. Transit time consumes the opportunity.
Positive Pending RetryAfter is at most ExecutionRemaining and strictly less
than R. If no positive value is safe, it is zero. Other states use zero.

Admission reserves resources before invocation. Existing message/encoding and
aggregate resource limits apply to the complete envelope, not just its inner
Result. The prototype caps retained results: oversized original output becomes
a small ResultUnavailable outcome. This never authorizes repeating work.

A smaller delivery limit after recovery can prevent an exchange from carrying
an existing result. That failure does not rewrite or acknowledge the saved
outcome or, by itself, invalidate the operation identity. Collection can resume
after permitted capacity is restored within the original R. It cannot renew R,
replay submission, raise an applicable captured safety ceiling or bypass
security, namespace/type validation or other decoder limits. Storage still used
by an in-flight sender stays accounted for after logical release.

### Controls and acceptance

Cancel requests stopping active work where possible. It does not promise
rollback. A repeated request is AlreadyApplied only while that invocation is
active; ended work is NotApplicable. An original cancellation response is an
original Service outcome, distinct from a failed control exchange.

Acknowledgement accepts an available outcome and releases its payload.
Abandonment gives up recovery without requesting work cancellation. These
actions have distinct applicability; they are not aliases.

ACK is a separate exchange. If its reply is lost, retry ACK, not the original
request. Failure of that exchange does not replace an already accepted result.

Acceptance policy is Client-local. Default SDK ACK follows decoding,
validation **and acceptance into the caller completion context**; decoding an
internal object is insufficient. Selected application/storage acceptance must
also succeed before ACK. The same wire action is used for every local policy.
For a typed Method API, this includes decoding and validating its output
arguments through the generated proxy. Decoding a CallResponse alone is not
acceptance of its typed Method result.

Ordinary Cancel by requestHandle ends the wrapper wait after admission, not the
owned invocation. Cancellation observed before admission can prevent dispatch.
Connection loss and stopping a local wait imply neither abandonment nor work
cancellation.

### Existing StatusCodes

No new StatusCodes are allocated.

| Condition | Status |
| --- | --- |
| Invalid identifier, selector, token, action or duration | `BadInvalidArgument` |
| Unsupported positive lifetime | `BadOutOfRange` |
| Remembered duplicate Client ID | `BadInvalidState` |
| Unsupported or excluded original Service | `BadServiceUnsupported` |
| Admission reservation failure | `BadResourceUnavailable` |
| Record known in the requesting Session, but creator or action authorization fails | `BadUserAccessDenied` |
| Security configuration ineligible for recovery | `BadSecurityModeRejected` |
| Execution-deadline outcome | `BadTimeout` |
| Oversized original response | `BadResponseTooLarge` |
| Other original-response encoding failure | `BadEncodingError` |

Lookup is Session-scoped. For an otherwise authorized request, a token or ID
belonging only to another Session is indistinguishable from a nonexistent
reference in this Session. Get returns Good plus Unknown. Control returns Good
plus UnknownOperation and Unknown metadata. Neither reveals the foreign
identity, existence or payload.

This does not conceal every authorization failure. A known record in the
requesting Session can produce BadUserAccessDenied when its creator or current
permission check fails. Normal authentication and general Service-permission
errors also retain their ordinary behaviour.
For a retained Read of a Value, current authorization includes the effective
`UserAccessLevel.CurrentRead` permission and its metadata callback, not only
role permissions. This check must not invoke the original Value read again.

### Optional runtime model

The generated overlay contains 67 Nodes and 15 types. Compiling its definitions
does not enable the feature. A disabled Server neither installs its capability
root nor executes the new Services.

| Definition or root | NodeId | Base type or parent |
| --- | --- | --- |
| AsyncServiceCapabilitiesType | i=1200040 | BaseObjectType, i=58 |
| AsyncServiceDiagnosticsType | i=1200041 | BaseObjectType, i=58 |
| AuditServiceOperationEventType | i=1200042 | AuditSessionEventType, i=2069 |
| AsyncServiceCapabilities | i=1200100 | ServerCapabilities, i=2268 |
| AsyncServiceDiagnostics | i=1200101 | ServerDiagnostics, i=2274 |

Activation installs the overlay and fixed root anchors. Runtime children get
Server-assigned NodeIds in the application namespace and retain namespace-zero
BrowseNames. A type member's declaration NodeId is not an instance child ID.

The capability root reports ContractVersion, SupportedServices and
minimum/maximum/default E and R. `SupportsRecoverableExecution` is an optional
read-only Boolean Property on a Method instance. Absence is undeclared; True
is not an admission guarantee; False reports ineligibility under current
Server policy. Admission evaluates current policy, not cached Client discovery.
A provider must not publish current False yet ignore it because Call is in
its supported-Service list. A current False on an otherwise callable Method
rejects the whole recoverable Call submission with BadServiceUnsupported before
any item executes. Ordinary per-item validation and permission failures remain
per-item results, not an all-items-authorized transaction.

Diagnostics are privileged aggregate measurements, not an operation browser.
ActiveOperationCount follows invocation lifetime; RetainedOutcomeBytes includes
storage still used by senders. Cumulative counters saturate at UInt64 maximum.

The new audit Event adds ClientOperationId, ServiceId, Action, OperationState,
OutcomeKind and OutcomeStatus to inherited audit fields, including SessionId.
It contains no raw control token, credentials or copied request/result payload.
Audit action success is separate from the original operation's outcome.

The publication-metadata package is not required at runtime and introduces no
runtime namespace dependency.

### Limits

The contract provides bounded recovery within one Session. It does not promise
restart durability, replacement-Session recovery, exactly-once business
effects, batch rollback, scheduling priority, progress or completion estimates.
Codec and in-process endpoint tests are not evidence of native reply loss or
certificate rotation; those require the integration tests described in
[Evidence boundaries](#evidence-boundaries).
Evidence for a mapping must observe the Server's actual encoding and established
protection; configuring an endpoint or silently falling back to another mapping
does not demonstrate it.
