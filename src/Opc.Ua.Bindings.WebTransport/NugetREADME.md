# OPC UA WebTransport bindings

**Experimental; full Parts 30–32 acceptance remains open.** The native H3
binding supports a management-only UASC path for native Data Channels and a
separate HTTPS/UACP baseline for ordinary Services. Browser interoperability,
the draft's JSON CONTROL representation, and native minimum-retention
qualification remain open. The older mTLS flow carrier below is not an
implementation of the revised WebTransport authentication mapping.

The legacy native flow path uses mutually authenticated native QUIC, HTTP/3 and
WebTransport draft-ietf-webtrans-http3-16. It is not a WebSocket fallback,
Kestrel draft02, or a second QUIC runtime. `opc.wt` endpoints select exact
HTTPS Extended CONNECT authority and path/query policy. The UACP baseline is a
separate HTTPS profile and does not infer application identity from TLS.

The native QUIC backend must provide ABI4, including independent received
RESET and actual peer STOP observations, confirmed TLS authentication and
actual reliable-reset evidence. Older native images are refused, not assigned
synthetic capabilities. These observations do not establish minimum-retention
qualification.

## Native Data Channel management

`WebTransportUascTransport.ConnectDataChannelsAsync` and
`ListenDataChannelsAsync` select `DataChannelProfiles.WebTransport` at an
`opc.wt` endpoint. The endpoint maps to HTTPS for CONNECT. TLS authenticates the
HTTPS server; the first client-created WebTransport stream then carries
HEL/ACK and SignAndEncrypt OPN to authenticate both OPC UA applications.
Application identity is not inferred from TLS or HELLO fields.

The authenticated UASC channel creates the native context through
`IDataChannelUascManagementProvider`. The provider retains that exact
application identity and attaches flow ingress on the same WebTransport
session before HELLO starts. Repeated attachment is refused. Management writes
use the existing UASC ordered writer, security sequence and token renewal.
The native flow streams and datagrams do not require an exporter or
ASSOCIATE token exchange.

Management CONTROL records occupy one final protected MSG each. RequestId
equals the common-record MessageId, and there is no Service TypeId or inner
ByteString wrapper. The management dispatcher rejects DATA, ordinary Service
bodies, non-final chunks, mismatched RequestIds and trailing records. Flow
streams retain ordered DATA, revision barriers and directional END.
Those streams use native stream identifiers within the same session; datagrams
use the four-byte ChannelId prefix and common DATA record.

Obtain TLS trust from certificate configuration and provide separate
application-certificate settings and a context factory:

```csharp
using NativeQuicCredentials tlsTrust = NativeQuicCredentials.ForTlsClient(trustRootsDer);
var transport = new WebTransportUascTransport(
    backend, http3Library, applicationBudget, options);
var endpoint = new Uri("opc.wt://device.example:4840/flow/v1");
await using WebTransportUascBaselineConnection nativeSession =
    await transport.ConnectDataChannelsAsync(
        endpoint, tlsTrust, cancellationToken: cancellationToken);

applicationSettings.DataChannels = contextFactory;
var client = new UascDataChannelClient(nativeSession, telemetry);
await using UascDataChannelConnection connection = await client.ConnectAsync(
    endpoint, applicationSettings, cancellationToken);
DataChannel flow = await connection.Context.OpenAsync(
    parameters, cancellationToken: cancellationToken);
DataChannelResult result = await flow.SendAsync(payload, cancellationToken: cancellationToken);
if (result != DataChannelResult.Good)
{
    throw new DataChannelException(result);
}
```

`applicationSettings` is a `TransportChannelSettings` with the exact
`opc.wt` endpoint, `DataChannelProfiles.WebTransport`, SignAndEncrypt and a
supported UASC security policy. It supplies the expected server application
certificate, the client application certificate and its validator. The TLS
trust roots do not replace that validation. `parameters` is an explicitly
authorized native flow descriptor. `connection.Services` is null.

For a server, use `ListenDataChannelsAsync` with server-only TLS credentials
and an authorizer for the mapped HTTPS CONNECT endpoint. Pass each accepted
connection to `TcpTransportListener.AcceptTransport` after `OpenExternal`.
The external listener settings must contain the exact bound `opc.wt` endpoint,
the `WebTransport` profile, SignAndEncrypt and the application context factory.
The endpoint description carries the UASC application certificate and no
Session user-token policies. Native QUIC's None security fields do not apply
to this profile.

```csharp
await using WebTransportUascListener listener = await transport.ListenDataChannelsAsync(
    endpoint, localUdpEndpoint, tlsServerCredentials, authorizeConnect,
    cancellationToken: cancellationToken);
uascListener.OpenExternal(listener.EndpointUrl, applicationListenerSettings, requestCallback);
await using WebTransportUascBaselineConnection accepted =
    await listener.AcceptAsync(cancellationToken);
_ = uascListener.AcceptTransport(accepted, certificateValidator, maxBufferSize);
```

`WebTransportUascTransport.AddWebTransportUasc` registers the transport factory
with `IOpcUaBuilder`. The host supplies the native backend, shared application
budget and TLS snapshots; a resolved factory exposes both explicitly selected
profiles.

The H3 session and UASC channel have distinct lifetimes that end together:
close the UASC channel before disposing its native session. A new native
session must perform a new OPN/HELLO exchange; the implementation does not
resume or pool TLS connections. UASC certificate invalidation closes the
context, and native TLS/session loss faults all of its flows.

