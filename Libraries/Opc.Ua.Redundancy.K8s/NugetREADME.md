# OPC UA .NET Standard — Kubernetes Distributed Server Integration

`OPCFoundation.NetStandard.Opc.Ua.Redundancy.K8s` adds opt-in Kubernetes integration to `OPCFoundation.NetStandard.Opc.Ua.Redundancy.Server`.

## Overview

The package keeps Kubernetes concerns outside the base high-availability package. It provides Lease-backed leader election, peer discovery from EndpointSlices, an HTTP readiness/liveness bridge driven by OPC UA `ServiceLevel`, and a strongly-consistent multi-node Raft consensus wiring (`UseKubernetesRaftConsensus`, RaftCs over NanoMsg with an optional file WAL).

## Getting started

```csharp
services.AddOpcUa()
    .AddServer(...)
    .UseDistributedAddressSpace(options => options.UseLeaderElection = true)
    .UseKubernetesLeaderElection(options => options.LeaseName = "opcua-ha")
    .UseKubernetesPeerDiscovery(options => options.ServiceName = "opcua-ha-headless")
    .UseKubernetesReadiness(options => options.Port = 8080);
```

For strong consistency, wire a Raft cluster from the StatefulSet ordinals and compose it with `UseRedundancyConsistency`:

```csharp
services.AddOpcUa()
    .AddServer(...)
    .UseKubernetesRaftConsensus(o =>
    {
        o.HeadlessServiceName = "opcua-ha-headless";
        o.ReplicaCount = 3;                      // static membership; odd for quorum
        o.StoragePath = "/var/lib/opcua/raft";   // PersistentVolume mount for the WAL
    })
    .UseRedundancyConsistency(RedundancyConsistencyMode.Strong);
```

## Target frameworks

`net8.0`, `net9.0`, `net10.0`. .NET Framework and netstandard consumers use the base distributed package without Kubernetes integration.

## Additional documentation

See the [Kubernetes high availability deployment guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/HighAvailabilityKubernetes.md) for RBAC, Service, StatefulSet, readiness, and time-sync guidance.
