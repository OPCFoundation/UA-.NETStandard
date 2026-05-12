# Detailed Design: Subscription V2 GC reduction via `IAllocator`

Branch: `sgen-server-fluent`
Foundation merged: PR #3730 (V2 subscription engine, `bd4543e96`)

## 1. Overview

The V2 subscription engine takes tight control over the publish / decode / dispatch loop. A POC showed pooling `DataValue`, `MonitoredItemNotification`, and the dispatch arrays significantly reduces GC pressure and lifts the achievable session count per host.

This design introduces **`IAllocator`** as a small abstraction on the decoder layer. The decoder rents reference-type instances and backing arrays from the allocator instead of `new`-ing them. A passthrough implementation preserves today's behavior. A pooling implementation rents from per-type pools and supports controlled return.

Caller-side retention (handlers that need to hold a `DataValue` past the notification call) is expressed via opt-in **`DataValueLease`** / **`EventLease`** handles carrying a ref count on the underlying pooled object.

`ISubscriptionNotificationHandler` is V2-only and entirely new — no back-compat to maintain. Signatures change directly to use `ArrayOf<T>` instead of `ReadOnlyMemory<T>` / `IReadOnlyList<T>`, matching the rest of the project.

## 2. Design rationale

| Question | Answer |
|---|---|
| Where do we hook pooling? | At the **decoder** layer. The decoder is the single place where `new DataValue()` / `new MonitoredItemNotification()` / `new T[N]` happens. One hook covers all poolable types. |
| Why not a custom V2 decode path? | Source-gen already produces decoders for every IEncodeable. Routing through an allocator means: enable pooling for a publish → all decode-side allocations become rentals automatically. |
| Why not change `IDecoder`? | `IDecoder` now carries the `Allocator` property directly. All decoder implementations (`BinaryDecoder`, `JsonDecoder`, `XmlDecoder`) get it; the V2 publish path uses `BinaryDecoder` exclusively. The interface change is intentional — the allocator is a first-class part of the decoder contract. |
| Why `ArrayOf<T>` in the V2 handler interface? | The rest of the OPC UA service-set surface uses `ArrayOf<T>` (e.g. `NotificationMessage.StringTable`, `DataChangeNotification.MonitoredItems`). Using it in the handler too removes the `ToArray()` copies and keeps the V2 surface consistent. |
| Why no `IPooled*` sibling interface? | `ISubscriptionNotificationHandler` is brand new on this branch; there is no installed user base. The single interface gets the lease-aware signature. Passthrough mode supplies stub leases; user code is identical in both modes. |
| Why per-session pool, not per-subscription? | Decode runs in `PublishWorker` (per-session). At decode time we don't yet know which subscription owns the response. A per-session pool is sized by `Σ subscription.monitoredItems.Count`. |
| How does ownership transfer decode → dispatch? | `AllocationScope` (carries the rental ledger) travels with `IncomingMessage` through the priority channel; dispatch disposes it after the handler returns. |

## 3. New public API

### 3.1 `IAllocator`

```csharp
// Stack/Opc.Ua.Types/Encoders/IAllocator.cs
namespace Opc.Ua
{
    /// <summary>
    /// Allocator used by decoders to obtain reference-type instances and
    /// reference-type backing arrays. Provides a single hook point that
    /// pooling implementations can intercept to recycle decoded objects
    /// instead of allocating fresh ones per publish.
    /// </summary>
    public interface IAllocator
    {
        /// <summary>
        /// Rent a fresh instance of <typeparamref name="T"/>. The instance
        /// is owned by the allocator until passed back via
        /// <see cref="Return{T}"/>. Pooling implementations may return a
        /// recycled instance whose <see cref="IPoolableReset.Reset"/> has
        /// been called.
        /// </summary>
        T Rent<T>() where T : class, new();

        /// <summary>
        /// Rent a fresh <see cref="IEncodeable"/> instance. Equivalent in
        /// semantics to <see cref="Rent{T}"/> but constrained to encodeable
        /// types — kept separate so source generators can emit a focused
        /// call site for poolable types.
        /// </summary>
        T RentEncodeable<T>() where T : class, IEncodeable, new();

        /// <summary>
        /// Rent a backing array of at least <paramref name="minimumLength"/>
        /// elements. The returned array may be larger; callers must slice
        /// it to the exact length when exposing it through
        /// <see cref="ArrayOf{T}"/>. Element type may be reference or
        /// value type — backing storage is heap memory either way.
        /// </summary>
        T[] RentArray<T>(int minimumLength);

        /// <summary>
        /// Return a single instance to the allocator. Passthrough
        /// implementations may ignore the call.
        /// </summary>
        void Return<T>(T instance) where T : class;

        /// <summary>
        /// Return an array to the allocator. When
        /// <paramref name="clearArray"/> is true (default), references in
        /// the array are cleared before recycling to avoid leaking
        /// publish-graph references into the pool.
        /// </summary>
        void ReturnArray<T>(T[] array, bool clearArray = true);
    }
}
```

