# OPC UA .NET Standard — Kubernetes Distributed Server Integration

`OPCFoundation.NetStandard.Opc.Ua.Server.Redundancy.K8s` adds opt-in Kubernetes integration to `OPCFoundation.NetStandard.Opc.Ua.Server.Redundancy`.

## Overview

The package keeps Kubernetes concerns outside the base high-availability package. It provides Lease-backed leader election, peer discovery from EndpointSlices, and an HTTP readiness/liveness bridge driven by OPC UA `ServiceLevel`.

## Getting started

```csharp
services.AddOpcUa()
    .AddServer(...)
    .UseDistributedAddressSpace(options => options.UseLeaderElection = true)
    .UseKubernetesLeaderElection(options => options.LeaseName = "opcua-ha")
    .UseKubernetesPeerDiscovery(options => options.ServiceName = "opcua-ha-headless")
    .UseKubernetesReadiness(options => options.Port = 8080);
```

## Target frameworks

`net8.0`, `net9.0`, `net10.0`. .NET Framework and netstandard consumers use the base distributed package without Kubernetes integration.

## Additional documentation

See the [Kubernetes high availability deployment guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/HighAvailabilityKubernetes.md) for RBAC, Service, StatefulSet, readiness, and time-sync guidance.
