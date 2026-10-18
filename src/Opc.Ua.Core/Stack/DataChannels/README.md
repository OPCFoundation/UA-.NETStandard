# Common Data Channels runtime

`Opc.Ua.DataChannels` is the carrier-independent, complete-message runtime.
Carrier adapters own authentication, endpoint selection, transport framing and
cryptography. The runtime owns context confirmation, flow admission, revisions,
queues, lifetime and resource accounting. Identifiers in `DataChannelProfiles`
are provisional version-one profile and payload identifiers.

There is one internal record/control implementation, under `Wire`. Generated
`Opc.Ua.DataChannel*` types are the public model; applications must not introduce
another serializer or replace opaque/PubSub payload bytes with custom framing.
The OPC 10000-28 wire revision uses binary CONTROL opcodes and FLO/ChannelId
envelopes for both UASC profiles. The [public guide](../../../../docs/DataChannels.md)
records the immutable specification pin and unresolved carrier gates.

## Direct construction

Obtain `IDataChannelCarrier` from an explicitly selected, authenticated adapter.
Its verified application/key evidence is not a substitute for resource policy:
provide an `IDataChannelAuthorizer` that denies unmatched resources and peers.

```csharp
var budget = new DataChannelApplicationBudget();
var factory = new DataChannelContextFactory(
    authorizer,
    new DataChannelOptions(),
    budget,
    timeProvider,
    telemetry);

await using DataChannelContext context = factory.Create(carrier);
// Attach the adapter's ingress callbacks before starting context confirmation.
await context.StartAsync(cancellationToken);

await using DataChannel flow = await context.OpenAsync(
    new DataChannelParametersDataType
    {
        ResourceUri = "urn:example:measurements",
        Protocol = DataChannelProfiles.OpaquePayload,
        ContentType = "application/octet-stream",
        Direction = DataChannelDirection.Duplex,
        Delivery = DataChannelDeliveryMode.Ordered,
        Protection = DataChannelProtectionMode.Confidential,
        MaxMessageSize = 1024,
        MaxReceiveBytes = 65536
    },
    cancellationToken: cancellationToken);

DataChannelResult result = await flow.SendAsync(payload, cancellationToken: cancellationToken);
if (result != DataChannelResult.Good)
{
    throw new DataChannelException(result);
}

using DataChannelMessage? message = await flow.ReadAsync(cancellationToken);
if (message != null)
{
    ConsumeCompleteMessage(message.Payload);
}
```

Use one application budget across all contexts. Dependency injection registration
is available through `services.AddOpcUa().AddDataChannels(...)`; register the
authorizer explicitly. `IDataChannelContextFactory.Create` returns an unstarted
context so a provider can register it before transport attachment. `WaitForReadyAsync`
only waits; it never starts a handshake.

`OpenAsync` returns after the opener's READY write, and `AcceptAsync` exposes a
remotely opened flow only after READY receipt. `AcceptAsync` may be called before
`StartAsync`. Transport, opener and Publisher roles are independent. A flow's
`Context` property is borrowed identity/routing access, not ownership of other
flows or of the underlying connection.

## Message ownership and closure

- Each DATA payload is a complete immutable message. `SendAsync` copies it into
  bounded owned storage before awaiting transmission.
- Public `SendAsync` retains its immediate `WouldBlock` admission result. Internal
  complete-chunk transports can use `SendWithAdmissionAsync` to retain the same
  borrowed chunk while authorization or queue capacity is temporarily unavailable.
  The caller retains its buffer until completion. Waiting uses context/application
  capacity signals, not polling; no DATA identifier or message reservation is
  consumed before admission, and an already admitted send is never resubmitted.
- A received `DataChannelMessage` retains both byte and message-count capacity
  until disposed. Reading alone does not release credit. A reset does not reclaim
  messages already handed to application code.
- Queue expiry is opt-in and local. After the carrier's `DataChannelWrite.TryCommit`
  cut-off, cancellation can abandon the caller's wait but cannot free writer-owned
  storage or retract numbered UASC chunks.
- `EndAsync` drains and ends the local data direction. The reverse direction and
  required control can remain active. `ResetAsync` discards unsecured queues and
  terminates the selected flow.
  A committed revision still awaiting acknowledgement makes `EndAsync` return
  `WouldBlock`; retrying the same facade is idempotent and must retain the caller's
  overall deadline.