### 3.2 `PassthroughAllocator`

```csharp
// Stack/Opc.Ua.Types/Encoders/PassthroughAllocator.cs
namespace Opc.Ua
{
    /// <summary>
    /// Default <see cref="IAllocator"/> that allocates a fresh instance on
    /// every <see cref="Rent{T}"/> and ignores returns. Behaviour matches
    /// pre-allocator code byte-for-byte.
    /// </summary>
    public sealed class PassthroughAllocator : IAllocator
    {
        public static IAllocator Instance { get; } = new PassthroughAllocator();
        private PassthroughAllocator() { }

        public T Rent<T>() where T : class, new() => new T();
        public T RentEncodeable<T>() where T : class, IEncodeable, new() => new T();
        public T[] RentArray<T>(int minimumLength)
            => minimumLength == 0 ? [] : new T[minimumLength];
        public void Return<T>(T instance) where T : class { }
        public void ReturnArray<T>(T[] array, bool clearArray = true) { }
    }
}
```

### 3.3 `IPoolableReset`

```csharp
// Stack/Opc.Ua.Types/Encoders/IPoolableReset.cs
namespace Opc.Ua
{
    /// <summary>
    /// Optional reset hook implemented by types that wish to be pooled.
    /// The pooling allocator calls <see cref="Reset"/> before returning an
    /// instance to the pool so it is ready for the next rent.
    /// </summary>
    public interface IPoolableReset
    {
        void Reset();
    }
}
```

Partial implementations on the poolable types (Phase B):

```csharp
// Stack/Opc.Ua.Types/BuiltIn/DataValue.Poolable.cs
public partial class DataValue : IPoolableReset
{
    void IPoolableReset.Reset()
    {
        m_value = Variant.Null;
        StatusCode = StatusCodes.Good;
        SourceTimestamp = DateTimeUtc.MinValue;
        ServerTimestamp = DateTimeUtc.MinValue;
        SourcePicoseconds = 0;
        ServerPicoseconds = 0;
    }
}

// Stack/Opc.Ua/Types/MonitoredItemNotification.Poolable.cs
public partial class MonitoredItemNotification : IPoolableReset
{
    void IPoolableReset.Reset()
    {
        ClientHandle = 0;
        Value = null!;
        Message = null!;
        DiagnosticInfo = null!;
    }
}
```

Source generator can be taught (Phase D) to auto-emit `IPoolableReset` on types marked `[Poolable]` in the design XML.

### 3.4 V2 notification surface

`DataValueChange` and `EventNotification` are tightened to expose a `Retain()` returning a lease. In passthrough mode the lease's `Dispose` is a no-op; in pooled mode it carries a ref-count on the underlying pooled object.

```csharp
// Libraries/Opc.Ua.Client/Subscription/ISubscriptionNotificationHandler.cs

/// <summary>
/// Data value change observed on a monitored item during a V2 publish
/// dispatch. The contained <see cref="Value"/> is valid for the duration
/// of the handler call. Callers that need to retain the value beyond the
/// call invoke <see cref="Retain"/> to obtain a <see cref="DataValueLease"/>
/// they must dispose.
/// </summary>
public readonly struct DataValueChange
{
    public IMonitoredItem? MonitoredItem { get; }
    public DataValue Value { get; }
    public DiagnosticInfo? DiagnosticInfo { get; }

    // Null in passthrough mode.
    internal IPooledHandle? Handle { get; }

    /// <summary>Retain the value past the handler. Caller owns lease lifetime.</summary>
    public DataValueLease Retain();
}

/// <summary>Event notification mirror of <see cref="DataValueChange"/>.</summary>
public readonly struct EventNotification
{
    public IMonitoredItem? MonitoredItem { get; }
    public ArrayOf<Variant> Fields { get; }
    internal IPooledHandle? Handle { get; }
    public EventLease Retain();
}

/// <summary>
/// Retained handle on a pooled DataValue. Owns one ref count; dispose to
/// release. In passthrough mode this is a zero-cost stub.
/// </summary>
public readonly struct DataValueLease : IDisposable
{
    public IMonitoredItem? MonitoredItem { get; }
    public DataValue Value { get; }
    public DiagnosticInfo? DiagnosticInfo { get; }
    internal IPooledHandle? Handle { get; }
    public void Dispose();
}

public readonly struct EventLease : IDisposable
{
    public IMonitoredItem? MonitoredItem { get; }
    public ArrayOf<Variant> Fields { get; }
    internal IPooledHandle? Handle { get; }
    public void Dispose();
}
```

### 3.5 `ISubscriptionNotificationHandler` (refreshed)

