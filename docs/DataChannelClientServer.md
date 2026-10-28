# Optional Client/Server lane on native data channels

**Experimental legacy composition.** Complete WebTransport lane acceptance
remains blocked by the
[peer-STOP reliable-header limitation](DataChannels.md#current-limitations).
This guide describes the current separately selected Client/Server lane, not
the current Part 31/32 baseline. Those drafts carry UACP/UASC Services on the
reliable transport baseline; the implementation below has not yet been
reworked to that contract.

For the separate managed Part 31 baseline, use
[`QuicUascTransport`](../src/Opc.Ua.Bindings.Quic/NugetREADME.md#part-31-reliable-uasc-baseline).
It carries UACP/UASC directly on the reliable stream and does not use this
reserved resource, outer flow authorization or nested lane. The WebTransport
native HTTPS UASC baseline and explicitly selected
[`NativeQuicUascTransport`](../src/Opc.Ua.Bindings.Quic/NugetREADME.md#explicit-native-part-31-baseline-and-direct-delivery)
are separate paths. Their TLS identity is not used as the UASC application
identity. Native QUIC/WebTransport direct delivery uses protected binding and
token admission, not this nested lane. Browser interoperability and full
conformance remain separate evidence gates.

Native QUIC and WebTransport data contexts do not require a UA SecureChannel,
Session, or AddressSpace. An application that also needs ordinary Client/Server
Services can explicitly enable the reserved Client/Server resource on either
native carrier.

The lane uses the existing UACP/UASC and Client/Server implementations. It does
not start a TCP listener, forward traffic over loopback, or encode a second
Service protocol. Each common DATA payload carries exactly one complete UACP
MessageChunk, including HEL, ACK, OPN, MSG, CLO, or ERR. Normal UASC chunking,
security sequencing, request correlation, buffer ownership, and Session
processing remain in the existing stack.

This implementation follows the superseded OPC 10000-28 draft's optional
native lane and its OPC 10000-6 transport text,
`OPCF-Members/OPC10000-Core@726c91572a4e5725e114115cf0f69ab8cc2a6b0f`,
`source/data-channels/OPC-UA-Part6-Data-Channel-Transport.md`,
“Native peer binding and connection lifetime” and “Optional Client/Server lane.”

## Independent opt-ins

There are three separate choices:

1. Establish a native context using `QuicDataChannelTransport` or
   `WebTransportDataChannelTransport`. This performs actual native TLS/mTLS and
   profile confirmation, not UASC or Session establishment.
2. Optionally authorize and open a Client/Server lane using
   `DataChannelClientServerClient` and `DataChannelClientServerListener`.
3. Independently enable the optional data-channel model and Session control
   using `ServerDataChannelProvider` and the Server hosting APIs.

Installing a lane service does not open a lane. Native-only Publisher/Subscriber
applications need neither the lane nor a Session. A configured native payload
flow can remain active while a lane on the same context opens or closes.

## Security and resource policy

Both peers must explicitly authorize this exact descriptor:

| Field | Required value |
|---|---|
| ResourceUri | `urn:opcfoundation:ua:flow:resource:client-server:1` |
| Protocol | `urn:opcfoundation:ua:flow:payload:opaque:1` |
| Direction | Duplex |
| Delivery | Ordered |
| Protection | Confidential |
| AllowQueueExpiry | false |

Other opaque resources are never interpreted as Services.

The inner lane cannot create another Data Channel carrier or context. Client
and listener configuration reject flow profiles, reserved `/flow` resources and
nested context factories. The complete-chunk adapter rejects the superseded
FLO/FLC UACP message types on both send and receive before passing bytes to UASC.
This check applies only to the reserved Client/Server lane, not to arbitrary
opaque or PubSub payload bytes.
Registering a `ServerDataChannelProvider` for outer flows and optional Session
control does not inject it into the inner lane's listener settings. The server
supplies a context factory only to TCP/WSS listeners advertising an explicit
UASC Data Channel profile; the inner ordinary Service endpoint remains separate.

The outer native connection remains confidential and mutually authenticated.
The inner SecureChannel has its own tokens, sequence numbers, policy and
reservations. This composition deliberately requires an inner `Sign` or
`SignAndEncrypt` policy and ordinary certificate-manager validation. In
addition, both inner application certificates must match the corresponding
native application's authenticated URI and SHA-256 SPKI identity. Trusting a
third application's certificate does not authorize it to forward a UASC
conversation through the native peer.

`Sign` does not mean the native transport is unencrypted. Conversely, outer TLS
does not silently select an inner `None` policy. Discovery descriptions of a
native **data-only** endpoint must not be reused as the inner Service
description.

## Client composition

Keep the native connection alive while the lane is in use. Supply an explicit
virtual Service URL and ordinary UACP/UASC endpoint description. For example,
the native WebTransport CONNECT URL may be `opc.wt://host:4840/flow/v1` while
the configured inner Service URL is `opc.wt://host:4840/ua`. The latter is
selected by HEL inside the flow; it is not another HTTP CONNECT or socket.

The following uses already initialized application configuration, certificate
managers, and a ready native connection:

```csharp
using CertificateEntry own =
    clientCertificates.AcquireApplicationCertificateBySecurityPolicy(
        SecurityPolicies.Basic256Sha256)
    ?? throw new InvalidOperationException("The application certificate is unavailable.");

using Certificate peer = Certificate.FromRawData(
    nativeConnection.NativeConnection.VerifiedPeerCertificateChain[0]);

ServiceMessageContext messageContext = configuration.CreateMessageContext();
var settings = new TransportChannelSettings
{
    Description = serviceEndpointDescription,
    Configuration = EndpointConfiguration.Create(configuration),
    ClientCertificate = own.Certificate,
    ServerCertificate = peer,
    CertificateValidator = clientCertificates,
    Factory = messageContext.Factory,
    NamespaceUris = messageContext.NamespaceUris
};

var lanes = new DataChannelClientServerClient(telemetry);
await using DataChannelClientServerConnection lane = await lanes.ConnectAsync(
    nativeConnection.Context,
    new Uri(serviceEndpointDescription.EndpointUrl),
    settings,
    cancellationToken);

ISessionFactory sessions = new DefaultSessionFactory(telemetry)
    .WithSubscriptionEngine(DefaultSubscriptionEngineFactory.Instance);

using ISession session = sessions.Create(
    lane.Services,
    configuration,
    new ConfiguredEndpoint(null, lane.Services.EndpointDescription,
        lane.Services.EndpointConfiguration),
    own.Certificate.AddRef());

await session.OpenAsync(
    "Native Client/Server lane", 60000, userIdentity, [],
    checkDomain: true, closeChannel: false, cancellationToken);

// Use ordinary generated Session APIs: ReadAsync, CreateSubscriptionAsync,
// CreateMonitoredItemsAsync, PublishAsync, CallAsync, and others.

await session.CloseAsync(10000, closeChannel: false, cancellationToken);
```

`serviceEndpointDescription` must describe the intended application, its real
certificate, its configured user-token policies, and its selected inner
security policy. Its transport profile is `Profiles.UaTcpTransport`, identifying
the ordinary virtual UACP/UASC stack, **not** the outer native data profile.
Its URL must exactly match the Service URL passed to `ConnectAsync`.

The client's settings and certificate handles are borrowed during connection
establishment; the lane retains independent certificate references for its
own lifetime.

## Server composition and routing

Register the reserved resource with an explicit `IDataChannelSourcePolicy`,
alongside any independently authorized native resources. Inject the same
application-wide `DataChannelApplicationBudget` into the native binding and
the `ServerDataChannelProvider`. Use the provider as the native transport's
`IDataChannelContextFactory`.

The provider is the **sole** consumer of each registered context's accept
queue. The listener consumes only its exact resource/protocol/carrier route:

```csharp
var listener = new DataChannelClientServerListener(
    DataChannelCarrierKind.WebTransport,
    telemetry,
    source: dataChannelProvider);

var bindings = new DefaultTransportBindingRegistry();
bindings.RegisterListenerFactory(
    new DataChannelClientServerListenerFactory(listener));

server.TransportBindings = bindings;
server.UseDataChannels(dataChannelProvider, enableModel: false);

// BaseAddresses contains one explicit virtual Service URL for this factory:
// opc.wt://host:4840/ua. Configure the ordinary signed policies and identities.
await server.StartAsync(serverConfiguration, cancellationToken);

// Separately start/adopt the real native listener and accept native connections.
// Pass dataChannelProvider to that transport as its context factory.
```

The factory supplies its ordinary Service transport profile through
`ITransportListenerProfile`; it does not invent a native profile or change the
native data-only endpoint's discovery metadata. One factory serves one virtual
Service endpoint. A different listener/factory can serve the other carrier.

For a standalone application that already owns routing, construct the listener
without a source, open it using `TransportListenerSettings` and the ordinary
`ITransportListenerCallback`, then pass an already routed flow:

```csharp
await using DataChannelClientServerLane lane =
    await listener.AcceptAsync(reservedFlow, cancellationToken);
```

Alternatively, `AcceptAsync(IDataChannelAcceptedFlowSource, CancellationToken)`
accepts one reserved lane from a shared router. Do not concurrently consume
`DataChannelContext.AcceptAsync` when a provider owns that queue.

## Dependency injection

The client can be resolved through `IDataChannelClientServerClient`:

```csharp
services.AddOpcUa()
    .AddDataChannelClientServer(new DataChannelClientServerOptions
    {
        MaxChunkSize = 32768,
        MaxReceiveBytes = 262144,
        MaxLanes = 16,
        MaxPendingRequests = 128
    })
    .AddDataChannelClientServerListener(listener);
```

This registration preserves existing context factories and resource policies.
It does not register native credentials, load native images, start a native
listener, enable the model, or establish a Session. Those remain explicit
application decisions. Inject `IBufferManagerFactory`, `TimeProvider`, and
`ITelemetryContext` for client behavior; the direct listener constructor accepts
the same providers.

## Limits and lifetime

- Each lane reserves an independent flow and ordinary UASC admission resources.
  Native connection/application limits still apply.
- Negotiated UACP chunk size is bounded by the admitted flow's payload size.
  The common-record overhead is outside that payload. The default native
  65,536-byte record capacity therefore cannot carry a 65,536-byte UACP chunk.
  A lane cannot operate below the UACP minimum of 8,192 bytes.
- Client lane count, listener lane count and outstanding client Service requests
  have finite limits. The listener also reuses ordinary handshake deadlines,
  connection admission, reassembly budgets and server resource isolation.
  The client request limit includes Session keep-alive and background Publish
  requests; leave capacity for those when selecting a lower limit.
- Closing or disposing a lane resets only its flow. Asynchronous disposal joins
  native flow release and active writes; it never disposes the shared context.
- A standard Session may dispose its Service transport. The lane's
  `DisposeAsync` remains the ownership join before the host tears down the
  native connection.
- Cancelling an open or request does not abandon owned cleanup or retract a
  secured, numbered chunk after commitment.
- Reconnection is explicit: create a newly authorized lane, then use the
  ordinary Session reactivation APIs as appropriate. An old flow, security
  sequence, or native context is never silently reused as a replacement.
- Stop/dispose the Server and its lane listeners before disposing the native
  connections. The host that created a native listener or connection remains
  responsible for awaiting that object's own disposal/completion.

## Required native acceptance

`tests\Opc.Ua.DataChannel.ClientServer.Tests` is an opt-in native test project,
not a legacy/net48 or asset-free platform test. It requires the production
reviewed native QUIC and HTTP/3 images:

- `OPCUA_QUIC_NATIVE_PATH`, with the matching pack-generated SHA-256 sidecar.
- `OPCUA_HTTP3_NATIVE_PATH`.
- `OPCUA_HTTP3_NATIVE_SHA256`, the independently reviewed HTTP/3 image digest.

With the .NET 10 SDK and the native prerequisites configured:

```powershell
dotnet test tests\Opc.Ua.DataChannel.ClientServer.Tests\Opc.Ua.DataChannel.ClientServer.Tests.csproj `
    -p:CustomTestTarget=net10.0
```

Missing prerequisites fail the native acceptance gate. Tests do not skip the
carrier, replace sockets with mocks, disable peer/hostname validation, or
install a production trust bypass.
