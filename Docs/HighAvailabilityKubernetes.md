# Kubernetes High Availability Deployment

This guide is the single Kubernetes deployment guide for OPC UA high availability. It covers the base `OPCFoundation.NetStandard.Opc.Ua.Redundancy.Server` features and the opt-in `OPCFoundation.NetStandard.Opc.Ua.Redundancy.K8s` extension: Kubernetes Lease leader election, EndpointSlice peer discovery, and HTTP readiness/liveness driven by OPC UA `ServiceLevel`.

See [HighAvailability.md](HighAvailability.md) for the OPC 10000-4 §6.6 redundancy model. The worked server sample is `Applications/RedundantServer`.

The API names distinguish standardized OPC UA model wiring from deployment extensions. `AddServerRedundancy(...)`, `AddServerServiceLevel(...)`, and `AddRequestServerStateChange(...)` publish or maintain OPC 10000-4 §6.6 nodes/methods; `UseDistributedAddressSpace(...)`, `UseDistributedSessions(...)`, `UseDistributedSubscriptionMirroring(...)`, `UseKubernetesLeaderElection(...)`, `UseKubernetesPeerDiscovery(...)`, and `UseKubernetesReadiness(...)` register beyond-spec extension services. `AddServerRedundancy(...)` does not drive `Server.ServiceLevel` by itself, so Kubernetes readiness must also register a ServiceLevel provider, commonly with `AddServerServiceLevel(...)` or the leader-aware provider used by the sample.

## Redundancy shapes on Kubernetes

Use **non-transparent redundancy** when each pod has its own OPC UA endpoint and clients use `Server.ServerRedundancy`, `FindServers`, and `ServiceLevel` to fail over. Use **transparent redundancy** when clients connect to one virtual address, typically a `Service` or external load balancer, and the pods share the endpoint URL, application URI, certificate, session state, and subscription state required to hide failover.

A multi-pod deployment needs a shared store reachable by all replicas for distributed address-space/session/subscription state. The in-memory store is only for single-process tests and local samples. Protect the store conduit with authenticated encryption, provision a shared record-protection key through a Kubernetes Secret or CSI-backed secret store, and size/expire session, nonce, and subscription keys so a faulty replica cannot exhaust the backend.

## Application wiring

```csharp
services.AddOpcUa()
    .AddServer(server =>
    {
        // endpoint, certificate, and application configuration
    })
    .AddNodeManager<MyNodeManagerFactory>()
    .UseDistributedAddressSpace(options =>
    {
        options.UseLeaderElection = true;
        options.LeaseDuration = TimeSpan.FromSeconds(30);
        options.RenewInterval = TimeSpan.FromSeconds(10);
        options.NodeId = Environment.GetEnvironmentVariable("HOSTNAME") ?? Environment.MachineName;
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
    })
    .UseKubernetesLeaderElection(options =>
    {
        options.LeaseName = "opcua-ha-leader";
        options.Kubernetes.NodeId = Environment.GetEnvironmentVariable("HOSTNAME") ?? Environment.MachineName;
    })
    .UseKubernetesPeerDiscovery(options =>
    {
        options.ServiceName = "opcua-ha-headless";
        options.LocalAddress = Environment.GetEnvironmentVariable("POD_IP");
    })
    .UseKubernetesReadiness(options =>
    {
        options.Port = 8080;
        options.ReadinessPath = "/readyz";
        options.LivenessPath = "/livez";
        options.ReadyMinimumServiceLevel = ServiceLevels.HealthyMinimum;
    });
```

`UseKubernetesLeaderElection` uses the in-cluster service account token, namespace, and CA mounted at `/var/run/secrets/kubernetes.io/serviceaccount`. Outside Kubernetes it falls back to `SharedStoreLeaseElection` by default (`UseSharedStoreFallback = true`) so local development can still exercise the distributed path.

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
apiVersion: rbac.authorization.k8s.io/v1
kind: Role
metadata:
  name: opcua-ha
  namespace: opcua
