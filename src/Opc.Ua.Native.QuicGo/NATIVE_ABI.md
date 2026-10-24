# Native QUIC ABI7 and reproducible packaging

`native\include\opcua_quic.h` is authoritative. All ten production `opcua_quic_*`
exports are Cdecl; scalar widths are fixed, x64 natural alignment is eight.
No Go pointer or native-owned payload pointer crosses the boundary. Handles are
distinct typed 64-bit identities, not pointers, and never recycle within the process.
The ABI descriptor reports every layout; managed loading fails closed on a mismatch.
The legacy proof's pair/suite/identity-generation exports are **not** included.

| Structure | Bytes |
| --- | ---: |
| handle | 8 |
| bytes | 16 |
| identity | 96 |
| peer rule | 32 |
| config | 160 |
| request | 48 |
| result | 112 |
| connection info | 920 |
| counts | 40 |
| ABI descriptor | 32 |

## Ownership and bounds

Begin validates/copies bounded input, admits one job, and returns without waiting for
network, TLS or a free queue slot. Worker operations have deadlines of 1..60000 ms.
Poll returns pending, short-buffer, or terminal observation. Terminal operation failure
is in `result.status`, **not** poll's return. Short buffers do not consume/observe.
Repeated terminal polls are stable. Release refuses pending or unobserved jobs.
Cancel requests interruption; it never abandons buffers or revokes a returned result.

- 128 process-wide outstanding jobs including unreleased terminal results. This accommodates
  concurrent passive H3 read/STOP/accept watches without dropping real evidence; callers still
  enforce their own per-connection admission/reserved write capacity.
- 4 MiB conservative operation byte reservations, independent of job count.
- 8 listeners; 32 native connections including inbound handshakes and unaccepted peers.
  Address validation uses native QUIC Retry before inbound connection admission.
- 256 stream handles and 1..32 incoming streams per direction per connection.
- Stream receive windows: fixed 64 KiB; connection receive window: fixed 256 KiB.
- Read/write payload: at most 64 KiB; 1..32 application policy rules.
- Certificate/key/trust bundles: at most 16 KiB each; at most 16 certificates.
- Bounded native datagram queues are quic-go's reviewed fixed queues. A blocked native
  datagram Send has no per-datagram cancellation primitive: cancellation/deadline closes
  **that connection** to unblock it. Receive cancellation does not close the connection.
- Trace storage retains at most 256 stream facts per connection, not packet history.
  It stores only current sent high-water, sent/received reset metadata and the first
  actual peer STOP_SENDING. Releasing
  a stream removes its facts; allocator high-water rejects late recreation of them.
- Persistent native state is separately bounded by the handle/handshake/window limits.
  `retained_bytes` is not a claim to measure all Go/TLS/socket memory.

One read and one write can operate concurrently on a stream. A reset cannot overtake
an active write; release cannot invalidate any referencing job, even an unreleased
terminal job. Connection close interrupts its native I/O, but connection release
requires all child streams and referencing jobs to be released. Listener close drains
unaccepted peers and releases its own socket reference; accepted connections retain
their transport independently until they close. Counters can be used to verify complete
shutdown (allowing finite background transport cleanup).

## Security and transport evidence

The default application-mTLS endpoint has a supplied leaf, intermediates, private key, trust anchors and local
URI. Every peer rule requires an exact certificate URI SAN **and** SHA256 SPKI pin.
TLS performs ordinary chain/lifetime/EKU validation, plus connector hostname validation.
Unknown roots, malformed material, missing key capability and empty policy fail closed.
The listener's rule array is explicit and extensible to multiple applications/key rotations;
there is no single hardcoded client and no authorization based on application records.
Policies are immutable snapshots. Providers must enforce any additional revocation,
trust-list or authorization rules before supplying a snapshot and close affected live
connections when such policy changes. Online revocation fetches are not implemented.

TLS 1.3 only; no tickets, resumption or early/0RTT endpoint API. Successful connector
admission requires observation of a CertificateRequest, selection of the configured local
certificate, completed TLS Certificate/CertificateVerify/Finished processing, verified expected
peer identity, and authenticated HANDSHAKE_DONE receipt. A pinned/trusted server that omits
CertificateRequest is refused, not treated as mutually authenticated. Server admission requires
its own per-connection certificate selection and native post-client-auth handshake completion.
Higher-layer application acceptance/authorization remains separate.

