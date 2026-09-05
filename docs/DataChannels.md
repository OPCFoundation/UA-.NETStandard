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

Consumers opt in by suppressing the experimental diagnostic. The engine is part of
`OPCFoundation.NetStandard.Opc.Ua.Core`, so no extra package is needed for inline
framing; `opc.quic` is a separate package because it carries channels on native QUIC
streams:

```xml
<PropertyGroup>
  <NoWarn>$(NoWarn);DataChannels</NoWarn>
</PropertyGroup>
```

## Where the code lives

The engine is part of `Opc.Ua.Core`, under `Stack/DataChannels`. `UaSCUaBinaryChannel`
owns the `DataChannelManager` for its SecureChannel and dispatches `STR` chunks to it
after decrypting, verifying and sequence-checking them, so the engine only ever sees
authenticated content. `EnableDataChannels` turns the feature on for a channel; until it
is called an incoming `STR` chunk closes the SecureChannel, which is what the
interoperability rule of §5.16 requires of a peer that does not implement this
specification.

`SequenceNumberBudget` tracks the sequence space every MessageType on the channel draws
on. The channel claims a SequenceNumber while it secures a frame and refuses the send with
`Bad_SecureChannelTokenUnknown` when the space under the current token is exhausted, so a
sender stalls rather than reuse a number (§5.1.1). Assigning the number and applying
message security are serialized against Service traffic — both draw on the same keys and
the same counter — while the write itself is awaited outside that serialization, so a slow
peer on a data channel cannot stall `Publish`.

`UaSCSecureChannelRegistry` maps a SecureChannel identifier to the channel that owns it.
It is public because an application writing its own `IServerDataChannelTransport` for
inline framing needs it to resolve the channel behind a request, which
`samples/Core/ConsoleDataChannelStreaming` demonstrates.

The server-side Service surface — `IServerDataChannelTransport` and
`InlineServerDataChannelTransport` — is part of `Opc.Ua.Server`. The `opc.quic` transport
is its own package, `OPCFoundation.NetStandard.Opc.Ua.Bindings.Quic`, because it carries
data channels on native QUIC streams rather than as UASC chunks and needs no part of the
inline framing.

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
| Per-direction state machine, half-close, reset, drain timeout | Complete except `OpenTimeout` and `PingTimeout`, which are negotiated and carried but not yet enforced (§5.14) |
| Deadline expiry and per-run `GAP` emission | Complete |
| `PING`/`PONG` round-trip measurement and rate limiting (§5.11) | Complete on **both** sides: a sender is held to one unanswered PING and one per second per ChannelId, and a receiver discards a PING that breaches that bound and resets the channel once the breach persists. Enforcing only the sending half would bound a well-behaved peer and leave a hostile one with an amplification surface, since PING is credit-exempt and compels a PONG ahead of queued payload |
| SequenceNumber budget, renewal threshold, stall-rather-than-reuse | Complete for the sender and the client-side renewal trigger; the `revisedLifetime` obligation of §5.1.1 is not implemented |
| `OpenDataChannel`, `ModifyDataChannel`, `CloseDataChannel` | Complete, and served by `StandardServer`: a real Client opens a channel through a real Session |
| Inline framing (`opc.tcp`, `opc.wss`) on the Server | Complete on the raw-socket `opc.tcp` listener. `InlineServerDataChannelTransport` resolves the UASC channel behind the request and enables the engine on it, so a Server with no transport configured still carries channels over the connection the Client already holds. A SecureChannel that can carry no frames is refused with `Bad_DataChannelTransportUnsupported` rather than accepted and then drained silently, which §5.16 requires |
| Authorization | Complete, and **direction-aware**: `SourceToSink` requires Read on the source, `SinkToSource` requires Write, `Bidirectional` requires both (Part 4 errata §7.2, DCS-023). Re-evaluated on an interval, on `ActivateSession` and on role change |
| Parameter negotiation, Session scoping, authorization recheck, audit | Complete, and driven from the Server. **Server-initiated offers (Part 4 §6) are not implemented**: the registry exists but nothing creates an offer or raises `DataChannelOfferedEventType`, so `TryRedeem` can only fail |
| `opc.quic` — url scheme, ALPN negotiation and enforcement, control stream, client channel and factory | Complete (`Opc.Ua.Bindings.Quic`, **net8.0+**) |
| `opc.quic` — listener, service host, endpoint discovery, reverse connect, certificate rotation | Complete |
| `opc.quic` — data channels bound to per-channel streams, `RESET_STREAM` carrying the StatusCode | Complete. The stream is released when the channel reaches a terminal state, so an orderly close completes the writes and a `RESET` becomes a `RESET_STREAM` carrying the StatusCode |
| `opc.quic` — direction to stream type and initiator (§7.4), `revisedTransportChannelId` | Complete; `SourceToSink` gets a server-initiated unidirectional stream whose id is returned to the Client |
| `opc.quic` — TLS-to-OPC-UA key binding (§7.6.1) | Complete. The binding is verified on the connect path, comparing the TLS peer's subjectPublicKeyInfo against the OPN senderCertificate in constant time |
| `DataChannelCapabilities` model projection | **Not wired.** `DataChannelModel` builds the values, but the Object is never instantiated under `ServerCapabilities`, so a Client cannot read the capabilities or discover the feature through the address space (Part 3 §6, Part 4 §10) |
| Worked samples | `samples/Core/ConsoleDataChannelStreaming` (throughput benchmark) and `samples/Core/ConsoleDataChannelAudio` (looping audio, played back by the Client) |
| Unreliable datagrams (§7.5) | **Not implementable on .NET.** `QuicConnection` exposes no RFC 9221 datagram API through .NET 10, so `SupportsUnreliableDatagrams` is `False` and the Server refuses `Unreliable` and `PartiallyReliable` with `Bad_DeliveryModeUnsupported` — which is what the errata requires rather than silently carrying them on the stream |
| DI / fluent builder extension | Complete. `AddQuicTransport()` registers the listener and channel factories **and** the server-side data channel transport, so a DI-built Server carries channels on `opc.quic` streams with no further wiring. `UseQuicDataChannelTransport()` is the direct-construction fallback |
| Connection loss and SecureChannel close | Complete. A closed SecureChannel or lost transport faults every data channel riding on it (§5.13), on both the inline and `opc.quic` paths |

