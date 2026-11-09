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
  - [Security: Authenticated SecureChannel Validation](#security-authenticated-securechannel-validation)
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

For the connected server's own `ConfigurationFile`, use an explicitly
configured endpoint and provide the complete serialized file content, target
paths and timing values:

```csharp
await using var pushClient = new ServerPushConfigurationClient(appConfig);
await pushClient.ConnectAsync(configuredEndpoint);

ConfigurationFileUpdateResult result = await pushClient.UpdateConfigurationFileAsync(
    configurationFile,
    targets,
    revertAfterTime: 30_000,
    restartDelayTime: 0);

if (result.Outcome is ConfigurationFileUpdateOutcome.Applied or ConfigurationFileUpdateOutcome.Confirmed)
{
    Console.WriteLine($"Configuration updated to version {result.NewVersion}.");
}
else
{
    Console.Error.WriteLine($"Configuration update outcome: {result.Outcome} ({result.StatusCode}).");
}
```

The method reads `CurrentVersion`, uploads the supplied content and targets,
then reconnects its owned session when the server returns a non-empty
`UpdateId` or `restartDelayTime` is nonzero. It waits that interval before
reconnecting when the delay is nonzero and confirms only the returned
identifier. A borrowed client rejects the operation before dispatch because it
cannot disconnect and reconnect the caller's session. It never retries
`CloseAndUpdate`: if the request or
confirmation response is lost, inspect `Outcome` and `StatusCode` before
deciding what to do next. `OutcomeUnknown` means the client did not receive
enough information to determine whether the update or confirmation completed;
`ConfirmationPending` means the update was applied but the confirmation was not
accepted.

Part 12 §7.8.5.1 requires a Client to read the existing configuration and set
the file position to the beginning before writing. The current
`ServerPushConfigurationClient` opens for Read + Write and writes without
calling `Read` or `SetPosition`. On this server, a Read + Write open starts
with an empty stream; a Read-only open loads the provider's configuration.
That local behavior does not implement the required transfer sequence, and
the handling of shorter replacement content by other servers has not been
established. ConfigurationFile transfer support therefore remains Partial.

[Part 12 §7.8.5.1](https://reference.opcfoundation.org/specs/OPC-10000-12/v1.05.07/7.8.5.1)
permits only Open modes `0x01` and `0x03`, so the Part 20 `EraseExisting` bit
cannot truncate a `ConfigurationFile`. [Part 20 §4.2.5](https://reference.opcfoundation.org/specs/OPC-10000-20/v1.05.06/4.2.5)
defines positional writes but not how a shorter replacement truncates a file.
[Part 12 §7.8.5.2](https://reference.opcfoundation.org/specs/OPC-10000-12/v1.05.07/7.8.5.2)
describes how a server handles configuration contents that the update does not
target. It does not establish that the server may ignore trailing bytes in the
targeted file.
The shorter-replacement behavior remains unresolved.

[OPC 10000-20 v1.05.06 §4.2.2](https://reference.opcfoundation.org/specs/OPC-10000-20/v1.05.06/4.2.2)
permits multiple read handles, requires a write-open to return
`Bad_NotWritable` while any handle is open, and requires a read-open to return
`Bad_NotReadable` while a writer is open. Part 12's
Read/Read + Write mode restriction does not state an exception to those
FileType rules. `ApplicationConfigurationFile.OpenAsync` instead replaces any
existing handle when a new open succeeds and resets `OpenCount` to 1. The
previous handle is therefore lost; a second reader is not retained, and
incompatible opens do not receive the required busy statuses. This is a
separate ConfigurationFile support gap.

For a managed server, the caller creates a separate owned client and connects
it to that application's host-selected endpoint, not the registrar endpoint.
The client resolves `ConfigurationFile` below the connected server's
`ServerConfiguration`; an application record on the registrar does not forward
this operation. The caller supplies the serialized content and update targets
and retains the returned result. The registrar's certificate-proof verifier
closes its session before returning, so it does not lend a session to the update
client or manage this update's confirmation lifecycle.

The target server must configure an `IApplicationConfigurationFileProvider`.
If that provider requires confirmation, it must also implement
`IRecoverableApplicationConfigurationFileProvider`; otherwise the target rejects
`CloseAndUpdate` with `Bad_NotSupported` before validation or provider mutation.
The provider retains the prior configuration and pending update durably, and
the target server restores its apply or revert schedule before serving requests
after restart. Reconnection failure leaves the returned identifier pending;
the client does not repeat the update or cancel the server's revert deadline.
See [provider migration](MigrationGuide.md#migrating-confirmation-enabled-configurationfile-providers).

For an application exposed under a management server's `ManagedApplications`
folder or as a provisionable-device component, use its namespace-URI node address:

```csharp
ExpandedNodeId applicationNode = ExpandedNodeId.ToExpandedNodeId(
    applicationConfigurationNodeId, pushClient.Session!.NamespaceUris);
ConfigurationFileUpdateResult result = await pushClient.UpdateApplicationConfigurationFileAsync(
    applicationNode, configurationFile, targets,
    revertAfterTime: 30_000, restartDelayTime: 0);
```

Connect this owned client to the management server, not to the managed
application's endpoint. The method resolves the selected application's
`ConfigurationFile` on both session generations. It resolves the namespace URI
again after reconnecting and never substitutes the host's `ServerConfiguration`
when the application namespace or file is unavailable. The same result and
uncertain-outcome rules apply as for the host's own file.

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
`CloseAndUpdate` and `ConfirmUpdate`; that Part 12 transaction remains outside
registrar push, and managed application configuration through it stays
partial. `GetCertificates` is optional in Part 12, but the processor needs it
as evidence: a device without it fails the push with the device's status.
Even with it, the result shows what the application reports; the separate
secure-channel check is required to prove the certificate actually presented
by a managed Server.

`AddDeviceRegistrarPush` selects the bounded host-owned queue and one processor;
register it before `AddDeviceRegistrarRuntime`. Queue acceptance remains
process-local. After a restart or interrupted attempt, the host must reconcile
retained registrar and device state before retrying. The queue cannot show
whether accepted work completed. The device-side installer below supplies local
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

``IProvisionableDeviceRegistrarStore.ReadAsync` returns the persisted candidates
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
device resource. Shared Part 22 Resources integration remains a source gap;
connected recovery/reset fixtures are present, with execution status tracked
separately.

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
configured peer trust. It does not resume unfinished signing requests or
execute a software update required by the registrar.

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
Part 22 `Resources` parent reference to the singleton device under the shared
Base Network manager. Connected recovery/reset fixtures are present; their
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

### External authentication certificate handoff

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

### Part 21 requirements and implementation boundaries

Part 21 support remains partial because the provisionable-device factory adds
the Part 22 `Resources` reference without integrating the shared `Resources`
manager. The optional external-authentication handoff uses the provider boundary
described above; FDO itself remains external.
Registrar push uses the configured certificate type by default, and
the host can select a different advertised type for each managed application.
The following map uses
[OPC 10000-21 v1.05.04](https://reference.opcfoundation.org/specs/OPC-10000-21/v1.05.04/)
and separates those implementation gaps from optional Part 12
`ConfigurationFile` behavior and test execution status. A test that has not run
reports an unknown validation result; it does not identify an implementation
gap.

| Requirement | Implementation | Regression sources and remaining boundary |
|---|---|---|
| §§4.2.2, 4.3.2: software approval before operational provisioning | `DeviceRegistrarRuntime`, lifecycle providers, `IPullDcaSoftwareUpdateProvider` | `ProvideIdentitiesRuntimeTests`, `PullDcaWorkflowTests`, `PullRegistrarIntegrationTests`; the connected fixture exercises approval roles and issuance gates, not software-manager execution or a device endpoint gate; remote execution remains required |
| §§4.3.3, 7.2: DCA and managed-application credentials | `PullDcaWorkflow`, generated certificate-directory proxies, application-specific authorization | `PullDcaWorkflowTests`, `ApplicationsNodeManagerOnboardingTests`, `PullRegistrarIntegrationTests`; the connected fixture exercises one DCA's issuance, local installation and new-channel proof; connected multi-application setup and remote execution remain required |
| §§4.3.6, 7.3: security reset and return to onboarding | `ProvisionableDeviceSecurity`, retained configuration owner, concrete installer and existing `IServerConfigurationResetProvider` path | `ProvisionableDevicePushChannelTests.ConcretePushActivatesCertificateAndRecoversAcrossHostRestartAndFactoryReset` covers installed-certificate recovery, factory reset and a replacement registrar; the test's presence does not assert a passing run |
| §§5.1-5.3, 7.1: exact device/composite identity and private-key proof | `OnboardingIdentitySelector`, scoped identity trust, actual-channel registrar authorization, `OnboardingPushIdentityValidator` | `OnboardingIdentitySelectorTests`, `OnboardingIdentityTrustProviderTests`, `DeviceRegistrarRuntimeTests`, `OnboardingPushIdentityValidatorTests`; discovery alone never proves possession |
| §6.4: source-dependent ticket authority trust | `OnboardingTicketValidator`, `OnboardingTicketTrustProvider` | `OnboardingTicketValidatorTests`, `OnboardingTicketTrustProviderTests`; online authority-revocation services are not supplied by these adapters |
| §7.2 and Part 12 §§7.9.5, 7.9.8: request retry and certificate recovery | issuance receipts, client receipts, `IPullDcaSigningRecoveryProvider`, sample signing journal with one fresh retained key per request | issuance-store, receipt-store, pull-workflow and `OnboardingSigningJournalTests` fixtures; remote execution and connected response-loss evidence remain required |
| §7.3: push provisioning, first registrar and installed trust | device security transition provider, standard push authorization and transaction completion, generated-client processor with separate trust/certificate apply connections, secret-store enrollment journal, retained singleton scope and connected registrar/device samples | processor fixtures cover managed Client, Server and ClientAndServer paths; Server paths require host-authoritative endpoint metadata and two fresh-channel certificate proofs. `ConnectedManagedApplicationRecoveryTests.InterruptedManagedRotationReopensFilesAndResumesOnlyRetainedCertificate` covers process-durable recovery through two live servers, and `ProvisionableDevicePushChannelTests.ConcretePushActivatesCertificateAndRecoversAcrossHostRestartAndFactoryReset` covers DCA restart/reset. These fixtures are authored; this map does not assert their execution results. |
| §7.4: alternate authentication/FDO | Dedicated CA validation and configured product/DCA approval through `IDeviceRegistrarExternalAuthenticationProvider`; typed external pull and registrar push reuse shared registrar/Part 12 lifecycle | `ExternalAuthenticationProviderTests`, `ProvideIdentitiesRuntimeTests`, `PullDcaWorkflowTests`, `DeviceRegistrarPushProcessorTests` and enrollment recovery cases cover the handoff. `PullRegistrarIntegrationTests.ExternalHandoffOnLiveChannelRequiresDeviceApprovalBeforePart12Issuance` exercises the provider boundary on real channels with software approval gating. FDO protocols and credential installation remain external; no FDO interoperability result is asserted |
| §§8.1-8.2: signed JSON tickets and String-subtype `EncodedTicket` | `OnboardingTicketWriter`, ticket validator, standard model and strict-UTF-8 compatibility wrappers | writer, validator, onboarding-client and stack-generation fixtures; vendor-specific unprotected metadata is not interpreted and cannot extend signing-certificate validity |
| §§9.2.2-9.2.8: registrar methods and application ownership | registrar runtime, async bindings, shared GDS database and certificate authorization | `DeviceRegistrarRuntimeTests`, `ProvideIdentitiesRuntimeTests`, `ApplicationsNodeManagerOnboardingTests`; database/lifecycle publication is not a distributed transaction |
| §§9.2.9-9.2.15: administration, managed authorities and restricted audit delivery | ticket administration bindings, managed authority file services, `DeviceRegistrarAuditReporter` | administration, authority-list and audit-reporter fixtures; connected authorized/unauthorized event delivery still needs remote execution |
| §§6.4, 9.2.11-9.2.12: accepted ticket retention across restart | `FileSystemTicketStore`, `AddFileSystemOnboardingTicketStore` | `FileSystemTicketStoreTests`; one instance owns the path, and remote execution remains required |
| §§9.3.1-9.3.5: provisionable resource, singleton layout, tickets and registrar endpoints | `ProvisionableDeviceRuntime`, `ProvisionableDeviceNodeManagerFactory`, `AddProvisionableDeviceApplications` and generated node bindings | `ProvisionableDeviceRuntimeTests` cover fluent registration; `ManagedApplicationPushTests` exercise the actual device factory, configuration-file recovery and authorization. The Part 22 manager owns the shared `Resources` object; a full hosted composition test remains absent. |

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

The metadata-backed `DefaultManagedApplicationsNodeManager` uses this store to
enumerate `ApplicationConfigurationState` nodes under ManagedApplications
(§7.10.16). Its address-space binding does not call the store's write or confirm
methods and does not expose a ConfigurationFile transaction:

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

### Independent application push management

Use `IManagedApplicationPushProvider` when each managed application has its own
configuration, certificate manager and runtime. Its registrations pair an
`IApplicationConfigurationPushTarget` with application-specific authorization.
The target owns its configuration and certificate manager; the node manager owns
the push engine and drains its calls and committed effects before disposal.
Targets must keep their configuration and store paths stable for that lifetime.

The same registration is available on regular servers and GDS hosts. Given an
application provider created by the composition root:

```csharp
services.AddOpcUa()
    .AddServer(options => options.ApplicationName = "DeviceManagement")
    .AddManagedApplicationPush(_ => applicationProvider);
```

Each target has separate certificate groups, pending keys and trust handlers in
the node manager's namespace. The binding rejects shared configuration objects,
certificate managers, application URIs, normalized store locations and
pending-key store instances. Store comparison does not resolve filesystem links
or detect arbitrary custom-store aliases; providers must supply independent
backing stores. Authorization requires the standard secure-channel and
SecurityAdmin checks as well as the registered policy.

`ApplyCertificateChangesAsync` activates certificates on the target and retires
its affected channels after the method response grace period.
`ApplyTrustListChangesAsync` revalidates the target's channels and certificate
user identities. Providers implement these effects against their own runtime;
the binding does not close the management server's channels on their behalf.

An application registration can expose its own `ConfigurationFile` through the
same remote node:

```csharp
var registration = new ManagedApplicationPushRegistration(
    applicationTarget, applicationAuthorization,
    configurationFileProvider: applicationConfigurationFileProvider);
```

The provider belongs to that application and remains externally owned.
Bindings reject reuse of the host's or a sibling application's provider instance.
Distinct provider objects must also use independent backing state; the binding
cannot inspect arbitrary external storage aliases. Without a provider, the
optional file node is absent. Metadata-only `IConfigurationDataStore` entries do
not acquire live file handlers.

When confirmation is required, the provider implements
`IRecoverableApplicationConfigurationFileProvider`. Startup recovers each
application's pending update before publishing that application: a due update is
applied, an expired update is reverted, and an already-applied update retains its
original confirmation identifier. File calls use the application's standard
channel and SecurityAdmin checks plus its registered policy. On a provisionable
device they also check the installed registrar and software approval. Deferred
apply and revert effects re-enter the deployment gate after their timer expires;
they do not retain admission during the delay or run after deployment revocation.
Shutdown cancels the timers and drains active file effects before releasing the
binding.

The connected application fixture
`EncryptedChannelRoutesIndependentApplicationsAndEnforcesSecurityAdmin` covers
file upload and exact-identifier confirmation on the selected runtime, and
rejects an observer's file read before provider access.
`ProvisionableDeviceFactoryRecoversApplicationConfigurationBeforePublishing`
covers applied, due and expired recovery through the actual device factory.
`ManagedApplicationUpdateRetainsSelectedOwnerAfterReconnect` covers namespace
index changes between client session generations. These are test-source
references, not an assertion that the current head has passed execution.

For a non-singleton device, register a separate
`IProvisionableDeviceApplicationsProvider` with the server builder:

```csharp
services.AddSingleton<DeviceApplicationsProvider>();
IOpcUaServerBuilder serverBuilder = services.AddOpcUa().AddServer(...);
serverBuilder
    .AddProvisionableDeviceRuntime(isSingleton: false, ticketIds: ticketIds)
    .AddProvisionableDeviceApplications(
        provider => provider.GetRequiredService<DeviceApplicationsProvider>());
```

The provider returns configured application targets and their access policies.
Before publishing the address space, the device node manager creates one
`ApplicationConfigurationType` component for each target. It rejects a
singleton device with application components and rejects duplicate application
URIs. Each push binding requires installed DCA trust, checks the optional
software-update policy, then evaluates the application's access policy. Its
deployment provider covers each method call, certificate registry reload and
deferred runtime effect, so reset cannot overlap those operations or replay an
old deployment's effects after reinstallation. The application provider remains
separate from the GDS `ManagedApplications` provider; it owns target runtimes
and certificate managers, while the device node manager owns the push bindings.
Direct node-manager hosts can use
`BindProvisionableDeviceApplicationsAsync` before publishing an existing
`ProvisionableDeviceState`. This binding does not create Part 22 Resources or
reset an application's stores; the platform reset provider remains responsible
for restoring all device security. Connected rotation, trust-change and reset
evidence across multiple applications remains required.

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

### Security: Authenticated SecureChannel Validation

`AuthorizationHelper.HasAuthenticatedSecureChannel()` enforces that
all GDS methods are called over a signed (or encrypted) secure
channel. If the system context cannot be validated, the method throws
`BadSecurityModeInsufficient`.

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
| `ConfigurationFile` / `ApplicationConfigurationFileType` incl. `CloseAndUpdate` / `ConfirmUpdate` (§7.10.20) | 🔲 provider-gated; ⚠️ transfer and FileType open-handle behavior partial | `IApplicationConfigurationFileProvider` + `IRecoverableApplicationConfigurationFileProvider` + `ApplicationConfigurationFile.cs` | `ServerConfigurationSurfaceTests` (`ConfigurationFilePendingUpdateIsRecoveredAndConfirmedAfterRestartAsync`, `ExpiredConfigurationFileUpdateIsRevertedBeforeServingAfterRestartAsync`, `ConfigurationFileConfirmationWithoutRecoveryIsRejectedBeforeValidationAsync`) |

### GDS-managed applications proxy (§7.10.14–§7.10.16)

The ManagedApplications folder supports independent runtime targets through
`IManagedApplicationPushProvider`, including an optional configuration-file
provider on each registration. Metadata-only registration does not supply push
handlers. `UpdateApplicationConfigurationFileAsync` selects an application by
namespace-URI node address and confirms its update after reconnecting to the
management server; `UpdateConfigurationFileAsync` targets the host itself.

| Section | Feature | Status | Source |
|---------|---------|--------|--------|
| §7.10.14 | `ApplicationConfigurationType` | 🔲 provider-gated | Runtime-backed registrations bind independent certificate, trust and optional configuration-file handlers. File transfer and open-handle limitations are listed separately below |
| §7.10.16 | `ManagedApplications` folder | 🔲 provider-gated | `DefaultManagedApplicationsNodeManager` supports metadata or runtime providers; runtime entries expose per-application update, confirmation and restart recovery through their configured file providers |

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

The following optional Part 12 members are not implemented or remain partial.
The table identifies each gap; assess its effect on the applicable Facet
conformance units separately.

| Item (§) | Status | Notes |
|----------|--------|-------|
| `ServerConfiguration.AuthorizationServices` folder (§9.6.2 / *Authorization Service Configuration Server* CU) | ❌ not exposed | A push Server does not expose the configuration folder listing trusted Authorization Services. Client access-token validation is instead handled by the pluggable [Identity Providers](IdentityProviders.md). |
| `ConfigurationFile` transfer and FileType open handles (§7.8.5.1; Part 20 §4.2.2) | ⚠️ partial | The client does not read the existing content or rewind before writing; this server starts a Read + Write stream empty, while Read-only opens load provider content. The server replaces an existing handle on each successful new open instead of retaining multiple readers or rejecting incompatible opens with `Bad_NotWritable` / `Bad_NotReadable`. Replacement behavior for shorter content on other servers is not established. |
| `ManagedApplications` / `ApplicationConfigurationType` proxy (§7.10.14–§7.10.16) | 🔲 provider-gated | Runtime-backed entries bind independent certificate and trust management plus an optional per-application `ConfigurationFile` provider with update, confirmation and restart recovery. `UpdateApplicationConfigurationFileAsync` selects the same application after reconnecting. Metadata-only `IConfigurationDataStore` entries have no push handlers. The shared ConfigurationFile transfer and open-handle limitations above apply to application files too |
| KeyCredential authentication `ProfileUris` — AMQP SASL Plain / MQTT UserName / UA transport UserName (§8, optional CUs) | 🔲 provider-defined | The push binding is implemented; the concrete ProfileUri set advertised is defined by the credential provider, not fixed by the stack. |
| A & C CertificateExpiration optional CUs — *Comment* / *Confirm* / *Shelving* | 🔲 inherited | Available through the standard OPC 10000-9 condition model on the alarm instances; not separately exercised by the certificate-alarm tests. |
| Onboarding / OPC 10000-21 device setup interplay with `ResetToServerDefaults` / `InApplicationSetup` (§7.10.13, Annex G) | ⚠️ partial | Ticket administration, signing, ticket-scoped identity trust, composite-aware selection and registrar runtime bindings are implemented. GDS issuance receipts and the file-backed registrar lifecycle provider retain state across restart for a single-host deployment. Push queue acceptance remains process-local. After a restart or interruption, the host must reconcile registrar and device state before retrying because the queue cannot show whether an accepted attempt completed. Connected recovery fixtures are present; their execution and interoperability results are not asserted here. |

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
