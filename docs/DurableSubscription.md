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
