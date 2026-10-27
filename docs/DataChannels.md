# Data Channels

**Experimental; not yet conformant to the current drafts.** The legacy flow
carriers and optional Client/Server lane below retain behavior developed against
the superseded OPC 10000-28 draft `0.2.0` at
[`OPCF-Members/OPC10000-Core@726c915`](https://github.com/OPCF-Members/OPC10000-Core/tree/726c91572a4e5725e114115cf0f69ab8cc2a6b0f/source/data-channels).
The current target is the split OPC 10000-30, -31 and -32 drafts `0.3.0` at
[`OPCF-Members/OPC10000-Core@2525043`](https://github.com/OPCF-Members/OPC10000-Core/tree/25250437f7086f173e825a6720476c01abe9e3af).
This revision splits out the Part 14 streaming extensions and adds an optional
normative UAFX binding. Its Native WebTransport data-channel mapping requires
server-authenticated TLS and a SignAndEncrypt UASC management stream on the same
WebTransport session. This branch still uses ordinary Service dispatch on that
stream and exporter-backed direct binding, so it does not conform to that
mapping. Earlier regression evidence targets revision `ff4ff8e` unless its
source scope is stated explicitly; it does not establish conformance to the new
UAFX annex.
The managed QUIC baseline and explicitly selected native QUIC/WebTransport
baselines now carry ordinary UACP/UASC independently of the legacy native flow
protocol. Windows native loopbacks complete HEL/ACK, SignAndEncrypt OPN,
correlated Services and authenticated direct ordered/datagram
payload/revision/END exchanges; WebTransport additionally completes CONNECT.
Browser interoperability and the full
Parts 30–32 assertion matrix remain unverified. The details below describe
current branch behavior, not conformance claims.
Identifiers and registrations remain provisional. See
[migration](#migrating-from-the-earlier-experiment) and
[limitations](#current-limitations) before deploying.

Public Data Channel entry points carry `[Experimental("DataChannels")]`, with
this document as their diagnostic link. The optional QUIC and WebTransport
binding assemblies carry the same annotation. Consumers must explicitly opt in,
for example by adding `DataChannels` to their project's `NoWarn` property:

```xml
<NoWarn>$(NoWarn);DataChannels</NoWarn>
```

Repository projects opt in centrally in `common.props`; this does not suppress
the diagnostic in applications consuming the packages.

A Data Channel carries **complete application messages** over an authenticated
association. The common runtime owns establishment, authorization, readiness,
bounded queues, revisions and closure. Applications choose the carrier separately
from the payload protocol. Configured flows require neither a Session nor an
address-space model; optional Session control uses generated Methods through
ordinary `Call`, not a new Service Set.

## Start with a working sample

The [brokerless PubSub sample](../samples/PubSub/ConsoleSecureChannelPubSub/README.md)
starts both peers on real loopback sockets and validates changing DataSet values,
complete NetworkMessage bytes, a live revision and cleanup. From the repository
root, using the SDK selected by `global.json`:

```powershell
dotnet run --project samples\PubSub\ConsoleSecureChannelPubSub --property:CustomTestTarget=net10.0
```

The default uses confidential Dedicated UASC, UADP and configured authorization.
It creates no Session or optional Data Channel model and needs no broker, native
library, pre-existing server or certificate-validation bypass.

```powershell
# UADP, with ordinary Read/Publish on the same shared SecureChannel.
dotnet run --project samples\PubSub\ConsoleSecureChannelPubSub --property:CustomTestTarget=net10.0 -- --shared

# Add X.509 Session authorization and reverse the Publisher role.
dotnet run --project samples\PubSub\ConsoleSecureChannelPubSub --property:CustomTestTarget=net10.0 -- --shared --authorization session --reverse --count 12
```

The sample README also documents separate server/client processes. `--reverse`
changes the Publisher role, not transport connection initiation.

| Sample | Outcome |
| --- | --- |
| [SecureChannel PubSub](../samples/PubSub/ConsoleSecureChannelPubSub/README.md) | UADP, dedicated/shared UASC, configured/Session authorization, one or two processes |
| [Streaming](../samples/Core/ConsoleDataChannelStreaming/README.md) | One complete-message workload across all four carriers; finite runs and a shared-channel Read/Publish benchmark |
| [Audio](../samples/Core/ConsoleDataChannelAudio/README.md) | Complete opaque PCM messages; finite headless WAV output or explicit Windows playback |

For example, these finite managed-carrier runs need no native assets:

```powershell
$env:CustomTestTarget = 'net10.0'
dotnet run --project samples\Core\ConsoleDataChannelStreaming -- --carrier dedicated --count 8 --size 64 --revise
dotnet run --project samples\Core\ConsoleDataChannelAudio -- --carrier shared --wav received.wav --duration-ms 200 --frame-ms 20 --revise
```

Choose a new WAV filename; the audio sample refuses to overwrite an existing
file. The samples manage owned demo identities through the certificate manager
and stores, explicitly trust their peer certificates, and fail on invalid input
or unsuccessful cleanup. Their READMEs describe production trust and secret
provisioning separately.

## Select a carrier and a payload

The managed QUIC baseline is exposed separately through `QuicUascTransport`,
`DataChannelProfiles.UascQuic` and ALPN `opcua-uasc-draft`. It feeds ordinary
UACP/UASC directly and can carry Services alone or explicitly configured
FLO/FLC, without opening the legacy native Client/Server lane. It requires
`SignAndEncrypt`, preserves independent application-certificate authentication,
and does not advertise direct streams, datagrams or exporter binding. See the
[QUIC baseline API and lifetime contract](../src/Opc.Ua.Bindings.Quic/NugetREADME.md#part-31-reliable-uasc-baseline).

`NativeQuicUascTransport` explicitly selects the native Part 31 backend for the
same profile/ALPN and existing UASC pipeline. It additionally exposes the
connection-owned exporter and actual token-bound ordered/datagram providers.
The protected common runtime still authenticates application certificates,
binding proofs and ASSOCIATE/ACK/READY; transport-only TLS is never application
identity. Finite native capacity, pending admissions and token history share
the injected application budget. Managed QUIC remains independently selectable.
See the [native baseline/direct API](../src/Opc.Ua.Bindings.Quic/NugetREADME.md#explicit-native-part-31-baseline-and-direct-delivery).

The native WebTransport baseline is exposed separately through
`WebTransportUascTransport` and `DataChannelProfiles.UascWebTransport`. It
uses HTTPS Extended CONNECT and one reliable WebTransport stream for ordinary
UACP/UASC. TLS authenticates the server only; UASC independently authenticates
the application and requires `SignAndEncrypt`. The native loopback exercises
ordinary Services through the existing external UASC listener and token-bound
ordered and datagram delivery after exporter proof and ASSOCIATE/ACK/READY.
Actual browser interoperability remains unimplemented. See the
[WebTransport baseline API](../src/Opc.Ua.Bindings.WebTransport/NugetREADME.md#part-32-reliable-uasc-baseline).

`UascStreamByteTransport` and its asynchronous connector factory are the shared
reliable-stream adapter. External hosts must enforce transport negotiation,
endpoint and stream admission before calling `TcpTransportListener.OpenExternal`
and `AcceptTransport`. The adapter itself is not a QUIC, HTTP/3 or browser
implementation, and stream establishment alone does not authenticate a UASC
application.

The following table distinguishes the current baseline profiles from the
separately selectable legacy native flow profiles.

Use `Opc.Ua.DataChannels.DataChannelProfiles`, rather than duplicating URI strings.
Profile matching is exact; an unavailable profile is not substituted with another.

| Carrier | Profile constant | Endpoint and security | Ordinary Services |
| --- | --- | --- | --- |
| Dedicated FLO/UASC | `DedicatedUasc` | `opc.tcp` or binary `opc.wss`, resource `/flow/v1/dedicated`; protected UASC | Ordinary MSG remains standard Services |
| Shared FLO/UASC | `SharedUasc` | `opc.tcp` or binary `opc.wss`, resource `/flow/v1/shared`; protected UASC | Share the authenticated SecureChannel |
| Part 31 QUIC baseline | `UascQuic` | `opc.quic`, ALPN `opcua-uasc-draft`; server-authenticated TLS plus SignAndEncrypt UASC | Ordinary Services and configured FLO/FLC |
| Part 32 WebTransport baseline | `UascWebTransport` | `https`, HTTP/3 Extended CONNECT; server-authenticated TLS plus SignAndEncrypt UASC | Ordinary Services; browser support unimplemented |
| Legacy native QUIC flow | `Quic` | `opc.quic`, ALPN `opcua-flow/1`; mutually authenticated TLS 1.3 | Only through the separately selected Client/Server lane |
| Legacy native WebTransport flow | `WebTransport` | `opc.wt`, HTTP/3 and WebTransport draft-16; mutually authenticated TLS 1.3 | Only through the separately selected Client/Server lane |

Both UASC profiles support raw TCP, Kestrel TCP and binary WSS
(`opcua+uacp`). WSS is not WebTransport. HTTPS request/response, JSON WebSocket,
Kestrel's older WebTransport draft and browser WebTransport are not substitute
carriers.

UASC flow endpoints reject `SecurityMode.None`. Confidential protection is the
default. Integrity-only requires explicit binding permission **and** resource
policy approval. Legacy native flow endpoints use TLS/mTLS without an outer
UASC layer; their discovery `None` fields do not mean unprotected data.
The Part 31/32 native baselines independently authenticate applications through
SignAndEncrypt UASC; transport TLS identity does not replace that authentication.

The payload constants are independent of the carrier:

| Constant | Payload |
| --- | --- |
| `UadpPayload` | Ordinary UADP data and discovery NetworkMessages (types 0–2) |
| `UadpAvApplicationPayload` | Minimal UADP type-3 A/V packets or application-format fragments |
| `UadpOpaqueApplicationPayload` | Minimal UADP type-3 opaque units or application-format fragments |
| `JsonPayload` | Not supported for PubSub over Data Channels |
| `OpaquePayload` | An application-defined complete message |

Type 3 requires selecting one of its application-payload protocols and configuring
the corresponding `ContentType` before opening the flow. It carries no DataSet,
chunk or generic reassembly wrapper; application-format boundaries and recovery
remain the application's responsibility.

Before admitting a shared-UASC streaming receive direction, register its finite
typed PublisherIds against the configured resource on that receiving context:

```csharp
using IDisposable route = context.RegisterUascPublisherRoute(
    resourceUri,
    new Variant(42u),
    writerGroupId: null,
    dataSetWriterId: null,
    networkMessageType: 3);
```

A duplex flow needs receive associations on both peers. Wildcard-only and
writer-field-only configuration cannot admit minimal streaming headers. Numeric
PublisherId types remain distinct; Byte 42 is not UInt32 42. A provisional or
active receive flow owns its admitted typed IDs, so an overlapping flow fails
before READY. The same typed ID may be used in the opposite receiving direction.
Ordinary UADP's distinct writer-set associations are not subject to this rule.

Streaming admission snapshots these associations. Disposing registrations or
editing the resource's routes affects future admissions, not an already admitted
flow's DATA routing. Close/reset that flow or revoke its authorization to stop it;
route-registration disposal alone is not streaming-flow revocation. Its typed
IDs become available for another admission after the flow terminates.

Directions are relative to the **flow opener**: `OpenerToAcceptor`,
`AcceptorToOpener` or `Duplex`. The context initiator, transport initiator,
flow opener and PubSub Publisher are distinct roles. The context initiator
sends the first HELLO and selects ContextId and Generation. The UASC initiator
fills that role for dedicated and shared UASC; the transport initiator fills it
for native carriers. Odd ChannelIds belong to the context initiator. Ordered
and Datagram delivery are distinct contracts; a datagram request never silently
becomes an ordered stream.

## Compose the application

The common runtime is in `Opc.Ua.Core`, under `Stack\DataChannels`.
`DataChannelContext` represents one authenticated association. The carrier
validates transport security and supplies its identity evidence before protected
HELLO. `OpenAsync` and `AcceptAsync` return a usable `DataChannel` only after
OPEN/ACCEPT/READY; receipt of a Session grant alone does not establish a flow.

Use the injected carrier factories and context factory, or their direct
construction equivalents. One `DataChannelApplicationBudget` shared across the
application bounds aggregate contexts, flows, bytes, messages, operations, grants,
probes and streams. `DataChannelOptions` bounds each context and flow, with an
injected `TimeProvider` for timers. Do not create a fresh application budget per
connection and expect an application-wide limit.

For UASC, supply `TransportChannelSettings.DataChannels` and connect through
`UascDataChannelClient`. Its `UascDataChannelConnection` exposes the ready
`Context`; `Services` is available only for Shared UASC. Server hosting attaches
the provider through `UseDataChannels(provider, enableModel: false)` or the
`IOpcUaServerBuilder.AddDataChannels` and `AddDataChannelSource` extensions.
No provider means no flow endpoint.

For TCP Reverse Connect, accept a waiting connection through
`ReverseConnectHost` or `ReverseConnectManager`, then pass that
`ITransportWaitingConnection` to the same client:

```csharp
await using UascDataChannelConnection connection = await client.ConnectAsync(
    waitingConnection,
    settings,
    cancellationToken);
DataChannelContext context = connection.Context;
```

The selected endpoint description must match the RHE endpoint URL and server
application URI. These advertised values do not authenticate the peer: the
ordinary UASC application-certificate validation and protected HELLO still run.
The handoff overload never substitutes a new outbound connection. The physical
server initiator owns the context identity and odd ChannelIds; the UASC client
remains the OPN requester and uses its original cryptographic role. Either peer
can open flows, independently of those roles. Integrity-only flows still need
explicit binding and resource-policy approval.

For native transport construction and deployment, see the
[QUIC binding](../src/Opc.Ua.Bindings.Quic/NugetREADME.md) and
[WebTransport binding](../src/Opc.Ua.Bindings.WebTransport/NugetREADME.md).
Native dependencies remain outside Core, ordinary Client/Server and base PubSub.

### Manager audit records

With the optional model and standard auditing enabled, each successful or refused
manager Call produces two records restricted to `SecurityAdmin` before EventFilter
selection and again before publication:

- `AuditDataChannelEventType` is a fieldless subtype of the standard Method audit.
  It supplies `MethodId`, `StatusCodeId` and positional input/output arguments.
  The Open binding input and grant output are null Variants; refused foreign-flow
  identifiers are redacted without shifting argument positions.
- `DataChannelOperationEventType` carries flow/context/revision/source attribution.
  Its `AuditEventId` equals the Method audit's `EventId`, and `ActionSucceeded`
  equals that record's `Status`. `Result` is the flow outcome: a successfully
  staged Modify Call can therefore have `ActionSucceeded=true` and
  `Result=WouldBlock` while the data-path revision is still pending.

Configured admission and autonomous data-path actions produce only operational
records, with no fabricated Method audit, correlation identifier or Session.
Foreign-Session refusals identify the caller but do not disclose the owner's
channel, context, generation, revision or source. Correlation identifiers and
SessionIds are attribution, not authorization or AuthenticationTokens.

### Complete-message I/O

Once a channel is ready, application code uses the same facade for every carrier.
The following send assumes `flow`, a complete `ByteString payload` and a
`CancellationToken cancellationToken` supplied by the application:

```csharp
DataChannelResult result = await flow.SendAsync(
    payload,
    cancellationToken: cancellationToken);
if (result != DataChannelResult.Good)
{
    throw new InvalidOperationException($"Data Channel send failed: {result}.");
}
```

Check the result rather than treating admission pressure as delivery.
`WouldBlock` can be retried under the application's finite deadline and pacing
policy. `SendAsync` copies the complete message into bounded owned storage.
Queue expiry and cancellation can prevent commitment; after commitment, the
writer retains its buffers and preserves the secured write even if the caller
stops waiting.

Every receive returns an owned message. Keep that lease alive through processing:

```csharp
while (true)
{
    using DataChannelMessage? message = await flow.ReadAsync(cancellationToken);
    if (message is null)
    {
        break;
    }

    Console.WriteLine($"Received {message.Payload.Length} bytes.");
}
```

`null` means directional EOF, not a reset or authorization success fallback.
Disposal releases both byte and message-count capacity. Do not retain payload
memory after disposing its message. `EndAsync` drains the local sending direction;
the other direction of a duplex flow can continue. Observe its result and the
peer's EOF before reporting orderly completion. `ResetAsync` and asynchronous
disposal join flow resource release without closing unrelated flows.

`Parameters`, `GetStatus`, `GetDiagnostics` and `StateChanged` expose the admitted
descriptor, outcomes and lifecycle. `UpdateAsync` performs the revision transaction;
changing a local descriptor object is not a negotiated update. A repeated
operation uses the same nonzero operation ID and proposal. A committed update
cannot be rolled back by caller cancellation.

`GetStatus().TransportStreamId` is reserved and always zero, including for legacy
native carriers. Do not use it to identify a direct lane: direct associations use
direction-specific tokens and owned stream objects, not exposed stream numbers.

`BytesSent` and `BytesReceived` count successfully transmitted or admitted complete
messages using the carrier's charged-byte basis. Baseline FLO counts UADP body
bytes; direct ordered delivery includes its five-byte DATA header and direct
datagrams include their 24-byte prefix. Legacy native records include their
13-byte ordered or 17-byte datagram framing. These counters exclude transport and
UASC security overhead, Services, controls and pre-transmission queue-expiry drops.

### Connection and Session ownership

A flow borrows its `Context`; owning one flow does not authorize disposing the
association used by other flows. The UASC connection or native transport owner
owns that association.

To create a Session on Shared UASC, use the ordinary session factory's
transport-taking `Create` overload with `connection.Services`, then open the
Session. A configuration-only `CreateAsync` opens another transport and does not
demonstrate sharing. The samples check transport identity and SecureChannel IDs
as well as actual Read/Publish traffic.

Finish the flows before disposing their transport. When closing only a shared
Session, use `CloseAsync(..., closeChannel: false)`; its later disposal still owns
the supplied transport. The samples close the Session, close the connection,
then dispose the Session. Certificate handles passed to transport settings are
owning handles: use independent `AddRef()` handles when retaining the originals,
and release handles not transferred during failed establishment.

After native WebTransport ingress is attached, readiness failures retain the
HTTP/3 or CONNECT rejection instead of being masked by the transport's close
token. Caller cancellation and the configured establishment deadline remain
independent.

## Optional model and Session authorization

Configured authorization uses an explicit `IDataChannelAuthorizer` on both peers.
Server applications can register exact resources and an `IDataChannelSourcePolicy`
with `DataChannelSourceRegistry` and `ServerDataChannelProvider`. Unknown resources
are denied. Configured flows do not become dependent on Sessions merely because
the application also exposes an ordinary server.

Unregistering a resource immediately revokes its active configured flows without
closing their shared data context or affecting flows for other registered
resources. For an in-place policy change, update the policy and call
`RevokeConfiguredFlows(resourceUri)`; this revokes current configured flows while
keeping the resource available for new admissions under the updated policy.
Re-registering an unregistered URI also creates a new admission lifetime;
existing flows remain revoked and must be opened again. Neither operation
closes a Session or substitutes for Session-flow reauthorization.

The independently enabled model provides the generated manager, sources,
capabilities, offers, events and per-Session diagnostics. The manager's
`OpenDataChannel`, `ModifyDataChannel` and `CloseDataChannel` are **Methods invoked
through normal Call**, not request/response Service types.

`DataChannelSessionControl` uses the generated manager proxy. Given an ordinary
Session, a `SessionDataChannelAuthorizer` wrapping the local resource policy and
the same clock used by that authorizer:

```csharp
var control = new DataChannelSessionControl(session, authorizer, clock);
DataChannel flow = await control.OpenAsync(
    context, sourceNodeId, parameters, cancellationToken);
```

The helper reserves an ID, obtains and validates the single-use grant, and waits
for actual flow readiness. Both peers still enforce resource policy. Session
permissions are additional checks, not replaced by application policy.
`control.ModifyAsync` stages the complete update through Call and waits for its
data-path outcome; the Call response alone is not a committed revision.

Grants are bound to the authenticated context, peer, challenges and ChannelId.
They do not carry an AuthenticationToken or private key. A disconnected control
transport is not a closed Session. Session expiry, closure or revoked
identity/permission invalidates its grants and dependent flows; configured
neighbours remain independent. Context loss invalidates all flows on that context.
Reconnect requires fresh context identity, IDs and authorization.

The provider owns one accepted-flow router. Consumers select their exact
resource/protocol/carrier through that router; they must not race separate
`Context.AcceptAsync` calls against it.

## PubSub over the flow

The adapter in `Opc.Ua.PubSub.Transports.DataChannels` implements the existing
`IPubSubTransportFactory` / `IPubSubTransport` contracts. It reuses Part 14
**1.05.06** writers, readers, metadata, diagnostics, encoders and security.
This feature does not upgrade the Part 14 version.

Configure the connection with `DataChannelPubSubConfiguration.Configure` or
`PubSubConnectionBuilder.WithDataChannel`. Register the provider using
`PubSubApplicationBuilder.UseDataChannelTransport` or
`IPubSubBuilder.AddDataChannelTransport`. See the
[configuration example](../src/Opc.Ua.PubSub/NugetREADME.md#brokerless-datachannel-transport)
and [PubSub guide](PubSub.md#data-channels-experimental).

The PubSub mapping requires UADP; JSON NetworkMessages are not supported on
Data Channels. The carrier URI cannot choose the encoding. One complete
encoded NetworkMessage is one flow payload, with every byte preserved.
ChannelId never replaces PublisherId, WriterGroupId, DataSetWriterId or
NetworkMessageType. PubSub reader routes associate ordinary DataSet
NetworkMessages and writer-addressable discovery responses with their
configured writer identity. Discovery requests and publisher-only responses
use the configured PublisherId and NetworkMessageType. Discovery requests
also require a visible subtype. Publisher-only responses are admitted only
when the PublisherId selects one configured resource; ambiguous matches are
rejected.
Multiple writers can share an authorized flow; different resource permissions,
delivery contracts or lifetimes need separate flows.

The optional UAFX configuration bridge uses the standard
`DataChannelProfiles.PubSubUasc` profile. `ConfigureUafx` validates an
`opc.tcp` or `opc.wss` address, null transport-specific settings at the connection,
group and DataSetWriter/DataSetReader scopes, and a local
ordered/confidential UADP policy with `application/octet-stream`; it does not
serialize that policy into PubSub `ConnectionProperties`. Register the shared
UASC flow provider and a local policy resolver with
`IPubSubBuilder.AddUafxDataChannelTransport`; server resource registries can
project the same local policy with `PubSubDataChannelSource.CreateUafx`. The
resolver must return the stable preprovisioned policy snapshot for the
selected connection; runtime changes require a newly configured connection.
Caching that policy does not skip revalidation of the current PubSub
configuration when resolving parameters or creating a flow request.
Every configured writer and reader group must have a finite
`MaxNetworkMessageSize` no greater than the local flow limit. That policy is
validated before the flow is opened. The bridge rejects type-3 application
payloads and message-settings objects from non-UADP models rather than
substituting an encoding or ignoring incompatible settings. Transport-specific
network service requests are rejected even on disabled components, before
changing the profile or creating a runtime transport. This is only the
transport/config bridge: Part 81 ConnectionManager procedures,
endpoint ownership, role claims, authorization, status, persistence and
recovery remain unimplemented, so this API does not establish UAFX conformance.
The [UAFX model package](UAFX.md) supplies the standard generated Part 81
contracts independently of this transport bridge.

The PubSub adapter rejects `AllowQueueExpiry=true`. Dropping a key frame or
metadata can invalidate later messages, so enabling expiry requires a
Publisher-side dependency and recovery policy that this adapter does not yet
provide. It neither silently clears the option nor parses or rewrites payloads
to simulate recovery. UADP delivery is unchanged.

Carrier encryption is independent of optional Part 14 message security.
SecureChannel protection alone requires no broker or SKS. Existing UADP
signing/encryption remains available through its normal key provider.

## Optional native Client/Server lane

Native configured flows do not create UASC or Sessions. An application can
explicitly select `DataChannelProfiles.ClientServerResource` for an ordered,
duplex, confidential, opaque flow with no queue expiry. The common byte adapter
carries one complete UACP/UASC chunk per DATA message and reuses ordinary
Client/Server dispatch inside it.

This separate lane is a legacy composition, not the Part 31/32 baseline. The
current drafts carry UACP/UASC Services on the reliable baseline stream and
define direct payload delivery as an additional capability.

The lane, optional model and Session authorization are separate choices. See
[Client/Server lane integration](DataChannelClientServer.md) for direct and DI
composition and its native prerequisites. Do not start competing accepted-flow
readers when hosting the lane beside PubSub or other resources.

## Wire and resource boundaries

Native ordered streams and datagrams use the common record header. A final
UASC FLO body is exactly one unchanged UADP NetworkMessage; its first byte is
the first byte of that message, with no ChannelId or common-record prefix.
Final FLC carries Revision, OperationId, then the CONTROL opcode and ordered
little-endian fields. The binary codec implements opcodes `0..17`, including
direct-binding opcode 15 (binding proof), 16 (ASSOCIATE), and 17
(ASSOCIATE-ACK). The selected direct runtime verifies exporter-derived
proofs, confirms opaque direction tokens and gates payload admission on
ASSOCIATE/ACK/READY. The managed QUIC baseline does not expose this capability.
An exact duplicate binding request replays its recorded proof or STATUS outcome,
without resolving the exporter again. A request-bound refusal remains final for
that transaction even if exporter availability changes. Conflicting proofs or
STATUS outcomes close the affected context; an unchanged Unsupported outcome
leaves baseline Services and configured FLO available.
When the context initiator has no exporter provider or cannot produce
exporter material, it sends a connection-scoped `STATUS(Unsupported)` over the
protected UASC baseline. The acceptor records that result and may open a
baseline FLO without waiting for an impossible direct proof.
Implemented
controls use OPC UA Binary Guid layout and strict length-prefixed UTF-8 strings;
HELLO carries an exactly 32-byte challenge.
Booleans are `0` or `1`; rates must be finite and nonnegative, with
both zero representations treated as unlimited for ordered delivery. Datagram
rates must be strictly positive. A Configured OPEN has no grant bytes;
a Session OPEN has exactly one Guid. There is no JSON CONTROL fallback.

The control-only AdmissionId retains the model's ChannelId name. It is not
serialized before UADP and is not a PublisherId. Configured UADP associations
select the admitted resource from authenticated identity and visible source /
writer fields. Both UASC profiles use zero RequestId; FLO and FLC are separate
message types. Legacy native datagrams carry an external ChannelId and common record.
Authenticated direct datagrams instead carry a 16-byte UA Binary Guid token,
UInt32 revision and UInt32 nonwrapping per-direction sequence before unchanged
UADP. Their controls remain on protected baseline FLC, never in a datagram.
A temporary direct datagram receiver registration failure is flow-scoped.
The failed receive worker retires before returning the failure, so a later flow
can retry registration on the same context without reusing a stale refusal.
The runtime rejects malformed framing, noncanonical control values, invalid
association/stream ownership and premature application data; applications do
not implement a second parser.

Every baseline UASC FLO admission uses additive DATA credit, including
Dedicated UASC. Credit starts at zero and counts exactly the UADP NetworkMessage
body bytes, excluding UASC headers and security overhead.
Native datagrams have 17 bytes of DATA overhead; native ordered streams have
13. UASC DATA has no flow-record overhead. Native ordered flows backpressure at the record header before allocating
the body. Physical UASC ingress reserves a bounded context/application queue
and waits at most `DrainTimeout` for DATA queue capacity. If the application
does not drain it, the affected flow fails instead of indefinitely preventing
later management processing. Native datagrams have finite admission, rate and
replay budgets rather than stream-credit semantics.

Direct datagrams charge their complete 24-byte prefix plus UADP body against
rate, burst and queue limits. Admission clamps message size to the actual
path/session capacity minus that prefix; later path-size reductions cause
explicit refusal rather than stream fallback. Unknown tokens, wrong directions,
invalid revisions, duplicates and unauthorized sources are discarded before
application-message allocation. A full application queue drops the datagram
without waiting or consuming its replay sequence. Revision ACK gates new DATA;
old-revision cutoff and bounded grace use the existing replay window. Sequence
exhaustion faults the flow and requires new admission, never wrapping to zero.
`IDataChannelDirectDatagramProvider` is forwarded through the UASC byte transport
and accepted-connection wrappers. The WebTransport pump uses a single bounded
receive rendezvous and never waits for the legacy standalone context on a UASC
baseline. Its context worker reserves one complete receive buffer and operation
until context closure; unsolicited input without a waiting receiver is dropped.

Native parser staging, incomplete records, queued messages and application-held
leases share one application budget. DATA queue handoff transfers the existing
ingress allocation and reservation; control admission covers overlapping encoded
and decoded-value storage. Both new revision directions stay reserved until
their directional barriers complete, and retained old messages keep their old
revision charged.

Each context withholds **512 bytes and four message slots** for fixed-field
management/control progress, including a directional revision exchange and its
CREDIT/ACK replies. DATA and generic carrier allocations cannot borrow this
headroom. Unused headroom is not reported as retained `BufferedBytes` or
`Messages`; it returns when the context closes. Larger establishment controls
still need additional application admission. Native streams also reserve their
two 13-byte parser staging areas before allocation.

UASC FLO and FLC chunks are final chunks and use the same per-sender security sequence
as MSG/OPN/CLO. Numbering and writer admission remain atomic. The unsealed writer
selects a pending control after at most eight consecutive Service messages;
Services remain ahead of DATA. Already numbered or in-flight writes, including
coalesced writes, cannot be reordered. FLO CONTROL on opc.wss is binary
application data, not an RFC6455 control frame. These policies imply no
wall-clock latency or one-frame Service-delay guarantee.
Benchmark results include their actual load, pacing and loopback setup;
numbers from the discarded framing are not evidence for this protocol.

## Native deployment

The optional native bindings target .NET 8 or newer. Native execution qualification
uses .NET 10 on Windows x64 and glibc Linux x64. The backend packages and their
deployment contracts are:

- [Native.QuicGo](../src/Opc.Ua.Native.QuicGo/NugetREADME.md): pinned quic-go,
  mutual-authentication and confirmed-handshake evidence, real datagrams and
  reliable-reset observation. Load one compatible Go image for the process
  lifetime; never unload it.
- [Native.Http3](../src/Opc.Ua.Native.Http3/NugetREADME.md): the separate bounded
  draft-16 HTTP/3 state engine using the same QUIC association, not a second
  QUIC/TLS stack.

Select exact reviewed native paths and expected SHA-256 values. The streaming
sample documents `OPCUA_QUIC_NATIVE_PATH`, `OPCUA_QUIC_NATIVE_SHA256`,
`OPCUA_HTTP3_NATIVE_PATH` and `OPCUA_HTTP3_NATIVE_SHA256`. Missing prerequisites,
wrong ABI/layouts, hash mismatch or insufficient authentication fail explicitly;
there is no PATH search, automatic download or carrier downgrade.

The certificate bridge uses certificate-manager identities and explicit peer
policy. The current native key interface requires supported exportable software
keys; unsupported HSM/non-exportable signing is refused, not replaced by another
identity. Compiler OS/glibc floors are not proof of execution on the minimum host.
Read each package's runtime and license notices before redistribution.

## Migrating from the earlier experiment

**Upgrade both peers together.** There is no compatibility mode for the old draft.
Ordinary stable Client/Server Services and non-flow PubSub transports are not
replaced by this change.

| Earlier experiment | Replacement |
| --- | --- |
| JSON CONTROL and UASC common-record DATA/CONTROL envelope | Unchanged UADP in final FLO; typed Revision/OperationId/opcode fields in final FLC; RequestId zero |
| New Open/Modify/Close Service requests and responses | Generated manager Methods through ordinary Call; configured flows need no Call |
| `DataChannelManager`, direct registry access and response-sent callbacks | `DataChannelContext`, carrier factories and ready `DataChannel` facades |
| Frame flags, fragments, GAP and old delivery modes | Complete messages, Ordered/Datagram delivery and transactional revisions |
| Source/sink-relative directions | Opener-relative directions |
| `channel.Write(...)` and frame queues | Await `SendAsync`; inspect its result and dispose each received message |
| Implicit UASC control connection on `opc.quic`, ALPN `opcua/1` | Native mTLS flow association, ALPN `opcua-flow/1`; explicit optional Client/Server lane |
| Old experimental NodeIds and custom StatusCodes | Regenerated model identifiers and `DataChannelResult` |
| Sample `--transport`, direct-manager mode and old frame counters | `--carrier`, finite complete-message runs and validated received-byte summaries |

Regenerate model-dependent code and use generated constants; do not transplant
the old numeric IDs or status mappings. Re-provision endpoint/profile selection
and resource policies. Existing transport connections, grants and channel IDs
cannot be transferred into the new protocol.

## Validation and current limits

The common codec/runtime tests live in `Opc.Ua.Core.DataChannels.Tests`.
Physical UASC tests, optional model/control tests, PubSub tests, native binding
tests and real-process sample tests exercise their own layers. Passing a codec
suite alone does not establish carrier or application interoperability.

```powershell
$env:CustomTestTarget = 'net10.0'
dotnet test tests\Opc.Ua.Core.DataChannels.Tests\Opc.Ua.Core.DataChannels.Tests.csproj -f net10.0
dotnet test tests\Opc.Ua.PubSub.Samples.Tests\Opc.Ua.PubSub.Samples.Tests.csproj -f net10.0
```

The existing `Opc.Ua.Aot.Tests` executable also includes native-free
`DataChannelsAotTests`. These exercise direct and DI context construction,
binary CONTROL, complete DATA, revision-window ownership and unsupported
control rejection through the public runtime APIs. After publishing that
project with NativeAOT on Windows, run the focused cases with:

```powershell
.\tests\Opc.Ua.Aot.Tests\bin\Release\net10.0\win-x64\publish\Opc.Ua.Aot.Tests.exe --treenode-filter "/*/*/DataChannelsAotTests/*" --report-trx
```

These TUnit cases use an in-process verified-plaintext UASC seam. They do not
load QUIC/HTTP3 backends or qualify transport authentication, native resource
retention, or the native Client/Server lane. The AOT project's executable
runner is separate from the NUnit `dotnet test` commands above.

The separate `Opc.Ua.Bindings.WebTransport.Aot` host exercises real native
QUIC/HTTP3 ownership. Its TLS-only baseline checks retain final direct-stream
bytes through sender disposal for both openers and verify surviving UACP.
Those transport-primitive checks do not replace authenticated baseline UASC
Services or actual browser qualification.

See the [Developer Guide](DeveloperGuide.md) for full-solution framework runs,
coverage and the dedicated required Windows/Linux native workflow. Native tests
require their actual assets and fail when prerequisites are absent; a skipped
or empty test run is not native acceptance.

### Current limitations

- **Baseline conformance is incomplete.** The managed QUIC baseline carries
  Services and configured FLO/FLC. Explicitly selected native QUIC/WebTransport
  baselines carry Services and authenticated direct flows, using transport-only
  TLS independently of UASC authentication. Actual browser interoperability,
  complete QUIC/WebTransport profile coverage and the full normative
  Parts 30–32 assertion matrix remain open.
  The Part 32 WebTransport Baseline Facet requires both `WT-UASC` and
  `WT-Browser`; browser support is not an optional conformance unit.
  Native interoperability alone cannot establish that facet's conformance.
  Direct delivery is an optional unit in both transport baseline facets.
- **Exporter-unavailable QUIC baseline fallback is implemented.** A managed
  initiator explicitly declines optional direct binding with protected
  `STATUS(Unsupported)`, allowing a native acceptor to open configured FLO.
  The native server-opener case passes in the QUIC binding suite; a delayed
  proof still times out rather than being treated as an unsupported exporter.
- **Legacy native flow endpoints remain separate.** Their `opcua-flow/1` and
  `opc.wt` management protocols and optional Client/Server lane are not the
  Part 31/32 baseline profiles and are not a fallback for baseline negotiation.
- **Direct delivery is only partially integrated.** The common runtime implements
  protected binding, exact association replay, direction/token ownership and
  ACK/READY gates for actual ordered streams and direct datagrams. Windows and
  Linux native QUIC/WebTransport exercise both with UASC Services before and after direct
  DATA, revision and END. Current Windows native QUIC and WebTransport cases also
  exercise minimal opaque streaming in all directions and both opener roles,
  plus A/V ordered/datagram delivery with unchanged type-3 NetworkMessages.
  Native QUIC exercises both streaming profiles under NativeAOT and managed-client
  shared-FLO fallback with pre-READY typed receive admission.
  Browser interoperability, final native qualification and
  the full source-specific transport negative matrix remain open.
  The direct ordered-frame codec preserves complete UADP DATA and encodes
  COMMIT/END barriers with the revision, operation identifier and binary control
  body. Their minimum complete frame sizes are 22 and 18 bytes respectively;
  the eight-byte revision/operation header alone is not a valid barrier.
  Ordered COMMIT uses a zero lastOldMessageId; the active revision follows its
  position in the actual sending stream. Non-sending directions require no
  invented reverse stream. A barrier overtaking secured ACCEPT-UPDATE waits for
  that acceptance. Direct DATA is charged for its five-byte frame header and
  body; decoder ownership transfers into the application message without a
  second copy or message slot. Barriers use independent control headroom.
  Direct stream providers whose disposal may abandon accepted bytes implement
  `IDataChannelDirectStreamCompletion`. The runtime supplies encoded END as its
  final write before orderly release; explicit RESET does not use that barrier.
  Native QUIC closes with FIN and releases handle ownership without cancelling
  outstanding sends. This does not depend on the optional RESET_STREAM_AT extension.
  WebTransport retains that final native write, and all preceding bytes, through
  reset-based disposal. Fully retained peer reset is delivered after its bytes,
  and termination of the unused reverse half does not abort the active direct
  direction. Current Windows native QUIC/WebTransport and Linux native QUIC
  retention checks pass. Browser, broader Linux integration and full profile
  acceptance remain open.
- **Part 30 conformance is incomplete.** The passing implementation test suites
  cover the current branch behavior only; the source-revision-specific
  assertion matrix has not yet been fully mapped to executable evidence.
  Minimal streaming now reserves typed receive associations before READY,
  including provisional-flow conflicts, stable route snapshots and ordinary
  UADP writer-set preservation. This bounded common-runtime and actual reverse
  TCP evidence does not establish the complete transport assertion matrix.
- **UAFX integration is partial.** The standard `pubsub-uasc` profile, strict
  local-policy validation and an opt-in shared-UASC PubSub transport bridge are
  implemented. The [UAFX model package](UAFX.md) supplies generated Part 81
  contracts. ConnectionManager/AutomationComponent configuration,
  endpoint ownership, authorization, status, persistence and recovery are not.
  This does not establish the Data Channel UAFX Binding Facet or a UAFX role
  claim. The binding selects ordered, confidential PubSub over UASC; it does
  not apply to QUIC, WebTransport or type-3 streaming payloads.
- **Configured UADP routing is partial.** Receiving PubSub flows register each
  reader's PublisherId, WriterGroupId, and DataSetWriterId against the exact
  resource URI. Visible UADP source and writer fields and NetworkMessageType
  select the configured resource. ChannelId is control-only and is not
  serialized before UADP in FLO. A multi-writer NetworkMessage is admitted only when every visible
  DataSetWriterId resolves to the same resource; unmatched or ambiguous traffic
  is rejected rather than routed by arrival order. Metadata and
  writer-configuration discovery bodies are also
  matched when they expose DataSetWriterIds. Reader-derived routes include all
  six discovery request subtypes, selected by PublisherId, NetworkMessageType
  and visible subtype; writer selectors remain for requests that expose writer
  identities. Publisher-only responses use the configured PublisherId and
  NetworkMessageType when they select one resource; ambiguous matches are
  rejected. Metadata and writer-configuration responses retain writer-based
  matching. Callers can register additional explicit PublisherId,
  NetworkMessageType and discovery-subtype selectors. Request
  subtype values 1–6 identify PublisherEndpoints, DataSetMetaData,
  DataSetWriterConfiguration, ApplicationInformation, PubSubConnection and
  Probe, in that order. Response subtype values are 1–5; Probe has no response
  subtype. A secured header hides the subtype;
  when a matching subtype-specific association exists, the context rejects that
  message instead of falling back to a broader route. The
  [UASC discovery routing tests](../tests/Opc.Ua.Core.DataChannels.Tests/Runtime/FlowDeliveryTests.cs)
  cover an empty-writer discovery request, PublisherEndpoints and metadata
  responses, and rejection of secured traffic when the subtype is hidden.
  This does not establish full source/writer association compliance or
  conformance to the current Data Channels source. Type 3 is accepted only on
  its explicitly selected A/V or opaque application profile; it is not admitted
  by an ordinary UADP or PubSub reader route. Explicit type-3 route registration
  rejects WriterGroupId and DataSetWriterId selectors because those fields are
  absent from the supported minimal streaming headers. Ordinary UADP writer
  selectors remain supported.
- **WebTransport peer-STOP retention is implemented in the pinned QUIC backend.**
  Its overlay preserves an established reliable boundary and outstanding prefix
  frames when peer `STOP_SENDING` arrives before local cancellation. A local
  reset that already owns the send direction is not replaced; streams without
  a reliable prefix retain ordinary reset behavior. The native WebTransport
  loopback test
  [`AcknowledgedPrefixRemainsReliableAfterPeerStop`](../tests/Opc.Ua.Bindings.WebTransport.Tests/WebTransportNativeLifecycleTests.cs)
  covers this path. The [QUIC peer-STOP regression](../tests/Opc.Ua.Native.QuicGo.Tests/native/peer_stop_regression.py)
  checks both opener roles, delayed peer STOP, crossed resets and a zero-error
  STOP. These tests do not establish browser interoperability or full Part 32
  conformance, and native qualification still requires the reviewed assets.
- Native release signing, final-artifact advisory/SBOM evidence and minimum-host
  acceptance are separate gates. Local unsigned validation packages are not
  approved product distributions.
- Browser interoperability, ARM, musl, macOS native assets and non-exportable
  native signing keys are not claimed. The audio sample's hardware playback is
  Windows-only; headless WAV output is separate.
- Optional Client/Server lane integration and coherent final-dependency
  validation must be completed before claiming full rewrite acceptance.
  Historical sample results do not qualify a replacement native binary.

## References

- [Part 30: PubSub over UASC](https://github.com/OPCF-Members/OPC10000-Core/tree/25250437f7086f173e825a6720476c01abe9e3af/source/data-channels)
- [Part 31: OPC UA over QUIC](https://github.com/OPCF-Members/OPC10000-Core/tree/25250437f7086f173e825a6720476c01abe9e3af/source/quic)
- [Part 32: OPC UA over WebTransport](https://github.com/OPCF-Members/OPC10000-Core/tree/25250437f7086f173e825a6720476c01abe9e3af/source/webtransport)
- [Shared transport contract](https://github.com/OPCF-Members/OPC10000-Core/blob/25250437f7086f173e825a6720476c01abe9e3af/source/transport-common/contract.md)
- [PubSub integration](PubSub.md#data-channels-experimental)
- [Optional native Client/Server lane](DataChannelClientServer.md)
- [Certificate manager and secret store](CertificateManager.md)
- [Contributor build and test guide](DeveloperGuide.md)
