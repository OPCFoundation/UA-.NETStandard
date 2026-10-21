# Client recovery with RSE 0.4

RSE is an experimental protocol. Opt-in Client execution uses the ordinary
`Call` Service and the generated `RecoverableExecutionManagerTypeClient`.
The Client resolves `http://opcfoundation.org/UA/RSE/` dynamically and discovers
the actual manager and Method instance NodeIds beneath
`Server/RecoverableExecutionManager`. It does not calculate instance identifiers
from the model's declaration identifiers.

## Ordinary calls and receipt scopes

Normal `ISession` signatures and generated application proxies are unchanged.
Recovery is disabled unless selected when acquiring a managed Session:

```csharp
await using ManagedSession session =
    await new ManagedSessionBuilder(configuration, telemetry)
        .UseEndpoint(endpoint)
        .PreferRecoverableExecution()
        .ConnectAsync(ct);

int value = await session.ReadValueAsync<int>(nodeId, ct);
```

The default preference is Session mode. An unsupported namespace, manager,
contract revision or original Service may permit ordinary execution **only
before Submit is attempted**. Authorization failures, malformed discovery data,
transport failures and any failed Submit reply are not fallback authorization.
After an attempted Submit, the Client collects by Client ID or token; it never
repeats the original request, including a MethodId compatibility fallback.

The current Server prototype advertises fourteen original request families:
Read, Write, Call, HistoryRead, HistoryUpdate, Browse, BrowseNext,
TranslateBrowsePathsToNodeIds, RegisterNodes, UnregisterNodes, AddNodes,
AddReferences, DeleteNodes and DeleteReferences. Generic Client typing also
recognizes QueryFirst/QueryNext, but the prototype has no ordinary Query engine
and does not advertise those Services. Their existing unsupported behavior is
preserved; generic typing is not a claim of Server Query support.

Session lifecycle, subscription and monitored-item Services, Publish/Republish
and the ordinary Cancel Service are not submitted through RSE. Internal Session
maintenance bypasses application acceptance and does not create receipts.

Generated Method outputs and typed convenience results are validated before the
application acceptance callback and before acknowledgement. Existing receipt
scopes group the original requests belonging to one caller operation:

```csharp
await using RecoverableAcknowledgementScope receipt =
    session.BeginAcknowledgementScope(requireRecoverableReceipt: true);

int value = await session.ReadValueAsync<int>(nodeId, ct);
await SaveApplicationValueAsync(value, ct);
await receipt.AcknowledgeAsync(ct);
```

Disposing a scope performs local cleanup only. It does not cancel, abandon or
acknowledge Server work. A failed acknowledgement does not replace an accepted
original result. Retry the receipt, not the original operation.

## Owner and Durable selection

Owner and Durable must be selected explicitly and never downgrade to Session
or ordinary execution:

```csharp
var options = new RecoverableSessionOptions
{
    RecoveryMode = ServiceOperationRecoveryMode.Owner,
    IdentityProvider = authenticatedOwnerMapping
};
```

`IRecoverableExecutionIdentityProvider` is an application-supplied, injectable
continuity authority. Its `VerifyAsync` method receives the activated, borrowed
Session and returns independent opaque keys for the logical Server, authenticated
Client application and authenticated user. It must validate actual authenticated
credentials and authorized renewal mappings. Display names, claimed
ApplicationUris and trust in a new certificate alone do not establish continuity.
Denied, revoked, ambiguous or unavailable mappings must throw.

The Client rejects anonymous users and unauthenticated applications for these
modes. The Server independently enforces its current owner mapping. A saved
Client ID or token grants no authority.

Session mode continues to use verified reactivation of the original Session.
Owner/Durable recovery uses a separate binding: it acquires a fresh strict
context from the original `ManagedSession`, verifies Server and owner continuity,
and preserves the captured namespace/type interpretation. It does not relax
`SessionRequestContext.CaptureAsync` or `CaptureRecoverableAsync`, retarget an
arbitrary transport, transfer Session resources, or make an ineligible original
request eligible. A changed interpretation fails closed.

## Protected Client checkpoints

Durable Client mode requires `RecoverableOperationCheckpointStore`. Owner mode
can also select it. The store uses `ISharedKeyValueStore`, the explicit
`ISharedKeyValueStoreDurability` qualification, linearizable conditional writes,
and `IRecordProtector` with `RecordProtectionContext`.

The protector and its keys must come from the application's existing protected
key/secret infrastructure. `NullRecordProtector` is rejected. Neither credentials
nor a Session authentication token are persisted. Original request/result data,
operation tokens, identity keys and acceptance state are encrypted and
authenticated for the complete application-prefixed record key.

```csharp
await using var backend = new FileSharedKeyValueStore(
    checkpointDirectory, logicalStoreId, protector, maximumSnapshotBytes);
var checkpoints = new RecoverableOperationCheckpointStore(
    backend, protector, "application/rse-client/");

var options = new RecoverableSessionOptions
{
    RecoveryMode = ServiceOperationRecoveryMode.Durable,
    IdentityProvider = authenticatedOwnerMapping,
    CheckpointStore = checkpoints
};
```

