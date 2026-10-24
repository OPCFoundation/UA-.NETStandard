# OPC UA QUIC bindings

## Part 31 reliable UASC baseline

`QuicUascTransport` is a separate managed `System.Net.Quic` implementation of
the reliable baseline. It selects `DataChannelProfiles.UascQuic`
(`uasc-quic-draft`) and ALPN `opcua-uasc-draft`. Its first application bytes are
ordinary UACP HEL; ACK, OPN, Services, FLO, FLC and CLO use the existing UASC
pipeline, without a native flow-management envelope or nested Client/Server lane.

This is an experimental implementation surface, not a complete Parts 30-32
conformance claim. It does not advertise direct streams, datagrams or exporter
binding. The legacy native implementation described below remains distinct;
its interoperability results do not establish Part 31 conformance.

On .NET 8, the managed baseline types explicitly require opting into the
`System.Net.Quic` preview API; the existing native APIs are not marked preview.
Later targets use the stable managed API. Client establishment and first-stream
admission have explicit 15-second deadlines. The server also configures the TLS
handshake timeout on .NET 9 and later; .NET 8 uses its QUIC runtime handshake limit.

The managed baseline supports ordinary Services without a Data Channel factory
or Session. Supplying `TransportChannelSettings.DataChannels` and the listener's
corresponding context factory additionally enables protected configured FLO/FLC
with baseline encoded-byte credit. The context retains QUIC carrier identity;
it is not relabeled as TCP or given the superseded native record framing.
Both modes require UASC `SignAndEncrypt` and application-certificate validation,
independently of TLS. Baseline TLS does not require a client certificate.
The built-in TCP/WSS factories cannot select a Part 31/32 native baseline:
external byte factories must explicitly declare their negotiated profile through
`IUaSCTransportProfile`. This declaration is host evidence, not a substitute for
the actual QUIC or WebTransport negotiation.

### Client construction

Obtain TLS validation policy from the host's certificate-store configuration.
The TLS options must select the exact ALPN, match the configured authority in
`TargetHost`, and leave `EnabledSslProtocols` at `None`, allowing the operating
system's QUIC implementation to select TLS. No trust bypass or credentials are
installed by this library.

```csharp
var endpoint = new Uri("opc.quic://device.example:4840/uasc");
tlsOptions.TargetHost = endpoint.DnsSafeHost;
tlsOptions.ApplicationProtocols =
    [new System.Net.Security.SslApplicationProtocol(DataChannelProfiles.UascQuicAlpn)];

var transport = new QuicUascTransport();
await using QuicUascConnection baseline =
    await transport.ConnectAsync(endpoint, tlsOptions, cancellationToken);

// Ordinary settings include application certificates, their validator and
// Description.TransportProfileUri = DataChannelProfiles.UascQuic.
using ITransportChannel channel = baseline.Create(telemetry);
await ((ISecureChannel)channel).OpenAsync(endpoint, settings, cancellationToken);
// Send ordinary Services, or use UascDataChannelClient(baseline, telemetry)
// with an explicitly configured DataChannels factory.
await channel.CloseAsync(cancellationToken);
```

Keep the owning `QuicUascConnection` alive until its UASC channel is closed, then
dispose it asynchronously. Stream I/O transfers only once. Reconnection requires
a fresh connection owner and OPN; a closed stream is never silently replaced.

### Listener construction

Supply a TLS server certificate acquired from the certificate manager and exact
ALPN in `SslServerAuthenticationOptions`. `ListenAsync` accepts a numeric local
authority; port zero chooses an ephemeral port reported by `EndpointUrl`.
Initialize `TcpTransportListener.OpenExternal` with matching Part 31 endpoint
descriptions and ordinary UASC certificate settings, then attach each accepted
connection through `AcceptTransport(connection, certificateManager, receiveSize)`.
This dispatcher does not bind another TCP socket.

Each accepted connection is independently owned and must be asynchronously
disposed after its UASC channel closes. Disposing the QUIC listener stops new
accepts, not already accepted connections. The baseline advertises no inbound
unidirectional streams and only one initiator-created bidirectional stream.
TLS/first-stream establishment has a finite deadline; stream and idle limits
remain finite before capability confirmation.

`services.AddOpcUa().AddQuicUasc()` registers the injectable transport without
creating certificates, opening sockets or advertising endpoints. Managed QUIC
availability must be checked on the deployment platform.

## Explicit native Part 31 baseline and direct delivery

