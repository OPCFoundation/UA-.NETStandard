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
| `--suite` | off | Run a browse/read/subscribe workload against the redundant `ISession`. |

The sample connects, logs the server's reported `RedundancySupport` (or notes that the server is not redundant), subscribes to `Server.ServerStatus.CurrentTime`, and logs the values together with any transparent connection-state changes (reconnect or failover). To observe failover, lower the active server's service level (for example with the `RedundantServer` sample's manual failover support) or stop the active server; the managed session reconnects to a healthy peer on its own.

See [HighAvailability.md](../../Docs/HighAvailability.md) for the redundancy design and the [RedundantServer](../RedundantServer/README.md) sample for the server side.

## Run with docker compose (scale the client and server independently, see failover & data loss)

`docker-compose.yml` in this folder brings up a redundant server set and one or more managed clients where **both sides scale independently**, and **both log their failover / HA behavior — including data loss**. Run it from the repository root:

```bash
# Default: 2 servers + 1 client (failover demoable out of the box).
docker compose -f Applications/RedundantClient/docker-compose.yml up --build

# Scale each side independently (overrides the deploy.replicas defaults):
docker compose -f Applications/RedundantClient/docker-compose.yml up --build --scale server=3 --scale client=2
docker compose -f Applications/RedundantClient/docker-compose.yml up --scale client=5   # clients only
```

To trigger a failover, stop the replica a client is on — for example scale the servers down (`… up -d --scale server=1`) or `docker kill` one server container. Then watch the **client** log, which turns the transparent failover and its data impact into explicit lines:

| Client log line | Meaning |
| --- | --- |
| `Connection state: Connected -> Reconnecting` / `Failover` | The session lost its server and is selecting a healthy replica. |
| `CONNECTED: session (re)connected to …` | The session re-established a channel (transparent reconnect/failover). |
| `FAILOVER: now served by replica '<id>' (was '<other>')` | The `HighAvailability.ActiveReplica` value changed — a different server is now serving the session. |
| `DATA LOSS: CurrentTime jumped 6.0s (5 update(s) missed during failover)` | Gap in `ServerStatus.CurrentTime`: samples were missed while the subscription was re-established on the new server. |
| `DATA LOSS: Counter … across failover (state did not carry over)` | The replicated `Counter` regressed or diverged — the value was **not** preserved (see below). |
| `HA OK: Counter continued <n> -> <n+k> across failover (no data loss)` | The `Counter` value **was** preserved across the failover. |

The **server** log shows the other side of the same events: `HA: replica <id> became ACTIVE writer` / `… became STANDBY` on role changes, and a periodic `HA: replica <id> ACTIVE, Counter=<n>` heartbeat so you can see which replica produces which values.

Whether the `Counter` shows data loss or continuity depends on the topology:

- **`docker-compose.yml` (active/active, eventual consistency).** Every replica writes its own independent `Counter`, so on failover the value does not carry over and the client logs **data loss** (`DATA LOSS: Counter jumped …`). This is the default and the scalable topology.
- **`Strong/docker-compose.yml` (active/passive, strong Raft consistency).** The `Counter` rides a linearizable Raft store, so a promoted standby continues it and the client logs **no data loss** (`HA OK: Counter continued …`). Strong consistency needs a fixed odd quorum (3 nodes), so this topology is **not** `--scale`-able:

```bash
docker compose -f Applications/RedundantClient/Strong/docker-compose.yml up --build
# then: docker compose -f Applications/RedundantClient/Strong/docker-compose.yml stop server-a
```

## Client replica set (high availability)

This sample is **one client per process**: run the client image in multiple containers so each replica is its own process. There is no in-process replica set — a single-process demo cannot coordinate a real deployment. Two process-per-replica models are supported:

- **Independent managed clients** (each fails over on its own): every container builds a `ManagedSession` with `WithServerRedundancy()` and reconnects to a healthy peer independently. Scale the client image — use this folder's [`docker-compose.yml`](docker-compose.yml) (`--scale client=N`), or the `clients` profile in [`RedundantServer/Scale/docker-compose.yml`](../RedundantServer/Scale/docker-compose.yml).
- **Coordinated single-active replica set** (exactly one active client, the rest standby): each container registers `AddRedundantClientSession(...)` over a CAS-capable `AddRaftClientSharedStore` (a fixed Raft quorum, like the server's active/passive) or Kubernetes Lease election, so the processes elect one leader that holds the session and the followers take over on leader loss. Each replica exposes a transparent `RedundantClientSession` (`ISession`). See [HighAvailability.md](../../Docs/HighAvailability.md).

