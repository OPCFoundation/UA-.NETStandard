# OPC Foundation UA .NET Standard Reference Client

## Introduction

The console reference client can be configured using several console parameters.
Some of these parameters are explained in more detail below.

To see all available parameters call console reference client with the parameter `-h`.

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

