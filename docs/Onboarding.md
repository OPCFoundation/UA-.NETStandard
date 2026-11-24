<!--
SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
SPDX-License-Identifier: MIT
-->

# OPC UA device onboarding

See the [GDS developer guide](GDS.md) for directory and certificate management.

## Contents

- [OPC 10000-21 OnboardingClient](#opc-10000-21-onboardingclient)
- [External authentication certificate handoff](#external-authentication-certificate-handoff)
- [Part 21 requirements and implementation boundaries](#part-21-requirements-and-implementation-boundaries)
- [Replace a provisionable device's node manager](#replace-a-provisionable-devices-node-manager)

## OPC 10000-21 OnboardingClient

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

Select filesystem persistence before registering the runtime:

```csharp
services.AddFileSystemOnboardingTicketStore(ticketCheckpointPath, fileSystem);
```

`FileSystemTicketStore` implements the same `ITicketStore` contract as
`MemoryTicketStore` and also supports direct construction. It retains the
exact envelope bytes, validated metadata and creation timestamp across
restart. Replacing a ticket ID replaces its record; removing it persists
that removal. Reads and enumeration return detached snapshots.

One provider instance must exclusively own its path. It serializes
read/modify/publication operations locally, flushes a staging file, then
uses the injected `IFileSystem.Replace` to publish the complete checkpoint.
The filesystem provider must make replacement indivisible. A failed
publication leaves the preceding checkpoint intact; publication and
staging-cleanup failures are both reported. Cancellation after replacement
does not report a committed update as uncommitted. The adapter limits the
complete file to 16 MiB and 10,000 tickets, rejects corrupt or incompatible
state on reads and writes, and never reconstructs an empty store from a
damaged file.

Protect the checkpoint against unauthorized writes. Persistence does not
validate envelopes, grant ticket authority or replace the registrar's
current trust checks. The fluent registration rejects a competing ticket
store without changing the existing registrations. Shared writers and
high-availability deployments need a provider with storage-level
coordination rather than multiple instances of this adapter.

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

Select `services.AddHostedFileSystemDeviceRegistrarLifecycle(checkpointPath)` before
registering the runtime to persist candidate identities, software approval,
application relations and pending DCA issuance through one provider. The model
factory binds this provider to its running server's namespace table before
publishing registrar methods; calls before binding return `BadServerHalted`
without reading or writing a checkpoint. Do not construct a separate table with
assumed namespace indexes. Direct hosts can still supply their actual table to
the `FileSystemDeviceRegistrarLifecycle` constructor. The path requires one
writer and protection against changes by other applications; this provider does
not coordinate multiple processes or replicated registrars.

For GDS issuance receipts, use
`gdsBuilder.AddSecretStoreCertificateRequestIssuanceStore(secretStore, scope)`
to bind the adapter to that GDS's actual namespace table during pre-startup.
The binding does not run on a regular server sharing the container. Receipt
access before binding returns `BadServerHalted` without invoking issuance or
accessing the secret store. The existing service-collection overload remains
available to direct hosts that already own the running server's namespace
table. Both paths require one writer per secret-store scope and retain the
host's responsibility for storage durability.

Select `services.AddHostedDeviceRegistrarAuditing()` before registering the
runtime to route audit calls through the standard factory's restricted native
event reporter. The adapter resolves without constructing server nodes; the
factory attaches it after model initialization and detaches it during shutdown.
Calls without an active model return `BadServerHalted`, and another live model
cannot replace its reporter. An existing custom `IDeviceRegistrarAuditReporter`
is retained. Direct hosts can continue constructing and injecting their own
`DeviceRegistrarAuditReporter`; the hosted adapter supports one registrar per
service collection.

Enable server auditing with
`options.ConfigureBuilder = server => server.SetAuditingEnabled(true)` when
configuring the GDS host. Registering the hosted reporter does not enable
auditing; when auditing is disabled, the reporter rejects delivery with
`BadInvalidState`. The registrar sample enables auditing before startup so its
ticket, identity and approval operations can report their required events.

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
call after acceptance does not cancel its queued attempt. Without a work store,
host shutdown reports unprocessed registrations and acceptance does not survive
process termination. Processor failures are logged with a classified application
identity rather than retried in a loop.

For durable acceptance, register
`AddFileSystemDeviceRegistrarPushWorkStore(checkpointPath, fileSystem)` before
`AddDeviceRegistrarPush`. The worker persists each announcement before returning
from acceptance, records processing before contacting the device, and removes
the record only after processing completes. Restart restores pending work and
requires `IDeviceRegistrarPushRecoveryProcessor.ReconcileInterruptedAsync` before
repeating an interrupted attempt. Failed attempts remain retained for a later
restart; a failed reconciliation does not authorize another processing attempt.
The standard processor reads authenticated device state and retained
enrollment, issuance, trust, and lifecycle evidence before resuming.

One scheduler and store instance must exclusively own the checkpoint path.
The filesystem store stages and flushes complete binary checkpoints, then
publishes them through `IFileSystem.Replace`. Corrupt state, unsupported versions,
and capacity violations reject startup or acceptance instead of discarding work.
The selected filesystem must publish replacements indivisibly; this adapter
does not coordinate multiple processes or guarantee power-loss durability of an
operating-system cache. Distributed hosts need a transactional
`IDeviceRegistrarPushWorkStore`.

If a durable enqueue throws, the scheduler reads the store while it still
holds acceptance. It queues the item only if the store contains exactly one
record with the generated work identifier, the same application description,
and state `Pending`. The scheduler logs the write error as a warning when the
read confirms acceptance. If the read does not confirm acceptance, the
scheduler rethrows the write error. If the read also throws, the scheduler
reports both errors in an `AggregateException`.

The read does not use the caller's cancellation token because the write may
already have accepted the item. The scheduler does not retry the write or
generate a new work identifier during confirmation.

`Dispose` rejects new work without waiting for an enqueue already in progress.
The scheduler defers queue closure and worker cancellation until the enqueue
finishes, including inserting an item whose durable write succeeds or is
confirmed. Stop and wait for the worker before disposing its borrowed store
and processor. On restart, the scheduler recovers pending work and reconciles
interrupted work before processing it again.

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
or implement the supervised `IDeviceRegistrarPushScheduler`.

`DeviceRegistrarPushProcessor` supplies the registrar's generated-client
operation sequence. It validates the actual device peer and tickets, records
the policy-selected application in the shared GDS database, and checks persisted
software approval before requesting a CSR or changing credentials. It writes
the complete trust list and applies that change first: application-group
`UpdateCertificate` validates against committed issuer stores, not the
registrar's supplied issuer argument. Trust application can close the first
channel, so certificate transfer uses a new connection pinned to the same
device peer and rechecks software approval. After certificate application, a
third connection proves the exact installed certificate before confirming the
lifecycle transition. A transport failure leaves the retained
request and pending certificate available for explicit recovery; the scheduler
does not repeat an uncertain operation automatically.

`AddDeviceRegistrarPushConnections` accepts either an initialized
`ApplicationConfiguration` or a configuration factory resolved from the owning
service provider. The factory overload lets an asynchronously initialized host
select its configuration without constructing a second container or blocking on
initialization. Complete configuration initialization before resolving the push
provider; resolution creates no session, and target selection still runs for
each new connection. Both overloads reject automatic certificate acceptance.
The connected push-registrar sample uses the factory overload.

When the device reports `IsSingleton`, the processor records a restriction in
the shared lifecycle before issuance or credential promotion. The restriction
survives identity refresh, software approval, certificate replacement and
restart; a later `IsSingleton = false` response does not remove it. Its
certificate can access its own application, but cannot register or configure
other applications even when the host's per-application policy approves.
Existing managed-application relations remain recorded without granting access.
Custom lifecycle providers must implement
`IDeviceRegistrarApplicationScopeLifecycle`; managed-application operations
return `BadNotSupported` when that capability is unavailable.

The filesystem lifecycle checkpoint includes this restriction in version 2.
Version 1 checkpoints have no application-scope evidence, so their devices load
with managed-application access prohibited. Loading does not rewrite the file;
the next successful mutation publishes version 2. A host must review legacy
device scope rather than infer unrestricted DCA privilege from missing evidence.

`AddDeviceRegistrarPushConnections` selects
`SessionDeviceRegistrarPushConnectionProvider`, which creates a fresh session
through the existing `ISessionFactory` for each attempt and installed-certificate
proof. The host supplies a target selector returning a `DeviceRegistrarPushTarget`
with an authorized encrypted endpoint, registrar user identity and
namespace-URI-based model addresses. The adapter enables domain checking,
rejects automatic acceptance of untrusted certificates, and checks the actual
transport leaf against the expected installed certificate. Discovery certificate
bytes cannot satisfy that check. The host must configure the initial
manufacturer or device trust before connecting; signed-ticket admission remains
the processor's separate authorization step. Disposing a connection closes only
its freshly created session. Close and disposal failures are retained.

`AddDeviceRegistrarPushEnrollments` selects
`SecretStoreDeviceRegistrarPushEnrollmentProvider` on the GDS builder and binds
it to that host's actual `ApplicationsNodeManager`. It retains the validated
signed ticket envelopes, device-generated CSR and namespace-URI request
identifiers through the explicitly selected `ISecretStore`. The provider uses
the existing GDS authority, approval queue and immutable issuance receipt store;
it does not create another authority or fabricate a device-authenticated pull
request. Local certificate requests and deliveries use the GDS audit reporter.

The host must configure the shared lifecycle and issuance receipt store before
starting the GDS. Exactly one provider instance may own a journal scope across
all processes, and the secret store must publish each complete value durably
before returning. `InMemorySecretStore` cannot provide restart durability.
Software approval and DCA ownership are checked before CSR creation, request
submission and issuance publication. Automatic certificate-request approval
follows the existing GDS setting; it does not approve device software.

The provider records an uncertain transition before asking the device to create
a CSR or inserting a certificate request. If the operation succeeds but its
following journal write fails, a subsequent attempt stops instead of generating
another key or submitting another request. A trusted host can use `RecoverAsync`
with independently recovered original CSR evidence. Recovery after queue
insertion also requires the original unaccepted request identifier, whose CSR,
group and type must match the journal. Recovery cannot replace a completed
enrollment or clear an uncertain transition to start another request.

`FindRequestAsync` exposes a retained request identifier for administrative
approval. It returns a null `NodeId` before request retention; an uncertain
insertion must be resolved from the original GDS request database. A retained
request resumes through the same receipt even when GDS accepted it before the
final enrollment journal write failed. If an accepted request has no retained
receipt, processing stops without issuing another certificate.

Enrollment evidence retains the original device-generated CSR. Before uploading
trust or promoting an installed peer, the processor verifies its signature,
application URI and requested key profile, then matches its public key and
algorithm parameters to the retained issued certificate. A matching request
identifier and group/type labels cannot substitute for that key comparison.
Missing or substituted signing evidence rejects recovery without changing the
pending credential or requesting another device key.

On an existing `IGdsServerBuilder` named `gds`, select the existing authority and
certificate type, then register the connection provider and processor:

```csharp
gds.AddDeviceRegistrarPushEnrollments(
    "registrar-push",
    Opc.Ua.Gds.ObjectIds.Directory_CertificateGroups_DefaultApplicationGroup,
    ObjectTypeIds.RsaSha256ApplicationCertificateType);
services.AddDeviceRegistrarPushConnections(configuration, selectTarget);
services.AddFileSystemDeviceRegistrarPushWorkStore(pushWorkCheckpointPath, fileSystem);
services.AddDeviceRegistrarPush(capacity: 32);
```

Direct hosts can construct `SecretStoreDeviceRegistrarPushEnrollmentProvider`
with their initialized `ApplicationsNodeManager` and register that instance as
`IDeviceRegistrarPushEnrollmentProvider`.

A secure channel to the issued DCA certificate proves only that certificate.
Part 21 §4.3.3 also counts the TrustList as part of application setup, and the
TrustList can change after the registrar applied it. When the DCA already
presents the issued certificate, the processor reads its TrustList and writes
the retained TrustList only when it differs; it does not request another
certificate. The processor confirms the credential only on a new connection,
after that connection has read back the intended TrustList. A device that
reports any other TrustList leaves the credential pending with
`BadInvalidState`.

After the processor confirms the installed DCA credential of a non-singleton
device, it provisions managed applications through the same proven session.
It browses `ApplicationConfigurationType` instances below the device object
and in the standard `ManagedApplications` folder, then reads each instance's
`ApplicationUri`, `ProductUri` and `ApplicationType`. When the optional
`ApplicationNames` property is absent or empty, the `ApplicationUri` becomes
the registered name. An instance that reports the DCA's own `ApplicationUri`
is skipped.

The managed `ApplicationConfiguration` model does not provide discovery URLs,
but the shared GDS database requires them for Server records. For `Server` and
`ClientAndServer` applications, the host must register an
`IDeviceRegistrarManagedApplicationProvider`. For each application URI it
supplies the authoritative discovery URLs, currently installed leaf
certificate, selected `ConfiguredEndpoint`, selected `IUserIdentity` and
nonempty `ServerCapabilities`. Capability identifiers come from the application
owner, not discovery; a missing or blank value rejects provisioning before
registration. The host must select the standard unknown-capability identifier
explicitly if it cannot identify any supported capability.
The host can also set `CertificateTypeId` for that application. If it leaves
the identifier null, push uses the registrar connection's configured type.
This rule also applies to Client applications. Without a metadata provider,
a Client uses the configured default; a configured provider returning no target
is a configuration error, not permission to fall back.
Before creating a signing request, push checks that the selected type appears
in the application's `DefaultApplicationGroup.CertificateTypes`; it does not
silently choose another type. A host can therefore provision applications
that support different certificate types while keeping the selection under
operator policy. The OnboardingRegistrar sample accepts an optional
`--managedApplicationCertificateTypeId` expanded NodeId for its configured
managed application.
The standard enrollment provider implements
`IDeviceRegistrarPushCertificateEnrollmentProvider` to route that selected type
to the existing GDS authority. A custom provider implementing only the original
enrollment interface can serve the configured default; a nondefault selection
is rejected before registration or CSR creation.
Before saving a new enrollment journal or invoking the device's CSR callback,
the standard provider also checks the selected type against its local authority.

The enrollment journal retains the original selected type and certificate group.
Recovery validates the original CSR and request against those retained values,
even if the configured default has changed. A subsequent attempt selecting a
different type is rejected rather than starting another enrollment. The journal
format remains version 1; an older implementation can resume it only when its
configured type matches the retained selection. The connection default and
enrollment-provider default remain separate host configuration values.
The processor uses the model's application identity and type; it does not let
the provider replace them. Discovery URLs identify DiscoveryEndpoints; the
selected connection endpoint URL can differ from them (Part 4 §7.2 and
§5.5.4). Both must be absolute URLs, and the selected endpoint's application URI
must match the model.
The processor resolves and snapshots this metadata before application
authorization so `CanManageAsync` sees the same `DiscoveryUrls` and
`ServerCapabilities` that registration would use. Authorization still precedes
certificate verification and GDS registry lookup.
The processor and verifier copy the standard endpoint, application, user-token
and quota fields into owned instances without using provider `Clone()`
overrides. Plain endpoint names retain their locale and text. Deferred
translation metadata is currently rejected rather than discarded; support
depends on the shared `LocalizedText` safe-copy contract. Selected token-policy
index, binary-encoding support, reverse-connect settings and XML extensions are
copied. Retained discovery endpoint lists are detached and preserved for the
session's comparison with the endpoints returned by CreateSession, without
rediscovery. Embedded endpoint identities are rejected rather than silently
dropped. The selected authentication identity
remains host-owned.
The default channel verifier rejects an enabled reverse-connect target before
session creation; copying those settings does not make its forward-session
factory overload use reverse transport. Such a target requires a host-provided
channel verifier using the existing reverse-connect session-factory overload.

The host also registers a channel verifier with an initialized client
`ApplicationConfiguration`. The onboarding verifier supports UA-TCP and HTTPS.
HTTPS validates the TLS peer independently from the pinned OPC UA application
certificate and expected application URI:

```csharp
services.AddDeviceRegistrarManagedApplicationProvider(
    provider => provider.GetRequiredService<MyManagedApplicationProvider>());
services.AddDeviceRegistrarManagedApplicationChannelVerifier(
    provider => provider.GetRequiredService<IOpcUaApplicationConfigurationProvider>().Configuration);
```

After per-application authorization succeeds, the verifier opens a fresh
session through the built-in `DefaultSessionFactory` or a factory that
explicitly declares `IPinnedServerCertificateSessionFactory`. It checks the
channel endpoint and certificate against values captured from the host's
selected endpoint before constructing a session. The session must retain that
same channel, and the verifier repeats the endpoint and certificate checks
before opening it with the target user identity. The factory must validate the
signed CreateSession response before sending ActivateSession credentials. On
the initial attempt, the pinned certificate is the provider's installed
certificate; after rotation, it must match the retained GDS issuance receipt
described below. The verifier closes and disposes the session before returning.
This check occurs before the managed application is registered or associated
with the device. The device's DCA peer is checked again after the separate
connection. The processor also requires the managed application's
`GetCertificates` result to report the selected certificate before it reads or
creates the retained signing request.

The verifier supports UA-TCP with `SignAndEncrypt`. The UA-TCP channel is
configured with the selected endpoint certificate, security mode and policy.
Before channel creation returns, the client authenticates the signed
OpenSecureChannel response and checks its sender certificate against the
selected endpoint certificate. This binds the UA-TCP peer to the selected
application certificate before the verifier supplies user credentials.
During session opening, the client also checks the CreateSession response's
server application URI against the URI captured from the selected endpoint.
The verifier opts into this check before opening the session; generic session
opens do not enable it by default.
It rejects a mismatch before sending ActivateSession credentials.

HTTPS requires `Sign` with a non-None security policy. The host can set
`DeviceRegistrarManagedApplicationTarget.HttpsServerCertificate` to an exact TLS
leaf pin; without it, the channel uses operating-system TLS trust and revocation
checking. Both policies retain hostname and validity checks. The channel captures
the policy before its first request, applies it to every TLS handshake, including
replacement pooled connections, and does not follow redirects. An injected HTTP
pipeline cannot bypass this policy.

The TLS certificate may differ from the OPC UA application certificate.
CreateSession must independently prove the pinned application key and return
the expected application URI before ActivateSession sends user credentials.
The TLS terminator is part of the trust boundary and can access transport and
session data. Legacy CreateSession signatures cover the client certificate and
nonce; they do not cryptographically bind the TLS terminator to the application
certificate. Separate TLS validation does not change that limitation
([Part 6 §7.4.1](https://reference.opcfoundation.org/specs/OPC-10000-6/v1.05.07/7.4.1)).

The verifier accepts the exact built-in `DefaultSessionFactory` type or a
factory whose concrete type directly declares
`IPinnedServerCertificateSessionFactory` and whose base types do not declare
that capability. A subclass cannot become capable only by inheriting the
interface from a base factory. A custom factory must establish the selected
channel without the target user identity. For UA-TCP it must authenticate the
signed OpenSecureChannel response and check its sender certificate against the
selected endpoint certificate. For HTTPS it must expose
`IHttpsServerCertificateValidation` and enforce the selected TLS policy on every
handshake. Both transports must enforce the requested security mode and policy
and preserve the selected endpoint and
channel when constructing the session. Before `OpenAsync`, the verifier checks
both `ConfiguredEndpoint.Description` and the transport channel endpoint against
the selection, and checks them again after opening. The session must reject a
changed CreateSession application URI before ActivateSession. Its channel
disposal must tolerate a second call if session cleanup fails. The verifier
rejects a factory that changes the selected endpoint or returns a session
attached to another channel.
The configured `CertificateManager` remains authoritative for transport trust
validation when it runs, including its current policy, auto-accept settings and
host callback. Application-certificate identity binding does not replace
transport-certificate trust validation.

After the processor applies the TrustList and issued certificate through the
managed application's standard Part 12 methods, it opens another fresh session
and verifies the issued OPC UA application certificate in the signed
CreateSession response. The host must keep the endpoint, user identity and
client trust configuration valid through both checks. The provider's
certificate value is the initial operator pin. If a process stops after the
managed application has installed the issued certificate but before the final
proof succeeds, a retry reads the current `GetCertificates` value and the
retained enrollment for that device and application. It accepts a different
current value only when it exactly matches the certificate in that enrollment's
GDS issuance receipt, then verifies it in another signed CreateSession response.
It does not obtain a replacement pin from discovery or start another issuance.
A certificate matching neither
the operator pin nor the retained receipt is rejected before the processor
changes the application. This permits recovery when the operator's certificate
file still contains the pre-rotation certificate; the host must still trust the
issued certificate or its issuer. The receipt cannot restore the preceding
certificate, so recovery confirms or reapplies the retained issuance rather
than rolling the application back. Missing providers, invalid discovery or
connection URLs, insecure endpoints, certificate
mismatches, or channel failures reject that application's push. Hosts that do
not register both services cannot provision managed Server or
ClientAndServer applications. The OnboardingRegistrar sample demonstrates a
host provider for one explicitly configured managed application, including its
endpoint, SHA-256 security policy, public certificate and separate user
credentials. See the [push registrar configuration](../samples/Gds/README.md#host-the-connected-push-registrar).

Each application is authorized on its own:
`IDeviceRegistrarApplicationAuthorization` decides before the processor reads
any existing registry entry. The processor then registers the application in
the shared GDS database and associates it with the device. Before it creates
the signing request and before each write to the application, the processor
rechecks the DCA's persisted software approval and the device's recorded
relation to that application. It performs the standard Part 12 sequence on the
application's own `ApplicationConfiguration`: `CreateSigningRequest` for the
default application group, issuance through the registrar's retained push
enrollment, a TrustList write completed with `CloseAndUpdate`, and
`UpdateCertificate`. `CreateSigningRequest` and `UpdateCertificate` pass a null
group, which Part 12 assigns to `DefaultApplicationGroup`.

Before it creates the signing request, the processor reads that group's
`CertificateTypes` and rejects the application with `BadNotSupported` when the
host-selected type—or the registrar's configured default when no type was
selected—is absent.

Part 21 §4.3.3 defines application setup as both the certificate and the
TrustList, so the processor checks each separately. It calls `GetCertificates`
with the resolved `DefaultApplicationGroup` and reads the TrustList file. It
replaces only what differs: an application that holds the issued certificate
with a missing or stale TrustList receives the TrustList alone, and repeating
the push does not replace an installed credential. Each replaced value is read
back before the application counts as provisioned. Part 12 §7.10.8 returns
`CertificateTypeIds` and `Certificates` as parallel arrays; a response whose
arrays differ in length fails with `BadUnknownResponse` and changes nothing.

`ApplyChanges` commits every change queued in the session, so the processor
rechecks approval and the actual peer immediately before each `ApplyChanges`.
Revocation while the device stages a TrustList or certificate therefore leads
to `CancelChanges` instead of activation. When `ApplyChanges` itself fails, the
processor also calls `CancelChanges` and retires the lease before another
application can use it.

A failed reply does not prove that nothing was staged. Opening the TrustList
for writing starts a transaction (Part 12 §7.8.2.2), and a device can stage
`CloseAndUpdate` or `UpdateCertificate` and then lose or fail the reply. Any
failure or cancellation of the TrustList transfer, `CloseAndUpdate` or
`UpdateCertificate` therefore leads to `CancelChanges`, followed by lease
retirement; the DCA and managed paths behave the same way. A successful
cancellation or `Bad_NothingToDo` reports the transaction state when
cancellation executes, not completion of the earlier request. A timed-out
request can still stage its changes after cancellation returns. Part 4 §7.32
defines `timeoutHint` as a hint to the server, not a completion barrier, so the
processor never proceeds to another application on that lease after an
uncertain staging or apply result. `CancelChanges` runs without the caller's
cancellation token and has an independent five-second deadline, including when
the transport has no operation timeout. Transports must honor cancellation for
this deadline to release the pending call. With such a transport, a device
that does not reply before the deadline produces a cancellation cleanup error;
the processor retains it with the original failure, then disposes the lease.
A custom transport that ignores cancellation can still prevent that await
from completing; the token does not forcibly abort it. A lease disposal failure
is also retained. `Bad_NothingToDo` adds no cleanup error to the original failure.
The processor accepts a `TimeProvider` for cleanup deadlines; fluent hosting
uses the registered clock when available and otherwise uses the system clock.

A denial or failure before mutation does not stop the processor from trying
other applications. An approval rejection after a completed staging request
can also continue when cancellation succeeds. Uncertain staging or apply
results, failed cancellation, caller cancellation and an invalidated session
stop the loop. Application failures are reported together in one
`AggregateException`. When `CancelChanges` fails, for example on a device
without transaction support, changes may remain queued. The processor reports
the original error and the cleanup error together in a nested
`AggregateException`; it does not assume the device rolled back. It releases
the lease, whose provider must attempt session close and disposal while
retaining any cleanup errors.

Every `IDeviceRegistrarPushConnectionProvider.ConnectAsync` call must create a
fresh session exclusively owned by the returned lease. Custom providers must
not borrow container-owned sessions, share sessions with other callers, or pool
sessions for later attempts. Disposal permanently retires the session even if
close or disposal fails; a retry must create another session. This prevents a
later caller's `ApplyChanges` from committing changes left on a session whose
`CancelChanges` failed. A failed close still leaves remote transaction state
uncertain, but no subsequent lease may use that session. The default provider
already creates and closes dedicated sessions; custom providers must implement
the same ownership contract. Operation, cancellation and lease cleanup errors
are reported together rather than replacing one another.

A singleton device provisions no managed applications, regardless of host
policy. The sample's optional managed-application configuration applies only to
the explicitly selected application on a non-singleton device.

The processor does not update `ConfigurationFile` contents with
`CloseAndUpdate` and `ConfirmUpdate`. That optional Part 12 transaction is
separate from Part 21 certificate and trust provisioning. `GetCertificates`
is optional in Part 12, but the processor needs it as evidence: a device
without it fails the push with the device's status.
Even with it, the result shows what the application reports; the separate
secure-channel check is required to prove the certificate actually presented
by a managed Server.

`AddDeviceRegistrarPush` selects one processor and a host-owned queue backed by
the explicitly registered work store; register both before
`AddDeviceRegistrarRuntime`. Retained work distinguishes pending announcements
from interrupted attempts that require reconciliation. The device-side installer below supplies local
credential activation and reset, and the standard push methods bind the security
transition and transaction-completion providers. Connected recovery fixtures
are present; their execution status is tracked separately.

The processor also serializes direct calls through installed-certificate proof,
so a second call cannot replace the first attempt's pending device key or trust
transaction. Cancellation while waiting does not contact the device. Direct
owners must stop new work and await active attempts before disposing the
processor. Hosts must stop the scheduler before disposing its service provider.

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
require host-configured connection and trust providers. Select
`FileSystemDeviceRegistrarLifecycle` for single-host registrar restart
persistence; `MemoryDeviceRegistrarLifecycle` retains state only within the
current process.

`PullDcaWorkflow` borrows a container-owned `ManagedSession` and uses generated
registrar and certificate-directory proxies. Its identity and credential
providers retain private keys, select the returned device identity, and install
issued credentials through the existing certificate stores; the workflow does
not close or dispose that session. A required software update invokes the
configured software manager before certificate issuance.

`RunAsync` copies the offered certificate bytes, tickets and managed-application
records before borrowing a connection. Changes to the caller's input buffers or
application records during an operation do not change its identity offer or
application registrations.

For candidates retained by `IProvisionableDeviceRegistrarStore`, use
`PullDcaRegistrarSelection.SelectConfigured` with the operator-selected endpoint,
expected registrar application URI and resolved registrar/directory node identifiers.
It rejects missing or ambiguous matches before a connection is opened. Selection
does not validate the remote certificate or grant trust; the managed session must
still enforce the host's registrar certificate policy. The connected device sample
can initiate this pull operation when its endpoint and expected registrar
ApplicationUri are configured.

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
The receipt adapter alone cannot recover an accepted response lost before
local publication.

[Part 12 §7.9.5](https://reference.opcfoundation.org/specs/OPC-10000-12/v1.05.07/7.9.5)
defines recovery when a repeated `FinishRequest` returns `Bad_InvalidArgument`
after a network failure. A client that used `StartSigningRequest` can read the
currently assigned certificate through `GetCertificates`; a client that used
`StartNewKeyPairRequest` must start a new key-pair request. `PullDcaWorkflow`
supports both paths. Register an `IPullDcaSigningRecoveryProvider`
to enable its `GetCertificates` recovery path. Direct construction accepts the
same optional provider.

Select `PullDcaCertificateRequestMode.NewKeyPairRequest` through
`PullDcaRequest.DcaCertificateRequestMode` or each managed application's
`CertificateRequestMode`. The default remains `SigningRequest`.
Register an `IPullDcaNewKeyPairCredentialProvider` before selecting key-pair
requests; unsupported requests are rejected before connection admission.
The workflow requests PFX output over an encrypted SecureChannel and clears
its private-key buffer after use. The installer validates that the private
key matches the issued certificate and retains it in the selected certificate
or secret store. The public signing receipt store does not hold private keys.

For restart recovery, register the host's `IProtectedPullDcaRecoveryStore`
with `AddPullDcaKeyPairRecovery`. The workflow checkpoints the received PFX
before downloading trust or installing credentials. It can reuse a retained
result without repeating `FinishRequest` and removes the checkpoint only after
the installer verifies installation. See [Recover pull DCA key-pair
requests](GDS.md#recover-pull-dca-key-pair-requests) for the store contract and operator reset
procedure.

`MaximumNewKeyPairRestarts` bounds replacement requests after
`Bad_InvalidArgument`; the default permits one restart. Key-pair recovery never
uses `GetCertificates` as a replacement for a lost private key. With a saved
request ID, the workflow calls `FinishRequest` for that ID. If the registrar
already accepted `FinishRequest` but the Client lost the response before
retaining the private key, recovery requires a fresh key-pair request. If
Start may have succeeded but its request ID is unknown, the protected
checkpoint prevents automatic redispatch until the host reconciles the
attempt and resets the checkpoint.

For signing recovery, the workflow requests certificates for the original
application and group, requires exactly one assignment for the original
certificate type, downloads a complete TrustList, and invokes the provider
before retaining or installing output. The provider must match the
retained registrar/request binding, prove
the original private key, and validate the application URI, certificate validity
and trust. It must also reject stale assignments: an old certificate with a reused
public key is not proof that the original request completed. Missing evidence
must reject recovery. No provider is registered implicitly. The
`OnboardingClient` sample opts in when `--signingJournal` and
`--signingKeyStore` are both configured with `--pull true`; it registers its
device credential provider for recovery and request observation. The journal
retains the registrar, application, request, group, type, CSR and public key
certificate, while the separate signing store retains the private key. The
sample resumes a `Started` request after restart and validates a certificate
read through `GetCertificates` against that binding and the retained key.
Without the journal options, `Bad_InvalidArgument` remains visible; the
separate `--resume*` path does not recover an accepted response whose completion
reply was lost.

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
`ApplicationConfiguration` components. Runtime registration alone does not expose
a device resource. `ProvisionableDeviceNodeManagerFactory` publishes the
standard namespace-zero device identifiers through lifecycle override routing,
while the Part 22 manager retains ownership of `Resources`, `Communication`,
and its network model. Reference snapshots retain the published routing view
across browse continuations and manager removal or recreation. Connected
composition fixtures cover both factory registration orders; their presence
does not establish a passing execution result.

`ProvisionableDeviceSecurity` supplies device-side installation and reset
orchestration with an explicit `IProvisionableDeviceSecurityInstaller`. Its
`RequireRegistrarAsync` gate retains the first SecurityAdmin registrar's actual
channel certificate in a `Configuring` checkpoint before permitting a protected
configuration operation. A failed checkpoint write rejects the operation.
Restart preserves that owner without invoking credential installation, and
security reset releases the claim. Security checkpoints support versions 1
through 3; the configuration-owner state requires version 2 or later.

For device-initiated installation, `InstallFromPullAsync` borrows an encrypted
stack session and checks the transport's actual registrar certificate against
the expected application URI and the device's current peer trust. It does not
accept discovery certificates or invoke an untrusted-certificate acceptance
callback. The local host selects the registrar from its persisted candidates
before connecting, for example with `PullDcaRegistrarSelection.SelectConfigured`.
Use `RequireConfiguredEndpoint` when the remote model NodeIds are not yet
available; resolve those identifiers from the authenticated session after
connecting.
The method uses the same installation checkpoint and platform installer as
inbound provisioning; the host does not fabricate an inbound administrator
context or replace certificate stores independently.

A pull credential provider can forward the downloaded public credentials to the
shared device-security owner. The existing private key must remain in the
device installer's configured application store:

```csharp
await deviceSecurity.InstallFromPullAsync(
    registrarSession,
    configuredRegistrarApplicationUri,
    new ProvisionableDeviceCredentials(
        downloaded.Certificate,
        downloaded.IssuerCertificates,
        downloaded.TrustList),
    cancellationToken);
```

This API does not start outbound connections or retain unfinished certificate
requests. The connected `OnboardingDevice` sample can initiate one explicitly
selected pull operation after recovery and listener activation. Its credential
adapter reuses local identity selection and CSR generation but sends received
material through `InstallFromPullAsync`, not through a second store writer.
The sample requires a previously persisted registrar candidate and separately
configured peer trust. Its pull journal retains dispatch, request, receipt,
installation, and installed-DCA continuation state. A separate certificate store
retains attempt-bound signing keys without placing private keys in the public
journal. Startup resumes a retained signing request or received credential,
then verifies installed security before continuing `GetManagers` and
managed-application provisioning on a session authenticated with the installed
DCA certificate. An uncertain dispatch is not treated as an unused request and
cannot silently switch to another registrar. The sample does not execute a
software update required by the registrar.

Hosts using this recovery path register
`IProvisionableDevicePullInstallationJournal` before device security.
The fluent security registration injects that journal and exposes the same
state owner as `IProvisionableDevicePullInstallation` and
`IProvisionableDevicePullInstallationRecovery`. Installation compares the
attempt, registrar certificate, and received credentials before applying effects;
recovery verifies the installed private key before recording completion.
Reset records its intent in both security and pull state, revokes old
deployment operations, and retires retained attempt keys after completion.

Use `RequireInstalledRegistrarAsync` when access requires an already configured
DCA. It checks the installed state and registrar trust together, without
claiming an unconfigured device. Reading `GetStageAsync` and then calling
`RequireRegistrarAsync` is not equivalent: a reset between those calls could
cause bootstrap authorization to record a new configuration owner. The installed
check authorizes the current state; it does not reserve that state for a later
application mutation.

`CreateApplicationOperations` supplies the deployment lifetime used by independent
application configuration engines. The provider admits an operation only while
DCA credentials are installed and no uncertain security publication requires
recovery. Device installation, recovery and reset wait for admitted application
operations to finish. Once reset intent is persistent, existing providers reject
further operations, including deferred activation after a successful
`ApplyChanges` response; installing credentials again does not restore the old
providers. Recreate the application bindings for the new deployment. This
provider orders operations within one host; it neither authorizes a registrar
nor persists deferred effects across process termination.

The installation checkpoint retains complete credentials and the actual
registrar channel certificate before invoking the installer. If installation
fails, only
that peer can retry the same credentials; another peer or different material
cannot replace the pending operation. After installation, registrar access is
validated against the installed TrustList through an isolated certificate
validation scope. The first registrar's leaf certificate is not a permanent
restriction: another registrar accepted by installed trust may manage the
configured device, as specified by
[Part 21 §7.3](https://reference.opcfoundation.org/specs/OPC-10000-21/v1.05.04/7.3).

`ProvisionableDeviceSecurityInstaller` retains the initial certificate and
private key in a separate identity store before configuration listeners open.
Unconfigured startup and the first direct configuration operation call
`IProvisionableDeviceSecurityDefaults.PreserveDefaultsAsync` before recording a
registrar or installation. Later recovery calls `VerifyDefaultsAsync`, which reads
the retained identity key and original TrustList without creating either. Missing
or corrupt defaults stop recovery; current operational trust cannot become the
factory reset configuration. Direct hosts call `PreserveDefaultsAsync` before
exposing configuration methods.
It captures the original TrustList once in the selected secret
store and binds that checkpoint to the application, stores and certificate
group. A reset reloads the retained identity key and restores that TrustList; it
does not call a software installer or remove installed software.

The installer claims only a pending key matching the issued certificate, or
reuses the matching active or retained identity key. Failed installation restores
a claimed key without replacing a newer request; both installation and
restoration errors are reported. After updating the certificate manager it calls
`IServerApplicationCertificateActivator`, whose standard-server implementation
updates endpoint descriptions and transport listeners and propagates activation
errors. The old application certificate is removed only after activation
completes. This is a local activation acknowledgement, not evidence that a
remote client has connected with the new certificate.

When a deployment requires software updates, register an
`IProvisionableDeviceSoftwareUpdatePolicy` with
`.AddProvisionableDeviceSoftwareUpdatePolicy(provider => deviceSoftwarePolicy)`,
or pass it to the installer's constructor. The policy reads the existing
software manager's durable state for the DCA application URI and throws if an
update or its approval is incomplete. Direct installation checks the policy
before changing stores or activating endpoints. Standard push completion and
startup recovery also check it before accepting a non-factory certificate.
Factory identity capture, default preservation and security reset remain
available without software approval; these operations do not change software
state. Omitting the provider means that the deployment requires no software
update, not that an update has been performed. This adapter does not provide a
software-update engine or replace the registrar's `UpdateSoftwareStatus` flow.

Select the application slot, identity store and existing pending-key provider:

```csharp
serverBuilder.AddProvisionableDeviceSecurity(
    "device-security", dcaApplicationUri,
    new ProvisionableDeviceSecurityInstallerOptions
    {
        ApplicationCertificate = configuredApplicationSlot,
        DeviceIdentityStorePath = deviceIdentityStorePath,
        DeviceIdentityStoreType = CertificateStoreType.Directory,
        CertificateGroupId = ObjectIds.ServerConfiguration_CertificateGroups_DefaultApplicationGroup
    });
```

The secret store must persist complete values across restart, and the identity
store must retain private keys independently of application-certificate rotation.
The host supplies passwords when those stores require them. A custom platform
can instead register its own installer:

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
provider and gates `SetRegistrarEndpoints` through it. The built-in security
provider lets an authenticated `SecurityAdmin` replace candidates while the
device is unconfigured without recording the caller as the registrar. It
serializes the replacement with security transitions, rejects it while
configuration, installation, or reset is pending, and rejects it when security
recovery has not completed. After installation, the engineering client's
accepted encrypted session and `SecurityAdmin` role authorize candidate
publication; its certificate need not belong to the DCA's registrar TrustList.
The DCA's outbound registrar connections still require that TrustList, and
candidate metadata does not grant trust. A custom provider can implement
`IProvisionableDeviceRegistrarConfiguration` to coordinate replacements with
its own security state. Otherwise, the runtime calls its existing
`RequireRegistrarAsync` authorization method before storing candidates.
`AddProvisionableDeviceSecurity` also registers that same state owner as
`IPushConfigurationAuthorization`. Standard server hosting installs it on
`ServerConfigurationOptions.PushAuthorization` before creating the configuration
address space. `CreateSigningRequest`, `UpdateCertificate`, self-signed
certificate creation, certificate deletion, `ApplyChanges`, `CancelChanges` and
security reset await this policy after their existing channel and SecurityAdmin
checks. TrustList write-open, chunk writes, `CloseAndUpdate`, certificate
addition and removal use the same policy; a later denial does not consume an
existing write handle. Closing a handle without applying it remains available.

Direct hosts set `ServerConfigurationOptions.PushAuthorization` to the state
owner when constructing their `ConfigurationNodeManager`. A custom policy can
implement `IPushConfigurationAuthorization` without referencing the onboarding
library. `TrustList.SetWriteAuthorization` supplies the corresponding awaited
write policy for custom TrustList hosts; it replaces the constructor's write
callback, so that policy must also enforce its required channel and role checks.
The setter removes synchronous mutation callbacks rather than blocking on an
asynchronous decision. Existing hosts with no policy retain their original
access checks. SecurityAdmin on an
encrypted authenticated session is required for `InstallAsync`. The state
adapter has one exclusive scope owner and the same complete-publication and
durability prerequisites as the client receipt adapter. It does not provide
distributed coordination.

Standard push records installed device security inside the existing
certificate and TrustList transaction. `ProvisionableDeviceSecurity` implements
`IPushConfigurationCompletion`; the default coordinator appends this completion
after taking the staged operation batch, so another request cannot insert a
mutation after the security checkpoint. Completion reads the selected DCA slot,
including possession of its private key, and the complete current trust stores
through `IProvisionableDeviceAppliedSecurity`. A trust-only update leaves the
device in `Configuring` while the retained device identity remains active.
A changed DCA credential is validated against that trust and recorded as
`Installed`, without repeating installation or endpoint activation.

If completion fails, the coordinator compensates the preceding store changes
and does not schedule endpoint rotation. An uncertain checkpoint publication
blocks further registrar access until recovery. Startup compares an applied
checkpoint with the actual keyed certificate and trust stores; a mismatch aborts
startup rather than installing credentials over a compensated transaction.
If the process stopped after committing stores but before recording completion,
startup can reconcile those stores with the retained configuring registrar.
Trust-store enumeration order does not change the result. Checkpoint versions
1 and 2 remain readable; version 3 distinguishes applied stores from an
installation that still requires platform effects.

Custom installers need `IProvisionableDeviceAppliedSecurity` for this standard
push path. Custom transaction coordinators need
`IPushConfigurationTransactionCompletion`; an unsupported coordinator rejects
apply before committing anything. Direct installation still uses
`IProvisionableDeviceSecurityInstaller`. The original identity and trust
snapshot remain available for security reset.

The same security instance is registered as `IServerConfigurationResetProvider`,
so the existing configuration node manager retains ownership of network
authorization, audit reporting and deferred shutdown. An application-controlled
physical reset can invoke its local `ResetToServerDefaultsAsync` entry point.
Reset intent is persisted before the platform callback runs; a failed reset
keeps registrar access disabled, and startup recovery retries it before accepting
a new registrar. The installer must restore device identity and security defaults
without deleting installed software. Corrupt checkpoints are reported without
being erased. The connected `OnboardingDevice` sample selects the concrete
installer and retained factory identity, with public-only security checkpoints,
standard network reset and an explicit local reset option. Its factory adds a
Part 22 `Resources` parent reference to the singleton device through the shared
lifecycle routing view alongside the Base Network manager. Connected recovery/reset fixtures are present; their
execution status is tracked separately, and source composition alone does not
establish successful endpoint switching, restart or reset over real
connections. See the
[device host procedure](../samples/Gds/README.md#host-the-connected-device).

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

## External authentication certificate handoff

The registrar accepts a DCA certificate installed by an external authentication
service under
[Part 21 §7.4.1](https://reference.opcfoundation.org/specs/OPC-10000-21/v1.05.04/7.4.1).
The service owns device authentication and key/certificate installation, and
must arrange credential trust with the deployment's Registrar,
SoftwareUpdateManager and CertificateManager. The
registrar validates the authenticated channel certificate against dedicated
authentication-service CA trust, then applies the host's approval policy for
the product instance and DCA application. Trusting an issuer alone does not
approve every device issued by that authority.

On an existing GDS builder, register an
`IDeviceRegistrarExternalAuthenticationPolicy` implementation that selects the
configured `ProductInstanceUri`, DCA `ApplicationRecordDataType` and software
requirements:

```csharp
gds.AddDeviceRegistrarExternalAuthentication(
    trustedStorePath: externalTrustedCaPath,
    issuerStorePath: externalIssuerPath,
    policyFactory: services => services.GetRequiredService<MyDeviceApprovalPolicy>());
```

`ExternalAuthenticationProvider` uses the shared `ICertificateManager` and its
separate `ExternalAuthenticationAuthorities` trust list. Populate that list
with authentication-service CA anchors, issuer certificates and current CRLs.
Leaf pins cannot replace CA anchors in this list. Normal OPC UA channel trust
must also admit the peer before the handoff can run; neither ticket authorities
nor the application's peer list implicitly populate the external CA list.
The provider rejects CA certificates used as device leaves, validates usage and
revocation without error overrides, and requires both the approved product
instance URI and DCA application URI in the leaf's URI subject alternative names.
An unavailable validator or denied approval propagates an error. Revocation and
validity failures also reject the handoff instead of selecting ticket fallback.

An approved inbound DCA enters the existing shared application database and
candidate lifecycle. An existing application record must already belong to
that product's DCA; approval cannot take over an unrelated application.
`GetManagers` is available before software approval, while certificate issuance
and managed-application registration retain the existing software gate.
After admission, persisted lifecycle authorization governs the DCA, as with
ticket-based admission. Hosts use the existing lifecycle operations to change
software approval and application ownership.

The typed client uses the already installed external DCA credential. First
obtain the configured managers without requesting application certificates:

```csharp
PullDcaExternalAuthenticationResult discovery =
    await workflow.RunExternalAuthenticationAsync(
        registrar, [], cancellationToken: ct);
```

The host completes any required software update through the advertised manager.
It can then call `RunExternalAuthenticationAsync` with its configured
`ArrayOf<PullDcaManagedApplication>`. Registration, certificate requests,
receipts, installation and signing recovery use the same Part 12 path as
ticket-based pull. This entry point does not call `ProvideIdentities`, activate
a different identity or replace the externally installed DCA certificate.

Registrar push evaluates the actual peer through the same provider before
requesting tickets. Accepted external evidence bypasses `RequestTickets` and
retains its original certificate and product identity through enrollment and
recovery. Before effects, push rechecks external approval, the original DCA
application relation and software status. An explicit approval denial is not a
request to try ticket authentication instead. The secret-store enrollment
provider retains ticket checkpoints in version 1 and external-only checkpoints
in version 2; version 2 has no ticket payload or signed envelope. Both are read
by the current provider. Older providers cannot read external checkpoints, so
do not downgrade a host with an unfinished external enrollment.

Direct hosts can construct `ExternalAuthenticationProvider` with their
certificate manager and policy. A separately supplied adapter can instead
implement `IDeviceRegistrarExternalAuthenticationProvider` and register it with
`AddDeviceRegistrarExternalAuthenticationProvider`. It must enforce equivalent
issuer, URI and approval checks. The seam permits a future .NET FDO SDK adapter
without requiring an SDK dependency in the registrar.

[Part 21 §7.4.2](https://reference.opcfoundation.org/specs/OPC-10000-21/v1.05.04/7.4.2)
describes an FDO Owner supplying the DCA credential through `fdo.csr`. FDO
TO0/TO1/TO2, ownership vouchers, service-info negotiation and the installation
of that credential remain external. This stack supplies the certificate
handoff and subsequent OPC UA management, not an FDO implementation or evidence
of interoperability with an FDO Owner.

## Part 21 requirements and implementation boundaries

The SDK provides **Full** support for the Part 21 pull and push onboarding
workflows (see the [published specification support matrix](Profiles.md#published-specification-support-matrix)).
The in-memory
key-pair recovery fixture demonstrates reuse of retained credentials after
recreating the Session and recovery adapter. Implementers supply protected,
durable production storage and credential installers. The in-memory fixture
does not demonstrate process-restart durability or production storage
protection. Distributed ownership and software installation remain host
responsibilities. The external-authentication provider supplies the handoff;
FDO itself remains external.

The provisionable-device factory uses the shared Part 22 `Resources` manager.
For same-runtime replacement without reinitializing the managed applications,
see [Replace a provisionable device's node manager](#replace-a-provisionable-devices-node-manager).
Registrar push uses the configured certificate type by default. The host can
select a different advertised type for each managed application.

The following map covers
[OPC 10000-21 v1.05.04](https://reference.opcfoundation.org/specs/OPC-10000-21/v1.05.04/).
It identifies implementation and regression sources, not certification or
results for a particular build. Optional Part 12 `ConfigurationFile` limits
are listed separately.

| Requirement | Implementation | Regression sources and remaining boundary |
|---|---|---|
| §§4.2.2, 4.3.2: software approval before operational provisioning | `DeviceRegistrarRuntime`, lifecycle providers, `IPullDcaSoftwareUpdateProvider` | `ProvideIdentitiesRuntimeTests`, `PullDcaWorkflowTests`, `PullRegistrarIntegrationTests`; the connected fixture exercises approval roles and issuance gates, not software-manager execution or a device endpoint gate; remote execution remains required |
| §§4.3.3, 7.2: DCA and managed-application credentials | `PullDcaWorkflow`, generated certificate-directory proxies, application-specific authorization | `PullDcaWorkflowTests`, `ApplicationsNodeManagerOnboardingTests`, `PullRegistrarIntegrationTests`; the connected fixture exercises one DCA's issuance, local installation and new-channel proof; connected multi-application setup and remote execution remain required |
| §§4.3.6, 7.3: security reset and return to onboarding | `ProvisionableDeviceSecurity`, retained configuration owner, concrete installer and existing `IServerConfigurationResetProvider` path | `ProvisionableDevicePushChannelTests.ConcretePushActivatesCertificateAndRecoversAcrossHostRestartAndFactoryReset` covers installed-certificate recovery, factory reset and a replacement registrar; the test's presence does not assert a passing run |
| §§5.1-5.3, 7.1: exact device/composite identity and private-key proof | `OnboardingIdentitySelector`, scoped identity trust, actual-channel registrar authorization, `OnboardingPushIdentityValidator` | `OnboardingIdentitySelectorTests`, `OnboardingIdentityTrustProviderTests`, `DeviceRegistrarRuntimeTests`, `OnboardingPushIdentityValidatorTests`; discovery alone never proves possession |
| §6.4: source-dependent ticket authority trust | `OnboardingTicketValidator`, `OnboardingTicketTrustProvider` | `OnboardingTicketValidatorTests`, `OnboardingTicketTrustProviderTests`; online authority-revocation services are not supplied by these adapters |
| §7.2 and Part 12 §§7.9.5, 7.9.8: request retry and certificate recovery | Public signing receipts, retained signing keys, protected key-pair checkpoints and bounded replacement of consumed key-pair requests | `PullDcaWorkflowTests`, `DevicePullSigningRecoveryTests`, `DevicePullReceiptRetryTests`, `DevicePullResetGenerationTests`, `ProvisionableDevicePullInstallationRecoveryTests` and `PullRegistrarIntegrationTests.KeyPairRecoveryInstallsRetainedCredentialsOnARecreatedSession` |
| §7.3: push provisioning, first registrar and installed trust | device security transition provider, standard push authorization and transaction completion, generated-client processor with separate trust/certificate apply connections, secret-store enrollment journal, retained singleton scope and connected registrar/device samples | processor fixtures cover managed Client, Server and ClientAndServer paths; Server paths require host-authoritative endpoint metadata and two fresh-channel certificate proofs. `ConnectedManagedApplicationRecoveryTests.InterruptedManagedRotationReopensFilesAndResumesOnlyRetainedCertificate` covers process-durable recovery through two live servers, and `ProvisionableDevicePushChannelTests.ConcretePushActivatesCertificateAndRecoversAcrossHostRestartAndFactoryReset` covers DCA restart/reset. These fixtures are authored; this map does not assert their execution results. |
| §7.4: alternate authentication/FDO | Dedicated CA validation and configured product/DCA approval through `IDeviceRegistrarExternalAuthenticationProvider`; typed external pull and registrar push reuse shared registrar/Part 12 lifecycle | `ExternalAuthenticationProviderTests`, `ProvideIdentitiesRuntimeTests`, `PullDcaWorkflowTests`, `DeviceRegistrarPushProcessorTests` and enrollment recovery cases cover the handoff. `PullRegistrarIntegrationTests.ExternalHandoffOnLiveChannelRequiresDeviceApprovalBeforePart12Issuance` exercises the provider boundary on real channels with software approval gating. FDO protocols and credential installation remain external; no FDO interoperability result is asserted |
| §§8.1-8.2: signed JSON tickets and String-subtype `EncodedTicket` | `OnboardingTicketWriter`, ticket validator, standard model and strict-UTF-8 compatibility wrappers | writer, validator, onboarding-client and stack-generation fixtures; vendor-specific unprotected metadata is not interpreted and cannot extend signing-certificate validity |
| §§9.2.2-9.2.8: registrar methods and application ownership | registrar runtime, async bindings, shared GDS database and certificate authorization | `DeviceRegistrarRuntimeTests`, `ProvideIdentitiesRuntimeTests`, `ApplicationsNodeManagerOnboardingTests`; database/lifecycle publication is not a distributed transaction |
| §§9.2.9-9.2.15: administration, managed authorities and restricted audit delivery | ticket administration bindings, managed authority file services, `DeviceRegistrarAuditReporter` | administration, authority-list and audit-reporter fixtures; connected authorized/unauthorized event delivery still needs remote execution |
| §§6.4, 9.2.11-9.2.12: accepted ticket retention across restart | `FileSystemTicketStore`, `AddFileSystemOnboardingTicketStore` | `FileSystemTicketStoreTests`; one instance owns the path, and remote execution remains required |
| §§9.3.1-9.3.5: provisionable resource, singleton layout, tickets and registrar endpoints | `ProvisionableDeviceRuntime`, `ProvisionableDeviceNodeManagerFactory`, lifecycle override routing, `AddProvisionableDeviceApplications` and generated node bindings | `ProvisionableDeviceResourcesIntegrationTests` covers connected composition, same-runtime replacement and remove/re-add across pinned retirement. |

[Section 8.1](https://reference.opcfoundation.org/specs/OPC-10000-21/v1.05.04/8.1)
permits vendor-specific unprotected metadata, including an RFC 3161
timestamp; it does not define a standard timestamp field or require that
extension. This validator verifies the signature and the signer's current
certificate trust without interpreting such metadata. A host that needs
timestamp-based historical validation must supply that policy explicitly,
not assume that an unprotected timestamp changes the accepted validity
period.
[Section 7.4](https://reference.opcfoundation.org/specs/OPC-10000-21/v1.05.04/7.4)
allows external authentication, including an FDO handoff, as an alternative
to OPC UA ticket/identity authentication. It does not make an FDO stack a
dependency of the ticket-based pull and push paths described here. The registrar
handoff is implemented through the configured provider and does not fabricate
a ticket when the external service has authenticated the device.

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
certificate-management services it uses. Pending test execution is a validation
status, not proof of a missing behavior; the source gaps identified above remain
the reason for the Partial rating. The external certificate handoff does not
establish FDO protocol interoperability.

## Replace a provisionable device's node manager

A host can replace the address-space manager for a running
`ProvisionableDevice` without replacing its runtime or its managed
applications. Use `ProvisionableDeviceNodeManagerFactory.QueueReplacement` from
host control-plane code with the exact current `NodeManagerRegistration`.
This applies to a single replacement on the standard server. Generic
`PrepareAsync` and `ApplyAsync` are not the handoff entry points.

Use the same factory retained at registration or resolved from dependency
injection. The worker retains the exact device runtime, application provider,
security and update policies, and the original managed target bindings,
including their effective key stores and file providers. Replacement is not a
way to reload those providers or change the list of applications. The stock
Part 22 `Resources` manager continues to own the parent node.

`Good` means the worker accepted the request, not that publication succeeded.
Use lifecycle inspection and host telemetry to confirm the outcome. Never
wait for publication inside a device callback. Once the worker pauses the
predecessor, new device operations receive retryable `BadWouldBlock` while
admitted calls and their effects finish. If an open file or TrustList handle,
an active transaction, or pending file work cannot transfer, the worker
rejects the replacement instead of cancelling or migrating that work.

The successor uses the same target engine and reservation with new nodes and
handlers; initialization and recovery do not run again. Only the generation
in the published route can admit new effects. A failure before publication
starts can reopen the still-selected predecessor unless shutdown has begun.
A failure after irreversible publication starts but before the route changes
leaves the device closed and the lifecycle writer faulted; restart the host
rather than retrying the operation. If the route has already changed, a
cleanup failure does not reinstate the predecessor.
