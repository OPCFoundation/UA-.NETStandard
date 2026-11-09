# PubSub samples

| Folder | Contents |
| --- | --- |
| [ConsoleReferencePubSubClient](ConsoleReferencePubSubClient/README.md) | One reference application with publisher, subscriber and external-server adapter modes. Includes configuration builders, `appsettings.json` and a Dockerfile. |

See [PubSub](../../docs/PubSub.md) for transport, security and application APIs.

## Run

From the repository root:

```powershell
dotnet run --project samples\PubSub\ConsoleReferencePubSubClient\ConsoleReferencePubSubClient.csproj -f net10.0 -- --help
```

The sample README gives commands for each mode and for a publisher/subscriber
pair on one computer. MQTT examples require a broker.
