# Data channels

**Experimental.** This is an implementation of the *OPC UA Data Channels* errata, a
proposed addition to OPC 10000-3, 10000-4 and 10000-6 that is **not** endorsed by the
OPC Foundation. Every identifier it uses — the `STR` MessageType, the NodeIds in the
65000 block, the twelve StatusCodes, the `opcua/1` ALPN protocol — is provisional and
will change if and when the OPC Foundation assigns final values.

## What it is

OPC UA has no streaming primitive. A camera, a microphone, a firmware image or a log
tail has to be carried by something that was designed for something else: `Read`
polling, a `Subscription` carrying ByteString values, the FileTransfer model, or PubSub
alongside the SecureChannel rather than on it.

A **data channel** is a named, authorized, flow-controlled, bidirectional stream of
opaque bytes multiplexed onto an existing SecureChannel. It is opened by a Service,
described in the AddressSpace, and carried by frames that interleave with ordinary
Service traffic without blocking it.

```text
                 one SecureChannel
   ┌──────────────────────────────────────────────┐
   │  MSG  MSG        MSG              MSG        │  Service traffic, unchanged
   │      STR STR STR    STR STR STR      STR STR │  channel 1: video
   │   STR         STR            STR             │  channel 2: audio
   │                STR                           │  channel 0: flow control, ping
   └──────────────────────────────────────────────┘
```

## Turning it on

The feature is inert until it is enabled. Nothing changes for a peer that does not use
it, and a peer that does use it never speaks first: it may not transmit a frame until an
`OpenDataChannel` on that SecureChannel has completed successfully. That rule matters
because an unrecognized `MessageType` is a protocol error that closes the SecureChannel,
taking every Session, Subscription and Service call with it.

Consumers opt in by referencing the package and suppressing the experimental diagnostic:

```xml
<ItemGroup>
  <PackageReference Include="OPCFoundation.NetStandard.Opc.Ua.Core.Channels" />
</ItemGroup>
<PropertyGroup>
  <NoWarn>$(NoWarn);DataChannels</NoWarn>
</PropertyGroup>
```

## Where the code lives

The engine ships in `OPCFoundation.NetStandard.Opc.Ua.Core.Channels` rather than in
`Opc.Ua.Core`. It is a consumer of the message-extension seam in Core: it registers as the
owner of the `STR` MessageType, and the SecureChannel decrypts, verifies and
sequence-checks every chunk before the engine sees it. Core itself carries no data channel
vocabulary — `ISecureChannelMessageExtension` and `ISecureChannelMessageHost` name only "a
MessageType that is neither a Service call nor part of establishing the SecureChannel".

Two things stay in Core because they are the SecureChannel's own concerns rather than the
feature's: `SequenceNumberBudget`, which tracks the sequence space every MessageType draws
on, and `UaSCSecureChannelRegistry`, which maps a SecureChannel identifier to the channel
that owns it. The `opc.quic` transport remains its own package,
`OPCFoundation.NetStandard.Opc.Ua.Bindings.Quic`.

## Server side

```csharp
// One manager per SecureChannel. The channel implementation creates it.
DataChannelManager channels = binaryChannel.EnableDataChannels(
    isServer: true,
    telemetry,
    maxDataChannels: 16,
    maxCreditPerChannel: 1024 * 1024);

// Register the endpoints that can be streamed.
var sources = new DataChannelSourceRegistry();
sources.Register(new CameraSource(nodeId));

var handler = new DataChannelServiceHandler(
    channels,
    sources,
    new DataChannelServerCapabilities
    {
        MaxFrameSize = 8192,
        MaxCreditPerChannel = 1024 * 1024,
        SupportedDeliveryModes = [DataChannelDeliveryMode.ReliableOrdered],
        SupportedTransportProfileUris = [Profiles.UaTcpTransport]
    },
    authorizer,
    auditor);

OpenDataChannelResponse response = await handler.OpenDataChannelAsync(
    context, sourceNodeId, offerId, requestedParameters, ct);

// The channel may not carry a frame until the response is on the wire.
handler.OnResponseSent(response.ChannelId);
```

## Application side

