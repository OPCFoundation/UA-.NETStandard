# Final-value publication before node removal

Some application models require a terminal observation before deleting dynamic
nodes. For example, OPC 40501-1 §8.4.1 requires a production job's final values to
be sent to clients with active subscriptions before removing the job.

`ClearChangeMasksAsync` is not a publication barrier. It admits
report-by-exception work to a consumer. Even after the consumer queues a value,
deleting its node can replace a queue-size-one item's pending value with
`BadNodeIdUnknown`. A subscription can also dequeue several monitored values
while constructing messages for **later** Publish requests.

## Calling the barrier

`AsyncCustomNodeManager` implements the additive `INodeValuePublication`
capability. Derived DI, fluent, and generated managers inherit it. No member was
added to `IAsyncNodeManager` or `IMonitoredItem`.

```csharp
// First finish the application's terminal-state update and stop its producers.
await manager.WaitForValuePublicationAsync(
    [job],
    TimeSpan.FromSeconds(10),
    cancellationToken);

// Remove only after successful observation. Failure must retain the job.
cancellationToken.ThrowIfCancellationRequested();
await manager.DeleteNodeAsync(
    manager.SystemContext,
    job.NodeId,
    cancellationToken);
```

The overload `(ArrayOf<NodeState> nodes, CancellationToken cancellationToken)`
uses a **30-second** deadline. The explicit timeout must be positive, finite,
and supported by the platform timer. One deadline covers membership capture,
pending report work, final reads, and publication. The server's
`ITimeProviderProvider.TimeProvider` supplies the deadline clock, falling back
to `TimeProvider.System`.

The roots and their descendants must be registered in this manager. Repeated
or overlapping roots are deduplicated. All monitored Value attributes in the
subtrees participate, not just the roots themselves.

The operation is available directly on an existing manager. An application can
also inject `INodeValuePublication`, binding it to its **selected owning
manager instance**, or pass that instance to its runtime provider. There is no
global singleton that guesses which namespace's manager owns a node.

A final-observation callback can delegate without duplicating queue logic:

```csharp
Func<ArrayOf<NodeState>, CancellationToken, ValueTask> publishFinal =
    (nodes, ct) => manager.WaitForValuePublicationAsync(nodes, ct);
```

Do not replace ordinary equipment refreshes with this call. Ordinary
`ClearChangeMasksAsync` updates should not wait for client Publish requests.

## What completion means

Completion means that each required final notification has entered a
**constructed response to a real client Publish request**, or that its
reporting obligation has ended as described below. It does **not** mean:

- A notification was merely queued or dequeued.
- A later, prebatched `NotificationMessage` was constructed.
- The transport finished sending bytes.
- The client received, processed, or acknowledged a sequence number.

The implementation uses the standard subscription pipeline. It neither
creates Publish requests nor sends synthetic responses. It tracks the exact
notification instance selected for the response, not client handles (which
can be duplicated), queue lengths, or an enqueue/publish counter.

Transport failure after response construction remains possible. An application
requiring client acknowledgement needs a stronger, separately defined
acknowledgement protocol; this interface does not claim to provide one.

## Sampling and queue behavior

1. Capture the relevant reporting monitored items and their sessions without
   retaining a node-manager gate.
2. Fence already-admitted report-by-exception consumer work without stopping
   the consumer. A fence is only a sampling-order guarantee.
3. Read a fresh final Value for each subscriber through the node manager's
   normal read and permission paths, preserving index range and data encoding.
   Periodically sampled items use this same explicit final read; an old
   periodic read cannot substitute for it.
4. Queue one required final sample through each participating monitored item's
   normal bounded queue. Data-change trigger/deadband suppression is bypassed
   for this sample, so unchanged or deadband-filtered terminal values are
   observable. No node is written and permissions are not bypassed.
5. Await the corresponding actual Publish responses.

Queue size, discard policy, timestamp selection, localization, and the
subscription's notification/message limits still apply. Older queued values
may occupy earlier responses. Queue overflow may mark the final sample's
Overflow bit. The required sample never adds an over-capacity queue entry.

While a final observation is pending, ordinary samples for that item cannot
overwrite it. This also protects against previously started asynchronous
sampling work. The application must stop changing the subtree before calling
the barrier and keep it unchanged through removal. On cancellation, the waiter
and queue protection are released; an already queued or transmitted
notification is not rolled back. Normal sampling remains available.

## Membership and lifecycle

Membership is a snapshot at admission, not an address-space transaction or a
subscription-admission lock.

| Situation | Behavior |
| --- | --- |
| Reporting Value item with a live session | Requires a final Publish response |
| Publishing disabled, but session still live | Continues waiting until enabled, cancelled, or timed out |
| Disabled or Sampling item at capture | Excluded; triggering does not change this policy |
| Event item or non-Value attribute | Excluded |
| Detached durable subscription with no session | Excluded |
| Captured item leaves Reporting | Releases that item's obligation |
| Item/subscription deleted, session closed, or subscription expires | Releases that subscriber's obligation; other subscribers still have to publish |
| Subscription transferred during observation | Fails rather than reusing a read made for the old owner |
| Source deleted or detached before observation finishes | Fails |
| Overlapping finalization of the same pending item | Fails with `BadInvalidState`; does not supersede its predecessor |
| Manager shutdown | Cancels observation before waiting for admitted operations to drain |
| Deadline or caller cancellation | Fails without deleting nodes |

New subscriptions or newly enabled items after capture are outside that
snapshot. The operation does not freeze admission between completion and a
later `DeleteNodeAsync`. Applications that need an atomic, changing-membership
retirement transaction must coordinate that policy separately. Likewise,
source/read providers must honor cancellation; the server cannot forcibly
terminate arbitrary application callbacks.

## Custom monitored items and failures

`IMonitoredItemValuePublication` is an optional capability for custom items.
The standard `MonitoredItem` implements it with the standard `Subscription`
pipeline. A relevant custom item without the capability produces
`NotSupportedException`, never a successful omission.

Aggregated items are not supported: publishing an unprocessed terminal value
as an aggregate would misrepresent its filter. They fail explicitly. A custom
item implementing the capability must honor the same deadline, cleanup,
session-ownership, and response-boundary contract. A `MonitoredItem` subclass
overriding per-value publication must preserve the base notification tracking,
or implement its own capability; bypassing tracking cannot produce successful
completion.

`TimeoutException`, cancellation, permission/read errors, unsupported items,
and concurrent-finalization errors are all reasons to **retain the subtree**.
The barrier does not remove a node, dispose a subscriber, alter monitoring
mode, or enable publishing on the caller's behalf.

## Regression coverage

`MonitoredItemPublicationTests` exercises bounded batches, exact final samples,
zero budgets, competing observations, cancellation, injected-clock deadlines,
and item lifecycle through the optional interface.

`ValuePublicationIntegrationTests` uses real opc.tcp sessions and ordinary
Publish requests. It verifies queue-size-one final-before-deletion ordering,
two-client obligations, partial prebatched responses, both overflow policies,
report-consumer fencing, periodic sampling, filtering/unchanged values,
per-subscriber reads, teardown, and retention on failed observation.

On .NET Framework, set `OPCUA_TEST_PKI_ROOT` to a short writable test directory
when build artifacts live under a long path. The fixture removes its unique
certificate-store child directory at teardown.
