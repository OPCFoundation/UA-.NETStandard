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

## Run with docker compose (one file, the full HA matrix, env-driven)

A single [`docker-compose.yml`](docker-compose.yml) in this folder runs the whole matrix — a redundant server set and one or more clients — where **server consistency and client mode are chosen independently by an environment variable**, and **both sides log their failover / HA behavior, including data loss**. The adjacent [`.env`](.env) sets the default cell (eventual servers + independent clients), so a bare `up` needs no flags. docker compose auto-loads that `.env` even when you run from the repository root.

Selection is by Docker **compose profiles** (the `COMPOSE_PROFILES` environment variable) — pick one server profile and one client profile:

| Profile | Topology | Scales? |
| --- | --- | --- |
| `server-eventual` | Active/active servers, CRDT gossip + DNS discovery (the default) | yes (`--scale server=N`) |
| `server-strong` | Fixed 3-node active/passive Raft servers — no data loss | no (fixed odd quorum) |
| `client-independent` | N managed clients, each fails over on its own (the default) | yes (`--scale client=N`) |
| `client-coordinated` | Fixed 3-node client replica set — exactly one active client | no (fixed odd quorum) |

For `client-coordinated`, `CLIENT_CONSISTENCY` (`eventual` \| `strong`, default `eventual`) selects the shared-store flavor used to hand the session over to the promoted client. Only the eventual server and the independent clients are `--scale`-able; the strong server and the coordinated client are Raft quorums that need stable identities, so they are fixed 3-node named replicas. `SERVER_REPLICAS` (default 3) and `CLIENT_REPLICAS` (default 2) set the scalable counts.

Run from the repository root:

```bash
# DEFAULT: 3 eventual servers + 2 independent clients (failover + data loss demoable out of the box).
docker compose -f Applications/RedundantClient/docker-compose.yml up --build

# Scale the eventual server and the independent clients independently:
docker compose -f Applications/RedundantClient/docker-compose.yml up --build --scale server=5 --scale client=3

# STRONG server (no data loss) x independent clients:
COMPOSE_PROFILES=server-strong,client-independent \
  docker compose -f Applications/RedundantClient/docker-compose.yml up --build

# Any matrix cell, e.g. STRONG server x EVENTUAL coordinated client:
COMPOSE_PROFILES=server-strong,client-coordinated CLIENT_CONSISTENCY=eventual \
  docker compose -f Applications/RedundantClient/docker-compose.yml up --build
```

To trigger a server failover, stop the replica a client is on — for example scale the eventual servers down (`… up -d --scale server=1`) or `docker kill` one server container (for `server-strong`, stop the Raft leader, e.g. `… stop server-a`). Then watch the **client** log, which turns the transparent failover and its data impact into explicit lines:

| Client log line | Meaning |
| --- | --- |
| `Connection state: Connected -> Reconnecting` / `Failover` | The session lost its server and is selecting a healthy replica (independent client). |
| `CONNECTED: session (re)connected to …` | The session re-established a channel (transparent reconnect/failover). |
| `ACTIVE CLIENT: replica '<id>' is now the active client` | This coordinated replica was elected leader and (re)established monitoring. |
| `STANDBY: this replica is no longer the active client` | This coordinated replica lost leadership; a peer client took over. |
| `FAILOVER: now served by replica '<id>' (was '<other>')` | The `HighAvailability.ActiveReplica` value changed — a different server is now serving the session. |
| `DATA LOSS: CurrentTime jumped 6.0s (5 update(s) missed during failover)` | Gap in `ServerStatus.CurrentTime`: samples were missed while the subscription was re-established on the new server (or while a standby client was being promoted). |
| `DATA LOSS: Counter … across failover (state did not carry over)` | The replicated `Counter` regressed or diverged — the value was **not** preserved (see below). |
| `HA OK: Counter continued <n> -> <n+k> across failover (no data loss)` | The `Counter` value **was** preserved across the failover. |

The **server** log shows the other side of the same events: `HA: replica <id> became ACTIVE writer` / `… became STANDBY` on role changes, and a periodic `HA: replica <id> ACTIVE, Counter=<n>` heartbeat so you can see which replica produces which values.

Whether the `Counter` shows data loss or continuity depends on the **server** profile:

- **`server-eventual` (active/active, eventual consistency, the default).** Every replica writes its own independent `Counter`, so on failover the value does not carry over and the client logs **data loss** (`DATA LOSS: Counter jumped …`). This is the scalable topology.
- **`server-strong` (active/passive, strong Raft consistency).** The `Counter` rides a linearizable Raft store, so a promoted standby continues it and the client logs **no data loss** (`HA OK: Counter continued …`). Strong consistency needs a fixed odd quorum (3 nodes), so it is **not** `--scale`-able.

## Client replica set (high availability)

Run the client image in multiple containers so each replica is its own process — a single-process demo cannot coordinate a real deployment. The client mode is chosen with `CLIENT_MODE` (which the compose sets per profile); two process-per-replica models are supported:

- **Independent managed clients** (`CLIENT_MODE=independent`, the compose default, **2, scalable**): every container builds a `ManagedSession` with `WithServerRedundancy()` and reconnects to a healthy peer independently. Scale the client image with `--scale client=N`, or use the `clients` profile in [`RedundantServer/Scale/docker-compose.yml`](../RedundantServer/Scale/docker-compose.yml).
- **Coordinated single-active replica set** (`CLIENT_MODE=eventual` \| `strong`, the `client-coordinated` profile, **fixed odd quorum, default 3**): each container builds a `RedundantClientSession` over a real Raft cluster among the client replicas (`RaftLeaderElection` + a `RaftSharedKeyValueStore`, or a `HybridSharedKeyValueStore` over CRDT gossip for eventual) — the same building blocks the server uses, mirrored on the client. The processes elect one leader that holds the session and share its protected session secrets, so a follower takes over on leader loss and resumes monitoring. Each replica exposes a transparent `RedundantClientSession` (`ISession`). Because the election is Raft-quorum-based in both eventual and strong, a coordinated set needs an **odd ≥ 3 quorum** (a 2-member set is degenerate); the compose ships a fixed 3-node `client-a/b/c` set. A coordinated set mirrors session secrets through a networked store, so it requires a record protector: set `CLIENT_RECORD_KEY` to a shared base64 32-byte key in production, or `CLIENT_INSECURE=true` for an isolated demo (a well-known, non-secret demo key). See [HighAvailability.md](../../Docs/HighAvailability.md).