- Reset/disposal joins actual per-flow native release and outstanding writer
  ownership. Repeated disposal joins the same release; a flow owner must not
  destroy a shared host while that join is pending. Cleanup failures are observable.
  An explicit native reset starts native stream interruption before waiting for
  the common RESET record, which can itself be blocked by connection byte credit.
- Native flow/reset failures remain flow-scoped. Native management failure,
  invalid authentication, and failed committed UASC chunks invalidate the context.
  A datagram rejected after a path-capacity reduction is not reported as delivered.

## Revisions and admission

Only the opener updates a flow. Pass a complete generated descriptor to
`UpdateAsync`; immutable fields must remain equal. Explicit operation IDs enable
idempotent retries. Updates use per-direction ordered barriers and retain bounded
outcomes. A committed operation is not rolled back by timeout or ABORT.

Old application-held messages keep their reservation after a revision changes.
Both newly admitted directions remain reserved between the first and second
directional barriers; activating one direction cannot free the other.
Datagram reception retains the prior revision for the configured monotonic grace,
bounded by the sender's old-message barrier. A third live revision is refused
until the previous network state retires.

Shared UASC DATA credit starts at zero and includes the record and ChannelId.
Aggregate connection grants use flow/revision zero independently of per-flow
grants, so an opposite-direction revision barrier cannot strand connection credit.
Native ordered paths use actual backpressure. Physical UASC ingress transfers
its owned DATA buffer and reservation into the application queue. Capacity waits
are bounded by `DrainTimeout` from ingress admission; expiration resets the flow
rather than indefinitely stalling later controls. Datagrams use admitted
rate, burst, capacity and replay policies; they are never emulated over a reliable
stream. The application budget also offers atomic carrier reservations for
additional native/host buffers and jobs:
`DataChannelApplicationBudget.DataChannelCarrierReservation`.
Do not count logical flow/management streams twice.

Each context reserves 512 bytes and four message slots of management headroom
against the application limits. Unused headroom is withheld from DATA and
carrier admission but is not reported as retained message storage. Control
ownership includes encoded/decoded overlap; a COMMIT also reserves its later
barrier re-encoding. Both native parser headers are admitted before allocation,
and their storage is released on stream failure or closure.

## Authorization

Configured policy does not require a Session or model nodes. Session providers
may use `DataChannelGrantStore` for bounded, single-use context/key/challenge-bound
grants and must independently enforce live Session permission and staged updates.
Loss of a control connection alone is not Session revocation.

`DataChannelAuthorization.Lifetime` limits the freshness of authorization
evidence. At its deadline admission pauses while a live recheck renews or denies
permission. `RevocationToken` is an authoritative immediate revocation signal,
including during establishment. Grant redemption expiry remains a separate hard
deadline. All protocol deadlines use the injected `TimeProvider`.

## Carrier integration

Reliable ingress calls must preserve stream order and retain input storage until
the asynchronous receive returns. Native OPEN is itself the first common record
on a new bidirectional flow stream; bounded provisional decoding validates that
its claimed stream matches actual ingress before ACCEPT. READY and revision
barriers use the bound flow stream. There is no extra QUIC association prefix or
dummy DATA/FIN announcement.

Adapters deliver actual native RESET/STOP signals through
`ReceiveStreamResetAsync`; this out-of-band path releases blocked ingress without
fabricating stream data or EOF. Scoped management refusals do not discard later
records in the same segment. A non-Good per-flow receive outcome is not permission
to invalidate an otherwise ready context.

UASC adapters use the existing security sequence and single writer. Service and
control precedence applies only before DATA is secured; already numbered chunks
retain their order. SecurityMode.None is rejected. Integrity-only UASC additionally
requires explicit application and resource-policy approval.
Shared Service requests remain unsealed until arbitration; a pending control
is selected after at most eight consecutive Services, even within a coalesced
send. This is a selection bound, not a wall-clock latency guarantee.

Reverse shared-UASC with divergent transport/UASC initiator roles is intentionally
fail-closed pending clarification of the pinned context-selector/first-HELLO rules.
Ordinary reverse Services are unaffected. This unresolved applicability is not
claimed as reverse-flow conformance.
