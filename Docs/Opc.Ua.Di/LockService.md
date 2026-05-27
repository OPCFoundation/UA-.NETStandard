# DI Lock Service

The lock service implements the OPC 10000-100 §10.5 locking facet. Ittracks ownership of every
`TopologyElementType.Lock` instance in the address space, enforces
configurable timeouts, releases locks automatically when the owning
session closes, and exposes the four spec-defined methods through
the typed `LockingServicesState` proxy.

## Components

| Type | Purpose |
|------|---------|
| `ILockService` | Application-facing facade: `InitLock`, `RenewLock`, `ExitLock`, `BreakLock`, `GetState`. Thread-safe. |
| `LockState` | Snapshot record (Locked, LockingClient, LockingUser, RemainingLockTimeSeconds). |
| `LockStatus` | OPC 10000-100 status-code constants. |
| `DefaultLockService` | Default in-memory implementation backed by a `ConcurrentDictionary`. |
| `LockingServicesExtensions.BindToLockService` | Wires the four generated `*MethodState.OnCall` handlers on a `LockingServicesState` instance through an `ILockService`. |

Namespaces: `Opc.Ua.Di.Server.Locking`.

## Status codes

Following OPC 10000-100 §10.5:

| Constant | Value | Returned from |
|----------|-------|---------------|
| `LockStatus.Ok` | 0 | All methods (success) |
| `LockStatus.AlreadyLocked` | 1 | `InitLock` only |
| `LockStatus.CouldNotLock` | 2 | `InitLock` only |
| `LockStatus.NotLocked` | 1 | `RenewLock` / `ExitLock` / `BreakLock` |
| `LockStatus.WrongClient` | 2 | `RenewLock` / `ExitLock` |

## Default lock service

```csharp
var lockService = new DefaultLockService(
    lockDuration: TimeSpan.FromMinutes(5),   // optional, defaults to 5 min
    timeProvider: TimeProvider.System);       // optional, for tests

// Hook session-close so locks held by a disconnecting session are
// released automatically.
lockService.AttachToSessionManager(server.SessionManager);
```

Lock ownership is keyed by `ServerSystemContext.SessionId`. When the
service is invoked from a test (`SystemContext` rather than
`ServerSystemContext`), it falls back to a synthetic session id
derived from `ISystemContext.UserId` so unit tests can still
distinguish callers.

## Binding to a `LockingServicesState`

The DI spec defines a `Lock` child on every `TopologyElementType` that
implements four method invocations. Wire them through your service:

```csharp
LockingServicesState lockNode = device.Lock!;
lockNode.BindToLockService(elementId: device.NodeId, service: lockService);
```

`BindToLockService` reads `lockNode.InitLock`, `RenewLock`, `ExitLock`,
`BreakLock` and replaces their `OnCall` handlers with delegates that
route into the `ILockService`. The `elementId` argument keys the
service's internal dictionary — typically the topology element's own
NodeId so two different devices can hold independent locks.

## End-to-end flow

1. Client calls `InitLock(context: "tag")` on a device's Lock.
2. The method's `OnCall` runs the bound `ILockService.InitLock`,
   which checks the per-device record:
   - If unlocked / expired → record `(SessionId, "tag", UserId, now + duration)`,
     return `Ok` (0).
   - If already locked by another session → return `AlreadyLocked` (1).
3. `RenewLock` (same session) extends the expiry timestamp; from a
   different session it returns `WrongClient` (2).
4. `ExitLock` removes the record (subject to ownership check).
5. `BreakLock` removes the record regardless of ownership; intended
   for administrative recovery.
6. If the owning session closes (server-emitted `SessionClosing`
   event), the `DefaultLockService` walks its records and releases
   anything that was held by that session.

## Hosting

Register the service as a singleton through standard
Microsoft.Extensions DI:

```csharp
services.AddSingleton<ILockService, DefaultLockService>();
```

Then attach the session-close hook from inside a
`ConfigureDevicesFor<TNodeManager>` configurator once the server's
`SessionManager` is available:

```csharp
.ConfigureDevicesFor<DiNodeManager>(ctx =>
{
    ILockService svc = ctx.GetRequiredService<ILockService>();
    // ctx.Manager.Server is the IServerInternal; SessionManager hangs off it.
    if (svc is DefaultLockService defaultSvc)
    {
        defaultSvc.AttachToSessionManager(ctx.Manager.Server.SessionManager);
    }
    // Bind to specific device locks:
    foreach (DeviceState device in ctx.Manager.PredefinedNodes
        .Values.OfType<DeviceState>())
    {
        device.Lock?.BindToLockService(device.NodeId, svc);
    }
});
```

## See also

- [DiLockClient](ClientHelpers.md#dilockclient) — client-side wrapper
  for the four lock methods.
- [Hosting](Hosting.md) — how `ConfigureDevicesFor` runs your
  binding code at the right moment.
