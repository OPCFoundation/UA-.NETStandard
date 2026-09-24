# Minimal Boiler Server

An OPC UA server exposing the source-generated boiler model at
`opc.tcp://localhost:62541/MinimalBoilerServer`.

```powershell
dotnet run --project samples\MinimalApi\MinimalBoilerServer -- --help
dotnet run --project samples\MinimalApi\MinimalBoilerServer -- --port 62541
```

## Trust and configuration

Provision and trust the client and server application certificates before connecting.
The server rejects untrusted client certificates by default and does not expose
SecurityPolicy None. For isolated development only, pass `--auto-accept`
(`--autoaccept` or `-a`) to accept untrusted client certificates with a warning on
stderr. Other certificate checks still apply; this does not disable message security.
Omitting the flag, `--auto-accept=false`, or `-a false` keeps trust enforcement enabled.

`--port` accepts 1–65535. JSON and environment configuration remain effective when
an option is omitted. Precedence is named sample options, positional `key=value`
settings, forwarded host arguments, then normal host configuration.
Trust relaxation requires the sample flag; a JSON/configuration value cannot turn it on.
Forward other generic-host switches after a second separator:

```powershell
dotnet run --project samples\MinimalApi\MinimalBoilerServer -- --auto-accept=false -- --Logging:LogLevel:Default Warning
```

Help exits without creating a host or PKI. Unknown or malformed sample options fail
with an error on stderr; use `--help` for accepted options.
