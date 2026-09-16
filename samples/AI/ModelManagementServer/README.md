# AI Model Management Server

Hosts OPC UA AI Model Management at
`opc.tcp://0.0.0.0:62640/ModelManagementServer` by default.
Use `--host localhost` to restrict the advertised endpoint to local development.

```powershell
dotnet run --project samples\AI\ModelManagementServer -- --help
dotnet run --project samples\AI\ModelManagementServer -- --host localhost --port 62640
```

## Trust and configuration

Trust both application certificates before connecting. Untrusted client certificates
are rejected by default and SecurityPolicy None is not exposed. For isolated
development, `--auto-accept` (aliases `--autoaccept`, `-a`) accepts untrusted client
certificates with a warning on stderr, without bypassing other certificate checks or
disabling message security. Omission or an explicit `false` keeps trust enforcement.

`--port` accepts 1–65535. Named sample options override positional `key=value`
settings, which override forwarded host arguments and normal JSON/environment
configuration. Omitted options preserve host configuration; auto-accept requires the
sample flag even if configuration contains an auto-accept setting.

AI and inference backend settings retain their existing configuration keys. Use
positional assignments or forward generic-host switches after the sample's `--`:

```powershell
dotnet run --project samples\AI\ModelManagementServer -- --host localhost -- --InferenceBackend:Kind ChatClient
```

`--help` has no host/PKI side effects. Unknown or malformed sample options fail on stderr.