```csharp
DataChannel channel = channels.Channels[0];

// Send. The frame is assigned its FrameSequenceNumber here, not at
// transmission, which is what lets a GAP frame name a frame that was
// never sent.
channel.Write(frameBytes, DataChannelFrameFlags.MessageStart |
                          DataChannelFrameFlags.MessageEnd |
                          DataChannelFrameFlags.Marker);

// Receive. Disposing the message is what returns its buffer *and*
// releases flow-control credit: an application that never disposes
// stalls the channel it is reading.
using DataChannelMessage? message = await channel.ReadAsync(ct);

if (message != null)
{
    if (StatusCode.IsUncertain(message.Status))
    {
        // Frames GapFrom..GapTo were discarded or lost. A decoder can
        // conceal, or wait for the next frame carrying IsMarker.
    }

    Decode(message.Payload.Span);
}
```

## What is implemented

| Area | State |
|---|---|
| `STR` MessageChunk, stream header, seven frame types, five flags | Complete, verified byte for byte against the specification's thirteen published hex vectors |
| Serial arithmetic, replay window, bounded GAP runs | Complete |
| Per-direction channel and connection credit, bootstrap, replenishment | Complete for inline framing |
| Deficit round robin, per-channel quantum, anti-starvation | Complete |
| Per-direction state machine, half-close, reset, drain timeout | Complete |
| Deadline expiry and per-run `GAP` emission | Complete |
| SequenceNumber budget, renewal threshold, stall-rather-than-reuse | Complete |
| `OpenDataChannel`, `ModifyDataChannel`, `CloseDataChannel` | Complete, and served by `StandardServer`: a real Client opens a channel through a real Session |
| Inline framing (`opc.tcp`, `opc.wss`) on the Server | Complete. `InlineServerDataChannelTransport` resolves the UASC channel behind the request and enables the engine on it, so a Server with no transport configured still carries channels over the connection the Client already holds. A SecureChannel that can carry no frames is refused with `Bad_DataChannelTransportUnsupported` rather than accepted and then drained silently, which §5.16 requires |
| Parameter negotiation, offers, Session scoping, authorization recheck, audit | Complete, driven from the Server rather than only callable |
| `opc.quic` — url scheme, ALPN negotiation and enforcement, control stream, client channel and factory | Complete (`Opc.Ua.Bindings.Quic`, **net9.0+**) |
| `opc.quic` — listener, service host, endpoint discovery, reverse connect, certificate rotation | Complete |
| `opc.quic` — data channels bound to per-channel streams, `RESET_STREAM` carrying the StatusCode | Complete |
| `opc.quic` — direction to stream type and initiator (§7.4), `revisedTransportChannelId` | Complete; `SourceToSink` gets a server-initiated unidirectional stream whose id is returned to the Client |
| `opc.quic` — TLS-to-OPC-UA key binding (§7.6.1) | Complete, and **invoked on the connect path** — it previously existed only as tested, uncalled code, which left the profile unbound |
| `DataChannelCapabilities` model projection | Complete (`DataChannelModel`) |
| Worked sample | `samples/ConsoleDataChannelStreaming` |
| Unreliable datagrams (§7.5) | **Not implementable on .NET.** `QuicConnection` exposes no RFC 9221 datagram API through .NET 10, so `SupportsUnreliableDatagrams` is `False` and the Server refuses `Unreliable` and `PartiallyReliable` with `Bad_DeliveryModeUnsupported` — which is what the errata requires rather than silently carrying them on the stream |
| DI / fluent builder extension | `AddQuicTransport()` |

## Why the QUIC binding is net9.0+

`System.Net.Quic` is still behind `[RequiresPreviewFeatures]` on net8.0. Opting in
would emit a `RequiresPreviewFeatures` assembly attribute that every consumer would
then have to opt into as well, so the binding targets net9.0 and net10.0, where the
API is stable. `Opc.Ua.Core` itself is unaffected and still builds for all six TFMs.

## Running the sample

```sh
cd samples/ConsoleDataChannelStreaming
dotnet run -- --transport tcp  --mode server --frames 2000 --size 1200
dotnet run -- --transport quic --mode server --frames 2000 --size 1200
```

`--mode server` is the one that matters: it stands up a real `StandardServer`,
connects a real Client, creates a Session, and opens the channel through
`OpenDataChannel` — so it exercises the Service dispatch, the Session binding and
the negotiation, not just the framing. `--mode direct` drives `DataChannelManager`
directly and is kept because that is the shorter path for measuring the scheduler.

