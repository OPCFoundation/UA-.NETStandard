# Reverse Connect

## Overview

The Reverse Connect option consists of the following elements:

* Updated C# Stack that supports the *ReverseHello* message for Client and Server;
* Updated server library which support to
  * Create a *ReverseConnectServer* derived from a *StandardServer* class.
  * Extended configuration parameters to setup the client location and timeouts.
  * an API extension in the *ReverseConnectServer* to programmatically control client connections.
* Updated client library which support to
  * Configure a client endpoint to accept *ReverseHello* messages using a *ReverseConnectManager*.
  * A client API extension to allow applications to register for reverse connections either by callback or by waiting for the *ReverseHello* message for a specific server endpoint and application Uri combination. An optional filter for server Uris or endpoint Urls can be applied to allow multiple clients to use the same endpoint.
* The C# [Console Reference Server](../Applications/ConsoleReferenceServer) with reverse connect support in the configuration xml.
* The C# Core [Console Reference Client](../Applications/ConsoleReferenceClient) that can initiate a Reverse connection with command line options.
* A modified C# [Aggregation Server](https://github.com/OPCFoundation/UA-.NETStandard-Samples/tree/master/Workshop/Aggregation) that supports incoming and outgoing reverse connections.

## Reverse Connect Handshake

More details on the reverse connect handshake can be found in the OPC UA spec Part 6, [Establishing a connection](https://reference.opcfoundation.org/v104/Core/docs/Part6/7.1.3/).

The *ReverseHello* message allows Servers behind firewalls to initiate communication with Clients. This requires that the Server be pre-configured with the location of the Client. The Server adds a configuration option that can be used to initiate a *ReverseHello* with the Client running behind a firewall.

Once the reverse connection is established, the Server will automatically re-establish the connection if it is closed. Most Servers keep sending *ReverseHello* messages, even if the Client is already connected. In this Server implementation the behavior is configurable to keep sending *ReverseHello* messages, to allow only a single connection or to stop sending messages once the maximum number of Server sessions is exceeded. Only for the single connection configuration the sending of messages is suspended for a configurable timeout if the connection is rejected (i.e. the Client returns *BadTcpMessageTypeInvalid* meaning the Client does not support reverse connections or it does not want a connection from this Server at this time).

In order to validate and accept a reverse connection in a Client application a *ReverseConnectManager* is configured to call back to the registered applications or to hold incoming connections open for a programmable timeout. An application can register for incoming requests to accept or reject a reverse connection directly or an application can start a connection and wait for an incoming *ReverseHello* message. If the *ReverseConnectManager* holds already an open connection to the Server the connection can be established without waiting.

The host port is implemented by a transport which implements the *ITransportListener* interface. The transport calls the *ReverseConnectionManager* class in the client library to provide the application interface. This implementation uses only a single port on a client to support multiple incoming *ReverseHello* server connections. The clients register at the *ReverseConnectionManager* for specific serversUris, endpointUrls or any incoming message for callbacks. When the Client receives a *ReverseHello* the application receives an *ITransportWaitingConnection* connection object which can be used to create a client session in a similar way as by connecting using the endpointUrl, just by using a different Connect API which supports the connection object as a parameter. The client then uses the open socket to send the *Hello* message back to the Server for the well known establishment of a secure communication session.

The second option for a client application is to call the Connect API with a configured *ReverseConnectionManager* to wait for an incoming connection and to establish the connection before the timeout expires. This connection model is similar to the standard connect flow with a Server and might be a good model to add reverse connect support for existing applications, without changing the application logic.

If no client responds to the *ReverseHello* message or if it is even rejected, the channel is closed with a *BadTcpMessageTypeInvalid* error which the server should interpret as an indication that the Client is not configured to respond to *ReverseHello* messages of that Server.

If the Client accepts the connection, a secure connection requires that the Client calls *GetEndpoints* to fetch the Server Certificate. At this point the Client closes the channel, which means it needs to wait for the Server to automatically re-connect. When it does, it can use the security information previously cached to connect securely back to the Server. An optimized Server implementation could respond with an immediate *ReverseHello* message to avoid connection delays after a call to *GetEndpoints* .

The auto-reconnect behavior on the Server is essential to any real application, because Clients close the Socket when the SecureChannel is closed. According to the specification a Server needs to abort the auto-reconnect if it receives a *BadTcpMessageTypeInvalid* code, because that is the error it will receive from peers that have not been upgraded to support the *ReverseHello*. Because of this a Client can use the same error code to tell the Server to stop reconnecting, if a user has rejected the connection. However, in this implementation, only if the Server is configured for a single connection it applies an extended timeout before reconnecting to the Client to reduce the overall traffic. In other configurations the Server keeps sending the *ReverseHello* messages at the configured time interval.

## Sharing a listener across multiple Servers

A reverse-connect listener remains bound for the lifetime of its `ReverseConnectManager`. Seeing the listener port remain in the `LISTENING` state after a Session is established is expected. The listening socket accepts additional transport connections while each accepted socket is handed to the Session that claimed its `ReverseHello` message. Dispose the manager when the listener should be released.

Use one shared `ReverseConnectManager` for all Servers that connect to the same Client URL. Register or wait for each Server separately by using its Server `EndpointUrl` and, preferably, its `ServerUri`. The fluent dependency-injection integration registers the manager as a singleton.

``` csharp
using var manager = new ReverseConnectManager(telemetry);
manager.AddEndpoint(new Uri("opc.tcp://client-host:65300"));
manager.StartService(new ReverseConnectClientConfiguration
{
    HoldTime = 15000,
    WaitTimeout = 20000
});

Task<ITransportWaitingConnection> serverA = manager.WaitForConnectionAsync(
    new Uri("opc.tcp://server-a:4840"),
    "urn:example:server-a",
    cancellationToken);
Task<ITransportWaitingConnection> serverB = manager.WaitForConnectionAsync(
    new Uri("opc.tcp://server-b:4840"),
    "urn:example:server-b",
    cancellationToken);

await Task.WhenAll(serverA, serverB).ConfigureAwait(false);
```

Pass each returned `ITransportWaitingConnection` to the session factory for the corresponding Server.

Do not create a separate manager for each Server when those managers use the same local listener URL. Only one listener can bind a given host and port. `StartService` validates and opens all configured listener endpoints atomically. An invalid URL reports `BadTcpEndpointUrlInvalid`, an unsupported transport retains its transport-specific status, and a bind or listener-open failure reports `BadNoCommunication`. Startup diagnostics identify the affected endpoint URLs, and listeners opened by a failed attempt are closed instead of allowing a later connection wait to time out.

This behavior follows [OPC UA Part 6, 7.1.3](https://reference.opcfoundation.org/specs/OPC-10000-6/v1.05.07/7.1.3), which defines a separate transport connection for each reverse connection and requires Servers to maintain an available socket to each configured Client. [OPC UA Part 12, 4.4.2](https://reference.opcfoundation.org/specs/OPC-10000-12/v1.05.07/4.4.2) defines one or more Client URLs that allow Servers to connect. Clients shall validate the `ServerUri` and `EndpointUrl` as described in [OPC UA Part 2, 6.14](https://reference.opcfoundation.org/specs/OPC-10000-2/v1.05.06/6.14).

## Configuration Extensions

This configuration sample shows the configuration setting for a reverse connection on port 65300.

The Server configuration extension to connect to one or more reverse connect clients:

``` xml
</ServerConfiguration>
  ...
  <ReverseConnect>
    <Clients>
      <ReverseConnectClient>
        <EndpointUrl>opc.tcp://localhost:65300</EndpointUrl>
        <Timeout>30000</Timeout>
      </ReverseConnectClient>
    </Clients>
    <ConnectInterval>15000</ConnectInterval>
    <ConnectTimeout>30000</ConnectTimeout>
    <RejectTimeout>60000</RejectTimeout>
  </ReverseConnect>
</ServerConfiguration>

```

The Client configuration extension to allow incoming connections for one or more reverse connect servers:

``` xml
<ClientConfiguration>
  ...
  <ReverseConnect>
    <ClientEndpoints>
      <ClientEndpoint>
        <EndpointUrl>opc.tcp://localhost:65300</EndpointUrl>
      </ClientEndpoint>
    </ClientEndpoints>
    <HoldTime>15000</HoldTime>
    <WaitTimeout>20000</WaitTimeout>
  </ReverseConnect>
</ClientConfiguration>
```

## WSS reverse-connect (`opc.wss://`)

The WSS reverse-connect path covers the same two halves as the
TCP path but layered over TLS + WebSocket:

* **Server-side outbound** — `HttpsTransportListener.CreateReverseConnection`
  opens an outbound `ClientWebSocket` to the configured client URI,
  wraps the resulting `WebSocket` in a `WebSocketClientByteTransport`,
  and drives the reverse-hello handshake via the same
  `TcpServerChannel.BeginReverseConnect(... IUaSCByteTransport ...)`
  overload the TCP path uses. The server's `ReverseConnect` configuration
  block accepts `opc.wss://...` URIs identical to the TCP form above.

* **Client-side listener** — `ReverseConnectHost.CreateListener` accepts
  an optional TLS certificate (and validator) pair for `opc.wss://`
  reverse-connect endpoints. The host wires the cert into the
  Kestrel-backed `HttpsTransportListener` via
  `settings.ReverseConnectListener = true`, dispatches each accepted
  WSS upgrade to a `TcpReverseConnectChannel`, and fires
  `ConnectionWaiting` via `TransferListenerChannelAsync` when the
  `ReverseHello` arrives. The application takes ownership of the
  `ITransportWaitingConnection` exactly as it would for the TCP path.

`ReverseConnectManager.AddEndpoint` gained an additive overload to
support TLS-terminating listeners:

``` csharp
// Old: works for opc.tcp:// (no TLS state needed at bind time).
manager.AddEndpoint(new Uri("opc.tcp://localhost:65300"));

// New: also works for opc.wss:// because the CertificateManager is
// supplied at AddEndpoint time (m_appConfig is otherwise only set
// when StartService runs - too late for WSS listeners that need the
// server certificate at bind time).
manager.AddEndpoint(new Uri("opc.wss://localhost:65300"), config);
manager.StartService(config);
```

The original single-parameter `AddEndpoint(Uri)` is unchanged for
back-compat (opc.tcp consumers do not need the new overload).

## Kestrel-hosted opc.tcp reverse-connect (opt-in)

The `Opc.Ua.Bindings.Https` package serves the `opc.tcp`
listener from a Kestrel `IHost` instead of raw `Socket` + SAEA (an
opt-in alternative on `net8.0`+ to the default raw-socket listener that
ships in `Opc.Ua.Core`). It
supports the full forward AND reverse-connect listener modes the
raw-socket `TcpTransportListener` does. To use it, install the binding
via DI before opening the listener:

``` csharp
// Microsoft.Extensions.DependencyInjection consumers:
services
    .AddOpcUa()
    .AddOpcTcpTransport()           // raw-socket opc.tcp default
    .AddKestrelOpcTcpTransport();   // overrides with Kestrel (last-writer-wins)

// Non-DI consumers (e.g. test fixtures):
DefaultTransportBindingRegistry registry =
    DefaultTransportBindingRegistry.WithDefaultTcp();
registry.RegisterListenerFactory(new KestrelTcpTransportListenerFactory());
// Forward the registry to:
//   - ServerBase via the new ctor overload or .TransportBindings setter
//     (server-side reverse-connect outbound and forward listeners),
//   - ReverseConnectManager.TransportBindings (client-side reverse-connect
//     listener: every AddEndpoint(Uri,...) call constructs a
//     ReverseConnectHost that resolves the right factory from this registry).
```

The default raw-socket implementation stays available for
deployments that want to avoid the ASP.NET Core dependency in
`Opc.Ua.Core`.

## Known limitations and issues

* Only a limited number of samples is available yet, the Reference Server, the Aggregation Server and the Console server and client.