```csharp
public interface ISubscriptionNotificationHandler
{
    ValueTask OnDataChangeNotificationAsync(
        ISubscription subscription,
        uint sequenceNumber,
        DateTime publishTime,
        ArrayOf<DataValueChange> notification,
        PublishState publishStateMask,
        ArrayOf<string> stringTable);

    ValueTask OnEventDataNotificationAsync(
        ISubscription subscription,
        uint sequenceNumber,
        DateTime publishTime,
        ArrayOf<EventNotification> notification,
        PublishState publishStateMask,
        ArrayOf<string> stringTable);

    ValueTask OnKeepAliveNotificationAsync(
        ISubscription subscription,
        uint sequenceNumber,
        DateTime publishTime,
        PublishState publishStateMask);
}
```

Changes from the current interface:

- `ReadOnlyMemory<DataValueChange>` → `ArrayOf<DataValueChange>`.
- `ReadOnlyMemory<EventNotification>` → `ArrayOf<EventNotification>`.
- `IReadOnlyList<string> stringTable` → `ArrayOf<string> stringTable` (zero-copy passthrough from `NotificationMessage.StringTable`).

`DataValueChange.Retain()` returns `DataValueLease`. Handlers that don't need to retain ignore the lease and let scope-dispose clean up.

### 3.6 `SubscriptionManagerOptions` / builder

```csharp
public partial class SubscriptionManagerOptions
{
    /// <summary>
    /// Enable pooled notification dispatch. The publish decoder routes
    /// allocations through a per-session <see cref="IAllocator"/> backed by
    /// per-type pools. Incompatible with the V1 <c>SubscriptionBridge</c>;
    /// setting both throws at builder time. Default: <c>false</c>.
    /// </summary>
    public bool PoolNotifications { get; init; } = false;

    /// <summary>
    /// Maximum pooled instances retained per type. Bounded to prevent
    /// pathological retention under bursty load. Default: 1024.
    /// </summary>
    public int MaxPooledPerType { get; init; } = 1024;
}

public partial class ManagedSessionBuilder
{
    public ManagedSessionBuilder WithPoolNotifications(bool enabled = true);
}
```

## 4. New internal API

### 4.0 How the `BinaryDecoder` gets the allocator (plumbing)

The V2 publish worker does **not** construct the `BinaryDecoder` directly. The actual decode happens deep in the channel layer:

```
SubscriptionManager.PublishWorker.PublishWorkerAsync           (Libraries/Opc.Ua.Client)
  → m_session.PublishAsync(...)                                 (Session.cs)
  → SessionClient.PublishAsync (source-gen)                     (marshals request)
  → channel layer sends UA-TCP request; awaits TCS
  → UaSCBinaryClientChannel I/O loop receives response chunks   (Stack/Opc.Ua.Core)
  → UaSCBinaryClientChannel.ParseResponse                       (UaSCBinaryClientChannel.cs:1234)
  → BinaryDecoder.DecodeMessage<IServiceResponse>(
        stream, Quotas.MessageContext)                          (BinaryDecoder.cs:194)
  → static factory does:
        using var decoder = new BinaryDecoder(stream, context);
        return decoder.DecodeMessage<T>();
  → channel resolves TCS with decoded PublishResponse
  → m_session.PublishAsync awaits TCS → returns response
  → worker hands NotificationMessage to subscription
```

By the time `m_session.PublishAsync` returns, the decode has already happened on a channel I/O continuation. The worker therefore cannot inject an allocator after the fact. Three viable plumbing strategies, ranked:

#### Strategy A (chosen) — allocator on `IServiceMessageContext`

`IServiceMessageContext` is the context object the channel passes into every `BinaryDecoder` via its constructor (`BinaryDecoder.cs:51-106`). Add an `IAllocator Allocator { get; }` member; the decoder reads it from `Context.Allocator` and stores it in its own `Allocator` property.

- The session owns one `SubscriptionPool` and one `IAllocator` over it.
- Session sets the allocator on its `Quotas.MessageContext` at startup (`SubscriptionManagerOptions.PoolNotifications == true`).
- Channel uses this `MessageContext` for **all** decodes on this session → all `BinaryDecoder`s automatically see the pool.
- **No per-call override needed.** Concurrent decodes are safe because rent operations are pool-level and lock-free.

This makes the "one pool per session" choice fundamental: the channel's decoder always uses the same pool for every response on that session. Cross-request contamination isn't a problem because:

1. Only types marked `[Poolable]` (Phase D) route through `IAllocator.Rent/RentArray`; everything else continues to `new`. So unmonitored small allocs aren't pooled at all.
2. The V2 `Subscription` is the **single owner of returns**: after handler dispatch, it walks the response and returns every pooled object back to the pool. No `AllocationScope` "ledger" inside the decoder — the dispatcher reconstructs the ownership graph from the response shape.

#### Strategy B (alternative if A proves too coupled) — per-request allocator at the channel

