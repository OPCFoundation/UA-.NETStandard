# Reverse Connect #
## Overview  ##

The Reverse Connect option consists of the following elements:

* Updated C# Stack that supports the *ReverseHello* message for Client and Server;
* Updated server library which support to 
  - Create a *ReverseConnectServer* derived from a *StandardServer* class.
  - Extended configuration parameters to setup the client location and timeouts.
  - an API extension in the *ReverseConnectServer* to programmatically control client connections.
* Updated client library which support to 
  - Configure a client endpoint to accept *ReverseHello* messages using a *ReverseConnectManager*.
  - A client API extension to allow applications to register for reverse connections either by callback or by waiting for the *ReverseHello* message for a specific server endpoint and application Uri combination. An optional filter for server Uris or endpoint Urls can be applied to allow multiple clients to use the same endpoint.
* The updated C# [Reference Server](../Applications/ConsoleReferenceServer) with reverse connect support.
* The C# Core [Client](https://github.com/OPCFoundation/UA-.NETStandard-Samples/tree/master/Samples/NetCoreComplexClient) and [Server](https://github.com/OPCFoundation/UA-.NETStandard-Samples/tree/master/Samples/NetCoreConsoleServer) samples that can initiate a Reverse connection with command line options.
* A modified C# [Aggregation Server](https://github.com/OPCFoundation/UA-.NETStandard-Samples/tree/master/Workshop/Aggregation) that supports incoming and outgoing reverse connections.

## Reverse Connect Handshake ##
More details on the reverse connect handshake can be found in the OPC UA spec Part 6, [Establishing a connection](https://reference.opcfoundation.org/v104/Core/docs/Part6/7.1.3/).

The *ReverseHello* message allows Servers behind firewalls to initiate communication with Clients. This requires that the Server be pre-configured with the location of the Client. The Server adds a configuration option that can be used to initiate a *ReverseHello* with the Client running behind a firewall. 

Once the reverse connection is established, the Server will automatically re-establish the connection if it is closed. Most Servers keep sending *ReverseHello* messages, even if the Client is already connected. In this Server implementation the behavior is configurable to keep sending *ReverseHello* messages, to allow only a single connection or to stop sending messages once the maximum number of Server sessions is exceeded. Only for the single connection configuration the sending of messages is suspended for a configurable timeout if the connection is rejected (i.e. the Client returns *BadTcpMessageTypeInvalid* meaning the Client does not support reverse connections or it does not want a connection from this Server at this time).

In order to validate and accept a reverse connection in a Client application a *ReverseConnectManager* is configured to call back to the registered applications or to hold incoming connections open for a programmable timeout. An application can register for incoming requests to accept or reject a reverse connection directly or an application can start a connection and wait for an incoming *ReverseHello* message. If the *ReverseConnectManager* holds already an open connection to the Server the connection can be established without waiting. 

The host port is implemented by a transport which implements the *ITransportListener* interface. The transport calls the *ReverseConnectionManager* class in the client library to provide the application interface. This implementation uses only a single port on a client to support multiple incoming *ReverseHello* server connections. The clients register at the *ReverseConnectionManager* for specific serversUris, endpointUrls or any incoming message for callbacks. When the Client receives a *ReverseHello* the application receives an *ITransportWaitingConnection* connection object which can be used to create a client session in a similar way as by connecting using the endpointUrl, just by using a different Connect API which supports the connection object as a parameter. The client then uses the open socket to send the *Hello* message back to the Server for the well known establishment of a secure communication session. 

The second option for a client application is to call the Connect API with a configured *ReverseConnectionManager* to wait for an incoming connection and to establish the connection before the timeout expires. This connection model is similar to the standard connect flow with a Server and might be a good model to add reverse connect support for existing applications, without changing the application logic.

If no client responds to the *ReverseHello* message or if it is even rejected, the channel is closed with a *BadTcpMessageTypeInvalid* error which the server should interpret as an indication that the Client is not configured to respond to *ReverseHello* messages of that Server. 

If the Client accepts the connection, a secure connection requires that the Client calls *GetEndpoints* to fetch the Server Certificate. At this point the Client closes the channel, which means it needs to wait for the Server to automatically re-connect. When it does, it can use the security information previously cached to connect securely back to the Server. An optimized Server implementation could respond with an immediate *ReverseHello* message to avoid connection delays after a call to *GetEndpoints* .

The auto-reconnect behavior on the Server is essential to any real application, because Clients close the Socket when the SecureChannel is closed. According to the specification a Server needs to abort the auto-reconnect if it receives a *BadTcpMessageTypeInvalid* code, because that is the error it will receive from peers that have not been upgraded to support the *ReverseHello*. Because of this a Client can use the same error code to tell the Server to stop reconnecting, if a user has rejected the connection. However, in this implementation, only if the Server is configured for a single connection it applies an extended timeout before reconnecting to the Client to reduce the overall traffic. In other configurations the Server keeps sending the *ReverseHello* messages at the configured time interval.

## Configuration Extensions

This configuration sample shows the configuration setting for a reverse connection on port 65300.

The Server configuration extension to connect to one or more reverse connect clients:

```
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

```
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



## Known limitations and issues

- Only support for TCP connections is implemented. Https transport is currently out of scope.

- Only a limited number of samples is available yet, the Reference Server, the Aggregation Server and the Console server and client.

  