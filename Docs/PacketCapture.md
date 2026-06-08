# OPC UA Packet Capture, Dissection, and Replay

The OPC UA packet-capture feature records UA traffic, stores the secure-channel keys needed for offline decoding, reconstructs service calls, and replays captured conversations. The reusable engine ships as `OPCFoundation.NetStandard.Opc.Ua.Bindings.Pcap`; the OPC UA MCP server exposes it as capture, decode, and replay tools. Decoding follows OPC UA Part 6 secure conversation framing and reuses the stack `UaSCUaBinaryChannel` path instead of reimplementing cryptography.

## When to use it

- Debug OPC UA interoperability issues that only appear on a real wire.
- Capture a repro for a bug that cannot be reproduced under a debugger.
- Validate wire-level effects of stack, application, or security-policy changes.
- Build decoder and replay tests from real captures.

## Architecture

```mermaid
graph TD
    A[Capture source] --> B[pcap / pcapng file]
    A --> C[keylog file]
    B --> D[Offline decoder]
    C --> D
    D --> E[UaSCUaBinaryChannel decoding path]
    E --> F[Service-call timeline]
    F --> G[Formatters]
    F --> H[Replay engines]
```

## Integration with the central channel manager

The Pcap binding composes with the central [`IClientChannelManager`](Sessions.md#4-iclientchannelmanager--centralised-channel-sharing-and-reconnect) introduced in issue [#3288](https://github.com/OPCFoundation/UA-.NETStandard/issues/3288) via the global `TransportBindings.Channels` registry: `AddOpcUaBindingsPcap` installs a `PcapTransportChannelBinding` decorator over the TCP channel factory, and `ClientChannelManager` — which by default reads from that same global registry — picks the wrapped factory up automatically. There is no extra wiring code; the composition is pure layering at the transport binding level.

Three properties of the channel manager flow through to capture for free:

- **Sharing.** When multiple participants share a single `IManagedTransportChannel` (the refcount + key-equivalence model in `ManagedChannelKey`), the underlying `CapturingMessageSocket` records all participant traffic onto a single wire stream. One capture session covers a `Session`, a `DiscoveryClient`, and a `RegistrationClient` all targeting the same endpoint.
- **Transparent reconnect.** During the manager's coalesced reconnect cycle the wrapping socket is re-created against the new transport but the `IChannelCaptureRegistry` keeps recording into the same session file — the reconnect transition appears in the timeline as a state edge rather than a capture interruption.
- **Faulted-entry swap.** The `Phase E` `SwapFaultedEntryAsync` path produces a fresh `ChannelEntry` under the same `ManagedChannelKey`; the active capture observer rolls onto the new entry so an exhaustion-then-recover cycle is recorded end-to-end in one session.

If you compose the channel manager and the capture binding via DI the standard registration order is:

```csharp
services.AddOpcUa().AddClient(options => { });    // central IClientChannelManager
services.AddSingleton<OpcUaSessionManager>();     // your application services
services.AddOpcUaBindingsPcap();                  // capture binding + CaptureSessionManager
services.AddOpcUaBindingsPcapFormatters();        // service-timeline / pcap / pcapng / json / csv / text
services.AddOpcUaBindingsPcapReplay();            // mock-client / mock-server replay engines
```

Registration order does not strictly matter (the Pcap binding installs synchronously into the process-wide registry), but the order above reads top-down as "register the channel manager, then your services, then opt in to capture". For non-DI consumers, call `PcapBindings.Install()` at startup to achieve the same effect.

## Capture sources

**NIC (`nic`)** captures live traffic through SharpPcap. It requires libpcap on Linux/macOS or Npcap on Windows and can use interface names and BPF filters.

**In-process client (`inproc-client`)** taps OPC UA client channels in the current process. It uses `IFrameCaptureSink` for wire chunks and client token activation for key material. No native driver is required.

**In-process server (`inproc-server`)** passively taps channels accepted by a hosted OPC UA server. It uses the same frame sink plus `TcpListenerChannel.OnTokenActivated`. No native driver is required.

**Replay (`replay`)** reads an existing pcap or pcapng plus `.uakeys.json` or `.uakeys.txt`. Use it for offline decode, summaries, and mock-client or mock-server replay.

## Quick start: in-process client capture

```csharp
services.AddOpcUa().AddClient(options => { });   // central IClientChannelManager
services.AddOpcUaBindingsPcap();                  // capture binding + CaptureSessionManager

CaptureSessionManager manager = serviceProvider.GetRequiredService<CaptureSessionManager>();

var session = await manager.StartAsync(new StartCaptureRequest
{
    Source = CaptureSourceKind.InProcessClient,
    MaxDurationSeconds = 60,
    SessionFolder = "C:/captures"
}, ct);

// Do real OPC UA work here — every channel created through the central
// IClientChannelManager (Session.CreateAsync, ManagedSessionBuilder,
// DiscoveryClient, RegistrationClient, GDS clients, …) is automatically
// wrapped by the Pcap binding. Channel sharing means a single recording
// stream covers every participant on the same endpoint.

await manager.StopAsync(session.SessionId, ct);

var bytes = await manager.GetCaptureAsync(
    session.SessionId,
    FormatKind.ServiceTimeline,
    ct);
```

## Quick start: replay and decode an existing pcap

```csharp
services.AddOpcUaBindingsPcap();

CaptureSessionManager manager = serviceProvider.GetRequiredService<CaptureSessionManager>();

var session = await manager.StartAsync(new StartCaptureRequest
{
    Source = CaptureSourceKind.Replay,
    PcapFilePath = "C:/captures/issue-1423.pcap",
    KeyLogFilePath = "C:/captures/issue-1423.uakeys.json",
    SessionFolder = "C:/captures"
}, ct);

var timeline = await manager.GetCaptureAsync(
    session.SessionId,
    FormatKind.ServiceTimeline,
    ct);
```

## Quick start: mock-server replay

```csharp
services.AddOpcUaBindingsPcap();

CaptureSessionManager manager = serviceProvider.GetRequiredService<CaptureSessionManager>();

var replay = await manager.ReplayAsync(new ReplayPcapRequest
{
    PcapFilePath = "C:/captures/server-conversation.pcap",
    KeyLogFilePath = "C:/captures/server-conversation.uakeys.json",
    Mode = ReplayMode.MockServer,
    ListenEndpointUrl = "opc.tcp://localhost:4840/CapturedServer"
}, ct);
```

## Security profile coverage

Every stack-supported profile can be decoded because the offline decoder reuses `UaSCUaBinaryChannel`, `ChannelToken`, and the stack cryptography helpers:

- `Basic128Rsa15`
- `Basic256`
- `Basic256Sha256`
- `Aes128_Sha256_RsaOaep`
- `Aes256_Sha256_RsaPss`
- `RSA_DH_AesGcm`
- `RSA_DH_ChaChaPoly`
- `ECC_nistP256`, `ECC_nistP256_AesGcm`, `ECC_nistP256_ChaChaPoly`
- `ECC_nistP384`, `ECC_nistP384_AesGcm`, `ECC_nistP384_ChaChaPoly`
- `ECC_brainpoolP256r1`, `ECC_brainpoolP256r1_AesGcm`, `ECC_brainpoolP256r1_ChaChaPoly`
- `ECC_brainpoolP384r1`, `ECC_brainpoolP384r1_AesGcm`, `ECC_brainpoolP384r1_ChaChaPoly`
- `ECC_curve25519`, `ECC_curve25519_AesGcm`, `ECC_curve25519_ChaChaPoly`

## Transport coverage

- `opc.tcp`: full capture, decode, and replay support.
- `opc.tcp` reverse connect: full support; the TCP stream is decoded through the same Part 6 framing.
- `opc.https`: best-effort. The pcap can be captured, but decoding encrypted TLS payloads requires an external TLS keylog such as `SSLKEYLOGFILE`.

## MCP tool reference

| Tool | Description | Parameters |
|---|---|---|
| `list_interfaces` | Enumerates NICs available to SharpPcap. | None |
| `start_capture` | Starts a capture session. | `source`; optional interface, filter, endpoint, limits, folder |
| `stop_capture` | Stops an active session and finalizes artifacts. | `sessionId` |
| `list_captures` | Lists capture sessions. | Optional `state` |
| `get_capture` | Returns an artifact or formatted analysis. | `sessionId`, `format`, optional packet/partial controls |
| `capture_now` | Starts, waits, stops, and returns output. | Capture options plus output `format` |
| `list_active_channels` | Lists in-process secure channels with current tokens. | None |
| `dump_keys` | Emits keylog data. | Optional `sessionId`, `format`, `includeExpired` |
| `decode_pcap_with_keys` | Decodes an existing pcap and keylog offline. | `pcapPath`, `keylogPath`, `format`, optional `maxFrames` |
| `summarize_service_calls` | Reports service counts, latency, and errors. | `sessionId` or `pcapPath` + `keylogPath`, optional `top` |
| `replay_pcap` | Replays as a mock server or mock client. | `pcapPath`, `keylogPath`, `mode`, endpoints, `speed` |

## File format reference

See [PacketCaptureFileFormats.md](PacketCaptureFileFormats.md) for `.uakeys.json`, `.uakeys.txt`, pcap synthesis, and Wireshark interop details.

## Security considerations

> **Keylog files are secrets.** They contain symmetric keys that grant full decrypt access to captured payloads. Keep them in the per-session folder, do not commit them, restrict access, and redact before sharing.

## Limitations

- The stack does not produce live TLS keylogs for `opc.https`; provide an external TLS keylog when TLS decryption is required.
- PubSub UDP and MQTT capture are out of scope for this feature.
- A Wireshark Lua plugin is not included. The text keylog format is documented so a plugin can be added later.

## Troubleshooting

- `list_interfaces` returns empty: install libpcap or Npcap and ensure the process has permission to enumerate adapters.
- `decode_pcap_with_keys` returns 0 service calls: verify the pcap and keylog are from the same capture and include the same channel and token ids.
- Frames are captured but keys are missing: start the in-process capture before opening the secure channel so token activation is observed.
