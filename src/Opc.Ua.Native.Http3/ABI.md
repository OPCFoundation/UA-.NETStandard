# Production HTTP3 state ABI

`include\opcua_http3.h` is authoritative. The only exports are the nine symbols
in `build\exports.json`; no feasibility/fixture exports or identities are present.
`opcua_http3_abi_version()` is **1**, `opcua_http3_api_revision()` is **3**.
The operation revision retains the adopted lease/cancellation/final-size contract.
The production `create` signature is new and deliberately cannot load the old proof DLL.

| Structure | x64 bytes |
| --- | ---: |
| Transport facts | 80 |
| Event metadata | 48 |
| Resource snapshot | 64 |
| Caller configuration | 224 |
| Typed error | 48 |
| Layout discovery | 40 |

Call `opcua_http3_layout` with the exact layout structure size before any handle creation.
Fail closed on every mismatch. The interface uses C calling conventions, fixed-width
integers and `size_t` only for caller-owned slices. No `bool`, callbacks, managed objects,
reflection, cross-allocator frees, or hidden asynchronous jobs are involved.

## Authentication and configuration boundary

The **real companion QUIC backend** must supply:

- confirmed TLS 1.3, ALPN `h3`, no 0-RTT CONNECT;
- the explicitly selected TLS authentication policy described below, using the host's
  certificate-store and validation policy;
- both nonzero QUIC DATAGRAM transport parameters;
- actual reset09 TP `0x1d` with empty local and peer values, and frame `0x24`.

The native state engine does not manufacture or independently authenticate these facts.
Facts version1 retains application-pair mTLS: `mutual_tls_authenticated=1` and
`authentication_policy=0`. Facts version2 explicitly selects server-authenticated transport
TLS: `mutual_tls_authenticated=0` and `authentication_policy=1`. Both occupy the same
80 bytes; the former reserved final field is the policy discriminator. Unknown versions,
mixed policies, invented mutual authentication and missing transport capabilities fail closed.
Version2 facts must originate from the actual transport-only backend observations, not
from relabelling application-mTLS facts. Neither contract substitutes for UASC application
authentication, authorization or exporter proof.

The nonzero connection/application-pair tags are immutable host correlation identifiers,
not certificate evidence. A state test with synthetic facts is not network evidence.
The two `initial_max_*` fields retain their 80-byte-layout positions but are explicitly
**local WT credit policy**, not observed QUIC limits. Choose them from application resource
admission; do not confuse WT session credit with QUIC connection/stream flow control.

Creation requires explicit, validated UTF8 policy:

- an exact ASCII authority (lowercase DNS or IPv6, optional nonzero port);
- an exact absolute URI path, optionally including query, without fragments/control bytes;
- Origin mode **absent only**, **absent or exact**, or **required exact HTTPS origin**;
- configuration version1: exactly one nonempty application-protocol identifier;
- configuration version2: an empty application protocol, explicitly omitting private
  application-subprotocol negotiation for the reliable baseline;
- exactly **one CONNECT/session lifetime**, even after rejection or closure;
- finite local limits and three distinct, already allocated local uni stream IDs.

Strings are validated before allocation and copied during creation. No pointer is retained.
There is no default authority, resource, origin, credential, identity or inferred application protocol.
The server must explicitly accept/reject CONNECT. Acceptance rechecks exact authority,
path, Origin and offered protocol; duplicates and obsolete negotiation headers cannot
bypass policy. Only status 200 establishes this profile. Client success requires an
explicit, exact `WT-Protocol` selection in configuration1. Configuration2 emits neither
`WT-Available-Protocols` nor `WT-Protocol`, refuses offered/selected subprotocols and rejects
Cookie, Authorization and Proxy-Authorization request headers. Its exact authority, path/query,
Origin, draft and transport-capability checks remain mandatory; it does not bypass application
authentication in the independent UASC layer. A configuration1 caller cannot silently fall back
to configuration2. Configuration2 requires the actual external connection tag but does not
require an application-pair tag; zero expresses the absence of a UASC-authenticated application
pair at CONNECT establishment. Configuration1 retains its nonzero application-pair tag gate.
Both configuration versions are independent of the transport-facts version
and retain the same 224-byte layout. Event 23 reports a non-success HTTP status.
There is no redirect following, origin pooling, second session, or protocol fallback.

## Stream allocator cooperation

Use a fresh dedicated TLS connection for the selected transport policy. Reserve actual client
bidirectional stream **0** exclusively for CONNECT before allocating client data streams.
The maintained HTTP3 engine allocates CONNECT zero internally; production checks that
the host reserved the same identifier. This is an explicit integration constraint, not
a fabricated stream allocator or a general-purpose pooled HTTP server.

