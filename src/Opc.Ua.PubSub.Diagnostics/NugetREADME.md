# OPCFoundation.NetStandard.Opc.Ua.PubSub.Diagnostics

Packet-capture, dissection and replay tooling for **OPC UA PubSub** (Part 14)
traffic. Captures the raw NetworkMessages exchanged over the UDP datagram and
MQTT broker transports, writes them to `.pcap` / `.pcapng` for Wireshark, and
dissects them back into structured DataSets — including **decryption of
encrypted UADP messages** when the matching security keys are available (from a
captured key log or a live Security Key Service).

## What it does

- **Capture** PubSub frames in-process via a zero-cost, opt-in tap on the
  `Opc.Ua.PubSub.Udp` / `Opc.Ua.PubSub.Mqtt` transports, or off the wire from a
  network interface.
- **Dissect** captured UADP and JSON NetworkMessages into DataSetMessages /
  DataSets, reusing the standard PubSub decoders.
- **Decrypt** encrypted UADP NetworkMessages (PubSub-Aes128-CTR /
  PubSub-Aes256-CTR, Part 14 §8.3 / Annex A.2.2.5) by resolving the
  `SecurityTokenId` in the UADP SecurityHeader to the matching key.
- **Replay** a recorded capture back through the dissection pipeline.

## Relationship to `Opc.Ua.Core.Diagnostics`

This package mirrors the UA-SC capture stack in
`OPCFoundation.NetStandard.Opc.Ua.Core.Diagnostics` and reuses its `.pcap` /
`.pcapng` writers. PubSub is connectionless and message-secured, so it uses its
own frame and key-material abstractions rather than the UA-SC channel/token
model.

## Target frameworks

`net8.0`, `net9.0`, `net10.0`.

## Documentation

See `docs/PubSubDiagnostics.md` in the
[UA-.NETStandard](https://github.com/OPCFoundation/UA-.NETStandard) repository.
