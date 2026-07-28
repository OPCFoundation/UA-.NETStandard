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

Consumers opt in by suppressing the experimental diagnostic:

```xml
<PropertyGroup>
  <NoWarn>$(NoWarn);DataChannels</NoWarn>
</PropertyGroup>
```

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
| `OpenDataChannel`, `ModifyDataChannel`, `CloseDataChannel` | Generated from the model compiler inputs; server-side handler complete |
| Parameter negotiation, offers, Session scoping, authorization recheck, audit | Complete |
| `opc.quic` — url scheme, ALPN negotiation and enforcement, control stream, client channel and factory | Complete (`Opc.Ua.Bindings.Quic`, **net9.0+**) |
| `opc.quic` — listener, service host, endpoint discovery, reverse connect, certificate rotation | Complete |
| `opc.quic` — data channels bound to per-channel streams, `RESET_STREAM` carrying the StatusCode | Complete |
| `opc.quic` — TLS-to-OPC-UA key binding (§7.6.1) | Complete |
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
dotnet run -- --transport tcp  --frames 2000 --size 1200
dotnet run -- --transport quic --frames 2000 --size 1200
```

The same application code drives both framings; only the transport differs. The
sample reports throughput, discarded frames and the credit-stall counter, and the
stall counter staying at zero over `opc.quic` is the visible consequence of QUIC
owning the flow control there.

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

## Deviation from the errata

`OpenDataChannel` in the errata carries `transportChannelId` in both the request and the
response. No OPC UA service reuses a parameter name across the two, and the model
compiler enforces it, so the response parameter here is `revisedTransportChannelId`. The
errata needs the same correction.

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
