# OPC UA native WebTransport flow binding

**Experimental; acceptance blocked.** The pinned QUIC backend can discard the
WebTransport header's reliable boundary when responding to peer STOP_SENDING.
The draft-16 requirement and native HTTP/3 checks remain strict. A private
minimum-retention candidate has not passed its source-adoption gate and is
not part of this package. Earlier passing runs do not qualify this unresolved path.

This opt-in binding uses mutually authenticated native QUIC, HTTP/3 and
WebTransport draft-ietf-webtrans-http3-16. It is not a browser transport, a WebSocket
fallback, Kestrel draft02, or a second QUIC runtime. `opc.wt` endpoints select exact
HTTPS Extended CONNECT authority and path/query policy.

The generic native QUIC backend must provide ABI4, including independent received
RESET and actual peer STOP observations, confirmed mutual authentication and
actual reliable-reset evidence. Older native images are refused, not assigned
synthetic capabilities. ABI4's truthful observations do not resolve the
minimum-header limitation above.

The application supplies `INativeQuicBackend`, `INativeQuicCredentialProvider`,
`NativeHttp3Library`, `IDataChannelContextFactory`, one shared
`DataChannelApplicationBudget`, and explicit `WebTransportDataChannelOptions`.
The certificate provider in `Opc.Ua.Bindings.Native` reuses the stack certificate
manager, secret stores, trust validation and rotation. A listener additionally
requires `IWebTransportConnectAuthorizer`; unmatched policy must be denied.

`WebTransportDataChannelTransport.ConnectAsync` and `ListenAsync` return owned
connection/listener objects. An accepted connection exposes `Context` only after
real H3 session establishment and Core HELLO. Call `Context.OpenAsync` or
`Context.AcceptAsync` for ordinary configured or explicitly Session-authorized
resources. No Server, Session, discovery service or native ClientServer lane is
opened implicitly. The optional reserved opaque ClientServer resource uses the
same explicit OPEN/authorization path.

`AddWebTransportDataChannels` registers the direct factory with the normal
`IOpcUaBuilder`; `AddDataChannels` provides a standalone common context factory.
A caller-supplied or Server-registered `IDataChannelContextFactory` is preserved.

## Direct client usage

The providers below are application-owned. `resourcePolicy` must explicitly admit
the requested resource for the actual application/key pair; it must deny unmatched
resources. Use the existing certificate-manager bridge for `credentials`.

```csharp
var budget = new DataChannelApplicationBudget();
var contextFactory = new DataChannelContextFactory(
    resourcePolicy, new DataChannelOptions(), budget, TimeProvider.System, telemetry);
var library = new NativeHttp3Library(trustedAbsoluteImagePath, reviewedSha256);
var transport = new WebTransportDataChannelTransport(
    nativeBackend, library, credentials, contextFactory, budget,
    new WebTransportDataChannelOptions(WebTransportOriginPolicy.AbsentOnly));

await using WebTransportDataChannelConnection connection = await transport.ConnectAsync(
    new Uri("opc.wt://device.example:4840/flow/v1"), cancellationToken);
DataChannel flow = await connection.Context.OpenAsync(
    new DataChannelParametersDataType
    {
        ResourceUri = "urn:example:temperature",
        Protocol = DataChannelProfiles.OpaquePayload,
        ContentType = "application/octet-stream",
        Direction = DataChannelDirection.Duplex,
        Delivery = DataChannelDeliveryMode.Ordered,
        Protection = DataChannelProtectionMode.Confidential,
        MaxMessageSize = 1024,
        MaxReceiveBytes = 8192,
        AllowQueueExpiry = false
    },
    cancellationToken: cancellationToken);
DataChannelResult sent = await flow.SendAsync(payload, cancellationToken: cancellationToken);
if (sent != DataChannelResult.Good)
{
    throw new DataChannelException(sent);
}
using DataChannelMessage? response = await flow.ReadAsync(cancellationToken);
```

For a listener, supply `IWebTransportConnectAuthorizer`, call `ListenAsync`, then
`listener.AcceptAsync` and `connection.Context.AcceptAsync`. The overload taking
an `IPEndPoint` binds a local address independently of the exact advertised
authority, for wildcard interfaces or explicit port forwarding. It does not
enable origin pooling. Use `DataChannelDeliveryMode.Datagram` only when the
resource policy explicitly admits genuinely unreliable application datagrams.

Native endpoint discovery can use SecurityMode None / SecurityPolicy None to
describe the absence of a UASC envelope. It does **not** mean unprotected traffic:
confirmed TLS 1.3 mutual application authentication and exact expected keys
remain mandatory.

One CONNECT/session and one application pair are permitted per fresh connection.
No pooling, resumption, 0-RTT, redirects or retry on a rejected/closed CONNECT is
performed. Listener disposal stops accepts but does not kill returned connections.
Dispose each connection to drain Core messages, native jobs, H3 leases and handles.
Per-flow reset is intended to leave unrelated flows open; the peer-STOP case
above still blocks acceptance of that guarantee. TLS/trust/key loss closes the context.

Native assets are separately packaged and hash-bound. Select an absolute trusted
RID asset using `NativeHttp3Library`; there is no PATH/current-directory probing.
Supported asset targets are x64 Windows and glibc Linux (minimum 2.34), not macOS,
ARM, musl or browsers. The Go image remains loaded for process lifetime. Source
checkout packaging retains the native-only HTTP/3 dependency and copies its
RID assets through NuGet. Product signing and release integration remain gated;
staged local proofs do not establish final-hash SBOM/SCA/VEX or minimum-host approval.

The binding reserves conservative, finite additional H3/native/host capacity from
the shared application budget before H3 creation. Core application message/flow
reservations remain separate. Tune the explicit limits for the application's
concurrency rather than creating a separate budget for every connection.
