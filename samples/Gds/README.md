# OPC 10000-21 Onboarding Demo

This sample provides OPC 10000-21 registrar administration and an opt-in
single-device pull registrar. It uses the model and runtime APIs shipped
by this repository:

- `OnboardingRegistrar` loads the generated `Opc.Ua.Onboarding` model,
  exposes the standard `DeviceRegistrar_Administration` object, and binds
  `RegisterTickets` / `UnregisterTickets` to an injected
  `MemoryTicketStore` and ticket validator. Its authority trust-list file
  services share the managed ticket and device-identity stores.
- `OnboardingClient` connects with a managed session, resolves the
  well-known registrar NodeId, and uses the DI-provided
  `Opc.Ua.Gds.Client.OnboardingClient`.
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
The administration script does not exercise pull onboarding.

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
the PKI root and CA keys together across restarts. Tickets and issuance
receipts remain in memory. Reimport tickets after a restart, and do not
rely on this sample to retain consumed-request receipts or generated
private-key responses across registrar restarts. A production host needs
durable ticket and issuance providers.

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
The memory ticket store does not survive a registrar restart. Do not
reuse demo credentials or keys in a production deployment.
