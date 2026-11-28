<!-- SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL -->
<!-- SPDX-License-Identifier: MIT -->

# FDO enrollment and OPC UA handoff

This .NET 10 demo initializes one Device, transfers its ownership voucher,
registers and retrieves the Owner through Rendezvous (TO0 and TO1), and
enrolls a DCA certificate using `fdo.csr` in protected TO2. It then
authenticates to a local OPC UA registrar with the installed certificate
and calls `GetManagers`. It uses no OPC UA onboarding ticket.

## Run locally

From the OPC UA repository root, run:

```powershell
dotnet run --project samples\Gds\FdoOnboarding `
  -p:CustomTestTarget=net10.0 `
  -- D:\temp\fdo-onboarding-run-1 62561
```

Use an empty user-controlled state directory and an unused loopback port.
The demo prints `FDO_ONBOARDING_COMPLETE managers=0` when the registrar
admits the DCA. No software or managed-application manager is configured.
The state directory retains the demo PKI, including private keys. The default
run keeps registrar receipts in memory; `--replace` persists them to disk.
Keep the directory private and dispose of it after inspection.
The demo does not use or modify the operating system certificate stores.

The demo is not part of `UA.slnx`. The solution includes the opt-in module,
but applications that reference only the base GDS packages do not depend on FDO.

To enroll only a provisioned Device against an existing deployment, use
[OnboardingClient's FDO mode](../README.md#enroll-a-provisioned-fdo-device).

### Credential replacement

Use a different empty state directory and unused loopback port. Supply a
fresh 32-byte wrapping key to the sample process through the environment.
Do not pass it on the command line, log it, or put it in the state directory.
Build before setting the wrapping key, then run the compiled sample directly.

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

In this mode, the Owner signs the replacement with a distinct Owner2
key. The Device stages its new voucher secret and credential header
in an encrypted `fdo-device` checkpoint without replacing the active
credential. After authenticating `Done20`, the Owner commits the replacement
voucher separately in `fdo-owner`. After authenticating `DoneAck20`, the
Device publishes the staged credential and then installs the DCA. The demo
reopens both checkpoints and checks that the new secret, not the old one,
validates the committed replacement voucher. It prints
`FDO_REPLACEMENT_ACTIVATED` before the normal encrypted registrar handoff.
The public registrar receipt contains no replacement secret.

## What the demo trusts

The demo uses loopback HTTP for the FDO roles and maps its configured
`rvserver.local` name to that same listener. TO2 verifies the signed
Rendezvous redirect against the voucher Owner and protects enrollment with
the paired ECDH P-256/AES-128-GCM profile. The registrar checks the
provisioned Device GUID and attestation certificate before signing the CSR;
it records the exact leaf and commits the receipt only on authenticated
`Done20`. The Device checks the leaf against separately configured enrollment
trust and its retained RSA key before `Done20`. It persists, reloads and
activates the exact leaf only after authenticating `DoneAck20`.

The registrar provisions the enrollment CA and CRL independently in its OPC
UA channel trust and external admission stores. The Device pins the local
registrar certificate from the demo's own PKI directory before opening its
encrypted OPC UA connection. Installation does not replace peer trust.

This is a single-run demo on an operator-controlled local filesystem.
Manufacturing, attestation and Owner keys and Rendezvous state live in
memory; attestation revocation is not checked for the locally generated
short-lived CA. The default run reuses the FDO credential. The replacement
run demonstrates staged and activated checkpoint boundaries, not crash
recovery, retry, or later resale. Its operator retains the wrapping key
separately if the checkpoint must be inspected after the process exits.
Neither run demonstrates HTTPS, full Base Profile support, or interoperability
with another implementation.
