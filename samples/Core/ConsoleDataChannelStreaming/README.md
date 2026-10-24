# Complete-message Data Channel streaming

This **.NET 10** executable exercises the experimental rewritten Data Channels
protocol over real authenticated loopback sockets. It replaces the previous
draft's three Services, frame flags, delivery modes and direct-manager mode.
Identifiers remain provisional; this is not a conformance or performance guarantee.

`StreamingHarness` composes production carrier factories. `MessageWorkload` uses
`DataChannel.SendAsync`, owned `DataChannelMessage` instances, live revision,
and directional `END`. The same opaque application workload serves all four
carriers and the audio sample. UASC uses the amended UADP type-3 opaque profile
with its minimal PublisherId header; native carriers use the complete opaque
message profile. The harness does not add a private payload format, transport
registry, readiness shortcut or fallback transport.

## Build and finite runs

Run these PowerShell commands from the repository root using the SDK pinned by
`global.json`:

```powershell
$env:CustomTestTarget = 'net10.0'
dotnet build samples\Core\ConsoleDataChannelStreaming\ConsoleDataChannelStreaming.csproj -c Debug -f net10.0

# Configured dedicated FLO/UASC: no Session, ordinary Service lane or optional model.
dotnet run --project samples\Core\ConsoleDataChannelStreaming --no-build --no-restore -- --carrier dedicated --count 8 --size 64 --revise

# Shared FLO/UASC uses the exact authenticated transport for ordinary Read and Publish.
dotnet run --project samples\Core\ConsoleDataChannelStreaming --no-build --no-restore -- --carrier shared --count 80 --size 64 --interval-ms 20 --revise

# Generated normal Call Methods issue grants; OPEN/ACCEPT/READY still establish the flow.
dotnet run --project samples\Core\ConsoleDataChannelStreaming --no-build --no-restore -- --carrier shared --authorization session --count 80 --size 64 --interval-ms 20 --revise

# The transport initiator/flow opener can also be the payload sender.
dotnet run --project samples\Core\ConsoleDataChannelStreaming --no-build --no-restore -- --carrier dedicated --publisher client --count 8 --size 64 --revise

# A bounded comparison; enough real time is allowed for competing notifications.
dotnet run --project samples\Core\ConsoleDataChannelStreaming --no-build --no-restore -- --carrier shared --mode benchmark --count 80 --size 64 --interval-ms 20 --monitored-items 3 --repeat 1

# Demonstrate cancellation after READY, not during certificate setup. Expected exit: 130.
dotnet run --project samples\Core\ConsoleDataChannelStreaming --no-build --no-restore -- --count 1000000 --size 64 --interval-ms 10 --cancel-after-ms 100
```

The default payload sender is the listening server; the connecting client opens
the flow. Thus the default descriptor is **AcceptorToOpener**. `--publisher client`
selects **OpenerToAcceptor** without changing connection initiation. This is payload
role reversal, not reverse-transport UASC, whose ambiguous shared combination is
not offered by the sample.

All messages use Ordered delivery, Confidential protection, no queue expiry, one
exact opaque resource and explicit finite bounds. `--size` accepts 4–32768
application bytes; UASC's message bound includes the seven-byte UADP header.
The first four bytes are a little-endian sample counter, not an OPC UA payload
standard. Every received byte and decoded counter is checked. A transient
`WouldBlock` send or `END` result is retried through the same public facade under
the finite deadline; it is not treated as successful delivery or a reason to
discard/reframe data.

`--revise` changes priority halfway through the messages. It waits until the first
half has arrived, then uses the opener's actual revision transaction and requires
revision 2 on both peers. In Session mode the generated control helper stages the
proposal using `Call`; the data-path ACK, not the Call response, completes it.
Dedicated/native Session mode explicitly opens a **separate ordinary TCP control
connection**. Configured dedicated/native mode creates no Session and needs no model.

## Native QUIC and WebTransport prerequisites

**The current native acceptance gate is blocked.** The managed native loader now
requires ABI 4, while the last completed sample checkpoint below used a coherent
ABI 3 closure. A qualified matching ABI 4 closure and resolution of WebTransport's
peer-STOP/minimum-reliable-header requirement are still required. Do not reuse an
old ABI 3 runtime as a fallback or treat the historical passing sample runs as
proof that this newer transport-conformance gate is satisfied.

