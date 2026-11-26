<!--
SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
SPDX-License-Identifier: MIT
-->

# RedundantServer

Run from the repository root:

```powershell
dotnet run --project samples/Redundancy/RedundantServer
```

By default this is a standalone server at `opc.tcp://localhost:62543/RedundantServer`. It offers secured
UA endpoints only, rejects untrusted certificates, and gives anonymous sessions read-only access. Exchange
the application certificates in each application's trusted store before connecting. Use `HA_PKI_ROOT` and
`HA_SUBJECT_NAME` to select the server's provisioned application PKI. The default Compose deployment is
also standalone; it publishes only the UA port on host loopback:

```powershell
docker compose -f samples/Redundancy/RedundantServer/docker-compose.yml up --build
```

## Active/passive

`HA_MODE=ap` and `HA_CONSISTENCY=strong` use Raft for the shared store. For a local
exercise set `HA_LOCAL_DEMO=true` and do not supply Raft peers, a bind address, or a redundant peer list.
This starts one in-process Raft member with native election and opens no Raft network socket. It does not demonstrate
failover. For an external quorum set a stable `HA_NODE_ID` and follow the
[cluster transport and freshness setup](../README.md). External replicas use their configured Raft member
IDs for the address-space publisher lease. The independent freshness authority must implement
`IDeadlineGuardedSharedKeyValueStore` so it checks the lease and its expiry in the same commit as the mutation.
Plain Raft guarded writes do not implement this contract. `HA_FAST_RECONNECT` requires this external
topology and defaults to off. `HA_HISTORIAN` is unsupported until its stores and writer fencing are wired
to the independent freshness boundary.

## Active/active

`HA_MODE=aa` and `HA_CONSISTENCY=eventual` replicate the sample address space, not session secrets.
Set a stable `HA_NODE_ID`. Configure `HA_GOSSIP_BIND` (numeric IP), `HA_GOSSIP_PORT` and
`HA_GOSSIP_PEERS` (static `IP:port|SHA256`
entries), and provision the [cluster TLS stores](../README.md#cluster-transport). Configure static UA
discovery peers separately as `HA_REDUNDANT_PEERS=applicationUri|applicationName|discoveryUrl,...`.
Strong active/active, active/active session mirroring, and dynamic DNS membership are unsupported.

For an explicitly isolated demo only, set `HA_INSECURE=true`. The server binds to numeric loopback
(`localhost` is translated to `127.0.0.1`), accepts untrusted UA certificates, enables `SecurityPolicy.None`,
and allows anonymous writes. In that mode active/active gossip accepts only loopback addresses, with
`HA_GOSSIP_PEERS=IP:port,...` and distinct ports for local replica processes. There is no insecure network
Raft mode. The former multi-container scale, transparent proxy and load-direction Compose presets are
retired; they did not provision the trust and shared-state boundaries these deployments need.
