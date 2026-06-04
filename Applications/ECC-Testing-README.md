# ECC Testing with the Console Reference Server and Client

This guide describes how to use the **ConsoleReferenceServer** and
**ConsoleReferenceClient** to exercise the Elliptic Curve Cryptography (ECC)
security profiles of the OPC UA stack, how to generate the user certificates the
tests rely on, and how to enable the detailed cryptographic tracing that helps
diagnose interoperability problems.

Both applications live under `Applications/`:

| Application | Folder |
| --- | --- |
| Reference Server | `Applications/ConsoleReferenceServer` |
| Reference Client | `Applications/ConsoleReferenceClient` |

## Overview

The reference server exposes the full matrix of OPC UA security policies,
including the ECC policies:

- `ECC_nistP256_AesGcm` / `ECC_nistP256_ChaChaPoly`
- `ECC_nistP384_AesGcm` / `ECC_nistP384_ChaChaPoly`
- `ECC_brainpoolP256r1_AesGcm` / `ECC_brainpoolP256r1_ChaChaPoly`
- `ECC_brainpoolP384r1_AesGcm` / `ECC_brainpoolP384r1_ChaChaPoly`

(in addition to the RSA-based `Basic256Sha256` and `Aes*_RSA` policies).

The client's `--testall` mode connects to **every** endpoint the server
advertises, with every applicable user identity, so a single run validates the
complete security matrix end to end.

## 1. Build

Build both projects from the repository root (or open the solution and build
the two console projects):

```powershell
dotnet build Applications\ConsoleReferenceServer\ConsoleReferenceServer.csproj
dotnet build Applications\ConsoleReferenceClient\ConsoleReferenceClient.csproj
```