Augment the channel's outstanding-request table with an optional `IAllocator?`. `Session.PublishAsync` registers the allocator when sending; channel response-decode looks it up before constructing the BinaryDecoder. More surgery; preserves per-publish allocator scoping. Reject unless we hit a need.

#### Strategy C (alternative) — deferred ExtensionObject decode

Channel returns `PublishResponse` with `NotificationMessage.NotificationData` as raw bytes inside `ExtensionObject`s (i.e., make `ExtensionObject.Body` lazy). Worker re-decodes the inner content with its own BinaryDecoder + allocator. Cleanest separation but requires a big change to `ExtensionObject` decode semantics — defer.

#### Strategy A: implications

- `IServiceMessageContext.Allocator` defaults to `PassthroughAllocator.Instance`.
- `BinaryDecoder` ctor reads `Allocator` from `Context.Allocator` once at construction (settable via `decoder.Allocator = X;` if needed for test injection, since `IDecoder.Allocator { get; set; }`).
- `SubscriptionManager` constructs a `SubscriptionPool` and an `IAllocator` over it when `PoolNotifications == true`. Stores it on `Session.MessageContext` (mutable: `Session.ConfigureAllocator(IAllocator)`).
- The V2 `Subscription` does **not** carry an `AllocationScope`. Instead, after the handler dispatch completes, it walks the just-handled `DataChangeNotification` / `EventNotificationList` and explicitly calls `Allocator.Return*` for each leaf object (and the backing arrays). This walking is straightforward because the Subscription already iterates the response (`MonitoredItemManager.CreateNotification`).
- For retained leases: when the handler calls `change.Retain()`, the lease takes a CAS-incremented ref count on a per-instance handle. The dispatcher's "return after handler" call decrements; retained leases keep refcount ≥ 1 until disposed.

### 4.1 `SubscriptionPool`

```csharp
// Libraries/Opc.Ua.Client/Subscription/Pool/SubscriptionPool.cs
namespace Opc.Ua.Client.Subscriptions.Pool
{
    /// <summary>
    /// Per-session pool: thread-safe per-type sub-pools backed by bounded
    /// <see cref="ConcurrentQueue{T}"/>. Bounded so a pathological publish
    /// burst does not pin unbounded memory.
    /// </summary>
    internal sealed class SubscriptionPool
    {
        private readonly ConcurrentDictionary<Type, ConcurrentQueue<object>> m_singles;
        private readonly int m_maxPerType;

        public SubscriptionPool(int maxPerType = 1024) { ... }

        internal T Rent<T>() where T : class, new();
        internal void Return<T>(T instance) where T : class;
        internal T[] RentArray<T>(int minimumLength)
            => ArrayPool<T>.Shared.Rent(minimumLength);
        internal void ReturnArray<T>(T[] array, bool clearArray)
            => ArrayPool<T>.Shared.Return(array, clearArray);
    }
}
```

### 4.2 `IAllocator` for the V2 publish path (`PoolingAllocator`)

```csharp
// Libraries/Opc.Ua.Client/Subscription/Pool/PoolingAllocator.cs
namespace Opc.Ua.Client.Subscriptions.Pool
{
    /// <summary>
    /// IAllocator over a <see cref="SubscriptionPool"/>. Stateless apart
    /// from the pool reference — safe to share across decoders and
    /// concurrent publish workers.
    /// </summary>
    internal sealed class PoolingAllocator : IAllocator
    {
        private readonly SubscriptionPool m_pool;
        public PoolingAllocator(SubscriptionPool pool) => m_pool = pool;

        public T Rent<T>() where T : class, new() => m_pool.Rent<T>();
        public T RentEncodeable<T>() where T : class, IEncodeable, new()
            => m_pool.Rent<T>();
        public T[] RentArray<T>(int minimumLength)
            => m_pool.RentArray<T>(minimumLength);

        public void Return<T>(T instance) where T : class
        {
            if (instance is IPoolableReset r) r.Reset();
            m_pool.Return(instance);
        }

        public void ReturnArray<T>(T[] array, bool clearArray = true)
            => m_pool.ReturnArray<T>(array, clearArray);
    }
}
```

### 4.3 Ownership: Subscription walks the response after dispatch

There is no `AllocationScope` carried through the channel. Instead, the V2 `Subscription` owns the return graph. After the handler dispatch completes (with or without retained leases), the subscription walks the just-dispatched notification and returns every pooled object to the pool.

For data-change notifications:

