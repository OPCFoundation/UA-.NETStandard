# OPC 10000-21 Onboarding Demo

This sample provides OPC 10000-21 registrar administration and separate opt-in
single-device pull and push registrar modes. It uses the model and runtime APIs shipped
by this repository:

- `OnboardingRegistrar` loads the generated `Opc.Ua.Onboarding` model,
  exposes the standard `DeviceRegistrar_Administration` object, and binds
  `RegisterTickets` / `UnregisterTickets` to an injected
  `MemoryTicketStore` and ticket validator. Its authority trust-list file
  services share the managed ticket and device-identity stores.
- `OnboardingClient` owns one managed session and resolves the well-known
  registrar NodeId. Ticket administration, software-status reporting, and the
  pull workflow use that same session, which the sample disposes when the
  operation completes or fails. Software-status reporting uses the generated
  registrar client.
- `run-onboarding-demo.ps1` builds both applications and prepares signed
  tickets with fresh certificate-store keys. It exchanges the applications'
  public channel certificates and imports the public ticket signer into
  the registrar's ticket authority store before starting the server,
  checks untrusted-channel and anonymous rejection, and runs authenticated
  ticket administration.
  The registrar process is stopped in a `finally` block.

The default demo covers ticket administration. Select the registrar's
separate pull mode to host the standard `DeviceRegistrar` and GDS
`Directory` objects for one explicitly approved device. The client can
also use its pull-DCA mode against an independently configured registrar.
The separate push mode operates against an independently configured device
server. The administration script does not exercise either provisioning mode.
`OnboardingDevice` separately hosts a singleton provisionable device for push
management, restart and reset, with opt-in device-initiated pull provisioning
through the same security state machine.

## Run the complete demo

From the repository root:

```powershell
pwsh samples/Gds/run-onboarding-demo.ps1
```

The script uses
`opc.tcp://localhost:62560/OnboardingRegistrar` by default. Select a
different port with:

```powershell
pwsh samples/Gds/run-onboarding-demo.ps1 -Port 62660
```

Successful output ends with:

```text
REGISTER Good Good
INVALID_TICKET_REJECTED BadTicketInvalid
UNREGISTER Good
UNREGISTER_AGAIN BadNotFound
ONBOARDING_DEMO_OK
```

Pass `-Keep` to retain the temporary PKI stores and registrar logs for
diagnostics. Use `-NoBuild` to run existing net10.0 Debug sample assemblies;
the script reports an error if either assembly is missing.
Each client process has a 120-second limit, adjustable with
`-ClientTimeoutSeconds`. On timeout, the script stops that client process
and reports its captured output; registrar cleanup still runs.
The client also passes a cancellation token through its operations with a
90-second deadline, configurable as `--operationTimeoutSeconds` from 1 to
3600. Host shutdown has a separate 10-second budget after cancellation.

## Run the applications manually

Set `ONBOARDING_DEMO_USER` and `ONBOARDING_DEMO_PASSWORD` to the same
non-empty values in both terminals. Select fresh client and registrar PKI
roots and prepare their channel certificates and tickets before starting
either application:

```powershell
dotnet run --project samples/Gds/OnboardingClient -f net10.0 -- `
  --prepareTickets true `
  --pkiRoot "$env:TEMP\opcua-onboarding-client" `
  --registrarPkiRoot "$env:TEMP\opcua-onboarding-registrar" `
  --ticketAuthorityTrustPath "$env:TEMP\opcua-onboarding-registrar\ticket-authorities\trusted"
```

The preparation step writes `ticket-1.json` and `ticket-2.json` under the
client PKI root. Each application's private key remains in its own
certificate store; the other application's `trusted` store receives only
its public certificate. Ticket-signing keys remain in the client's
`ticket-signing` store, and the registrar's selected ticket authority store
receives only the public signer. Preparation rejects nonempty application
certificate stores rather than leaving ambiguous certificates for startup
selection. Choose new PKI roots for another preparation run.

Start the registrar:

```powershell
dotnet run --project samples/Gds/OnboardingRegistrar -f net10.0 -- `
  --port 62560 `
  --pkiRoot "$env:TEMP\opcua-onboarding-registrar"
```

After `ONBOARDING_REGISTRAR_READY` is printed, run the client:

