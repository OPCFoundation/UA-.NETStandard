# Recoverable Service Execution (RSE)

RSE separates one original OPC UA request from the exchanges used to collect
and acknowledge its outcome. Losing a reply is not permission to repeat a
mutation.

This default-off reference implementation targets the experimental
**OPC 10000-29 draft 0.4.0** contract pinned at Core
`533587684928669b0cd183fc9ab61b5b0730a576`. It is not an adopted standard,
a certification claim or an exactly-once business transaction.
The [contract tooling](../tools/AsyncServices/README.md) records the canonical
inputs and fingerprints. Focused validation covers the integrated recovery
engine, Client, storage, native transports and the process-restart and NativeAOT
paths described below. Compilation or a local storage test alone is not complete
restart-conformance evidence.

The original proposal is
[#4813](https://github.com/OPCFoundation/UA-.NETStandard/issues/4813).
The current specification is
[OPCF-Members/OPC10000-Core#8](https://github.com/OPCF-Members/OPC10000-Core/pull/8).
The earlier three-Service prototype is retired, not a second supported binding.

## Five manager Methods

The feature uses ordinary `Call`, generated Structures and generated Method
bindings. It does not add a transport or dedicated ServiceSet.

| Method | Purpose |
| --- | --- |
| `Submit` | Admit one original request and start it at most once |
| `GetResult` | Look up state and collect the shared retained outcome |
| `Acknowledge` | Release an outcome accepted by the Client |
| `Cancel` | Request stopping owned work, without promising rollback |
| `Abandon` | Give up recovery without requesting work cancellation |

Each Method has one scalar typed request argument and one scalar typed response
argument. The declared wrapper Structures have no request or response headers.
The invoked control Method selects the action; there is no control Action field.
The complete original request and complete immutable original response remain
inside their declared envelopes.

Keep three result layers separate: the outer `Call` exchange, the individual
manager Method's `CallMethodResult`, and the original or protocol outcome.
Every outer exchange has a fresh header and ordinary per-exchange
`requestHandle`. Collection does not rewrite the saved original header,
diagnostics or item results.

Multiple manager invocations in one `Call` do not form an ordered or atomic
transaction. A sibling failure or a lost outer reply does not undo another
admission. The whole outer response owns any selected retained data until its
transport has finished using it.

### Model identity and discovery

All RSE definitions use `http://opcfoundation.org/UA/RSE/`; Core references remain
in namespace zero. Resolve namespace indices from the live URI table.
The canonical `Opc.Ua.RSE.NodeSet2.xml` contains 96 nodes and 16 types, including
NamespaceMetadata. Generated public C# types are in `Opc.Ua`, with model-scoped
`Rse*` identifier helpers.

Discover `Server/RecoverableExecutionManager/Capabilities` and the live manager
Methods. The manager's RSE identifier is 73, its Capabilities identifier is 71,
and its type identifier is 69. The type's Submit/GetResult/Acknowledge/Cancel/
Abandon declaration triples occupy 74 through 88. Runtime Method children have
Server-assigned application-namespace identities: **do not call a type
declaration or calculate an instance MethodId from it**.

Capabilities reports contract version, original request DataTypes, currently
qualified recovery modes and the supported minimum/default/maximum E and R.
`SupportedServices` is not a promise that every parameter or mode is eligible.
A disabled Server neither installs the manager nor executes its Methods.
The `rse-session-0.4`, `rse-owner-0.4` and `rse-durable-0.4` facet URIs and
`RSE-*` conformance identifiers remain experimental.

## Enable the Server explicitly

There is no extra build switch. Session mode is the default when the feature is
enabled:

```csharp
services.AddOpcUa()
    .AddServer(options =>
    {
        options.ApplicationName = "RecoverableServicePrototype";
        // Configure ordinary endpoints, certificate stores and authorization.
    })
    .WithExperimentalAsyncServices(options =>
    {
        options.MaxExecutionAllowance = 60_000;
        options.MaxResultRetentionTime = 60_000;
    });
```

Direct construction uses the same implementation:

```csharp
var server = new StandardServer(telemetry)
{
    AsyncServices = new AsyncServiceExecutionOptions
    {
        Enabled = true,
        RecoveryModes = [ServiceOperationRecoveryMode.Session],
        MaxExecutionAllowance = 60_000,
        MaxResultRetentionTime = 60_000
    }
};
```

The host supplies finite `TransportQuotas.MaxMessageSize` and the existing
[resource-isolation infrastructure](ServerScalability.md). Admission reserves
work, input, outcome and history capacity before dispatch. Unlimited message
capacity or missing isolation does not permit an unbounded recovery obligation.
There is no separate RSE quota hierarchy or result-size policy Node.

`DefaultAsyncServiceExecutionProviderFactory` accepts the same dependencies as
the direct `AsyncServiceExecutionProvider` constructor.
`IAsyncServiceExecutionProviderFactory` is the replacement-provider interface.
Startup awaits initialization before installing the manager. Shutdown stops new
exchanges and drains owned local work and maintenance before disposing journal
synchronization.

### Original requests

The implementation wraps the existing ordinary handlers for fourteen families:
Read, Write, Call, HistoryRead, HistoryUpdate, Browse, BrowseNext,
TranslateBrowsePathsToNodeIds, RegisterNodes, UnregisterNodes, AddNodes,
AddReferences, DeleteNodes and DeleteReferences.

QueryFirst and QueryNext remain unsupported. No query engine is added.
Discovery, SecureChannel and Session lifecycle, Subscription/MonitoredItem
operations, Publish/Republish and the ordinary Cancel Service cannot be submitted.
An original Call containing any RSE manager Method is rejected before any item
executes.

Ordinary limits, permission checks, diagnostics and per-item results still
apply. A positive `SupportsRecoverableExecution` Method hint cannot override
mode or resource restrictions. A current False hint on an otherwise callable
Method rejects the whole recoverable submission; ordinary invalid or
unauthorized items retain their ordinary per-item failure behavior.
The hint's BrowseName is qualified by `http://opcfoundation.org/UA/RSE/`,
not namespace zero. Resolve the namespace URI when declaring or finding it.
An unconfigured, recognized recovery mode is rejected with `BadNotSupported`;
an ineligible original Service or Session-bound resource uses `BadServiceUnsupported`.

BrowseNext release calls retain their ordinary empty `Results` and
`DiagnosticInfos` arrays. Normal continuation requests still require a result
for every input continuation point.

The default provider uses StandardServer's shared handlers and node-manager
callbacks. It does not invoke an arbitrary derived Server's public Service
override as an owned continuation. Custom execution must use those ordinary
handler interfaces or an execution provider that preserves the same contract.

## Recovery modes and ownership

| Mode | Recovery scope | Required Server qualification |
| --- | --- | --- |
| Session | Original Server Session and validated creator | Ordinary activation, transport and authorization rules |
| Owner | Stable authenticated application-and-user owner across Sessions | Owner mapping, held independent-execution eligibility and stable resource accounting |
| Durable | The same stable owner across Server restart | Owner requirements, protected durable storage, qualified restart-safe time, codec compatibility and recoverable work fencing |

Session mode remains enabled in every supported configuration. Owner and
Durable must be selected explicitly; missing qualification is an error, not a
request to downgrade.

Session closure ends Session-mode recovery but does not cancel Owner or Durable
work. Independently owned invocation contexts copy validated identity, roles,
locales and channel facts instead of retaining a live Session. Original enclosed
headers remain unchanged; effective authentication and diagnostic permissions
are held separately.

### Stable authenticated owners

`IRecoverableExecutionOwnerProvider` maps authenticated proof to an opaque
`RecoverableExecutionOwnerId`. The configured adapter uses explicit SHA-256
application-certificate mappings and exact validated issuer/subject mappings.
Display names, claimed ApplicationUri values, TLS alone or possession of a
control token are not ownership evidence.

`WithRecoverableExecutionOwners` registers configured mappings.
`ConfiguredRecoverableExecutionOwnerProvider` also supports direct construction.
Keep authority, application and user identities stable across restart.
Certificate renewal requires an explicit alias and ordinary trust and
application-authentication validation. Preserve original mapping evidence while
retained records depend on it; missing or inconsistent evidence fails closed.

Session duplicates are scoped to the actual Session and validated creator.
Owner and Durable share one stable-owner Client-ID namespace. Changing mode,
opening a new Session or using an authorized certificate alias cannot bypass
duplicate detection or multiply the owner's existing resource ceiling.

### Eligibility is a held guarantee

`IRecoverableExecutionEligibilityProvider.AcquireAsync` must establish before
admission that the exact original inputs and every possible response are
independent of the issuing Session. Its reservation holds the required
routing/configuration/resource conditions through invocation completion.

The absence of an input continuation point is insufficient: Browse and
HistoryRead can create Session-scoped resources in their responses. Without an
applicable transfer contract, reject Session aliases, continuation points,
Session-dependent Methods and response-created Session resources before
dispatch. The prototype rejects BrowseNext and nonempty HistoryRead continuation
inputs for non-Session modes independently of application qualification.
Guaranteed bounded, self-contained responses can qualify; probing the operation
to discover eligibility cannot.

Durable mode requires `IRecoverableExecutionDurableEligibilityProvider`.
`AcquireDurableAsync` associates the held conditions with a unique execution
identity before admission. On restart, `RecoverAsync` must establish termination
or hold a fence excluding conflicting work, including ordinary handler calls.
A new process, expired lease or absent local Task is not termination proof.
The Server neither executes nor resumes the saved original request.

A recovered invocation without termination proof remains charged even after
its outcome and public history expire. Disposing local recovery resources during
shutdown must not remove the underlying protection against surviving work.
When termination is established at a subsequent qualified recovery, the journal
records that fact without changing the selected outcome or renewing retention.

### Current authorization

`IAsyncServiceAuthorizationPolicy` adds current action-specific authorization.
It does not replace ordinary authentication, owner validation or node checks.
Collection rechecks current owner identity, admitted roles, the invoked manager
Method and the relevant original permissions after waits. Successful original
permission checks are captured in a bounded descriptor rather than recreated by
executing the request again.

A lost read permission denies disclosure without repeating the Value callback.
Foreign owner scopes and exact-mode mismatches produce canonical Unknown
metadata, not an existence-revealing result. A known authorized scope with
revoked permissions can return `BadUserAccessDenied`.

## Time, outcomes and controls

E (`ExecutionAllowance`) and R (`ResultRetentionTime`) are exact positive finite
milliseconds. Unsupported or unreservable values are rejected before dispatch,
not clamped. WaitTime is finite and nonnegative; zero is a poll.
Supported values must roundtrip unchanged through the implementation's
100-nanosecond ticks. This check and metadata projection are consistent across
target frameworks; an adjacent floating-point value is not silently revised.
Remaining-duration fields conservatively floor to whole milliseconds.

E starts at admission commit. The initial Submit wait starts at the same point;
GetResult waiting starts with its first authorized Pending lookup. Outer
timeoutHint and cancellation of a wrapper wait do not shorten E.
R starts with the committed final outcome. A result returned directly by Submit
has the same retention and explicit acknowledgement contract as a later result.

At E, the Server selects an immutable `ExecutionDeadlineExceeded`/`BadTimeout`
outcome and requests cancellation where possible. Late original output cannot
replace it. Work remains charged until actual completion is established.
Polling and repeated controls renew neither E nor R.

Only `OutcomeAvailable` plus `ServiceResponse` carries an original payload.
The original response type and complete header remain intact. Good at the
original Service level does not make all item results successful.
`ResultUnavailable`, `ExecutionDeadlineExceeded` and `ExecutionIndeterminate`
are protocol outcomes without an original payload; none proves absence of
business effects.

| Action | Effect |
| --- | --- |
| Ordinary Cancel by outer requestHandle | Ends that wrapper wait after admission; does not cancel owned work |
| RSE Cancel | Requests work cancellation; does not promise rollback |
| Acknowledge | Releases an accepted available outcome |
| Abandon | Waives recovery without cancelling work |
| R expiry | Releases the retained outcome at its existing deadline |

After release, small terminal history remains for another R, called H here.
Acknowledgement and abandonment retries return their remembered dispositions
within that interval. Expiry uses the original R deadline as its release time.
History expiration is not proof that an unresolved invocation stopped.

The Client chooses a nonzero random Guid before submission. The Server uses a
32-byte random token within the protocol's 64-byte maximum. GetResult accepts
exactly one selector: Client ID with a null token, or token with a null Guid.
Controls require the token. These identifiers are non-bearer references.
Remembered duplicate Client IDs are rejected regardless of input equality;
there is no fingerprint-based resubmission replay.

Unknown has canonical empty metadata and no payload. It is a lookup result,
not proof of non-execution and not permission to submit again.

## Durable storage and restart

The reference backend is
[`FileSharedKeyValueStore`](DurableStorage.md), a bounded local store with one
live owner. Merely linearizable or in-memory storage is not durable.
The journal requires durability verification, authenticated record protection,
stable ownership and a qualified time authority.

Supply these through DI or direct construction. The following variables represent
application-qualified providers and mappings, not built-in permissive defaults:

```csharp
services.AddSingleton<IRecordProtector>(recordProtector);

services.AddOpcUa()
    .AddFileSharedKeyValueStore(
        storeDirectory, "urn:example:rse-store", maximumSnapshotBytes: 64 * 1024 * 1024)
    .AddServer(options =>
    {
        options.ApplicationName = "DurableRecoverableServer";
    })
    .WithRecoverableExecutionOwners(authorityId, applicationMappings, userMappings)
    .WithRecoverableExecutionEligibility(durableEligibility)
    .WithRecoverableExecutionClock(qualifiedClock)
    .WithRecoverableExecutionCodecQualifier(customCodecQualifier)
    .WithExperimentalAsyncServices(options =>
    {
        options.RecoveryModes =
        [
            ServiceOperationRecoveryMode.Session,
            ServiceOperationRecoveryMode.Owner,
            ServiceOperationRecoveryMode.Durable
        ];
    });
```

Manage protection keys through the
[secret-store infrastructure](CertificateManager.md). The logical Server URI,
store identity, owner mappings and key availability must remain consistent.
Do not persist reusable credentials as a substitute for current authentication.
No token, original payload or protection key belongs in diagnostics or logs.

### Qualified time and serialized decisions

`IRecoverableExecutionClock` supplies a qualified elapsed-time domain that
includes downtime. The host supplies this provider explicitly. An unchecked UTC
clock or a new process-relative origin is not a substitute. A missing domain,
rollback or unavailable proof withdraws Durable readiness.

This implementation uses the following serialized commit interpretation:

1. Persist admission, sample qualified time after that write returns, then persist
   E's origin/deadline before original dispatch. The first write's latency
   consumes neither E nor the initial Submit wait; the second write's latency
   consumes both.
2. Persist a candidate outcome and sample time inside the per-operation
   serializer. Before E, that original outcome wins; at or after E, the deadline
   outcome wins instead. Persist R's origin/deadline before exposing O.
   This final write consumes R and cannot extend it.
3. For acknowledgement or abandonment, sample the logical release decision and
   atomically persist state, reason, release time and H's deadline. Publication
   latency consumes H. Result expiry uses its recorded R deadline.

On recovery:

| Durable state | Recovery action |
| --- | --- |
| Admission without a recorded E origin/deadline | Commit ExecutionIndeterminate; do not dispatch |
| Pending admission without a committed final outcome | Commit ExecutionIndeterminate; do not resume |
| Candidate outcome without a recorded R origin/deadline | Discard its uncommitted original payload and commit ExecutionIndeterminate |
| Outcome with its R origin/deadline | Restore it unchanged, including elapsed downtime |
| Released state | Keep it released, with the existing H deadline |

ExecutionIndeterminate has `BadUnexpectedError` and no original payload.
Its new R starts at its recovery commit. A subsequent restart restores that
committed outcome rather than starting another interval.

### Capacity, interpretation and failure

Admission reserves both existing isolation capacity and a protected storage slot
large enough for the bounded future record. The private stored envelope is padded
while unreleased; selecting the outcome cannot grow that slot. Release shrinks
it to terminal history. This is an internal storage representation, not an
additional wire field, result-size configuration or policy Node.

`ISharedKeyValueStoreCapacity` distinguishes a known pre-publication capacity
rejection from an ambiguous failed mutation. A full store rejects new admission
without disabling collection, acknowledgement or expiration of existing records.
Unexpected storage failures can have committed and withdraw readiness instead.
They do not become Unknown or permission to repeat original work.

Existing message limits include wrapper overhead and complete outer Call batches.
Oversized original output becomes a small explicit `ResultUnavailable` outcome,
not an overflowing retained response or a re-execution. A later smaller delivery
ceiling does not rewrite the saved outcome. In-flight senders and collectors
retain their own storage ownership after logical release.

`RecoverableExecutionTypeCompatibility` binds persisted interpretation to exact
URI tables, registrations, ordered definitions, encoding identities and qualified
codec revisions. The Server revalidates the live factory before interpreting,
collecting or releasing stored data. Unknown or opaque nested types fail closed.
Custom codecs need an explicit `IRecoverableExecutionCodecQualifier`; metadata
alone cannot prove arbitrary handwritten encoding semantics.

Startup reconstructs indices, reservations and required fences before
advertising Durable. Loss of qualification returns `BadResourceUnavailable`
and removes Durable from current SupportedRecoveryModes. It does not silently
renegotiate an existing admission to Owner, Session or ordinary execution.

## Client use and acceptance

The [Client guide](ClientRecoverableExecution.md) covers transparent calls,
explicit modes, handle-first submission, receipt scopes, protected checkpoints
and typed reattachment.

```csharp
await using ManagedSession session = await new ManagedSessionBuilder(configuration, telemetry)
    .UseEndpoint(endpoint)
    .PreferRecoverableExecution()
    .ConnectAsync(ct);

await using var acceptance = session.BeginAcknowledgementScope(
    requireRecoverableReceipt: true);
int value = await session.ReadValueAsync<int>(nodeId, ct);
await applicationStore.AcceptAsync(value, ct);
await acceptance.AcknowledgeAsync(ct);
```

Session remains the default preference. Owner and Durable selection is strict.
Ordinary `ISession` signatures, generated proxies and typed results do not change.
The existing strict request context remains strict; verified same-Session
reactivation and authenticated owner-scoped replacement use distinct opt-in
bindings.

Ordinary fallback is allowed only after conclusive lack of support **before any
Submit attempt**. An explicit rejected Submit, malformed reply, lost connection,
Unknown lookup, store error or unsupported selected mode cannot authorize
ordinary dispatch of the original request.

Default SDK acknowledgement follows decoding, complete typed validation and
acceptance into the caller completion context. Application acceptance or durable
storage policies are Client-local and use the same Acknowledge Method.
If its reply is lost, retry Acknowledge only. A failed acknowledgement exchange
does not replace already accepted caller data.

A protected Client checkpoint records identity and interpretation context for
reattachment. Reattachment collects or retries controls; it cannot submit again.
Server durability and Client acceptance/storage are separate obligations.
An indeterminate operation requires application/operator reconciliation.

## Security, native mappings and certificate management

The demonstrated native mappings are UA Binary over UA-TCP and HTTPS UA JSON.
HTTPS protection comes from an established TLS handshake; the stack does not
relabel UA MessageSecurityMode or SecurityPolicy. URI schemes, profile strings
and caller headers are not transport-security evidence.

Session reactivation preserves the existing Core requirements for mode, policy,
Client certificate and validated user. Anonymous plus Sign is rejected for
recoverable admission. A renewed Server certificate still requires normal trust
and intended-peer validation. Endpoint refresh is not permission to weaken
security or recreate an operation.

The two-hop certificate fixture
[`CertificateCycleAcrossRecoverableGateway`](../tests/Opc.Ua.Server.Tests/AsyncServices/AsyncServiceTransportTests.cs)
and [forwarder](../tests/Opc.Ua.Server.Tests/AsyncServices/AsyncCertificateForwarder.cs)
reuse CertificateManager, trust-list transactions, pending-key/secret stores and
normal Push Management ownership. They perform CSR generation, local test-CA
issuance, UpdateCertificate, ApplyChanges and private-key/communication
verification over all four UA-TCP/HTTPS combinations, including lost replies.
All PKI material is synthetic and generated at run time.

These Push Management operations remain Session-mode demonstrations. Selecting
Owner or Durable does not transfer a device transaction, pending key, file handle
or issuing Session. A device shutdown or rejected trust change can still end
that recovery scope. ApplyChanges is not a substitute for GetResult.

## Samples and validation scope

The [console sample](../samples/AsyncServices/README.md) demonstrates an ordinary
generated Method call across a real loopback TCP outage, with one invocation and
explicit acknowledgement after local acceptance:

```powershell
dotnet run --project samples\AsyncServices\RecoverableServiceDemo -- --network-outage
```

That scenario keeps the original Server process and Session alive. It is not
restart or failover evidence.

On Windows, the process-restart scenario launches five Server incarnations and
terminates four specifically owned child processes:

```powershell
dotnet run --project samples\AsyncServices\RecoverableServiceDemo -- --process-restart-test
```

It checks Owner recovery through a replacement Session, byte-preserving Durable
outcome recovery, released history, and interruption after a committed business
effect but before the outcome commit. Recovery returns ExecutionIndeterminate
without dispatching that original again. It also checks the exact history
boundary and loss of the qualified clock domain.

The surviving supervisor supplies explicitly simulated **TEST time**. This is
real process-termination evidence, not a physical-host reboot, power-loss test or
production clock qualification. The separate qualified-clock entry point
requires an application-supplied `IRecoverableExecutionClock`; it has no permissive
UTC fallback. The sample's certificates, protection keys and stores are created
at run time in owned temporary storage.

Keep evidence categories separate:

| Selection | What it exercises |
| --- | --- |
| `RseModelContractTests`, `RseOutcomeContractTests` | Canonical identities, generated codecs and envelope rules |
| Client recoverable-context and managed-session fixtures | Send-once lifecycle, typed acceptance, current interpretation and receipts |
| `AsyncServiceExecutionTests` | Real in-process handlers, exact lifetimes, permissions and retained delivery |
| Owner execution/identity/resource fixtures | Replacement Sessions, bounded independent identity and stable quotas |
| Journal/record-codec fixtures | Serialized durable decisions, fault phases and unchanged deadlines |
| `RecoverableDurableExecutionTests` | Integrated admission, restoration, current authorization, fencing, capacity and cleanup |
| File shared-store fixtures | Actual local publication/reopen, bounded capacity and uncertainty handling |
| `AsyncServiceTransportTests` | Native wire loss, mapping/protection observation and certificate cycles |
| `AsyncServiceNetworkChaosTests` | Opt-in sustained Session recovery through a real TCP chaos proxy on both native mappings |
| `RecoverableServiceDemoTests` | Ordinary/outage examples and the Windows child-process restart script |
| `AsyncServicesAotTests` | Generated codecs, typed Session Read/ACK, interpretation witness and protected local storage in a published native executable |

Run the certificate fixture with the repository's pinned SDK:

```powershell
dotnet test tests\Opc.Ua.Server.Tests\Opc.Ua.Server.Tests.csproj `
    -f net10.0 -p:CustomTestTarget=net10.0 `
    --filter FullyQualifiedName~CertificateCycleAcrossRecoverableGateway
```

Use net10.0 and net48 for the applicable focused Server selections.

### Opt-in network chaos soak

`RecoverableExecutionSurvivesSeededNetworkChaos` is an explicit net10.0 test,
also marked `LongRunning` for the standard CI exclusion filter. It reuses the
channel stress suite's TCP chaos proxy without terminating encryption.
Two long-lived peers alternate UA Binary
over SignAndEncrypt UA-TCP and HTTPS UA JSON. The default campaign lasts one
hour and stops on the first failed assertion or unexpected exception.

```powershell
$env:OPCUA_CHAOS_SEED = '4813'
$env:OPCUA_RSE_SOAK_SECONDS = '3600'
dotnet test tests\Opc.Ua.Server.Tests\Opc.Ua.Server.Tests.csproj `
    -c Debug -f net10.0 -p:CustomTestTarget=net10.0 `
    --filter 'FullyQualifiedName=Opc.Ua.Server.Tests.AsyncServices.AsyncServiceNetworkChaosTests.RecoverableExecutionSurvivesSeededNetworkChaos' `
    --logger 'trx;LogFileName=rse-network-chaos.trx'
```

Use `OPCUA_RSE_SOAK_SECONDS=60` for a smoke run. The maximum is 3600 seconds;
the current iteration and cleanup can finish after the campaign deadline.
The seed controls mutation amounts and the blocked-accept/stall schedule.
Fault phases cycle through lost Submit, GetResult and Acknowledge replies,
then interruption while owned work is still pending. A 45-second per-operation
watchdog rejects non-recovery; transport failures are expected only at the
explicitly injected boundaries, never counted as successful operations.

Every completed cycle checks unchanged Session identity, one Submit and one
original Method invocation, the exact mutation result, byte-identical retained
original responses, non-renewing retention and explicit acknowledgement.
Lost ACK replies must return AlreadyApplied on retry. Pending-work recovery
must not request work cancellation. The test logs its seed, elapsed time,
per-mapping completion counts and fault-phase counts even on failure.
Certificates and credentials are synthetic and stay in run-owned temporary
stores. This is sustained Session/network recovery evidence, not a Server
restart, Owner/Durable recovery, physical network or power-loss qualification.

### Evidence limits

Codec tests are not wire tests; in-process reopening is not an OS process crash;
a managed build is not NativeAOT evidence; a process restart is not proof of a
physical host reboot or power-loss guarantee. Historical CI and coverage belong
to their original source revisions, not to later uncommitted changes.
The focused NativeAOT selection is not full live Owner/Durable NativeAOT
conformance evidence.

There is no progress/completion notification feature, owner-operation browser,
generic executor resumption, implicit failover or business rollback.
Diagnostics remain privileged aggregate metadata, and audit events do not
contain raw tokens, credentials or copied original payloads.

Cumulative transition counters belong to the live execution-provider instance.
Restart reconstructs current-state gauges; it does not replay past outcome,
acknowledgement or abandonment events as new actions. New recovery commits,
such as resolving an incomplete admission or applying elapsed expiry, are
counted when they occur. Retrying a remembered control does not increase its
first-application counter.