```csharp
// Subscription.cs (Phase B)
protected override async ValueTask OnDataChangeNotificationAsync(
    uint sequenceNumber, DateTime publishTime,
    DataChangeNotification notification,
    PublishState publishStateMask, ArrayOf<string> stringTable)
{
    IAllocator allocator = Session.Allocator;  // from MessageContext
    ArrayOf<DataValueChange> changes =
        m_monitoredItems.CreateNotification(notification, allocator);
    DataValueChange[]? backing = TryGetBackingArray(changes);
    try
    {
        await m_handler.OnDataChangeNotificationAsync(
            this, sequenceNumber, publishTime,
            changes, publishStateMask, stringTable).ConfigureAwait(false);
    }
    finally
    {
        // Return the response objects (only those marked [Poolable];
        // others are normal GC-managed allocations).
        ReturnResponse(notification, allocator);
        if (backing is not null)
        {
            allocator.ReturnArray(backing, clearArray: true);
        }
    }
}

private static void ReturnResponse(
    DataChangeNotification notification, IAllocator allocator)
{
    ReadOnlySpan<MonitoredItemNotification> items =
        notification.MonitoredItems.Span;
    for (int i = 0; i < items.Length; i++)
    {
        MonitoredItemNotification item = items[i];
        // Each retained DataValueLease holds its own ref count; Return
        // here just decrements the dispatcher's ref. The pool reclaims
        // when refcount hits zero.
        if (item.Value is not null)
        {
            allocator.Return(item.Value);
        }
        allocator.Return(item);
    }
    // Return the backing array of notification.MonitoredItems too
    if (MemoryMarshal.TryGetArray(notification.MonitoredItems.Memory,
            out ArraySegment<MonitoredItemNotification> seg)
        && seg.Array is not null)
    {
        allocator.ReturnArray(seg.Array, clearArray: true);
    }
}
```

For events the walk is symmetric over `EventNotificationList.Events` and `EventFieldList`.

For ref-counted retention: `allocator.Return(instance)` decrements the per-instance handle's refcount; the pool reclaim happens only when refcount → 0. Passthrough's `Return` is a no-op so handlers in non-pooled mode behave identically.

### 4.4 Per-instance handle tracking (for retention)

`PoolingAllocator.Rent<T>()` could return a fresh `new T()` or a recycled one. To support `DataValueChange.Retain()`, each rented `DataValue` / `MonitoredItemNotification` must be associated with a refcount handle that survives across the rent.

Implementation: the pool stores objects keyed by instance and lazily attaches a `PooledHandle<T>` the first time `Rent` returns the instance. The handle's refcount starts at 1 each rent (cleared between cycles). The `DataValueChange` struct carries the handle reference; `Retain()` increments the count; `Return` decrements; pool reclaim happens on refcount → 0.

Alternative, simpler: `PoolingAllocator` maintains a `ConditionalWeakTable<object, PooledHandle>` so we don't pollute the pooled instance fields. But that adds lookup overhead on every Rent. Defer the choice to Phase C — Phase B's `Return` semantics work without any handle if no caller retains.

```csharp
internal sealed class PooledHandle<T> where T : class
{
    private int m_refCount;
    public T Instance { get; }
    public PooledHandle(T instance) { Instance = instance; }

    public void Activate() => Volatile.Write(ref m_refCount, 1);

    public bool TryRetain()
    {
        int observed;
        do
        {
            observed = Volatile.Read(ref m_refCount);
            if (observed <= 0) return false;  // refuse retain from zero
        } while (Interlocked.CompareExchange(
            ref m_refCount, observed + 1, observed) != observed);
        return true;
    }

    public bool Release()
    {
        int newCount = Interlocked.Decrement(ref m_refCount);
        if (newCount < 0) throw new InvalidOperationException("Double release.");
        return newCount == 0;
    }
}
```

`PoolingAllocator` keeps a per-type pool of handles plus instances; `Rent<T>()` pops a handle, returns its instance, calls `Activate()` (refcount → 1).

## 5. End-to-end flow (pooled mode)

