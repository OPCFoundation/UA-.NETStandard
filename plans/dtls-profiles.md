# OPC UA PubSub DTLS profile implementation status

Part 14 §7.3.2.4 defines DTLS for unicast UADP PubSub. The stack implements the
supportable subset with .NET BCL cryptography only. Profiles are registered at
runtime only when the required cipher and curve are available; otherwise they
fail closed with a clear error. There is no downgrade or silent substitution.

| Profile | Cipher suite | ECDHE / certificate curve | Status |
| ------- | ------------ | ------------------------- | ------ |
| `ECC_curve25519` | `TLS_CHACHA20_POLY1305_SHA256` | Curve25519 | Unsupported: no portable BCL X25519 API. |
| `ECC_curve25519_AesGcm` | `TLS_AES_128_GCM_SHA256` | Curve25519 | Unsupported: no portable BCL X25519 API. |
| `ECC_curve448` | `TLS_CHACHA20_POLY1305_SHA256` | Curve448 | Unsupported: no BCL X448 API. |
| `ECC_curve448_AesGcm` | `TLS_AES_256_GCM_SHA384` | Curve448 | Unsupported: no BCL X448 API. |
| `ECC_nistP256` | `TLS_SHA256_SHA256` integrity-only | NIST P-256 | Implemented on net8/net9/net10. |
| `ECC_nistP384` | `TLS_SHA384_SHA384` integrity-only | NIST P-384 | Implemented on net8/net9/net10. |
| `ECC_brainpoolP256r1` | `TLS_SHA256_SHA256` integrity-only | Brainpool P256r1 | Implemented when the platform BCL creates OID `1.3.36.3.3.2.8.1.1.7`. |
| `ECC_brainpoolP384r1` | `TLS_SHA384_SHA384` integrity-only | Brainpool P384r1 | Implemented when the platform BCL creates OID `1.3.36.3.3.2.8.1.1.11`. |
| `ECC_nistP256_AesGcm` | `TLS_AES_128_GCM_SHA256` | NIST P-256 | Implemented when `AesGcm.IsSupported`. |
| `ECC_nistP384_AesGcm` | `TLS_AES_256_GCM_SHA384` | NIST P-384 | Implemented when `AesGcm.IsSupported`. |
| `ECC_brainpoolP256r1_AesGcm` | `TLS_AES_128_GCM_SHA256` | Brainpool P256r1 | Implemented when AES-GCM and the Brainpool curve are available. |
| `ECC_brainpoolP384r1_AesGcm` | `TLS_AES_256_GCM_SHA384` | Brainpool P384r1 | Implemented when AES-GCM and the Brainpool curve are available. |
| `ECC_nistP256_ChaChaPoly` | `TLS_CHACHA20_POLY1305_SHA256` | NIST P-256 | Implemented when `ChaCha20Poly1305.IsSupported`. |
| `ECC_nistP384_ChaChaPoly` | `TLS_CHACHA20_POLY1305_SHA256` | NIST P-384 | Implemented when `ChaCha20Poly1305.IsSupported`. |
| `ECC_brainpoolP256r1_ChaChaPoly` | `TLS_CHACHA20_POLY1305_SHA256` | Brainpool P256r1 | Implemented when ChaCha20-Poly1305 and the Brainpool curve are available. |
| `ECC_brainpoolP384r1_ChaChaPoly` | `TLS_CHACHA20_POLY1305_SHA256` | Brainpool P384r1 | Implemented when ChaCha20-Poly1305 and the Brainpool curve are available. |

Target-framework notes:

- net8/net9/net10: profiles above are registered according to runtime primitive
  probes.
- netstandard2.1: DTLS source compiles, but profiles that require raw ECDHE /
  AEAD are not registered by default; unsupported profiles fail closed.
- net48: no DTLS profiles are registered; `opc.dtls://` fails closed instead of
  using unsupported cryptography.