Allocate three local uni streams first and pass their actual IDs in the configuration.
Their IDs need not be 2/6/10 or 3/7/11, but must be distinct and have the correct role/type.
The returned initial writes establish HTTP control/QPACK streams; keep these critical
streams open until connection shutdown.

For command 12 pass the actual new local bidi stream ID. Local IDs must increase and are
never recycled, including after FIN/reset. The first client data stream is normally 4;
the first server data stream is normally 1. The maintained encoder emits the exact
`0x41` WT bidi signal plus session ID. Only the initiating direction carries that prefix.
Serialize local allocation/registration attempts and retain an allocated ID while retrying
WT credit backpressure; do not register a newer ID first and later rewind into the gap.
Remote prefixes are inspected with the maintained bounded decoder before buffering;
cross-session associations and WT unidirectional data streams are rejected.

## Calls, errors and ownership

Serialize every call for one handle, including polling, errors, snapshots and destruction.
Never call concurrently or reentrantly. Inputs are consumed/copied synchronously and may
be released after the call. The host owns TAP scheduling, clocks/timeouts, cancellation,
process-wide quotas, certificate lifetimes and transport shutdown.

- Creation/command: 0 success, -1 invalid ABI pointers/arguments, -2 typed failure,
  -3 contained panic. Destroy a poisoned handle after transport leases have settled.
- Poll: 1 event, 0 empty, 2 output capacity too small, -1 invalid arguments,
  -3 contained panic. On 2, metadata includes required length and the event is unchanged.
- `last_error`: writes typed information; 1 diagnostic copied, 2 diagnostic capacity
  insufficient, 0 no error, -1 invalid ABI pointers. No NUL terminator is appended.
  Configuration failure writes the typed error directly to `create`'s error output.
- Unknown/stale non-null pointers and invalid memory regions are caller bugs; an ABI
  cannot validate arbitrary pointers. Rust allocation abort is not catchable.

Use **scope + code + protocol_code**, not text matching. Scopes distinguish configuration,
operation, stream, session and connection failures. `protocol_code` preserves actual
HTTP3 error values; zero denotes a local adapter error. Error flags identify retryable
backpressure/credit blocking (1) and fatal connection errors (2). Identifiers use
`UINT64_MAX` for absent; session zero is valid. Diagnostics never interpolate peer
headers, credentials, identities or payload.

Retryable input backpressure consumes no input bytes. On a stream/protocol failure,
apply the indicated reset/STOP/session/connection action rather than replaying consumed
network bytes. Drain generated events even on a nonfatal stream error. A fatal connection
allows only outstanding send/reset completion accounting before destruction.

### Commands

Unused arguments must be zero. Payload is permitted only on 1, 2, 13, 14 and 16.

| Operation | Input |
| --- | --- |
| 1 | Received ordered stream bytes: `id=stream`, `arg=FIN(0/1)` |
| 2 | Actual received QUIC DATAGRAM bytes, including HTTP Quarter Stream ID |
| 3 | Received reset: `id=stream`, `arg=wire error`, `aux=peer final size`; deliver reliable prefix first |
| 4 | Received STOP_SENDING: `id=stream`, `arg=wire error` |
| 10 | Client starts CONNECT after peer SETTINGS; `id=actual reserved stream 0` |
| 11 | Server response: `id=0`, `arg=200/400/403/405/406/421/429/503` |
| 12 | Open WT bidi: `id=session 0`, `arg=actual allocated local stream ID` |
| 13 | Send opaque WT bytes: `id=data stream`, `arg=FIN(0/1)` |
| 14 | Send WT datagram: `id=session 0`, opaque payload |
| 15 | Drain session `id=0` |
| 16 | Close session `id=0`, `arg=uint32 application error`, UTF8 reason <=1024 bytes |
| 17 | Cancel/reset local send: `id=data stream`, `arg=uint32 WT application error` |
| 18 | Application consumed delivered payload: `id=0`, `arg=byte count` |
| 19 | Send GOAWAY: `id=boundary` |
| 20 | Send completion: `id=token`, `arg=cumulative accepted bytes`, `aux=flags` |
| 22 | Local reset completion: `id=stream`, `arg=actual LOCAL final size`, `aux=actual reliable size` |

### Events