```
0. Session startup:
   • Builder sees PoolNotifications == true.
   • Session constructs a SubscriptionPool sized by MaxPooledPerType.
   • Session wraps the pool in a PoolingAllocator.
   • Session.MessageContext.Allocator is set to that PoolingAllocator.

1. SubscriptionManager.PublishWorker calls m_session.PublishAsync(...).
   • Worker has no allocator handle — irrelevant; the channel will use
     Session.MessageContext for decode.

2. Channel I/O loop receives the response chunks, calls
   UaSCBinaryClientChannel.ParseResponse:
     BinaryDecoder.DecodeMessage<IServiceResponse>(stream, MessageContext)
   • BinaryDecoder ctor reads MessageContext.Allocator → PoolingAllocator.
   • All marked-[Poolable] type allocations route through it:
       ReadDataValue              → Allocator.Rent<DataValue>()
       ReadEncodeableArray<MIN>   → Allocator.RentArray<MIN>(N)
       ReadEncodeable<MIN>        → Allocator.RentEncodeable<MIN>()
     Same for EventFieldList, DataChangeNotification, EventNotificationList.
   • Unmarked types continue to `new` (no pool, normal GC).
   • ArrayOf<T> wraps the rented backing array sliced to exact length.

3. Channel resolves the TCS; m_session.PublishAsync returns PublishResponse.

4. Worker hands NotificationMessage to subscription via
   subscription.OnPublishReceivedAsync(message, ...). MessageProcessor
   enqueues it onto the per-sub priority channel.

5. Subscription's single-reader ProcessMessageAsync loop pops the message:
   • Subscription.OnDataChangeNotificationAsync calls
     MonitoredItemManager.CreateNotification(datachange, allocator).
   • CreateNotification rents a DataValueChange[] from the allocator and
     fills it; returns ArrayOf<DataValueChange>.
   • In pooled mode each DataValueChange carries a PooledHandle reference
     for the underlying DataValue (refcount = 1 from the original rent).

6. Dispatch:
   • await m_handler.OnDataChangeNotificationAsync(
         this, seqNo, publishTime,
         changes, publishStateMask, message.StringTable);
   • Handler iterates, optionally retains specific items via change.Retain()
     which CAS-bumps refcount and returns a DataValueLease the caller owns.

7. After the awaited handler returns, the finally block walks the response:
   • For each MonitoredItemNotification in notification.MonitoredItems:
       allocator.Return(item.Value)   ← decrements DataValue refcount
       allocator.Return(item)         ← returns MonitoredItemNotification
     If refcount > 0 (caller retained), pool reclaim is deferred until
     the retained lease disposes.
   • allocator.ReturnArray(monitoredItems backing array, clearArray: true).
   • allocator.ReturnArray(DataValueChange[] dispatch buffer, clearArray: true).

8. Retained leases:
   • lease.Dispose() decrements its handle's refcount; on zero → Reset() +
     pool reclaim.
```

The same shape applies for events (`EventNotificationList` / `EventFieldList`).

## 6. Lifetime model

| Stage | Ref count of pooled `DataValue` |
|---|---|
| `Rent<DataValue>()` returns (during channel decode) | 1 |
| Embedded in `MonitoredItemNotification.Value` and surfaced via `DataValueChange` | 1 |
| Handler calls `change.Retain()` | 2 |
| Handler returns | 2 |
| Subscription's `finally` walk: `allocator.Return(item.Value)` | 1 |
| Caller `retainedLease.Dispose()` | 0 → reset + return to pool |

Invariants:

- `TryRetain()` refuses to increment from zero (CAS loop).
- `Release()` throws on negative refcount.
- Passthrough mode: `Return*` are no-ops; lease.Dispose() is harmless.

## 7. Thread-safety

- `BinaryDecoder` is **single-threaded per instance**; the decoder is local to one channel response decode (channel I/O continuation).
- `SubscriptionPool` is **multi-reader / multi-writer**; `ConcurrentDictionary` + `ConcurrentQueue` (lock-free fast paths). `ArrayPool<T>.Shared` is thread-safe.
- Multiple publish workers on a session decode concurrently — each gets fresh rentals from the shared pool. No coordination needed.
- The V2 `Subscription`'s `ProcessMessageAsync` loop is single-threaded per subscription, so the per-publish "return walk" runs on one thread.
- `PooledHandle.TryRetain` / `Release` use `Interlocked.CompareExchange` — thread-safe across handler retention and dispatcher release.

## 8. Wire-in points (specific edits)

### `IDecoder` (Phase A)

```csharp
// Stack/Opc.Ua.Types/Encoders/IDecoder.cs
public interface IDecoder : IDisposable
{
    EncodingType EncodingType { get; }
    IServiceMessageContext Context { get; }

    /// <summary>
    /// Allocator used to obtain reference-type instances and backing
    /// arrays during decoding. Defaults to
    /// <see cref="PassthroughAllocator.Instance"/> when not explicitly
    /// set; assign a pooling allocator for hot paths that benefit from
    /// instance reuse (e.g. the V2 subscription publish loop).
    /// </summary>
    IAllocator Allocator { get; set; }    // NEW

    // ... existing members unchanged
}
```

All three implementations (`BinaryDecoder`, `JsonDecoder`, `XmlDecoder`) implement the property. `BinaryDecoder` consumes it on the four hot sites listed below; `JsonDecoder` / `XmlDecoder` may opt in later but for Phase A they just store and return the value (passthrough by default).

### `BinaryDecoder` (Phase A)

```csharp
// Stack/Opc.Ua.Types/Encoders/BinaryDecoder.cs

public IAllocator Allocator { get; set; } = PassthroughAllocator.Instance;

// :620 ReadDataValue
- var value = new DataValue();
+ var value = Allocator.Rent<DataValue>();

// :923 ReadEncodeable<T>()
- var encodeable = new T();
+ var encodeable = Allocator.RentEncodeable<T>();

// :1467 ReadEncodeableArray<T>(string, ExpandedNodeId)
- var values = new T[length];
+ var values = Allocator.RentArray<T>(length);
...
- return values;
+ return new ArrayOf<T>(values.AsMemory(0, length));

// :1502 ReadEncodeableArrayAsExtensionObjects<T>
- var values = new T[length];
+ var values = Allocator.RentArray<T>(length);
...
- return values;
+ return new ArrayOf<T>(values.AsMemory(0, length));

// :1523 ReadEncodeableArray<T>()
- var values = new T[length];
+ var values = Allocator.RentArray<T>(length);
...
- return values;
+ return new ArrayOf<T>(values.AsMemory(0, length));
```

