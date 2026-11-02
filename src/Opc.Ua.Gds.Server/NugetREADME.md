# OPC UA .NET Standard — GDS server

`OPCFoundation.NetStandard.Opc.Ua.Gds.Server` is the
server-side library for the OPC UA Global Discovery Server (GDS) —
OPC 10000-12 (Part 12). It implements the GDS service-call surface
on top of `Opc.Ua.Server`, with pluggable `ICertificateGroupProvider`
/ `IApplicationsDatabase` / `ICertificateRequest` providers so the
backing certificate authority and applications store can be swapped
to match the deployment.

## Overview

Reference this package from a GDS executable. The standard
applications-database provider ships an in-process LINQ
implementation; production deployments usually plug a real database
back-end behind the same `IApplicationsDatabase` surface.

The package also provides the OPC 10000-21 registrar administration
surface. Load the generated `Opc.Ua.Onboarding` nodes from
`Opc.Ua.Gds`, then bind `DeviceRegistrarAdminState` to an
`ITicketStore` with `BindToTicketStore`. `MemoryTicketStore` is provided
for samples and tests; production hosts should inject a persistent,
concurrency-safe implementation.

`AddDeviceRegistrarPushScheduler<TProcessor>` connects one bounded in-memory
queue to the host lifetime and `IDeviceRegistrarPushScheduler`. The processor
owns actual provisioning and retained recovery state. Queue failures are
reported; uncertain remote operations are not automatically repeated.
Durable acceptance requires a separately supplied scheduler.

`OnboardingPushIdentityValidator` uses the registrar's ticket-scoped identity
selector to check the actual authenticated server certificate of a push device.
It reads `RequestTickets` through a generation-bound encrypted session; discovery
metadata cannot substitute for the channel peer. `CaptureAsync` retains that
generation for subsequent generated clients through `OnboardingPushSession.Session`;
replacement sessions and channels cannot receive requests authorized by the
earlier admission. Disposing the context never closes the caller's connection.
The host still supplies channel trust, software-update policy and supervised push
orchestration. This validator
does not install certificates or complete the push onboarding flow.

## Target frameworks

`net48`, `net8.0`, `net9.0`,
`net10.0`.

## Additional documentation

See the [main repository README](https://github.com/OPCFoundation/UA-.NETStandard)
and the
[GDS guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/GDS.md).