The same application code drives both framings; only the transport differs. The
sample reports throughput, discarded frames and the credit-stall counter, and the
stall counter staying at zero over `opc.quic` is the visible consequence of QUIC
owning the flow control there.

## Measuring throughput against a competing Publish load

`--mode benchmark` answers a narrower question: what does a data channel sustain
while the Session is also carrying Service traffic? It runs four cases — no
subscription, then monitored items publishing at 10 ms, 100 ms and 1000 ms — for
the transport given by `--transport`, and prints them as one table.

```sh
dotnet run -- --transport tcp  --mode benchmark --frames 60000 --size 1200 \
  --monitored-items 100 --repeat 5
dotnet run -- --transport quic --mode benchmark --frames 60000 --size 1200 \
  --monitored-items 100 --repeat 5
```

Comparing inline framing against `opc.quic` means running it twice, which is
deliberate: over inline framing the STR chunks share one SecureChannel and one
SequenceNumber space with Publish, so the credit window is what keeps a media
stream from starving the Service path; over `opc.quic` the channel owns its own
stream and QUIC applies the flow control, so the two should barely interact.

One Server, one Session and one data channel serve the whole matrix and only the
subscription changes between cases, so every row runs on the same channel with
the same negotiated credit. Each case takes one warm-up run that is discarded and
then `--repeat` measured runs, reported as a median with the spread.

### Reading the output honestly

Three things will silently turn this measurement into a plausible-looking lie,
so the benchmark checks for all three and says so in its output rather than
leaving the reader to assume:

- **The competing load may not exist.** If the monitored items do not report,
  every "loaded" row is really a second baseline, the rows agree beautifully,
  and any conclusion drawn from them is false. The `notif/s` column is the
  evidence, and a row that received far fewer notifications than
  `items x duration / interval` is called out.
- **The Server revises the publishing interval.** `MinPublishingInterval`
  defaults to 100 ms, so a requested 10 ms becomes 100 ms and two rows become
  the same experiment however different the request column looks. The table
  carries a `revised` column and warns when the two differ.
- **The run may be shorter than a publishing cycle.** At 1000 ms a run of a few
  hundred milliseconds contains no Publish traffic at all, so it understates the
  load rather than measuring it. Raise `--frames` until a run lasts several
  seconds.

The figures are in-process loopback figures, bound by CPU and cryptography
rather than by a network interface. They are for comparing the four cases
against each other, not for quoting as link throughput. `DataChannel.Write`
enqueues rather than blocking, so what is measured is the rate the pipeline
drains at: first write to last frame received.

Credit stalls are the mechanism rather than a fault. Over inline framing a
stalled data channel is exactly what leaves the SecureChannel free for Publish,
which is the property the credit window exists to provide.

## Design notes worth knowing

**The single-chunk rule is the load-bearing constraint.** A data channel frame is
exactly one MessageChunk. A multi-chunk frame would sit in the existing chunk assembler
and block every other Message on the connection until it completed — the precise failure
a streaming layer exists to avoid.

**Service traffic keeps precedence structurally, not by a second scheduler.** The
transport already serializes writes in arrival order, so a `MSG`, `OPN` or `CLO` chunk
that becomes ready while a frame is being written is admitted immediately after it. The
maximum delay is one frame, which is exactly what the specification requires.

**The sequence arithmetic uses modulus 2^32−1, not 2^32.** Zero is excluded from the
`FrameSequenceNumber` value space, so with modulus 2^32 the wrap from `4294967295` to
`1` computes as a distance of two and the receiver reports a gap that did not happen.

**Only `DATA` advances `HighestReceived`.** If a `GAP` advanced it, the `GAP` announcing
an expiry would push `HighestReceived` past a lower-numbered frame that survived and is
still to be transmitted, and the receiver would discard as a duplicate precisely the
frame the per-run rule exists to protect.

**`Paused` and `Closing` are both per direction.** Receiving `END` marks the *peer's*
direction ended and nothing more: it never starts the local drain clock and never stops
the local application enqueueing. That is what makes `END` a half-close rather than a
close, and it is why a long upload survives the other end half-closing.

