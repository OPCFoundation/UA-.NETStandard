# OPC 10000-21 Onboarding Demo

This sample demonstrates the registrar-administration part of OPC
10000-21 end to end over a real OPC UA connection. It uses only the
model and runtime APIs shipped by this repository:

- `OnboardingRegistrar` loads the generated `Opc.Ua.Onboarding` model,
  exposes the standard `DeviceRegistrar_Administration` object, and binds
  `RegisterTickets` / `UnregisterTickets` to an injected
  `MemoryTicketStore`.
- `OnboardingClient` connects with a managed session, resolves the
  well-known registrar NodeId, and uses the DI-provided
  `Opc.Ua.Gds.Client.OnboardingClient`.
- The targeted `OnboardingSampleStartupTests` exercise both executables,
  anonymous denial, authenticated ticket administration and explicit
  bootstrap consent using isolated stores and owned child processes.

The demo covers ticket administration. It does not implement the
device-facing `ProvideIdentities` flow or a complete production GDS.

## Bootstrap consent

Both applications now reject untrusted peer certificates by default. There is
no `--security-none` option: enrollment administration retains
`SignAndEncrypt` / `Basic256Sha256`. The default-false `--auto-accept` is
**explicit controlled-bootstrap consent**, not general permission to trust any
registrar or enrolling device. It only relaxes unknown-certificate trust; it
does not bypass certificate expiry, identity/hostname checks, registrar roles,
ticket authorization or channel encryption.

Before opting in, isolate/restrict the network and independently verify the
registrar endpoint, certificate fingerprint and application URI. Do this before
transmitting administrator credentials or tickets. Both applications print a
bootstrap-specific warning when consent is enabled.

`--auto-accept=false` explicitly preserves trust checks. Generic-host JSON,
environment or `AutoAcceptUntrustedCertificates=true` assignments cannot enable
the flag. Host settings can be supplied as `key=value` or forwarded after a
second `--`; explicit sample options override positional and forwarded values.
`--help` and malformed/unknown options exit before credentials, PKI or network
initialization.

The existing `run-onboarding-demo.ps1` wrapper does not yet forward bootstrap
consent. Its separate owner must add `--auto-accept` to the registrar and both
client invocations for its isolated lab mode (or provision mutual trust).
Until then, use the manual commands below rather than the old script command.

## Run an isolated bootstrap demonstration

Set `ONBOARDING_DEMO_USER` and `ONBOARDING_DEMO_PASSWORD` to the same fresh,
non-empty, one-run values in both terminals before starting either application.
Provision them through the environment using your secret-management tooling,
not command-line arguments or committed files. The client moves the password
into the existing secret-store/provider APIs. Do not reuse these credentials.

After independently verifying the bootstrap peer identities, start the registrar:

```powershell
dotnet run --project samples\Gds\OnboardingRegistrar -f net10.0 -- `
  --auto-accept `
  --port 62560 `
  --pkiRoot "$env:TEMP\opcua-onboarding-registrar"
```

After `ONBOARDING_REGISTRAR_READY` is printed, run the client:

```powershell
dotnet run --project samples\Gds\OnboardingClient -f net10.0 -- `
  --auto-accept `
  --endpoint "opc.tcp://localhost:62560/OnboardingRegistrar" `
  --pkiRoot "$env:TEMP\opcua-onboarding-client"
```

Add `--anonymous true` to the client command to demonstrate
`BadUserAccessDenied`; this must not print `ONBOARDING_DEMO_OK`. Without that
option, successful authenticated output ends with:

```text
REGISTER Good Good
UNREGISTER Good
UNREGISTER_AGAIN BadNotFound
ONBOARDING_DEMO_OK
```

Stop only the processes you started and retain or remove only their specific
PKI directories. Clear one-run credential environment variables afterward.

## Provisioned secure operation

Omit `--auto-accept` from both commands. Before connecting, initialize each
application certificate using the existing application-configuration and
[certificate-manager APIs](../../docs/CertificateManager.md), then provision
mutual peer trust: the client trusts the registrar and the registrar trusts
the client application certificate (or their verified issuing chains). Verify
fingerprints, application URIs, hostnames and validity out of band before
trusting self-signed certificates. Keep the application's own private key in
its own store; copy only public certificates to peer trust lists.

Use stable `--pkiRoot` paths across restarts. The application URIs are
`urn:localhost:OPCFoundation:OnboardingRegistrar` and
`urn:localhost:OPCFoundation:OnboardingClient`; the default endpoint is
`opc.tcp://localhost:62560/OnboardingRegistrar`. Unknown trust must be resolved
by verified provisioning, not by enabling None or accepting every rejected
certificate.

## Wire contract

The client API uses the Part 21 types directly:

```csharp
ArrayOf<StatusCode> results = await onboarding.RegisterTicketsAsync(
[
    new ByteString(firstTicket),
    new ByteString(secondTicket)
]);
```

`EncodedTicket` is a subtype of `ByteString`. The server returns one
`StatusCode` for every supplied ticket.

## Security

The demo selects `SignAndEncrypt` with `Basic256Sha256`. The registrar
authenticates the environment-provisioned account, grants that user the
generated `RegistrarAdmin` role, and restricts both ticket methods to that role.
The endpoint
also permits an anonymous bootstrap session because `ManagedSession`
activates its configured identity provider after creating the session;
anonymous users have no permission to call the registrar methods.

Certificate auto-accept is available only through explicit bootstrap consent. A
production registrar must use managed trust lists, authenticate and
authorize registrar administrators, persist tickets in a protected
store, and apply the audit/redaction requirements described in the
[GDS developer guide](../../docs/GDS.md).

Ticket enrollment has not been disabled or replaced by an allow-all callback.
The sample remains an in-memory administration demonstration rather than a
production ticket persistence or complete device enrollment service.