```powershell
dotnet run --project samples/Gds/OnboardingClient -f net10.0 -- `
  --endpoint "opc.tcp://localhost:62560/OnboardingRegistrar" `
  --pkiRoot "$env:TEMP\opcua-onboarding-client"
```

## Host the connected pull registrar

`--pullRegistrar true` selects `AddGdsServer` and `AddDeviceRegistrar`
instead of the administration-only node manager. Configure
`--allowedProductInstanceUri` with the exact product instance URI in the
device's validated identity and `--dcaApplicationUri` with that device's
actual DCA application URI. A valid ticket alone does not admit another
device. This sample accepts only the configured product and supplies a
client-only `OnboardingClient` application record for its DCA.

Prepare channel trust and ticket authority trust as described above.
Also populate the registrar's `identity-authorities\trusted` and
`identity-authorities\issuers` stores with the authorities required to
validate the offered device identity, and trust its channel certificate
through the registrar's ordinary peer trust store. Trust the registrar's
channel certificate at the device. These are separate trust decisions;
the sample does not accept an unknown certificate during connection.

Set the same administrator environment variables as for the default
mode, then start the registrar with the approved URIs:

```powershell
dotnet run --project samples/Gds/OnboardingRegistrar -f net10.0 -- `
  --port 62560 `
  --pkiRoot "D:\temp\configured-registrar-pki" `
  --pullRegistrar true `
  --allowedProductInstanceUri "<approved product instance URI>" `
  --dcaApplicationUri "<actual DCA application URI>" `
  --autoApprove true
```

`--autoApprove true` is an explicit demonstration option, not a production
approval policy. Without it, certificate requests remain pending; this
sample does not supply an approval UI. The configured administrator has
`RegistrarAdmin`, `DiscoveryAdmin`, and `CertificateAuthorityAdmin` roles
in this mode. Use `--anonymous true` for the device command; only the
named administrator account receives these roles. Device certificate
requests pass through the registrar's identity, lifecycle and software
authorization checks.

This mode assumes that the operator has already approved the device's
installed software. It does not advertise software-update or configuration
managers, rejects push scheduling with `BadNotSupported`, and denies
management of other device applications. It is not a push-onboarding
sample or a production registrar.

Application records and pending request state use `applications.json`;
device identity and certificate lifecycle state use
`device-lifecycle.json`, both under the selected PKI root. The host binds
these providers to its actual namespace table. Use one process and keep
the PKI root and CA keys together across restarts. Accepted tickets use
`tickets.bin` through `AddFileSystemOnboardingTicketStore`; the exact
envelopes and metadata survive restart, and unreadable or corrupt ticket
state is reported instead of being replaced. The file is limited to
16 MiB and 10,000 tickets and must be protected against other writers.
Issuance receipts remain in memory. Do not rely on this sample to retain
consumed-request receipts or generated private-key responses across
registrar restarts; a production host needs a durable issuance provider.

## Host the connected push registrar

`--pushRegistrar true` selects the hosted GDS and its supervised push processor.
The registrar admits one configured product instance after signed-ticket and
device-identity validation, requests a CSR from that device, and obtains its
certificate through the GDS request queue and CA. It applies the device TrustList
before transferring the certificate over a new connection pinned to the same
configuring peer. A third connection must prove the installed certificate before
the registrar promotes the lifecycle binding. The sample can also provision one
explicitly configured managed Server or ClientAndServer application after DCA
proof; it does not accept other managed applications.

Use an absolute, operator-controlled `--pkiRoot`. Prepare the registrar's channel,
ticket-authority and device-identity trust as for pull mode. The registrar's client
trust store must also accept the CA that will issue the device certificate,
because the final connection validates that new certificate. Configure the
device to trust the registrar's application certificate and authorize the
outbound user for standard certificate and TrustList administration. Discovery
does not add certificates to either peer's trust store.

Set `ONBOARDING_DEMO_USER` and `ONBOARDING_DEMO_PASSWORD` for inbound registrar
administration. Set `ONBOARDING_PUSH_USER` and `ONBOARDING_PUSH_PASSWORD` separately
for the registrar's outbound device sessions; passwords are not command-line
arguments. Start with the actual product, DCA URI, endpoint and device model
addresses:

