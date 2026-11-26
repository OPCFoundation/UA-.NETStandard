<!--
SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
SPDX-License-Identifier: MIT
-->

# Refresh managed subscription values after recovery

A managed V2 subscription can request current values when its session
reactivates or when the existing server subscription transfers successfully
to a replacement session in the same process. This is off by default. It
doesn't run on initial connect, when a subscription is created from scratch,
or during restore or takeover from a persisted snapshot.

For recovery that replaces the session, enable in-process transfer with
`ManagedSessionBuilder.WithTransferSubscriptionsOnRecreate()`. Refresh does
not enable transfers or change how the client handles a failed transfer.

Set `RefreshValuesOnRecovery` when registering the subscription. The setting
applies to all of its partitions and is not saved in subscription snapshots.
Pass a notification handler that also implements `ISubscriptionValueSnapshotHandler`:

```csharp
ISubscription subscription = session.AddSubscription(handler, options => options with
{
    PublishingEnabled = true,
    PublishingInterval = TimeSpan.FromSeconds(1),
    KeepAliveCount = 10,
    LifetimeCount = 100,
    RefreshValuesOnRecovery = true
});
```

In a dependency-injected application, set the same option on the client
builder and pass the injected `IOptionsMonitor<SubscriptionOptions>` to
`session.AddSubscription(handler, subscriptionOptions)`:

```csharp
clientBuilder.AddSubscriptions(options => options.RefreshValuesOnRecovery = true);
```

After recovery, the client tries `Server.ResendData` for each surviving
publishing partition. If the method succeeds, values arrive as ordinary
notifications. If the method fails, the client reads reporting `Value`
items in batches bounded by `MaxNodesPerRead`. Reads use the applied node,
index range and data encoding. A failed read does not fail the recovery.

Use the snapshot callback to feed the same application value handler as your
normal data-change callback. For example, implement this method alongside
the other `ISubscriptionNotificationHandler` methods:

```csharp
public ValueTask OnValueSnapshotAsync(
    ISubscription subscription,
    IMonitoredItem monitoredItem,
    DataValue value,
    CancellationToken ct)
{
    return ApplyValueAsync(monitoredItem, value, ct);
}
```

The client discards bad results and results made obsolete by a newer
recovery, changed item state, or an intervening notification. When a value
has already been delivered for the item state, the snapshot must have a
strictly newer comparable timestamp. The callback has no Publish sequence
number or acknowledgement. It supplies a current value, not missed samples
or events, and it doesn't change normal Publish or Republish delivery.

See [OPC 10000-4 §6.7](https://reference.opcfoundation.org/specs/OPC-10000-4/v1.05.07/6.7)
for the distinction between recovering messages and refreshing values, or
[the classic subscription recipe](Subscriptions.md#refreshing-classic-monitored-values-when-resenddata-fails)
if you manage recovery yourself.
