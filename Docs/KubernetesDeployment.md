# Kubernetes Deployment for High Availability

This guide shows how to run the OPC UA server as a high-availability replicaset on Kubernetes using the distributed building blocks described in [HighAvailability.md](HighAvailability.md) and secured per [HighAvailabilitySecurity.md](HighAvailabilitySecurity.md). The worked sample is `Applications/HighAvailabilityServer`.

There are two redundancy shapes (see [HighAvailability.md](HighAvailability.md)):

- **Non-transparent** — each replica is its own OPC UA endpoint; clients read `ServiceLevel` / `RedundantServerArray` and fail over to the highest-service-level replica. Needs no shared session state.
- **Transparent** — one virtual endpoint (a `Service` / load balancer) fronts the replicas; failover is hidden and relies on the shared session store plus subscription transfer.

## Prerequisites

- A shared store reachable by all pods. The in-memory store is single-process only; a multi-pod deployment needs a network backend (e.g. Redis — see the deferred `Opc.Ua.Server.Distributed.Redis` package). The store conduit must be mutual-TLS + authenticated and **fail closed** in production (IEC 62443 zone/conduit boundary).
- A **shared record-protection master key (KEK)** provisioned to every replica from a Kubernetes `Secret` (ideally via the [Secrets Store CSI driver](https://secrets-store-csi-driver.sigs.k8s.io/) backed by a KMS) — never a baked-in constant. All replicas must use the *same* key so any replica can read shared records; rotate with `KeyRingRecordProtector`.
- A **shared `ApplicationInstanceCertificate` and private key** across replicas when using transparent redundancy or session fast-reconnect (REQ-UA-14), provisioned through the certificate / secret store.

## StatefulSet

A `StatefulSet` gives each replica a stable ordinal identity (`$(POD_NAME)`), which is convenient for the per-replica node id and peer discovery.

```yaml
apiVersion: apps/v1
kind: StatefulSet
metadata:
  name: opcua-ha
spec:
  serviceName: opcua-ha            # headless service below
  replicas: 3
  selector:
    matchLabels: { app: opcua-ha }
  template:
    metadata:
      labels: { app: opcua-ha }
    spec:
      containers:
        - name: server
          image: registry.example.com/opcua-ha:latest
          ports:
            - { name: opcua, containerPort: 62543 }
          env:
            - name: POD_NAME
              valueFrom: { fieldRef: { fieldPath: metadata.name } }
            - name: HA_NODE_ID
              value: "$(POD_NAME)"
            # Peers behind the headless service (non-transparent redundancy).
            - name: peerServerUris
              value: "opc.tcp://opcua-ha-0.opcua-ha:62543/HighAvailabilityServer,opc.tcp://opcua-ha-1.opcua-ha:62543/HighAvailabilityServer,opc.tcp://opcua-ha-2.opcua-ha:62543/HighAvailabilityServer"
            - name: HA_FAST_RECONNECT
              value: "false"        # opt-in; the safe default is re-auth on failover
            # Shared record-protection key (base64, >= 32 bytes) from a Secret.
            - name: HA_RECORD_KEY
              valueFrom:
                secretKeyRef: { name: opcua-ha-kek, key: record-key }
          readinessProbe:
            tcpSocket: { port: opcua }
            initialDelaySeconds: 5
            periodSeconds: 10
```

The sample reads `HA_NODE_ID`, `peerServerUris`, `HA_RECORD_KEY`, and `HA_FAST_RECONNECT` (see `Applications/HighAvailabilityServer/Program.cs`).

## Services

**Headless service** (stable per-pod DNS for peer discovery, non-transparent redundancy):

```yaml
apiVersion: v1
kind: Service
metadata:
  name: opcua-ha
spec:
  clusterIP: None
  selector: { app: opcua-ha }
  ports:
    - { name: opcua, port: 62543 }
```

**Transparent redundancy** additionally fronts the replicas with a single regular `Service` (or an external load balancer); clients connect to that one endpoint and never see the individual replicas. Failover then depends on the shared session store (`UseDistributedSessions`) and subscription transfer ([TransferSubscription.md](TransferSubscription.md)).

## Leader election and service level

Elect a single writer with `SharedStoreLeaseElection` over the shared store (compare-and-swap lease; `UseLeaderElection = true`), or with a Kubernetes `Lease` (`coordination.k8s.io`). The leader writes; standbys hydrate and apply. The elected leader reports a high `Server.ServiceLevel` and standbys a low one, so the client redundancy handler selects the leader.

To make readiness reflect leadership (so a `Service` routes preferentially to the active replica), expose a leadership/health endpoint and tie the readiness probe to it, or use a separate probe that returns ready only above a `ServiceLevel` threshold. (`tcpSocket` readiness, as above, is the simplest baseline.)

## Secrets — KEK and application certificate

Provision the record-protection key as a `Secret` (prefer the CSI Secrets Store driver + a KMS so the key never lives in etcd in clear):

```yaml
apiVersion: v1
kind: Secret
metadata:
  name: opcua-ha-kek
type: Opaque
data:
  record-key: <base64-of-32-or-more-random-bytes>
```

For transparent redundancy / session fast-reconnect, mount the **same** `ApplicationInstanceCertificate` + private key into every replica (a `Secret` mounted into the PKI `own` store), so a client signature made against one replica validates on another (REQ-UA-14).

## Security checklist

- The shared store transport is mutual-TLS + authenticated and fails closed if unavailable (do not run Redis in the clear). Use a least-privilege, per-replica credential.
- The record-protection KEK is provisioned from a Secret/KMS, identical across replicas, rotated with `KeyRingRecordProtector`; never a baked-in constant.
- Session fast-reconnect (`HA_FAST_RECONNECT=true`) is opt-in; the standby still performs the full `ActivateSession` signature check and the server nonce is single-use across the replica set. The default is re-authentication on failover.
- Apply TTL / eviction and keyspace caps on the shared store (`session/`, `nonce/`) so a faulty or hostile replica cannot exhaust it.

See [HighAvailabilitySecurity.md](HighAvailabilitySecurity.md) for the full threat model and the operator responsibilities.
