# ReverseHello Prototype Readme #
## Overview ##
The ReverseHello Prototype codebase includes 7 elements:

* Modified C# Stack that supports the ReverseHello message;
* A C# Server that can initiate a ReverseHello;
* A C# Client that listens for ReverseHellos and connects to the Server;

## ReverseHello Handshake ##
The ReverseHello allows Servers behind firewalls to initiate communication with Clients. This requires that the Server be pre-configured with the location of the Client. The Server example adds a menu option that can be used initiate a ReverseHello with the Client sample running on the same machine. Once the reverse connection is established the Server will automatically re-establish the connection if it is closed. The auto-reconnect is stopped if the connection is rejected (i.e. the Client returns BadTcpMessageTypeInvalid meaning the Client does not support reverse connections or it does not want a connection from this Server).

When the Client receives a ReverseHello for the first time it pops up a dialog asking the user to accept the incoming connection. If it rejected the channel is closed with BadTcpMessageTypeInvalid error which the Server should intepret to mean the Client does not support ReverseHellos. If it is accepted the Client calls GetEndpoints and fetches the Server Certificate. At this point the Client closes the channel which means it needs to wait for the Server to automatically re-connect. When it does it can use the security information previously cached to connect securely back to the Server.

The auto-reconnect behavoir on the Server is essential to any real application because Clients close the Socket when the SecureChannel is closed. Servers need to abort the auto-reconnect if it receives a BadTcpMessageTypeInvalid code because that is the error it will receive from peers that have not been upgraded to support the ReverseHello. Because of this the Client sample can use the same error code to tell the Server to stop reconnecting because the user has rejected the connection.





