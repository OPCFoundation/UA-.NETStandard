# V2 subscription engine — feature parity matrix vs. the classic engine

Snapshot for the test split + classic-deprecation roadmap. The matrix maps every
classic public surface exercised by the integration tests under
`Tests/Opc.Ua.Subscriptions.Tests/` to its V2 equivalent.

Status legend:

* **Direct** — V2 has a 1:1 public method/property that the V2 tests can call straight through.
* **Via raw service** — no V2 surface yet; the V2 tests should call the underlying
  service-set on `ISession` directly with a `// TODO(V2): expose <Op>Async on ISubscription` marker.
* **Adding in this PR** — the V2 surface is being added as part of the test split work.
* **Deferred** — classic-specific knob that is not blocking the V2 test ports. Needs a
  follow-up before the classic engine can be deleted.

## 1. `Opc.Ua.Client.Subscription` (classic) → `Opc.Ua.Client.Subscriptions.ISubscription` (V2)

### 1.1 Lifecycle

| Classic | V2 | Status | Notes |
|---|---|---|---|
| `new Subscription(template)` + `Session.AddSubscription(s)` + `s.CreateAsync()` | `ISubscriptionManager.Add(handler, IOptionsMonitor<SubscriptionOptions>)` | Direct | V2 creates on the server asynchronously after `Add`; tests poll `subscription.Created`. |
| `Session.RemoveSubscriptionAsync(s)` | `await subscription.DisposeAsync()` | Direct | V2 removal is dispose-on-subscription. |
| `s.CreateAsync(ct)` / `s.ModifyAsync(ct)` / `s.DeleteAsync(silent, ct)` | implicit via `Add` / options push / `DisposeAsync` | Direct | No explicit V2 calls; behavior is driven by options + lifecycle. |
| `s.SetPublishingModeAsync(bool, ct)` | push `SubscriptionOptions { PublishingEnabled = ... }` via `OptionsMonitor` | Direct | Tests update options through the monitor; the V2 manager picks up the change. |
| `s.ChangesPending` / `s.ChangesCompleted()` | n/a | Deferred | V2 is fully push-driven; no "pending changes" concept. Test ports should use options pushes + waits. |

### 1.2 Notifications and callbacks

| Classic | V2 | Status |
|---|---|---|
| `s.FastDataChangeCallback` (delegate) | `ISubscriptionNotificationHandler.OnDataChangeNotificationAsync(...)` | Direct |
| `s.FastKeepAliveCallback` (delegate) | `ISubscriptionNotificationHandler.OnKeepAliveNotificationAsync(...)` | Direct |
| `s.FastEventCallback` | `ISubscriptionNotificationHandler.OnEventDataNotificationAsync(...)` | Direct |
| `item.Notification += handler` (per-item event) | per-item dispatch through the handler with `DataValueChange.MonitoredItem` to identify the source | Direct |
| `item.DequeueValues()` (client-side cache) | n/a — V2 streams values into the handler; caller stores if needed | Deferred (test-only need; ports keep their own list) |
| `s.LastNotification` / `s.Notifications` / `s.LastNotificationTime` | `s.MissingMessageCount` / `RepublishMessageCount` + handler `publishTime` | Deferred (no equivalent surface; tests should track via handler) |
| `s.PublishingStopped` | computed internally in `Subscription.cs` (V2); not on `ISubscription` | Deferred (expose if tests need it; default to raw `MissingMessageCount` checks) |

### 1.3 Subscription-level service operations

| Classic | V2 | Status |
|---|---|---|
| `s.RepublishAsync(seq, ct)` | raw `session.RepublishAsync(null, subscriptionId, seq, ct)` | Via raw service |
| `s.ResendDataAsync(ct)` | raw `session.CallAsync(null, ResendData methodId, ...)` | Via raw service |
| `s.ConditionRefreshAsync(ct)` | `s.ConditionRefreshAsync(ct)` | Direct |
| `s.ConditionRefresh2Async(monitoredItemId, ct)` | n/a | Deferred |
| `s.SetTriggeringAsync(triggering, links, removes, ct)` | **`s.SetTriggeringAsync(triggeringClientHandle, linksToAdd, linksToRemove, ct)`** | **Adding in this PR** |
| `s.TransferAsync(target, sendInitialValues, ct)` | `ISubscriptionManager` transfer-on-recreate via `ManagedSessionBuilder.WithTransferSubscriptionsOnRecreate(true)` | Direct (different shape; covered by V2-shaped transfer tests) |
| `s.SetSubscriptionDurableAsync(...)` | n/a | Deferred (durable tests are out of scope this round) |
| `s.SaveMessageInCache(...)` | n/a | Deferred (classic internal) |

