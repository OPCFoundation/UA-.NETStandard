# Durable Subscription
## Overview

The durable subscriptions service enables long lifetimes of subscriptions with big queue sizes and therefore zero-dataloss scenarios.
If the connection to the client fails the server continues to sample values. On reconnect the client can use the transfer subscription service to gain access to the missed values.

## Fix an existing server with minimal changes
  - If a custom implementation of `INodeManager` is used, or the Method `CreateMonitoredItems` is overridden, add the new parameter `createDurable`. As long as durable Subscriptions are not enabled in the configuration this parameter can be ignored.
  - Implement`INodeManager.RestoreMonitoredItems`, this method is only called if durable subscriptions are enabled in the server configuration.
  - If a custom `IMonitoredItem` implementation is used, set `IsDurable` to false. Implement a `Dispose` method.

## Enabling durable subscriptions on an existing server

Typically the following porting steps are necessary:

  - Provide an implementation of `IMonitoredItemQueueFactory` that sets `SupportsDurableQueues` to true. This factory returns your own implementation of `IMonitoredItemQueue` that persists values to storage and supports large queue sizes. As a reference see [DurableMonitoredItemQueueFactory](../../Applications/Quickstats.Servers/DurableSubscription/DurableMonitoredItemQueueFactory.cs).
  - Register the `IMonitoredItemQueueFactory` by overriding the StandardServer method `CreateMonitoredItemQueueFactory`.
  - Provide an implementation of `ISubscriptionStore` that persists and restores subscriptions to storage to continue them after a server restart. The subscription store shall also enshure persistent queues are provided to the monitored items after a restore.
  - - Register the `ISubscriptionStore` by overriding the StandardServer method `CreateSubscriptionStore`.
  - If a custom implementation of `INodeManager` is used, or the Method `CreateMonitoredItems` is overridden, add the new parameter `createDurable`. Pass the value of this parameter to the MonitoredItem constructor. If a custom queue length check if performed for durable subscriptions check against the durable queue length from SererConfiguration.
  - If a custom `IMonitoredItem` implementation is used, implement the `IsDurable` property. Implement a `Dispose` method. Add a `createDurable` paramter to the constructor. 
  - Make the custom `MonitoredItem` use the `IMonitoredItemQueueFactory` from `IServerInternal.MonitoredItemQueueFactory` to get the registerd durable queues instead of using internal queues for events & value changes.
  - To test custom queues use the unit tests in Server Test Project in the file monitoredItemTests and adapt to use your own queue by providing your `IMonitoredItemQueueFactory` in the constructor.

Extend the ServerConfiguration
  - Set `DurableSubscriptionsEnabled` to true
  - Set `MaxDurableNotificationQueueSize` to the desired value
  - Set `MaxDurableEventQueueSize` to the desired value
  - Set `MaxDurableSubscriptionLifetime` to the desired value

## Known limitations and issues

- Subscriptions are only persistet on a gracefuls shutdown. If the server crashes or needs to be shut down forcefully all `Subscriptions` / `MonitoredItems` are lost.
- **Breaking change**: The Interfaces for INodeManager & IMonitoredItem were extended to support durable subscriptions. 
