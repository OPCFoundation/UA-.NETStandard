# PubSub Diagnostics (packet capture & dissection)

The `OPCFoundation.NetStandard.Opc.Ua.PubSub.Diagnostics` package adds
packet-capture, dissection and replay tooling for **OPC UA PubSub** (Part 14)
traffic. It captures the raw NetworkMessages exchanged over the UDP datagram and
MQTT broker transports, writes them to `.pcap` / `.pcapng` for Wireshark, and
dissects them back into structured DataSets — including **decryption of
encrypted UADP messages** when the matching security keys are available.

It is the PubSub counterpart of
[`Opc.Ua.Core.Diagnostics`](Diagnostics.md) (the UA-SC capture engine) and reuses
its `.pcap` / `.pcapng` writers. Because PubSub is connectionless and
message-secured, it uses its own frame and key-material abstractions rather than
the UA-SC channel/token model.

> **Target frameworks:** `net8.0`, `net9.0`, `net10.0`. The opt-in capture seam
> itself lives in `Opc.Ua.PubSub` and is available on every supported TFM.

## Contents

1. [How capture works](#1-how-capture-works)
2. [Capturing in-process](#2-capturing-in-process)
3. [Dissecting captured frames](#3-dissecting-captured-frames)
4. [Decrypting encrypted UADP messages](#4-decrypting-encrypted-uadp-messages)
5. [Writing pcap / pcapng files](#5-writing-pcap--pcapng-files)
6. [Environment-variable auto-capture](#6-environment-variable-auto-capture)
7. [MCP server tools](#7-mcp-server-tools)
8. [Security considerations](#8-security-considerations)

## 1. How capture works

The PubSub transports (`Opc.Ua.PubSub.Udp`, `Opc.Ua.PubSub.Mqtt`) expose a
zero-cost, opt-in capture seam in the `Opc.Ua.PubSub.Transports` namespace:

- `IPubSubCaptureObserver` — receives the raw wire bytes of every sent /
  received frame together with a `PubSubCaptureContext` (direction, transport
  profile, endpoint / topic, timestamp).
- `IPubSubCaptureRegistry` / `PubSubCaptureRegistry` — a lock-free holder for
  the active observer. The transports do a single volatile read on their hot
  send / receive path; when no observer is registered there is **no** runtime
  cost beyond that read.

A diagnostics capture session installs an observer on the shared registry; the
registry is registered as a DI singleton by `AddPubSub(...)`, so the transports
and the capture tooling share the same instance.

## 2. Capturing in-process

`InProcessPubSubCaptureSource` implements the observer and buffers every frame
into a bounded channel. `PubSubCaptureSessionManager` owns a single active
session:

```csharp
using Opc.Ua.PubSub.Pcap;
using Opc.Ua.PubSub.Transports;

// The registry shared with the PubSub transports (resolve from DI in a real app).
IPubSubCaptureRegistry registry = serviceProvider
    .GetRequiredService<IPubSubCaptureRegistry>();

await using var manager = new PubSubCaptureSessionManager(registry);

IPubSubCaptureSource source = await manager.StartAsync();
// ... run the publisher / subscriber ...
await manager.StopAsync();

await foreach (PubSubCaptureFrame frame in source.ReadCapturedFramesAsync(
    maxFrames: null, CancellationToken.None))
{
    Console.WriteLine($"{frame.Direction} {frame.TransportProfileUri} {frame.Data.Length} bytes");
}
```

## 3. Dissecting captured frames

`PubSubOfflineDissector` projects captured bytes into structured DataSets by
reusing the standard PubSub decoders (`UadpDecoder`, `JsonDecoder`). Malformed
input is returned as an undecodable result rather than throwing.

```csharp
var dissector = new PubSubOfflineDissector();
await foreach (PubSubCaptureFrame frame in source.ReadCapturedFramesAsync(null, ct))
{
    PubSubDissectionResult result = await dissector.DissectAsync(frame, ct);
    // result.MessageType, result.SecurityState, result.PublisherId,
    // result.WriterGroupId, result.DataSets (field name/value pairs)
}
```

JSON PubSub messages have no message-level security in Part 14 (confidentiality
relies on the MQTT TLS transport), so JSON frames are always dissected as
cleartext.

## 4. Decrypting encrypted UADP messages

Encrypted UADP NetworkMessages
([Part 14 §8.3](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/8.3),
[Annex A.2.2.5 PubSub-Aes-CTR](https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/A.2.2.5))
are laid out as `[outerPrefix ‖ SecurityHeader ‖ ciphertext ‖ signature]`. The
`SecurityHeader` carries the `SecurityTokenId` that selects the key. The
dissector decrypts a secured frame when a key resolves for that token id; it
reuses the production `UadpSecurityWrapper` to verify the signature and decrypt,
then dissects the recovered cleartext. Decryption failures are flagged, never
thrown.

Keys are supplied by an `IPubSubKeyResolver`:

- `CapturedKeyLogKeyResolver` — resolves keys from a captured key log
  (`PubSubKeyLogReader`) or from the keys buffered during capture.
- `SksKeyResolver` — wraps a live `IPubSubSecurityKeyProvider` (for example a
  `PullSecurityKeyProvider` backed by `OpcUaSecurityKeyServiceClient`, which
  calls `PublishSubscribe.GetSecurityKeys` on the SKS server).

```csharp
// Build a resolver from captured key material (for example a key log).
var resolver = new CapturedKeyLogKeyResolver();
var reader = new PubSubKeyLogReader("publisher.uakeys.json");
await foreach (PubSubKeyMaterial key in reader.ReadAllAsync(ct))
{
    resolver.AddKeyMaterial(key);
}

// Dissect with the resolver so encrypted UADP frames are decrypted.
// 'context' is the PubSubNetworkMessageContext used by the decoders.
var dissector = new PubSubOfflineDissector(context, resolver);

PubSubDissectionResult result = await dissector.DissectAsync(encryptedFrame, ct);
// result.SecurityState == Encrypted, but result.DataSets now contains the
// decrypted fields.
```

When no key resolves, the result reports `SecurityState = Encrypted` with the
`SecurityTokenId` and the marker `"encrypted (key required)"`.

## 5. Writing pcap / pcapng files

`PubSubPcapWriter` writes captured UDP frames to a libpcap / pcapng file,
synthesizing Ethernet/IPv4/UDP framing so Wireshark's OPC UA PubSub dissector
can read the capture. MQTT payloads are written to the JSON / text formats
(`PubSubJsonFormatter`, `PubSubTextFormatter`) rather than synthesizing broker
TCP framing.

```csharp
var writer = new PubSubPcapWriter();
long written = await writer.WritePcapAsync(
    source.ReadCapturedFramesAsync(null, ct), "pubsub.pcap", ct);
```

Real-wire capture from a network interface (SharpPcap) is also supported for UDP
multicast traffic.

## 6. Environment-variable auto-capture

`AddPubSubPcapFromEnvironment()` auto-starts an in-process capture when an
environment variable is set, and flushes it to disk on host shutdown:

| Variable | Effect |
| --- | --- |
| `OPCUA_PUBSUB_PCAP_FILE` | Auto-start capture; write to this `.pcap` / `.pcapng` on stop. |
| `OPCUA_PUBSUB_KEYLOGFILE` | Path for the captured PubSub key log (for offline decryption). |

```csharp
builder.Services
    .AddOpcUa()
    .AddPubSub(pubsub => pubsub.AddPublisher().AddUdpTransport());
builder.Services.AddPubSubPcapFromEnvironment();
```

## 7. MCP server tools

The reference MCP server (`Applications/McpServer`) exposes the PubSub surface as
tools:

- **Action / configuration:** `pubsub_add_connection`, `pubsub_remove_connection`,
  `pubsub_add_writer_group`, `pubsub_add_reader_group`,
  `pubsub_add_dataset_writer`, `pubsub_add_dataset_reader`, `pubsub_enable`,
  `pubsub_disable`.
- **Security Key Service:** `pubsub_get_security_keys`,
  `pubsub_add_security_group`, `pubsub_remove_security_group`.
- **Capture / dissection:** `pubsub_start_capture`, `pubsub_stop_capture`,
  `pubsub_capture_status`, `pubsub_write_pcap`, `pubsub_dissect_capture`,
  `pubsub_load_keylog`.

See [McpServer.md](McpServer.md) for the full tool catalogue.

## 8. Security considerations

- The key log and any captured key material contain **live security keys**.
  `PubSubKeyMaterial` defensively copies and zeroizes key bytes on dispose, but
  the key-log file is plaintext JSON-lines — protect it like a private key and
  delete it when no longer needed.
- Capture is opt-in and inert until an observer is registered; never enable it
  in production without controlling access to the output files.
- Decryption is an offline diagnostic aid; it reuses the production security
  primitives unchanged and does not weaken the runtime security path.