### 1.4 Monitored-item management

| Classic | V2 | Status |
|---|---|---|
| `s.AddItem(item)` / `s.AddItems(IEnumerable)` | `s.MonitoredItems.TryAdd(name, IOptionsMonitor<MonitoredItemOptions>, out IMonitoredItem)` | Direct (V2 keys by name; tests pass a stable string id) |
| `s.RemoveItem(item)` / `s.RemoveItems(...)` | `s.MonitoredItems.TryRemove(clientHandle)` | Direct |
| `s.ApplyChangesAsync(ct)` | n/a — V2 batches automatically via options monitor | Direct |
| `s.CreateItemsAsync(ct)` / `s.ModifyItemsAsync(ct)` / `s.DeleteItemsAsync(...)` | implicit via `TryAdd` / options push / `TryRemove` | Direct |
| `s.SetMonitoringModeAsync(mode, ids, ct)` | push `MonitoredItemOptions { MonitoringMode = ... }` per item | Direct |
| `s.ResolveItemNodeIdsAsync(ct)` | n/a (V2 uses `StartNodeId` directly; relative-path resolution is caller-side) | Deferred (test-only need) |
| `s.MonitoredItems` / `s.MonitoredItemCount` | `s.MonitoredItems.Items` / `s.MonitoredItems.Count` | Direct |

### 1.5 Persistence (Save / Load)

| Classic | V2 | Status |
|---|---|---|
| `session.Save(Stream, IEnumerable<Subscription>)` (BinaryEncoder + `SubscriptionState.Encode`) | **`ISubscriptionManager.Save(Stream, IServiceMessageContext, ...)`** | **Added in this PR** |
| `session.Load(Stream, bool transferSubscriptions)` | **`ISubscriptionManager.LoadAsync(Stream, IServiceMessageContext, handlerFactory, false, ct)`** for recreate; `transferSubscriptions:true` currently **throws `NotImplementedException`** — see Deferred row below | **Added in this PR (recreate only)** |
| `s.Snapshot(out SubscriptionState)` / `s.Restore(SubscriptionState)` | V2 captures the same info via the serializer's binary header + per-subscription block; no per-subscription Snapshot/Restore is exposed on `ISubscription` (callers use the manager-level Save/Load) | Deferred (per-subscription surface) |

### 1.6 Tuning / classic-specific knobs

| Classic | V2 | Status |
|---|---|---|
| `s.MaxMessageCount` | n/a | Deferred |
| `s.MinLifetimeInterval` (property + `SubscriptionOptions.MinLifetimeInterval`) | already on V2 `SubscriptionOptions` | Direct |
| `s.DisableMonitoredItemCache` | n/a (V2 has no per-item cache to disable) | Deferred (V2 design choice — handler is the cache) |
| `s.SequentialPublishing` | n/a | Deferred (V2 publish pipeline is channel-based; sequential publishing test is `Inconclusive` on V2 with TODO) |
| `s.RepublishAfterTransfer` | implicit via `MessageProcessor.TryRepublishAsync` (always-on gap fill) | Direct (no opt-out; tests assert republish counters move on transfer) |
| `s.PublishStatusChanged` / `s.StateChanged` events | n/a | Deferred (test-only need; ports keep counters via handler) |
| `s.OutstandingMessageWorkers` | n/a (V2 manager-wide `PublishWorkerCount`) | Direct (manager-level) |
| `s.Id` / `s.TransferId` | internal in V2 `Subscription`; not on `ISubscription` | Deferred (resolve via reflection if needed by tests; per `UaLens diagnostics` memory). |
| `s.Handle` (caller bookkeeping) | not on `ISubscription`; tests use a side dictionary keyed by name | Direct (test convention) |

## 2. `Opc.Ua.Client.MonitoredItem` (classic) → `Opc.Ua.Client.Subscriptions.MonitoredItems.IMonitoredItem` (V2)

### 2.1 Configuration

All `MonitoredItemOptions` fields are direct mappings: `StartNodeId`, `AttributeId`,
`IndexRange`, `Encoding`, `MonitoringMode`, `SamplingInterval`, `Filter`, `QueueSize`,
`DiscardOldest`, `TimestampsToReturn`. Order/Name keys are V2-only.

`item.RelativePath` / `item.ResolveItemNodeIdsAsync` / `item.DisplayName` are classic
caller conventions; the V2 tests use a stable per-item `Name` string.

### 2.2 Status / runtime