Native modes require matching reviewed **Windows x64 or Linux x64** assets and the optional
binding dependencies. The generic QUIC backend must expose
the strict ABI required by the matching managed loader (**currently ABI 4**),
including actual reset/peer-STOP observation and verified mutual-authentication
evidence. Older ABIs are intentionally rejected; do not mix an old native image
with the current managed binding. WebTransport additionally needs
the matching native HTTP/3 **draft-16** implementation. BCL QUIC, WSS, Kestrel
draft-02 and browser WebTransport are not substitutes.

Select assets through these environment variables, or equivalent command-line
options:

| Environment variable | Required value |
| --- | --- |
| `OPCUA_QUIC_LIBRARY` | Absolute path to the independently reviewed `opcua_quic_go.dll` or `libopcua_quic_go.so` |
| `OPCUA_QUIC_SHA256` | Expected 64-hex SHA-256 from the deployment/review receipt |
| `OPCUA_HTTP3_LIBRARY` | Absolute path to the reviewed `opcua_http3.dll` or `libopcua_http3.so` |
| `OPCUA_HTTP3_SHA256` | Expected HTTP/3 image SHA-256 from that receipt |

The native CI fixture names are accepted aliases: `OPCUA_QUIC_NATIVE_PATH`,
`OPCUA_QUIC_NATIVE_SHA256`, `OPCUA_HTTP3_NATIVE_PATH` and
`OPCUA_HTTP3_NATIVE_SHA256`. Expected hashes are required in addition to paths;
there is no implicit trust of whichever binary happens to exist.

Do not derive a trusted expected hash from an arbitrary file being loaded.
The sample does not search `PATH`, download assets, accept an unknown certificate,
or silently change carrier. Missing assets, unsupported platform/key capability,
hash mismatch or handshake failure produce a nonzero exit.

With those explicit deployment inputs already set:

```powershell
if (!(Test-Path $env:OPCUA_QUIC_LIBRARY)) { throw 'Select the reviewed QUIC asset first.' }
dotnet run --project samples\Core\ConsoleDataChannelStreaming --no-build --no-restore -- --carrier quic --count 8 --size 64 --revise

if (!(Test-Path $env:OPCUA_HTTP3_LIBRARY)) { throw 'Select the reviewed draft-16 HTTP/3 asset first.' }
dotnet run --project samples\Core\ConsoleDataChannelStreaming --no-build --no-restore -- --carrier webtransport --count 8 --size 64 --revise

# Native payload role reversal and independently selected ordinary Session authorization.
dotnet run --project samples\Core\ConsoleDataChannelStreaming --no-build --no-restore -- --carrier quic --publisher client --count 8 --size 64 --revise
dotnet run --project samples\Core\ConsoleDataChannelStreaming --no-build --no-restore -- --carrier webtransport --authorization session --count 8 --size 64 --revise
```

Native configured modes use mTLS directly, with no implicit UASC or Client/Server
lane. WebTransport explicitly permits **absent Origin only** and authorizes the
exact loopback CONNECT endpoint and authenticated peer. The library performs
SETTINGS, CONNECT, stream association and protected HELLO.

The certificate-manager native bridge currently requires an exportable software
application key. HSM/non-exportable keys unsupported by that ABI are refused;
there is no raw-PFX, system-trust or alternate-key fallback.

## Trust, measurements and cleanup

Each run creates two owned application identities using `ApplicationInstance`,
certificate managers/stores and a random password held by the secret provider.
Each manager trusts only the other application's public certificate. Resource
policies separately pin the peer application URI and public-key SHA-256, payload
direction, content type, bounds and selected authorization mode. Session control
uses the client certificate as an explicitly trusted X.509 user identity.

State is created in a fresh repository-local directory and removed on normal
completion, failure or cooperative cancellation. `--state NEW-DIRECTORY` selects
that owned directory explicitly; an existing path is refused. Keys and passwords
are never embedded in code or printed.

Success produces one `SUMMARY` JSON line **after cleanup**:

- exact message/byte counts, independent sent/received SHA-256 and committed revision;
- actual ContextId, flow ChannelId and UASC SecureChannelId;
- ordinary Service SecureChannelId plus transport-reference equality in shared mode;
- real server Session/Read/Publish/Call counters and decoded notification counts;
- first-send through directional-EOF time and measured received-payload throughput;
- zero application budget/native ownership evidence.

