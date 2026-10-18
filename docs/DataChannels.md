# Data Channels

**Experimental protocol replacement.** This implementation targets OPC 10000-28 Data Channels
draft **0.2.0**, information-model version **2.0.0**, pinned to
[`OPCF-Members/OPC10000-Core@673b3cf`](https://github.com/OPCF-Members/OPC10000-Core/tree/673b3cfde6b2c8d6510d5b598dec1180c0add205/source/data-channels).
Identifiers and registrations remain provisional. This is not a claim of
ratification, certified conformance, or compatibility with the earlier experimental
implementation. See [migration](#migrating-from-the-earlier-experiment) and
[limitations](#current-limitations) before deploying.

This commit changes the wire format without changing the draft/model version
strings or profile identifiers. Both peers must use the same wire revision;
matching `0.2.0` or `2.0.0` alone is insufficient.

A Data Channel carries **complete application messages** over an authenticated
association. The common runtime owns establishment, authorization, readiness,
bounded queues, revisions and closure. Applications choose the carrier separately
from the payload protocol. Configured flows require neither a Session nor an
address-space model; optional Session control uses generated Methods through
ordinary `Call`, not a new Service Set.

## Start with a working sample

The [brokerless PubSub sample](../samples/PubSub/ConsoleSecureChannelPubSub/README.md)
starts both peers on real loopback sockets and validates changing DataSet values,
complete NetworkMessage bytes, a live revision and cleanup. From the repository
root, using the SDK selected by `global.json`:

```powershell
dotnet run --project samples\PubSub\ConsoleSecureChannelPubSub --property:CustomTestTarget=net10.0
```

The default uses confidential Dedicated UASC, UADP and configured authorization.
It creates no Session or optional Data Channel model and needs no broker, native
library, pre-existing server or certificate-validation bypass.

```powershell
# JSON, with ordinary Read/Publish on the same shared SecureChannel.
dotnet run --project samples\PubSub\ConsoleSecureChannelPubSub --property:CustomTestTarget=net10.0 -- --shared --encoding json

# Add X.509 Session authorization and reverse the Publisher role.
dotnet run --project samples\PubSub\ConsoleSecureChannelPubSub --property:CustomTestTarget=net10.0 -- --shared --authorization session --reverse --count 12
```

The sample README also documents separate server/client processes. `--reverse`
changes the Publisher role, not transport connection initiation.

| Sample | Outcome |
| --- | --- |
| [SecureChannel PubSub](../samples/PubSub/ConsoleSecureChannelPubSub/README.md) | UADP/JSON, dedicated/shared UASC, configured/Session authorization, one or two processes |
| [Streaming](../samples/Core/ConsoleDataChannelStreaming/README.md) | One complete-message workload across all four carriers; finite runs and a shared-channel Read/Publish benchmark |
| [Audio](../samples/Core/ConsoleDataChannelAudio/README.md) | Complete opaque PCM messages; finite headless WAV output or explicit Windows playback |

For example, these finite managed-carrier runs need no native assets:

```powershell
$env:CustomTestTarget = 'net10.0'
dotnet run --project samples\Core\ConsoleDataChannelStreaming -- --carrier dedicated --count 8 --size 64 --revise
dotnet run --project samples\Core\ConsoleDataChannelAudio -- --carrier shared --wav received.wav --duration-ms 200 --frame-ms 20 --revise
```

Choose a new WAV filename; the audio sample refuses to overwrite an existing
file. The samples manage owned demo identities through the certificate manager
and stores, explicitly trust their peer certificates, and fail on invalid input
or unsuccessful cleanup. Their READMEs describe production trust and secret
provisioning separately.

## Select a carrier and a payload

Use `Opc.Ua.DataChannels.DataChannelProfiles`, rather than duplicating URI strings.
Profile matching is exact; an unavailable profile is not substituted with another.

| Carrier | Profile constant | Endpoint and security | Ordinary Services |
| --- | --- | --- | --- |
| Dedicated FLO/UASC | `DedicatedUasc` | `opc.tcp` or binary `opc.wss`, resource `/flow/v1/dedicated`; protected UASC | Not dispatched on this endpoint |
| Shared FLO/UASC | `SharedUasc` | `opc.tcp` or binary `opc.wss`, resource `/flow/v1/shared`; protected UASC | Share the authenticated SecureChannel |
| Native QUIC | `Quic` | `opc.quic`, ALPN `opcua-flow/1`; mutually authenticated TLS 1.3 | Only through the separately selected Client/Server lane |
| Native WebTransport | `WebTransport` | `opc.wt`, HTTP/3 and WebTransport draft-16; mutually authenticated TLS 1.3 | Only through the separately selected Client/Server lane |

Both UASC profiles support raw TCP, Kestrel TCP and binary WSS
(`opcua+uacp`). WSS is not WebTransport. HTTPS request/response, JSON WebSocket,
Kestrel's older WebTransport draft and browser WebTransport are not substitute
carriers.

UASC flow endpoints reject `SecurityMode.None`. Confidential protection is the
default. Integrity-only requires explicit binding permission **and** resource
policy approval. Native endpoints use TLS/mTLS without an outer UASC layer;
their discovery `None` fields do not mean unprotected data.

The payload constants are independent of the carrier:

| Constant | Payload |
| --- | --- |
| `UadpPayload` | A complete OPC UA Part 14 UADP NetworkMessage |
| `JsonPayload` | A complete OPC UA Part 14 JSON NetworkMessage |
| `OpaquePayload` | An application-defined complete message |

Directions are relative to the **flow opener**: `OpenerToAcceptor`,
`AcceptorToOpener` or `Duplex`. The transport initiator, flow opener and PubSub
Publisher are different roles. Ordered and Datagram delivery are also distinct
contracts; a datagram request never silently becomes an ordered stream.

## Compose the application

The common runtime is in `Opc.Ua.Core`, under `Stack\DataChannels`.
`DataChannelContext` represents one authenticated association. The carrier
validates transport security and supplies its identity evidence before protected
HELLO. `OpenAsync` and `AcceptAsync` return a usable `DataChannel` only after
OPEN/ACCEPT/READY; receipt of a Session grant alone does not establish a flow.

Use the injected carrier factories and context factory, or their direct
construction equivalents. One `DataChannelApplicationBudget` shared across the
application bounds aggregate contexts, flows, bytes, messages, operations, grants,
probes and streams. `DataChannelOptions` bounds each context and flow, with an
injected `TimeProvider` for timers. Do not create a fresh application budget per
connection and expect an application-wide limit.

For UASC, supply `TransportChannelSettings.DataChannels` and connect through
`UascDataChannelClient`. Its `UascDataChannelConnection` exposes the ready
`Context`; `Services` is available only for Shared UASC. Server hosting attaches
the provider through `UseDataChannels(provider, enableModel: false)` or the
`IOpcUaServerBuilder.AddDataChannels` and `AddDataChannelSource` extensions.
No provider means no flow endpoint.

For native transport construction and deployment, see the
[QUIC binding](../src/Opc.Ua.Bindings.Quic/NugetREADME.md) and
[WebTransport binding](../src/Opc.Ua.Bindings.WebTransport/NugetREADME.md).
Native dependencies remain outside Core, ordinary Client/Server and base PubSub.

### Complete-message I/O

Once a channel is ready, application code uses the same facade for every carrier.
The following send assumes `flow`, a complete `ByteString payload` and a
`CancellationToken cancellationToken` supplied by the application:

```csharp
DataChannelResult result = await flow.SendAsync(
    payload,
    cancellationToken: cancellationToken);
if (result != DataChannelResult.Good)
{
    throw new InvalidOperationException($"Data Channel send failed: {result}.");
}
```

Check the result rather than treating admission pressure as delivery.
`WouldBlock` can be retried under the application's finite deadline and pacing
policy. `SendAsync` copies the complete message into bounded owned storage.
Queue expiry and cancellation can prevent commitment; after commitment, the
writer retains its buffers and preserves the secured write even if the caller
stops waiting.

Every receive returns an owned message. Keep that lease alive through processing:

```csharp
while (true)
{
    using DataChannelMessage? message = await flow.ReadAsync(cancellationToken);
    if (message is null)
    {
        break;
    }

    Console.WriteLine($"Received {message.Payload.Length} bytes.");
}
```

`null` means directional EOF, not a reset or authorization success fallback.
Disposal releases both byte and message-count capacity. Do not retain payload
memory after disposing its message. `EndAsync` drains the local sending direction;
the other direction of a duplex flow can continue. Observe its result and the
peer's EOF before reporting orderly completion. `ResetAsync` and asynchronous
disposal join flow resource release without closing unrelated flows.

`Parameters`, `GetStatus`, `GetDiagnostics` and `StateChanged` expose the admitted
descriptor, outcomes and lifecycle. `UpdateAsync` performs the revision transaction;
changing a local descriptor object is not a negotiated update. A repeated
operation uses the same nonzero operation ID and proposal. A committed update
cannot be rolled back by caller cancellation.

### Connection and Session ownership

A flow borrows its `Context`; owning one flow does not authorize disposing the
association used by other flows. The UASC connection or native transport owner
owns that association.

To create a Session on Shared UASC, use the ordinary session factory's
transport-taking `Create` overload with `connection.Services`, then open the
Session. A configuration-only `CreateAsync` opens another transport and does not
demonstrate sharing. The samples check transport identity and SecureChannel IDs
as well as actual Read/Publish traffic.

Finish the flows before disposing their transport. When closing only a shared
Session, use `CloseAsync(..., closeChannel: false)`; its later disposal still owns
the supplied transport. The samples close the Session, close the connection,
then dispose the Session. Certificate handles passed to transport settings are
owning handles: use independent `AddRef()` handles when retaining the originals,
and release handles not transferred during failed establishment.

After native WebTransport ingress is attached, readiness failures retain the
HTTP/3 or CONNECT rejection instead of being masked by the transport's close
token. Caller cancellation and the configured establishment deadline remain
independent.

## Optional model and Session authorization

Configured authorization uses an explicit `IDataChannelAuthorizer` on both peers.
Server applications can register exact resources and an `IDataChannelSourcePolicy`
with `DataChannelSourceRegistry` and `ServerDataChannelProvider`. Unknown resources
are denied. Configured flows do not become dependent on Sessions merely because
the application also exposes an ordinary server.

The independently enabled model provides the generated manager, sources,
capabilities, offers, events and per-Session diagnostics. The manager's
`OpenDataChannel`, `ModifyDataChannel` and `CloseDataChannel` are **Methods invoked
through normal Call**, not request/response Service types.

`DataChannelSessionControl` uses the generated manager proxy. Given an ordinary
Session, a `SessionDataChannelAuthorizer` wrapping the local resource policy and
the same clock used by that authorizer:

```csharp
var control = new DataChannelSessionControl(session, authorizer, clock);
DataChannel flow = await control.OpenAsync(
    context, sourceNodeId, parameters, cancellationToken);
```

The helper reserves an ID, obtains and validates the single-use grant, and waits
for actual flow readiness. Both peers still enforce resource policy. Session
permissions are additional checks, not replaced by application policy.
`control.ModifyAsync` stages the complete update through Call and waits for its
data-path outcome; the Call response alone is not a committed revision.

Grants are bound to the authenticated context, peer, challenges and ChannelId.
They do not carry an AuthenticationToken or private key. A disconnected control
transport is not a closed Session. Session expiry, closure or revoked
identity/permission invalidates its grants and dependent flows; configured
neighbours remain independent. Context loss invalidates all flows on that context.
Reconnect requires fresh context identity, IDs and authorization.

The provider owns one accepted-flow router. Consumers select their exact
resource/protocol/carrier through that router; they must not race separate
`Context.AcceptAsync` calls against it.

## PubSub over the flow

The adapter in `Opc.Ua.PubSub.Transports.DataChannels` implements the existing
`IPubSubTransportFactory` / `IPubSubTransport` contracts. It reuses Part 14
**1.05.06** writers, readers, metadata, diagnostics, encoders and security.
This feature does not upgrade the Part 14 version.

Configure the connection with `DataChannelPubSubConfiguration.Configure` or
`PubSubConnectionBuilder.WithDataChannel`. Register the provider using
`PubSubApplicationBuilder.UseDataChannelTransport` or
`IPubSubBuilder.AddDataChannelTransport`. See the
[configuration example](../src/Opc.Ua.PubSub/NugetREADME.md#brokerless-datachannel-transport)
and [PubSub guide](PubSub.md#data-channels-experimental).

The negotiated payload protocol selects UADP or JSON; the carrier URI cannot
choose the encoding. One complete encoded NetworkMessage is one flow payload,
with every byte preserved. ChannelId never replaces PublisherId, WriterGroupId
or DataSetWriterId. Multiple writers can share an authorized flow; different
resource permissions, delivery contracts or lifetimes need separate flows.

The PubSub adapter rejects `AllowQueueExpiry=true`. Dropping a key frame or
metadata can invalidate later messages, so enabling expiry requires a
Publisher-side dependency and recovery policy that this adapter does not yet
provide. It neither silently clears the option nor parses or rewrites payloads
to simulate recovery. Default no-expiry UADP and JSON delivery is unchanged.

Carrier encryption is independent of optional Part 14 message security.
SecureChannel protection alone requires no broker or SKS. Existing UADP
signing/encryption remains available through its normal key provider; JSON does
not silently satisfy a UADP message-security requirement.

## Optional native Client/Server lane

Native configured flows do not create UASC or Sessions. An application can
explicitly select `DataChannelProfiles.ClientServerResource` for an ordered,
duplex, confidential, opaque flow with no queue expiry. The common byte adapter
carries one complete UACP/UASC chunk per DATA message and reuses ordinary
Client/Server dispatch inside it.

The lane, optional model and Session authorization are separate choices. See
[Client/Server lane integration](DataChannelClientServer.md) for direct and DI
composition and its native prerequisites. Do not start competing accepted-flow
readers when hosting the lane beside PubSub or other resources.

## Wire and resource boundaries

The common record has a 13-byte header and either opaque DATA or binary CONTROL.
CONTROL uses opcodes `0..14`, ordered little-endian fields, OPC UA Binary Guid
layout, strict length-prefixed UTF-8 strings, and an exactly 32-byte challenge.
Booleans are `0` or `1`; rates must be finite and nonnegative, with both zero
representations treated as unlimited for ordered delivery. Datagram rates must
be strictly positive. A Configured OPEN has no grant bytes;
a Session OPEN has exactly one Guid. There is no JSON CONTROL fallback.

Both dedicated and shared UASC use `FLOF`, RequestId zero, and a four-byte
ChannelId followed by exactly one common record. Dedicated UASC admits only
management zero and DATA one, never ordinary MSG Services or CREDIT. Native
datagrams also carry an external ChannelId.
The runtime rejects malformed framing, noncanonical control values, invalid
association/stream ownership and premature application data; applications do
not implement a second parser.

Shared FLO credit starts at zero and counts encoded flow bytes. Both UASC
carriers and native datagrams have 17 bytes of DATA overhead; native ordered
streams have 13. Native ordered flows backpressure at the record header before
allocating the body. Physical UASC ingress reserves a bounded context/application
queue and waits at most `DrainTimeout` for DATA queue capacity. If the application
does not drain it, the affected flow fails instead of indefinitely preventing
later management processing. Native datagrams
have finite admission, rate and replay budgets rather than stream-credit
semantics.

Native parser staging, incomplete records, queued messages and application-held
leases share one application budget. DATA queue handoff transfers the existing
ingress allocation and reservation; control admission covers overlapping encoded
and decoded-value storage. Both new revision directions stay reserved until
their directional barriers complete, and retained old messages keep their old
revision charged.

Each context withholds **512 bytes and four message slots** for fixed-field
management/control progress, including a directional revision exchange and its
CREDIT/ACK replies. DATA and generic carrier allocations cannot borrow this
headroom. Unused headroom is not reported as retained `BufferedBytes` or
`Messages`; it returns when the context closes. Larger establishment controls
still need additional application admission. Native streams also reserve their
two 13-byte parser staging areas before allocation.

UASC flow chunks are final chunks and use the same per-sender security sequence
as MSG/OPN/CLO. Numbering and writer admission remain atomic. The unsealed writer
selects a pending control after at most eight consecutive Service messages;
Services remain ahead of DATA. Already numbered or in-flight writes, including
coalesced writes, cannot be reordered. FLO CONTROL on opc.wss is binary
application data, not an RFC6455 control frame. These policies imply no
wall-clock latency or one-frame Service-delay guarantee.
Benchmark results include their actual load, pacing and loopback setup;
numbers from the discarded framing are not evidence for this protocol.

## Native deployment

The optional native bindings target .NET 8 or newer. Native execution qualification
uses .NET 10 on Windows x64 and glibc Linux x64. The backend packages and their
deployment contracts are:

- [Native.QuicGo](../src/Opc.Ua.Native.QuicGo/NugetREADME.md): pinned quic-go,
  mutual-authentication and confirmed-handshake evidence, real datagrams and
  reliable-reset observation. Load one compatible Go image for the process
  lifetime; never unload it.
- [Native.Http3](../src/Opc.Ua.Native.Http3/NugetREADME.md): the separate bounded
  draft-16 HTTP/3 state engine using the same QUIC association, not a second
  QUIC/TLS stack.

Select exact reviewed native paths and expected SHA-256 values. The streaming
sample documents `OPCUA_QUIC_NATIVE_PATH`, `OPCUA_QUIC_NATIVE_SHA256`,
`OPCUA_HTTP3_NATIVE_PATH` and `OPCUA_HTTP3_NATIVE_SHA256`. Missing prerequisites,
wrong ABI/layouts, hash mismatch or insufficient authentication fail explicitly;
there is no PATH search, automatic download or carrier downgrade.

The certificate bridge uses certificate-manager identities and explicit peer
policy. The current native key interface requires supported exportable software
keys; unsupported HSM/non-exportable signing is refused, not replaced by another
identity. Compiler OS/glibc floors are not proof of execution on the minimum host.
Read each package's runtime and license notices before redistribution.

## Migrating from the earlier experiment

**Upgrade both peers together.** There is no compatibility mode for the old draft.
Ordinary stable Client/Server Services and non-flow PubSub transports are not
replaced by this change.

| Earlier experiment | Replacement |
| --- | --- |
| JSON CONTROL, dedicated MSG, shared STR and dedicated RequestId addressing | Binary CONTROL and identical FLO/ChannelId envelopes with RequestId zero on both UASC profiles |
| New Open/Modify/Close Service requests and responses | Generated manager Methods through ordinary Call; configured flows need no Call |
| `DataChannelManager`, direct registry access and response-sent callbacks | `DataChannelContext`, carrier factories and ready `DataChannel` facades |
| Frame flags, fragments, GAP and old delivery modes | Complete messages, Ordered/Datagram delivery and transactional revisions |
| Source/sink-relative directions | Opener-relative directions |
| `channel.Write(...)` and frame queues | Await `SendAsync`; inspect its result and dispose each received message |
| Implicit UASC control connection on `opc.quic`, ALPN `opcua/1` | Native mTLS flow association, ALPN `opcua-flow/1`; explicit optional Client/Server lane |
| Old experimental NodeIds and custom StatusCodes | Regenerated model identifiers and `DataChannelResult` |
| Sample `--transport`, direct-manager mode and old frame counters | `--carrier`, finite complete-message runs and validated received-byte summaries |

Regenerate model-dependent code and use generated constants; do not transplant
the old numeric IDs or status mappings. Re-provision endpoint/profile selection
and resource policies. Existing transport connections, grants and channel IDs
cannot be transferred into the new protocol.

## Validation and current limits

The common codec/runtime tests live in `Opc.Ua.Core.DataChannels.Tests`.
Physical UASC tests, optional model/control tests, PubSub tests, native binding
tests and real-process sample tests exercise their own layers. Passing a codec
suite alone does not establish carrier or application interoperability.

```powershell
$env:CustomTestTarget = 'net10.0'
dotnet test tests\Opc.Ua.Core.DataChannels.Tests\Opc.Ua.Core.DataChannels.Tests.csproj -f net10.0
dotnet test tests\Opc.Ua.PubSub.Samples.Tests\Opc.Ua.PubSub.Samples.Tests.csproj -f net10.0
```

The existing `Opc.Ua.Aot.Tests` executable also includes native-free
`DataChannelsAotTests`. These exercise direct and DI context construction,
binary CONTROL, complete DATA, revision-window ownership and unsupported
control rejection through the public runtime APIs. After publishing that
project with NativeAOT on Windows, run the focused cases with:

```powershell
.\tests\Opc.Ua.Aot.Tests\bin\Release\net10.0\win-x64\publish\Opc.Ua.Aot.Tests.exe --treenode-filter "/*/*/DataChannelsAotTests/*" --report-trx
```

These TUnit cases use an in-process verified-plaintext UASC seam. They do not
load QUIC/HTTP3 backends or qualify transport authentication, native resource
retention, or the native Client/Server lane. The AOT project's executable
runner is separate from the NUnit `dotnet test` commands above.

See the [Developer Guide](DeveloperGuide.md) for full-solution framework runs,
coverage and the dedicated required Windows/Linux native workflow. Native tests
require their actual assets and fail when prerequisites are absent; a skipped
or empty test run is not native acceptance.

### Current limitations

- **Reverse shared-UASC remains refused.** The pinned draft assigns ContextId
  selection to the transport initiator but the first HELLO to the OPN requester.
  Reverse connect separates these roles. No silent interpretation is selected;
  ordinary reverse Services remain unchanged.
- **WebTransport peer-STOP reset retention remains blocked.** The pinned QUIC
  backend can discard the reliable-header boundary in its automatic STOP response.
  The draft-16 header requirement and HTTP/3 accounting checks remain enforced.
  A private opt-in native-policy candidate requires review and qualification
  before adoption; observing the peer STOP alone does not satisfy this requirement.
- Native release signing, final-artifact advisory/SBOM evidence and minimum-host
  acceptance are separate gates. Local unsigned validation packages are not
  approved product distributions.
- Browser interoperability, ARM, musl, macOS native assets and non-exportable
  native signing keys are not claimed. The audio sample's hardware playback is
  Windows-only; headless WAV output is separate.
- Optional Client/Server lane integration and coherent final-dependency
  validation must be completed before claiming full rewrite acceptance.
  Historical sample results do not qualify a replacement native binary.

## References

- [Pinned specification sources](https://github.com/OPCF-Members/OPC10000-Core/tree/673b3cfde6b2c8d6510d5b598dec1180c0add205/source/data-channels)
- [PubSub integration](PubSub.md#data-channels-experimental)
- [Optional native Client/Server lane](DataChannelClientServer.md)
- [Certificate manager and secret store](CertificateManager.md)
- [Contributor build and test guide](DeveloperGuide.md)
