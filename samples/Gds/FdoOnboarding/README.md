<!-- SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL -->
<!-- SPDX-License-Identifier: MIT -->

# Use FDO to onboard an OPC UA device

Run this sample to see how a device gets its first OPC UA application
certificate through FIDO Device Onboard (FDO) instead of using an OPC UA
onboarding ticket. You don't need an existing FDO deployment: the sample
starts the FDO roles and an OPC UA registrar on your machine, and provisions
one simulated device with its own keys.

The FDO Owner uses the GDS certificate issuer to sign the device's certificate
request. The device installs the certificate, opens an encrypted OPC UA
connection, and calls `GetManagers`. That call verifies that the registrar
accepts the new credential, not just that FDO returned a certificate.

Use [OnboardingClient's FDO mode](../README.md#enroll-a-provisioned-fdo-device)
instead if you already have a provisioned device and running FDO roles.

## Run locally

Install the [.NET SDK required by this repository](../../../docs/DeveloperGuide.md#prerequisites)
and the .NET 10 runtime. From the repository root, run:

```powershell
dotnet run --project samples\Gds\FdoOnboarding `
  -p:CustomTestTarget=net10.0 `
  -- D:\temp\fdo-onboarding-run-1 62561
```

The first argument is a new absolute directory for sample state. The second
is a free loopback port for the OPC UA registrar. Use a fresh directory for
each run. It holds the sample's certificates and private keys, so keep it
private and delete it when you're done. The sample does not modify the
operating system certificate stores.

Success is `FDO_ONBOARDING_COMPLETE managers=0`. Zero is expected: the sample
checks registrar admission without configuring any software update or managed
application managers.

Run the demo project directly because it is a standalone walkthrough, not
part of the standard `UA.slnx` build. The reusable `Opc.Ua.Gds.Fdo` module
is in the solution and does not require the demo executable.

## What happens during enrollment

1. The sample initializes the device and transfers its ownership voucher to an
   Owner. The Owner registers with Rendezvous (TO0), and the device looks up
   the Owner there (TO1).
2. The device verifies the signed redirect and starts the protected Owner
   exchange (TO2). It sends a certificate signing request through `fdo.csr`.
   The Owner checks the approved device identity and asks GDS to sign it.
3. The Owner commits the issuance receipt after authenticating the device's
   `Done20` message. The device checks the issued certificate against its
   enrollment trust and retained key before confirming enrollment, then
   installs it only after authenticating `DoneAck20`.
4. The device uses that certificate to connect to the registrar. The registrar
   checks its own trust configuration and the committed receipt before allowing
   `GetManagers`. Enrollment does not replace the device's OPC UA peer trust.

## Try replacing the FDO credential

The default run enrolls an OPC UA certificate but keeps the device's FDO
credential. Use `--replace` to see the Owner also issue a replacement FDO
credential and the device activate it only after `DoneAck20`. This mode writes
the enrollment receipt and replacement checkpoints to disk rather than
keeping the receipt in memory.

Use another new state directory and a free port. Build first, then supply a
fresh 32-byte wrapping key in the process environment to protect the device
checkpoint. Keep the key out of command lines, logs, and the state directory.

```powershell
dotnet build samples\Gds\FdoOnboarding\FdoOnboarding.csproj `
  -p:CustomTestTarget=net10.0

$env:FDO_DEMO_WRAP_KEY = [Convert]::ToBase64String(
  [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
try {
  dotnet samples\Gds\FdoOnboarding\bin\Debug\net10.0\FdoOnboarding.dll `
    D:\temp\fdo-onboarding-replacement-1 62562 --replace
}
finally {
  Remove-Item Env:\FDO_DEMO_WRAP_KEY
}
```

Look for `FDO_REPLACEMENT_ACTIVATED` before `FDO_ONBOARDING_COMPLETE`. The
sample reopens the Owner and device checkpoints and verifies that the new
secret, not the old one, validates the replacement voucher. The registrar
receipt does not contain the replacement secret.

## Scope and limits

This is a single-machine sample, not a deployment recipe. The FDO roles use
loopback HTTP and the paired ECDH P-256/AES-128-GCM profile. The sample
generates its own trust material and does not check attestation revocation
for its short-lived demo CA. It does not demonstrate HTTPS, crash recovery,
later resale, full Base Profile support, or interoperability with another
FDO implementation. For deployment responsibilities, see the
[FDO onboarding guide](../../../docs/Onboarding.md#fdo-onboarding-for-opc-ua-gds).