| Classic | V2 | Status |
|---|---|---|
| `item.ClientHandle` | `IMonitoredItem.ClientHandle` | Direct |
| `item.ServerId` | `IMonitoredItem.ServerId` | Direct |
| `item.Status.Error` / `item.Status.Created` / `item.Status.Id` | `IMonitoredItem.Error` / `Created` / `ServerId` | Direct |
| `item.AttributesModified` | n/a (V2 reconciles on options change) | Deferred |
| `item.Filter` round-trip | `IMonitoredItem.FilterResult` | Direct |
| `item.DequeueValues()` / `item.LastValue` | n/a — values flow through `ISubscriptionNotificationHandler.OnDataChangeNotificationAsync(...)` | Direct (handler-side) |
| `item.Notification += ...` (event) | per-item dispatch through `OnDataChangeNotificationAsync` with `DataValueChange.MonitoredItem` | Direct |
| `item.GetEventTypeAsync` / `GetFieldValue` / `GetEventTime` / `GetFieldName` | n/a on V2 `IMonitoredItem` | Deferred (caller-side helpers; tests can carry helpers) |
| `item.TriggeringItemId` / `item.TriggeredItems` | added on V2 `MonitoredItem` as part of **Adding in this PR** (Phase C step 5) | Adding in this PR |

## 3. `Session` engine wiring

| Classic surface | V2 surface | Status |
|---|---|---|
| `Session.SubscriptionEngineFactory` (default `ClassicSubscriptionEngineFactory.Instance`) | flipping default to `DefaultSubscriptionEngineFactory.Instance` (Phase E) | Adding in this PR |
| `ClientFixture.SubscriptionEngineFactory` opt-back property | new test framework property (Phase D) | Adding in this PR |
| `Session.AddSubscription(Subscription)` (classic-typed) | unchanged — classic subscriptions still added via this API on classic-engine sessions | Direct |
| `ManagedSession.SubscriptionManager` (V2) | unchanged | Direct |

## 4. Test fixtures and helpers

| Classic helper | V2 helper | Status |
|---|---|---|
| `TestableSubscription : Subscription` | n/a — V2 subscriptions are sealed instances created by the manager | Direct (test convention: subclass the handler instead) |
| `TestableMonitoredItem : MonitoredItem` | n/a | Direct |
| `ClientTestFramework.CreateSubscriptionsAsync(...)` | `CreateV2SubscriptionsAsync(...)` (Phase D step 8) | Adding in this PR |
| `ClientTestFramework.CreateMonitoredItemTestSet(...)` | `CreateV2MonitoredItemTestSet(...)` (Phase D step 8) | Adding in this PR |
| inline `RecordingHandler` in `ManagedSessionSubscriptionManagerIntegrationTests.cs` | `RecordingSubscriptionHandler` (Phase D step 7) | Adding in this PR |

## 5. Coverage gap summary (drives follow-up work after this PR)

The following classic surfaces have **no** V2 equivalent today and remain a blocker
for classic engine deletion. They are intentionally deferred from this PR but listed
so the next round has a concrete target list:

* `ResendDataAsync` on V2 `ISubscription`.
* Manual `RepublishAsync(seq)` on V2 `ISubscription` (V2 has automatic gap-driven
  republish but not user-driven).
* **`ISubscriptionManager.LoadAsync(transferSubscriptions: true)`** — the current
  implementation throws `NotImplementedException`. A safe transfer path requires
  a new "load with state" entry point on the manager that creates the V2
  instance without queuing `CreateMonitoredItem` requests (which the V2 state
  machine's `Debug.Assert(request.RequestedParameters.ClientHandle ==
  Item.ClientHandle)` would otherwise trip on, because the snapshot's client
  handle differs from the freshly-generated one) and then issues an explicit
  `TransferSubscriptions` call to rebind to the server-side state.
* `FastDataChangeCallback` / `FastKeepAliveCallback` style callbacks (V2 already has
  `ISubscriptionNotificationHandler`; the deferred work is exposing additional
  per-message metadata that classic surfaced via the callback args).
* `SequentialPublishing` switch (V2 channel pipeline is inherently parallel by
  design; needs a deliberate API + implementation pass).
* `PublishStatusChanged` / `StateChanged` events.
* `DisableMonitoredItemCache` / `MaxMessageCount` (deliberately not ported — V2
  design replaces both with the handler-as-cache and unbounded channel model).
* `ConditionRefresh2Async` (per-item refresh).
* `SetSubscriptionDurableAsync` (durable subscriptions — separate test project).
* `Snapshot(out SubscriptionState)` / `Restore(SubscriptionState)` on individual
  subscriptions (V2 ships manager-level save/load only; per-subscription save needs
  separate API design).

Each row above is captured as a `// TODO(V2)` marker at the call site in the V2 test
ports, so a reader can find both the deferred functionality and the proxy raw-service
call that exercises it today.
