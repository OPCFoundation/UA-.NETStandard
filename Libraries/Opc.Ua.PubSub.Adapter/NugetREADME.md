# OPCFoundation.NetStandard.Opc.Ua.PubSub.Adapter

Adapters that bind OPC UA **PubSub** publisher/subscriber/action datasets to an
**external** OPC UA server through a managed client session
(`Opc.Ua.Client.ManagedSession`).

- **Publisher** — reads an external server's nodes and publishes them as PubSub
  DataSets. Two source modes: **cyclic Read** calls, or a client **Subscription**
  (monitored items) with affinity per WriterGroup (default) or DataSetWriter.
- **Subscriber** — writes received PubSub DataSet values back to an external server.
- **Actions** — maps inbound PubSub Action requests to external server method calls.

```csharp
services.AddOpcUa()
    .AddPubSub(pubsub => pubsub
        .AddPublisher()
        .AddUdpTransport()
        .UseConfigurationFile("pubsub-config.xml")
        .AddExternalServerPublisher(options =>
        {
            options.Connection.EndpointUrl = "opc.tcp://plant-server:4840";
            options.ReadMode = ExternalReadMode.Subscription; // or Cyclic
        }));
```

See `Docs/PubSub.md` for the full guide.
