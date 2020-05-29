# ReverseConnect Readme #
## Overview ##
The ReverseConnect option for client and server consists of the following elements:

* Updated C# Stack that supports the ReverseHello message for client and server;
* Updated server library which support to 
  - Create a ReverseConnectServer derived from a StandardServer class.
  - Extended configuration parameters to setup the client location and timeouts.
  - an API extension in the ReverseConnectServer to programmatically control client connections.
* Updated client library which support to 
  - Configure an endpoint for client reverse hello messages.
  - A client API extension to allow applications to register for reverse connections either by callback or by waiting for the ReverseHello message. An optional filter for server Uris or endpoint Urls can be applied.
* The C# Core Client and Server samples that can initiate a Reverse connection with command line options.
* A modified C# Aggregation Server that supports incoming and outgoing reverse connections.

## ReverseConnect Handshake ##
The ReverseHello message allows Servers behind firewalls to initiate communication with Clients. This requires that the Server be pre-configured with the location of the Client. The Server adds a configuration option that can be used initiate a ReverseHello with the Client sample running on the same machine. 

Once the reverse connection is established the Server will automatically re-establish the connection if it is closed. The auto-reconnect is suspended for a certain timeout if the connection is rejected (i.e. the Client returns BadTcpMessageTypeInvalid meaning the Client does not support reverse connections or it does not want a connection from this Server).

In order to support reverse connect the client needs to start a host which manages the port for the servers to connect with the ReverseHello message, called the ReverseConnectionManager class. Only a single port is needed on a client to support multiple incoming reverse hello server connections. The clients register at the ReverseConnectionManager for specific serversUris, endpointUrls or any incoming message.

When the Client receives a ReverseHello it receives an ITransportWaitingConnection connection object which can be used to create a client session in a similar way as by connecting using the endpointUrl, just by adding the connection object to the call. The client can use the open socket to send the Hello message back to the server for the usual initiation of a communication session. 

If no client responds to the ReverseHello message or if it is even rejected, the channel is closed with BadTcpMessageTypeInvalid error which the Server should interpret as a means that the Client is not configured to respond to ReverseHello messages of that server. 

If the client accepts the connection, currently, a secure connection requires that the Client calls GetEndpoints and fetches the Server Certificate. At this point the Client closes the channel which means it needs to wait for the Server to automatically re-connect. When it does it can use the security information previously cached to connect securely back to the Server.

The auto-reconnect behavoir on the Server is essential to any real application because Clients close the Socket when the SecureChannel is closed. Servers need to abort the auto-reconnect if it receives a BadTcpMessageTypeInvalid code because that is the error it will receive from peers that have not been upgraded to support the ReverseHello. Because of this the Client sample can use the same error code to tell the Server to stop reconnecting because the user has rejected the connection. In this implementation the server will just apply an extended timeout before reconnecting to the client, reducing the overall traffic.