The common CONTROL body is still encoded with the existing binary codec.
RESET also retains the legacy management-path exchange before native FIN,
rather than the draft's data-stream ordering. These entry points therefore do
not establish conformance to the pinned draft's complete wire contract, even
where the management framing matches.

## Part 32 reliable UASC baseline

`WebTransportUascTransport` establishes a server-authenticated TLS 1.3
connection, authorizes the exact HTTPS Extended CONNECT resource, and reserves
the first client-created WebTransport bidirectional stream for ordinary UACP
bytes. The existing UASC stack handles HEL/ACK, application-certificate
SignAndEncrypt OPN, Services, FLO/FLC and close. The transport TLS client
supplies trust roots only; application authentication remains an independent
UASC operation. No Data Channel context, Session or model is created by
baseline establishment.

The baseline exposes the Part 32 `IDataChannelDirectBindingProvider` exporter.
It derives 32 bytes with TLS label `EXPORTER-WebTransport` and binds the exact
CONNECT Session ID, application label and flow transcript digest. This
exporter alone does not associate streams or datagrams with a flow. The common
runtime uses its protected proofs and exact ASSOCIATE/ACK/READY exchange to select
actual ordered streams and direct datagrams. Browser interoperability remains open.

The baseline also exposes `IDataChannelDirectStreamProvider` through the UASC
transport and accepted-connection wrappers. This carrier primitive matches a
single-use Guid token to an actual ordered stream, consumes its 16-byte token
preface, and bounds pending admissions, token history and retained input.
Locally opened direct streams are write-only; accepted streams are read-only.
Cancellation does not make a token reusable. Forwarded admission and exporter
operations are cancelled when the UASC baseline closes; a stream returned during
that cancellation is disposed instead of escaping the closed adapter.
The flow runtime authenticates the binding and enforces ASSOCIATE/ACK, direction,
revision and READY before using this provider. It preserves complete UADP bodies,
processes COMMIT/END on each actual sending stream, and keeps management-only
barriers from changing direct-stream state. Provider availability alone does not
establish readiness or grant permission to send.

`IDataChannelDirectDatagramProvider` exposes the current payload capacity and
intact unreliable send/receive operations through the same UASC wrappers.
The common runtime authenticates each 24-byte token/revision/sequence prefix
before admitting unchanged UADP against finite replay, rate and queue limits.
No datagram carries control or Services; controls stay on protected FLC.
One context-owned, budgeted receive operation drains direct input. The H3 pump
discards unsolicited datagrams without waiting for the unattached legacy
context, and cancellation releases the single pending receiver.

The native Windows loopback test exercises CONNECT, HEL/ACK, SignAndEncrypt
OPN and correlated Reads through `TcpTransportListener.AcceptTransport`, including
continued Services after authenticated duplex ordered or datagram DATA, revision and END.
This is not actual browser evidence. Browser certificate/key possession,
browser Services, Origin/credentials policy and
full Parts 30–32 conformance remain unverified.

### Client construction

Supply the reviewed native backends and HTTP/3 image, an application-wide
budget, explicit options and trust-only TLS credentials. Obtain trust roots
from the application's certificate configuration; this package does not
disable server-chain or hostname verification.

```csharp
using NativeQuicCredentials tlsTrust = NativeQuicCredentials.ForTlsClient(trustRootsDer);
var transport = new WebTransportUascTransport(
    backend, http3Library, applicationBudget, options);
await using WebTransportUascBaselineConnection baseline = await transport.ConnectAsync(
    new Uri("https://device.example:4840/ua/wt?resource=service"),
    tlsTrust,
    cancellationToken: cancellationToken);

using ITransportChannel channel = baseline.Create(telemetry);
await ((ISecureChannel)channel).OpenAsync(
    baseline.EndpointUrl, secureChannelSettings, cancellationToken);
// Send ordinary OPC UA Services; close the UASC channel before disposing baseline.
await channel.CloseAsync(cancellationToken);
```

`secureChannelSettings` must use `Description.TransportProfileUri =
DataChannelProfiles.UascWebTransport`, the exact HTTPS endpoint, an application
certificate and its validator, and `SecurityMode.SignAndEncrypt`. The baseline
rejects endpoint substitutions and weaker UASC protection.

### Listener construction

The server supplies a TLS certificate/key separately from its UASC application
certificate and validates each CONNECT request through an explicit authorizer.
The returned `WebTransportUascBaselineConnection` implements the existing
UASC byte-transport factory; attach it to the shared external listener:

```csharp
await using WebTransportUascListener listener = await transport.ListenAsync(
    endpoint, localUdpEndpoint, tlsServerCredentials, authorizeConnect, cancellationToken: cancellationToken);
uascListener.OpenExternal(endpoint, transportListenerSettings, requestCallback);
await using WebTransportUascBaselineConnection accepted =
    await listener.AcceptAsync(cancellationToken);
_ = uascListener.AcceptTransport(accepted, certificateValidator, maxBufferSize);
```

The external listener's endpoint descriptions must advertise the exact
`UascWebTransport` profile. Disposing the H3 listener stops new accepts but
does not dispose accepted connections. Close each UASC channel, then
asynchronously dispose its baseline connection.

## Legacy mTLS flow carrier

This path does not perform the UASC management exchange described above.
It remains available for the earlier experiment, not for advertising the
revised native WebTransport profile.

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

### Direct client usage

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

The legacy path has no UASC envelope. Its TLS-only security fields are not
accepted by `ServerDataChannelProvider.RegisterEndpoint` for the revised
WebTransport profile; use the management path's SignAndEncrypt application
certificate description instead. Native QUIC retains its separate TLS-only
discovery contract.

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