Benchmark lines are separate `BENCHMARK` records for unloaded/loaded runs. Each
run uses a fresh authenticated connection; setup time is excluded. A loaded run
with no completed competing Read/Publish cycle is rejected, not advertised as a
successful scheduling measurement. Increase count/pacing for such a run.
These loopback numbers include application verification and any requested pacing;
they make **no one-frame Service-latency claim**.

## Acceptance tests and validation status

The process tests run the actual executables, compare known exact bytes, check
Session/model independence and real shared-channel identities, parse finalized
WAV data, exercise refusal/cancellation, and rebind released ports.

```powershell
dotnet test tests\Opc.Ua.DataChannel.Samples.Tests\Opc.Ua.DataChannel.Samples.Tests.csproj -f net10.0 --filter 'TestCategory!=NativeDataChannelSamples'
# Requires the reviewed native asset environment variables above; missing assets fail, never skip.
dotnet test tests\Opc.Ua.DataChannel.Samples.Tests\Opc.Ua.DataChannel.Samples.Tests.csproj -f net10.0 --filter 'TestCategory=NativeDataChannelSamples'
```

Historical **ABI 3 integration checkpoint**, preceding the blocked ABI 4 gate,
verified independently on **Windows x64 and Linux x64/.NET 10**:
**53 passed,
0 failed, 0 skipped on each platform**, covering all four real carriers, both payload
roles, configured/Session control, live revision, actual shared Read/Publish traffic,
headless WAV output, rejection and cancellation/cleanup. Separate finite Windows
CLI runs also completed for every carrier. Actual Windows playback completed a
200 ms run and drained the device. Earlier ABI 2 receipts are retained separately
as historical evidence, not reused for this checkpoint. A correctly hashed ABI 2
image was also explicitly refused by the current executable with a nonzero exit,
no listener and no success summary.

The coherent ABI 3 runs used the matching Core/QUIC/WebTransport managed outputs
and these independently supplied native-image hashes:

| Asset | Windows x64 SHA-256 | Linux x64 SHA-256 |
| --- | --- | --- |
| QUIC ABI 3 | `1027ed447b30c1447595df907fc081ed797f0ee4f2536ed4d16374274e5780a8` | `764e401b8280d384d401c9367e3186b9d73ea2d004418ffeb19e7c830929a726` |
| HTTP/3 draft-16 | `e4d27eef05b32727d6946b197ba504b852e4a92e1538447a6554b968dc941c49` | `3da96b4a830be760e2fa3589844b6495208b471e5cb99698899cdcb0578aa8ff` |

This is historical sample integration evidence for that candidate closure,
**not current whole-carrier/lane conformance or approval of a final native
distribution**, signing/SBOM or NativeAOT package gate. In particular, the later
peer-STOP/minimum-header regression is not covered by those passing receipts.

Linux acceptance executed the portable managed outputs, not just a cross-build.
The bounded runner below uses the already cached qualified public SDK 10.0.401
image. It never pulls an image, restores packages, builds a repository mirror or
installs a native toolchain. Point the four environment variables at the reviewed
**Linux ELF assets on the host**, then run:

```powershell
.\tests\Opc.Ua.DataChannel.Samples.Tests\Test-NativeLinux.ps1 `
    -QuicLibrary $env:OPCUA_QUIC_NATIVE_PATH -QuicSha256 $env:OPCUA_QUIC_NATIVE_SHA256 `
    -Http3Library $env:OPCUA_HTTP3_NATIVE_PATH -Http3Sha256 $env:OPCUA_HTTP3_NATIVE_SHA256 `
    -ResultsDirectory .git\testagent\dc-samples\linux-validation
```

Build the portable sample/test outputs and restore the existing centralized
`coverlet.collector` reference first. Results must name a new or empty directory:
stale reports cannot satisfy acceptance. The runner copies only portable output
closures, the existing collector and native assets (about 85 MiB in the verified
run), limits the container to two CPUs/2 GiB/240 seconds, uses no external network,
and removes its exact container and staging directory. All 53 cases really ran.

The common CI coverage collector is supported and produced its attachment.
It does **not** instrument the child sample executables, so that attachment's
empty in-process coverage is not represented as sample line/branch coverage.
Other platforms, browser interoperability, sample NativeAOT publication, and final
native package signing/distribution are not claimed by this sample.
