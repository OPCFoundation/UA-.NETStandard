# OPC UA .NET Standard — GDS client

`OPCFoundation.NetStandard.Opc.Ua.Gds.Client` is the
client-side library for the OPC UA Global Discovery Server (GDS) —
OPC 10000-12 (Part 12). It implements the GDS service-call surface
(`RegisterApplication`, `FindApplications`, `GetCertificateGroups`,
`StartSigningRequest`, `FinishRequest`, `GetTrustList`, …) on top of
`Opc.Ua.Client`, with the certificate-push management flows for
servers that delegate PKI to a GDS.

## Overview

Reference this package from any application or tool that needs to
talk to a GDS — typically a provisioning utility, a certificate
authority bridge, or a server that uses GDS-driven push management.

`ServerPushConfigurationClient.CaptureAsync` runs the existing certificate and
TrustList operations on an already authorized encrypted session generation. It
does not reconnect, change user credentials, or close the caller's connection.
`AddGdsClient` registers a
`Func<ISession, CancellationToken, ValueTask<ServerPushConfigurationClient>>`
capture factory alongside the existing connection-owning client. Dispose each
captured client asynchronously after its operations.

Captured TrustList reads and failed writes close remote handles with a separate
five-second cleanup token, even after transfer cancellation. If transfer and
cleanup both fail, the operation reports both exceptions. A failed
`CloseAndUpdate` response does not establish whether the server committed the
update; reconcile its state before repeating the write.

For OPC 10000-21 registrar administration, `OnboardingClient` calls the
standard `DeviceRegistrarAdminType` instance with specification-native
`ArrayOf<string>` signed JSON ticket inputs and returns `ArrayOf<StatusCode>`.
Register it through `.AddGdsClient().AddOnboardingClient()`.

`PullDcaWorkflow` provides bounded identity selection, software-update gating,
certificate requests, trust download and host-owned installation through
generated proxies and a borrowed `ManagedSession`. Register the identity and
credential providers before selecting `.AddGdsClient().AddPullDcaWorkflow()`.
Transport failures retry only the original signing request within the shared
finish-attempt budget. Exhaustion propagates the final error; authorization and
certificate-validation errors are not retried.

An optional `.AddPullDcaCertificateReceipts(namespaceUris, scope)` registration
retains received signing output in a host-selected `ISecretStore`, so a later
trust-download or installation failure can resume without repeating an accepted
`FinishRequest`. The built-in adapter requires one exclusive scope owner;
restart durability depends on the selected store. It does not restore private
keys or complete the remaining device lifecycle.

An explicit `IPullDcaSigningRecoveryProvider` enables Part 12 `GetCertificates`
recovery when a repeated signing-request completion returns `Bad_InvalidArgument`.
The provider must prove the retained request/key binding and reject stale
assignments before output is retained or installed. Persistence alone cannot
recover an accepted response lost before local publication. The sample does not
yet supply the request evidence needed for this recovery path.

## Target frameworks

`net48`, `net8.0`, `net9.0`,
`net10.0`.

## Additional documentation

See the [main repository README](https://github.com/OPCFoundation/UA-.NETStandard)
and the
[GDS guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/GDS.md).
