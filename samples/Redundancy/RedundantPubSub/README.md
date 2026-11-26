<!--
SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
SPDX-License-Identifier: MIT
-->

# RedundantPubSub

The local demo runs two publishers and one subscriber in the same process, with loopback UDP and no network
Raft. It requires explicit opt-in:

```powershell
dotnet run --project samples/Redundancy/RedundantPubSub -- --role demo --ha-insecure true --ha-mode hot
```

Choose `--ha-mode cold`, `warm` or `hot`. The `SIMULATED:` lines illustrate sequence continuity or reset;
the separate live pipeline runs beside this scripted narrative. Unsigned `publisher` or `subscriber` roles
without a Raft configuration are also limited to explicit loopback demos.

The Compose demo uses an isolated network namespace and publishes no ports:

```powershell
$env:HA_INSECURE = 'true'
docker compose -f samples/Redundancy/RedundantPubSub/docker-compose.yml --profile isolated-demo up --build
```

## External publisher or subscriber sets

Use `ROLE=publisher` or `subscriber`, a stable `OWNER_ID`, and an explicitly provisioned
[Raft and freshness setup](../README.md). Publisher and subscriber sets have independent memberships,
credentials and deployment IDs. External `warm` and `hot` runs require `HA_ELECTION=lease-store`
for activation fencing; native Raft-election-only mode is limited to `cold`.

Configure `PUBSUB_ENDPOINT`, `PUBSUB_SECURITY_GROUP` and `PUBSUB_SKS_URL`, and register the
deployment's authenticated `IPubSubSecurityKeyProvider` from `SampleFreshness.ConfigureDeployment`.
Writer and reader groups require `SignAndEncrypt`. This sample does not bootstrap an SKS or distribute
SecurityGroup keys. No unsigned external profile or hardcoded key is shipped.