The Client commits recovery identity and the sanitized original request before
attempting Submit. It commits the validated original outcome and local acceptance
state before Acknowledge. For compound typed calls, every retained original must
complete the outer caller's validation and acceptance policy before the group
may acknowledge any of them.

An application acceptance callback can add domain-specific durable storage.
The SDK persists original OPC UA outcomes, not arbitrary application object
graphs or a distributed transaction with application storage. A callback failure
or storage failure leaves acknowledgement pending. Failed conditional writes
may have committed; reload and reconcile rather than treating failure as
permission to repeat the original request.

## Reattachment after Client restart

Checkpoint format/protection version 2 requires a qualified type-interpretation
witness. Version 1 records are rejected; the Client does not retry their old
protection context or fabricate compatibility from matching IDs.

The shared Core `RecoverableExecutionTypeCompatibility` module hashes the exact
namespace/Server URI tables, full ordered structure/enum definitions, registration
and encoding identities, and qualified codec revisions with SHA256. The opaque
witness includes a format/version prefix. Message-size and other decoding limits
are **not** schema identity: the returned context applies them separately.

Built-in authority comes from a fresh Core/RSE generated registration set and
exact activator singletons, not a mutable global factory, CLR names or reflection.
Every custom registration requires `IDataTypeDefinitionSource` metadata and an
explicit `IRecoverableExecutionCodecQualifier` revision. This is a trusted
application qualification contract, not proof that metadata describes arbitrary
handwritten codec behavior. Qualified custom codecs must remain immutable for a
captured context's lifetime; their revision must change when their semantics do.
Opaque or nested unknown types that cannot be qualified are rejected.

`RestoreAsync(currentContext, savedWitness, ct)` recaptures and verifies the live
factory before decoding saved payloads. It detects registration changes behind
the same factory facade. The Client also rechecks before persistent submission,
acceptance and acknowledgement. Merely retaining a frozen map is not considered
proof that the current factory remains compatible.

The module is in Core and is reusable by Server journals without referencing
Client:

```csharp
RecoverableExecutionTypeContext captured =
    await compatibility.CaptureAsync(currentContext, ct);
ByteString witness = captured.Witness;
ByteString payload = captured.Encode(originalResponse);

// After authenticating the stored record, before interpreting its payload:
RecoverableExecutionTypeContext restored =
    await compatibility.RestoreAsync(currentContext, savedWitness, ct);
IServiceResponse original = restored.Decode<IServiceResponse>(savedPayload);
```

Its `MessageContext` exposes a copied context over the captured strict factory.
`Encode` validates the whole encoded payload tree; `Decode` rejects unknown types,
malformed bodies and trailing data rather than recovering them as opaque values.
Configure `RecoverableOperationCheckpointStore` with an explicitly constructed
compatibility module when custom codecs are needed, or register the qualifier
and call `AddRecoverableExecutionTypeCompatibility()` for DI.

Reopen the same protected store and reauthenticate against the same logical
Server and owner. Recover each saved original with its exact generated response
type:

```csharp
var recovery = new AsyncServiceClient(options);
ArrayOf<Uuid> ids = await checkpoints.GetOperationIdsAsync(ct);

await using AsyncServiceOperation<ReadResponse> operation =
    await recovery.ReattachAsync<ReadResponse>(session, savedReadId, ct: ct);

if (!operation.Completion.IsCompleted)
{
    await operation.WaitForResultAsync(ct: ct);
}

ReadResponse original = (await operation.Completion).GetValueOrThrow();
await operation.AcknowledgeAsync(ct);
```

Restored handles cannot Submit. They restore a previously accepted local outcome
or collect the remembered operation and use explicit acknowledgement. Typed
generated Method projections can use `ReattachMethodAsync`; their captured Call
must match the saved original Call before a result can be accepted.
An unfinished typed Method checkpoint cannot fall back to raw Call-response
acceptance. An unfinished application or compound-call acceptance policy must
also be supplied again; omitting it fails before collection or acknowledgement.

A Session-mode checkpoint cannot attach to a replacement Session, even for the
same authenticated owner. Missing checkpoints, an Unknown lookup, expired
retention, an indeterminate outcome or a failed control reply never prove that
business work did not execute. Durable mode does not resume interrupted business
execution or provide exactly-once external effects.

## Dependency injection

Direct construction remains available. The corresponding registrations are:

- `AddRecoverableOperationCheckpoints`, using registered qualified
  `ISharedKeyValueStore` and `IRecordProtector` implementations.
- `AddExperimentalAsyncServiceClient`, optionally accepting an
  `IServiceProvider`-based options factory so owner mapping and checkpoint storage
  can be injected.
- `ManagedSessionOptions.RecoverableExecution` or
  `ManagedSessionBuilder.PreferRecoverableExecution(options)` for unchanged
  ordinary `ISession` calls.

Client unit tests use scripted transports and distinguish that evidence from
production Server integration and restart-conformance tests.