```powershell
dotnet run --project samples/Gds/OnboardingRegistrar -f net10.0 -- `
  --pkiRoot "D:\temp\configured-push-registrar-pki" `
  --pushRegistrar true `
  --allowedProductInstanceUri "<approved product instance URI>" `
  --dcaApplicationUri "<actual DCA application URI>" `
  --pushEndpoint "<actual opc.tcp device endpoint>" `
  --pushDeviceId "<ProvisionableDevice expanded NodeId>" `
  --pushIsSingletonId "<IsSingleton expanded NodeId>" `
  --pushServerConfigurationId "<ServerConfiguration expanded NodeId>" `
  --pushCertificateGroupId "<application certificate group expanded NodeId>" `
  --pushCertificateTypeId "i=12560" `
  --pushTrustListId "<group TrustList expanded NodeId>" `
  --autoApprove true
```

To authorize one managed application, replace the final
`--autoApprove true` line in the push registrar command with:

```powershell
  --autoApprove true `
  --managedApplicationUri "<managed application URI>" `
  --managedApplicationEndpoint "<actual encrypted opc.tcp endpoint URL>" `
  --managedApplicationSecurityPolicyUri "<endpoint security policy URI>" `
  --managedApplicationServerCapabilities "<comma-separated capability identifiers>" `
  --managedApplicationCertificatePath "D:\temp\managed-server.der" `
  --managedApplicationCertificateTypeId "i=12560"
```

This sample uses the endpoint URL as the discovery URL recorded in the GDS
application entry. The provider contract also accepts distinct discovery and
connection URLs; a custom provider can supply them separately. Discovery must
return the selected connection endpoint with the configured application
URI and security policy. UA-TCP requires `SignAndEncrypt`; an HTTPS managed
endpoint requires `Sign`. The policy must be
`Basic256Sha256`, `Aes128_Sha256_RsaOaep` or `Aes256_Sha256_RsaPss`; SHA-1
policies are rejected. The certificate file must contain the
currently installed public X.509 certificate in DER form and must not contain a
private key. Set `ONBOARDING_MANAGED_APPLICATION_USER` and
`ONBOARDING_MANAGED_APPLICATION_PASSWORD` for the host-selected application
identity. Startup imports the managed password into a dedicated
`InMemorySecretStore`; `UserNamePasswordIdentityProvider` resolves it for each
attempt. The credential registry is separate from the public enrollment and
issuance checkpoints and does not write passwords to them.
The sample allowlists only that URI for the configured product
instance.
`--managedApplicationCertificateTypeId` is optional. When omitted, the
registrar uses `--pushCertificateTypeId`; when supplied, it selects that
expanded NodeId for this managed application and rejects provisioning unless
the application's default application group advertises the type.

For an HTTPS managed endpoint, set `--managedApplicationEndpoint` to its actual
`https://` or `opc.https://` URL. By default, discovery and application connections
use operating-system TLS trust and revocation checking. To select an exact TLS
leaf pin instead, add
`--managedApplicationHttpsCertificatePath "D:\temp\managed-tls.der"`.
This must be a public DER certificate; hostname and validity checks still apply
on every handshake, including replacement connections. It is independent of
`--managedApplicationCertificatePath`, which pins the OPC UA application
certificate. The application must prove that key and return the configured
application URI before credentials are sent. The TLS terminator is part of the
trust boundary; legacy application signatures do not cryptographically bind it.
The device's separate `--pushEndpoint` remains UA-TCP.

`--managedApplicationServerCapabilities` is required with managed application
configuration. Supply the capability identifiers selected by the application
owner, such as `DA` only when that application supports Data Access. Empty or
blank identifiers reject configuration. Discovery does not supply this value;
if capabilities are unknown, the owner must explicitly select the standard
unknown-capability identifier rather than relying on a sample default.

Before starting the registrar, place the currently installed managed-server
certificate or its issuer and the GDS issuance CA in the registrar client's
trusted peer store under `--pkiRoot`. The sample disables automatic certificate
acceptance. On the initial attempt, the configured certificate must match the
actual secure-channel peer and the application's `GetCertificates` result.
After the push, a fresh channel must present the issued certificate. The
certificate file is the initial operator pin and does not need to be updated
after rotation. If the application reports a different certificate on retry,
the registrar accepts it only when it exactly matches the retained issuance
receipt for this device and application, then proves it on a fresh secure
channel. It does not use discovery to select the replacement pin or create
another issuance. Any other certificate state is rejected before the processor
changes the application. The registrar client's trust store must still trust
the actual issued certificate or its issuer. Discovery results do not establish
trust.

