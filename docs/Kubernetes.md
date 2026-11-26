<!--
SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
SPDX-License-Identifier: MIT
-->

# Kubernetes High Availability Deployment

This is the Kubernetes deployment guide for OPC UA high availability. It
covers the base `OPCFoundation.NetStandard.Opc.Ua.Redundancy.Server`
features and the opt-in
`OPCFoundation.NetStandard.Opc.Ua.Redundancy.Kubernetes` extension. The
extension provides Kubernetes Lease leader election, EndpointSlice peer
discovery, and HTTP readiness/liveness driven by OPC UA `ServiceLevel`.

See [HighAvailability.md](HighAvailability.md) for the OPC 10000-4 §6.6 redundancy model. The worked server sample is `samples/Redundancy/RedundantServer`.

The API prefixes distinguish standardized OPC UA model wiring from
deployment extensions:

- `AddServerRedundancy(...)`, `AddServerServiceLevel(...)`, and
  `AddRequestServerStateChange(...)` publish or maintain OPC 10000-4 §6.6
  nodes and methods.
- `UseDistributedAddressSpace(...)`, `UseDistributedSessions(...)`,
  `UseDistributedSubscriptionMirroring(...)`,
  `UseKubernetesLeaderElection(...)`, `UseKubernetesPeerDiscovery(...)`, and
  `UseKubernetesReadiness(...)` register beyond-spec extension services.

`AddServerRedundancy(...)` does not drive `Server.ServiceLevel`. For
Kubernetes readiness, also register a `ServiceLevel` provider, commonly
through `AddServerServiceLevel(...)` or the leader-aware provider used by the
sample.

## Contents

