<!--
SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
SPDX-License-Identifier: MIT
-->

# OPC UA .NET Standard — Kubernetes Distributed Server Integration

`OPCFoundation.NetStandard.Opc.Ua.Redundancy.Kubernetes` adds opt-in Kubernetes integration to `OPCFoundation.NetStandard.Opc.Ua.Redundancy.Server`.

## Overview

The package provides Kubernetes Lease election, EndpointSlice peer discovery,
and HTTP readiness and liveness driven by OPC UA `ServiceLevel`.
`UseKubernetesRaftConsensus` configures a multi-member Raft store with
mutual TLS, a pinned certificate per member, and a durable write-ahead log.

## Getting started

```csharp
services.AddOpcUa()
    .AddServer(...)
    .UseKubernetesLeaderElection(options => options.LeaseName = "opcua-ha")
    .UseKubernetesPeerDiscovery(options => options.ServiceName = "opcua-ha-headless")
    .UseKubernetesReadiness(options =>
    {
        string podIp = Environment.GetEnvironmentVariable("POD_IP")
            ?? throw new InvalidOperationException("Set POD_IP from status.podIP.");
        options.Host = new UriBuilder("http", podIp).Host;
    });
```

Kubelet sends HTTP probes to the pod IP, so bind the listener to that IP
rather than its default, `localhost`. Set `POD_IP` on the container using
the Kubernetes Downward API:

```yaml
env:
  - name: POD_IP
    valueFrom:
      fieldRef:
        fieldPath: status.podIP
readinessProbe:
  httpGet:
    path: /readyz
    port: 4852
livenessProbe:
  httpGet:
    path: /livez
    port: 4852
```

Both probes use `KubernetesReadinessOptions.DefaultPort` (4852) unless you
set `Port`. If you change the port, update both probe definitions.

Register a leader-aware `IServiceLevelProvider` so readiness reflects the
elected role. Kubernetes Lease election alone does not guard publication of
distributed address-space state.

For a strongly consistent payload store, wire a Raft cluster from the
StatefulSet ordinals and compose it with `UseRedundancyConsistency`:

```csharp
services.AddOpcUa()
    .AddServer(...)
    .UseKubernetesRaftConsensus(o =>
    {
        o.HeadlessServiceName = "opcua-ha-headless";
        o.ReplicaCount = 3;                      // static membership; odd for quorum
        o.StoragePath = "/var/lib/opcua/raft";   // PersistentVolume mount for the WAL
        o.ResolveMemberCertificateAsync = ResolveRaftMemberCertificateAsync;
        o.ResolvePeerEndPointAsync = ResolveStableRaftPeerEndpointAsync;
    })
    .UseRedundancyConsistency(RedundancyConsistencyMode.Strong);
```

Provision the local TLS certificate with its private key and the expected
public leaf for each peer. Use stable addresses that route to exactly one
member. Distributed address-space writes and the hosted replicated event feed
also require a separate freshness authority that implements
`IDeadlineGuardedSharedKeyValueStore`. The Raft store cannot check lease expiry
in the same commit.

## Target frameworks

`net10.0`, `net11.0`. .NET Framework consumers use the base distributed package without Kubernetes integration.

## Additional documentation

See the [Kubernetes high availability deployment guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/Kubernetes.md) for RBAC, Service, StatefulSet, readiness, and time-sync guidance.