`NativeQuicUascTransport` explicitly selects the native backend for the same
`UascQuic` profile and `opcua-uasc-draft` ALPN. It does not replace
`QuicUascTransport`, change Core/Client/Server dependencies, or silently switch a
managed baseline to native code. The host supplies the native backend, TLS
credentials, finite `QuicDataChannelOptions` and shared application budget.

```csharp
var nativeTransport = new NativeQuicUascTransport(
    backend, applicationBudget, new QuicDataChannelOptions());
NativeQuicCredentials trust = NativeQuicCredentials.ForTlsClient(trustRootsDer);
await using NativeQuicUascConnection baseline = await nativeTransport.ConnectAsync(
    endpoint, trust, credentialsInvalidated: trustPolicyChanged,
    cancellationToken: cancellationToken);
using ITransportChannel channel = baseline.Create(telemetry);
await ((ISecureChannel)channel).OpenAsync(endpoint, settings, cancellationToken);
await channel.CloseAsync(cancellationToken);
```

Use ordinary application certificates, certificate validation and
`SignAndEncrypt` in `settings`, independently of the transport TLS credentials.
Without a Data Channel factory this is an ordinary Services baseline. With the
factory, protected FLC binds `quic-direct-draft` using the connection-owned TLS
exporter. Only authenticated ASSOCIATE/ACK/READY admits complete UADP messages
on token-prefaced actual streams or RFC 9221 datagrams. Ordered COMMIT/END use
actual-stream barriers; datagram control remains on protected FLC.

`ListenAsync` accepts a numeric `opc.quic` authority, with port zero for an
ephemeral port. Attach each accepted connection to the external UASC listener
using `AcceptTransport`. Native stream zero is announced by the client's actual
HEL write: run connection acceptance independently of client channel opening,
rather than awaiting server acceptance before starting HEL.

Native stream allocation, unbound-stream prefaces, pending token registrations
and nonreuse history are finite. Bidirectional and unidirectional stream
acceptors run from baseline establishment and refuse speculative streams when
no receiver registration exists. They share the pending-preface bound;
outgoing direct streams are bidirectional. Native capacity includes the two
pending stream accept operations. Capacity refusal does not close the baseline;
stream release wakes admission. DATAGRAM sends preserve the common commitment
owner, current path capacity and unreliable semantics. Disposing the baseline
owner joins native operations and the direct accept worker before releasing its
application reservation. Closing the baseline byte stream invalidates its native
connection and pending direct operations. Accepted connections outlive their
transport listener.

For DI, register the host's `INativeQuicBackend` and
`DataChannelApplicationBudget`, then call
`services.AddOpcUa().AddNativeQuicUasc(options)`. Registration does not load a
native image, acquire credentials, bind sockets or advertise an endpoint.

The native binding's NativeAOT harness exercises real authenticated UASC Read,
protected exporter binding, client-opened ordered and server-opened datagram duplex
delivery, revision transactions, carrier-charged byte diagnostics, directional END
and joined resource release, using the same
framework-independent scenario as its NUnit test. This is Windows qualification
of those paths, not complete Part 31 or platform/package qualification. Browser interoperability
and the full source-specific negative matrix remain separate acceptance work.

## Shared native transport ownership

`NativeQuicConnection` and `NativeQuicListener` share bounded stream/operation
ownership across two explicitly separate authentication policies. Existing
`ConnectAsync`/`ListenAsync` still require application-pair mutual TLS and the
certificate provider's URI/SPKI policy. `ConnectTlsAsync`/`ListenTlsAsync` select
server-authenticated transport TLS for hosts that authenticate applications in
an independent protocol, such as UASC. They do not create a Data Channel context.

```csharp
// backend and trustRootsDer come from the host's native-library and certificate configuration.
NativeQuicCredentials trust = NativeQuicCredentials.ForTlsClient(trustRootsDer);
await using NativeQuicConnection connection = await NativeQuicConnection.ConnectTlsAsync(
    backend, trust, "device.example:443", "h3", "device.example",
    credentialsInvalidated: trustPolicyChanged);
NativeQuicTlsEvidence tls = connection.TlsPeer;
```

Use `NativeQuicCredentials.ForTlsServer` with a supplied certificate, optional
intermediates and caller-owned signing key for `ListenTlsAsync`. Native creation
copies signing material; the host retains and disposes its key capability.
Neither side invents an application URI. A TLS client has a verified server
chain; the server has no verified client chain. Accessing `Peer` or `Credentials`
on a transport-only connection fails explicitly, rather than returning
application-mTLS credentials. Conversely, `TlsPeer` rejects application-mTLS.
`IsClient` and stream `IsLocal` describe actual transport roles in either mode.