Replace the device NodeId placeholders with addresses from its actual model.
Addresses outside namespace zero must use `nsu=` namespace URIs, not transient
namespace indexes. The sample selects `SignAndEncrypt` with `Basic256Sha256` and
the standard RSA SHA256 application certificate type. Pull and push registrar
modes cannot run together in one sample process. `--autoApprove true` has the
same demonstration-only meaning as in pull mode.

Startup queues one attempt and prints `ONBOARDING_PUSH_QUEUED`; this marker does
not indicate successful provisioning. Startup does not determine whether an
earlier attempt was interrupted. Do not restart the sample to retry an
interrupted attempt until that attempt has been reconciled. The processor retains
pending issuance and installation evidence. An uncertain CSR or request
creation requires the enrollment provider's explicit original-evidence
recovery APIs; this sample has no reconciliation or retry UI.

The `applications.json`, `device-lifecycle.json` and `tickets.bin` files retain
their roles described above. Push mode also keeps public enrollment journals
and signing receipts under `push-checkpoints`. One process holds an exclusive
directory lease and serializes checkpoint publication. Keep the directory,
application database and CA keys together across restarts. The sample rejects
new-key issuance receipts rather than writing private keys to public checkpoint
files; its `ISecretStore` adapter accepts only the enrollment provider's exact
journal identifiers and must not be used as a general secret store.

Without a software manager, this mode assumes operator approval of the installed
software. To require an externally operated manager, supply all of
`--softwareManagerEndpoint`, `--softwareManagerPurposeUri` and
`--softwareManagerProtocolUri`. The registrar advertises that manager and stops
the push attempt while the recorded status requires an update. In that mode the
configured sample administrator also receives `SecurityAdmin` to report the
result. The registrar does not execute firmware updates.

After the device is registered, an operator can report its software status
with the existing `ONBOARDING_DEMO_USER` and `ONBOARDING_DEMO_PASSWORD`
credentials. Configure mutual channel trust for the administrative client and
registrar as described above, then use the exact approved product instance URI:

```powershell
dotnet run --project samples/Gds/OnboardingClient -f net10.0 -- `
  --endpoint "opc.tcp://localhost:62560/OnboardingRegistrar" `
  --pkiRoot "D:\temp\configured-admin-client-pki" `
  --reportSoftwareStatus true `
  --productInstanceUri "<approved product instance URI>" `
  --softwareRevision "<installed software revision>"
