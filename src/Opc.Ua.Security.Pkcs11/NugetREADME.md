# OPC UA .NET Standard — PKCS#11 certificate store

`OPCFoundation.NetStandard.Opc.Ua.Security.Pkcs11` lets an OPC UA application keep its private keys inside a hardware token, smart card or HSM. The key is used for signing and decryption but is never present in process memory and can never be exported.

This package is optional. `OPCFoundation.NetStandard.Opc.Ua.Core` does not reference it, so applications that do not use a token are unaffected.

## Getting started

Register the store provider, then point any certificate store at the token with an RFC 7512 `pkcs11:` URI:

```csharp
using Microsoft.Extensions.DependencyInjection;

builder.Services.AddOpcUa()
    .AddPkcs11CertificateStore(new Pkcs11TokenOptions
    {
        ModulePath = "/usr/lib/softhsm/libsofthsm2.so",
        TokenLabel = "opcua",
        PinProvider = () => secretStore.Read("token-pin")
    });
```

Without dependency injection, pass the provider to the certificate manager directly:

```csharp
ICertificateManager manager = CertificateManagerFactory.Create(
    securityConfiguration,
    telemetry,
    options => options.AddStoreProvider(new Pkcs11StoreProvider()));
```

A store path names the token, and may carry the module and PIN:

```
pkcs11:token=opcua;object=server?module-path=/usr/lib/softhsm/libsofthsm2.so&pin-value=1234
```

Prefer `Pkcs11TokenOptions.PinProvider` over `pin-value` so the PIN comes from a secret store rather than a configuration file.

## What is supported

| Operation | Mechanism |
|---|---|
| RSA signing, PKCS#1 v1.5 | `CKM_RSA_PKCS` with a DER `DigestInfo` |
| RSA signing, PSS | `CKM_RSA_PKCS_PSS` with MGF1 and a matching salt length |
| RSA decryption, OAEP | `CKM_RSA_PKCS_OAEP` |
| RSA decryption, PKCS#1 v1.5 | `CKM_RSA_PKCS` |
| ECDSA signing | `CKM_ECDSA` |

SHA-256, SHA-384 and SHA-512 are supported. SHA-1 is deliberately not.

Which security policies actually work depends on the mechanisms your token implements, not only on this package.

## Limitations

- Revocation lists are not held on a token. Point the trusted issuer store at a directory store.
- Objects are provisioned and removed with the token vendor's tools, not through this store.
- A private key offered to `AddAsync` is refused and logged: a token does not import key material it did not generate.
- The token's validation status is reported as **uncertified** unless you assert one, because nothing in the PKCS#11 interface reports a FIPS certificate. See the crypto provider documentation for how this is audited.

## Target frameworks

`net472`, `net48`, `netstandard2.1`, `net8.0`, `net9.0`, `net10.0`.

## NativeAOT

This package is **not** validated for NativeAOT or trimming. `Pkcs11Interop` resolves the token module through native interop at run time and carries no trim or AOT annotations. The core stack's AOT support is unaffected, because `Opc.Ua.Core` never references this package.

## Additional documentation

See the [crypto provider documentation](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/CryptoProvider.md)
for pluggable cryptography, hardware-held private keys, FIPS claim boundaries and the audit surfaces.