The host's `credentialsInvalidated` token closes its listener and affected live
connections independently of operation cancellation. Certificate expiry also
closes affected ownership. Disposing a listener alone preserves already accepted
connections. Use the connection APIs, not its borrowed backend/handles, so
operation and stream limits, cancellation and asynchronous drainage remain
effective. These primitives do not establish HTTP/3, browser interoperability or
Parts 30-32 conformance by themselves.

## Superseded native flow protocol

`OPCFoundation.NetStandard.Opc.Ua.Bindings.Quic` is an **optional** native
`opc.quic` flow carrier. It uses the common `Opc.Ua.DataChannels` runtime and the
separately packaged, reviewed `Opc.Ua.Native.QuicGo` backend.

The native association requires mutual TLS 1.3 and the exact provisional ALPN
`opcua-flow/1`. Application URI, SHA-256 public-key identity, certificate chain,
hostname and explicit application policy are checked before exposing a context.
There is no anonymous, generated-identity, system-trust or protocol fallback.

## Independent features

- A native data context needs **neither UASC nor a Session**.
- Configured resource authorization is supplied through `IDataChannelAuthorizer`.
- Optional model/Session-control hosting can supply `IDataChannelContextFactory`.
  The factory registers an unstarted context; ingress is attached before HELLO.
- The reserved native ClientServer resource is an independently opened ordinary
  flow. This package never opens it implicitly.
- Ordered and datagram flows use the same complete-message API. A datagram is a
  real RFC 9221 datagram, not an emulated stream message.

The previous experimental `System.Net.Quic`/implicit-UASC transport wrappers and
their old frame format are replaced, not silently retained.

## Direct construction

Initialize an `ICertificateManager` through the stack's normal configuration,
certificate-store and secret/password providers. Select a loaded application
certificate and an explicitly configured trusted store. Supply exact permitted
peer URI/SPKI pairs; a trust anchor alone does not authorize an application.

```csharp
using Opc.Ua;
using Opc.Ua.Bindings;
using Opc.Ua.Bindings.Native;
using Opc.Ua.DataChannels;
using Opc.Ua.Native.QuicGo;

// manager, applicationUri, permittedPeers, authorizer and telemetry are supplied
// by the host's certificate configuration and resource authorization policy.
using var credentials = new NativeQuicCertificateProvider(
    manager,
    new NativeQuicCertificateOptions(
        applicationUri,
        ObjectTypeIds.RsaSha256ApplicationCertificateType,
        permittedPeers,
        TrustListIdentifier.Peers));

var library = await NativeQuicLibrary.FromDirectoryAsync(nativeAssetDirectory);
var backend = new NativeQuicBackend(library);
var budget = new DataChannelApplicationBudget();
var contexts = new DataChannelContextFactory(
    authorizer, new DataChannelOptions(), budget, TimeProvider.System, telemetry);
var transport = new QuicDataChannelTransport(
    backend, credentials, contexts, applicationBudget: budget);

await using var connection =
    await transport.ConnectAsync(new Uri("opc.quic://device.example:4840"));

await using var flow = await connection.Context.OpenAsync(
    new DataChannelParametersDataType
    {
        ResourceUri = "urn:example:telemetry",
        Protocol = DataChannelProfiles.OpaquePayload,
        ContentType = "application/octet-stream",
        Direction = DataChannelDirection.Duplex,
        Delivery = DataChannelDeliveryMode.Ordered,
        Protection = DataChannelProtectionMode.Confidential,
        MaxMessageSize = 4096,
        MaxReceiveBytes = 65536,
        Priority = 1,
        AllowQueueExpiry = false
    });

DataChannelResult result = await flow.SendAsync(new ByteString(new byte[] { 1, 2, 3 }));
using DataChannelMessage? reply = await flow.ReadAsync();
```

Resource policy runs on both peers. Applications must inspect operation results
and dispose every received message; message disposal releases byte **and message
count** reservations. `EndAsync` closes only the sending application direction.
`ResetAsync` closes only the affected flow.

To listen, use a numeric local address:

```csharp
await using var listener =
    await transport.ListenAsync(new Uri("opc.quic://127.0.0.1:4840"));
await using var connection = await listener.AcceptAsync();
await using var flow = await connection.Context.AcceptAsync()
    ?? throw new InvalidOperationException("The data context closed.");
using DataChannelMessage? message = await flow.ReadAsync();
```