### `IServiceMessageContext` (Phase A)

```csharp
// Stack/Opc.Ua.Types/Utils/IServiceMessageContext.cs
public interface IServiceMessageContext
{
    // ... existing members ...

    /// <summary>
    /// Allocator used by decoders constructed with this context. Default
    /// is <see cref="PassthroughAllocator.Instance"/>. Sessions that want
    /// pooled decoding override this property on a session-scoped
    /// context.
    /// </summary>
    IAllocator Allocator { get; }   // NEW
}
```

`BinaryDecoder` reads `Allocator` from `Context.Allocator` in its constructor and exposes it via `IDecoder.Allocator { get; set; }` for test injection.

### `Session` (Phase B)

```csharp
// Libraries/Opc.Ua.Client/Session/Session.cs

internal void ConfigureAllocator(IAllocator allocator)
{
    // Replace the session-scoped MessageContext with one whose
    // Allocator returns the supplied allocator. Existing channels
    // pick up the new context for subsequent decodes.
    m_messageContext = new ServiceMessageContext(m_messageContext)
    {
        Allocator = allocator
    };
}
```

`SubscriptionManager` calls `Session.ConfigureAllocator(new PoolingAllocator(pool))` at startup when `PoolNotifications == true`.

### `MessageProcessor` (Phase B)

- `IncomingMessage` `StringTable` becomes `ArrayOf<string>` (no copy from `NotificationMessage.StringTable`).
- `OnPublishReceivedAsync` accepts `ArrayOf<string> stringTable`. No `AllocationScope?` parameter; the allocator lives on `Session.MessageContext` and is accessed via `Session.Allocator` in `Subscription.OnDataChangeNotificationAsync`.

### `Subscription` (Phase B)

After dispatch, walks the response and returns each pooled object — see section 4.3.

## 9. Test plan

### Phase A (allocator scaffolding)

- **Decoder regression**: `Stack/Opc.Ua.Types.Tests/Encoders/*` with default `PassthroughAllocator.Instance`. Byte-for-byte identical decode output.
- **Unit**: passthrough invariants — `Rent<T>() != Rent<T>()`, `Return*` no-op, `RentArray<T>(0)` empty.

### Phase B (signature refresh + pooling allocator)

- **Compile-break check**: existing V2 tests update to `ArrayOf<DataValueChange>` API.
- **Unit**: rent → use → return → re-rent yields same reference.
- **Unit**: `Reset()` invoked between rent cycles.
- **Unit**: bounded queue drops on overflow (no leak).
- **Stress**: K=100 subs × N=100 items × M=10,000 publishes; pool stays bounded; ledger fully drained; no `DataValue` allocations after warm-up.

### Phase C (lease + ref counting)

- **Unit**: ref count correctness (retain / release / resurrection / double-release).
- **Unit**: `TryRetain` refuses to bump from zero.
- **Unit**: concurrent retain/release — no use-after-return.
- **Property**: retained lease observes a stable snapshot until disposed.
- **Conformance**: existing `Opc.Ua.Client.Tests` pass with `PoolNotifications = false`.
- **Conformance**: integration subset passes with `PoolNotifications = true`.

### Phase D (source-gen)

- Generated decoders for poolable types route through allocator; diff vs. pre-D output shows only wired-through sites changed.
- Decode round-trip tests pass.

### Phase E (V1-bridge skip-rewrap)

- Bridge unit tests assert reference-equality of decoder-allocated arrays and V1-cache-stored arrays.
- V1 consumer integration: same notifications observed in V1 cache before/after.

### Phase G (benchmarks) — see section 11.

## 10. Migration / backward compatibility

| Surface | Change | Impact |
|---|---|---|
| `IServiceMessageContext` | adds `IAllocator Allocator { get; }` | implementers add a property; default `PassthroughAllocator.Instance` keeps observable behavior |
| `IDecoder` | adds `IAllocator Allocator { get; set; }` | small contract addition; default propagates from `Context.Allocator` |
| `BinaryDecoder` / `JsonDecoder` / `XmlDecoder` | implement `Allocator`; `BinaryDecoder` routes 4 hot sites through it | none for callers |
| `Session` | new internal `ConfigureAllocator(IAllocator)` swaps the session's MessageContext to one that exposes the allocator | invoked only when `PoolNotifications == true` |
| `MessageProcessor.OnPublishReceivedAsync` | `IReadOnlyList<string>` → `ArrayOf<string>` | internal contract; tests updated |
| `DataValue` / `MonitoredItemNotification` / `EventFieldList` | adds `IPoolableReset` partial impl | none — explicit interface impl |
| `ISubscriptionNotificationHandler` | signature refresh (`ArrayOf<T>` + `Retain()` exposed via change struct) | V2 callers update; no public users yet |
| `DataValueChange` / `EventNotification` | adds internal `Handle` + `Retain()` | additive |
| `DataValueLease` / `EventLease` | new public types | additive |
| `SubscriptionManagerOptions` | new `PoolNotifications` + `MaxPooledPerType` | none (default off) |
| `MonitoredItemManager.CreateNotification` | adds `IAllocator` param; returns `ArrayOf<T>` | internal-only |

