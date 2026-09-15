# OPC UA Minimal Calc Server

This .NET 10 console sample demonstrates a source-generated calculator node manager
and the typed fluent method-call API. It listens at
`opc.tcp://localhost:62542/MinimalCalcServer` by default.

## Run

From this directory:

```bash
dotnet run
dotnet run -- --help
dotnet run -- --port 62543
```

`--help` (also `-h` or `-?`) prints usage and exits with status 0 before creating a
host or accessing PKI. Unknown sample options, malformed values and invalid ports
exit nonzero with an explanatory message on standard error.

## Certificate trust and endpoint security

**Changed sample default:** untrusted client certificates are no longer
automatically accepted. Provision certificate trust for normal use. For local
development only, explicitly opt into automatic acceptance:

```bash
dotnet run -- --auto-accept
dotnet run -- --auto-accept=false
```

`--autoaccept` and `-a` are aliases for `--auto-accept`. The flag defaults to
`false`; both `--auto-accept false` and `--auto-accept=false` are accepted.
When enabled, a warning is written to standard error before the server starts.
The production-use warning comment in `Program.cs` remains applicable.

Automatic acceptance changes certificate trust, **not** endpoint security.
This sample continues to exclude SecurityPolicy `None`; neither `--insecure`
nor `--nosecurity` is offered. It retains its configured SHA-1 rejection and
minimum certificate key size. These are application choices, not changes to
stack-library defaults.

The sample uses its existing OS-temporary-directory PKI location under
`OPC Foundation/MinimalCalcServer/pki`. See
[Certificate management](../../../docs/CertificateManager.md) for trust management.

## Host configuration and port precedence

The existing `port` configuration key remains supported:

```bash
dotnet run -- port=62543
dotnet run -- port=62543 Logging:LogLevel:Default=Warning
```

Forward arbitrary generic-host switches after a separate `--`. The first `--`
below belongs to `dotnet run`; the second separates sample options from host
configuration:

```bash
dotnet run -- --auto-accept=false -- --port 62543 --environment Development
```

Port precedence, highest first:

1. The sample's explicit `--port` option.
2. Positional `key=value` settings.
3. Generic-host switches forwarded after `--`.
4. The normal host configuration sources, including environment and appsettings.
5. The sample fallback, `62542`.

Explicit command-line ports must be integers from 1 to 65535. Forwarded switches
require values; use `--key value` or `key=value`. Tokens after the separator are
host configuration, not sample security opt-ins. Put `--auto-accept` before the
separator to enable it. Host configuration is not bound to the sample's
certificate-trust or endpoint-policy choices.
