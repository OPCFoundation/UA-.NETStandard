# Time and Timer Abstraction (TimeProvider)

> **When to read this:** Read this when adopting `System.TimeProvider` across the stack (replacing direct `DateTime.UtcNow`, `Timer`, and similar timing primitives in custom NodeManagers, durable subscriptions, and reconnect policies).

**Not source-breaking.** The stack now uses
[`System.TimeProvider`](https://learn.microsoft.com/dotnet/api/system.timeprovider) as
its canonical clock and scheduler so that timeouts, intervals, keep-alive loops,
reconnect back-off, publishing pacing, certificate-lifetime checks, and similar
duration-sensitive code paths are mockable in tests and immune to wall-clock changes.

`HiResClock` is still in place but **every public member is now marked
`[Obsolete]`**. The class itself is not obsolete so that existing field references
(`HiResClock.Disabled`) keep round-tripping through configuration; only the static
clock-reading members raise CS0618. The recommended replacements are:

| Legacy API                              | Replacement                                                                          |
| --------------------------------------- | ------------------------------------------------------------------------------------ |
| `HiResClock.UtcNow`                     | `timeProvider.GetUtcNow().UtcDateTime`                                               |
| `HiResClock.TickCount64` / `.Ticks`     | `timeProvider.GetTimestamp()`                                                        |
| `HiResClock.TickCount` (int wraparound) | `timeProvider.GetTickCount()` (internal extension in `Opc.Ua`)                       |
| `HiResClock.UtcTickCount(offsetMs)`     | `timeProvider.GetTimestampMilliseconds() + offsetMs`                                 |
| elapsed-time math via `TickCount`       | `long start = timeProvider.GetTimestamp(); … TimeSpan elapsed = timeProvider.GetElapsedTime(start);` |
| `new Stopwatch()` / `Stopwatch.StartNew()` for duration | `long start = timeProvider.GetTimestamp(); … timeProvider.GetElapsedTime(start);` |
| `new System.Threading.Timer(…)`         | `ITimer timer = timeProvider.CreateTimer(callback, state, dueTime, period);`         |
| `Task.Delay(delay, ct)` in production timing loops | `Task.Delay(delay, timeProvider, ct)`                                       |
| `new CancellationTokenSource(timeout)`  | `new CancellationTokenSource(timeout, timeProvider)`                                 |

**Constructor pattern.** Components that need a clock now take a nullable
`TimeProvider` as the **last** constructor parameter with a default value of `null`.
If `null` is passed, `TimeProvider.System` is used. Example:

```csharp
public sealed class Foo
{
    private readonly TimeProvider m_timeProvider;

    public Foo(/* existing args */, TimeProvider? timeProvider = null)
    {
        // existing initialisation…
        m_timeProvider = timeProvider ?? TimeProvider.System;
    }
}
```

For published public types whose existing constructors must remain
binary-compatible, the original constructor signature is preserved and a new
overload that ends with `TimeProvider?` is added. The legacy constructor delegates
to the new one passing `timeProvider: null`. No existing constructor is marked
`[Obsolete]` in this release.

**Dependency injection.** `AddOpcUaServerBuilder` / `AddOpcUaClientBuilder` register
`TimeProvider.System` via `TryAddSingleton<TimeProvider>` and wire the resolved
provider into every component they construct. To run a server or client against a
fake clock in tests, register a `Microsoft.Extensions.Time.Testing.FakeTimeProvider`
in the service collection before the OPC UA builders.

```csharp
services.AddSingleton<TimeProvider>(new FakeTimeProvider());
services.AddOpcUaServerBuilder(/* … */);
```

Outside DI, pass the `TimeProvider` directly to the type's constructor as the last
argument.

**Migrating off `HiResClock`.** Replace the call with the table above. If the
migration cannot happen immediately, wrap the affected scope with
`#pragma warning disable CS0618` / `#pragma warning restore CS0618`.

```csharp
// before:
long start = HiResClock.TickCount64;
DoWork();
TimeSpan elapsed = TimeSpan.FromTicks(HiResClock.TickCount64 - start);

// after:
long start = m_timeProvider.GetTimestamp();
DoWork();
TimeSpan elapsed = m_timeProvider.GetElapsedTime(start);
```

```csharp
// before:
DateTime utcNow = HiResClock.UtcNow;

// after — when a wall-clock value is required (e.g. for an OPC UA SourceTimestamp):
DateTime utcNow = m_timeProvider.GetUtcNow().UtcDateTime;
```

```csharp
// before:
m_timer = new Timer(OnTick, state: null, dueTime: 1_000, period: Timeout.Infinite);

// after:
m_timer = m_timeProvider.CreateTimer(OnTick, state: null,
    dueTime: TimeSpan.FromMilliseconds(1_000), period: Timeout.InfiniteTimeSpan);
```

The `Timer` field type changes from `System.Threading.Timer` to `ITimer` — both
implement `IDisposable` and the same `Change` / `Dispose` semantics; only the
parameter types on `Change` differ (`TimeSpan` instead of `int`/`uint`/`long`).

### Monotonic timestamps for duration calculations

`TimeProvider.GetTimestamp()` returns a `long` monotonic timestamp that does not
suffer from the 32-bit wraparound of `Environment.TickCount` / `HiResClock.TickCount`
nor the system-clock drift of `DateTime.UtcNow`. All internal duration math in the
stack now uses `GetTimestamp()` + `GetElapsedTime(start)` instead of `int`-tick
subtraction. The following public surface changes were made:

| Old (removed or `[Obsolete]`)                                            | New                                                                                                  |
| ------------------------------------------------------------------------ | ---------------------------------------------------------------------------------------------------- |
| `ISession.LastKeepAliveTickCount: int` (was on the interface)            | `ISession.LastKeepAliveTimestamp: long` + `timeProvider.GetElapsedTime(timestamp)` (legacy int now an `[Obsolete]` extension property in `SessionObsolete`) |
| `ChannelToken.Expired`, `ChannelToken.ActivationRequired`, `ChannelToken.CreatedAtTickCount` | Removed. Use `ChannelToken.IsExpired(TimeProvider)` / `ChannelToken.IsActivationRequired(TimeProvider)` (internal). |
| `UaSCUaBinaryChannel.LastActiveTickCount: int` (protected)               | Removed. Use `UaSCUaBinaryChannel.GetElapsedSinceLastActive(): TimeSpan` (internal).                 |

Pattern for new code computing an internal duration:

```csharp
// before:
int startTicks = m_timeProvider.GetTickCount();
// ... do work ...
int elapsedMs = m_timeProvider.GetTickCount() - startTicks;

// after:
long startTimestamp = m_timeProvider.GetTimestamp();
// ... do work ...
TimeSpan elapsed = m_timeProvider.GetElapsedTime(startTimestamp);
```

---

**See also**

- Related: [sessions-subscriptions.md](sessions-subscriptions.md), [node-states.md](node-states.md).
- [2.0 migration index](README.md) — analyzer quick-start + symptom → sub-doc table.
- [Migration Guide](../../MigrationGuide.md) — landing page across versions.