No public V2 breaking change for non-V2 users. V2 users see signature improvements (`ArrayOf<T>`, lease-aware change struct).

## 11. Benchmark plan

New project `Tests/Opc.Ua.Client.Benchmarks` (BenchmarkDotNet).

| Workload | Configuration | Metrics |
|---|---|---|
| A | V2 dispatch only, K=1 sub × N ∈ {1, 10, 100, 1000} items | Gen0/1/2, allocated bytes/op, mean dispatch ns |
| B | full publish loop, K ∈ {10, 100} subs × M=10k publishes | sustained throughput, allocated bytes / publish |
| C | V1-bridge path, before/after Phase E | bridge per-publish allocations |
| D | Phase B pool warm-up + steady state | steady-state Gen0/Gen1 collections per minute |

**Acceptance targets** (measured against Phase A baseline):

- Phase B/D — after warm-up: per-publish `DataValue` + `MonitoredItemNotification` + dispatch-array allocations → 0; sustained Gen0/Gen1 collections per minute down ≥ 80%.
- Phase E — V1-bridge per-publish allocations down ≥ 50%.
- Phase B signature refresh — per-dispatch `string[]` allocations → 0 (StringTable no-copy).
- Phase A — zero behavioral / allocation delta vs. baseline (regression gate).

## 12. Risk register

| Risk | Mitigation |
|---|---|
| Pool unbounded growth | Bounded queues per type; configurable `MaxPooledPerType`. |
| Reset misses a field, leaks state across renters | `IPoolableReset.Reset()` covers every mutable field; tests verify no carry-over. |
| Handler stashes a `DataValue` without `Retain()` | Documented; in pooled mode the reference is invalid after the handler returns. Default mode unaffected. |
| Variant payload arrays accidentally pooled | Out of scope; payload arrays bypass the allocator (Variant is a struct). |
| Cross-subscription pool sharing | Per-session pool; per-subscription pool is a future enhancement with no design change. |
| V1 bridge + pooling combination | Builder throws at startup. |
| Third-party decoders break | `IDecoder` adds the `Allocator` property — implementers must add a `{ get; set; }` (one line). Default `PassthroughAllocator` keeps observable behavior identical. |
| Async lifetime: pool reclaim races with `Retain()` | Refcount handled via CAS on `PooledHandle`; Retain refuses-from-zero, Return decrements without reclaiming when refcount > 0. |
| Cross-publish reuse of a pooled DataValue | Refcount only drops to 0 after the dispatcher's `Return` AND every retained lease's `Dispose`. The pool reclaims only at zero, so concurrent retainers see a stable instance. |

## 13. Out of scope

- Pooling Variant payload arrays.
- Pooling `NotificationMessage` (lives in V1 cache long-term).
- Public `IDecoder` interface unchanged in shape beyond the added `Allocator` property.
- Migrating V1 cache to be retention-aware.
- Making `DataValue` / `MonitoredItemNotification` immutable or value types.
- Encode-side pooling.

## 14. Phased delivery order

1. **Phase A** — `IAllocator` + `PassthroughAllocator` + 4 `BinaryDecoder` wire-throughs. No behavioral change. Lands first.
2. **Phase G baseline** — BenchmarkDotNet harness; capture pre-change numbers.
3. **Phase B** — interface signature refresh to `ArrayOf<T>` + `SubscriptionPool` + `PoolingAllocator` (no scope object) + `IPoolableReset` impls + `MonitoredItemManager.CreateNotification(allocator)` + `Session.ConfigureAllocator` + `MessageContext.Allocator` plumbing + `PoolNotifications` opt-in + builder check. Drops the `StringTable.ToArray()` copy as a free byproduct. Subscription's `OnDataChangeNotificationAsync` walks the response in `finally` and returns each pooled object to the pool.
4. **Phase C** — `DataValueLease` / `EventLease` + retain/release semantics on `DataValueChange.Retain()` / `EventNotification.Retain()`.
5. **Phase D** — source-gen `[Poolable]` attribute + emit change for marked types.
6. **Phase E** — `SubscriptionBridge` skip-rewrap (passthrough only, independent of B/C/D).
7. **Phase G final** — re-bench against acceptance targets.

---

## ✅ Previous: PR #3730 (publishchannel) — merged as `bd4543e96`