## Why the QUIC binding is net8.0+

`System.Net.Quic` carries `[RequiresPreviewFeatures]` on net8.0, so the binding enables
preview features for that target and a net8.0 consumer sets `EnablePreviewFeatures` in
its own project to use it. On net9.0 and net10.0 the API is stable and no opt-in applies.
`QuicServerConnectionOptions.HandshakeTimeout` is .NET 9+, so on net8.0 the platform
default bounds the handshake and the listener's own admission expiry still releases a
stalled peer's slot. `Opc.Ua.Core` is unaffected and builds for all six TFMs.

## Running the sample

### Audio streaming

`samples/Core/ConsoleDataChannelAudio` is the shortest path to seeing the feature do
something a Subscription cannot. It stands up a Server and a Client in one process,
synthesises a short melody as 16-bit PCM, and streams it on repeat over a data channel
while the Client plays it:

```sh
cd samples/Core/ConsoleDataChannelAudio
dotnet run                 # 20 ms frames
dotnet run -- 60           # 60 ms frames
```

The source writes in real time rather than as fast as the channel will take it, because a
media source is paced by its own clock and writing faster would only add latency. The
progress line reports frames, bytes and credit stalls, so a consumer that cannot keep up
is visible rather than silently buffered.

Playback uses NAudio, whose output devices are Windows interfaces; on Linux and macOS the
sample writes the received stream to a WAV in the temp directory instead and says so on
startup.

### Throughput benchmark

```sh
cd samples/Core/ConsoleDataChannelStreaming
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

**`Closing` is per direction.** Receiving `END` marks the *peer's*
direction ended and nothing more: it never starts the local drain clock and never stops
the local application enqueueing. That is what makes `END` a half-close rather than a
close, and it is why a long upload survives the other end half-closing. `Paused` is not
tracked per direction — the channel carries a single state field — because credit is only
ever withheld against the direction that carries payload towards this peer.

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

The suite is 316 tests on `net10.0` and 242 tests on `net48` over
`tests/Opc.Ua.Core.DataChannels.Tests`, covering `Stack/DataChannels`,
`Stack/Tcp/UaSCBinaryChannel.DataChannels.cs` and `Opc.Ua.Bindings.Quic`.

`DataChannelIntegrationTests` is the end-to-end leg: a real Client Session
drives `OpenDataChannel`, `ModifyDataChannel` and `CloseDataChannel` against a
live `StandardServer` over `opc.tcp`, and payload crosses the same
SecureChannel inline alongside the Service traffic. It is the only place
`Opc.Ua.Server/Server/StandardServer.DataChannels.cs` — the Service dispatch,
the per-SecureChannel state and the authorization chain — is exercised by a
request that actually arrived off a socket.

The paths at zero are not evenly distributed, which matters more than the
percentage does: `opc.quic` reverse connect and the retired-key teardown of
§7.6.1 are the largest gaps. Re-measure with the repo's own settings:

```sh
dotnet test tests/Opc.Ua.Core.DataChannels.Tests/Opc.Ua.Core.DataChannels.Tests.csproj \
  -f net10.0 -c Release --collect:"XPlat Code Coverage" --settings tests/coverlet.runsettings.xml
```

QUIC tests are guarded by `QuicConnection.IsSupported`, so the net472 and net48
legs and any agent without msquic skip them rather than failing. There is
deliberately **no build-time coverage gate**: a gate would fail exactly those
agents.

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
