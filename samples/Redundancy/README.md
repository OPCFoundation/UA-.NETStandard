<!--
SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
SPDX-License-Identifier: MIT
-->

# Redundancy samples

- [RedundantServer](RedundantServer/README.md): standalone UA server, active/active address-space gossip, and active/passive Raft.
- [RedundantClient](RedundantClient/README.md): an independent managed session or coordinated client replicas.
- [RedundantPubSub](RedundantPubSub/README.md): isolated UDP demonstration and deployment-specific publisher/subscriber clusters.

## Cluster transport

Raft uses mutual TLS with static membership. Each replica supplies the following settings using the `HA_` prefix
for server and PubSub, or `CLIENT_` for coordinated clients:

| Suffix | Setting |
|---|---|
| `RAFT_ID` | Unique, nonzero local voter ID. |
| `RAFT_BIND` | Numeric local `IP:port`; no `tcp://` prefix. A wildcard is allowed only for a bind address. |
| `RAFT_PEERS` | Comma-separated `voterID\|IP:port\|SHA256` entries for all other voters. Use at least three total voters and an odd count. |
| `TLS_STORE_PATH` | OPC UA certificate store containing the local certificate and private key. |
| `TLS_CERTIFICATE_THUMBPRINT` | Certificate store lookup thumbprint for the local identity, not a peer authentication pin. |
| `TLS_PEER_STORE_PATH` | OPC UA certificate store containing the expected public leaf certificates. |
| `TLS_STORE_TYPE`, `TLS_PEER_STORE_TYPE` | Optional store types; both default to `Directory`. An OS `X509Store` can hold the private key. |

Use distinct TLS certificates suitable for both client and server authentication. `SHA256` is the 64-hex-digit
SHA-256 digest of the DER leaf certificate. Configure this allowlist out of band. Raft binds each pin to a voter
ID and dial address. TCP gossip authenticates the exact leaf set for that fabric and dials only statically
configured addresses (`GOSSIP_PEERS`, each `IP:port|SHA256`). Neither transport uses DNS to add members.
Use separate credentials from the OPC UA application PKI and between unrelated clusters.

## Shared sensitive state

The stock binaries deliberately do not ship a freshness authority. External active/passive server state,
coordinated client sessions and external PubSub state require one, even when Raft is bound to loopback.
An encryption key alone does not provide freshness or prevent replay.

1. Implement `SampleFreshness.ConfigureDeployment` in a `RedundancySample` partial class in the relevant app
   project. Its `authorityFactory` must resolve a durable, linearizable `ISharedKeyValueStore` under security
   controls independent of the payload store. A payload-store writer must not be able to modify authority
   state. Do not point it at the Raft payload cluster, an in-memory store or the same credentials under
   a different key prefix. The sample registers the view with `AddSharedRecordFreshness`.
2. Set `DEPLOYMENT_ID` and `FRESHNESS_EPOCH` (nonempty GUIDs) consistently across that set. Set
   `RECORD_KEY_FILE` to a protected mount containing 32 raw random bytes, or supply an `ISecretRegistry`
   from the same hook and configure `RECORD_SECRET_NAME`, `RECORD_SECRET_TYPE` and optionally
   `RECORD_SECRET_SCOPE`. Use the same `HA_` or `CLIENT_` prefix throughout. Do not pass keys as CLI
   arguments, commit them, or copy them into container images.
3. Use a separate administrative invocation with `HA_ENROLL_ONLY=true` (or `CLIENT_ENROLL_ONLY=true`).
   It calls `FreshnessProtectedKeyValueStore.EnrollAsync` and exits without serving the application.
   Remove that setting from runtime replicas: they verify the existing epoch before serving traffic
   and never silently enroll it.

These are sample payload clusters with volatile Raft logs. Losing every payload replica can make records
unavailable even if the authority survives. The samples are not a production recovery or rotation
procedure. The Compose files intentionally do not claim readiness for these external topologies.

Local validation of TLS requires RaftCs.Transport 1.1.7 and the matching staged dependencies. A Docker
build also needs access to those package versions; the checked-in definitions do not publish or seed them.
