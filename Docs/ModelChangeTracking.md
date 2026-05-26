# Model Change Tracking

OPC UA Part 5 §6.4.32 defines `GeneralModelChangeEventType` for notifying clients
about address-space model changes (added/deleted nodes and references, type
changes). The `ModelChangeTracker` consumes these events on the client side and
invalidates the node cache so subsequent browses see the new structure.

## Server-side

Custom node managers raise model change events via:

```csharp
var aggregator = new ModelChangeAggregator();
aggregator.RecordNodeAdded(nodeId, typeDefinitionId);
aggregator.RecordReferenceAdded(sourceNodeId);

// At the end of a service call or publish cycle
if (aggregator.HasPending)
{
    RaiseGeneralModelChangeEvent(context, aggregator.Drain());
}
```

`CustomNodeManager` and `AsyncCustomNodeManager` both expose
`RaiseGeneralModelChangeEvent` and `RaiseSemanticChangeEvent`. The aggregator
batches changes so a single event reports many changes per service call.

## Client-side

```csharp
var streaming = new StreamingSubscription(session.SubscriptionManager);
var tracker = new ModelChangeTracker(streaming, nodeCache: session.NodeCache);

tracker.ModelChanged += (sender, args) =>
{
    foreach (ModelChange change in args.Changes)
    {
        Console.WriteLine($"{change.Verb}: {change.AffectedNode}");
    }

    if (args.RequiresFullCacheInvalidation)
    {
        // Re-browse interesting areas
    }
};

await tracker.StartTrackingAsync();
```

The tracker subscribes to `GeneralModelChangeEventType` (and its subtype
`SemanticChangeEventType`) on the `Server` object's notifier. When changes
arrive, it invalidates the configured `INodeCache` and raises the
`ModelChanged` event so callers can refresh their browse state.

## When to use

* Servers with dynamically changing address spaces (e.g., devices joining/
  leaving, configuration changes)
* Clients that cache browse results long-term and need invalidation
* Applications that build UI trees of OPC UA nodes and need to react to
  structural changes