# OPC Foundation UA .NET Standard Reference Client

## Introduction

The console reference client can be configured using several console parameters.
Some of these parameters are explained in more detail below.

To see all available parameters call console reference client with the parameter `-h`.

## Supported transport profiles

The client picks the wire transport automatically from the `serverUrl`
scheme. The OPC UA spec aliases the URL scheme to the
[`TransportProfileUri`](https://reference.opcfoundation.org/Core/Part6/v107/docs/7.4)
on the negotiated endpoint:

| URL scheme | TransportProfileUri | OPC UA Part 6 reference |
| --- | --- | --- |
| `opc.tcp://` | `uatcp-uasc-uabinary` | §7.4.2 |
| `opc.https://` (or `https://`) with binary body | `https-uabinary` | §7.4.4 |
| `opc.https://` (or `https://`) with `application/opcua+uajson` body | `https-uajson` | §7.4.5 |
| `opc.wss://` (or `wss://`) with `opcua+uacp` sub-protocol | `uawss-uasc-uabinary` | §7.5.2 |
| `opc.wss://` (or `wss://`) with `opcua+uajson` sub-protocol | `uawss-uajson` | §7.5.2 |

Examples:

```bash
# UA-TCP (default)
dotnet ConsoleReferenceClient.dll opc.tcp://localhost:62541/Quickstarts/ReferenceServer

# WSS+uacp (binary UA SecureChannel over TLS WebSocket)
dotnet ConsoleReferenceClient.dll opc.wss://localhost:62543/Quickstarts/ReferenceServer

# HTTPS (binary)
dotnet ConsoleReferenceClient.dll opc.https://localhost:62543/Quickstarts/ReferenceServer
```

The reference server (`ConsoleReferenceServer`) advertises the
`opc.tcp://`, `opc.https://` and `opc.wss://` endpoints by default; see
[`Docs/Transports.md`](../../Docs/Transports.md) for the full server
configuration story (TLS certificates, mutual TLS, JSON sub-protocol
restrictions, etc.).

## Reverse Connect

The OPC UA reverse connect feature allows an OPC UA server to initiate the connection to a client, rather than the traditional model where clients connect to servers. This is particularly useful in scenarios where the server is behind a firewall or NAT, making it difficult for clients to directly connect to it.

### How to use Reverse Connect

To enable reverse connect mode, specify the client endpoint URL using the `--rc` or `--reverseconnect` parameter:

```bash
dotnet ConsoleReferenceClient.dll --rc=opc.tcp://localhost:65300 opc.tcp://localhost:62541/Quickstarts/ReferenceServer
```

The client will start a reverse connect listener on the specified endpoint (e.g., `opc.tcp://localhost:65300`) and wait for the server to connect to it.

### Example: Client and Server with Reverse Connect

1. Start the client with reverse connect listener on port 65300:
   ```bash
   dotnet ConsoleReferenceClient.dll --rc=opc.tcp://localhost:65300 opc.tcp://localhost:62541/Quickstarts/ReferenceServer
   ```

2. In a separate terminal, start the server with reverse connect to the client:
   ```bash
   dotnet ConsoleReferenceServer.dll --rc=opc.tcp://localhost:65300 -a
   ```

The server will establish a reverse connection to the client endpoint, and the client will use this connection to communicate with the server.

### How to specify User Identity
#### Username & Password
Specify as console parameters:
    `-un YourUsername`
    `-up YourPassword`

#### Certificate
Place your user certificate in the TrustedUserCertificatesStore (the path can be found in the client configuration XML). Make sure to include an accessible private key with the certificate.
Specify console parameters:
    `-uc Thumbprint` (of the user certificate to select)
    `-ucp Password` (of the user certificates private key (optional))

