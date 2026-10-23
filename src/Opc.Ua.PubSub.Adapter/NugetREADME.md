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
Action target additions, removals and mapping changes are applied during reload
by clearing the previous handlers and registering the currently configured targets.

The fallback client configuration requires explicit peer/issuer trust and
matching server hostname; it does not auto-accept untrusted certificates.
Subscription reload compares complete dataset definitions and retires old
sources before replacement, so removed fields are no longer sampled/exported.
Configured `IndexRange` is preserved through monitoring, cache and publication.
Custom providers must implement `IRangedDataChangeSubscription` for ranged
fields; otherwise those fields fail before data access instead of disclosing
whole values.

```csharp
services.AddOpcUa()
    .AddPubSub(pubsub => pubsub
        .AddPublisher()
        .AddUdpTransport()
        .UseConfigurationFile("pubsub-config.xml")
        .AddServerAsPublisher(options =>
        {
            options.Connection.EndpointUrl = "opc.tcp://plant-server:4840";
            options.ReadMode = ReadMode.Subscription; // or Cyclic
        }));
```

See `docs/PubSub.md` for the full guide.
