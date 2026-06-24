# OPCFoundation.NetStandard.Opc.Ua.PubSub.Adapter

Adapters that bind OPC UA **PubSub** publisher/subscriber/action datasets to an
**external** OPC UA server through a managed client session
(`Opc.Ua.Client.ManagedSession`).

- **Publisher** — reads an external server's nodes and publishes them as PubSub
  DataSets. Two source modes: **cyclic Read** calls, or a client **Subscription**
  (monitored items) with affinity per WriterGroup (default) or DataSetWriter.
- **Subscriber** — writes received PubSub DataSet values back to an external server.
- **Actions** — maps inbound PubSub Action requests to external server method calls.

Runtime changes to the PubSub configuration store or named adapter options are
hot-reloaded incrementally: unchanged sources, sinks and external-server sessions
are reused, while removed datasets/readers release their session references.
Action target additions and mapping changes are applied by registering updated
handlers; target removal currently requires a host restart because the core
PubSub action responder API has no unregister operation.

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