```

`true` reports that the installed software is up to date. Use `false` and the
required revision when an update is still needed. The command calls
`UpdateSoftwareStatus` over the authenticated session and prints
`ONBOARDING_SOFTWARE_STATUS_REPORTED` only after the registrar accepts it.
It does not perform an update or queue another push attempt. If the first
attempt has stopped at the software gate, reporting approval does not resume
it; the sample has no reconciliation or retry UI.

Use the separate device host below to expose standard push and reset
operations; the administration script does not exercise that host.

## Host the connected device

`OnboardingDevice` hosts the generated `ProvisionableDevice` methods and the
standard `ServerConfiguration` certificate, TrustList and reset methods. It
uses the concrete certificate-store installer and the same device security
state machine for registrar ownership, installation completion and recovery.
The host is a net10.0 executable configured for Native AOT publication.

The factory adds a Part 22 `Resources` parent reference to the device object.
Shared Base Network integration and connected validation remain pending; this
source state does not establish full profile conformance. The sample exposes
one singleton DCA/operational server by default. Set
`--managedApplicationUris` to a semicolon-separated list of one to 32 distinct
absolute application URIs to publish separate client application components
under a non-singleton device. Each client has its own certificate manager and
private and trust stores beneath `managed-applications` in the selected PKI
root. The sample does not start those clients or maintain their outbound
sessions; their push callbacks therefore have no active channels to retire or
revalidate. This mode publishes and manages multiple application configurations.
Supply the same URI list on restart to publish the same components. Their
certificate and trust stores remain under the URI-specific directories. The
sample does not exercise recovery of live client sessions.

Prepare an absolute, operator-controlled PKI root before startup. Install the
factory RSA application certificate and its private key in `own`, and supply
that same certificate as an independent public DER file. The certificate must
contain both the selected application URI and product instance URI. Startup
disables automatic certificate creation: a missing key or mismatched identity
stops the process rather than creating a replacement identity.

Prepare ordinary channel trust for the registrar, plus separate
`ticket-authorities\trusted`, `ticket-authorities\issuers`,
`identity-authorities\trusted` and `identity-authorities\issuers` stores under
the PKI root. Supply existing signed device-ticket envelopes matching the
factory identity. Startup verifies the signatures and identity trust before
publishing any device methods. Unknown channel certificates are not accepted
through a callback.

Set `ONBOARDING_DEVICE_USER` and `ONBOARDING_DEVICE_PASSWORD` for the
registrar's authenticated outbound login. To permit standard certificate and
TrustList administration, explicitly set `ONBOARDING_DEVICE_ADMIN_USER` to
that exact username; otherwise the account receives only `AuthenticatedUser`.
Use those credentials as `ONBOARDING_PUSH_USER` and `ONBOARDING_PUSH_PASSWORD`
in the push registrar process.

```powershell
dotnet run --project samples/Gds/OnboardingDevice -f net10.0 -- `
  --pkiRoot "D:\temp\configured-device-server-pki" `
  --applicationUri "<actual DCA application URI>" `
  --productUri "<device product URI>" `
  --productInstanceUri "<approved product instance URI>" `
  --subjectName "<factory application certificate subject>" `
  --endpointUrl "opc.tcp://localhost:62561/OnboardingDevice" `
  --factoryCertificate "D:\temp\factory-identity.der" `
  --ticketFiles "D:\temp\device-ticket.json"
```

To publish two managed client applications, add
`--managedApplicationUris "urn:example:application:client-1;urn:example:application:client-2"`
to the command.

`--ticketFiles` accepts up to 32 semicolon-separated absolute paths. Each
input must be an unchanged signed UTF-8 envelope, at most 1 MiB. Duplicate
envelopes are rejected. Keep the original DER and signed inputs available on
restart; the host revalidates them rather than trusting cached input metadata.
Protect all input and state paths against modification by other applications.

`ONBOARDING_DEVICE_READY` is printed only after startup recovery and model
publication. The namespace URI is
`urn:opcfoundation.org:UA:Onboarding:Instances`; use
`nsu=urn:opcfoundation.org:UA:Onboarding:Instances;s=ProvisionableDevice`
and its `ProvisionableDevice/IsSingleton` child for the registrar's device
selectors. Browse the standard `ServerConfiguration` application certificate
group and TrustList for the other push selectors.
`ProvisionableDevice/RequestTickets` returns the validated signed envelopes,
and `ProvisionableDevice/SetRegistrarEndpoints` persists authorized candidate
changes. Candidate persistence does not start outbound pull orchestration
unless the operator also selects a pull registrar on startup.

To initiate pull from this device host, first run without pull options and use
the authorized `Device/SetRegistrarEndpoints` method to persist the registrar's
application description and discovery endpoint. Keep its actual channel
certificate trusted in the device's ordinary peer store. Restart with the same
device inputs and add both options:

```powershell
--pullRegistrarEndpoint "opc.tcp://localhost:62560/OnboardingRegistrar" `
--pullRegistrarApplicationUri "<actual registrar application URI>"
```

Set `ONBOARDING_PULL_USER` and `ONBOARDING_PULL_PASSWORD` for the device's
outbound login to that registrar. The password is placed in the host's ordinary
in-memory secret store, never in the public checkpoint files. The endpoint must
match exactly one persisted discovery entry for the selected application URI;
the host rejects a missing or ambiguous candidate before connecting. It then
checks the actual encrypted channel certificate and application URI before
offering device tickets. Discovery metadata does not grant certificate trust.