> **Debug vs. Release matters for crypto tracing.** The detailed crypto trace
> (see [Section 5](#5-debugging-crypto-with-the-debug-build)) is only compiled
> into **Debug** builds of `Opc.Ua.Core`. Build in Debug when you need to
> inspect the inputs to the cryptographic operations.

## 2. Run the Reference Server

From `Applications/ConsoleReferenceServer`:

```powershell
dotnet run -- -c -a
```

Useful options (see `--help` for the full list):

| Option | Meaning |
| --- | --- |
| `-c`, `--console` | Log to the console. |
| `-a`, `--autoaccept` | Auto-accept client/peer certificates (testing only). |
| `-r`, `--renew` | Renew (regenerate) the application instance certificate. |
| `--rc <url>` | Reverse connect to the supplied client endpoint. |

The server listens on `opc.tcp://<hostname>:62541/Quickstarts/ReferenceServer`
by default. The endpoints offered (including the ECC policies listed above) are
configured in `Quickstarts.ReferenceServer.Config.xml`.

### Trusting the test user certificates

For X.509 user-token authentication to succeed, the server must **trust** the
user certificates the client presents. The server reads its user trust list from
the `TrustedUserCertificates` store configured in
`Quickstarts.ReferenceServer.Config.xml`:

```xml
<TrustedUserCertificates>
  <StoreType>Directory</StoreType>
  <StorePath>../../pki/trustedUser</StorePath>
</TrustedUserCertificates>
```

The generation script (next section) writes the public certificates into
`pki/trustedUser/certs`, which is exactly the directory the server trusts. Make
sure the server and client resolve the **same** `pki/trustedUser` directory
(they share it by default when both run from the repository tree). If you point
the client at a different machine or container, copy the generated `*.der` files
from `pki/trustedUser/certs` into the server's trust store.

## 3. Generate the User Certificates

The `--testall` mode authenticates each ECC endpoint with a user certificate
whose key matches the endpoint's curve. These certificates are produced by:

```
Applications/ConsoleReferenceClient/generate_user_certificate.ps1
```

Run it from the `ConsoleReferenceClient` folder:

```powershell
.\generate_user_certificate.ps1
```

The script:

1. Creates `./bin/pki/trustedUser/certs` and `./bin/pki/trustedUser/private`.
2. Generates a self-signed user certificate for each supported curve —
   `nistP256`, `nistP384`, `brainpoolP256r1`, `brainpoolP384r1` — plus an
   RSA-2048 certificate, all with subject `CN=iama.tester@example.com` and a
   client-authentication EKU. Each certificate is rebuilt to include an
   Authority Key Identifier so it validates as a proper end-entity certificate.
3. Writes, per curve:
   - the public certificate (DER) to
     `bin/pki/trustedUser/certs/iama.tester.<curve>.der`, and
   - the PKCS#12 private key (PFX, password `password`) to
     `bin/pki/trustedUser/private/iama.tester.<curve>.pfx`.

The client (`ConnectTester`) selects the matching PFX for each endpoint based on
the endpoint's security policy — for example an `ECC_nistP384_*` endpoint uses
`iama.tester.nistP384.pfx`, while an RSA endpoint uses `iama.tester.rsa.pfx`.

> The PFX files are protected with the password `password` (the default of the
> `UserCertificatePassword` setting). Change both together if you customize it.

After generating, ensure the `certs` files end up in the server's
`pki/trustedUser/certs` trust store so the server accepts them (see
[Trusting the test user certificates](#trusting-the-test-user-certificates)).

## 4. Run `--testall` (Test All Endpoints)

With the server running and the user certificates generated and trusted, start
the client in test-all mode from `Applications/ConsoleReferenceClient`:

```powershell
dotnet run -- --testall -l -c
```

> **`-l -c` are required to see the test output.** `ConnectTester` writes its
> per-endpoint results (and reconnect/diagnostic messages) through the telemetry
> logger. That logger is only routed to the console when **both** `-l`
> (`--log`, log app output) **and** `-c` (`--console`, log to console) are
> supplied. Running plain `--testall` performs the same tests but prints almost
> nothing — always use `--testall -l -c` when you want to watch the matrix run
> or capture a trace.

`--testall` (alias `--ea`) takes the following path through the code
(`ConnectTester.cs`):

1. Loads the client application configuration and certificate.
2. Calls `GetEndpoints` on the configured server and enumerates **every**
   endpoint it advertises (optionally filtered — see `SecurityPolicyFilter`
   below).
3. For each endpoint, it builds the candidate user identities:
   - **Anonymous**,
   - **Username/Password** (when `UserName` is set), and
   - **X.509 user certificate** (when `SupportsX509` is `true`), automatically
     choosing the PFX whose key algorithm matches the endpoint's curve.
4. For every (endpoint × identity) combination it opens a session, forces a
   secure-channel renewal, reads `Server_ServerStatus_CurrentTime`, browses the
   address space, then closes the session — logging a clearly delimited
   `SECURITY-POLICY=… IDENTITY=…` / `TEST COMPLETE` block for each.

Each combination is reported independently, so a failure on one policy or
identity does not stop the rest of the matrix from running. When the matrix
finishes, the client prints `Ctrl-C to stop.` and waits.

## 5. Configure the Connection with the Settings File

The `--testall` behavior is driven by an external JSON settings file rather than
command-line arguments. By default the loader looks for
**`ConnectTester.Settings.json`** next to the executable; the path can be
overridden with the `REFCLIENT_CONNECTTESTER_SETTINGS_FILE` environment
variable. A missing or unparseable file falls back to the compiled-in defaults.

Example `ConnectTester.Settings.json`:

```json
{
  "ServerUrl": "opc.tcp://whitecat:62541/Quickstarts/ReferenceServer",
  "UserName": "sysadmin",
  "Password": "demo",
  "SupportsX509": true,
  "ReconnectPeriod": 1000,
  "ReconnectPeriodExponentialBackoff": 15000,
  "UserCertificatePath": "../../pki/trustedUser/private",
  "UserCertificatePassword": "password"
}
```

| Setting | Purpose |
| --- | --- |
| `ServerUrl` | **The server the client connects to.** Set this to point the test run at a particular server (local, remote host, or container). |
| `UserName` / `Password` | Credentials used for the username/password identity. Leave `UserName` empty to skip that identity. |
| `SupportsX509` | When `true`, also test the X.509 user-certificate identity on each endpoint. |
| `SecurityPolicyFilter` | Substring filter applied to the endpoint security-policy URIs; only matching endpoints are tested (empty = test all). Useful to focus on, e.g., `ECC_nistP384`. |
| `UserCertificatePath` | Directory holding the user PFX files (relative paths resolve against the working directory). |
| `UserCertificatePassword` | Password protecting the user PFX files (matches `generate_user_certificate.ps1`, default `password`). |
| `ReconnectPeriod` / `ReconnectPeriodExponentialBackoff` | Reconnect timing for the session reconnect handler. |

> The single most important field is **`ServerUrl`** — it specifies which server
> the client tests against. Edit it (or supply an alternate settings file via
> `REFCLIENT_CONNECTTESTER_SETTINGS_FILE`) to retarget a run without rebuilding.

## 6. Debugging Crypto with the Debug Build

When a security policy fails to negotiate or messages fail to decrypt, build
`Opc.Ua.Core` in **Debug** and run the test. Debug builds define the
`OPCUA_CryptoTrace` symbol, which compiles in the `CryptoTrace` instrumentation
in `CryptoUtils.cs`. With tracing active the stack writes the **detailed inputs
to each cryptographic operation directly to the console**, for example:

```
EncryptWithAesGcm
  Data Offset/Count=…
  TokenId/LastSequenceNumber=…
  EncryptingKey=…
  IV=…
  EncryptedData=…
  Tag=…
  ExtraData=…
```

The same is emitted for the ChaCha20-Poly1305 encrypt/decrypt and AES-GCM
decrypt paths, so you can line up the client's and server's view of the keys,
IVs, ciphertext, tags, and associated data and pinpoint exactly where they
diverge.

Notes:

- The trace is gated by both `#if DEBUG` / `OPCUA_CryptoTrace` **and** the
  runtime flag `CryptoTrace.Enabled` (default `true`). A **Release** build of
  `Opc.Ua.Core` strips the trace entirely.
- The output includes secret key material — only use it on test systems with
  throw-away certificates, and never share raw traces from production keys.

## Quick Start (local, all ECC policies)

```powershell
# 1. Generate the user certificates (once, or after changing curves/passwords)
cd Applications\ConsoleReferenceClient
.\generate_user_certificate.ps1

# 2. Start the server (Debug build for crypto tracing)
cd ..\ConsoleReferenceServer
dotnet run -- -c -a

# 3. In another terminal, point ConnectTester.Settings.json at the server,
#    then run the full endpoint/identity matrix
cd ..\ConsoleReferenceClient
dotnet run -- --testall -l -c
```

> Remember the `-l -c` pair: without them `--testall` runs silently. See
> [Section 4](#4-run---testall-test-all-endpoints).