The ABI2 layout, retained by ABI3 through ABI6, appends independent observations to the first 904 info bytes:
`handshake_confirmed` at904, `mutual_tls_authenticated` at908, `local_certificate_used` at912,
and the admitted `authentication_policy` at916. Offset16 remains **local TLS HandshakeComplete**, not QUIC
confirmation. Managed loading requires ABI6/info920. ABI1 lacks the authentication facts,
ABI2 lacks the independent received-reset watcher, and ABI3 lacks the actual peer-STOP
watcher; none is silently assumed. The ten export signatures and structure sizes are
unchanged by these additive ABI revisions.

ABI7 / operation revision7 adds stream operation31, `ReleaseGracefully`. It
closes the send direction with FIN, cancels only the unused receive direction,
and releases the managed native handle without `CancelWrite`. The QUIC connection
retains outstanding stream data and FIN until acknowledgment or connection close.
Ordinary stream `Release` remains abortive. Direct byte streams use graceful
release only after a complete final write carrying FIN; explicit reset, partial
write failure and uncompleted streams retain the abortive path. This behavior
does not depend on RESET_STREAM_AT negotiation.

ABI5 / operation revision5 adds connection operation19, `ExportKeyingMaterial`.
The request payload is exactly the 32-byte Part 31 binding transcript digest; the
result is the 32-byte TLS exporter output for `EXPORTER-OPCUA-QUIC-DRAFT`.
Exporter failure returns `CAPABILITY` and does not close the connection, so the
protected UASC baseline remains available. The managed connection wrapper validates
the transcript-digest length and links the operation to the owning connection lifetime.
Direct delivery must not be advertised when this operation is unavailable.

ABI6 / operation revision6 adds connection operation30, `ExportCustomKeyingMaterial`.
Its bounded payload is `[labelLength:8][ASCII label][contextLength:8][context][outputLength:8]`;
each length is one unsigned byte. It returns exactly the requested TLS exporter output.
This is used by Part 32 with TLS label `EXPORTER-WebTransport` and the serialized
WebTransport Session ID, application label and 32-byte flow-binding transcript digest.
The Part 31 fixed-label operation19 remains unchanged. Export failure returns
`CAPABILITY` without closing the connection.

Operation revision4 makes the config's final UInt32 and the info field at916
explicit authentication-policy discriminators. Policy0 preserves the full application-mTLS
contract, including its mandatory peer URI/SPKI allowlist and actual client-certificate request.
Policy1 is separately selected server-authenticated transport TLS; it does **not** authorize
OPC UA applications. A connector supplies only explicit trust roots and an expected hostname,
without a certificate, key, URI or client-certificate selector. A listener supplies its
certificate, optional intermediates and signing key, without client trust roots, application
URI or peer allowlist. Mixed-policy inputs and unknown discriminators are refused before
native operation admission.

Policy1 still requires ordinary server chain/lifetime/EKU/hostname validation, TLS1.3
and fresh confirmed QUIC. Datagrams and reset09 are independently observed optional
capabilities, not prerequisites for a reliable UASC baseline. Absent datagram transport
parameters and unavailable datagram payload capacity are reported as zero, not as the
native implementation's negative sentinel converted to an unsigned value. Managed loading
checks the consistency of these flags, transport parameters and payload capacity.
Policy0 retains its mandatory datagram/reset09 contract. The ABI6 layout is unchanged.
Policy1 info has no peer application URI
and `mutual_tls_authenticated` is zero in both roles. A client reports the actually verified
server chain/hashes and no local certificate use. A server reports actual local certificate
use, zero verified client certificates and zero peer hashes; a certificate query there returns
Invalid, never a fabricated identity. `NativeQuicTlsEvidence` and `NativeQuicPeerEvidence`
are disjoint checked types: neither accepts the other's authentication policy.
UASC must independently establish application authentication and authorization over a
transport-only baseline. These native facts alone do not establish Part32/browser conformance.

Peer facts return the actual verified certificate chain, authorized URI, leaf/SPKI hashes,
ALPN and TLS/transport facts. Exact **remote** TP `0x1d` comes from the observation overlay,
not the native merged draft07/09 capability. Local emission is the source-pinned native
encoder, golden-tested for empty TP `0x1d`. No wire encoder/decoder behavior is patched.
Typed `HandshakeConfirmed`, `MutualTlsAuthenticated` and `LocalCertificateUsed` properties
reflect those new native fields. Typed reset09 flags map to empty TP `0x1d` and frame `0x24`;
that protocol mapping is not an observation that a reset has already been transmitted.