The host runs one bounded two-minute pull operation before printing readiness.
Each attempt owns one session; the workflow and installer borrow that same
session. Overlapping attempts are rejected, and the host disposes the session
when the attempt completes or fails. A disposal failure prevents another attempt.
It reuses the factory private key for the CSR and routes the issued certificate
and complete TrustList through the device installer. Successful installation
prints `ONBOARDING_DEVICE_PULL_INSTALLED`; startup with an already installed
journal skips issuance and prints `ONBOARDING_DEVICE_PULL_ALREADY_INSTALLED`.
Failures stop startup and remain visible to the caller. Installation interrupted
after its checkpoint is saved uses normal device recovery on restart. The
sample does not retain an unfinished registrar signing request, and it rejects
a registrar-required software update because no software engine is registered.
Do not combine pull options with `--physicalReset true`; reset and reprovision
are separate invocations.

Keep the same PKI root across restarts. `tickets.bin` and
`registrar-candidates.bin` retain tickets and candidate endpoints; `device-state`
retains the device security journal and original trust defaults through an
exclusive writer lease. The public checkpoint adapter accepts only those two
exact device-scoped producer identifiers and is not the host's general secret
store. Original identity keys remain in `device-identity`; rotating application
keys remain in `own`. Removing the journal while retaining an operational
certificate does not authorize recapturing that certificate as the factory
identity.

Add `--physicalReset true` for an explicit local reset before listeners open.
Remove the option on the next normal restart unless another reset is intended.
Network reset uses the standard authorized `ResetToServerDefaults` method.
Both paths restore retained factory security through the same provider; they
do not remove installed software. Missing original defaults or key material
stop recovery and prevent readiness.

This device sample does not register a software-update engine or readiness
policy and therefore represents a deployment without required software updates.
A host that requires updates registers its existing manager through
`AddProvisionableDeviceSoftwareUpdatePolicy`; incomplete updates then prevent
the concrete installer from activating or accepting DCA credentials. The
registrar's separate software approval does not implement that device-side
update.

## Pull DCA against a configured registrar

The client registers `PullDcaWorkflow` through `AddPullDcaWorkflow` when
`--pull true` is selected. Its `OnboardingDeviceCredentials` provider
selects the exact offered RSA identity from the local certificate store,
creates a signing request with that key, validates the issued certificate
against the received complete TrustList, and installs the certificate
and trust through the existing certificate manager. The workflow then
reconnects the managed session with the installed DCA certificate.
Certificate-store creation uses the host's registered
`ICertificateStoreProvider` services before the built-in store types.

This mode requires a registrar exposing the standard `DeviceRegistrar`
and GDS `Directory` objects. Configure the client PKI with the device's
private-key-bearing identity certificate and its issuer chain, and
explicitly trust the registrar's channel certificate before starting.
The signed device ticket must match that identity. The registrar must
authorize the device, approve signing requests, and supply the complete
DCA TrustList, including the revocation information its policy requires.
The sample does not register a software-update provider; a registrar
requiring software updates is rejected rather than bypassing that step.

```powershell
dotnet run --project samples/Gds/OnboardingClient -f net10.0 -- `
  --endpoint "opc.tcp://registrar.example:4840" `
  --pkiRoot "D:\temp\configured-device-pki" `
  --anonymous true `
  --pull true `
  --deviceTicket "D:\temp\device-ticket.json" `
  --exportSigningKeyCertificate "D:\temp\original-signing-identity.der"
```

`--anonymous true` selects the session user identity, not an unsecured
channel: the connection still requires `SignAndEncrypt` and configured
registrar trust. A registrar requiring a named user can instead use the
environment-variable credentials described above.

`--exportSigningKeyCertificate` saves the public certificate identifying
the signing key before the request starts. It does not export the private
key, which must remain in the original PKI root. If approval remains
pending when the bounded polling period ends, the client prints
`PULL_DCA_PENDING_APPLICATION` and `PULL_DCA_PENDING_REQUEST` with
namespace-URI-qualified identifiers. Save both values.

Resume that pending request without calling `ProvideIdentities` or
starting another signing request:

```powershell
dotnet run --project samples/Gds/OnboardingClient -f net10.0 -- `
  --endpoint "opc.tcp://registrar.example:4840" `
  --pkiRoot "D:\temp\configured-device-pki" `
  --anonymous true `
  --pull true `
  --resumeApplicationId "<saved PULL_DCA_PENDING_APPLICATION value>" `
  --resumeRequestId "<saved PULL_DCA_PENDING_REQUEST value>" `
  --resumeSigningKeyCertificate "D:\temp\original-signing-identity.der"
