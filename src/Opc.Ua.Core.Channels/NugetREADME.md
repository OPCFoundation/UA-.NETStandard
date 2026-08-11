# OPC UA Data Channels

**Experimental.** This package implements the *OPC UA Data Channels* errata, a proposed
addition to OPC 10000-3, 10000-4 and 10000-6 that is **not** endorsed by the OPC Foundation.
Every identifier it uses — the `STR` MessageType, the NodeIds in the 65000 block, the
StatusCodes `1100`–`1113` — is provisional and will change if and when the OPC Foundation
assigns final values.

## What it is

OPC UA has no streaming primitive. A camera, a microphone, a firmware image or a log tail has
to be carried by something designed for something else: `Read` polling, a Subscription carrying
ByteString values, the FileTransfer model, or PubSub alongside the SecureChannel rather than on
it.

A data channel is a named, authorized, flow-controlled, bidirectional stream of opaque bytes
multiplexed onto a SecureChannel that is already open. It reuses the connection, the security
and the Session a Client already has.

## What is in this package

- The framing layer: the `STR` MessageChunk, the stream header, the seven frame types and the
  frame codec.
- The engine: per-channel and per-connection credit, the deficit round-robin scheduler, serial
  number arithmetic with a replay window, gap reporting and the per-direction state machine.
- The DataChannel Service Set handler, parameter negotiation, offers and authorization.
- The inline transport over `opc.tcp` and `opc.wss`, which carries frames on the connection the
  Client already holds.

The `opc.quic` transport lives in `OPCFoundation.NetStandard.Opc.Ua.Bindings.Quic`.

## How it attaches to a SecureChannel

The package is a consumer of the message-extension seam in
`OPCFoundation.NetStandard.Opc.Ua.Core`: it registers as the owner of the `STR` MessageType, and
the channel secures, verifies and sequences every chunk before the engine sees it. Nothing
changes for a peer that does not use data channels, and a peer that does use them never speaks
first — it may not transmit a frame until an `OpenDataChannel` on that SecureChannel has
completed successfully.

See [the data channels documentation](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/DataChannels.md)
for the full picture, and the
[errata drafts](https://github.com/marcschier/opcua-drafts/tree/main/core-specs/data-channels)
for the specification this implements.