rules:
  - apiGroups: ["coordination.k8s.io"]
    resources: ["leases"]
    verbs: ["get", "create", "update", "patch", "delete"]
  - apiGroups: ["discovery.k8s.io"]
    resources: ["endpointslices"]
    verbs: ["get", "list", "watch"]
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
            - name: HA_RECORD_KEY
              valueFrom:
                secretKeyRef:
                  name: opcua-ha-kek
                  key: record-key
          ports:
            - name: opcua-tcp
              containerPort: 4840
            - name: health
              containerPort: 8080
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
```

A Deployment variant is nearly identical: replace `kind: StatefulSet` with `kind: Deployment`, remove `serviceName`, and keep the same labels, probes, service account, and ports.

## Readiness and ServiceLevel

`UseKubernetesReadiness` starts a small HTTP listener. `/livez` returns success while the process can answer the probe. `/readyz` returns HTTP 200 only when `IServiceLevelProvider.GetServiceLevel()` is at least `ReadyMinimumServiceLevel`, which defaults to the Healthy sub-range (`200`). This lets a client-facing Service route only to the current leader or otherwise healthy replicas.

For Warm active/passive deployments, `LeaderServiceLevelProvider` normally makes the leader Healthy and standbys Degraded; readiness therefore routes traffic to the leader. For Hot load-balanced deployments, use the Healthy 200-255 sub-range to reflect load and keep all healthy pods ready.

## Kubernetes Lease leader election

`UseKubernetesLeaderElection` replaces the registered `ILeaderElection` with `KubernetesLeaseLeaderElection` when running in cluster. The Lease name defaults to `opcua-server-leader`; tune `LeaseDuration` and `RenewInterval` above worst-case scheduling, API-server, and network jitter. Outside Kubernetes, the extension can fall back to the base `SharedStoreLeaseElection` for local testing.

Leader election controls the writer role in `UseDistributedAddressSpace` and can drive `ServiceLevel` through `LeaderServiceLevelProvider`. A graceful shutdown should release the lease and give Kubernetes enough `terminationGracePeriodSeconds` to remove readiness before the pod exits.

For a strongly consistent alternative, `UseRedundancyConsistency` registers a native Raft `ILeaderElection` whose leadership is decided by the Raft consensus protocol itself (one leader per term, no split-brain) instead of a Kubernetes Lease or a lease-CAS. Run the Raft members as a StatefulSet with stable network identities and an odd replica count (3 or 5) for a fault-tolerant quorum, bind the external RaftCs engine through `RaftConsensusFactory`, and place the `RaftCs.Storage.File` WAL on a per-pod PersistentVolume so a restarted member rejoins from its log rather than a full snapshot. See [Consistency modes](HighAvailability.md) for the strong (Raft) vs. eventual (CRDT) trade-offs.

## EndpointSlice peer discovery

`UseKubernetesPeerDiscovery` polls EndpointSlices for the configured headless Service and builds peer URIs from address, port name, URI scheme, and port options. Set `LocalAddress` to the pod IP or DNS name so the local pod is excluded. For non-transparent redundancy, feed the discovered peers into `ServerRedundancyOptions.RedundantPeers` or the sample's peer configuration so clients can resolve the set through `FindServers`.

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
  record-key: <base64-of-32-or-more-random-bytes>
```

For transparent redundancy or session fast reconnect, mount the same `ApplicationInstanceCertificate` and private key into every replica because the client signs an `ActivateSession` challenge against one logical application. For non-transparent redundancy, each server may have its own ApplicationUri and certificate and is managed independently by the certificate manager. Never bake private keys, trust lists, or shared store credentials into images.

The shared transparent-mode private key has replica-set-wide blast radius: compromise of one pod or mounted secret compromises the virtual OPC UA server identity for every replica behind the Service. Store the key in a Kubernetes Secret only when that matches the cluster's threat model; otherwise use a CSI-backed secret store or HSM/KMS integration with strict RBAC, audit, and node access controls.

Rotate the shared transparent-mode certificate/key with an overlap window. Issue a replacement certificate for the same virtual endpoint identity, update client/GDS trust lists to trust the replacement before rollout, update the mounted secret or CSI version, restart or reload pods until every ready replica presents the new certificate, then revoke/remove the old certificate and delete old secret versions. For suspected key compromise, mark readiness unhealthy or drain the Service before any replica with the old key can continue serving clients.

## Time synchronization

OPC 10000-4 requires redundant server sets to be time synchronized. Kubernetes nodes should run NTP or PTP, monitor drift, and keep Lease durations comfortably above expected clock and scheduling jitter. Certificate validity, SecureChannel token validation, audit records, historian timestamps, SourceTimestamp/ServerTimestamp comparisons, and EventId strategies all depend on sane clocks.

## GDS and NTRS registration

For non-transparent redundancy, register each server with GDS and include the `NTRS` ServerCapability so clients and Network Topology and Routing Services can discover the redundant set. `AddServerRedundancy(...)` advertises `NTRS` by default for Cold/Warm/Hot/HotAndMirrored modes. For transparent redundancy, register the virtual application/endpoint that clients use; replicas can still have operational identities for diagnostics, but discovery should return the stable virtual address.

## Security checklist

- Restrict RBAC to the namespace and resources above; add NetworkPolicy so only the OPC UA pods that need it can reach the Kubernetes API and shared store.
- Protect shared-store traffic with mutual TLS or an equivalent authenticated channel and fail closed if the store is unavailable for state that is required by the selected redundancy mode.
- Use `IRecordProtector` with a key ring for shared records; rotate keys without writing plaintext records to the backend.
- Keep `UseDistributedSessions(... EnableFastReconnect = true)` opt-in and validate that single-use nonce storage is strongly consistent across replicas.
- Apply TTLs, eviction, and keyspace quotas for session, nonce, subscription, and retransmission keys.
- Run readiness from `ServiceLevel` rather than a raw TCP probe when clients should only reach the active or healthy server.
