# TransferSubscriptions

## Contents

- [Overview](#overview)
- [Porting an existing server](#porting-an-existing-server)
- [Known limitations and issues](#known-limitations-and-issues)
- [Recovering from unsolicited `Good_SubscriptionTransferred`](#recovering-from-unsolicited-good_subscriptiontransferred)
  - [Caveats](#caveats)

## Overview

`TransferSubscriptions` moves existing server subscriptions to another session.
It can reduce data loss during reconnect or client restart by preserving the
server's monitored items and queued notifications. It does not guarantee zero
loss: subscription lifetime, queue capacity, retransmission availability, and
authorization still apply.

The server and client implement the
[Part 4 TransferSubscriptions service](https://reference.opcfoundation.org/Core/Part4/v105/docs/5.14.7).
The target session must have permission to transfer the subscriptions. Transfer
also relies on the server's `GetMonitoredItems` method when the client needs to
recover monitored-item handles.

For current client APIs:

- Use [ManagedSession recovery](Sessions.md#reconnect-semantics-on-managedsession)
  for connection recovery; do not add `SessionReconnectHandler` around a
  `ManagedSession`.
- Use [subscription persistence and transfer](Subscriptions.md#persistence-save--load)
  to save client state and restore it after restart. A transfer attempts to
  recover an existing server subscription; recreating one starts new queues.
- If subscriptions must survive a deliberate session close, configure
  `DeleteSubscriptionsOnClose = false` before closing it. They remain subject to
  their server-side lifetime; this does not make them durable across server restart.
  See [durable subscriptions](DurableSubscription.md) for that separate capability.

## Porting an existing server

Servers based on `StandardServer` and the built-in node-manager bases already
participate in monitored-item transfer. A custom manager implementing the
interfaces directly must implement the transfer contract, including initial-value
queuing when requested, without retaining stale source-session ownership.
See [node managers](NodeManagers.md) and the
[2.0 session/subscription migration guide](migrate/2.0.x/sessions-subscriptions.md).

Transfer support was introduced in 1.4.368. The
[historical sample port](https://github.com/OPCFoundation/UA-.NETStandard-Samples/pull/267/commits/5d990b7f39880941a5e788d17b903fd41254a804)
is relevant only when upgrading from releases predating that support; it is not
the current SDK API reference.

## Known limitations and issues

- Transfer cannot recover samples already lost to queue overflow or expired
  subscriptions.
- Server restart requires server-side durability or mirroring to retain the
  subscription; saving client configuration alone is insufficient.
- Recovery from an unsolicited transfer notification is an explicit compatibility
  policy, described below, not a substitute for legitimate cross-session transfer.

## Recovering from unsolicited `Good_SubscriptionTransferred`

Per [OPC UA Part 4 §5.14.7](https://reference.opcfoundation.org/Core/Part4/v105/docs/5.14.7) the server emits a `StatusChangeNotification` with `Good_SubscriptionTransferred` on the **old** Session whenever the subscription is transferred away to a **new** Session via `TransferSubscriptions`. The receiving Session is expected to treat the subscription as gone and stop dispatching for it.

Some servers have been observed to deliver this notification against a subscription the client has just **freshly created** on its current Session — for example, Kepware after a server re-initialisation can leak the pre-restart subscription's pending status notifications onto the new session (especially noticeable because subscription identifiers are re-used starting at `1`). The client then sees the per-subscription dispatch silently disabled even though, from its point of view, the subscription is alive and should keep receiving data. Tracked as issue [#3540](https://github.com/OPCFoundation/UA-.NETStandard/issues/3540).

To opt into automatic in-place recovery, set `SubscriptionRecoveryPolicy.RecreateOnUnsolicitedTransfer`:

```csharp
// Classic (V1) subscription
var subscription = new Subscription(telemetry, options)
{
    RecoveryPolicy = SubscriptionRecoveryPolicy.RecreateOnUnsolicitedTransfer,
    FastDataChangeCallback = OnDataChange
};
```

```csharp
// V2 subscription options
var options = new Opc.Ua.Client.Subscriptions.SubscriptionOptions
{
    PublishingEnabled = true,
    RecoveryPolicy = SubscriptionRecoveryPolicy.RecreateOnUnsolicitedTransfer
};
```

When the policy is `RecreateOnUnsolicitedTransfer` and a `Good_SubscriptionTransferred` arrives while the subscription is still actively owned by this Session, the SDK will:

1. Drop every queued acknowledgement targeting the dead subscription id — this prevents `BadSubscriptionIdInvalid` ack errors on servers that re-use subscription identifiers across generations.
2. Recreate the subscription on the **same** Session via `CreateSubscription`, obtaining a fresh server-side subscription identifier.
3. Re-issue the monitored items so the data flow resumes against the new identifier.

The default is `SubscriptionRecoveryPolicy.ReportOnly`, which preserves the spec-strict behaviour: the `PublishStateChangedMask.Transferred` flag is raised (V1) or `PublishState.Transferred` is dispatched (V2) so the application can react manually, and the per-subscription publish dispatch is stopped (V1) or left as a no-op (V2).

### Caveats

* Auto-recovery is **not** lossless failover. The server-side retransmission queue and any triggering relationships tied to the invalidated subscription identifier are lost; only the subscription's wire-level options and the configured monitored items are re-applied.
* The recovery path runs against the same Session. If the Session itself is reconnecting, the V1 implementation defers to the reconnect pipeline rather than racing it; the V2 implementation lets the in-place recreate run when the subscription is `Created`.
* The "unsolicited" classification is conservative — concurrent recovery dispatches collapse into one — but it does not attempt to detect every legitimate cross-session/cross-client failover. If your design relies on another Session/Client legitimately pulling subscriptions away via `TransferSubscriptions`, keep the default `ReportOnly` policy and handle the `PublishStatusChanged` event explicitly.