**`IsFinal` is `F` and nothing else.** An Abort chunk's secured body is `Error` followed
by `Reason` per OPC 10000-6 §6.7.3, so accepting `A` would let that parser read a 32-bit
string length out of the attacker-controlled `FrameType`, `Flags` and `Reserved` bytes
of the stream header.

**The scheduler's deficit bounds volume, not frequency.** A round that leaves payload
queued schedules the next one immediately. The loop originally waited for its idle tick
between rounds, which capped a channel at one quantum per tick — about fifty frames a
second. The sample measured 0.5 Mbit/s before the fix and 1.3 Gbit/s after it, and
`ManyFramesDrainWithoutWaitingForTheIdleTick` fails loudly if the wake is dropped again.

## Test coverage

The suite is 284 tests on `net10.0` and 212 tests on `net48` over
`tests/Opc.Ua.Core.DataChannels.Tests`, covering
`Opc.Ua.Core.Channels`, `Stack/Tcp/UaSCBinaryChannel.MessageExtensions.cs`
and `Opc.Ua.Bindings.Quic`. Coverage was
**80.1%** at 255 tests, having fallen from 87.6% while the test count rose, because
wiring the previously-uncalled obligations added a good deal of
production code — the server-side Service dispatch, the stream mapping, the
certificate lifecycle — faster than tests were added for it. It is the more
honest number: the earlier one measured a smaller body of code, much of which
nothing invoked. Re-measure with the repo's own settings:

```sh
dotnet test tests/Opc.Ua.Core.DataChannels.Tests/Opc.Ua.Core.DataChannels.Tests.csproj \
  -f net10.0 -c Release --collect:"XPlat Code Coverage" --settings tests/coverlet.runsettings.xml
```

QUIC tests are guarded by `QuicConnection.IsSupported` and `#if NET9_0_OR_GREATER`, so
the net472 and net8.0 legs and any agent without msquic run 184 of them and skip the
rest rather than failing. There is deliberately **no build-time coverage gate**: a gate
would fail exactly those agents.

Three defects were found by writing these tests, all in code that the pre-existing
end-to-end tests had executed without asserting:

| Defect | Consequence |
| --- | --- |
| `TryPing()` had no channel-state guard, though `Write()` guards `Closed`/`Faulted` | On a dead channel it took a sequence number, enqueued a PING, re-woke the scheduler and latched `m_pingOutstanding`, so the channel could later be declared dead by a ping that should never have been sent. `TryPing` is public API. |
| `QuicConnectionBuilder.ConnectAsync` caught only `QuicException` | An ALPN or certificate rejection surfaces from the TLS handshake as `AuthenticationException` and escaped as a raw platform exception, so callers using the stack's `catch (ServiceResultException)` idiom missed it entirely. Now mapped to `Bad_SecurityChecksFailed`. |
| `QuicTransportListener` captured the TLS certificate in the accept callback's closure | `CertificateUpdate` moved the UASC layer to the rotated certificate while TLS kept presenting the retired one — breaking the very key-equality check of §7.6.1 that the errata exists to enforce. The callback now reads a field, endpoint descriptions are refreshed, and retired certificates are held until close so an in-flight handshake is never pulled out from under. |

The one lesson worth carrying forward: none of these were caught by coverage of the
*happy path*. The scheduler bug in particular had every line executed and still shipped,
because nothing asserted the rate.

## What running it found that tests did not

A later conformance review showed the components were right and the **wiring** was
missing: `QuicPeerBinding.Verify` had eight test references and no production callers,
`DataChannelServiceHandler` was never constructed, `DataChannelManager.Remove` was never
called. Wiring them, and then driving a real Client against a real Server, found four
more defects that the suite could not reach:

| Defect | Why no test caught it |
| --- | --- |
| `SendDataChannelFrameAsync` secured a `STR` chunk without holding `DataLock`, so the scheduler thread and the Service path reached the same HMAC concurrently — a `CryptographicException` from the CNG provider on Windows, and duplicate `SequenceNumber`s where it did not throw | Data channels are the first thing to write to a SecureChannel off the Service thread; no unit test runs both writers at once. Now a normative rule with conformance unit DCF-038 |
| `Session` rejected the DataChannel request types as an unexpected `RequestType` | Unit tests construct the handler directly and never traverse Session validation |
| `StandardServer` created listeners only for schemes in the hardcoded `Utils.DefaultUriSchemes`, so registering any out-of-tree binding silently did nothing | Listener tests construct the listener directly rather than going through server startup |
| `ServerBase` did not map `opc.quic` to a transport profile, so its endpoints advertised none | Same |

