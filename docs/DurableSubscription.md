# Durable Subscription

## Overview

Durable subscriptions let servers retain subscriptions for longer periods
and use large notification queues. If a client disconnects, the server
continues sampling values. After reconnecting, the client can use the
TransferSubscriptions service to retrieve missed notifications.

## Contents

- [Overview](#overview)
- [Fix an existing server with minimal changes](#fix-an-existing-server-with-minimal-changes)
- [Enabling durable subscriptions on an existing server](#enabling-durable-subscriptions-on-an-existing-server)
- [Known limitations and issues](#known-limitations-and-issues)

## Fix an existing server with minimal changes

- If you implement a custom `INodeManager` or override
  `CreateMonitoredItems`, add the `createDurable` parameter. You can ignore
  it while durable subscriptions are disabled in the server configuration.
- Implement `INodeManager.RestoreMonitoredItems`. The server calls this
  method only when durable subscriptions are enabled.
- If you implement a custom `IMonitoredItem`, set `IsDurable` to `false`
  and implement `Dispose`.

## Enabling durable subscriptions on an existing server

Typically the following porting steps are necessary:

- Implement `IMonitoredItemQueueFactory` and set
  `SupportsDurableQueues` to `true`. Return an `IMonitoredItemQueue` that
  persists values and supports large queue sizes. See the sample
  [DurableMonitoredItemQueueFactory](../samples/Quickstarts.Servers/DurableSubscription/DurableMonitoredItemQueueFactory.cs).
- Register the queue factory by overriding
  `StandardServer.CreateMonitoredItemQueueFactory`.
- Implement `ISubscriptionStore` to persist and restore subscriptions across
  server restarts. After restoring a subscription, the store must also provide
  persistent queues to its monitored items. Register the store by overriding
  `StandardServer.CreateSubscriptionStore`.
- Report a store that cannot read its subscriptions from
  `RestoreSubscriptionsAsync`, either by throwing or by returning
  `RestoreSubscriptionResult.Success = false`; return `Success = true` with no
  subscriptions when nothing was stored. Keep the stored subscriptions and the
  queue snapshots of their monitored items until
  `OnSubscriptionRestoreCompleteAsync` is called: the server calls it only
  after it has recreated every stored subscription, so a store that deletes
  its data while reading it loses the subscriptions when the restore fails.
- A failed restore fails `SubscriptionManager.RestoreSubscriptionsAsync` and
  with it the server startup: a store failure surfaces as a
  `ServiceResultException` (`BadInternalError`), stored subscriptions that
  cannot be recreated as an `AggregateException`. To start anyway, set
  `SubscriptionManager.ContinueOnRestoreFailure` to `true` in an override of
  `StandardServer.CreateSubscriptionManager`; failures are then logged as
  errors and warnings, and the subscriptions concerned are lost. A restore that
  fails or is cancelled deletes the subscriptions it already recreated without
  storing them; it keeps the stored contents of their monitored item queues and
  resets what it set on the stored subscriptions and monitored items the store
  returned, so the store may return the same objects to a retry. A server
  startup that fails releases its session and subscription managers, so the
  restore can be retried by starting again.
- A queue whose contents are kept in storage while the server runs, and which
  disposing removes from that storage, should implement
  `IRestorableMonitoredItemQueue`. While
  `SubscriptionManager.RestoreSubscriptionsAsync` restores the subscriptions it
  calls `BeginRestore` before it changes such a queue of a restored monitored
  item; the queue must then not write to its storage. When every stored
  subscription was restored and the store was told so, the server calls
  `CommitRestore`, and the queue writes its current contents once and resumes
  writing. The restore can no longer be rolled back then, so `CommitRestore`
  must not throw: a queue that cannot write handles the failure itself, e.g.
  logs it and writes on its next change; the server logs an exception it throws
  and commits the other queues. When the restore
  fails or is cancelled the server calls `AbortRestore` and disposes the queue;
  the queue must drop every write it has not started, never start a write
  again, and not remove its stored contents on dispose, because the retry reads
  them again. A write already in progress when `AbortRestore` is called cannot
  be recalled and may complete; a queue staged by `BeginRestore` starts no
  write before `CommitRestore`, so its stored contents stay exactly as they
  were. A queue
  disposed after `BeginRestore` but before `CommitRestore`, e.g. because a node
  manager failed to create its monitored item, must keep its stored contents as
  well. The mirrored queues of `SharedKeyValueMonitoredItemQueueFactory`
  implement it.
  Only the base `SubscriptionManager.RestoreSubscriptionsAsync` stages queues:
  a monitored item restored outside of it, e.g. by calling
  `Subscription.RestoreAsync` or the restoring `MonitoredItem` constructor
  directly, writes its queue to storage at once, as before.
- If you implement a custom `INodeManager` or override
  `CreateMonitoredItems`, add the `createDurable` parameter and pass it to
  the `MonitoredItem` constructor. When you check queue length for a durable
  subscription, compare it with the durable queue limit in
  `ServerConfiguration`.
- If you implement a custom `IMonitoredItem`, add the `createDurable`
  constructor parameter, implement `IsDurable`, and implement `Dispose`.
- In a custom `MonitoredItem`, use
  `IServerInternal.MonitoredItemQueueFactory` to get the registered durable
  queues for events and value changes instead of using internal queues.
- To test custom queues, adapt the monitored-item tests in the server test
  project. Provide your `IMonitoredItemQueueFactory` in the test constructor.

Configure durable subscriptions in `ServerConfiguration`:

- Set `DurableSubscriptionsEnabled` to true
- Set `MaxDurableNotificationQueueSize` to the desired value
- Set `MaxDurableEventQueueSize` to the desired value
- Set `MaxDurableSubscriptionLifetime` to the desired value

## Known limitations and issues

- The Quickstarts queues return `false` from `Dequeue` while a batch is
  being persisted or cannot be restored within the bounded wait. The queue
  retains the item count for a later attempt. A failed restore does not mean
  the batch data is resident. The store deletes restored batch files after
  their readers close.
- The server persists subscriptions only during a graceful shutdown. A crash
  or forced shutdown loses all subscriptions and monitored items.
- The Quickstarts durable-subscription store writes format version 3. It can
  read version 1 records, which enable publishing but do not persist the
  owning application URI. The store rejects retired version 2 records.
  Current records persist publishing state and the owning client
  `ApplicationUri`, allowing anonymous durable subscriptions to transfer
  after restart. The store removes user-name passwords before persistence.
  It does not persist issued-token subscriptions because their bearer token
  is their identity.
- **Breaking change:** The `INodeManager` and `IMonitoredItem` interfaces
  were extended to support durable subscriptions.
