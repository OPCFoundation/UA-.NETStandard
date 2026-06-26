# OPC UA .NET Standard — Distributed / High-Availability Server

`OPCFoundation.NetStandard.Opc.Ua.Server.Distributed` adds the optional distributed building blocks that let an `OPCFoundation.NetStandard.Opc.Ua.Server` server run as a redundant replica set (active/passive or active/active) while sharing its address space and — optionally — its session state across replicas.

## Overview

The core server library stays a self-contained, single-instance server. This package layers the distributed concerns on top through dependency injection so the in-memory, single-instance path is unchanged when the package is not used:

- A shared, integrity-protected key/value backend (`ISharedKeyValueStore`) that mirrors node additions, removals, references, and values across replicas.
- Leader election (`ILeaderElection`) for the shared-read / leader-write redundancy model, surfaced to clients through the standard OPC UA `ServiceLevel` and redundancy nodes.
- An opt-in `DistributedSessionManager` that mirrors encrypted, integrity-protected session state for fast reconnect on a standby, always running a full `ActivateSession` (the authentication token is only a lookup key).

## Getting started

Wire the distributed address space and (optionally) shared sessions through the fluent DI surface:

```csharp
services.AddOpcUa()
    .AddServer(...)
    .UseDistributedAddressSpace(distributed =>
    {
        distributed.UseSharedKeyValueStore(new InMemorySharedKeyValueStore());
    })
    .UseDistributedSessions();
```

The single-instance defaults remain in effect until a shared store is supplied, so the same server binary runs stand-alone or as part of a replica set.

## Target frameworks

`net472`, `net48`, `netstandard2.1`, `net8.0`, `net9.0`, `net10.0`.

## Additional documentation

See the [High Availability guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/HighAvailability.md) for the architecture, the security and threat model, and the [Kubernetes deployment guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/KubernetesDeployment.md) for running the server as a replica set.
