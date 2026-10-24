# Brokerless PubSub over a secure channel

This finite .NET 10 sample carries ordinary OPC UA Part 14 NetworkMessages over
an authenticated, encrypted UASC connection. It needs no MQTT broker, UDP
bridge, native QUIC library, external OPC UA server, or permissive certificate
callback.

The sample uses the [Data Channels CONTROL/FLO protocol](../../../docs/DataChannels.md)
and carries complete UADP NetworkMessages as DATA payloads. JSON PubSub is not
supported by this mapping. Queue expiry stays disabled;
the adapter refuses it without a Publisher dependency/recovery policy.

## Run

From the repository root, using the SDK selected by `global.json`:

```powershell
dotnet run --project samples\PubSub\ConsoleSecureChannelPubSub --property:CustomTestTarget=net10.0
```

The default executable provisions two application identities through
`ApplicationInstance` and `CertificateManager`, explicitly exchanges **only
their public certificates**, and starts a standard server/publisher and a
client/subscriber on real loopback TCP sockets. The private-key password is
random and process-local, backed by the existing secret store. Owned demo PKI
is deleted during cleanup. The default flow uses
`/flow/v1/dedicated`, confidential protection, and the exact
`DataChannelProfiles.DedicatedUasc` profile. It creates **no Session**, installs
**no DataChannel address-space model**, and sends **no ordinary Services**.

The production transport performs HEL/ACK/OPN and protected context/flow
establishment. A flow is usable only after the library completes READY; a grant
response is not readiness.

Useful variants:

```powershell
# Real Read and monitored Publish traffic on the SAME authenticated connection.
dotnet run --project samples\PubSub\ConsoleSecureChannelPubSub --property:CustomTestTarget=net10.0 -- --shared

# Optional generated manager model and X.509 Session-authorized grants.
dotnet run --project samples\PubSub\ConsoleSecureChannelPubSub --property:CustomTestTarget=net10.0 -- --authorization session

# Reverse the application roles, not the transport's connect direction.
dotnet run --project samples\PubSub\ConsoleSecureChannelPubSub --property:CustomTestTarget=net10.0 -- --reverse

# Combine all selections and retain non-secret validation evidence.
dotnet run --project samples\PubSub\ConsoleSecureChannelPubSub --property:CustomTestTarget=net10.0 -- --shared --authorization session --reverse --count 12 --result .git\testagent\pubsub-result.json
```

Every run has a finite `--count` (default 8, minimum 4) and `--timeout` (default
45 seconds). Cancellation, invalid options, mismatched decoded values, failed
authorization, and failed teardown return a nonzero exit code. There is no
alternate-transport fallback.

## What it demonstrates

- Two ordinary PublishedDataSets, one WriterGroup (`7`), two DataSetWriters
  (`101`, `202`), and a stable UInt16 PublisherId (`42`). The fields are Int32
  `Counter`, Boolean `Toggle`, and Double `Temperature`, with metadata version
  `1.0`. The two readers receive distinct, changing values. Unmatched
  PublisherId and DataSetWriterId readers receive nothing.
- UADP uses the existing standard encoder, metadata registry, writers, readers
  and sinks. A complete encoded NetworkMessage is a complete flow payload.
  There is no new NetworkMessage wrapper, flow-ID substitution, custom encoder,
  or decode/re-encode bridge.
- The existing metadata publisher's announcements are retained, so the total
  NetworkMessage count can exceed `--count`. Finite progress observes the
  standard WriterGroup's completed data publishing cycles, not raw packet
  counts. Exact byte comparison includes metadata announcements too.
- Half the real messages cross revision 1. The opener then reduces
  `MaxMessageSize` from 32768 to 16384 and `MaxReceiveBytes` from 262144 to
  131072 through the library transaction. The remaining messages cross
  committed revision 2 on the same flow. Both peers perform orderly END and
  cleanup.
- By default, the **server publishes while the client initiates the transport
  and opens the flow** (`AcceptorToOpener`). With `--reverse`, the client
  publishes (`OpenerToAcceptor`). Publisher, transport initiator and flow opener
  are separate roles. This is not transport reverse-connect.
