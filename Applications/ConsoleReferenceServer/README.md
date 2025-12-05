# OPC Foundation UA .NET Standard Reference Server

## Introduction

The console reference server can be configured using several console parameters.
Some of these parameters are explained in more detail below.

To see all available parameters call console reference server with the parameter `-h`.

## Reverse Connect

The OPC UA reverse connect feature allows an OPC UA server to initiate the connection to a client, rather than the traditional model where clients connect to servers. This is particularly useful in scenarios where the server is behind a firewall or NAT, making it difficult for clients to directly connect to it.

### How to use Reverse Connect

To enable reverse connect mode, specify the client endpoint URL using the `--rc` or `--reverseconnect` parameter:

```bash
dotnet ConsoleReferenceServer.dll --rc=opc.tcp://localhost:65300
```

or

```bash
dotnet ConsoleReferenceServer.dll --reverseconnect=opc.tcp://localhost:65300
```

### Example: Server and Client with Reverse Connect

1. Start the client with reverse connect listener on port 65300:
   ```bash
   dotnet ConsoleReferenceClient.dll --rc=opc.tcp://localhost:65300 opc.tcp://localhost:62541/Quickstarts/ReferenceServer
   ```

2. In a separate terminal, start the server with reverse connect to the client:
   ```bash
   dotnet ConsoleReferenceServer.dll --rc=opc.tcp://localhost:65300 -a
   ```

The server will establish a reverse connection to the client endpoint, and the client will use this connection to communicate with the server.

### Additional Options

- `-a` or `--autoaccept`: Auto accept untrusted certificates (for testing only)
- `-c` or `--console`: Log to console
- `-l` or `--log`: Log app output
- `-t` or `--timeout`: Timeout in seconds to exit application

For the complete list of options, use `--help`.
