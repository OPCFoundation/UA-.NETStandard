# OPC UA native QUIC flow binding

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