- [Redundancy shapes on Kubernetes](#redundancy-shapes-on-kubernetes)
- [Running with Kubernetes API integration](#running-with-kubernetes-api-integration)
  - [Application setup with "full" K8s integration](#application-setup-with-full-k8s-integration)
- [Running without Kubernetes API integration](#running-without-kubernetes-api-integration)
  - [Simple setup option](#simple-setup-option)
- [RBAC](#rbac)
- [Services](#services)
- [StatefulSet or Deployment](#statefulset-or-deployment)
- [Readiness and ServiceLevel](#readiness-and-servicelevel)
- [Kubernetes Lease leader election](#kubernetes-lease-leader-election)
- [EndpointSlice peer discovery](#endpointslice-peer-discovery)
- [Secrets, certificates, and trust](#secrets-certificates-and-trust)
- [Time synchronization](#time-synchronization)
- [GDS and NTRS registration](#gds-and-ntrs-registration)
- [Security checklist](#security-checklist)

## Redundancy shapes on Kubernetes

Use **non-transparent redundancy** when each pod has its own OPC UA
endpoint. Clients then use `Server.ServerRedundancy`, `FindServers`, and
`ServiceLevel` to fail over.

Use **transparent redundancy** when clients connect to one virtual address,
typically a `Service` or external load balancer. To hide failover, the pods
must share the endpoint URL, application URI, certificate, session state,
and subscription state.

A multi-pod deployment needs a shared store that all replicas can reach for
distributed address-space, session, and subscription state. Use authenticated
and encrypted connections, and provision record-protection keys through a
Kubernetes Secret or a secret store backed by the Container Storage Interface
(CSI). For trust decisions and replicated state that must not roll back,
configure a separate durable freshness authority. Its credentials, membership
and storage must be outside the authority of the payload-store writer. Record
encryption alone does not detect a replay or a removed record. Set limits and
expiration for session and subscription keys so a faulty replica cannot
exhaust the backend.

Choose whether to use the Kubernetes API:

- **With Kubernetes API integration:** use EndpointSlice discovery and
  readiness. The Kubernetes Lease adapter can coordinate workloads that do
  not need to publish guarded address-space writes.
- **Without Kubernetes API integration:** the in-package Raft/CRDT layers
  manage membership and replicated storage.

The following sections describe both options.

## Running with Kubernetes API integration

Use `UseKubernetesPeerDiscovery` and `UseKubernetesReadiness` for
EndpointSlice discovery and `ServiceLevel`-driven readiness. Discovery
requires a `ServiceAccount` with the [RBAC](#rbac) shown below. For
distributed address-space writes, use the freshness store's guarded writer
lease. The authority must implement `IDeadlineGuardedSharedKeyValueStore`
so it can verify ownership and expiry when the write commits.

### Application setup with "full" K8s integration

Register a production payload backend and record protector before adding the
server. The application-specific factories below must use separately scoped
credentials and validate the peers of both backends. Enroll the deployment
as an administrative step with `FreshnessProtectedKeyValueStore.EnrollAsync`
before starting replicas, not when a replica sees an empty store.

```csharp
services.AddSingleton<ISharedKeyValueStore>(CreatePayloadStore);
services.AddSingleton<IRecordProtector>(CreateRecordProtector);
services.AddSharedRecordFreshness(CreateFreshnessAuthority, deploymentId, epoch);

services.AddOpcUa()
    .AddServer(server =>
    {
        // endpoint, certificate, and application configuration
    })
    .AddNodeManager<MyNodeManagerFactory>()
    .UseReplicaNodeIdentity(
        "opcua-ha",
        ["urn:example:opcua:machine"],
        writerAssignedIds: true)
    .UseDistributedAddressSpace(options =>
    {
        options.UseLeaderElection = true;
        options.LeaseDuration = TimeSpan.FromSeconds(30);
        options.RenewInterval = TimeSpan.FromSeconds(10);
        options.NodeId = Environment.GetEnvironmentVariable("HOSTNAME") ?? Environment.MachineName;
        options.LeaseMemberIds = ["opcua-ha-0", "opcua-ha-1", "opcua-ha-2"];
        // This wires the leader-aware IServiceLevelProvider that UseKubernetesReadiness() consumes.
    })
    .UseDistributedSessions(options =>
    {
        options.EnableFastReconnect = true;
    })
    .UseDistributedSubscriptionMirroring()
    .AddServerRedundancy(options =>
    {
        options.Mode = RedundancySupport.Transparent;
        options.CurrentServerId = Environment.GetEnvironmentVariable("HOSTNAME") ?? Environment.MachineName;
        options.TransparentPeerServerIds.Add("opcua-ha-0");
        options.TransparentPeerServerIds.Add("opcua-ha-1");
        options.TransparentPeerServerIds.Add("opcua-ha-2");
    })
    .UseKubernetesPeerDiscovery(options =>
    {
        options.ServiceName = "opcua-ha-headless";
        options.LocalAddress = Environment.GetEnvironmentVariable("POD_IP");
    })
    .UseKubernetesReadiness(options =>
    {
        options.Host = Environment.GetEnvironmentVariable("POD_IP") ?? "localhost";
        options.Port = KubernetesReadinessOptions.DefaultPort; // 4852
        options.ReadinessPath = "/readyz";
        options.LivenessPath = "/livez";
        options.ReadyMinimumServiceLevel = ServiceLevels.HealthyMinimum;
    });
```

`UseDistributedAddressSpace` registers a leader-aware `IServiceLevelProvider`.
Its writer lease and the replicated state use the same enrolled freshness
view so a superseded grant cannot publish new data.

If you combine `UseKubernetesLeaderElection()` and
`UseKubernetesReadiness()` without `UseDistributedAddressSpace()`, register
your own `IServiceLevelProvider`, for example through
`AddServerServiceLevel(...)`, before `UseKubernetesReadiness()`. This keeps
followers from remaining ready.

`UseKubernetesLeaderElection` uses the in-cluster service account token,
namespace, and CA mounted at `/var/run/secrets/kubernetes.io/serviceaccount`.
Do not use a process-local store as a fallback if those files are absent.
If you explicitly enable a fallback for a separate installation, assign it
a protected shared lease backend and a common membership list.

## Running without Kubernetes API integration

Raft and CRDT replication do not need Kubernetes API access. If the pods
do not use EndpointSlice discovery or Kubernetes Lease election, they do
not need the RBAC below and can disable service account token mounting.

The storage choices below do not replace the independently administered
freshness authority. For distributed address-space writes and the hosted
replicated event feed, supply a networked authority that implements
`IDeadlineGuardedSharedKeyValueStore`. The included Raft store can
replicate payloads but cannot check lease expiry in the same commit.

### Simple setup option

- **Strong** consistency (via Raft) — compose `UseKubernetesRaftConsensus`
  with `UseRedundancyConsistency(RedundancyConsistencyMode.Strong)`. The
  consistency builder alone creates private, process-local consensus when no
  network consensus factory is registered. Give each member its own certificate,
  a stable transport address, and a `PersistentVolumeClaim` for its Raft WAL.
- **Eventual** consistency (via CRDT and Raft) — `UseReplicatedAddressSpace`/`UseReplicatedSessions` converge leaderlessly over gossip, so there is no election to coordinate. Point each pod's gossip peer list at the other pods' DNS names (again a headless `Service` over a `StatefulSet`). Exactly-once primitives such as the single-use nonce and the leader lease still ride the in-package Raft keyspace, so "eventual" here is the hybrid CRDT-plus-Raft store rather than pure gossip.

In both cases readiness/liveness can be a plain HTTP probe your app exposes from `Server.ServiceLevel` without `UseKubernetesReadiness`.

## RBAC

Grant only namespace-scoped access to Leases for election and EndpointSlices for peer discovery.

```yaml
apiVersion: v1
kind: Namespace
metadata:
  name: opcua
---
apiVersion: v1
kind: ServiceAccount
metadata:
  name: opcua-ha
  namespace: opcua
---
apiVersion: coordination.k8s.io/v1
kind: Lease
metadata:
  name: opcua-ha-leader
  namespace: opcua
spec: {}
---
apiVersion: rbac.authorization.k8s.io/v1
kind: Role
metadata:
  name: opcua-ha
  namespace: opcua
rules:
  - apiGroups: ["coordination.k8s.io"]
    resources: ["leases"]
    resourceNames: ["opcua-ha-leader"]
    verbs: ["get", "update"]
  - apiGroups: ["discovery.k8s.io"]
    resources: ["endpointslices"]
    verbs: ["list"]
---
apiVersion: rbac.authorization.k8s.io/v1
kind: RoleBinding
metadata:
  name: opcua-ha
  namespace: opcua
subjects:
  - kind: ServiceAccount
    name: opcua-ha
    namespace: opcua
roleRef:
  apiGroup: rbac.authorization.k8s.io
  kind: Role
  name: opcua-ha
```

## Services

Use a headless Service for stable pod DNS and EndpointSlice peer discovery. Use a separate regular Service for a transparent virtual address or for client access to ready pods.

```yaml
apiVersion: v1
kind: Service
metadata:
  name: opcua-ha-headless
  namespace: opcua
spec:
  clusterIP: None
  publishNotReadyAddresses: true
  selector:
    app: opcua-ha
  ports:
    - name: opcua-tcp
      port: 4840
      targetPort: opcua-tcp
---
apiVersion: v1
kind: Service
metadata:
  name: opcua-ha
  namespace: opcua
spec:
  type: LoadBalancer
  selector:
    app: opcua-ha
  ports:
    - name: opcua-tcp
      port: 4840
      targetPort: opcua-tcp
```

For clusters without external load balancers, use `ClusterIP` and place an ingress, gateway, node-level load balancer, or OT/DMZ routing appliance in front. For transparent redundancy, align the OPC UA `ApplicationUri`, endpoint URL, certificate subject alternative names, and trust configuration with the externally visible virtual address.

For Raft, a headless pod address can change on rescheduling. Create one
ClusterIP Service per Raft member and resolve it through the member endpoint
callback. Repeat this template for each StatefulSet ordinal:

```yaml
apiVersion: v1
kind: Service
metadata:
  name: opcua-ha-0-raft
  namespace: opcua
spec:
  publishNotReadyAddresses: true
  selector:
    statefulset.kubernetes.io/pod-name: opcua-ha-0
  ports:
    - name: raft
      port: 5560
      targetPort: raft
```

`publishNotReadyAddresses` prevents a readiness dependency cycle: Raft must
be able to contact its peers before the replicas can report readiness.

## StatefulSet or Deployment

A StatefulSet gives stable ordinal names for per-replica endpoints and audit identity. A Deployment is suitable when clients and certificates only use the virtual Service address and no peer URI depends on pod names.

```yaml
apiVersion: apps/v1
kind: StatefulSet
metadata:
  name: opcua-ha
  namespace: opcua
spec:
  serviceName: opcua-ha-headless
  replicas: 3
  selector:
    matchLabels:
      app: opcua-ha
  template:
    metadata:
      labels:
        app: opcua-ha
    spec:
      serviceAccountName: opcua-ha
      terminationGracePeriodSeconds: 30
      containers:
        - name: server
          image: ghcr.io/example/opcua-ha:latest
          imagePullPolicy: IfNotPresent
          env:
            - name: POD_IP
              valueFrom:
                fieldRef:
                  fieldPath: status.podIP
            - name: POD_NAME
              valueFrom:
                fieldRef:
                  fieldPath: metadata.name
            - name: HA_NODE_ID
              valueFrom:
                fieldRef:
                  fieldPath: metadata.name
            - name: HA_FAST_RECONNECT
              value: "true"
            - name: HA_RECORD_KEY_FILE
              value: /var/run/opcua/record-protection/record-key
          ports:
            - name: opcua-tcp
              containerPort: 4840
            - name: health
              containerPort: 4852
            - name: raft
              containerPort: 5560
          volumeMounts:
            - name: raft-data
              mountPath: /var/lib/opcua/raft
            - name: record-protection
              mountPath: /var/run/opcua/record-protection
              readOnly: true
          readinessProbe:
            httpGet:
              path: /readyz
              port: health
            periodSeconds: 5
            failureThreshold: 2
          livenessProbe:
            httpGet:
              path: /livez
              port: health
            periodSeconds: 10
            failureThreshold: 3
      volumes:
        - name: record-protection
          secret:
            secretName: opcua-ha-kek
            items:
              - key: record-key
                path: record-key
  volumeClaimTemplates:
    - metadata:
        name: raft-data
      spec:
        accessModes: ["ReadWriteOnce"]
        resources:
          requests:
            storage: 1Gi
```

A Deployment with replaceable pods is only suitable when its backends provide
the durable state externally. In that case omit the Raft port, volume mount,
claim template, and `serviceName`.

The StatefulSet shows the pod wiring, not a complete deployment. Configure
the application certificates, peer trust, and separate freshness authority
before starting the replicas; the mounted record key does not configure or
enroll that authority.

### Replica transport NetworkPolicy

The policy below allows traffic between replica pods on the example's Raft
port and two separately configured TCP gossip ports. Configure address-space
gossip on 5561 and session gossip on 5562 when deploying both transports.
Add rules for the OPC UA client endpoint and for kubelet probes according to
your network plugin and cluster routing. Apply an equivalent peer-only rule
to the freshness authority with its own selector and credentials.

```yaml
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata:
  name: opcua-ha-replica-ingress
  namespace: opcua
spec:
  podSelector:
    matchLabels:
      app: opcua-ha
  policyTypes: [Ingress]
  ingress:
    - from:
        - podSelector:
            matchLabels:
              app: opcua-ha
      ports:
        - { protocol: TCP, port: 5560 }
        - { protocol: TCP, port: 5561 }
        - { protocol: TCP, port: 5562 }
```

Do not expose these ports from a client-facing Service. Keep necessary egress
to cluster DNS, the Kubernetes API and each required backend, using rules
appropriate for the cluster. NetworkPolicy limits reachability; it does not
authenticate replicas.

## Readiness and ServiceLevel

`UseKubernetesReadiness` starts a small HTTP listener on port 4852 (`KubernetesReadinessOptions.DefaultPort`) when the server has started. The default deliberately avoids the ports web applications commonly use, such as 8080 or 9090: the probe shares the pod (and, with host networking, the node) with the application and its sidecars, so those defaults can collide with them. Port 4852 is unassigned in the [IANA registry](https://www.iana.org/assignments/service-names-port-numbers/service-names-port-numbers.xhtml); set `Port` if it is taken in your environment. `/livez` returns success while the process can answer the probe. `/readyz` returns HTTP 200 only when `IServiceLevelProvider.GetServiceLevel()` is at least `ReadyMinimumServiceLevel`, which defaults to the Healthy sub-range (`200`). This lets a client-facing Service route only to the current leader or otherwise healthy replicas.

For Warm active/passive deployments, `LeaderServiceLevelProvider` normally makes the leader Healthy and standbys Degraded; readiness therefore routes traffic to the leader. For Hot load-balanced deployments, use the Healthy 200-255 sub-range to reflect load and keep all healthy pods ready.

## Kubernetes Lease leader election

`UseKubernetesLeaderElection` replaces the registered `ILeaderElection` with
`KubernetesLeaseLeaderElection` when running in cluster. The Lease name
defaults to `opcua-server-leader`; make the configured and pre-created names
match. Tune `LeaseDuration` and `RenewInterval` above worst-case scheduling,
API-server, and network jitter.

Kubernetes Lease election can drive `ServiceLevel` through a leader-aware
provider, but it does not guard writes from `UseDistributedAddressSpace`.
Use the protected writer lease described above for that purpose. Set
`terminationGracePeriodSeconds` long enough to withdraw readiness and
release the lease before the pod exits.

Use `UseKubernetesRaftConsensus` with an odd number of authenticated members
for a Raft-backed shared store. The StatefulSet ordinal plus
one becomes the Raft member ID. Bind to the pod IP (`POD_IP`), store the file
WAL on a per-pod volume, and supply pinned member certificates. If pod
addresses can change, supply `ResolvePeerEndPointAsync` that returns a
stable address for each member (for example one Service per replica).
Do not use a Service that can route a peer connection to different members.

The following assumes record protection and the independent freshness
authority have already been registered as shown above:

```csharp
services.AddOpcUa()
    .AddServer(server => { })
    .UseKubernetesRaftConsensus(o =>
    {
        o.HeadlessServiceName = "opcua-ha-headless";
        o.ReplicaCount = 3;                 // static membership; odd for quorum
        o.StoragePath = "/var/lib/opcua/raft"; // PersistentVolume mount
        o.ResolveMemberCertificateAsync = ResolveRaftMemberCertificateAsync;
        o.ResolvePeerEndPointAsync = ResolveStableRaftPeerEndpointAsync;
    })
    .UseRedundancyConsistency(options =>
    {
        options.Mode = RedundancyConsistencyMode.Strong;
        options.UseRaftLeaderElection = false; // Guarded writer lease below.
    })
    .UseReplicaNodeIdentity("opcua-ha", ["urn:example:opcua:machine"], writerAssignedIds: true)
    .UseDistributedAddressSpace(options =>
    {
        options.UseLeaderElection = true;
        options.NodeId = Environment.GetEnvironmentVariable("POD_NAME") ?? Environment.MachineName;
        options.LeaseMemberIds = ["opcua-ha-0", "opcua-ha-1", "opcua-ha-2"];
    })
    .UseDistributedSessions(s => s.EnableFastReconnect = true);
```

See [Consistency modes](HighAvailability.md) for the strong (Raft) vs. eventual (CRDT) trade-offs.

## EndpointSlice peer discovery

`UseKubernetesPeerDiscovery` polls EndpointSlices for the configured headless Service and builds peer URIs from address, port name, URI scheme, and port options. Set `LocalAddress` to the pod IP or DNS name so the local pod is excluded. The discovered ServerUris are bridged into the generic `IPeerDiscovery` seam, so they update the client-facing `RedundantServerSet` (published through `FindServers`) **dynamically at runtime** — no manual feeding into `ServerRedundancyOptions.RedundantPeers` is required, and static configuration remains the fallback until discovery finds peers (see [High Availability → Dynamic peer discovery](HighAvailability.md#dynamic-peer-discovery-beyond-66-opt-in)). EndpointSlice discovery yields OPC UA endpoints, not CRDT gossip endpoints; for an active/active gossip fabric that also scales dynamically, pair it with `UseDnsPeerDiscovery` on the gossip headless service.

## Secrets, certificates, and trust

Provision the shared record-protection key as a Secret or, preferably, through the Secrets Store CSI driver backed by a KMS.

```yaml
apiVersion: v1
kind: Secret
metadata:
  name: opcua-ha-kek
  namespace: opcua
type: Opaque
data:
  record-key: "<base64-of-32-random-bytes>"
```

Kubernetes decodes `data` when it mounts the Secret. The redundancy server
sample reads exactly 32 raw bytes from the file at `HA_RECORD_KEY_FILE`.
Do not use `stringData` with Base64 text: that mounts the text itself rather
than the key bytes. Set file access for the server identity, or use the
sample's secret registry integration with your deployment provider.

For transparent redundancy or session fast reconnect, mount the same `ApplicationInstanceCertificate` and private key into every replica because the client signs an `ActivateSession` challenge against one logical application. For non-transparent redundancy, each server may have its own ApplicationUri and certificate and is managed independently by the certificate manager. Never bake private keys, trust lists, or shared store credentials into images.

The shared transparent-mode private key has replica-set-wide blast radius: compromise of one pod or mounted secret compromises the virtual OPC UA server identity for every replica behind the Service. Store the key in a Kubernetes Secret only when that matches the cluster's threat model; otherwise use a CSI-backed secret store or HSM/KMS integration with strict RBAC, audit, and node access controls.

Rotate the shared transparent-mode certificate/key with an overlap window. Issue a replacement certificate for the same virtual endpoint identity, update client/GDS trust lists to trust the replacement before rollout, update the mounted secret or CSI version, restart or reload pods until every ready replica presents the new certificate, then revoke/remove the old certificate and delete old secret versions. For suspected key compromise, mark readiness unhealthy or drain the Service before any replica with the old key can continue serving clients.

## Time synchronization

OPC 10000-4 requires redundant server sets to be time synchronized. Kubernetes nodes should run NTP or PTP, monitor drift, and keep Lease durations comfortably above expected clock and scheduling jitter. Certificate validity, SecureChannel token validation, audit records, historian timestamps, SourceTimestamp/ServerTimestamp comparisons, and EventId strategies all depend on sane clocks.

## GDS and NTRS registration

For non-transparent redundancy, register each server with GDS and include the `NTRS` ServerCapability so clients and Network Topology and Routing Services can discover the redundant set. `AddServerRedundancy(...)` advertises `NTRS` by default for Cold/Warm/Hot/HotAndMirrored modes. For transparent redundancy, register the virtual application/endpoint that clients use; replicas can still have operational identities for diagnostics, but discovery should return the stable virtual address.

## Security checklist

- Restrict RBAC to the namespace and resources above; add NetworkPolicy so only the OPC UA pods that need it can reach the Kubernetes API and shared store.
- Protect shared-store traffic with mutual TLS or an equivalent authenticated
  channel. If required records cannot be verified against the independent
  authority, the affected replica must not serve that state.
- Use `IRecordProtector` with a key ring for shared records; rotate keys without writing plaintext records to the backend.
- Keep `UseDistributedSessions(... EnableFastReconnect = true)` opt-in. Use
  signed SecureChannels and a shared, freshness-protected session store.
- Apply TTLs, eviction, and keyspace quotas for session, nonce, subscription, and retransmission keys.
- Run readiness from `ServiceLevel` rather than a raw TCP probe when clients should only reach the active or healthy server.