```

Use the original endpoint, application configuration and PKI root. The
client proves that the original private key remains available, restores
the application/group/type binding, and polls the existing request. After
installation it reconnects with the DCA certificate.
All three resumption options are required together. An exported public
certificate alone cannot restore a missing private key.

This sample mode provisions one DCA, not other device applications. It
does not persist workflow checkpoints or make certificate-store,
TrustList, and configuration writes one transaction. Installation errors
propagate and may leave completed store writes. Manual pending-request
resumption is not recovery of a lost accepted `FinishRequest` response.
It signs with the long-lived identity key, so a certificate assigned for
an earlier request with that key is indistinguishable from the current
one. Without the signing journal described next, a consumed request
therefore returns its error rather than accepting an assigned certificate
of unknown origin.

### Use an externally installed DCA certificate

The registrar and client can use the Part 21 §7.4 certificate handoff without
a device ticket. An external authentication service must first install the
DCA certificate and its private key. The certificate must contain the configured
product instance URI and DCA application URI as URI subject alternative names.
For `OnboardingClient`, the application URI is
`urn:localhost:OPCFoundation:OnboardingClient`; use that URI as the registrar's
`--dcaApplicationUri` and configure `--productInstanceUri` for the device.

Add `--externalAuthentication true` to the connected registrar command
(`--pullRegistrar true` or `--pushRegistrar true`). Before startup, populate
`<pkiRoot>\external-authentication\trusted` with the authentication-service CA
and its CRLs, and `<pkiRoot>\external-authentication\issuers` with any issuer
chain and issuer CRLs. The dedicated list does not replace normal OPC UA
channel trust: configure peer trust on the registrar and device separately.
The sample approves only the configured product instance and DCA application
pair, even if the CA has issued certificates to other devices.

Start the client with its externally provisioned PKI root:

```powershell
dotnet run --project samples/Gds/OnboardingClient -f net10.0 -- `
  --endpoint "opc.tcp://registrar.example:4840" `
  --pkiRoot "D:\temp\external-device-pki" `
  --anonymous true `
  --pull true `
  --externalAuthentication true
```

This mode disables certificate auto-creation and requires the installed DCA
private key. It calls `GetManagers` through `RunExternalAuthenticationAsync`,
prints `PULL_DCA_EXTERNAL_AUTHENTICATED` and the manager count, and leaves the
DCA certificate unchanged. It does not combine with ticket preparation,
ticket inputs or signing-journal/resumption options. The client sample has no
software-update engine or managed-application inputs; a host uses the returned
managers to complete required software updates and supplies its applications
to the typed workflow afterwards.

