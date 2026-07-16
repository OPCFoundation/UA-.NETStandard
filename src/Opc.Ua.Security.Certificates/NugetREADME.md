# OPC UA .NET Standard — Security.Certificates

`OPCFoundation.NetStandard.Opc.Ua.Security.Certificates` is the
certificate handling library that backs every OPC UA application
certificate lifecycle: X.509 generation (`CertificateBuilder`),
certificate stores (Directory / X509Store), CRL handling, certificate
chain validation, certificate-signing-request issuance, and the
high-level `CertificateManager` / `ICertificateProvider` /
`ICertificateValidator` APIs.

## Overview

Reference this package directly when you need to:

- generate, rotate, or push application instance certificates outside
  the standard `ApplicationInstance` flow,
- consume the `CertificateManager` provider model from a non-server /
  non-client process (e.g. an out-of-band PKI tool),
- implement a custom `ICertificateStoreType`.

The package is referenced transitively from `Opc.Ua.Core` so server
and client consumers get it for free.

## Target frameworks

`net472`, `net48`, `netstandard2.1`, `net8.0`, `net9.0`,
`net10.0`.

## Additional documentation

See the [main repository README](https://github.com/OPCFoundation/UA-.NETStandard)
and the [Certificate Manager guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/CertificateManager.md)
for the full provider model, certificate-store layout, and rotation
flow.
