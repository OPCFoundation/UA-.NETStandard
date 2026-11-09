# Local Discovery Server samples

| Folder | Contents |
| --- | --- |
| [ConsoleLdsServer](ConsoleLdsServer/) | A console Local Discovery Server with optional multicast discovery (LDS-ME). Includes `Lds.Server.Config.xml` and a Dockerfile. |

See [LDS and LDS-ME](../../docs/GDS.md#lds--lds-me-45) for registration,
discovery and deployment guidance.

## Run

From the repository root:

```powershell
dotnet run --project samples\Lds\ConsoleLdsServer\ConsoleLdsServer.csproj -f net10.0 -- --console
```

Use `--help` to list the command-line options. `--no-mdns` disables multicast
advertisement; `--mdns-loopback` restricts it to loopback for testing.
Automatic certificate acceptance (`--autoaccept`) is a test-only option.
