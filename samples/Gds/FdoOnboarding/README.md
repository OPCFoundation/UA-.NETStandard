# FDO enrollment and OPC UA handoff

This .NET 10 demo initializes one Device, transfers its ownership voucher,
registers and retrieves the Owner through Rendezvous (TO0 and TO1), and
enrolls a DCA certificate using `fdo.csr` in protected TO2. It then
authenticates to a local OPC UA registrar with the installed certificate
and calls `GetManagers`. It uses no OPC UA onboarding ticket.

## Run locally

Use the approved local development package, `Fdo` `1.0.0-local.1328a26`,
built from [FDO commit `1328a26`](https://github.com/marcschier/fdo/tree/1328a26cc491445a171c09357df83dccefcaf87a).
To reproduce the package, use a .NET 11 SDK and run this from a checkout
of that FDO commit:

```powershell
dotnet pack src\Fdo\Fdo.csproj -c Release `
  -p:PackageVersion=1.0.0-local.1328a26 -p:IncludeSymbols=false `
  -p:RepositoryCommit=1328a26cc491445a171c09357df83dccefcaf87a `
  -o D:\temp\opcua-fdo-local-1328a26\feed
```

From the OPC UA repository root, run:

```powershell
dotnet run --project samples\Gds\FdoOnboarding `
  -p:CustomTestTarget=net10.0 `
  -p:FdoLocalFeed=D:\temp\opcua-fdo-local-1328a26\feed `
  -- D:\temp\fdo-onboarding-run-1 62561
```

Use an empty user-controlled state directory and an unused loopback port.
The demo prints `FDO_ONBOARDING_COMPLETE managers=0` when the registrar
admits the DCA. No software or managed-application manager is configured.
The state directory retains the demo PKI, including private keys, and the
registrar receipts. Keep it private and dispose of it after inspection.
The demo does not use or modify the operating system certificate stores.

The project is not part of `UA.slnx`, and the local feed is used only when
explicitly supplied to this project. Routine stack restores do not need FDO.

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

This is a single-run demo. Manufacturing keys and Rendezvous state live in
memory; attestation revocation is not checked for the locally generated
short-lived CA. The demo opts into the provisional paired wire format and
FDO credential reuse. It does not show HTTPS, durable FDO recovery, support
for the full Base Profile, or interoperability with another implementation.
