# OPC Foundation UA .NET Standard Reference Server

## Introduction

The console reference server can be configured using several console parameters.
Some of these parameters are explained in more detail below.

To see all available parameters call console reference server with the parameter `-h`.

## Historical Access

The reference server starts with an in-memory Part 11 historian. Its historized
scalar nodes expose raw, modified, at-time, aggregate, annotation, and update
operations together with populated `HistoricalDataConfigurationType`
companions. `CTT/Historical_KeyValuePairs` demonstrates generic
StructuredHistoryData with two `KeyValuePair` entries at one timestamp. The
`CTT` notifier exposes historical event read/write bits, a
`HistoricalEventConfigurationType` companion, and seeded event history.
Writes to the two event-trigger variables report events; live forwarding
completes before the historian snapshots them into its bounded asynchronous
capture queue.

Run the matching client workflow with:

```bash
dotnet ConsoleReferenceClient.dll --historian --autoaccept --nosecurity \
  opc.tcp://localhost:62541/Quickstarts/ReferenceServer
```

See [`docs/HistoricalAccess.md`](../../../docs/HistoricalAccess.md) for the
provider contracts, status semantics, and custom storage examples.

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

## X509 user identity certificates

The reference server validates X509 **user** identity tokens against its trusted-user certificate
store (`TrustedUserCertificates`, by default `%LocalApplicationData%/OPC Foundation/pki/trustedUser`).
An untrusted user certificate is rejected with `BadIdentityTokenRejected` — the `--autoaccept` option
only auto-accepts the application/channel certificate, never user identity certificates.

To let a trusted client (for example the OPC Foundation Compliance Test Tool) authenticate with an
X509 user token, its user certificate must be present in that store. To make provisioning easy, the
server writes every **rejected** X509 user certificate to a dedicated review store,
`pki/rejectedUser` (a sibling of `pki/trustedUser`). After one failing activation you can move the
legitimate user certificate from `pki/rejectedUser/certs` into `pki/trustedUser/certs` (and any
issuing CA into `pki/issuerUser/certs`) and reconnect. Deliberately-untrusted certificates simply stay
out of the trusted store and continue to be rejected.
