<!--
SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
SPDX-License-Identifier: MIT
-->

# RedundantClient

Exchange the client and server application certificates through the OPC UA trusted peer stores. To
use a deployment-specific PKI, set `CLIENT_CONFIG_PATH` to an application configuration file.
Set `CLIENT_SERVER_URI` to the initial member's application URI from trusted
deployment configuration before enabling failover. For a Transparent set,
use its shared application URI. Do not copy the identity from a discovery response.

```powershell
dotnet run --project samples/Redundancy/RedundantClient -- `
    --server opc.tcp://localhost:62543/RedundantServer --duration 00:01:00
```

`CLIENT_MODE=independent` opens no Raft or gossip socket. With `CLIENT_SERVER_URI`,
it uses authenticated server redundancy; without it, it reconnects to the same server only.
`--suite` enables the browse/read/subscribe workload. `--identity`
and `--history` need an authenticated independent client and an appropriate redundant server deployment.
The history workload also needs a user authorized to write history; adapt the sample's anonymous identity
for that scenario.

For coordinated clients, use `CLIENT_MODE=strong` or `eventual`, set a stable `CLIENT_NODE_ID`,
and follow the [cluster transport and freshness setup](../README.md). Both modes use pinned mutual-TLS
Raft for election. Eventual also requires `CLIENT_GOSSIP_BIND=IP:port` and pinned TCP gossip
`CLIENT_GOSSIP_PEERS=IP:port|SHA256,...`. Its protected payload may be temporarily unavailable while
gossip catches up; it does not accept a stale session record in its place.

`--nosecurity` and `--autoaccept` require explicit `HA_INSECURE=true` and an independent loopback client.
These flags disable cross-server failover, not reconnect to the same loopback server. The Compose demo
shares the server's network namespace and publishes no ports; it exercises connectivity, not cluster
failover:

```powershell
$env:HA_INSECURE = 'true'
docker compose -f samples/Redundancy/RedundantClient/docker-compose.yml --profile isolated-demo up --build
```
