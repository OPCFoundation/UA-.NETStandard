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
for samples and tests. `AddFileSystemOnboardingTicketStore` selects the
filesystem implementation for a single writer, retaining exact ticket
envelopes and metadata across restarts without changing trust policy.
It rejects damaged state and competing store registrations. Hosts sharing
storage across processes need a provider with storage-level coordination;
the filesystem adapter only serializes calls within one instance.

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
This validator does not install certificates or complete the push onboarding flow.

`AddDeviceRegistrarPush` selects a generated-client processor and the bounded
queue. The processor checks actual-peer identity and persisted software approval,
uploads an existing request-correlated certificate and complete trust list, and
requires a new channel proving the installed credential before promotion.
`AddDeviceRegistrarPushConnections` supplies fresh authenticated sessions through
the existing session factory, with host-selected endpoints, credentials and
namespace-stable model addresses. It checks the actual installed peer and
retains cleanup failures without changing global certificate trust.
`AddDeviceRegistrarPushEnrollments` binds a secret-store enrollment journal to
the actual hosted GDS, reusing its authority, approval queue and issuance receipt
store. It retains original CSR and request evidence, resumes accepted issuance
through its receipt and requires explicit original-evidence recovery after an
uncertain key-creation or queue-insertion outcome. `FindRequestAsync` exposes
retained requests for administrative approval. Hosts must select storage that
survives restart and ensure one provider owns each journal scope; the default
in-memory secret store does not meet that durability requirement. Custom hosts
can still supply `IDeviceRegistrarPushEnrollmentProvider`.

`ProvisionableDeviceSecurityInstaller` supplies local application-certificate
installation, explicit endpoint activation and reset through a separate retained
device-identity key and captured TrustList defaults. The selected secret and
certificate stores own restart durability. The device security gate retains
the first authorized registrar before protected configuration proceeds; restart
preserves that owner, and reset releases it. Fluent hosting installs that same
owner as `IPushConfigurationAuthorization` for standard push mutations and
TrustList writes. The existing transaction coordinator retains staging and
rollback ownership. Recording its completed transaction as the installed device
security transition remains incomplete, so this registration alone does not
complete connected device provisioning.

Singleton application scope is retained in the shared lifecycle and enforced
for subsequent managed-application registration and certificate access.
The filesystem provider writes version 2 checkpoints; version 1 devices load
with managed-application access prohibited because those checkpoints contain no
scope evidence. Custom lifecycle providers need
`IDeviceRegistrarApplicationScopeLifecycle` to authorize managed applications.

## Target frameworks

`net48`, `net8.0`, `net9.0`,
`net10.0`.

## Additional documentation

See the [main repository README](https://github.com/OPCFoundation/UA-.NETStandard)
and the
[GDS guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/GDS.md).