`MaxDataChannels` also counted channels that had already ended, so a SecureChannel
refused every new channel after sixteen open-close cycles with none open — reachable
only because the connection-level limit had no test at all, unlike the source limit.

A later specification-compliance review found the same pattern four more times, and every
regression test added for these goes through the production entry point rather than the
component, because that is what the component-level tests kept missing:

| Defect | Why no test caught it |
| --- | --- |
| `QuicServerDataChannelTransport.BindClientStreamAsync` discarded the task carrying the §7.4 `transportChannelId` checks, so a Client could name a stream it did not own and the Server answered `Good` and echoed it | The validation had tests, but they called `BindChannelAsync` directly; nothing exercised the Service path that consumes it |
| The per-channel delivery queue was bounded in frames but derived from a byte credit, and blocked the shared receive loop when full — one unread channel stalled `MSG`, `OPN` and `CLO` for the whole SecureChannel | No test enqueued more small frames than the queue held while nothing consumed |
| `OnResponseSent` ran before the response object was even encoded, so the scheduler could emit a frame for a ChannelId the peer had not been told about | The state model was right and unit-tested; only the call site was wrong |
| `StandardServer` fell back to a transport whose `SendFrameAsync` returned without doing anything, so `opc.tcp` was advertised, accepted, and then silently dropped every frame | No test opened a data channel through `StandardServer` at all |

## Deviation from the errata

`OpenDataChannel` in the errata carries `transportChannelId` in both the request and the
response. No OPC UA service reuses a parameter name across the two, and the model
compiler enforces it, so the response parameter here is `revisedTransportChannelId`. The
errata needs the same correction.

Two further corrections were raised against the errata while implementing it, and both
are drafted in the [drafts repository](https://github.com/marcschier/opcua-drafts):

- **Part 4 §9 assigns no numeric StatusCodes.** The clause lists fourteen symbolic ids
  and says the numeric values are provisional, but never states them — while Part 3 pins
  provisional NodeIds in the 65000+ block. These StatusCodes travel on the wire in the
  `RESET` frame, so two implementations that each invent their own numbers cannot
  interoperate. This implementation had to pick `1100`–`1113`, and the errata now
  publishes the same block.
- **Part 6 §5.7 states both scheduling obligations for senders only.** §5.8 says
  backpressure is per channel and "stalls that stream and nothing else", but no **shall**
  binds the receiver. Under inline framing one reader carries both the frames and the
  Service traffic, so a receiver applying backpressure by not reading converts a
  per-channel stall into a connection-wide one while breaking no stated rule. The errata
  now states the receiver obligation and adds conformance unit DCF-039.

## Building

A cold build needs the model compiler built before the solution, otherwise the analyzers
load half-built and produce thousands of spurious errors in generated code:

```sh
dotnet build tools/Opc.Ua.SourceGeneration.Core/Opc.Ua.SourceGeneration.Core.csproj
dotnet build tools/Opc.Ua.SourceGeneration/Opc.Ua.SourceGeneration.csproj
dotnet build tools/Opc.Ua.SourceGeneration.Stack/Opc.Ua.SourceGeneration.Stack.csproj
dotnet build UA.slnx
```

After editing anything under `tools/Opc.Ua.SourceGeneration.Core/Design/`, rebuild
`Opc.Ua.Core.Types` with `-t:Rebuild`: an incremental build keeps the stale analyzer and
silently drops the new types while still reporting success.

## References

- [OPC UA Part 6 — Data Channel Transport](https://github.com/marcschier/opcua-drafts/blob/main/core-specs/data-channels/OPC-UA-Part6-Data-Channel-Transport.md)
- [OPC UA Part 4 — Data Channel Services](https://github.com/marcschier/opcua-drafts/blob/main/core-specs/data-channels/OPC-UA-Part4-Data-Channel-Services.md)
- [OPC UA Part 3 — Data Channel Model](https://github.com/marcschier/opcua-drafts/blob/main/core-specs/data-channels/OPC-UA-Part3-Data-Channel-Model.md)
