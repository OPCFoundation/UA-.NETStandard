# OPC UA Global Discovery Server (GDS) — Developer Guide

This package implements the OPC UA Global Discovery Server (GDS) as
defined in OPC 10000-12 (Part 12). It provides application
registration, certificate lifecycle management (pull and push models),
KeyCredentialService, AuthorizationService, and role-based access
control.

## Quick Links

| Topic | Document |
|-------|----------|
| GDS core (this file) | Directory, certificates, push management, roles |
| [KeyCredentialService](KeyCredentialService.md) | Credential issuance for non-UA services |
| [AuthorizationService](AuthorizationService.md) | OAuth2-style access token issuance |

## Contents

- [Quick Links](#quick-links)
- [Packages](#packages)
- [1. Client API](#1-client-api)
  - [GlobalDiscoveryServerClient](#globaldiscoveryserverclient)
  - [ServerPushConfigurationClient](#serverpushconfigurationclient)
  - [OPC 10000-21 OnboardingClient](#opc-10000-21-onboardingclient)
- [2. Server-Side: Building a GDS](#2-server-side-building-a-gds)
  - [Minimal GDS Server](#minimal-gds-server)
  - [Using GdsNodeManagerFactory](#using-gdsnodemanagerfactory)
  - [Extension Points](#extension-points)
- [3. Implementing Providers](#3-implementing-providers)
  - [IApplicationsDatabase](#iapplicationsdatabase)
  - [ICertificateGroup](#icertificategroup)
  - [IGdsUserDatabase](#igdsuserdatabase)
  - [IConfigurationDataStore](#iconfigurationdatastore)
- [4. Roles and Authorization](#4-roles-and-authorization)
  - [GDS Roles (OPC 10000-12 §7.2)](#gds-roles-opc-10000-12-72)
  - [ApplicationSelfAdmin Privilege](#applicationselfadmin-privilege)
  - [ApplicationAdmin Privilege](#applicationadmin-privilege)
  - [Security: Fail-Closed Channel Validation](#security-fail-closed-channel-validation)
- [5. End-to-End Example](#5-end-to-end-example)
- [Conformance Matrix](#conformance-matrix)
  - [Applicable Part 12 profiles and conformance units](#applicable-part-12-profiles-and-conformance-units)
  - [Automated verification](#automated-verification)
- [GDS Directory (§6.5)](#gds-directory-65)
- [Certificate Management — Pull Model (§7.9)](#certificate-management--pull-model-79)
- [Roles and Privileges (§7.2)](#roles-and-privileges-72)
- [Push Management — ServerConfiguration (§7.10)](#push-management--serverconfiguration-710)
  - [Transaction model, diagnostics and lifecycle (§7.10.2, §7.10.9, §7.10.11, §7.10.17)](#transaction-model-diagnostics-and-lifecycle-7102-7109-71011-71017)
  - [Optional ServerConfiguration surface (§7.10.13, §7.10.14, §7.10.20)](#optional-serverconfiguration-surface-71013-71014-71020)
  - [GDS-managed applications proxy (§7.10.14–§7.10.16)](#gds-managed-applications-proxy-7101471016)
- [TrustList (§7.8)](#trustlist-78)
  - [Certificate and TrustList alarms (§7.8.3)](#certificate-and-trustlist-alarms-783)
  - [TrustList-change effects on channels and Sessions (§7.10.9)](#trustlist-change-effects-on-channels-and-sessions-7109)
- [Audit Events](#audit-events)
- [KeyCredentialService (§8)](#keycredentialservice-8)
- [AuthorizationService (§9)](#authorizationservice-9)
  - [Refresh tokens](#refresh-tokens)
- [Remaining optional / unsupported Part 12 items](#remaining-optional--unsupported-part-12-items)
- [LDS / LDS-ME (§4–5)](#lds--lds-me-45)
- [Extension Points (Phase 2 Abstractions)](#extension-points-phase-2-abstractions)

## Packages

| NuGet Package | Contents |
|---------------|----------|
| `Opc.Ua.Gds` | Part 12 and Part 21 model types, node states, identifiers, encodeable types |
| `Opc.Ua.Gds.Server` | Server-side node managers, providers, authorization, onboarding ticket-store binding |
| `Opc.Ua.Gds.Client` | Client proxies for GDS, push, KeyCredential, AuthorizationService, onboarding |

---

## 1. Client API

### GlobalDiscoveryServerClient

The primary client for interacting with a GDS. Manages its own
session and exposes the full GDS directory and certificate
management API.

Both GDS and push clients validate the server certificate against the originally
requested endpoint hostname before activating credentials. Discovery cannot
replace that destination's identity with an advertised host or transport.

```csharp
using Opc.Ua.Gds.Client;

// Create and connect
var gdsClient = new GlobalDiscoveryServerClient(appConfig);
gdsClient.AdminCredentials = new UserIdentity("admin", "password");
await gdsClient.ConnectAsync("opc.tcp://gds-host:4840");

// Register an application
var appRecord = new ApplicationRecordDataType
{
    ApplicationUri = "urn:my-app",
    ApplicationType = ApplicationType.Server,
    ApplicationNames = new[] { new LocalizedText("My App") }.ToArrayOf(),
    DiscoveryUrls = new[] { "opc.tcp://my-app:4850" }.ToArrayOf()
};
NodeId appId = await gdsClient.RegisterApplicationAsync(appRecord);

// Request a new certificate (pull model)
NodeId requestId = await gdsClient.StartNewKeyPairRequestAsync(
    appId, default, ObjectTypeIds.RsaSha256ApplicationCertificateType,
    "CN=My App", domainNames, "PFX", null);

// Poll for completion
ByteString cert, privateKey;
ArrayOf<ByteString> issuerCerts;
do
{
    await Task.Delay(2000);
    (cert, privateKey, issuerCerts) =
        await gdsClient.FinishRequestAsync(requestId, default);
} while (cert.IsEmpty);

// Clean up
await gdsClient.DisconnectAsync();
```

### ServerPushConfigurationClient

Client for OPC 10000-12 §7.10 push certificate management on a
target server:

Private-key uploads require a confidential, generation-bound dispatch context.
A Sign-only channel is rejected before the key is sent, and reconnection cannot
redirect the captured operation to another channel. Certificate-only updates
retain their non-secret path.

```csharp
var pushClient = new ServerPushConfigurationClient(appConfig);
await pushClient.ConnectAsync(serverEndpoint);

// Push a new certificate
bool restartNeeded = await pushClient.UpdateCertificateAsync(
    pushClient.DefaultApplicationGroup,
    ObjectTypeIds.RsaSha256ApplicationCertificateType,
    newCertBlob, "PFX", privateKeyBlob, issuerCerts);

if (restartNeeded)
{
    await pushClient.ApplyChangesAsync();
}

// Create a self-signed certificate on the server
await pushClient.CreateSelfSignedCertificateAsync(
    pushClient.DefaultApplicationGroup,
    ObjectTypeIds.RsaSha256ApplicationCertificateType,
    "CN=NewSubject", domainNames);
```

### OPC 10000-21 OnboardingClient

`Opc.Ua.Gds` ships the generated `Opc.Ua.Onboarding` model,
including the well-known `DeviceRegistrar` instance and the typed
`DeviceRegistrarAdminTypeClient`. `OnboardingClient` is the DI-friendly
facade for loading and removing encoded tickets:

```csharp
NodeId registrarId = ExpandedNodeId.ToNodeId(
    Opc.Ua.Onboarding.ObjectIds.DeviceRegistrar_Administration,
    session.NamespaceUris);

var onboarding = new OnboardingClient(session, registrarId, telemetry);
ArrayOf<StatusCode> results = await onboarding.RegisterTicketsAsync(
[
    firstTicketJson,
    secondTicketJson
]);
```

The public contract follows Part 21: tickets are
`ArrayOf<string>` (`EncodedTicket` is a String subtype), and
the per-ticket results are `ArrayOf<StatusCode>`.
Each string contains the signed JSON envelope, not Base64-encoded JSON.
This representation is specified in
[Part 21 v1.05.04 §8.2.1](https://reference.opcfoundation.org/specs/OPC-10000-21/v1.05.04/8.2.1)
and in the matching
[namespace-zero NodeSet](https://github.com/OPCFoundation/UA-Nodeset/blob/UA-1.05.04-2025-01-08/Schema/Opc.Ua.NodeSet2.xml)
(`i=25726`, subtype of `i=12`).

On the server, load the generated model and bind the standard
administration object to an injected ticket store:

```csharp
NodeStateCollection nodes = new NodeStateCollection()
    .AddOpcUaGds(context)
    .AddOpcUaOnboarding(context);
DeviceRegistrarState registrar =
    nodes.OfType<DeviceRegistrarState>().Single();

registrar.Administration!.BindToTicketStore(ticketStore, ticketValidator);
```

Register ticket services with
`.AddOnboardingTickets(trustedTicketStorePath, issuerStorePath)` on the
server or GDS builder, using an injected `ICertificateManager`.
The GDS builder registers authority initialization on the GDS itself, including
when a regular server shares the service collection. The regular server builder
registers it on that server. Both use the explicitly injected certificate manager;
these registrations do not create separate per-server trust providers.
Repeated registrations may select the same authority-store paths. Selecting
different paths for the same authority kind in one service collection throws
before changing registrations; it does not silently retain the first paths.
Use separate service collections for registrars with independent default trust
contexts.
Resolve `IOnboardingTicketValidator` and pass it to the binding.
The validator accepts RFC 7515 general JSON serialization with protected
`alg`, `x5c`, and `cty` headers; a composite builder's signature also
identifies its composite through `opc-uri`. It decodes the UA payload
only after a SHA-2 signature and its signing authority have been verified.
For device-supplied tickets, the signer itself must be explicitly trusted;
trusting its root CA is sufficient only for tickets supplied out of band.

The handlers pass request cancellation to the validator and `ITicketStore`,
retain validated ticket metadata, and return a status for each input.
An invalid ticket returns `BadTicketInvalid` without entering the store.
The obsolete binding overload without a validator returns
`BadConfigurationError` for registration; it does not accept unverified
tickets. Removing a stored ticket does not revoke a device that was
already accepted.

Bind `TicketAuthorities` and `DeviceIdentityAuthorities` to their managed
certificate stores through `DeviceRegistrarAuthorityTrustLists`:

```csharp
var authorityLists = new DeviceRegistrarAuthorityTrustLists(
    registrar.Administration!, context, certificateManager, server, telemetry);
```

The node manager retains this binding and disposes it after active requests
have drained. Both authority lists need registered trusted and issuer store
paths. The file services use the manager's store resolver and notify the
manager after trust material changes, so validation does not retain an old
cached trust decision. A custom manager must implement `ICertificateStoreResolver`;
the binding does not substitute a process-global resolver. Reads and writes
require an encrypted authenticated SecureChannel and the `RegistrarAdmin` role;
the file-service limit is 1 MiB.
The fluent ticket and identity registrations initialize their authority
providers before node managers load, including when those providers have not
otherwise been resolved from the container.

Create a signed ticket with a private key resolved from the certificate store:

```csharp
var writer = new OnboardingTicketWriter(
    certificateManager.CertificateProvider, messageContext);
string ticketJson = await writer.WriteAsync(
    deviceTicket, signingCertificateId, issuerCertificates);
```

Supply-chain actors append their signature through `AppendSignatureAsync`.
Its required callback validates an existing trusted signature before the writer
accesses the new signing key. Select the validation source according to how
the actor acquired the ticket:

```csharp
string countersigned = await writer.AppendSignatureAsync(
    ticketJson,
    compositeSigningCertificateId,
    async (encoded, ct) =>
        (await ticketValidator.ValidateAsync(
            encoded, OnboardingTicketSource.OutOfBand, ct)).Ticket,
    compositeInstanceUri: compositeInstanceUri,
    ct: cancellationToken);
```

The writer preserves the encoded payload and every earlier signature, including
unprotected vendor metadata, and adds `opc-uri` to the new protected header.
Retaining timestamp metadata does not verify its RFC 3161 timestamp.
The writer and default reader share limits of 64 signatures, 16 certificates
per signing chain, and 1,048,576 characters per encoded ticket. Appending a
signature that would exceed either envelope limit returns `BadTicketInvalid`.

The registrar runtime binds `ProvideIdentities`, `UpdateSoftwareStatus`,
`RegisterDeviceEndpoint`, `GetManagers`, and `RegisterManagedApplication`
through `DeviceRegistrarState.BindToRuntime`. Configure it with
`.AddDeviceRegistrar(configureProviders)` on the server or GDS builder.
The host supplies its shared GDS application database, lifecycle storage,
application authorization, push scheduler, manager descriptions, audit
reporter, identity selector, candidate storage, and onboarding policy.
The builder rejects missing runtime providers and registers
`DeviceRegistrarNodeManagerFactory`. The GDS host loads the registrar namespace
through this factory, binds the runtime and ticket administration, attaches
the managed authority lists, and restricts registration-event permissions.
When both server kinds share a service collection, the GDS builder selects the
registrar namespace and certificate-issuance gate only for the GDS. The regular
server builder selects its namespace factory only for that server; it does not
enable onboarding certificate authorization on the cohosted GDS. This routing
does not create independent runtime or trust-provider instances for two
registrars in the same service collection.
The existing GDS directory retains ownership of its application model nodes.
Ticket hosting also requires the registered `ITicketStore`,
`IOnboardingTicketValidator`, and `ICertificateManager`; missing services reject
factory resolution. A direct host can construct the factory with those
dependencies. Do not register another node manager for the same onboarding
namespace.

Select `services.AddHostedDeviceRegistrarAuditing()` before registering the
runtime to route audit calls through the standard factory's restricted native
event reporter. The adapter resolves without constructing server nodes; the
factory attaches it after model initialization and detaches it during shutdown.
Calls without an active model return `BadServerHalted`, and another live model
cannot replace its reporter. An existing custom `IDeviceRegistrarAuditReporter`
is retained. Direct hosts can continue constructing and injecting their own
`DeviceRegistrarAuditReporter`; the hosted adapter supports one registrar per
service collection.

`UpdateSoftwareStatus` requires the host's SoftwareUpdateAdmin role. Part 21
names this role, but the published onboarding model assigns no standard NodeId
to it. Select the actual host role with
`services.AddDeviceRegistrarSoftwareUpdateRole(roleId)` before resolving the
runtime; `roleId` must identify a local node by namespace URI. The host publishes
the role and grants it to the authorized software-update sessions. Direct
construction uses `DeviceRegistrarRuntime.SoftwareUpdateAdminRoleId`.
Without this selection, software status changes return `BadConfigurationError`;
RegistrarAdmin does not substitute for SoftwareUpdateAdmin.

For an in-memory supervised push queue, register
`services.AddDeviceRegistrarPushScheduler<MyPushProcessor>(capacity: 32)` before
the registrar's provider check. `MyPushProcessor` implements
`IDeviceRegistrarPushProcessor`; it owns real channel authentication, certificate
operations, software approval and retained lifecycle recovery. The registration
shares one `DeviceRegistrarPushScheduler` between runtime acceptance and the
hosted worker. Direct hosts can construct that worker and call its asynchronous
start/stop methods.

The worker processes one attempt at a time and copies each accepted application
description. A full queue returns `BadTooManyOperations` without waiting; before
startup and after shutdown, acceptance returns `BadServerHalted`. Cancelling a
call after acceptance does not cancel its queued attempt. Host shutdown cancels
the running processor and reports registrations that were accepted but never
processed. Processor failures are logged with a classified application identity
and are not retried automatically: an uncertain certificate operation must first
be reconciled against the processor's retained request state. Queue acceptance
does not survive process termination. Hosts requiring durable acceptance must
supply their own `IDeviceRegistrarPushScheduler`.

For an existing GDS application, the runtime records the device relation before
refreshing shared application metadata. A conflicting relation therefore cannot
change another device's registration. The GDS database and lifecycle provider
commit separately: an association can remain if the later metadata update fails,
and a newly registered GDS entry can remain if recording its relation fails.
Retries use the same shared application identifier.

`OnboardingIdentitySelector` searches validated out-of-band tickets before
device-supplied tickets. It matches certificate subjectAltName URIs without
interpreting unrelated URI names, follows nested composite membership to the
outermost composite, and rejects product-only certificates for composite
members. Selection retains the matching device ticket and the containing
composite's signed envelope separately; a composite membership list cannot
replace the device ticket or supply additional identity trust anchors.
Its `IOnboardingIdentityTrustProvider` must validate the selected
certificate against the authorities in the matching ticket, including the
applicable revocation checks. Register the default provider and selector through
`.AddOnboardingIdentities(trustedIdentityStorePath, issuerIdentityStorePath)`.
The managed `DeviceIdentityAuthorities` list supplies issuer and CRL material;
only authorities from the accepted matching ticket establish trust for the
call. A missing revocation list rejects identities with CRL distribution point
or authority information access extensions. Without either extension, unknown
revocation status is permitted and the decision is logged.
Provision issuer certificates and applicable CRLs in the managed stores.
Ticket validation does not verify RFC 3161 signing timestamps; an expired
signing authority is rejected rather than accepted on an unverified timestamp.

For push admission, `OnboardingPushIdentityValidator` reads the device's
`RequestTickets` through a generation-bound encrypted session and passes only
the transport's actual server certificate to that same identity selector.
Additional certificates from the channel chain supply issuers, not alternative
device identities. A discovery certificate is not used as proof. Validation
rejects a selector that returns a different certificate and rejects a channel
generation change before returning. `AddDeviceRegistrar` registers the validator;
direct hosts can construct it with their selector and telemetry.

```csharp
await using OnboardingPushSession admitted = await pushIdentityValidator.CaptureAsync(
    deviceSession, provisionableDeviceId, cancellationToken);
OnboardingIdentitySelection identity = admitted.Identity;
ISession pushSession = admitted.Session;
```

Construct every subsequent generated certificate or TrustList client with
`admitted.Session`. Each dispatch checks the original authentication token,
namespace tables and physical channel, and requires encryption. It rejects a
replacement generation rather than following a reconnect. Disposing the admission
context releases only local client resources; the caller retains ownership of its
connection. `ValidateAsync` remains available for an identity check without a
retained context, but its result does not authorize requests through another
session.

`ServerPushConfigurationClient.CaptureAsync` reuses the existing certificate and
TrustList implementation on an already authorized encrypted session. Pass
`admitted.Session` to retain the device admission through those operations:

```csharp
await using ServerPushConfigurationClient push = await ServerPushConfigurationClient.CaptureAsync(
    registrarConfiguration, admitted.Session, ct: cancellationToken);
ByteString csr = await push.CreateSigningRequestAsync(
    certificateGroupId, certificateTypeId, subjectName,
    regeneratePrivateKey: false, nonce: default, ct: cancellationToken);
```

The borrowed client does not elevate or restore user credentials, reconnect, or
close the remote session. The server evaluates the identity that the host
authenticated before capture. Connection-management methods and endpoint
retargeting reject borrowed clients; `DisposeAsync` releases only the local
capture. `AddGdsClient` also registers
`Func<ISession, CancellationToken, ValueTask<ServerPushConfigurationClient>>` for
hosts that inject the capture factory. The caller disposes each returned client;
the existing singleton push client retains its connection-owning behavior.

The host still owns initial channel trust. This helper does
not approve an application, install credentials, satisfy a software-update gate,
or implement the supervised `IDeviceRegistrarPushScheduler`. Connected push
orchestration remains incomplete.

`ProvideIdentities` requires an authenticated SecureChannel, as specified by
[Part 21 §9.2.3](https://reference.opcfoundation.org/specs/OPC-10000-21/v1.05.04/9.2.3).
It records a candidate identity and returns the shared certificate-manager
application identifier; supplying certificate bytes does not prove possession
of their private key. The hosted GDS resolves an optional
`IDeviceRegistrarCertificateAuthorization` registration and installs it on
`ApplicationsNodeManager` before loading the address space. A direct host sets
`DeviceRegistrarCertificateAuthorization` on its manager at the same stage.
The asynchronous new-key, signing-request and completion handlers check the
selected channel certificate, software approval and persisted application
ownership. Managed applications also require the stored protocol and current
host permission; a known device's denial cannot fall back to a GDS administrator
role. Applications outside the onboarding lifecycle retain the existing GDS
authorization. Method-level admission does not grant a global GDS role.

Onboarding `FinishRequest` requires an `ICertificateRequestIssuanceStore`;
without one, completion returns `BadConfigurationError` before issuing a
certificate. The hosted GDS resolves this service from DI, while a direct host
sets `ApplicationsNodeManager.CertificateRequestIssuanceStore` before startup.
The receipt binds the application and request identifiers to the configured
certificate group, type, certificate, private-key output and issuer chain.
The handler retains this material before publishing a pending DCA credential.
While the request remains approved, a retry reuses that material rather than
generating a different key or certificate.

The receipt provider's `GetOrCreateAsync` contract includes exclusive creation
and complete publication before returning. All writers sharing its storage
must observe that contract: a second writer cannot invoke a competing issuance
factory for the same application and request. A distributed provider therefore
needs a transaction or cross-writer claim covering lookup, creation and
publication; transferring a claim must prevent its preceding owner from
publishing afterward. Published material remains unchanged until explicitly
removed. Separate `GetAsync` and `SetAsync` calls do not provide this
guarantee, even when each call uses a transactional backend.

For a single-writer host, register an explicitly selected secret store:

```csharp
services.AddSecretStoreCertificateRequestIssuanceStore(
    secretStore, server.NamespaceUris, "gds-database");
```

`SecretStoreCertificateRequestIssuanceStore` borrows that store and uses
namespace URIs to retain identifiers across namespace-index changes. Its
serialization applies within one adapter instance only; exactly one instance
may own the database scope, including across processes. `ISecretStore` supplies
neither a cross-writer claim nor a restart-durability guarantee. The host must
select storage that persists a complete value before `SetAsync` returns, protect
its contents, and retain the same database scope across restarts.
`InMemorySecretStore` does not provide restart recovery. Multiple registrar
writers need a different receipt provider and coordinated request and lifecycle
transactions; the built-in adapter does not implement HA recovery.

A failure before receipt publication leaves the pending credential unchanged,
and issuance may repeat on retry. After complete publication, a retry can reuse
the receipt even if the store committed it but failed to return confirmation.
A read error or corrupt receipt stops completion without regenerating material.
The host owns receipt retention and calls `RemoveAsync` only when no completion
retry needs the receipt. Receipts contain private-key output for new-key
requests, but do not retain its password. They do not prove installation.
An accepted request still returns `BadInvalidArgument` before consulting its
receipt. Recovery of a response lost after server acceptance remains incomplete.
The client-side receipt described below can retain a response that reached the
client before a later trust download or installation failure.

DCA delivery retains the issued certificate separately from the active one.
Authorization compares the server's actual authenticated channel peer certificate
with the pending certificate; a request-supplied identifier or an authenticated
channel using the old active credential cannot confirm the replacement.
Only a matching peer certificate promotes that credential and retires the
preceding DCA certificate. Restart
preserves both states. Repeating delivery of the same certificate does not
change the transition; delivering a different certificate while one is pending
returns `BadRequestNotAllowed` instead of overwriting an installation in progress.
Persisting issuance does not by itself prove installation.

The GDS TrustList read path checks the current channel's device and managed
application relations against the stores assigned in the shared application
database, then rechecks the application policy. A known device cannot read an
unrelated group's TrustList through an administrator-role fallback. The
`TrustList.SetReadAuthorization` callback is awaited on Open, OpenWithMasks,
Read and Close; installing it disables those methods' synchronous dispatch.
Write access retains the existing GDS policy, so DCA read authorization cannot
modify a shared group's trust material. Both device-management workflows still
need connected host providers and restart/reset support.
`MemoryDeviceRegistrarLifecycle` retains state only within the current process.

`PullDcaWorkflow` borrows a container-owned `ManagedSession` and uses generated
registrar and certificate-directory proxies. Its identity and credential
providers retain private keys, select the returned device identity, and install
issued credentials through the existing certificate stores; the workflow does
not close or dispose that session. A required software update invokes the
configured software manager before certificate issuance.

Certificate polling is bounded by `MaximumFinishAttempts`. When approval remains
pending, `PullDcaCertificatePendingException` retains the application and request
identifiers. Persist those identifiers with the original registrar, certificate
profile and retained private key. `CompleteCertificateRequestAsync` polls that
same request and installs its result without generating another key or starting
another request. It does not reconnect the DCA or finish the remaining device
lifecycle. Polling an already-consumed server request cannot recover a response
that was never retained locally without the signing-recovery path below.

Timeout, communication-loss and closed-connection/channel errors consume that
same finish-attempt budget and retry the original request after `FinishRetryDelay`.
The workflow does not regenerate the CSR, submit another signing request or
change the selected channel identity. The borrowed managed session owns transport
recovery. When the budget is exhausted, the final transport error propagates;
authorization and certificate-validation failures are not retried. Cancellation
also stops the retry delay. A retry that returns `BadInvalidArgument` still
requires the explicit request-bound signing-recovery provider below; transport
retry alone does not establish that a recovered assignment is fresh.

Register `IPullDcaCertificateReceiptStore` to retain received signing output
before the workflow downloads trust or invokes the credential installer.
`CompleteCertificateRequestAsync` then reuses that output without calling
`FinishRequest` again. The installer remains responsible for validating the
issued certificate against its retained key and for tolerating repeated
installation after partial effects. The receipt contains public certificates,
not the key, TrustList, or proof of installation.

For a single-owner client using an explicitly selected `ISecretStore`, configure
the receipt adapter on the GDS client builder:

```csharp
gdsBuilder.AddPullDcaCertificateReceipts(registrarNamespaceUris, "device-receipts")
    .AddPullDcaWorkflow();
```

After restoring the original key and channel identity, resume the saved request:

```csharp
PullDcaWorkflow workflow = provider.GetRequiredService<PullDcaWorkflow>();
PullDcaCredentials credentials = await workflow.CompleteCertificateRequestAsync(
    registrar, savedApplicationId, savedRequestId, savedCertificateProfile,
    cancellationToken: cancellationToken);
```

The namespace table must match the selected registrar session. The adapter binds
each receipt to the endpoint, registrar and directory nodes, application,
request, and certificate profile; namespace URIs preserve those bindings when
indices change after restart. A profile mismatch, read failure or corrupt
receipt stops recovery without repeating the network call. The adapter serializes
one instance only, so one instance must exclusively own its scope across all
processes. Its 16 MiB binary checkpoint limit supplements the standard codec
quotas. Storage must publish complete values before returning; restart durability
depends on the chosen store. A distributed provider must implement the
interface's exclusive receive-and-publish operation across writers.
Neither persistence adapter alone can recover an accepted response lost before
local publication.

[Part 12 §7.9.5](https://reference.opcfoundation.org/specs/OPC-10000-12/v1.05.07/7.9.5)
defines recovery when a repeated `FinishRequest` returns `Bad_InvalidArgument`
after a network failure. A client that used `StartSigningRequest` can read the
currently assigned certificate through `GetCertificates`; a client that used
`StartNewKeyPairRequest` must start a new key-pair request. `PullDcaWorkflow`
creates signing requests only. Register an `IPullDcaSigningRecoveryProvider`
to enable its `GetCertificates` recovery path. Direct construction accepts the
same optional provider.

The workflow requests certificates for the original application and group,
requires exactly one assignment for the original certificate type, downloads a
complete TrustList, and invokes the provider before retaining or installing
output. The provider must match the retained registrar/request binding, prove
the original private key, and validate the application URI, certificate validity
and trust. It must also reject stale assignments: an old certificate with a reused
public key is not proof that the original request completed. Missing evidence
must reject recovery. No provider is registered implicitly, and the sample does
not yet supply retained request evidence. Without this provider,
`Bad_InvalidArgument` remains visible to the caller; signing-response recovery
is therefore not complete for the sample.

`ProvisionableDeviceRuntime` supplies the device-side ticket and registrar
configuration methods without creating a Resources hierarchy. `RequestTickets`
reads only explicitly configured ticket identifiers, decodes UTF-8 without
replacement characters, and validates each signed envelope through the injected
ticket validator. This local check does not replace the remote registrar's
device-supplied ticket policy. `SetRegistrarEndpoints` requires `SecurityAdmin`
on an encrypted authenticated channel and publishes detached candidate
descriptions through `IProvisionableDeviceRegistrarStore`. Storing a discovery
candidate does not trust its certificate.

For a single-host device, select `FileSystemProvisionableDeviceRegistrarStore`
before registering the runtime:

```csharp
services.AddFileSystemProvisionableDeviceRegistrarStore(candidateCheckpointPath, fileSystem);
services.AddProvisionableDeviceRuntime(isSingleton, ticketIds);
```

`IProvisionableDeviceRegistrarStore.ReadAsync` returns the persisted candidates
as detached descriptions for the host's discovery process. The filesystem
adapter uses the standard UA binary codec, stages and flushes a complete
checkpoint, then publishes it through `IFileSystem.Replace`. Checkpoints are
limited to 16 MiB and the codec's standard field quotas. Unknown versions,
invalid descriptions, trailing content and unreadable files stop reads and
replacements; the provider does not erase damaged configuration. Clearing the
candidate list is an explicit empty replacement. One instance must exclusively
own the protected checkpoint path; this adapter does not coordinate writers
across processes or guarantee survival of an unflushed operating-system cache
after power loss. It stores no registrar approval, credential installation
checkpoint or software state, and does not implement device reset.

Register these services with
`.AddProvisionableDeviceRuntime(isSingleton, ticketIds)` after registering the
ticket and persistent registrar-store providers. The owning node manager binds
an existing `ProvisionableDeviceState` through `BindToRuntime`; singleton binding rejects separate
`ApplicationConfiguration` components. This registration does not expose a
device resource. Shared Part 22 Resources integration, endpoint credential
switching and connected device recovery/reset remain incomplete.

`ProvisionableDeviceSecurity` supplies device-side installation and reset
orchestration with an explicit `IProvisionableDeviceSecurityInstaller`. Its
pending checkpoint records the certificate from the actual authenticated
registrar channel before invoking the installer. If installation fails, only
that peer can retry the same credentials; another peer or different material
cannot replace the pending operation. After installation, registrar access is
validated against the installed TrustList through an isolated certificate
validation scope. The first registrar's leaf certificate is not a permanent
restriction: another registrar accepted by installed trust may manage the
configured device, as specified by
[Part 21 §7.3](https://reference.opcfoundation.org/specs/OPC-10000-21/v1.05.04/7.3).

The installer owns the existing private key, certificate-store updates, software
gate, and switch to DCA-certificate endpoints. It must tolerate replay of the
same installation. Configure these platform operations explicitly:

```csharp
services.AddSingleton<IProvisionableDeviceSecurityInstaller>(deviceSecurityInstaller);
services.AddProvisionableDeviceSecurity("device-security", dcaApplicationUri);
ProvisionableDeviceSecurity security = provider.GetRequiredService<ProvisionableDeviceSecurity>();
await security.RecoverAsync(cancellationToken);
```

Register `ISecretStore`, `ICertificateManager` and telemetry before this
registration. The fluent registration adds an `IServerPreStartupTask` that calls
`RecoverAsync` before the standard server creates node managers or opens
listeners. Recovery errors abort startup. A directly constructed host must call
`RecoverAsync` before exposing operational endpoints.
`ProvisionableDeviceRuntime` resolves the optional `IProvisionableDeviceSecurity`
provider and gates `SetRegistrarEndpoints` through it. The host invokes
`RequireRegistrarAsync` on its other registrar operations; registration alone
does not install these gates into an existing device. SecurityAdmin on an
encrypted authenticated session is required for `InstallAsync`. The state
adapter has one exclusive scope owner and the same complete-publication and
durability prerequisites as the client receipt adapter. It does not provide
distributed coordination.

The same security instance is registered as `IServerConfigurationResetProvider`,
so the existing configuration node manager retains ownership of network
authorization, audit reporting and deferred shutdown. An application-controlled
physical reset can invoke its local `ResetToServerDefaultsAsync` entry point.
Reset intent is persisted before the platform callback runs; a failed reset
keeps registrar access disabled, and startup recovery retries it before accepting
a new registrar. The installer must restore device identity and security defaults
without deleting installed software. Corrupt checkpoints are reported without
being erased. The runnable samples do not yet exercise this device-side
orchestration or prove endpoint switching and reset over real connections.

For a single-host registrar, `FileSystemDeviceRegistrarLifecycle` retains
identity candidates, active and pending DCA certificate bindings, software approval and
managed-application relations across restart. Register the same instance for
the lifecycle contracts:

```csharp
services.AddFileSystemDeviceRegistrarLifecycle(
    checkpointPath, server.NamespaceUris, fileSystem);
```

Select this provider before registering other lifecycle providers; conflicting
registrations are rejected. Without an explicit `fileSystem`, registration uses
the container's `IFileSystem`, or the local file system if none is registered.

The provider uses `IFileSystem`, stages a complete checkpoint, and calls its
indivisible `Replace` operation after writing and flushing the staged content.
A failed publication leaves the preceding checkpoint authoritative; a corrupt
checkpoint rejects reads and writes instead of becoming an empty database.
Namespace URIs preserve application identifiers when namespace indexes change
after restart. One provider instance must exclusively own the checkpoint path,
and the host must protect it against unauthorized writes. Multiple writer
processes and replicated registrars require a transactional lifecycle provider.
This checkpoint does not implement device-side recovery, reset or certificate
revocation.

`DeviceRegistrarAuditReporter` reports accepted identities with the exact
signed ticket envelopes and software status with its revision. It restricts
the onboarding event hierarchy, including installed subtypes, to `SecurityAdmin`
receivers. Reporting awaits `IServerInternal.ReportEventAsync`; the native
event dispatcher checks `ReceiveEvents` on the event type and source node for
each monitored session before queuing the complete event. Unauthorized sessions
receive no event, including when its fields contain signed tickets.
Disabled auditing or broadened event permissions reject reporting;
the error is returned to the caller. The lifecycle update may already be
committed when reporting fails; a retry retains the shared application identifier.
A custom identity selector using this reporter must retain the signed envelopes
in `OnboardingIdentitySelection`.

The obsolete `OnboardingClient` byte-input overloads decode UTF-8 before
dispatching String inputs. Invalid UTF-8 is rejected locally; the wire
representation is never changed back to ByteString.
See the runnable
[`samples/Gds` onboarding demo](../samples/Gds/README.md) for a secure
registrar/client round trip.

### Part 21 requirements and implementation boundaries

Part 21 support remains partial. The following map uses
[OPC 10000-21 v1.05.04](https://reference.opcfoundation.org/specs/OPC-10000-21/v1.05.04/)
and distinguishes implementation sources from completed interoperability evidence.
The named regression fixtures contain tests for these components; their presence
does not establish that the complete onboarding workflow or a conformance unit
has passed.

| Requirement | Implementation | Regression sources and remaining boundary |
|---|---|---|
| §§4.2.2, 4.3.2: software approval before operational provisioning | `DeviceRegistrarRuntime`, lifecycle providers, `IPullDcaSoftwareUpdateProvider` | `ProvideIdentitiesRuntimeTests`, `PullDcaWorkflowTests`; a connected software-update provider and endpoint gate remain required |
| §§4.3.3, 7.2: DCA and managed-application credentials | `PullDcaWorkflow`, generated certificate-directory proxies, application-specific authorization | `PullDcaWorkflowTests`, `ApplicationsNodeManagerOnboardingTests`; the sample serves one DCA and does not demonstrate multi-application setup |
| §§4.3.6, 7.3: security reset and return to onboarding | `ProvisionableDeviceSecurity`, existing `IServerConfigurationResetProvider` path | `ProvisionableDeviceSecurityTests`; platform reset, preservation of device-identity keys and installed software, and connected re-onboarding still need an installer and integration evidence |
| §§5.1-5.3, 7.1: exact device/composite identity and private-key proof | `OnboardingIdentitySelector`, scoped identity trust, actual-channel registrar authorization, `OnboardingPushIdentityValidator` | `OnboardingIdentitySelectorTests`, `OnboardingIdentityTrustProviderTests`, `DeviceRegistrarRuntimeTests`, `OnboardingPushIdentityValidatorTests`; discovery alone never proves possession |
| §6.4: source-dependent ticket authority trust | `OnboardingTicketValidator`, `OnboardingTicketTrustProvider` | `OnboardingTicketValidatorTests`, `OnboardingTicketTrustProviderTests`; online authority-revocation services are not supplied by these adapters |
| §7.2 and Part 12 §§7.9.5, 7.9.8: request retry and certificate recovery | issuance receipts, client receipts, explicit `IPullDcaSigningRecoveryProvider` | issuance-store, receipt-store and pull-workflow fixtures; concrete durable request evidence and stale-assignment validation remain required for accepted-response loss |
| §7.3: push provisioning, first registrar and installed trust | device security transition provider, actual-peer push validation, existing Part 12 push client | device-security and push-identity fixtures; supervised push orchestration, credential endpoint transition, singleton privilege handling and multi-application provisioning remain incomplete |
| §7.4: alternate authentication/FDO | no FDO protocol implementation | no FDO interoperability evidence; an external authentication service is not treated as an implemented onboarding flow |
| §§8.1-8.2: signed JSON tickets and String-subtype `EncodedTicket` | `OnboardingTicketWriter`, ticket validator, standard model and strict-UTF-8 compatibility wrappers | writer, validator, onboarding-client and stack-generation fixtures; RFC 3161 timestamp verification is not implemented |
| §§9.2.2-9.2.8: registrar methods and application ownership | registrar runtime, async bindings, shared GDS database and certificate authorization | `DeviceRegistrarRuntimeTests`, `ProvideIdentitiesRuntimeTests`, `ApplicationsNodeManagerOnboardingTests`; database/lifecycle publication is not a distributed transaction |
| §§9.2.9-9.2.15: administration, managed authorities and restricted audit delivery | ticket administration bindings, managed authority file services, `DeviceRegistrarAuditReporter` | administration, authority-list and audit-reporter fixtures; connected authorized/unauthorized event delivery still needs remote execution |
| §§9.3.1-9.3.5: provisionable resource, singleton layout, tickets and registrar endpoints | `ProvisionableDeviceRuntime` and generated node bindings | `ProvisionableDeviceRuntimeTests`; shared Part 22 Resources placement and non-singleton `ApplicationConfiguration` hosting remain incomplete |

The published model associates `EncodedTicket` with **Onboarding Ticket Reader**,
`DeviceRegistrarType` with
[**Onboarding Registrar PullManagement**](https://profiles.opcfoundation.org/conformanceunit/4437),
and `ProvisionableDeviceType` with **Onboarding Server PushManagement**.
The [UACore 1.05 conformance-unit catalog](https://profiles.opcfoundation.org/api/conformanceunit/?pg=171&rs=1)
also defines the corresponding DCA-client, registrar-client, and administration
units. All six onboarding records currently have Draft status; a published
Part 21 specification does not make these catalog records released profiles.

| Conformance unit | Role and implementation boundary |
|---|---|
| [Onboarding Ticket Reader](https://profiles.opcfoundation.org/conformanceunit/4441) | Ticket validation; timestamp and revocation boundaries are identified above |
| [Onboarding Registrar Administration](https://profiles.opcfoundation.org/conformanceunit/4440) | Registrar administration model, managed authorities and restricted audit delivery |
| [Onboarding Registrar PullManagement](https://profiles.opcfoundation.org/conformanceunit/4437) | Device-facing registrar server methods and issuance authorization |
| [Onboarding Client PullManagement](https://profiles.opcfoundation.org/conformanceunit/4438) | DCA pull workflow, retained key/request recovery and application provisioning |
| [Onboarding Registrar PushManagement](https://profiles.opcfoundation.org/conformanceunit/4439) | Registrar client orchestration; validated session capture and certificate operations alone are insufficient |
| [Onboarding Server PushManagement](https://profiles.opcfoundation.org/conformanceunit/4436) | Provisionable device, credential endpoint transition, singleton restrictions and reset |

These are coverage targets, not declarations of conformance. The catalog's
including-profile queries currently return no entries for these six records,
so they do not supply a released parent profile whose mandatory dependencies
can be expanded. This does not waive any requirements in Part 21 or the Part 12
certificate-management services it uses. Passing interoperability evidence is
still required before declaring the selected capabilities supported. No FDO
integration is established by the native pull/push components.

---

## 2. Server-Side: Building a GDS

### Minimal GDS Server

Use `GlobalDiscoverySampleServer` with dependency injection of
your storage backends:

```csharp
// 1. Create storage backends
IApplicationsDatabase database = JsonApplicationsDatabase.Load(
    "gds-applications.json", telemetry);
ICertificateGroup certGroup = new CertificateGroup(
    gdsConfig.AuthoritiesStorePath, gdsConfig.CertificateGroups);
IUserDatabase userDb = JsonUserDatabase.Load(
    "gds-users.json", telemetry);

// 2. Create the GDS server
// (database implements both IApplicationsDatabase and ICertificateRequest)
var gdsServer = new GlobalDiscoverySampleServer(
    database, database, certGroup, userDb, telemetry,
    autoApprove: true);

// 3. Start via ApplicationInstance
var app = new ApplicationInstance(telemetry)
{
    ApplicationName = "My GDS",
    ApplicationType = ApplicationType.Server,
    ConfigSectionName = "Opc.Ua.GlobalDiscoveryServer"
};
var config = await app.LoadAsync("MyGds.Config.xml");
await app.CheckApplicationInstanceCertificatesAsync(false);
await app.StartAsync(gdsServer);
```

### Using GdsNodeManagerFactory

For embedding GDS into an existing server:

```csharp
// In your server's CreateMasterNodeManager:
var gdsConfig = configuration.ParseExtension<GlobalDiscoveryServerConfiguration>();
var factory = new GdsNodeManagerFactory(gdsConfig);
additionalNodeManagers.Add(factory.Create(server, configuration));
```

### Extension Points

Every pluggable interface and its purpose:

| Interface | Purpose | Default |
|-----------|---------|---------|
| `IApplicationsDatabase` | Application registration store | `LinqApplicationsDatabase` / `JsonApplicationsDatabase` |
| `ICertificateRequest` | Certificate request lifecycle | `LinqApplicationsDatabase` |
| `ICertificateRequestIssuanceStore` | Material retained before onboarding request acceptance | Explicit provider required for onboarding |
| `ICertificateGroup` | CA operations (sign, revoke) | `CertificateGroup` |
| `IGdsUserDatabase` | User store + ApplicationAdmin bindings | Implement over `LinqUserDatabase` |
| `IKeyCredentialRequestStore` | KeyCredential lifecycle | `InMemoryKeyCredentialRequestStore` |
| `ISecretStore` | Secret storage for credentials | `InMemorySecretStore` |
| `IAccessTokenProvider` | Token issuance (AuthorizationService) | `null` (Bad_NotSupported) |
| `IConfigurationDataStore` | ManagedApplications config persistence | `InMemoryConfigurationDataStore` |
| `IManagedApplicationsNodeManager` | ManagedApplications folder | `StubManagedApplicationsNodeManager` |

---

## 3. Implementing Providers

### IApplicationsDatabase

Stores registered application records. Must also implement
`ICertificateRequest` for certificate request lifecycle (or provide
a separate implementation):

```csharp
public class SqlApplicationsDatabase
    : ApplicationsDatabaseBase, ICertificateRequest
{
    // ApplicationsDatabaseBase provides Match() and ValidateApplication()
    // Override the abstract CRUD methods:

    public override NodeId RegisterApplication(
        ApplicationRecordDataType application)
    {
        ValidateApplication(application); // call base validation
        return _db.InsertApplication(application);
    }

    public override void UpdateApplication(
        ApplicationRecordDataType application)
    {
        ValidateApplication(application);
        _db.UpdateApplication(application);
    }
    // ... implement remaining abstract methods
}
```

### ICertificateGroup

Implement to use an external CA (EST, ACME, EJBCA, etc.):

```csharp
public class EstCertificateGroup : ICertificateGroup
{
    public async Task<(Certificate, byte[], CertificateCollection)>
        NewKeyPairRequestAsync(
            ApplicationRecordDataType application,
            string subjectName,
            string[] domainNames,
            string privateKeyFormat,
            string privateKeyPassword)
    {
        // Call your EST server's /simpleenroll endpoint
        // Return (signedCert, privateKey, issuerChain)
    }
    // ... implement remaining interface methods
}
```

### IGdsUserDatabase

Extend `IUserDatabase` to support the `ApplicationAdmin` role
(OPC 10000-12 §7.2). The GDS sample server automatically detects
`IGdsUserDatabase` during username authentication:

```csharp
public class MyGdsUserDatabase : LinqUserDatabase, IGdsUserDatabase
{
    private readonly Dictionary<string, List<NodeId>> _appAdminMap = new();

    public IReadOnlyList<NodeId>? GetAdministeredApplicationIds(
        string userName)
    {
        return _appAdminMap.TryGetValue(userName, out var ids)
            ? ids : null;
    }

    // Admin API to grant ApplicationAdmin for specific apps:
    public void GrantApplicationAdmin(
        string userName, IEnumerable<NodeId> applicationIds)
    {
        _appAdminMap[userName] = applicationIds.ToList();
    }
}
```

### IConfigurationDataStore

Persistence backend for ManagedApplications (§7.10.16). The
`DefaultManagedApplicationsNodeManager` uses this to populate
`ApplicationConfigurationState` nodes:

```csharp
public class FileConfigurationDataStore : IConfigurationDataStore
{
    public async ValueTask<IReadOnlyList<ManagedApplicationInfo>>
        GetManagedApplicationsAsync(CancellationToken ct)
    {
        // Read from config file / database
    }

    public async ValueTask<uint> WriteConfigurationAsync(
        string applicationUri, byte[] data,
        uint currentVersion, CancellationToken ct)
    {
        // Persist with optimistic concurrency check
    }

    public async ValueTask ConfirmUpdateAsync(
        string applicationUri, uint configVersion,
        CancellationToken ct)
    {
        // Mark the configuration as applied
    }
}
```

---

## 4. Roles and Authorization

### GDS Roles (OPC 10000-12 §7.2)

| Role | Purpose |
|------|---------|
| `DiscoveryAdmin` | Register/update/unregister any application |
| `CertificateAuthorityAdmin` | Manage certificate requests and trust lists |
| `RegistrationAuthorityAdmin` | Approve/reject certificate requests |
| `ApplicationSelfAdmin` | Manage own application's registration |
| `ApplicationAdmin` | Manage a configured set of applications |

### ApplicationSelfAdmin Privilege

`GdsApplicationSelfAdminProvider` grants `GdsRole.ApplicationSelfAdmin`
when the secure-channel client certificate matches a certificate stored
for the registered `ApplicationRecordDataType` found by the channel
ApplicationUri. The provider is an `IIdentityAugmenter`, so it layers
SelfAdmin on top of any accepted identity instead of using a custom
`ImpersonateUser` callback.

For dependency-injection-hosted GDS servers the provider is registered with the symmetric
builder extension:

```csharp
services.AddOpcUa()
    .AddGdsServer(opt => opt.ApplicationName = "MyGds")
    .AddDefaultIdentityAuthenticators(opt => opt.EnableJwt = false)
    .AddGdsApplicationSelfAdminProvider();
```

`AddDefaultIdentityAuthenticators(...)` on `IGdsServerBuilder` also
adds this provider so standard GDS hosts get OPC 10000-12 §7.2
SelfAdmin behavior automatically.

### ApplicationAdmin Privilege

The `ApplicationAdmin` role allows a user to administer a specific
set of applications (not all applications like `DiscoveryAdmin`).

**How it works:**

1. Assign the `GdsRole.ApplicationAdmin` role to the user via
   `IUserDatabase.CreateUser()`
2. Implement `IGdsUserDatabase.GetAdministeredApplicationIds()` to
   return the `ApplicationId`s the user may administer
3. During username authentication, `GlobalDiscoverySampleServer` constructs a
   `GdsRoleBasedIdentity` with `AdministeredApplicationIds` populated
4. `AuthorizationHelper.CheckApplicationAdminPrivilege()` verifies
   the target application is in the user's administered set

```csharp
// Setup: create a user with ApplicationAdmin role
userDb.CreateUser("app-admin", passwordBytes,
    [GdsRole.ApplicationAdmin]);

// If using IGdsUserDatabase:
((MyGdsUserDatabase)userDb).GrantApplicationAdmin(
    "app-admin",
    [registeredAppId1, registeredAppId2]);
```

### Security: Fail-Closed Channel Validation

`AuthorizationHelper.HasAuthenticatedSecureChannel()` enforces that
all GDS methods are called over a signed (or encrypted) secure
channel. The check **fails closed** — if the system context cannot
be validated, the method throws `BadSecurityModeInsufficient`.

---

## 5. End-to-End Example

Complete example: register an application, get a certificate, push
it to the server.

```csharp
// ── 1. Start the GDS Server ─────────────────────────

var database = JsonApplicationsDatabase.Load("apps.json", telemetry);
var certGroup = new CertificateGroup(authStorePath, certGroupConfigs);
var userDb = JsonUserDatabase.Load("users.json", telemetry);

var gds = new GlobalDiscoverySampleServer(
    database, database, certGroup, userDb, telemetry);

var app = new ApplicationInstance(telemetry) { ... };
await app.StartAsync(gds);

// ── 2. Client: Register and Get Certificate ─────────

var gdsClient = new GlobalDiscoveryServerClient(clientConfig);
gdsClient.AdminCredentials = new UserIdentity("admin", "password");
await gdsClient.ConnectAsync("opc.tcp://localhost:58810");

// Register
var record = new ApplicationRecordDataType
{
    ApplicationUri = "urn:my-server",
    ApplicationType = ApplicationType.Server,
    ApplicationNames = new[] { new LocalizedText("My Server") }.ToArrayOf(),
    DiscoveryUrls = new[] { "opc.tcp://my-server:4840" }.ToArrayOf()
};
NodeId appId = await gdsClient.RegisterApplicationAsync(record);

// Request certificate
NodeId reqId = await gdsClient.StartNewKeyPairRequestAsync(
    appId, default,
    ObjectTypeIds.RsaSha256ApplicationCertificateType,
    "CN=My Server,O=My Org",
    new[] { "my-server", "localhost" }.ToArrayOf(),
    "PFX", null);

// Wait for completion
ByteString cert, key;
ArrayOf<ByteString> issuers;
do
{
    await Task.Delay(2000);
    (cert, key, issuers) =
        await gdsClient.FinishRequestAsync(reqId, default);
} while (cert.IsEmpty);

// ── 3. Push Certificate to Target Server ────────────

var pushClient = new ServerPushConfigurationClient(clientConfig);
await pushClient.ConnectAsync(serverEndpoint);

bool needsRestart = await pushClient.UpdateCertificateAsync(
    pushClient.DefaultApplicationGroup,
    ObjectTypeIds.RsaSha256ApplicationCertificateType,
    cert, "PFX", key, issuers);

if (needsRestart)
{
    await pushClient.ApplyChangesAsync();
}

// ── 4. Clean Up ─────────────────────────────────────

await pushClient.DisconnectAsync();
await gdsClient.DisconnectAsync();
```

---

## Conformance Matrix

Status key: ✅ Implemented · ⚠️ Partial · 🔲 Optional, provider-gated (exposed only when configured) · ❌ Not implemented · N/A Not applicable

This matrix links every implemented OPC 10000-12 (Part 12) requirement in the ServerConfiguration / Push Certificate Management, TrustList, certificate-alarm, KeyCredentialService and AuthorizationService areas to the source that implements it and to the automated tests that exercise it. Clause numbers are **OPC 10000-12 v1.05.07**. When Part 12 behaviour changes, keep the matrix truthful: every new Method, alarm or optional child gets a row with its source *and* at least one test, and any behaviour that regresses to partial/unsupported is downgraded here. The push/transaction behaviour summarised below is documented in depth in [CertificateManager.md → PushManagement Transactions](CertificateManager.md#pushmanagement-transactions-opc-ua-part-12-7102-71011).

### Applicable Part 12 profiles and conformance units

The server-side Part 12 features map to these Facets in the OPC UA profile registry (`profiles.opcfoundation.org`, profile group **UACore 1.05**). Conformance units are marked (M)andatory or (O)ptional as declared by the Facet.

| Facet (UACore 1.05) | Key conformance units | Repo status |
|---------------------|-----------------------|-------------|
| Global Certificate Management Server Facet (`Server/GlobalCertificateManagement`) | *Pull or Push Model* (M); *Push Model for Global Certificate and TrustList Management* (O); *Pull Model for Global Certificate and TrustList Management* (O) | ✅ Push model (`ServerConfiguration`); ✅ Pull model (GDS `CertificateDirectory`) |
| A & C CertificateExpiration 2022 Server Facet (`Server/ACCertificateExpiration2022`) | *A & C CertificateExpiration*, *A & C Alarm*, *A & C Acknowledge* (M); *A & C Comment / Confirm / Shelving* (O) | ✅ mandatory CUs; optional Comment/Confirm/Shelving inherited from the base condition model |
| KeyCredential Service Server Facet (`Server/KeyCredentialManagement`) | *Push Model for KeyCredential Service*, *KeyCredential Authentication Mechanism Support* (M); *ProfileURI – MQTT UserName / AMQP SASL Plain / UA transport UserName* (O) | ✅ push model + client proxy; ProfileURIs are provider-defined |
| Global Service Authorization Request Server Facet (`Server/GlobalServiceAuthorization`) | *GDS Authorization Service Server* (M) | ✅ token issuance (`AuthorizationServiceManager`) |
| Authorization Service Server Facet (`Server/AuthorizationServiceConfiguration`) | *Authorization Service Configuration Server* (M) | ⚠️ access-token validation is provided through pluggable [Identity Providers](IdentityProviders.md); the Part 12 `ServerConfiguration.AuthorizationServices` configuration folder is not exposed |
| Global Discovery and Certificate Mgmt 2022 Server (`Server/GlobalDiscoveryAndCertificateManagement2022`) | *GDS Application Directory*, *GDS Certificate Manager Pull Model*, *GDS Query Applications*, *GDS LDS-ME Connectivity* (M) | ✅ full GDS server |

### Automated verification

The automated conformance checks that run in CI are the NUnit suites: the unit suites in `Opc.Ua.Server.Tests` (address-space exposure, transaction coordinator, TrustList staging, certificate alarms, optional surface) and the end-to-end GDS integration suites in `Opc.Ua.Gds.Tests` — notably `PushTest.cs`, which drives a live push server through the `ServerConfiguration` Methods, and `TrustListValidationTest.cs`. The applicable Facets / conformance units above were identified with the OPC UA profile-graph tooling against `profiles.opcfoundation.org`.

There is no fully-automated CTT harness in this repository, and the formal **OPC UA Compliance Test Tool (UACTT / CTT)** — a licensed OPC Foundation corporate-member GUI application — was **not** run to produce this matrix; no CTT results are claimed here. Run it against the reference server (`samples/Reference/ConsoleReferenceServer`; see [samples/README.md](../samples/README.md)) for formal certification. Known UACTT *script* defects encountered in prior manual runs are catalogued in [`plans/ctt-issues.md`](../plans/ctt-issues.md).

## GDS Directory (§6.5)

| Section | Feature | Status | Source | Tests |
|---------|---------|--------|--------|-------|
| §6.5.3 | DirectoryType | ✅ | `ApplicationsNodeManager.cs` | `ClientTest.cs`, `GdsApplicationDirectoryTests.cs` |
| §6.5.4 | FindApplications | ✅ | `OnFindApplications` (any Client, §6.5.3) | `GdsApplicationDirectoryTests.cs`, `ClientTest.FindApplicationsAsSelfAdminAsync` |
| §6.5.5 | ApplicationRecordDataType / rcp+ rules | ✅ | `ApplicationsDatabaseBase.ValidateApplication` | `ApplicationsDatabaseBaseTests.cs`, `LinqApplicationsDatabaseTests.cs` |
| §6.5.6 | RegisterApplication | ✅ | `OnRegisterApplication` + `DiscoveryAdminOrAppAdmin` | `GdsApplicationDirectoryTests.cs`, `RegisteredApplicationTests.cs` |
| §6.5.7 | UpdateApplication | ✅ | `OnUpdateApplication` + `DiscoveryAdminOrSelfAdminOrAppAdmin` | `GdsApplicationDirectoryTests.cs` |
| §6.5.8 | UnregisterApplication | ✅ | `OnUnregisterApplicationAsync` + cert revocation | `GdsApplicationDirectoryTests.cs`, `ClientTest.cs` |
| §6.5.9 | GetApplication | ✅ | `OnGetApplication` | `GdsApplicationDirectoryTests.cs` |
| §6.5.10 | QueryApplications | ✅ | `OnQueryApplications` | `GdsQueryApplicationsTests.cs` |
| §6.5.11 | QueryServers | ✅ | `OnQueryServers` | `GdsQueryApplicationsTests.cs`, `ClientTest.cs` |
| §6.5.12 | ApplicationRegistrationChangedAuditEvent | ✅ | `AuditEvents.ReportApplicationRegistrationChangedAuditEvent` | `AuditEventsTests.cs` |

## Certificate Management — Pull Model (§7.9)

Pull Certificate Management moved from §7.6 (1.04) to **§7.9** (v1.05.07); the `CertificateDirectoryType` Methods below use the current clause numbers.

| Section | Feature | Status | Source | Tests |
|---------|---------|--------|--------|-------|
| §7.9.3 | StartSigningRequest | ✅ | `OnStartSigningRequestAsync` + CSR key / CertificateType check + audit | `GdsCertificateManagementTests.cs`, `CertificateGroupSigningRequestTests.cs`, `ClientTest.cs` |
| §7.9.4 | StartNewKeyPairRequest | ✅ | `OnStartNewKeyPairRequest` + audit | `GdsCertificateManagementTests.cs`, `ClientTest.cs` |
| §7.9.5 | FinishRequest | ✅ | `OnFinishRequestAsync` + issuer chain | `GdsCertificateManagementTests.cs`, `ClientTest.cs` |
| §7.9.6 | RevokeCertificate | ✅ | `OnRevokeCertificateAsync` + audit | `GdsCertificateManagementTests.cs`, `CertificateGroupTests.cs` |
| §7.9.7 | GetCertificateGroups | ✅ | `OnGetCertificateGroups` | `ClientTest.cs`, `CustomCertificateGroupIntegrationTest.cs` |
| §7.9.8 | GetCertificates | ✅ | `OnGetCertificates` | `GdsCertificateManagementTests.cs` |
| §7.9.9 | GetTrustList | ✅ | `OnGetTrustList` | `ClientTest.cs`, `CustomCertificateGroupIntegrationTest.cs` |
| §7.9.10 | GetCertificateStatus | ✅ | `OnGetCertificateStatus` | `ClientTest.cs` |
| §7.9.11 | CheckRevocationStatus | ✅ | `OnCheckRevocationStatusAsync` + ValidityTime | `GdsCertificateManagementTests.cs` |

`CheckRevocationStatus` uses locally configured issuer chains and verified local
CRLs. Caller-supplied AIA URLs do not trigger certificate or CRL downloads.
Incomplete or untrusted chains fail validation, and unavailable revocation
evidence is reported as unknown rather than accepted. Provision the complete
issuer chain and applicable CRLs in the configured stores, including on `net48`,
whose explicit-path validation does not call `X509Chain.Build`.

## Roles and Privileges (§7.2)

| Feature | Status | Source | Tests |
|---------|--------|--------|-------|
| DiscoveryAdmin | ✅ | `GdsRole.DiscoveryAdmin` | `AuthorizationHelperTests.HasAuthorizationSucceedsWhenUserHasDiscoveryAdminRole` |
| CertificateAuthorityAdmin | ✅ | `GdsRole.CertificateAuthorityAdmin` | `AuthorizationHelperTests.HasAuthorizationSucceedsWithCertificateAuthorityAdminRole` |
| RegistrationAuthorityAdmin | ✅ | `GdsRole.RegistrationAuthorityAdmin` | `AuthorizationHelperTests.cs` |
| ApplicationSelfAdmin | ✅ | `GdsRole.ApplicationSelfAdmin` | `AuthorizationHelperTests.HasAuthorizationSucceedsWithSelfAdminForOwnApplication`, `...ThrowsWithSelfAdminForDifferentApplication` |
| ApplicationAdmin | ✅ | `GdsRole.ApplicationAdmin` + `AdministeredApplicationIds` | `AuthorizationHelperTests.cs`, `GdsApplicationSelfAdminProviderTests.cs` |
| BadSecurityModeInsufficient (channel) | ✅ | `AuthorizationHelper.HasAuthenticatedSecureChannel` | `AuthorizationHelperTests.cs` (asserts `BadSecurityModeInsufficient`), `RoleAuthorizationGateTests.cs` |

## Push Management — ServerConfiguration (§7.10)

`ConfigurationNodeManager` implements the full push transaction model: every certificate/TrustList Method **stages** work that only takes effect at `ApplyChanges`. Clause numbers are corrected to v1.05.07 (the 1.04 numbering was §7.7.x).

| Section | Feature | Status | Source | Tests |
|---------|---------|--------|--------|-------|
| §7.10.5 | UpdateCertificate | ✅ | `ConfigurationNodeManager.UpdateCertificateAsync` (staged; ApplicationCertificate TrustList validation; supplied issuers ignored for app groups) | `ConfigurationNodeManagerPushTests` (`UpdateCertificateWith*`, `...IgnoresSuppliedIssuerCertificatesAsync`), `PushTest` (`UpdateCertificate*`) |
| §7.10.6 | CreateSelfSignedCertificate | ✅ | `...CreateSelfSignedCertificateAsync` (occupied slot → `Bad_InvalidState`; subject/SAN/key-size validation) | `ConfigurationNodeManagerPushValidationTests` (`CreateDefaultApplicationCertificate*`, `ValidateKeySize*`), `ConfigurationNodeManagerPushTests` (`CreateSelfSignedCertificate*`) |
| §7.10.7 | DeleteCertificate | ✅ | `...DeleteCertificateAsync` — empty slot → `Bad_InvalidState`; endpoint-reference check enforced at `ApplyChanges` | `ConfigurationNodeManagerPushTests` (`DeleteCertificate*`, `DeleteCertificateReferencedByEndpointIsRejectedByApplyChangesAsync`, `RotatedCertificateBecomesEndpointReferenced*`) |
| §7.10.8 | GetCertificates | ✅ | `...GetCertificates` (null group → `DefaultApplicationGroup`; occupied slots only, aligned pairs) | `ConfigurationNodeManagerPushValidationTests.SelectOccupiedCertificateSlotsOmitsEmptySlotsAndPreservesOrder`, `ConfigurationNodeManagerPushTests.GetCertificates*`, `PushTest.GetCertificatesAsync` |
| §7.10.9 | ApplyChanges | ✅ | `...ApplyChanges` (ordered commit + reverse rollback) | `PushConfigurationTransactionCoordinatorTests` (`ApplyChanges*`), `PushTest` (`ApplyChangesAsync`, `ApplyChangesForcesChannelRenegotiateAsync`) |
| §7.10.10 | CreateSigningRequest | ✅ | `...CreateSigningRequestAsync` (nonce ≥ 32 bytes mixed in) | `ConfigurationNodeManagerPushTests` (`CreateSigningRequest*`, `...WithRegenerateAndShortNonceThrowsBadInvalidArgument`), `PushTest.CreateSigningRequest*` |
| §7.10.11 | CancelChanges | ✅ | `...CancelChanges` (discard; `Bad_RequestCancelledByClient` diagnostics) | `PushConfigurationTransactionCoordinatorTests.CancelChanges*`, `ConfigurationNodeManagerPushTests.CancelChanges*` |
| §7.10.12 | GetRejectedList | ✅ | `...GetRejectedList` | `ConfigurationNodeManagerPushTests.GetRejectedList*`, `PushTest.GetRejectedListAsync` |

Per-Method SecureChannel/Role enforcement (`Bad_SecurityModeInsufficient` for the channel vs `Bad_UserAccessDenied` for the Role), the §7.10.10 `Nonce` entropy requirement, and the §7.10.7 endpoint-reference rule are described in [CertificateManager.md → PushManagement Transactions → *Method security and validation*](CertificateManager.md#pushmanagement-transactions-opc-ua-part-12-7102-71011).

### Transaction model, diagnostics and lifecycle (§7.10.2, §7.10.9, §7.10.11, §7.10.17)

| Requirement | Status | Source | Tests |
|-------------|--------|--------|-------|
| §7.10.2 transaction lifecycle: single owning Session; other Sessions get `Bad_TransactionPending`; ownership cleared atomically on apply | ✅ | `PushConfigurationTransactionCoordinator` | `PushConfigurationTransactionCoordinatorTests` (`StageStartsTransactionOwnedByStagingSession`, `StageFromAnotherSessionThrowsBadTransactionPending`, `ApplyChangesClearsOwnershipAtomicallySoANewSessionCanImmediatelyStageAsync`) |
| §7.10.2 `SupportsTransactions = true` exposed | ✅ | `ConfigurationNodeManager.CreateServerConfiguration` (the manager is split into `ConfigurationNodeManager.*.cs` concern files) | `ConfigurationNodeManagerPushTests.SupportsTransactionsIsExposedAndTrue` |
| Staging blocked during an in-flight commit (`Bad_InvalidState` same-owner / `Bad_TransactionPending` other Session) | ✅ | coordinator commit guard | `PushConfigurationTransactionCoordinatorTests` (`StageFromOwningSessionDuringInFlightCommitReturnsBadInvalidStateThenSucceedsAsync`, `StagingFromAnotherSessionDuringAnInFlightCommit...`) |
| §7.10.9 ordered commit, reverse-order rollback, `Bad_NothingToDo`, open-writer `Bad_InvalidState` | ✅ | coordinator prepare/commit/compensate | `PushConfigurationTransactionCoordinatorTests` (`ApplyChangesCommitsOperationsInRequestOrderAsync`, `ApplyChangesWithFailingCommitRollsBackEarlierOperationsInReverseOrderAsync`, `ApplyChangesWithNoActiveTransactionReturnsBadNothingToDoAsync`, `ApplyChangesWithOpenTrustListWriterReturnsBadInvalidStateWithoutClearingTransactionAsync`) |
| §7.10.11 wrong-Session apply/cancel → `Bad_SessionIdInvalid`; Session-close cancels its transaction | ✅ | coordinator ownership checks | `PushConfigurationTransactionCoordinatorTests` (`ApplyChangesFromWrongSessionReturnsBadSessionIdInvalidAsync`, `CancelChangesFromWrongSessionReturnsBadSessionIdInvalid`, `CancelForSessionClose*`) |
| §7.10.17 `TransactionDiagnostics` DataValue status: `Bad_OutOfService` before any transaction, `Bad_InvalidState` while active, `Good` after completion | ✅ | `ConfigurationNodeManager` diagnostics binding + coordinator snapshot | `ConfigurationNodeManagerPushTests` (`TransactionDiagnosticsReportBadOutOfServiceBeforeAnyTransactionAsync`, `...BadInvalidStateWhileTransactionActiveAsync`, `...GoodResultAfterCompletedTransactionAsync`), `PushConfigurationTransactionCoordinatorTests.GetSnapshot*` |
| §7.10.10 nonce entropy mixed into regenerated key (HMAC-DRBG) | ✅ | `AdditionalEntropyCertificateKeyGenerator` | `AdditionalEntropyCertificateKeyGeneratorTests.cs` |
| §7.10.10 regenerated private key persisted across Sessions and restart | ✅ | `DirectoryPendingCertificateKeyStore` / `InMemoryPendingCertificateKeyStore` | `PendingCertificateKeyStoreTests.cs` |
| Coordinator scoped per server (no cross-server sharing); injected coordinator honored | ✅ | `ConfigurationNodeManager` private per-server default | `ConfigurationNodeManagerPushTests` (`EachConfigurationNodeManagerOwnsADistinctDefaultCoordinator`, `AnInjectedCoordinatorIsSharedAcrossConfigurationNodeManagers`) |
| Deferred post-apply effects drained/cancelled on shutdown | ✅ | `ConfigurationNodeManager.DeleteAddressSpaceAsync` / `Dispose` | `ConfigurationNodeManagerShutdownTests.cs` |

### Optional ServerConfiguration surface (§7.10.13, §7.10.14, §7.10.20)

These optional members are exposed **only when their backing option/provider is configured** (see [CertificateManager.md → Optional ServerConfiguration Surface](CertificateManager.md#optional-serverconfiguration-surface-opc-ua-part-12-7103-71013-71020)); otherwise the node is suppressed.

| Member (§) | Status | Source | Tests |
|------------|--------|--------|-------|
| Identity variables `ApplicationUri` / `ProductUri` / `ApplicationType` / `ApplicationNames` (§7.10.3) | ✅ always | `ServerConfigurationOptions` from `ApplicationConfiguration` | `ServerConfigurationSurfaceTests.IdentityPropertiesAlwaysExposedAndValuedAsync` |
| `HasSecureElement` / `InApplicationSetup` (§7.10.3) | 🔲 provider-gated | `ServerConfigurationOptions` | `ServerConfigurationSurfaceTests` (`HasSecureElementExposedWithValueWhenConfiguredAsync`, `InApplicationSetupExposedWithValueWhenConfiguredAsync`, `OptionalMembersSuppressedWhenNotConfiguredAsync`) |
| `ResetToServerDefaults` (§7.10.13) | 🔲 provider-gated | `IServerConfigurationResetProvider` | `ServerConfigurationSurfaceTests.ResetToServerDefaultsExposedOnlyWhenProviderConfiguredAsync`, `ServerConfigurationResetTests.cs` |
| `ConfigurationFile` / `ApplicationConfigurationFileType` incl. `CloseAndUpdate` / `ConfirmUpdate` (§7.10.20) | 🔲 provider-gated | `IApplicationConfigurationFileProvider` + `ApplicationConfigurationFile.cs` | `ServerConfigurationSurfaceTests` (`ConfigurationFileExposedWithChildrenWhenProviderConfiguredAsync`, `ConfigurationFilePropertiesSeededFromProviderAsync`, `ConfigurationFileReadReturnsProviderContentAsync`) |

### GDS-managed applications proxy (§7.10.14–§7.10.16)

The GDS-side proxy that exposes *other* applications' configurations under a `ManagedApplications` folder is a separate, partially-implemented feature (out of scope for a self-managed push Server).

| Section | Feature | Status | Source |
|---------|---------|--------|--------|
| §7.10.14 | `ApplicationConfigurationType` | ⚠️ | `DefaultManagedApplicationsNodeManager` creates `ApplicationConfigurationState` nodes; `IConfigurationDataStore` abstracts persistence |
| §7.10.16 | `ManagedApplications` folder | ⚠️ | `DefaultManagedApplicationsNodeManager`; optimistic concurrency via version tracking |

## TrustList (§7.8)

| Section | Feature | Status | Source | Tests |
|---------|---------|--------|--------|-------|
| §7.8.2.2 | TrustListType / Open / OpenWithMasks (Read + Write+EraseExisting only) | ✅ | Core `TrustList.cs` | `TrustListTests.OpenWithMasksSyncAllReturnsGood` |
| §7.8.2.5 | CloseAndUpdate — staged, applied at `ApplyChanges` | ✅ | `TrustList.cs` + coordinator; `TrustListUpdatedAuditEvent` post-commit | `TrustListTransactionTests` (`CloseAndUpdateStagesInsteadOfApplyingImmediatelyAsync`, `...RollsBackWhenALaterOperationFailsAsync`, `...SelfCompensatesWhenOnlyOneOfSeveralStoresFailsAsync`), `PushTest.UpdateTrustListAsync` |
| §7.8.2.6/.7 | AddCertificate / RemoveCertificate — staged | ✅ | `TrustList.cs` | `PushTest` (`AddRemoveCertAsync`, `AddRemoveCATrustedCertAsync`, `AddRemoveCAIssuerCertAsync`), `TrustListTransactionTests.cs` |
| §7.8.2 | Open-for-write blocked with `Bad_TransactionPending` while another Session's transaction is active | ✅ | `TrustList.cs` + coordinator | `TrustListTransactionTests.cs`, `PushConfigurationTransactionCoordinatorTests.ApplyChangesWithOpenTrustListWriter*` |
| §7.8.2 / Part 20 §4.2.2 | Several concurrent read handles; an open while a write handle is open → `Bad_NotReadable`/`Bad_NotWritable` (also for the owning Session); a write open while any handle is open → `Bad_NotWritable` | ✅ | `TrustList.cs` | `TrustListTests` (`OpenForReadSeveralTimesKeepsEveryReadHandle`, `SameSessionOpenWhileItsWriteHandleIsOpenIsRejected`) |
| §7.8.2 | Read/write access control | ✅ | `TrustList.cs` | `TrustListTests` (`OpenReadWithoutReadAccessThrowsBadUserAccessDenied`, `OpenWriteWithoutWriteAccessThrowsBadUserAccessDenied`) |
| §7.8.2 | `LastUpdateTime` set on init + after committed update | ✅ | `TrustList.cs` | `TrustListTransactionTests.cs` |
| §7.8.2 | Writable / UserWritable; DefaultValidationOptions | ✅ | Set to true for GDS groups; generated from model CSV | `TrustListTests.cs` |
| §7.8.2.1 | ActivityTimeout: a handle with no Method call for the node's `ActivityTimeout` (default 60 000 ms) is closed and its changes discarded; every Open/Read/Write restarts it | ✅ | `TrustList.cs` (per-handle timer on the server `TimeProvider`) | `TrustListTests` (`IdleWriteHandleIsClosedAfterActivityTimeout`, `ActivityTimeoutPropertyValueIsHonored`) |
| §8.4.5 | `MaxTrustListSize` advertised honestly + resource-protection safety ceiling; oversize `Write`/`AddCertificate` → `Bad_RequestTooLarge`; `Read` clamped to the server-encoded stream | ✅ | `TrustList.cs` + `ServerConfigurationOptions.MaxTrustListSizeSafetyCeiling` | `TrustListValidationTest` (`NormalSizeTrustListAsync`, `WriteTrustListExceedsSizeLimit`, `TrustListJustUnderLimitAsync`), `ConfigurationNodeManagerPushTests.MaxTrustListSizeAdvertisesHonestFiniteEffectiveLimit` |

### Certificate and TrustList alarms (§7.8.3)

Each `CertificateGroup` exposes the two optional standard alarm instances
with **full active/inactive transitions and events** (previously
property-only). `ConfigurationNodeManager` creates and wires them in
`CreateAddressSpace`. `StandardServer.OnServerStarted` starts periodic
monitoring every 60 s once the subscription infrastructure is ready. The
injected `TimeProvider` supplies all thresholds and timers. See
[CertificateManager.md → Certificate-Expiration and TrustList-Staleness Alarms](CertificateManager.md#certificate-expiration-and-trustlist-staleness-alarms-opc-ua-part-12-783).

| Alarm | Status | Source | Tests |
|-------|--------|--------|-------|
| `CertificateExpirationAlarmType` (`CertificateExpired`) — active within `ExpirationLimit` / expired, Medium/High severity, `Retain`, Acknowledge, cleared after certificate replacement | ✅ | `CertificateGroupAlarmMonitor` + `CertificateAlarmScheduler.UpdateAndEvaluate` / `StartAlarmMonitoring` | `CertificateAlarmMonitoringTests` (`CertificateExpiredActivatesWithMediumSeverity*`, `...HighSeverityWhenAlreadyExpired`, `...DeactivatesAfterCertificateReplacement`, `RepeatedEvaluationsDoNotEmitDuplicateTransitionEvents`, `AcknowledgedAlarmClearsRetainAfterDeactivation`, `ClientAcknowledgeMethodMarksAlarmAcknowledged`) |
| `TrustListOutOfDateAlarmType` (`TrustListOutOfDate`) — active when `UpdateFrequency` elapses, disabled when non-positive | ✅ | same | `CertificateAlarmMonitoringTests` (`TrustListOutOfDateActivatesWhenStale`, `TrustListOutOfDateStaysInactiveWhenFresh`, `TrustListOutOfDateDisabledWhenUpdateFrequencyIsZero`) |

### TrustList-change effects on channels and Sessions (§7.10.9)

| Requirement | Status | Source | Tests |
|-------------|--------|--------|-------|
| Committed TrustList change forces SecureChannel renegotiation (application group) and revalidates certificate user identities / closes invalidated Sessions (user-token group) | ✅ | `PushConfigurationTrustListEffectHandler` + `ConfigurationNodeManager` | `ConfigurationNodeManagerTrustListEffectsTests` (`BuildTrustListEffects*`, `ApplyChangesDispatches*EffectFor*`, `ApplyChangesUsesCommittedTargetsNotAConcurrentSessionsStagedTargetsAsync`), `PushConfigurationTrustListEffectHandlerTests.cs` |
| Origin-independent enforcement: any `TrustListUpdated`/`CrlUpdated` on the certificate manager (out-of-band store write, `WriteTrustListAsync`, `ITrustListTransaction` commit) triggers the same effect fan-out; a host running the legacy non-transactional TrustList must OPT IN by calling `SetTrustListChangeNotifier` — nothing in the stack wires it automatically | ✅ | `CertificateChangePump` consumed by `ConfigurationNodeManager` | `ConfigurationNodeManagerTrustListEffectsTests` (`OutOfBand*`, `BuildTrustListEffectsForScopes*`), `CertificateChangePumpTests` |
| A CRL pushed through `CloseAndUpdate` + `ApplyChanges` disconnects a connected client whose certificate it revokes, without waiting for the security-token renewal | ✅ | commit → validator-cache invalidation → §7.10.9 sweep | `PushTest` (`PushedCrlDisconnectsRevokedClientAsync`), `ConfigurationNodeManagerTrustListEffectsTests` (`PushedCrlIsEnforcedByTheLiveValidatorAfterApplyChangesAsync`), `CertificateValidationCoreTests` (`ValidatedCertificateFastPathDoesNotSkipRevocationAsync`) |

## Audit Events

| Event Type | Status | Source | Tests |
|------------|--------|--------|-------|
| ApplicationRegistrationChangedAuditEvent | ✅ | Register/Update/Unregister | `AuditEventsTests.cs` |
| CertificateRequestedAuditEvent | ✅ | StartNewKeyPair + StartSigning | `AuditEventsTests.cs` |
| CertificateDeliveredAuditEvent | ✅ | FinishRequest | `AuditEventsTests.cs` |
| CertificateRevokedAuditEvent | ✅ | RevokeCertificate | `AuditEventsTests.cs` |
| CertificateUpdateRequestedAuditEvent | ✅ | UpdateCertificate (push) | `AuditEventsTests.cs`, `PushTest.cs` |
| TrustListUpdatedAuditEvent | ✅ | Core TrustList.cs | `TrustListTransactionTests.cs`, `PushTest.cs` |
| KeyCredentialRequestedAuditEvent | ✅ | StartRequest | `AuditEventsTests.cs` |
| KeyCredentialDeliveredAuditEvent | ✅ | FinishRequest | `AuditEventsTests.cs` |
| KeyCredentialRevokedAuditEvent | ✅ | Revoke | `AuditEventsTests.cs` |
| AccessTokenIssuedAuditEvent | ✅ | RequestAccessToken | `AuthorizationService/RefreshTokenTests.cs` |
| Secrets redacted from audit payloads | ✅ | RedactedPrivateKeyPassword / RedactedPrivateKey | `AuditEventsTests.cs` |

## KeyCredentialService (§8)

| Feature | Status | Source | Tests |
|---------|--------|--------|-------|
| StartRequest | ✅ | `OnKeyCredentialStartRequest` | `KeyCredentialRequestStoreTests.StartRequestReturnsNonNullIdAsync` |
| FinishRequest | ✅ | `OnKeyCredentialFinishRequest` | `KeyCredentialRequestStoreTests` (`FinishRequestReturnsCredentialAsync`, `FinishRequestWithCancelRejectsRequestAsync`, `FinishRequestWithUnknownIdThrows`) |
| Revoke | ✅ | `OnKeyCredentialRevoke` | `KeyCredentialRequestStoreTests` (`RevokeMarksCredentialAsRejectedAsync`, `RevokeUnknownCredentialThrows`) |
| InMemoryKeyCredentialRequestStore | ✅ | `IKeyCredentialRequestStore.cs` | `KeyCredentialRequestStoreTests.cs` |
| Client proxy | ✅ | `KeyCredentialServiceClient.cs` | `KeyCredentialRequestStoreTests.cs` |
| KeyCredential Push binding | ✅ | `WithKeyCredentialPush` / `KeyCredentialPushSubject` | `ConfigurationNodeManagerPushTests` (`BindKeyCredentialPushWithExistingFolderBindsSubjectAsync`, `BindKeyCredentialPushWithNullSubjectThrowsArgumentNullException`) |

## AuthorizationService (§9)

| Feature | Status | Source | Tests |
|---------|--------|--------|-------|
| GetServiceDescription | ✅ | `OnGetServiceDescription` | `AuthorizationService/RefreshTokenTests.GetServiceDescriptionReportsConfiguredServiceUri` |
| RequestAccessToken (obsolete single-call) | ✅ | Legacy compatibility path via `IAccessTokenProvider.RequestAccessTokenAsync` | `AuthorizationService/StartRequestTokenTests.LegacyRequestAccessTokenIssuesAnonymousUnprivilegedToken` |
| StartRequestToken (RC) | ✅ | `OnStartRequestToken` / `AuthorizationServiceManager` | `AuthorizationService/StartRequestTokenTests.cs` |
| FinishRequestToken (RC) | ✅ | `OnFinishRequestToken` / `AuthorizationServiceManager` | `AuthorizationService/StartRequestTokenTests.cs` |
| RefreshToken (RC) | ✅ | `OnRefreshTokenAsync` / `AuthorizationServiceManager` | `AuthorizationService/RefreshTokenTests.cs`, `AuthorizationService/InMemoryAccessTokenProviderRefreshTests.cs` |
| SupportedRoles (RC) | ✅ | Model property exposed | — (model property; not separately unit-tested) |
| Client proxy | ✅ | `AuthorizationServiceClient.cs` | `AuthorizationService/StartRequestTokenTests.cs`, `AuthorizationService/RefreshTokenTests.cs` |

### Refresh tokens

The default in-memory AuthorizationService provider issues a refresh token from
`FinishRequestToken` and accepts it through `RefreshToken` (OPC 10000-12 §9.7). Refresh
tokens are opaque, single-use secrets: each successful refresh consumes the current token,
issues a new access token, and rotates to a new refresh token. The refresh-token lifetime
slides by `AuthorizationServiceOptions.DefaultRefreshTokenLifetime` (7 days by default).
Set `AuthorizationServiceOptions.EnableRefreshTokens = false` to preserve the legacy
behavior where `FinishRequestToken` returns no refresh token and `RefreshToken` returns
`Bad_NotSupported`.

```csharp
var (newJwt, newJwtExpiresAt, newRefreshToken, newRefreshExpiresAt) =
    await authClient.RefreshTokenAsync(resourceId, refreshToken);

refreshToken = newRefreshToken;
```

Refresh-token audit events include only the `resourceId` input and the outcome. The
refresh token itself is a secret and is never placed in audit payloads.

## Remaining optional / unsupported Part 12 items

The following optional Part 12 members are **not** implemented (or only partially). None are required for the mandatory conformance units of the applicable Facets above; they are listed here so the matrix stays honest.

| Item (§) | Status | Notes |
|----------|--------|-------|
| `ServerConfiguration.AuthorizationServices` folder (§9.6.2 / *Authorization Service Configuration Server* CU) | ❌ not exposed | A push Server does not expose the configuration folder listing trusted Authorization Services. Client access-token validation is instead handled by the pluggable [Identity Providers](IdentityProviders.md). |
| `ManagedApplications` / `ApplicationConfigurationType` proxy (§7.10.14–§7.10.16) | ⚠️ partial | `DefaultManagedApplicationsNodeManager` creates the nodes with an `IConfigurationDataStore`; the full GDS-as-proxy update/confirm flow to *remote* managed applications is not complete. |
| KeyCredential authentication `ProfileUris` — AMQP SASL Plain / MQTT UserName / UA transport UserName (§8, optional CUs) | 🔲 provider-defined | The push binding is implemented; the concrete ProfileUri set advertised is defined by the credential provider, not fixed by the stack. |
| A & C CertificateExpiration optional CUs — *Comment* / *Confirm* / *Shelving* | 🔲 inherited | Available through the standard OPC 10000-9 condition model on the alarm instances; not separately exercised by the certificate-alarm tests. |
| Onboarding / OPC 10000-21 device setup interplay with `ResetToServerDefaults` / `InApplicationSetup` (§7.10.13, Annex G) | ⚠️ partial | Ticket administration, signing, ticket-scoped identity trust, composite-aware selection, and registrar runtime bindings are implemented. GDS issuance integration, durable lifecycle storage, and complete pull/push device workflows remain incomplete. |

## LDS / LDS-ME (§4–5)

Semaphore files are local-host liveness indicators (OPC UA Part 12). `RegisterServer`
and `RegisterServer2` accept a non-empty `SemaphoreFilePath` only from a
transport-observed loopback peer (including IPv4-mapped loopback). Remote peers
and transports without peer-address evidence must omit the path and periodically
refresh their registration. A certificate URI or a discovery URL naming localhost
is not evidence of locality.

Paths must be fully qualified native file paths. UNC paths (including mixed or
forward slashes), Windows device/extended namespaces, drive-relative paths,
URIs, control characters, alternate streams, reserved DOS device names, and
ambiguous dot/space components are rejected with `BadInvalidArgument` before
filesystem access, including offline requests. The store independently checks
paths before registration and pruning; unsafe entries loaded or seeded into the
store are pruned without probing their paths. The built-in store is in-memory;
there is no registration persistence loader.

Ordinary existing local semaphore files retain their liveness behavior: a missing
file removes the registration. The filesystem can be injected through
`RegisteredServerStore` or LDS hosting's `IFileSystem` service. Path validation is
lexical, not a filesystem sandbox: administrators must keep semaphore directories,
drive mappings, mounts, and links local and trusted. Local applications connecting
through a non-loopback interface must reconnect through loopback to use semaphores.

| Feature | Status | Source | Tests |
|---------|--------|--------|-------|
| FindServers | ✅ | `LdsServer.FindServersAsync` | `LocalDiscoveryTests.cs` |
| GetEndpoints | ✅ | `LdsServer.GetEndpointsAsync` | `LocalDiscoveryTests.cs` |
| RegisterServer | ✅ | `LdsServer.RegisterServerAsync` | `LocalDiscoveryTests.cs` |
| RegisterServer2 | ✅ | `LdsServer.RegisterServer2Async` | `LocalDiscoveryTests.cs` |
| Local semaphore path and peer validation | ✅ | `LdsServer.ValidateRegistration`, `RegisteredServerStore` | `RegistrationValidationTests.cs`, `SemaphorePathTests.cs` |
| FindServersOnNetwork | ✅ | `LdsServer.FindServersOnNetworkAsync` | `LocalDiscoveryTests.cs`, `LdsServerStaticTests.cs` |
| LDS-ME capability | ✅ | `ComputeServerCapabilities` | `ServerCapabilitiesTests.cs`, `ServerCapabilityTests.cs` |
| mDNS _opcua-tcp._tcp | ✅ | `MulticastDiscovery.OpcUaServiceType` | `LdsServerStaticTests.cs` |
| rcp+ reverse-connect | ✅ | `MulticastDiscovery.ReverseConnectScheme` | `LdsServerStaticTests.cs` |
| Annex C TXT keys | ✅ | path / caps / rc | `LdsServerStaticTests.cs` |
| Annex D identifiers | ✅ | LDS / LDS-ME | `LdsServerStaticTests.cs` |

## Extension Points (Phase 2 Abstractions)

Every extension point a user needs to provide or override:

| Interface | Purpose | Default impl | User replaces when... |
|-----------|---------|-------------|----------------------|
| `IApplicationsDatabase` | Application registration store | `LinqApplicationsDatabase` / `JsonApplicationsDatabase` | Using a SQL/NoSQL database |
| `ICertificateRequest` | Cert request lifecycle | `LinqApplicationsDatabase` | Custom approval workflow |
| `ICertificateGroup` | CA operations | `CertificateGroup` | External CA (EST, ACME, etc.) |
| `IKeyCredentialRequestStore` | KeyCredential lifecycle | `InMemoryKeyCredentialRequestStore` | Persistent store, external IdP |
| `ISecretStore` | Secret storage for credentials | `InMemorySecretStore` | Key Vault, DPAPI, K8s secrets |
| `IAccessTokenProvider` | Token issuance for AuthorizationService | `null` (Bad_NotSupported) | OAuth2/JWT provider |
| `IConfigurationDataStore` | ManagedApplications config persistence | `InMemoryConfigurationDataStore` | File-system, database |
| `IManagedApplicationsNodeManager` | ManagedApplications folder | `StubManagedApplicationsNodeManager` / `DefaultManagedApplicationsNodeManager` | Full push management |
| `IConfigurationNodeManager` | Alarm monitoring | `ConfigurationNodeManager.StartAlarmMonitoring` | Custom thresholds/sources |
| `IUserDatabase` | User/password store | Existing in `GlobalDiscoverySampleServer` | LDAP, database |
