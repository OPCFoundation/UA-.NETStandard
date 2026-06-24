# Console Reference External Server PubSub

A self-contained OPC UA **Part 14 PubSub** reference sample that bridges an
**external OPC UA server** to PubSub using the `Opc.Ua.PubSub.Adapter` library.
It is built on the fluent + dependency-injection + .NET Generic Host surface and
is Native AOT publishable.

The sample demonstrates **both directions** of the adapter, plus the optional
action responder:

| `--mode`     | Adapter call                       | What it does                                                               |
| ------------ | ---------------------------------- | -------------------------------------------------------------------------- |
| `publisher`  | `AddExternalServerPublisher`       | Reads nodes from an external server and **publishes** them over UDP/UADP.   |
| `subscriber` | `AddExternalServerSubscriber`      | Receives PubSub DataSets and **writes** the values back to an external server. |
| `responder`  | `AddExternalServerActionResponder` | Maps an inbound PubSub **Action** to an external server **method call**.     |

## Wiring order

The adapter enumerates the configured PubSub datasets / readers when it is added,
so the PubSub configuration must be supplied with `UseConfiguration` **before**
the `AddExternalServer*` call:

```csharp
builder.Services.AddOpcUa().AddPubSub(pubsub => pubsub
    .AddPublisher()
    .AddUdpTransport()
    .UseConfiguration(config)              // 1. configuration first ...
    .AddExternalServerPublisher(options => // 2. ... then the adapter
    {
        options.Connection.EndpointUrl = "opc.tcp://localhost:62541/Quickstarts/ReferenceServer";
        options.ReadMode = ExternalReadMode.Cyclic;            // or .Subscription
        options.Affinity = ExternalSubscriptionAffinity.WriterGroup;
    }));
```

The subscriber and responder are wired the same way:

```csharp
// Subscriber: write received DataSet fields back to the external server.
pubsub.AddSubscriber().AddUdpTransport()
      .UseConfiguration(subscriberConfig)
      .AddExternalServerSubscriber(o => o.Connection.EndpointUrl = endpoint);

// Responder: map a PubSub Action to an external method call.
pubsub.AddSubscriber().AddUdpTransport()
      .UseConfiguration(subscriberConfig)
      .AddExternalServerActionResponder(o =>
      {
          o.Connection.EndpointUrl = endpoint;
          o.MethodMap.Add("ResetCounters", objectId, methodId);
          o.Targets.Add(new PubSubActionTarget { DataSetWriterId = 1, ActionName = "ResetCounters" });
      });
```

## PubSub configuration

`ExternalServerPubSubConfiguration` builds a small inline configuration with the
fluent `PubSubConfigurationBuilder` and then attaches the two adapter-specific
pieces:

- **Publisher** — the `PublishedDataSet` source variables are mapped onto the
  external server's well-known `Server` status nodes (`CurrentTime`, `State`,
  `ServiceLevel`) so the sample produces meaningful data against **any** OPC UA
  server without prior address-space knowledge.
- **Subscriber** — the `DataSetReader`'s `TargetVariables` are placeholder nodes
  (`ns=2;s=Demo.External.*`). Point them at any writable variables of matching
  type on your target server.

## Running

The sample builds and is AOT-publishable without a live server; it only contacts
the server at run time.

```bash
# Publisher, cyclic Read each cycle (default)
dotnet run -- --mode publisher --read-mode cyclic

# Publisher, client Subscription cache, one Subscription per DataSetWriter
dotnet run -- --mode publisher --read-mode subscription --affinity datasetwriter

# Subscriber, writing received values back to the external server
dotnet run -- --mode subscriber

# Action responder
dotnet run -- --mode responder

# Point at any OPC UA server (defaults to the repo ConsoleReferenceServer)
dotnet run -- --mode publisher --endpoint opc.tcp://localhost:62541/Quickstarts/ReferenceServer
```

| Option              | Default                                                      | Description                                                       |
| ------------------- | ----------------------------------------------------------- | ---------------------------------------------------------------- |
| `--mode`            | `publisher`                                                 | `publisher` \| `subscriber` \| `responder`.                       |
| `--read-mode`       | `cyclic`                                                    | Publisher source: `cyclic` (Read each cycle) \| `subscription`.   |
| `--affinity`        | `writergroup`                                               | Subscription grouping: `writergroup` \| `datasetwriter`.          |
| `--endpoint`        | `OPCUA_EXTERNAL_ENDPOINT` or the ConsoleReferenceServer URL | External OPC UA server endpoint URL.                              |
| `--pubsub-endpoint` | `opc.udp://239.0.0.1:4840`                                  | UDP/UADP PubSub transport endpoint URL.                           |

To try the publisher end-to-end against the repository's reference server, start
`ConsoleReferenceServer` (which listens on
`opc.tcp://localhost:62541/Quickstarts/ReferenceServer`) and run this sample with
`--mode publisher`. Then run it again with `--mode subscriber` (pointed at a
server exposing writable `ns=2;s=Demo.External.*` nodes) to close the loop.

> The demo connects to the external server **unsecured** for zero-config interop.
> Production bridges must use `SignAndEncrypt` with a provisioned application
> instance certificate (set `options.Connection.SecurityMode` and supply an
> `ApplicationConfiguration`).
