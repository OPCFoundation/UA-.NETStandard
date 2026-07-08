# RedundantPubSub

`RedundantPubSub` is a runnable OPC UA PubSub high-availability sample that demonstrates an active/standby publisher set, an active/standby subscriber set, and subscriber-side SequenceNumber continuity across publisher failover.

The sample uses the existing PubSub fluent + DI host surface (`services.AddOpcUa().AddPubSub(...)`) with UDP/UADP transport and opts publisher replicas into `services.AddPubSubRedundancy(...)`. In `HA_MODE=hot`, the shared writer checkpoint store lets the promoted publisher resume from the previous writer SequenceNumber plus the stack safety margin, so the subscriber sees a forward jump but not a reset. In `HA_MODE=cold`, the promoted publisher starts from its local sequence state and the subscriber logs the reset.

## Roles

`ROLE=publisher` or `--role publisher` starts one publisher replica. Multiple replicas form one active/standby set when they share the same Raft configuration. Important environment variables are `HA_MODE=hot|warm|cold`, `HA_ELECTION=leader-election|lease-store`, `OWNER_ID`, `HA_RAFT_ID`, `HA_RAFT_MEMBERS`, `HA_RAFT_BIND`, `HA_RAFT_PEERS`, `HA_RECORD_KEY`, `HA_INSECURE`, `PUBSUB_ENDPOINT`, and `PUBLISH_INTERVAL_MS`.

`ROLE=subscriber` or `--role subscriber` starts a UDP/UADP subscriber plus a raw UADP sequence monitor. The regular subscriber sink logs decoded fields, and the monitor logs SequenceNumber continuity lines. When the subscriber is given a distributed election (the same `HA_ELECTION` + `HA_RAFT_*` variables as a publisher, i.e. `HA_RAFT_MEMBERS` greater than one or a non-empty `HA_RAFT_PEERS`), it joins an active/standby subscriber set: the activation coordinator governs its ReaderGroup so only the elected-active replica dispatches received data sets to the sink, while standby replicas stay paused until promoted. Every replica still receives the multicast stream, so a promoted standby resumes dispatching seamlessly.

`ROLE=demo` or `--role demo` starts two in-process publishers and one subscriber over the same UDP endpoint, backed by a shared in-memory store and a manual leader election. This is the simplest local demonstration and avoids any external broker or multi-node store setup.

## Run locally

From the repository root:

```powershell
dotnet run --project Applications\RedundantPubSub\RedundantPubSub.csproj -- --role demo --ha-mode hot
```

The demo runs publisher-a, then prints `FAILOVER: stopping publisher-a; publisher-b is promoted.` and promotes publisher-b. In hot mode the subscriber should log a line like:

```text
HA OK: sequence continued 5 -> 10 across failover (gap, no reset).
```

Run the same demo with cold mode to make the reset visible:

```powershell
dotnet run --project Applications\RedundantPubSub\RedundantPubSub.csproj -- --role demo --ha-mode cold
```

The subscriber should log:

```text
DATA LOSS: sequence reset 5 -> 1 (subscriber must reset de-duplication).
```

## Run with Docker Compose

From the repository root, the default `.env` selects the `strong` profile: three named publisher replicas with Raft leader election and a Raft-backed shared store, plus three subscriber replicas that form their own Raft-backed active/standby reader set.

```powershell
docker compose -f Applications/RedundantPubSub/docker-compose.yml up --build
```

Trigger publisher failover by stopping the current active publisher container. The publisher logs include `Publisher <owner>: <component> -> Active` or `Standby`; stop the active one from another terminal:

```powershell
docker compose -f Applications/RedundantPubSub/docker-compose.yml stop publisher-a
```

If another publisher was active, stop that publisher instead. The subscriber log demonstrates the behavior: `HA OK: sequence continued <n> -> <m> across failover (gap, no reset)` in hot mode, or `DATA LOSS: sequence reset <n> -> <k> (subscriber must reset de-duplication)` in cold mode.

Trigger subscriber failover the same way. The subscriber logs include `Subscriber <owner>: <component> -> Active` or `Standby`, and only the active subscriber logs `DataSet with <n> field(s) received`. Stop the active subscriber:

```powershell
docker compose -f Applications/RedundantPubSub/docker-compose.yml stop subscriber-a
```

A standby subscriber is promoted to `Active` and resumes dispatching received data sets without losing the multicast stream it already receives.

To run the single-container in-process demo through compose:

```powershell
$env:COMPOSE_PROFILES='demo'
docker compose -f Applications/RedundantPubSub/docker-compose.yml up --build
```

## Notes and simplifications

The multi-container profile uses UDP multicast (`opc.udp://239.0.0.1:4840`) on the compose bridge network. This is brokerless and mirrors the existing `ConsoleReferencePubSubClient` UDP sample, but multicast support depends on the Docker host and network driver. If multicast is unavailable on a host, use the local `ROLE=demo` workflow to demonstrate the same failover and SequenceNumber behavior in one process.

The compose file focuses on the working strong-consistency multi-container topology. The in-process demo is the documented simple fallback for environments where a scalable eventual shared store is not available. `HA_INSECURE=true` uses a well-known non-secret record-protection key for an isolated demo only; set the same base64 32-byte `HA_RECORD_KEY` on all publisher replicas for production-like protected shared records.
