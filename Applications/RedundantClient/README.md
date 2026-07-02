# Managed client sample

This console sample shows the recommended way to connect an OPC UA client: build a single `ManagedSession` with `WithServerRedundancy()`. The same code works whether or not the target server is configured for redundancy, because a client is almost never aware of the server topology until it has connected.

- Against a redundant server, the managed session reads `Server.ServerRedundancy` / `Server.ServiceLevel`, discovers the redundant set from the connected server, and fails over transparently. There is no client-side failover-mode selection and no hand-maintained seed list — the peer set comes from the server.
- Against a server that is not configured for redundancy, the same session simply runs as a resilient, automatically reconnecting client (`RedundancySupport=None`).

## Run

```powershell
dotnet run --project Applications\RedundantClient\RedundantClient.csproj -- `
  --server opc.tcp://localhost:62543/RedundantServer --autoaccept --nosecurity --duration 00:05:00
```

| Option | Default | Description |
| --- | --- | --- |
| `--server`, `-s` | `opc.tcp://localhost:62543/RedundantServer` | Discovery URL of any server in the (optionally) redundant set. |
| `--nosecurity` | off | Select endpoints with `MessageSecurityMode.None`. |
| `--autoaccept` | off | Automatically accept untrusted server certificates (sample only). |
| `--duration`, `-d` | `00:02:00` | How long to monitor before exiting; `00:00:00` runs until Ctrl+C. |

The sample connects, logs the server's reported `RedundancySupport` (or notes that the server is not redundant), subscribes to `Server.ServerStatus.CurrentTime`, and logs the values together with any transparent connection-state changes (reconnect or failover). To observe failover, lower the active server's service level (for example with the `RedundantServer` sample's manual failover support) or stop the active server; the managed session reconnects to a healthy peer on its own.

See [HighAvailability.md](../../Docs/HighAvailability.md) for the redundancy design and the [RedundantServer](../RedundantServer/README.md) sample for the server side.

## Client replica set (high availability)

Run an in-process client replica set where exactly one leader holds the session and followers stand by:

```powershell
dotnet run --project Applications\RedundantClient\RedundantClient.csproj -- --server opc.tcp://localhost:62543/RedundantServer --autoaccept --nosecurity --replicas 3
```

Leader election uses a shared `ISharedKeyValueStore` + `SharedStoreLeaseElection` (in-memory here). A multi-process deployment uses a CAS-capable shared store (Redis) or Kubernetes Lease election with the same `ClientReplicaCoordinator`.
