# OPC Foundation UA .NET Standard Reference Server

## Introduction

The console reference server can be configured using several console parameters.
Some of these parameters are explained in more detail below.

To see all available parameters call console reference server with the parameter `-h`.

## Security choices

The ordinary `Quickstarts.ReferenceServer.Config.xml` advertises secured endpoints only.
Provision trust in the application certificate stores before connecting clients. Trust acceptance and
endpoint message security are separate choices:

| Option | Default and effect |
| --- | --- |
| `--autoaccept`, `-a` | Off. Accepts the application/channel certificate's `BadCertificateUntrusted` error for isolated testing. Does not accept other certificate errors or X509 user identity certificates, and does not enable None endpoints. |
| `--allow-none` | Adds a `SecurityPolicy None` endpoint with no message security; isolated testing only. `--allow-none=false` removes None policies from the loaded configuration. Omission preserves explicitly configured policies. |
| `--provision` | Off. Explicit consent to the existing limited-namespace certificate-provisioning mode and untrusted application-certificate acceptance, even with `--autoaccept=false`. Does not enable None or bypass other certificate errors. Use only for isolated commissioning, then restart without this option. |

Enabled command-line relaxations print `WARNING:` before taking effect. `--help` and invalid arguments
do not load configuration, check/create certificates, or start listeners.

The None override runs **after the final configuration load**, including a `--shadowconfig` reload:
it preserves all other policies and settings. Explicit false removes None even if it was present in
the loaded file; it is not an instruction to revert to that file's defaults. The deliberate `--ctt`
configuration remains separate and retains its existing test policies, including None. Combining
`--ctt` with `--allow-none=false` deliberately removes that endpoint and can affect CTT cases.
LDS discovery exceptions and library defaults are unchanged.

Explicit command-line values take precedence over `REFSERVER_` environment defaults, including false
values and aliases. Environment keys use the existing long-option spelling:
`REFSERVER_AUTOACCEPT`, `REFSERVER_PROVISION`, and `REFSERVER_ALLOW-NONE`. A configured environment
opt-in is still explicit consent. The latter key contains a hyphen; in PowerShell, for example:

```powershell
[Environment]::SetEnvironmentVariable('REFSERVER_ALLOW-NONE', 'true')
dotnet ConsoleReferenceServer.dll --allow-none=false
```

Examples for an isolated test environment:

```powershell
# Trust relaxation only; endpoint security is unchanged.
dotnet ConsoleReferenceServer.dll --autoaccept

# None only; application-certificate acceptance is unchanged.
dotnet ConsoleReferenceServer.dll --allow-none

# Provisioning consent, retaining secured ordinary endpoints.
dotnet ConsoleReferenceServer.dll --provision
```

Custom XML security/trust settings remain an administrator responsibility; `--autoaccept` controls
the host's existing untrusted-certificate callback, not every certificate-manager setting.

## Reverse Connect

The OPC UA reverse connect feature allows an OPC UA server to initiate the connection to a client, rather than the traditional model where clients connect to servers. This is particularly useful in scenarios where the server is behind a firewall or NAT, making it difficult for clients to directly connect to it.

### How to use Reverse Connect

To enable reverse connect mode, specify the client endpoint URL using the `--rc` or `--reverseconnect` parameter:

```bash
dotnet ConsoleReferenceServer.dll --rc=opc.tcp://localhost:65300
```

or

```bash
dotnet ConsoleReferenceServer.dll --reverseconnect=opc.tcp://localhost:65300
```

### Example: Server and Client with Reverse Connect

1. Start the client with reverse connect listener on port 65300:
   ```bash
   dotnet ConsoleReferenceClient.dll --rc=opc.tcp://localhost:65300 opc.tcp://localhost:62541/Quickstarts/ReferenceServer
   ```

2. In a separate terminal, start the server with reverse connect to the client:
   ```bash
   dotnet ConsoleReferenceServer.dll --rc=opc.tcp://localhost:65300
   ```

The server will establish a reverse connection to the client endpoint, and the client will use this connection to communicate with the server.
Trust the participating applications' certificates first. Reverse connect does not require relaxing trust.

### Additional Options

- `-a` or `--autoaccept`: Auto accept untrusted certificates (for testing only)
- `-c` or `--console`: Log to console
- `-l` or `--log`: Log app output
- `-t` or `--timeout`: Timeout in seconds to exit application

For the complete list of options, use `--help`.

## X509 user identity certificates

The reference server validates X509 **user** identity tokens against its trusted-user certificate
store (`TrustedUserCertificates`, by default `%LocalApplicationData%/OPC Foundation/pki/trustedUser`).
An untrusted user certificate is rejected with `BadIdentityTokenRejected` — the `--autoaccept` option
only auto-accepts the application/channel certificate, never user identity certificates.

To let a trusted client (for example the OPC Foundation Compliance Test Tool) authenticate with an
X509 user token, its user certificate must be present in that store. To make provisioning easy, the
server writes every **rejected** X509 user certificate to a dedicated review store,
`pki/rejectedUser` (a sibling of `pki/trustedUser`). After one failing activation you can move the
legitimate user certificate from `pki/rejectedUser/certs` into `pki/trustedUser/certs` (and any
issuing CA into `pki/issuerUser/certs`) and reconnect. Deliberately-untrusted certificates simply stay
out of the trusted store and continue to be rejected.

## Host policy tests

`Program.CreateCommand`, `Program.ParseArguments`, and `Program.ConfigureHost` expose the executable's
parser and post-load configuration boundary without server startup. The existing Tools test project
loads only these public methods from the built sample; it does not inspect private implementation
details and has no project reference to the executable. Build the sample with the same configuration
as the tests before running:

```powershell
dotnet build samples\Reference\ConsoleReferenceServer\ConsoleReferenceServer.csproj -f net10.0 --no-restore
dotnet test tests\Opc.Ua.Tools.Tests\Opc.Ua.Tools.Tests.csproj -f net10.0 --no-restore --filter FullyQualifiedName~SampleReferenceServerHostPolicyTests
```

These tests cover parser/help/errors, warnings, command-line/environment precedence, independent
trust/None choices, provisioning consent, and configuration policy overrides. They do not start
listeners or exercise certificate-manager validation, the live provisioning namespace, or CTT runs.