| Kind | Meaning |
| --- | --- |
| 1 | Write stream bytes; `arg=lease token`, `aux=absolute offset`, flags 1 FIN / 2 association prefix |
| 2 | Send one real QUIC datagram; `arg=lease token`; no stream emulation |
| 3 | Send RESET_STREAM_AT; `arg=wire error`, `aux=reliable size`; transport owns final size |
| 4 | Send STOP_SENDING; `arg=wire error` |
| 10 | Required peer SETTINGS accepted |
| 11 | Local CONNECT started |
| 12 | CONNECT headers ready for explicit host accept/reject |
| 13 | Session established; flag 1 capsule flow control enabled |
| 14 | Peer WT bidi stream associated |
| 15 | Opaque WT stream bytes |
| 16 | Receive FIN (opposite send half may remain open) |
| 17 | WT datagram payload with HTTP prefix removed |
| 18 | Processed flow-control capsule, encoded capsule payload |
| 19 | Session draining |
| 20 | Session closed; `arg=application error`, `aux=WT wire error`, UTF8 reason |
| 21 | Received reset; `arg=wire error`, `aux=peer final size` |
| 22 | Received STOP_SENDING; `arg=wire error` |
| 23 | CONNECT rejected; `arg=HTTP response status` |
| 28 | Received GOAWAY; `stream=boundary` |

Every successful poll of kinds 1/2 transfers a **lease**, not send success.
Keep the caller buffer alive until the transport has released it. Only one ordered
write per stream can be leased; other streams, datagrams and cancellation can progress.
The prefix write must finish and the native reliable boundary must be set before
acknowledging its completion or transmitting body data.

Command 20 flags are cumulative: **0** progress; **1** released; **3** released and FIN
actually accepted; **5** released with terminal failure. Counts cannot decrease or
exceed the lease. Datagram acceptance is zero or full length. Progress retains the
whole lease quota. Identical terminal replays are idempotent within bounded receipt
history; stale, conflicting, unleased and unknown tokens fail without releasing credit.

Cancellation removes only unleased body/FIN writes, preserves the association prefix,
and never frees caller-leased buffers. Once the prefix is transport-confirmed, reset
can be polled while a body write is still in flight. A failed prefix aborts the
connection; there is no unreliable-reset downgrade.

After reset, first complete every outstanding body lease. Then submit command 22 from
the transport's **locally sent** reset observation. Never use a received peer reset,
the last write's count, queued length, or merely accepted cumulative bytes as final size.
Final size may be smaller than buffered accepted bytes; it must remain within the
cumulative accepted/admitted range and at least the reliable prefix. Only the admitted
size minus actual final size returns reserved payload credit. Header bytes are excluded.
Repeated identical confirmations are harmless; conflicting or premature confirmations
cannot create credit.

## Resource limits and teardown

The exact accepted overlay retains 64KiB request/capsule storage per stream/session and
128KiB aggregate bounds. This adapter permits only session zero and may impose stricter
caller-selected ingress budgets. Limits are checked **before copies**. QPACK dynamic
tables and blocked streams are disabled. Header count/bytes, input calls, uni-control
lifetime bytes, early session bytes, output bytes/events, sends, receipts and lifetime
stream/tombstone records are finite.

Request/control input admission conservatively reserves possible decoded event counts.
Configuration rejects an event budget smaller than `header_bytes / 32 + 8`; otherwise
even a one-byte fragmented header could never be admitted.
If one input cannot fit, split the transport read into smaller ordered chunks without
altering bytes or FIN position; if current output is full, drain/complete and retry.
Do not use an input rejection as permission to drop stream bytes. Production transports
must also enforce process/connection/stream idle and lifetime policies.

Up to 1MiB queued plus leased payload and 512 pending sends are permitted by the reviewed
maximum policy. Zero-byte writes still consume descriptors. At most 256 completion
receipts and 4096 lifetime stream records are retained. Stream IDs are never evicted and
reused; reaching the lifetime quota requires a fresh authenticated connection.
Session closure uses retained stream records as a bounded reset/STOP work list, so a
large session does not require an unbounded output batch. Unleased datagrams are dropped;
leased datagrams still require release acknowledgements.

The host must stop new I/O, cancel transport operations, drain/acknowledge leases and
observe actual local reset completions before normal handle destruction. Dropping a
handle releases state but does not cancel real transport operations. Keep the separate
Go shared library loaded for process lifetime; no Go image is loaded by this package.
Inspect the companion's structured native outcome even after caller cancellation.
In the selected QUIC backend, cancelling an already blocked datagram send closes its
connection (the upstream queue cannot cancel one datagram); receive cancellation is local.
Command 20 accounts for buffers, not transport liveness. Propagate connection loss to
the managed flow context and destroy this state after all leases settle; never continue
using an apparently established H3 session on that closed connection.