Port zero requests an ephemeral port; `listener.EndpointUrl` returns the actual
bound address. Disposing the listener drains pending accepts but does not close
already returned connections. Keep the credential provider alive until all its
listeners and connections are disposed.

## Dependency injection

```csharp
services.AddNativeQuicGo(library);
services.AddSingleton<ICertificateManager>(manager);
services.AddSingleton<IDataChannelAuthorizer>(authorizer);

services.AddOpcUa()
    .AddDataChannels()
    .AddNativeQuicCertificates(new NativeQuicCertificateOptions(
        applicationUri,
        ObjectTypeIds.RsaSha256ApplicationCertificateType,
        permittedPeers))
    .AddQuicDataChannels();
```

Resolve `QuicDataChannelTransport`. Registration does not start listeners, install
Service transport factories, or advertise endpoints. Hosts with optional
model/control routing can register their `IDataChannelContextFactory` instead of
the direct common factory.

Discovery uses `DataChannelProfiles.Quic`. Native discovery descriptors use
SecurityPolicy `None` and MessageSecurityMode `None` to indicate **no UASC**,
not unprotected transport. They must still identify the actual TLS application
certificate. An `opc.quic` data endpoint is not an ordinary discovery endpoint.

## Native credential and lifetime helpers

`Opc.Ua.Bindings.Native` contains reusable `NativeQuicCertificateProvider`,
`NativeQuicListener`, `NativeQuicConnection` and `NativeQuicStream`. They have no
flow, Session or HTTP/3 framing requirement and can also host the optional
WebTransport binding.

Credentials are explicit DER leaf/intermediates, trusted roots, application URI,
peer URI/SPKI rules and software PKCS8 signing material. The native signer supports exportable
RSA2048..4096 and ECDSA P256/P384/P521. Non-exportable/hardware keys and unsupported
curves are refused, not silently substituted. Exported key buffers are cleared
after native listener/connect creation completes.

The native engine checks chain, validity, EKU, hostname and peer pins. The bridge
also awaits the stack certificate manager's validation, including its configured
revocation policy. Selected certificate/trust/CRL changes invalidate affected
associations; periodic revalidation and certificate expiry bound their lifetime.
A selected certificate replacement conservatively requires a fresh native
association, including a same-key reissue. Unrelated trust scopes do not revoke it.

Native completion results retain accepted byte counts and racing cancellation
outcomes. A committed flow write retains its input until the real native result
is drained. Close wakes native I/O, drains pending operations, releases streams,
then releases the connection. A blocked native datagram send can close its
connection on failure; that actual context loss is propagated.

Peer RESET and STOP are observed independently of a backpressured application
reader or an earlier local reset, using actual received native frames rather
than a fabricated EOF or local cancellation cause. The binding
requires native ABI4 (operation revision3); older libraries are refused before
opening a listener or connection. High-level connection `Completion` also waits
for ingress and its reserved host capacity to drain.

The raw helper reports reset frame type, final size and reliable size exactly as
sent. Generic QUIC permits an ordinary RESET_STREAM in response to peer
STOP_SENDING, including after an earlier reliable reset. Such evidence is not
automatically sufficient for a higher-level protocol's reliable-prefix policy;
the WebTransport binding must enforce its additional requirements independently.

## Bounds and platform scope

`QuicDataChannelOptions` and `NativeQuicConnectionSettings` bound stream handles,
unbound-stream lifetime, anti-reuse history, record size, operations and
establishment/recheck deadlines. The common application budget independently
bounds contexts, flows, messages, bytes and control operations. Backend global
limits also apply; they are not replaced by larger application settings.

Management uses the actual first initiator-created bidirectional stream. A
common OPEN on each new flow stream makes it physically observable before
ACCEPT/READY. Association requires the actual connection-owned handle and
correct stream identifier, direction and opener. There is no extra QUIC flow
header, fabricated stream ID or empty-write/FIN announcement workaround.

Supported managed targets are .NET 8, 9 and 10; legacy solution matrix legs are
inert and do not claim native .NET Framework support. Native assets are currently
win-x64 and glibc linux-x64. No musl, ARM or macOS support is implied.
The Go native image is process-lifetime and must not be unloaded. Asset loading
checks the selected path, SHA256 receipt and ABI. Packaging requires the backend's
`NativeQuicAssetRoot`; no native binary or test credential belongs in source.

See the native backend's `NATIVE_ABI.md` for asset prerequisites and production
signing, per-RID security/SBOM and minimum-host acceptance requirements. A source
build or backend test alone is not whole-product distribution approval.
