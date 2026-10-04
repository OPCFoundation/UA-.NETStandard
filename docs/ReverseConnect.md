# Reverse Connect

## Overview

The stack supports Reverse Connect through these components:

- The C# stack implements the `ReverseHello` message for clients and servers.
- The server library provides server-initiated connections through
  `ReverseConnectServer`. The regular dependency-injection server uses this
  support automatically. Configuration options control client locations
  and timeouts, and the API lets applications manage client connections.
- The client library provides `ReverseConnectManager` to accept
  `ReverseHello` messages. Applications can register for connections by
  callback or wait for a message matching a server endpoint and application
  URI. Optional server-URI and endpoint-URL filters let multiple clients
  share one listener.
- The [Console Reference Server](../samples/Reference/ConsoleReferenceServer)
  supports reverse connect through its configuration XML.
- The [Console Reference Client](../samples/Reference/ConsoleReferenceClient)
  can initiate a reverse connection through command-line options.
- The [Aggregation Server](https://github.com/OPCFoundation/UA-.NETStandard-Samples/tree/master/Workshop/Aggregation)
  supports incoming and outgoing reverse connections.

## Contents

- [Overview](#overview)
- [Reverse Connect Handshake](#reverse-connect-handshake)
- [Sharing a listener across multiple Servers](#sharing-a-listener-across-multiple-servers)
- [Server-side dependency injection](#server-side-dependency-injection)
- [Dependency-injection lifecycle](#dependency-injection-lifecycle)
- [Configuration Extensions](#configuration-extensions)
- [WSS reverse-connect](#wss-reverse-connect-opcwss)
- [Kestrel-hosted opc.tcp reverse-connect](#kestrel-hosted-opctcp-reverse-connect-opt-in)
- [Known limitations and issues](#known-limitations-and-issues)

## Reverse Connect Handshake

More details on the reverse connect handshake can be found in the OPC UA spec Part 6, [Establishing a connection](https://reference.opcfoundation.org/v104/Core/docs/Part6/7.1.3/).

The *ReverseHello* message allows Servers behind firewalls to initiate communication with Clients. This requires that the Server be pre-configured with the location of the Client. The Server adds a configuration option that can be used to initiate a *ReverseHello* with the Client running behind a firewall.

Once the reverse connection is established, the Server will automatically re-establish the connection if it is closed. Most servers continue sending `ReverseHello` messages after a client connects.
This implementation offers three behaviors: continue sending messages, allow
only one connection, or stop sending after the server reaches its maximum
session count. In single-connection mode, a rejected connection suspends
further messages for a configurable timeout. A client rejects an unsupported
or unwanted connection with `BadTcpMessageTypeInvalid`.

To accept reverse connections, configure a client `ReverseConnectManager` to
invoke registered callbacks or hold incoming connections open for a
configurable timeout. An application can register a callback to accept or
reject requests, or start a connection and wait for a matching
`ReverseHello`. If the manager already holds an open connection to the
server, the client can establish the session without waiting.

The client transport implements `ITransportListener` and routes incoming
connections through `ReverseConnectionManager`. One client port can accept
`ReverseHello` messages from multiple servers. Register callbacks for a
specific server URI, endpoint URL, or any incoming connection.

When a client receives `ReverseHello`, its callback receives an
`ITransportWaitingConnection`. Pass this connection object to the session
connect API to create a session, much like connecting with an endpoint URL.
The client uses the open socket to send `Hello` back to the server and
continue secure-channel establishment.

Alternatively, call the Connect API with a configured
`ReverseConnectionManager`. The client waits for an incoming connection and
establishes the session if it arrives before the timeout expires. Existing
applications can adopt this pattern without changing their broader
connection flow.

If no client responds to `ReverseHello`, or the client rejects it, the
channel closes with `BadTcpMessageTypeInvalid`. The server should interpret
this as an indication that the client is not configured to accept that
server's reverse connections.

After accepting the connection, a client that needs a secure channel must
call `GetEndpoints` to retrieve the server certificate. The client then
closes that channel and waits for the server to reconnect. On reconnection,
the client can use the cached security information to connect securely.
An optimized server can send `ReverseHello` immediately after `GetEndpoints`
to reduce this delay.

Server auto-reconnect is essential because clients close the socket when
the SecureChannel closes. The specification requires a server to stop
reconnecting if it receives `BadTcpMessageTypeInvalid`. Older peers that do
not support `ReverseHello` return this error. A client can also return it
when a user rejects the connection.

In this implementation, a server configured for a single connection waits
for an extended, configurable timeout before sending another `ReverseHello`.
In other modes, the server continues sending `ReverseHello` messages at the
configured interval.

## Sharing a listener across multiple Servers

A reverse-connect listener remains bound for the lifetime of its `ReverseConnectManager`. Seeing the listener port remain in the `LISTENING` state after a Session is established is expected. The listening socket accepts additional transport connections while each accepted socket is handed to the Session that claimed its `ReverseHello` message. Dispose the manager when the listener should be released.

Use one shared `ReverseConnectManager` for all Servers that connect to the same Client URL. Register or wait for each Server separately by using its Server `EndpointUrl` and, preferably, its `ServerUri`. The fluent dependency-injection integration registers the manager as a singleton.

A `ManagedSession` needs that manager for recovery as well as initial connection.
A waiting `ITransportWaitingConnection` supplied directly to `CreateAsync` is
single-use. Without a `ReverseConnectManager`, recovery fails through the configured
reconnect policy instead of obtaining a fresh connection; it never silently
switches to an outbound connection.

``` csharp
await using var manager = new ReverseConnectManager(telemetry);
manager.AddEndpoint(new Uri("opc.tcp://client-host:65300"));
await manager.StartServiceAsync(
    new ReverseConnectClientConfiguration
    {
        HoldTime = 15000,
        WaitTimeout = 20000
    },
    cancellationToken).ConfigureAwait(false);

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

`HoldTime` controls how long an incoming `ReverseHello` that does not match any registration yet is held open before it is rejected. This matters when several Servers connect before the application has registered a waiting connection for each of them: every held connection waits for the remainder of its own hold time, also when a registration for a different Server arrives in the meantime. A Server whose hold time expires without a matching registration is rejected and reconnects on its next `ReverseHello` interval. A committed stop or dispose releases the held connections immediately.

`StartServiceAsync` validates and prepares the complete listener set before it changes a running service. If activation fails after existing listeners have stopped, the manager recreates and reopens the previous configuration. Cancellation cleans partially initialized candidates and either preserves or restores the prior service. Use `await manager.StopServiceAsync(...)` for an explicit stop and `await manager.DisposeAsync()` (or `await using`) for teardown.

The synchronous `StartService`, `RegisterWaitingConnection`, and `Dispose` APIs remain as obsolete compatibility wrappers. New code should use `StartServiceAsync`, `RegisterWaitingConnectionAsync`, and `DisposeAsync`; the compatibility wrappers may block a caller thread.

## Server-side dependency injection

The regular `AddServer(...)` host uses
`DependencyInjectionStandardServer`, which derives from
`ReverseConnectServer`. Configured outbound reverse connections start
and stop with the hosted server. Existing DI hooks for node managers,
session and subscription services, identity, and startup tasks coexist
with reverse connect; an application does not need a server subclass.
Queued callbacks from disposed or superseded retry timers are ignored,
including during startup-failure cleanup.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.Server.Hosting;

services.AddOpcUa()
    .AddServer(options =>
    {
        options.ApplicationName = "PlantServer";
        options.EndpointUrls.Add("opc.tcp://localhost:4840/PlantServer");
    })
    .AddReverseConnect(options =>
    {
        options.ConnectIntervalMs = 15000;
        options.Clients.Add(new ServerReverseConnectClientOptions
        {
            EndpointUrl = "opc.tcp://client.example.com:65300",
            Timeout = 30000,
            MaxSessionCount = 1,
            Enabled = true
        });
    });
```

`AddReverseConnect(...)` configures `OpcUaServerOptions.ReverseConnect`.
The projection into `ServerConfiguration.ReverseConnect` is unchanged,
and the same options can be bound from `OpcUa:Server:ReverseConnect`.

On server configuration reload, removing the `ReverseConnect` section or
emptying its clients removes only configuration-owned connections. Connections
added through `AddReverseConnection` retain their settings and are not
overwritten by a configured client with the same endpoint URL.

When `ConfigurationFile` or `ConfigurationStream` supplies the
application configuration, that document is authoritative. The
`ReverseConnect` options and `AddReverseConnect(...)` shortcut do not
overwrite it. Use `ConfigureLoadedConfiguration` to change loaded
settings; this example retains the file's clients and changes only
their retry interval:

```csharp
services.AddOpcUa().AddServer(options =>
{
    options.ConfigurationFile = "PlantServer.Config.xml";
    options.ConfigureLoadedConfiguration = configuration =>
    {
        var reverseConnect = configuration.ServerConfiguration.ReverseConnect
            ?? throw new InvalidOperationException(
                "Configure reverse-connect clients in the XML file.");
        reverseConnect.ConnectInterval = 20000;
    };
});
```

The ordinary `StandardServer` is unchanged. Applications constructing
servers manually still choose `ReverseConnectServer` explicitly when
they need server-initiated connections; setting reverse-connect
configuration alone does not add that behavior to `StandardServer`.
See [Dependency Injection](DependencyInjection.md#server-side-reverse-connect)
for the complete options shape and other hosted-server customization.

## Dependency-injection lifecycle

`AddClient(...)` registers one singleton `ReverseConnectManager`, an `IReverseConnectConfigurationProvider`, and an internal hosted service. In a .NET Generic Host, the hosted service eagerly opens configured listeners during host startup and closes them during host shutdown. In a plain `ServiceCollection` without a running host, `WaitForConnectionAsync` and `RegisterWaitingConnectionAsync` call `EnsureStartedAsync` lazily. Resolving the manager itself never blocks on listener startup.

``` csharp
services
    .AddOpcUa()
    .AddClient(options =>
    {
        options.Configuration = applicationConfiguration;
        options.ReverseConnect = new ClientReverseConnectOptions
        {
            HoldTimeMs = 15000,
            WaitTimeoutMs = 20000
        };
        options.ReverseConnect.ClientEndpointUrls.Add(
            "opc.tcp://client-host:65300");
    });
```

Applications can replace the default pass-through provider to asynchronously validate or transform the effective listener configuration before any active listener is stopped:

``` csharp
services.AddSingleton<IReverseConnectConfigurationProvider, MyProvider>();
```

Providers run outside the manager's lifecycle gate. Provider exceptions reject the candidate while the current service remains active. The former protected `OnUpdateConfiguration` hooks were removed; custom configuration logic belongs in `IReverseConnectConfigurationProvider`.

Do not create a separate manager for each Server when those managers use the same local listener URL. Only one listener can bind a given host and port. An invalid URL reports `BadTcpEndpointUrlInvalid`, an unsupported transport retains its transport-specific status, and a bind or listener-open failure reports `BadNoCommunication`. Startup diagnostics identify the affected endpoint URLs, and listeners opened by a failed attempt are closed instead of allowing a later connection wait to time out.

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
await manager.StartServiceAsync(config, cancellationToken)
    .ConfigureAwait(false);
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