## Stream completion and reliable reset

Native allocation supplies real bidirectional/unidirectional IDs. For H3, the host reserves
client bidi0 for CONNECT and three actual uni IDs for control/QPACK before allocating
application streams. This backend does not reserve or interpret H3/flow records.

Write completion reports **actual accepted bytes**, including partial errors. FIN is
reported successful only after the native send Close succeeds. Boundary flag2 writes
the entire supplied prefix, then calls native `SetReliableBoundary` before returning.
Immediate cancellation cannot overtake this marking. A failed partial prefix closes
that connection rather than emitting an unassociated reset. Reset/close outcomes never
derive final size from leased payloads.

`WaitResetSent` waits for the actual **local** native packetizer frame and returns final
size, reliable size, application error and original native encoder codepoint `0x24`
or `0x04`. An ordinary reset can follow an actual peer STOP_SENDING even after a reliable
boundary was established; see the policy limitation below. Reads return data before FIN/reset outcome.
Received reset metadata is independently observed; it is not substituted for local
send confirmation. `SentOffset` reports transmitted STREAM high-water, not Write counts.

`WaitResetReceived` (stream operation28, operation revision2) independently waits for actual
peer RESET_STREAM/RESET_STREAM_AT metadata already captured by the native packet recorder.
It can run while Read is paused by application backpressure, or concurrently with Read/Write.
It returns `Remote=true`, reset outcome, actual error/final/reliable/frame values and no payload.
It neither consumes bytes nor cancels either direction; reliable prefix bytes may still be
buffered and must retain their normal ownership/processing rules. It is distinct from
`WaitPeerStopped`, which observes a peer STOP_SENDING directed at the local send half.
Cancelling/timing out this watcher produces no invented EOF/reset evidence.

Operation28 was introduced with ABI3/revision2. Keep the watcher bounded and drain/release
it before releasing its stream.

### Actual peer STOP and crossed resets

`WaitPeerStopped` (stream operation29, operation revision3) observes the first authenticated
received STOP_SENDING frame for a send-capable stream. Success returns `Remote=true`, the
actual stream ID and peer application error, including error zero. Payload, byte count,
final size, reliable size and reset frame type remain zero: a STOP is not a reset.
It is independent of Read, Write and local send cancellation. A local Reset or FIN cannot
complete it as peer evidence. Cancellation, timeout or connection closure without a
recorded STOP fails without inventing a peer error. Repeated completed observations retain
the first peer error, and concurrent same-kind watchers are bounded by the normal Busy rule.

The older `WaitStopped` operation25 is retained as a send-context observer. It reports the
first cancellation cause, which can be local. If local Reset wins first, a subsequent peer
STOP cannot replace that context cause. Use operation29 for actual peer STOP, including
crossed shutdown; do not interpret the legacy local context result as remote evidence.

ABI4 introduced the runtime gate for operation29. The current managed loader
requires ABI7 for graceful stream release, while preserving the ABI6 TLS/exporter
capabilities. Receipt revision3 recorded the operation29 introduction; revision4
additionally records the explicit TLS policy; revision5 records the transcript-bound
QUIC exporter; revision6 records bounded custom-label exporter support; revision7
records graceful stream release. Receipt provenance
does not replace the runtime ABI gate. Export signatures and structure sizes remain unchanged.

### STOP and application reliable-minimum policies

The pinned quic-go STOP_SENDING handler is overlaid to preserve an established reliable
boundary. If STOP_SENDING arrives before local cancellation, the response reset retains the
reliable prefix and retransmits any outstanding prefix frames. If a reset already owns the
send direction, the later STOP_SENDING does not replace or downgrade that reset. A stream
without an established reliable boundary still uses an ordinary reset. The recorder reports
the actual frames; it never substitutes the earlier reliable boundary or received reset metadata.

