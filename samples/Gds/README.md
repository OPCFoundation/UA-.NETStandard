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
- `run-onboarding-demo.ps1` builds both applications, waits until the
  OPC UA server reaches its running state, proves an anonymous caller is
  denied, runs the authenticated client, and stops the exact registrar
  process in a `finally` block.

The demo covers ticket administration. It does not implement the
device-facing `ProvideIdentities` flow or a complete production GDS.

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
UNREGISTER Good
UNREGISTER_AGAIN BadNotFound
ONBOARDING_DEMO_OK
```

Pass `-Keep` to retain the temporary PKI stores and registrar logs for
diagnostics.

## Run the applications manually

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

The demo selects `SignAndEncrypt` with `Basic256Sha256`. For a
self-contained local run, both applications automatically accept the
other application's newly created certificate and store it under an
isolated temporary PKI root. The script also generates a one-run random
username and password, grants that user the generated `RegistrarAdmin`
role, and restricts the two ticket methods to that role. The endpoint
also permits an anonymous bootstrap session because `ManagedSession`
activates its configured identity provider after creating the session;
anonymous users have no permission to call the registrar methods.

Automatic certificate acceptance is a sample-only convenience. A
production registrar must use managed trust lists, authenticate and
authorize registrar administrators, persist tickets in a protected
store, and apply the audit/redaction requirements described in the
[GDS developer guide](../../docs/GDS.md).

For a manual run, set `ONBOARDING_DEMO_USER` and
`ONBOARDING_DEMO_PASSWORD` to the same non-empty values in both
terminals before starting either application. Do not reuse demo
credentials in a production deployment.