- `--shared` constructs the actual Session on
  `UascDataChannelConnection.Services`, not a second service socket. While the
  PubSub flow is live, it reads server time and receives changing monitored
  notifications through ordinary Publish. The summary and evidence expose
  actual SecureChannel/context identifiers, same-transport identity, and
  client/server service counters. Client counters cover the explicit probe;
  server Session counters also include initialization Reads.
- `--authorization session` uses a certificate-authenticated ordinary Session,
  the generated manager proxy and normal Call service to acquire a fresh,
  context/peer/challenge-bound grant. The local Session authorizer and server
  source policy both validate admission. The Session route also stages its
  revision through the generated proxy. Dedicated mode uses a separate
  ordinary `/control` endpoint; Services are never sent to its flow endpoint.
  The optional model is installed only for this authorization mode.

Both peers independently allow-list the exact resource, protocol, direction,
protection, limits, expected application URI and peer public key. The server
additionally restricts Session authorization to its configured X.509 caller;
there is no anonymous grant bypass. UASC protects confidentiality, so this
sample does not require an SKS or introduce extra Part 14 message-security keys.

Certificate wrappers supplied to `TransportChannelSettings` are independent
`AddRef()` handles: the transport owns them, not the manager's borrowed handles.
Failed connection attempts release any untransferred handles. Shared teardown
finishes the flows, checks `Session.CloseAsync(..., closeChannel: false)`, closes
the owning connection, then disposes the Session. Disposal cannot turn a failed
close into a successful sample result.

## Separate processes

Build once. Choose a fresh PKI directory and readiness filename. The same
private-key password must be supplied privately to setup and both processes;
the sample resolves it through the secret-store-backed certificate password
provider. It is never a command-line argument, source constant, or report field.

```powershell
dotnet build samples\PubSub\ConsoleSecureChannelPubSub --property:CustomTestTarget=net10.0
$app = "samples\PubSub\ConsoleSecureChannelPubSub\bin\Debug\net10.0\ConsoleSecureChannelPubSub.dll"

# Example provisioning input; use an appropriately protected secret source in deployment.
$env:OPCUA_SAMPLE_KEY_PASSWORD = [Convert]::ToBase64String(
    [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
dotnet $app setup --pki .git\testagent\pubsub-pki
```

In a server terminal with that same environment secret:

```powershell
$app = "samples\PubSub\ConsoleSecureChannelPubSub\bin\Debug\net10.0\ConsoleSecureChannelPubSub.dll"
dotnet $app server --pki .git\testagent\pubsub-pki --ready .git\testagent\pubsub-ready.json --count 8 --result .git\testagent\pubsub-server.json
```

In a client terminal with that same environment secret:

```powershell
$app = "samples\PubSub\ConsoleSecureChannelPubSub\bin\Debug\net10.0\ConsoleSecureChannelPubSub.dll"
dotnet $app client --pki .git\testagent\pubsub-pki --ready .git\testagent\pubsub-ready.json --count 8 --result .git\testagent\pubsub-client.json
```

The client waits for the atomic readiness file written only after the real
listener starts. An automatically selected loopback port is the default;
`server --port 48420` selects a fixed data port. Both processes must agree on
`--shared`, `--encoding`, `--authorization`, `--reverse`, and `--count`.
Use a fresh readiness filename on the next run. Setup refuses to overwrite an
existing identity/trust directory. Process-mode PKI is retained for explicit
reuse; delete only that owned directory when finished and clear the environment
secret. Missing state or an incorrect password is an actionable error, not a
reason to create a new identity or accept an arbitrary certificate.

## Real-process tests and byte evidence

```powershell
dotnet test tests\Opc.Ua.PubSub.Samples.Tests --framework net10.0 --property:CustomTestTarget=net10.0
```

The tests launch the compiled sample in both single- and two-process modes,
covering both codecs, carriers, authorization routes and application roles.
They compare **every byte of every actual successful transport send** with
the corresponding complete receive, independently of decoded-value assertions.
The diagnostic decorator forwards the real transport's encoding/limit
capabilities and yields its original receive frames without changing lease
ownership. It does not inspect Core records or implement READY/credit.
Reports also retain per-message revisions, decoded changing values and service
counters. Setup, invalid-option and finite-deadline cases exercise owned PKI
cleanup and real listener release.

This sample is deliberately UASC-only. It makes no claim about native QUIC,
WebTransport or unresolved transport reverse-connect combinations.