[Reliable-reset draft09 section5.4](https://www.ietf.org/archive/id/draft-ietf-quic-reliable-stream-reset-09.txt)
recommends ordinary RESET_STREAM in response to STOP_SENDING. The
[WebTransport draft16 section4.4](https://www.ietf.org/archive/id/draft-ietf-webtrans-http3-16.txt)
requires a reliable size at least as large as the WebTransport header when resetting data
streams. The pinned adapter policy preserves the WebTransport requirement while retaining
ordinary reset behavior for streams with no reliable boundary.

### Late application-error cancellation

An H3 ResetSend event can arrive while a body write still owns its lease. Attach a
one-shot `NativeQuicCancellation` to the request rather than waiting for the send
before attempting the conflicting `Reset` operation:

```csharp
using var reset = new NativeQuicCancellation();
var request = new NativeQuicRequest(timeout, body)
{
    Cancellation = reset
};
Task<NativeQuicCompletion> writing = backend.StreamAsync(
    stream, NativeQuicStreamOperation.Send, request, lifetimeToken).AsTask();

// When the application learns the mapped wire error:
await reset.CancelAsync(wireError);
NativeQuicCompletion sent = await writing;
```

`CancelAsync` publishes the full 62-bit error before invoking cancellation callbacks.
It awaits **only signalling callbacks**, not the native write or its body lease.
The callback uses the existing C `opcua_quic_cancel(operation, error)` export, so it
interrupts only the send direction and leaves the native prefix critical section intact.
Passing `reset.Token` separately is optional. The first control request wins; repeat
calls cannot overwrite its `ErrorCode`. Keep the control alive until its operation drains.

The ordinary lifetime token continues using `CancellationErrorCode` unless the dynamic
control has already been requested. A cancellation already delivered to native code
remains authoritative: a later control request cannot replace that earlier wire error.
For a send that already completed, cancellation may lose the race without emitting any
reset. After draining, the host must use ordinary `Reset` if still required by stream state.
Never infer reset transmission from successful signalling.

For H3: reconcile the actual terminal write bytes with operation20, then obtain
`WaitResetSent` **local** final/reliable/error evidence for operation22. Do not release
the H3 lease early, cancel the receive half or manufacture reset metadata. No native
ABI/export/library change is needed for this managed cancellation control.

## Reproduction

Use verified public Go1.27.2 and Zig0.17.0 Windows archives from `build\native-pins.json`.
No installer or global PATH/configuration change is required. `ToolRoot` contains
`go\bin\go.exe` and `zig\zig.exe`. `CacheRoot` is an existing writable Go/Zig cache;
`QuicSourceRoot` is the exact source tree from the checksum-verified module zip.
Reuse a reviewed tree instead of duplicating a full mirror. Every source file is compared
with the verified Go module, then only the three pinned observation files and the pinned
STOP_SENDING reliable-prefix policy patch are overlaid.
Go forbids overlays directly in its module cache; an output-local small shim source tree
uses a junction to the reviewed QUIC tree with a stable `./quic-go` replacement. This
avoids embedding private source paths in native module build-info and makes no upstream
source mirror. The committed go.mod/go.sum remain canonical, without local replacement.

```powershell
.\build\Build-Native.ps1 -ToolRoot $tools -CacheRoot $cache -QuicSourceRoot $quicSource `
    -OutputRoot $output -RuntimeIdentifier win-x64
.\build\Build-Native.ps1 -ToolRoot $tools -CacheRoot $cache -QuicSourceRoot $quicSource `
    -OutputRoot $output -RuntimeIdentifier linux-x64
.\build\Pack-Native.ps1 -ToolRoot $tools -CacheRoot $cache -QuicSourceRoot $quicSource `
    -OutputRoot $packageOutput -UnsignedDevelopment
```

The scripts verify executable/source/module/overlay pins, run Go vet, build with
`-trimpath -buildvcs=false -buildmode=c-shared`, restrict/inspect actual exports and
emit path-free per-RID receipts plus asset SHA256 sidecars. Package verification
checks hashes, source receipts, all exports and a strict file allowlist; test fixtures,
keys, caches, PDBs, diagnostic paths, brand assets and private spec material are excluded.
PowerShell recipes are packaged as `.ps1.txt` inert source, never NuGet install scripts;
rename them to `.ps1` after extracting if using the recipe outside the repository.
No cross-host bit-for-bit build or independent compiler-bootstrap claim is made.

The root solution/package/workflow wiring is intentionally not performed by this package.
Parent integration must add the optional project/test project, reference it only from
opt-in bindings, invoke these build/pack scripts, and implement the certificate-manager
provider/managed flow integration. The source adoption gate does not waive downstream
signing, final linked SBOM/notices/SCA or advertised-minimum-host acceptance.
