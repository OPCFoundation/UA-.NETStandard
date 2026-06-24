# Console Reference PubSub

A single, self-contained OPC UA **Part 14 PubSub** reference application built on the
fluent `PubSubApplicationBuilder` + dependency injection + .NET Generic Host surface.
One executable exposes three command-line-selectable **modes**, and publishes as a
NativeAOT-ready single-file executable.

## Modes

```
ConsoleReferencePubSub <mode> [options]
```

| Mode | Purpose |
| ---- | ------- |
| `publisher` | Publishes a built-in sample DataSet over UDP/UADP or MQTT (UADP/JSON). |
| `subscriber` | Receives DataSets and logs each decoded message to the console. |
| `external` | Bridges an **external** OPC UA server to PubSub via the `Opc.Ua.PubSub.Adapter` library (publisher / subscriber / responder direction). |

### `publisher`

```bash
ConsoleReferencePubSub publisher --profile udp-uadp --interval 1000
ConsoleReferencePubSub publisher --profile mqtt-json --endpoint mqtt://localhost:1883
```

Options: `--profile udp-uadp|mqtt-uadp|mqtt-json`, `--config-file <xml>`,
`--publisher-id`, `--writer-group-id`, `--data-set-writer-id`, `--endpoint`, `--interval`.

### `subscriber`

```bash
ConsoleReferencePubSub subscriber --profile udp-uadp
```

Options: `--profile`, `--config-file <xml>`, `--publisher-id-filter`,
`--writer-group-id-filter`, `--data-set-writer-id-filter`, `--endpoint`.

### `external`

Bridges an external OPC UA server (defaults to the repository's ConsoleReferenceServer
at `opc.tcp://localhost:62541/Quickstarts/ReferenceServer`; override with `--endpoint` or
the `OPCUA_EXTERNAL_ENDPOINT` environment variable).

```bash
# Read an external server and publish its values (cyclic Read each cycle)
ConsoleReferencePubSub external --mode publisher --read-mode cyclic

# Read via a client Subscription cache, one subscription per WriterGroup
ConsoleReferencePubSub external --mode publisher --read-mode subscription --affinity writergroup

# Write received PubSub values back to an external server
ConsoleReferencePubSub external --mode subscriber

# Map an inbound PubSub Action to an external server method call
ConsoleReferencePubSub external --mode responder
```

Options: `--mode publisher|subscriber|responder`,
`--read-mode cyclic|subscription`, `--affinity writergroup|datasetwriter`,
`--endpoint <external server>`, `--pubsub-endpoint <udp url>`.

> The samples connect to the external server unsecured (`SecurityMode.None`) for
> zero-config interop. A production bridge must use `SignAndEncrypt` with a provisioned
> application instance certificate. See
> [Docs/PubSub.md external-server adapter section](../../Docs/PubSub.md#binding-pubsub-to-an-external-opc-ua-server-client-session-adapters).

## Build / publish

```bash
dotnet build Applications/ConsoleReferencePubSub/ConsoleReferencePubSub.csproj
dotnet publish Applications/ConsoleReferencePubSub/ConsoleReferencePubSub.csproj -r win-x64
```

See [Docs/PubSub.md](../../Docs/PubSub.md) for the full PubSub guide.
