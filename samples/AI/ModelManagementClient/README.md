# AI Model Management Client

Runs the AI scenarios against a server implementing OPC UA AI Model Management.

```powershell
dotnet run --project samples\AI\ModelManagementClient -f net10.0 -- --help
dotnet run --project samples\AI\ModelManagementClient -f net10.0 -- opc.tcp://localhost:62640/ModelManagementServer
```

The positional endpoint defaults to the URL above. Provision and trust both application
certificates before connecting; the default is trusted certificates with
Basic256Sha256 / SignAndEncrypt.

For isolated development, `--auto-accept` (aliases `--autoaccept`, `-a`) accepts
untrusted **server certificates**, with other certificate checks still enforced.
`--insecure` independently selects **SecurityPolicy None**, without signing or
encryption; it does not enable certificate auto-accept. It requires a target that
explicitly exposes None; the paired ModelManagementServer does not.
Both relaxations default to false, accept explicit `false` (for example
`--auto-accept=false --insecure=false`), and warn on stderr only when enabled.
Do not use either relaxation in production.

Flags can precede or follow the positional URL. Unknown options, extra positional
arguments, malformed booleans and invalid endpoint URLs fail with a diagnostic on
stderr. `--help` does not create a host, touch PKI, or connect to a server.
