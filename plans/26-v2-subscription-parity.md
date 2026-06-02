# V2 subscription engine — feature parity matrix vs. the classic engine

Snapshot for the test split + classic-deprecation roadmap. The matrix maps every
classic public surface exercised by the integration tests under
`Tests/Opc.Ua.Subscriptions.Tests/` to its V2 equivalent.

Status legend:

* **Direct** — V2 has a 1:1 public method/property that the V2 tests can call straight through.
* **Added** — V2 surface added (or extended) to close a parity gap.
* **Deliberately not ported** — V2 deliberately drops this classic surface as a
  design choice; the V2 design replaces it (handler-centric, channel-based,
  options-driven, etc.). Rationale is captured per-row.
* **Via raw service** — no V2 surface yet; the V2 tests should call the underlying
  service-set on `ISession` directly with a `// TODO(V2): expose <Op>Async on ISubscription` marker.

## 1. `Opc.Ua.Client.Subscription` (classic) → `Opc.Ua.Client.Subscriptions.ISubscription` (V2)

### 1.1 Lifecycle

| Classic | V2 | Status | Notes |
|---|---|---|---|
| `new Subscription(template)` + `Session.AddSubscription(s)` + `s.CreateAsync()` | `ISubscriptionManager.Add(handler, IOptionsMonitor<SubscriptionOptions>)` | Direct | V2 creates on the server asynchronously after `Add`; tests poll `subscription.Created`. |
| `Session.RemoveSubscriptionAsync(s)` | `await subscription.DisposeAsync()` | Direct | V2 removal is dispose-on-subscription. |
| `s.CreateAsync(ct)` / `s.ModifyAsync(ct)` / `s.DeleteAsync(silent, ct)` | implicit via `Add` / options push / `DisposeAsync` | Direct | No explicit V2 calls; behavior is driven by options + lifecycle. |
| `s.SetPublishingModeAsync(bool, ct)` | push `SubscriptionOptions { PublishingEnabled = ... }` via `OptionsMonitor` | Direct | Tests update options through the monitor; the V2 manager picks up the change. |
| `s.ChangesPending` / `s.ChangesCompleted()` | n/a | **Deliberately not ported** (V2 is fully push-driven; no "pending changes" concept — test ports use options pushes + waits) |

### 1.2 Notifications and callbacks

| Classic | V2 | Status |
|---|---|---|
| `s.FastDataChangeCallback` (delegate) | `ISubscriptionNotificationHandler.OnDataChangeNotificationAsync(...)` | Direct |
| `s.FastKeepAliveCallback` (delegate) | `ISubscriptionNotificationHandler.OnKeepAliveNotificationAsync(...)` | Direct |
| `s.FastEventCallback` | `ISubscriptionNotificationHandler.OnEventDataNotificationAsync(...)` | Direct |
| `item.Notification += handler` (per-item event) | per-item dispatch through the handler with `DataValueChange.MonitoredItem` to identify the source | Direct |
| `item.DequeueValues()` (client-side cache) | n/a — V2 streams values into the handler; caller stores if needed | **Deliberately not ported** (handler-as-cache design choice; tests carry their own list when needed) |
| `s.LastNotification` / `s.Notifications` / `s.LastNotificationTime` | `s.MissingMessageCount` / `RepublishMessageCount` + handler `publishTime` | **Deliberately not ported** (handlers maintain their own derived state; the V2 surface exposes only the manager-wide counters) |
| `s.PublishingStopped` | not exposed on `ISubscription`; **handler-derived** via `ISubscriptionNotificationHandler.OnSubscriptionStateChangedAsync` (PublishState mask flips between Republish / Recovered / Transferred) | **Deliberately not ported** (V2 is handler-centric; handlers maintain their own derived state per the design principle in `Docs/MigrationGuide.md`) |

### 1.3 Subscription-level service operations