Registrar push uses the same approval provider and retained external evidence
without requesting tickets from an externally authenticated device. The
`OnboardingDevice` sample remains the ticket-based factory-identity example;
it does not run an external authentication service or install its DCA credential.
FDO discovery, ownership vouchers, TO0/TO1/TO2 and `fdo.csr` exchange remain
outside these samples. No FDO SDK dependency or FDO interoperability result is
provided. See [the handoff contract](../../docs/GDS.md#external-authentication-certificate-handoff)
for provider registration and recovery behavior.

### Recover a lost FinishRequest response

The registrar can accept `FinishRequest` and issue the certificate while
the response is lost. A retry then returns `Bad_InvalidArgument`, and
OPC 10000-12 §7.9.5 lets a `StartSigningRequest` client read the assigned
certificate through `GetCertificates` (§7.9.8). That certificate is only
safe to install when it matches the retained key and request context.
The opt-in signing journal records that context and gives each request
its own key:

```powershell
dotnet run --project samples/Gds/OnboardingClient -f net10.0 -- `
  --endpoint "opc.tcp://registrar.example:4840" `
  --pkiRoot "D:\temp\configured-device-pki" `
  --anonymous true `
  --pull true `
  --deviceTicket "D:\temp\device-ticket.json" `
  --signingJournal "D:\temp\device-state\signing-request.journal" `
  --signingKeyStore "D:\temp\device-state\signing-keys"
```

Both options are required together and only with `--pull true`. For each
new request the client creates a fresh RSA key and CSR, records the
public request data, and retains the private key in the directory store
at `--signingKeyStore` before submitting the CSR. It reloads the stored
key to confirm that private-key retention succeeded. The signing store
must differ from the application certificate store. The private
key exists only in that certificate store. The journal file holds public
data: the key's certificate, the CSR, and the namespace-URI-qualified
application, certificate group and certificate type. The client writes
the journal in the `Prepared` state before it calls `StartSigningRequest`.
It adds the registrar and the returned request ID in the `Started` state
before the first `FinishRequest`. A certificate issued for an earlier
request cannot match a key used only for the current request.

When the `FinishRequest` retries end in `Bad_InvalidArgument`, the
workflow reads `GetCertificates` and the complete TrustList for the
journaled group and type. The client installs the returned certificate
only when all of the following hold:

- The journal is `Started` and its registrar, application, request ID,
  group and type equal the values the workflow is completing.
- The retained private key is still in the signing-key store.
- The certificate carries the application URI, is an RSA certificate
  for the requested type, and contains the retained public key.
- The certificate chain validates against the received TrustList,
  including its issuers and revocation lists.

Any failed check rejects the certificate and installs nothing. After
installation the client marks the journal `Installed`, then deletes the
retained key and the journal.

Run the same command after a restart. The client acts on the journal
state before it calls `ProvideIdentities`:

- `Started`: the client restores the retained key and binding, completes
  the existing request without starting another, and prints
  `PULL_DCA_REQUEST_INSTALLED`. A journal started at another registrar
  is rejected.
- `Prepared`: the `StartSigningRequest` outcome is unknown, and the
  client stops. Run once with `--discardSigningRequest true`, which
  deletes the retained key and the journal and prints
  `PULL_DCA_REQUEST_DISCARDED`, then start a new request. The client
  never repeats `StartSigningRequest` with a retained key. A request the
  registrar did accept remains there, without a key to complete it.
- `Installed`: the process stopped after installation but before
  cleanup. The client deletes the retained key and the journal and
  prints `PULL_DCA_REQUEST_CLEANED`.

`--discardSigningRequest true` also abandons a `Started` request that
must not be completed.

The journal assumes one writer. It takes no lock across processes or
hosts, so it does not support shared storage or a highly available
device pair. It cannot be combined with the `--resume*` options. The
journal update, key deletion and certificate installation are ordered
but not one transaction; the restart states above define which step
runs next.
Protect both paths from writes by other applications. A restart after
the issued key pair was stored but before trust installation completed
reuses that exact key pair and repeats the remaining installation steps;
it does not add a duplicate certificate. Failure to reload the stored
private key stops installation.

## Wire contract

The client API uses the Part 21 types directly:

```csharp
ArrayOf<StatusCode> results = await onboarding.RegisterTicketsAsync(
[
    firstTicketJson,
    secondTicketJson
]);
```

`EncodedTicket` is a subtype of `String`. Each input is the signed JSON
envelope itself. The server returns one `StatusCode` for every supplied
ticket. It verifies the JWS signature and ticket authority before storing
the payload and its identity metadata; malformed tickets return
`BadTicketInvalid`.

## Security

The demo selects `SignAndEncrypt` with `Basic256Sha256`. Both applications
reject untrusted channel certificates. The preparation step creates their
keys in isolated temporary PKI roots and exchanges their public
certificates locally before either application starts; discovery does not
grant trust. The script also generates a one-run random
username and password, grants that user the generated `RegistrarAdmin`
role, and restricts the ticket methods and authority trust lists to that
role. Authority file operations also require an encrypted authenticated
SecureChannel. The endpoint
also permits an anonymous bootstrap session because `ManagedSession`
activates its configured identity provider after creating the session;
anonymous users have no permission to call the registrar methods.

The local preparation step is demo setup, not a production trust
distribution mechanism. A production registrar must use managed trust
lists, authenticate and authorize registrar administrators, persist
tickets in a protected store, and apply the audit/redaction requirements
described in the
[GDS developer guide](../../docs/GDS.md).

Channel trust does not grant ticket-signing authority. The signer must be
trusted through the `ticket-authorities` stores, even in this local demo.
For the channel-rejection check, preparation places the same client key
in a second temporary application store without registrar trust. The
registrar therefore recognizes the client's channel certificate; the
rejection tests the client's missing registrar trust, not an unknown
client certificate at the registrar.
The default administration demo's memory ticket store does not survive a
registrar restart; the connected pull mode uses the filesystem store
described above. Do not reuse demo credentials or keys in a production
deployment.
