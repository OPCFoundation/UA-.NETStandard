# Finite opaque audio over Data Channels

This **.NET 10** sample sends the existing generated melody as complete opaque
messages over the same production flow facade and carrier setup as
[ConsoleDataChannelStreaming](../ConsoleDataChannelStreaming/README.md).
It offers hardware-independent WAV output and actual NAudio Windows playback.
No media codec, packetization protocol or new OPC UA payload standard is introduced.

The exact application resource identifies **44100 Hz, signed 16-bit little-endian,
mono PCM**. Its ContentType is `application/octet-stream`, not `audio/L16`:
registered L16 has big-endian sample order. Message boundaries always preserve
whole PCM samples, including when a message crosses the repeating melody boundary.
For durations not exactly representable at 44100 Hz, each message's sample count
rounds down; reported byte counts are the actual produced/received sample counts.

## Headless acceptance

Run from the repository root with its pinned SDK. Output paths must be new;
existing files are never overwritten.

```powershell
$env:CustomTestTarget = 'net10.0'
dotnet build samples\Core\ConsoleDataChannelAudio\ConsoleDataChannelAudio.csproj -c Debug -f net10.0

# Ten complete messages, 17640 PCM bytes, no sound device dependency.
dotnet run --project samples\Core\ConsoleDataChannelAudio --no-build --no-restore -- --carrier dedicated --wav received-dedicated.wav --duration-ms 200 --frame-ms 20 --revise

# The same actual data connection also carries ordinary Read and Publish.
dotnet run --project samples\Core\ConsoleDataChannelAudio --no-build --no-restore -- --carrier shared --wav received-shared.wav --duration-ms 200 --frame-ms 20 --revise

# Explicit optional Session grants and live data-path revision.
dotnet run --project samples\Core\ConsoleDataChannelAudio --no-build --no-restore -- --carrier shared --authorization session --wav received-session.wav --duration-ms 200 --frame-ms 20 --revise

# Sender role is independent of the transport initiator/flow opener.
dotnet run --project samples\Core\ConsoleDataChannelAudio --no-build --no-restore -- --carrier dedicated --publisher client --wav received-client.wav --duration-ms 200 --frame-ms 20
```

Headless mode writes as fast as the bounded flow and file sink allow; it does not
sleep to simulate sound hardware. `--duration-ms` must be a positive multiple of
`--frame-ms` (1–250). The run is finite even without Ctrl-C. Success requires exact
received bytes, finalized RIFF/WAVE output and complete flow/socket/PKI cleanup.
The `SUMMARY` SHA-256 covers received PCM, not the RIFF header.

Each owned `DataChannelMessage` remains alive until its asynchronous sink write
completes, then is disposed to release receive bytes and message reservations.
The producer sends directional `END`; the receiver consumes EOF. Cancellation
disposes both flows and all carrier resources. A partially written file after a
failed/cancelled run is not reported as a complete result.

## Playback

On Windows with a working default audio output device:

```powershell
dotnet run --project samples\Core\ConsoleDataChannelAudio --no-build --no-restore -- --playback --carrier dedicated --duration-ms 1000 --frame-ms 20
```

Playback uses the existing NAudio dependency, bounded buffering and real-time
producer pacing. It drains the device buffer before finishing. Missing hardware
or unsupported platform does not silently become WAV success. Use `--wav` for
unattended checks; NAudio 2.x has no Linux ALSA or macOS CoreAudio playback backend.

## Native modes

**Current native acceptance is blocked** pending a qualified coherent ABI 4
closure and the WebTransport peer-STOP/minimum-reliable-header decision. The
historical ABI 3 results below must not be represented as acceptance of the newer
gate, and old native assets must not be used as a fallback.

Use the reviewed native assets and explicit path/hash environment variables
documented in the [streaming sample](../ConsoleDataChannelStreaming/README.md#native-quic-and-webtransport-prerequisites).
These require the strict native QUIC ABI expected by the matching managed loader
(currently ABI 4) and, for WebTransport, native HTTP/3 draft-16. Older ABIs are rejected.
The native carriers are not WSS or a browser compatibility demo.

```powershell
dotnet run --project samples\Core\ConsoleDataChannelAudio --no-build --no-restore -- --carrier quic --wav received-quic.wav --duration-ms 200 --frame-ms 20 --revise
dotnet run --project samples\Core\ConsoleDataChannelAudio --no-build --no-restore -- --carrier webtransport --wav received-webtransport.wav --duration-ms 200 --frame-ms 20 --revise
```

Configured dedicated/native flows need no Session or optional model. Native TLS
uses the existing certificate manager, exact peer trust and explicit software-key
capability checks; unsupported non-exportable/HSM identities are refused.
`--authorization session` explicitly adds a separate ordinary control connection
for native/dedicated carriers, not an implicit UASC data lane.

## Automated verification

```powershell
dotnet test tests\Opc.Ua.DataChannel.Samples.Tests\Opc.Ua.DataChannel.Samples.Tests.csproj -f net10.0 --filter 'FullyQualifiedName~AudioProcessTests&TestCategory!=NativeDataChannelSamples'
# Reviewed native assets must already be configured; absence is a failure, not a skip.
dotnet test tests\Opc.Ua.DataChannel.Samples.Tests\Opc.Ua.DataChannel.Samples.Tests.csproj -f net10.0 --filter 'FullyQualifiedName~AudioProcessTests&TestCategory=NativeDataChannelSamples'
```

Tests parse actual RIFF chunks, require PCM format 1/mono/44100 Hz/16 bits, exact
data length and known initial sample values, and compare the entire data-chunk
digest to the received payload digest. Additional cases cross the melody boundary,
exercise sample alignment and preserve an existing output file.

Historical **ABI 3 integration checkpoint**, preceding the blocked ABI 4 gate,
verified independently on **Windows x64 and Linux x64/.NET 10**:
headless WAV
output over all four real carriers, sample alignment, the melody boundary and output
preservation, with **53 passed, 0 failed, 0 skipped on each platform** in the full
process suite. Linux ran the actual portable executables against the qualified ELF
backends in the cached SDK image; it was not merely cross-built. Earlier ABI 2
receipts remain separate historical evidence. The matching native image hashes
and managed-binding requirements are recorded in the streaming sample's
[historical integration checkpoint](../ConsoleDataChannelStreaming/README.md#acceptance-tests-and-validation-status).
Those passing runs do not satisfy the later transport-conformance blocker or approve
a final native distribution, signing/SBOM or NativeAOT gate.

A separate Windows shared Session-authorized WAV run delivered 10 messages /
17640 PCM bytes at revision 2. Actual Windows default-device playback also completed
a finite 200 ms run and drained its buffer. See the
[streaming sample's bounded Linux runner](../ConsoleDataChannelStreaming/README.md#acceptance-tests-and-validation-status)
for reproducible no-download/no-mirror execution.

No Linux playback, browser, other-platform or sample NativeAOT result is claimed.