| Classic | V2 | Status |
|---|---|---|
| `s.RepublishAsync(seq, ct)` | raw `session.RepublishAsync(null, subscriptionId, seq, ct)` (V2 auto-republishes on gap detection via `MessageProcessor.TryRepublishAsync` — no user-driven variant) | **Deliberately not ported** (V2 design: automatic gap-driven republish replaces manual call) |
| `s.ResendDataAsync(ct)` | raw `session.CallAsync(null, ResendData methodId, ...)` | **Deliberately not ported** (V2 design: handler-centric, no manual resend on the public surface) |
| `s.ConditionRefreshAsync(ct)` | `s.ConditionRefreshAsync(ct)` | Direct |
| `s.ConditionRefresh2Async(monitoredItemId, ct)` | `item.ConditionRefreshAsync(ct)` on `IMonitoredItem` (per-item, no monitoredItemId arg) | **Added** (covered by `MonitoredItemConditionRefreshLiveV2Tests` against the reference server's alarm sources) |
| `s.SetTriggeringAsync(triggering, links, removes, ct)` | **`s.SetTriggeringAsync(triggeringClientHandle, linksToAdd, linksToRemove, ct)`** | **Added** |
| `s.TransferAsync(target, sendInitialValues, ct)` | `ISubscriptionManager` transfer-on-recreate via `ManagedSessionBuilder.WithTransferSubscriptionsOnRecreate(true)` + new `SubscriptionOptions.SendInitialValuesOnTransfer` flag (default `false`) | **Added** (covered by `SubscriptionFailoverV2Tests` + `V2FollowUpCoverageTests.SendInitialValuesOnTransferV2Async`) |
| `s.SetSubscriptionDurableAsync(...)` | **`ISubscription.SetSubscriptionDurableAsync(uint lifetimeInHours, CancellationToken ct = default)` → revised lifetime hours** | **Added** (covered by `SubscriptionDurableV2Tests` × 5 ports in `Opc.Ua.Subscriptions.Durable.Tests`) |
| `s.SaveMessageInCache(...)` | n/a | **Deliberately not ported** (classic internal — V2 message pipeline is channel-based, no replay cache) |

### 1.4 Monitored-item management

| Classic | V2 | Status |
|---|---|---|
| `s.AddItem(item)` / `s.AddItems(IEnumerable)` | `s.MonitoredItems.TryAdd(name, IOptionsMonitor<MonitoredItemOptions>, out IMonitoredItem)` | Direct (V2 keys by name; tests pass a stable string id) |
| `s.RemoveItem(item)` / `s.RemoveItems(...)` | `s.MonitoredItems.TryRemove(clientHandle)` | Direct |
| `s.ApplyChangesAsync(ct)` | n/a — V2 batches automatically via options monitor | Direct |
| `s.CreateItemsAsync(ct)` / `s.ModifyItemsAsync(ct)` / `s.DeleteItemsAsync(...)` | implicit via `TryAdd` / options push / `TryRemove` | Direct |
| `s.SetMonitoringModeAsync(mode, ids, ct)` | push `MonitoredItemOptions { MonitoringMode = ... }` per item | Direct |
| `s.ResolveItemNodeIdsAsync(ct)` | n/a (V2 uses `StartNodeId` directly; relative-path resolution is caller-side) | **Deliberately not ported** (V2 caller resolves `RelativePath` to `NodeId` ahead of time via `Browse`/`TranslateBrowsePathsToNodeIds`; tests carry helpers when needed) |
| `s.MonitoredItems` / `s.MonitoredItemCount` | `s.MonitoredItems.Items` / `s.MonitoredItems.Count` | Direct |

### 1.5 Persistence (Save / Load)

| Classic | V2 | Status |
|---|---|---|
| `session.Save(Stream, IEnumerable<Subscription>)` (BinaryEncoder + `SubscriptionState.Encode`) | **`ISubscriptionManager.Save(Stream, IServiceMessageContext, ...)`** + fluent `ManagedSession.SaveSubscriptions(stream)` extension | **Added** |
| `session.Load(Stream, bool transferSubscriptions)` | **`ISubscriptionManager.LoadAsync(Stream, IServiceMessageContext, handlerFactory, transferSubscriptions, ct)`** + fluent `ManagedSession.LoadSubscriptionsAsync(stream, factory, transfer, ct)`. Recreate (`false`) and transfer (`true` via TransferSubscriptions; falls back to recreate on `BadSubscriptionIdInvalid` / `BadServiceUnsupported`) both work end-to-end. | **Added (recreate + transfer)** |
| `s.Snapshot(out SubscriptionState)` / `s.Restore(SubscriptionState)` | **`ISubscription.Snapshot()` → `SubscriptionStateSnapshot`** and **`ISubscriptionManager.RestoreAsync(handler, state, transfer, ct)`**; per-item `IMonitoredItem.Snapshot()` → `MonitoredItemStateSnapshot`. Fluent `ManagedSession.SnapshotSubscriptions()` / `RestoreSubscriptionsAsync(states, factory, transfer, ct)`. | **Added** |

### 1.6 Tuning / classic-specific knobs

| Classic | V2 | Status |
|---|---|---|
| `s.MaxMessageCount` | n/a | **Deliberately not ported** (V2 channel-based pipeline; unbounded queue with backpressure replaces this) |
| `s.MinLifetimeInterval` (property + `SubscriptionOptions.MinLifetimeInterval`) | already on V2 `SubscriptionOptions` | Direct |
| `s.DisableMonitoredItemCache` | n/a (V2 has no per-item cache to disable) | **Deliberately not ported** (V2 design choice — handler is the cache) |
| `s.SequentialPublishing` | always-on (V2 prioritized publish-ack channel guarantees per-subscription in-order delivery; documented on `ISubscriptionNotificationHandler`) | **Deliberately not ported** (always-sequential by design; covered by `SubscriptionV2Tests.SequentialPublishingV2Async`) |
| `s.RepublishAfterTransfer` | implicit via `MessageProcessor.TryRepublishAsync` (always-on gap fill) | Direct (no opt-out; tests assert republish counters move on transfer) |
| `s.PublishStatusChanged` / `s.StateChanged` events | unified into single `ISubscriptionNotificationHandler.OnSubscriptionStateChangedAsync(ISubscription, SubscriptionState, PublishState, CancellationToken)` callback | **Added** (single unified handler API; covered by `V2FollowUpCoverageTests.HandlerStateChangedFiresOnLifecycleV2Async`) |
| `s.OutstandingMessageWorkers` | n/a (V2 manager-wide `PublishWorkerCount`) | Direct (manager-level) |
| `s.Id` / `s.TransferId` | **`ISubscription.ServerId` (uint)** | **Added** (no more reflection needed; covered by all V2 tests) |
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
| `item.AttributesModified` | n/a (V2 reconciles on options change) | **Deliberately not ported** (V2 reconciliation is driven by `OptionsMonitor` change tokens; there is no "modified" flag to query) |
| `item.Filter` round-trip | `IMonitoredItem.FilterResult` | Direct |
| `item.DequeueValues()` / `item.LastValue` | n/a — values flow through `ISubscriptionNotificationHandler.OnDataChangeNotificationAsync(...)` | Direct (handler-side) |
| `item.Notification += ...` (event) | per-item dispatch through `OnDataChangeNotificationAsync` with `DataValueChange.MonitoredItem` | Direct |
| `item.GetEventTypeAsync` / `GetFieldValue` / `GetEventTime` / `GetFieldName` | n/a on V2 `IMonitoredItem` | **Deliberately not ported** (event-field helpers are caller-side; tests carry them when needed — see `MonitoredItemConditionRefreshLiveV2Tests.RefreshEventHandler` for the in-test pattern) |
| `item.TriggeringItemId` / `item.TriggeredItems` | V2 `IMonitoredItem.TriggeringItem` (lazy lookup via context) | **Added** (reverse "items I trigger" is on-demand via the context — no eagerly-maintained list) |

## 3. `Session` engine wiring

| Classic surface | V2 surface | Status |
|---|---|---|
| `Session.SubscriptionEngineFactory` (default `ClassicSubscriptionEngineFactory.Instance`) | default flipped to `DefaultSubscriptionEngineFactory.Instance` | **Added** |
| `ClientFixture.SubscriptionEngineFactory` opt-back property | `ClientTestFramework.ClientFixtureSubscriptionEngineFactory` | **Added** |
| `Session.AddSubscription(Subscription)` (classic-typed) | unchanged — classic subscriptions still added via this API on classic-engine sessions | Direct |
| `ManagedSession.SubscriptionManager` (V2) | unchanged | Direct |

## 4. Test fixtures and helpers

| Classic helper | V2 helper | Status |
|---|---|---|
| `TestableSubscription : Subscription` | n/a — V2 subscriptions are sealed instances created by the manager | Direct (test convention: subclass the handler instead) |
| `TestableMonitoredItem : MonitoredItem` | n/a | Direct |
| `ClientTestFramework.CreateSubscriptionsAsync(...)` | `CreateV2SubscriptionsAsync(...)` | **Added** |
| `ClientTestFramework.CreateMonitoredItemTestSet(...)` | `CreateV2MonitoredItemTestSet(...)` | **Added** |
| inline `RecordingHandler` in `ManagedSessionSubscriptionManagerIntegrationTests.cs` | `RecordingSubscriptionHandler` in `Opc.Ua.Client.TestFramework` | **Added** |

## 5. Final coverage summary

After this PR, **zero** rows above carry a `Deferred` status. Every classic surface
is either:

* **Direct** / **Added** — the V2 engine has the equivalent surface, and the V2
  test ports exercise it.
* **Deliberately not ported** — a classic surface that V2 replaces by design
  (handler-centric, channel-based pipeline, snapshot/restore, OptionsMonitor-driven
  reconciliation, etc.). The matrix above carries the rationale for each.

## 6. Bridge wiring — open TODO before classic engine removal

The V2 parity work above closes the **V2-native API surface**. There is a
separate open item required before the classic engine can be removed: wire the
`SubscriptionBridge` so existing consumers calling classic
`Session.AddSubscription(Subscription)` continue to work when the session's
engine is the V2 `DefaultSubscriptionEngine`.

**Status today:** documented gap, partial scaffolding in place.

* `Libraries/Opc.Ua.Client/Subscription/SubscriptionBridge.cs` — `SubscriptionBridge`
  and `ISubscriptionMessageSink` are now `public` (were `internal sealed`).
* `Libraries/Opc.Ua.Client/Subscription/Classic/Subscription.cs:45` — the classic
  `Subscription` now implements `ISubscriptionMessageSink` (its existing
  `SaveMessageInCache(ArrayOf<uint>, NotificationMessage)` signature matches
  exactly; the interface just makes the contract explicit).
* `Libraries/Opc.Ua.Client/Session/Session.cs:3126` — `AddSubscription(Subscription)`
  now carries an XML-doc warning describing the gap.
* `Tests/Opc.Ua.Subscriptions.Tests/ClassicOnV2EngineBridgeGapTests.cs` —
  `[Explicit]` test fixture that **reproduces the gap**:
  `ClassicSubscriptionOnV2EngineReceivesNoNotificationsAsync` runs classic
  `Subscription` + classic `MonitoredItem` on the V2 engine and asserts
  `Inconclusive` (no notifications). Once the wiring lands, flip `[Explicit]`
  off and rewrite the `Assert.Inconclusive` into an `Assert.That(notificationCount, Is.GreaterThan(0))`.

**Remaining wiring (TODO):**

1. **Expose an external-subscription registration hook on
   `ISubscriptionManager`** so non-V2 owners can plug into the publish dispatch:

   ```csharp
   bool TryRegisterExternalSubscription(uint subscriptionId, ISubscriptionMessageSink sink);
   bool TryUnregisterExternalSubscription(uint subscriptionId);
   ```

   In the V2 publish loop (`SubscriptionManager.cs:937`), when
   `GetById(subscriptionId)` returns null, consult the external registry; if
   found, call `sink.SaveMessageInCache(availableSequenceNumbers, notificationMessage)`
   and increment `m_goodPublishRequestCount` instead of issuing
   `DeleteSubscriptionsAsync`. Also extend acknowledgement handling so the
   V2 manager keeps acking on behalf of external subscriptions.

2. **Wire the bridge in `DefaultSubscriptionEngine`** by subscribing to
   `Session.SubscriptionsChanged` and, for each classic `Subscription` whose
   `Id` is non-zero, calling `m_manager.TryRegisterExternalSubscription(sub.Id, sub)`.
   Unregister on remove. Forward `availableSequenceNumbers` through the
   bridge (the V2 `MessageProcessor` already tracks it as
   `AvailableInRetransmissionQueue` at `MessageProcessor.cs:118`).

3. **Implement `OnSubscriptionStateChangedAsync` on the bridge** to translate
   V2 `PublishState` (Republish/Recovered/Transferred) and `SubscriptionState`
   into classic `PublishStatusChanged` / `StateChanged` invocations. Today
   the bridge no-ops this callback; many state events still come for free via
   classic `SaveMessageInCache` (which fires `Recovered` when called after
   `PublishingStopped`).

4. **Save/Load format migration** — classic `Session.Save` produces a
   `SubscriptionState`-based binary blob; V2 `SubscriptionManager.LoadAsync`
   reads a different encoding via `SubscriptionManagerSerializer`. Consumers
   that persist durable state across restarts need either auto-format
   detection in V2 `LoadAsync` or a one-way
   `SubscriptionMigration.UpgradeClassicStream(...)` helper documented in
   `Docs/MigrationGuide.md`.

5. **Per-item legacy surface** — `IMonitoredItem.Notification` event,
   `LastValue` / `DequeueValues()`, and event-field helpers
   (`GetEventTypeAsync`, `GetFieldValue`, `GetEventTime`, `GetFieldName`)
   are not on the V2 surface. Either restore them as opt-in cache + event,
   or document the migration recipe in `Docs/MigrationGuide.md` with a
   handler-side example.

### V2 surfaces added in this round (final list)

* `ISubscription.ServerId` — server-assigned subscription identifier exposed
  publicly. Tests no longer need reflection on the internal `Subscription` type.
* `ISubscription.SetSubscriptionDurableAsync(uint lifetimeInHours, CancellationToken ct = default)`
  — wraps `Server.SetSubscriptionDurable` method; returns revised lifetime hours.
* `SubscriptionOptions.SendInitialValuesOnTransfer` (bool, default `false`) — read
  by `SubscriptionManager.RestoreTransferAsync` when calling
  `TransferSubscriptionsAsync`.
* `ISubscriptionNotificationHandler.OnSubscriptionStateChangedAsync(
  ISubscription, SubscriptionState, PublishState, CancellationToken)` — single
  unified callback that surfaces lifecycle (Opened / Created / Modified / Deleted)
  and publish-state (Republish / Recovered / Transferred) transitions. Handlers
  maintain derived state by responding to this callback — there is no
  `PublishingStopped` property on `ISubscription`.

### V2 tests added in this round

* `Tests/Opc.Ua.Subscriptions.Durable.Tests/SubscriptionDurableV2Tests.cs` — 5
  V2 ports of the classic `DurableSubscriptionTest.cs` patterns.
* `Tests/Opc.Ua.Subscriptions.Tests/V2FollowUpCoverageTests.cs` — handler
  state-change callback, fluent stream-based `LoadSubscriptionsAsync`,
  `SendInitialValuesOnTransfer`, snapshot edge cases (empty / DataChangeFilter
  round-trip / concurrent mutation).
* `Tests/Opc.Ua.Subscriptions.Tests/SubscriptionFailoverV2Tests.cs` — channel
  break + reconnect with and without `WithTransferSubscriptionsOnRecreate`.
* `Tests/Opc.Ua.Subscriptions.Tests/MonitoredItemConditionRefreshLiveV2Tests.cs`
  — live `ConditionRefresh` with reference server event source; verifies
  RefreshStart / RefreshEnd events flow through the handler.
* `Tests/Opc.Ua.Subscriptions.Tests/SubscriptionV2Tests.cs` —
  `SequentialPublishingV2Async` promoted from Inconclusive to Passing.
