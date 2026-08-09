# Migration Guide

This document is the landing page for migrating your application between
versions of the OPC UA .NET Standard Stack. The detailed per-version
content lives in the [`migrate/`](migrate/) sub-folder; this page is the
index that points you at the right version folder and keeps the small
legacy migration notes inline.

## General principles

1. All API that is replaced with newer API is marked `[Obsolete]` and
   code should compile and work albeit of the warnings (which can be
   suppressed). `[Obsolete]` API will be cleaned up in the next *minor*
   version increment. We therefore recommend upgrading from minor
   version to minor version and fixing all `[Obsolete]` warnings as you
   go along.
2. API that cannot be supported anymore will be removed in a minor
   version and migration steps documented in the version sub-folder.
   We try to keep this to an absolute minimum.
3. Bugs or issues found in obsoleted API are not supported.
4. We follow semver, but do not use the major version indicator to
   denote breaking changes like (1) or (2) as we should if we followed
   related conventions. We are a small team and cannot afford to
   maintain previous major versions, therefore we try to keep cases of
   (2) to a minimum and expect you to upgrade to the next minor version
   within 6 months of release.

> **Pro TIP.** Point your favourite coding agent at this guide and let
> it do the migration work for you. The
> [`opcua-v20-migration`](../.agents/skills/opcua-v20-migration/SKILL.md)
> agent skill knows when to load which sub-doc and runs the
> migration-analyzer codefixer end-to-end.

## Per-version migration index

| From | To | Where to read |
| --- | --- | --- |
| `1.5.378` | `2.0.x` | [`migrate/2.0.x/`](migrate/2.0.x/README.md) — landing page + 13 thematic sub-docs (telemetry, packages, source-generation, types, encoders, node-states, identity, certificates, configuration, sessions-subscriptions, [pubsub](migrate/2.0.x/pubsub.md), alarms-model-change, timeprovider). |
| `1.05.377` | `1.05.378` | [§ inline below](#migrating-from-105377-to-105378) — small enough to keep on this page. |
| `1.04` | `1.05` | [§ inline below](#migrating-from-104-to-105) — small enough to keep on this page. |

Looking for the broader narrative (non-prescriptive overview of what
changed in a release)? See
[What's New in 2.0](WhatsNewIn2.0.md).

## Migrating code that used the exposed diagnostics locks

`IServerInternal`, `ISession` and `ISubscription` no longer expose their
synchronization primitives. The removed members are:

| Interface | Removed |
| --- | --- |
| `IServerInternal` | `DiagnosticsLock`, `DiagnosticsWriteLock` |
| `ISession` | `DiagnosticsLock` |
| `ISubscription` | `DiagnosticsLock`, `DiagnosticsWriteLock` |

A caller could not reason about these locks: it could not see what else took
them, in what order, or for how long, and holding one across a call back into
the stack could deadlock. Each owner now applies the mutation itself, so the
critical section stays inside the object that understands it.

```csharp
// was
lock (server.DiagnosticsLock)
{
    server.ServerDiagnostics.RejectedSessionCount++;
}

// now
server.UpdateServerDiagnostics(diagnostics => diagnostics.RejectedSessionCount++);
```

The same shape applies to sessions and subscriptions:

```csharp
session.UpdateDiagnostics(diagnostics => diagnostics.ClientLastContactTime = now);
subscription.UpdateDiagnostics(diagnostics => diagnostics.NextSequenceNumber = next);
```

To read a value derived from the diagnostics, use the read counterpart, which
holds the same lock for the duration of the projection:

```csharp
uint count = session.ReadDiagnostics(diagnostics => diagnostics.RepublishRequestCount);
```

**Do not let the diagnostics object escape the callback.** Once the callback
returns the lock is released, so any field read from a captured reference is
unsynchronized. Project the values you need inside the callback and return
those.

`IServerInternal.ServerDiagnostics` was removed for the same reason: it handed
out the mutable structure that the lock protects. `UpdateServerDiagnostics` and
the diagnostic node manager are the supported paths.

Analyzer `UA0024` flags each removed member and names its replacement. It
reports rather than auto-fixes: turning a `lock` statement body into a lambda is
not a transformation that can be applied safely without understanding what the
body captures and returns.

### Why there is no `[Obsolete]` shim

Every other removal in this guide keeps an `[Obsolete]` member for a release.
These do not, and deliberately so. A lock is only useful if it is *the* lock the
owner takes. A shim would have to hand back either a lock nobody else takes -
silently turning a working critical section into no synchronization at all - or
the real lock, which is exactly the coupling being removed. A missing member is
a compile error the analyzer explains; a shim would be a race that shows up in
production. `ISession` and `ISubscription` are also implemented by downstream
code, and re-adding an interface member would break every implementer.

## Migrating code that used ILocalNode.DataLock

`ILocalNode.DataLock` (implemented by `Node`) was removed. It returned the node
instance itself, so `lock (node.DataLock)` was `lock (node)`: one lock shared
between the stack, the node and every caller, taken in an order none of them
could see.

```csharp
// was
lock (node.DataLock)
{
    value = node.Value;
}

// now - the node guards its own state
value = node.Value;
```

If the surrounding operation has to stay atomic across several calls, take a
lock the calling component owns. Do not reach for one that is reachable from a
shared node. Analyzer `UA0025` flags the removed member.

## Migrating code that used BaseVariableValue.Lock

`BaseVariableValue.Lock` was removed, and the constructor now takes a
`System.Threading.Lock` instead of an `object`.

A derived value class - which is what the source generator emits for every
structure variable - synchronizes through the protected `EnterLock()` /
`ExitLock()` pair:

```csharp
EnterLock();
try
{
    // read or write the value fields
}
finally
{
    ExitLock();
}
```

A component that has to make its own state atomic with the value passes a lock
it already owns to the constructor and takes that one directly. This is how the
server keeps its status and its diagnostics mutually exclusive:

```csharp
private readonly Lock m_diagnosticsLock = new();

// the value is constructed with the lock its owner already holds elsewhere
m_status = new ServerStatusValue(statusNode, status, m_diagnosticsLock);

// so the owner synchronizes against the value without the value handing anything out
lock (m_diagnosticsLock)
{
    ...
}
```

Analyzer `UA0026` flags the removed member. Note that regenerating the model
sources with the 2.0 generator produces the `EnterLock()` / `ExitLock()` form
already, so this only affects hand-written derived value classes and callers.

## Migrating node types that override FindChild or CreateChild
`NodeState.FindChild` and `NodeState.CreateChild` take
`assignInstanceNodeIds` as their last parameter, and the four argument
`FindChild` / two argument `CreateChild` virtuals are gone. The parameter
defaults to `true`, so **call sites are unaffected**; an override fails to
compile (`CS0115`) until the parameter is added and passed on.

Behaviour note: a node copy — `NodeState.Create(context, source)` and the
`Initialize(ISystemContext, NodeState)` path behind it — now passes
`assignInstanceNodeIds: false`. It no longer asks
`ISystemContext.NodeIdFactory` for identifiers that the copy overwrites
from the source on the very next statement. If your `INodeIdFactory`
counts, reserves or audits every allocation, expect **fewer** calls than in
1.5.378 for the same address space; the resulting NodeIds are unchanged.
Any `NodeState` subclass you own must thread the argument into its
`CreateOrReplace<Child>` calls to get that benefit.

See
[Node states § FindChild and CreateChild](migrate/2.0.x/node-states.md#nodestate-findchild-and-createchild-state-nodeid-assignment)
for the before/after and
[Custom node types and assignment control](NodeManagers.md#custom-node-types-and-assignment-control)
for the runtime rules.

## Removed members on IServerInternal

The following members had no consumer anywhere in the stack, its samples or
its tests, and have been removed. Each has a direct replacement:

| Removed | Use instead |
| --- | --- |
| `CloseSession(OperationContext, NodeId, bool)` | `CloseSessionAsync(…)` |
| `Status` | `CurrentState` to read, `UpdateServerStatus` to write |
| `ServerDiagnostics` | `UpdateServerDiagnostics(Action<…>)` |
| `DiagnosticsEnabled` | `IDiagnosticsNodeManager.DiagnosticsEnabled` |
| `ModellingRulesManager`, `ConformanceUnitsManager` | the concrete `ServerInternalData`, which owns them |

`MessageContext`, `DefaultSystemContext`, `CurrentState`, `ServerObject`,
`ReportEventAsync`, `CloseSessionAsync`, `DeleteSubscriptionAsync` and
`UpdateServerDiagnostics` all moved *down* to `IServerContext`, the ambient
view of a running server that `IServerInternal` now derives from. They remain
reachable through `IServerInternal` unchanged, so no call site has to move.
`CurrentState` is read-only on the ambient interface; the server itself still
sets it.

## Migrating servers that relied on unserved history advertisement

Server startup now reconciles variables that advertise history with the
historian providers actually wired into the server. If a variable has
`Historizing=true` or `HistoryRead` / `HistoryWrite` access-level bits
from a NodeSet but no `IHistorianProvider` resolves for it, the server
clears the advertisement and masks the attribute read callbacks before
accepting clients. Variables with a provider keep their NodeSet-declared
history surface.

If a client or CTT setup expected `HistoryRead` solely because the
NodeSet declared it, wire a historian instead of relying on the static
flag: use `builder.UseHistorian()` and `.Historize(...)`, register a
provider through the server-wide historian registry, or override
`GetHistorianProvider(NodeState)` in the node manager. See
[Server address-space metadata](NodeManagers.md#server-address-space-metadata) and
[Historical Access](HistoricalAccess.md).

## Migrating custom ISessionManager implementations to ShutdownAsync

`ISessionManager.Shutdown()` is **gone**, replaced by
`ShutdownAsync(CancellationToken)`. `SessionManager` previously started its
session monitor loop with a discarded `Task.Factory.StartNew(...)`, so
`Shutdown()` only *signalled* the loop and returned: the server could
finish tearing down while the monitor was still closing expired sessions
and raising keep-alive events against half-disposed state. There is no
correct synchronous way to wait for that loop — blocking on it would be
sync-over-async — so the synchronous overload was removed rather than
kept as a trap. `ShutdownAsync` cancels the loop and awaits it before
disposing the sessions, matching `ISubscriptionManager.ShutdownAsync`.

**Callers** await instead of calling:

```csharp
// before
server.SessionManager.Shutdown();

// after
await server.SessionManager.ShutdownAsync(cancellationToken)
    .ConfigureAwait(false);
```

**Implementers** of `ISessionManager` (for example a manager registered
through `services.AddSessionManager<T>()`) replace `Shutdown` with
`ShutdownAsync`. If your implementation has no background work, return a
completed task:

```csharp
public ValueTask ShutdownAsync(CancellationToken cancellationToken = default)
{
    CloseAllSessions();
    return default;
}
```

Deriving from `SessionManager` requires no change beyond renaming any
`Shutdown` override: `ShutdownAsync` is `virtual` and the base
implementation already awaits the monitor loop.

## Migrating from 1.05.377 to 1.05.378

### Asynchronous as default

The server now supports `AsyncNodeManagers`; see
[Server Async (TAP) Support](AsyncServerSupport.md). The client APIs are
async by default and all synchronous and APM-based API has been
deprecated. To migrate, update your code to use the `Async` version of
every API where possible. Not recommended but for expedience you can
call the `Async` version synchronously with
`GetAwaiter().GetResult()`.

### Observability

Observability is now plumbed through `ITelemetryContext`. The legacy
static `Utils.SetLogger` / `Utils.Trace*` model has been removed in
2.0. See
[`migrate/2.0.x/telemetry.md`](migrate/2.0.x/telemetry.md) for OLD
vs NEW snippets, the per-type constructor matrix, and the full
inventory of removed / `[Obsolete]` `Utils` APIs.

Configuration-level trace apply APIs were removed as well: `TraceConfiguration.ApplySettings()` and `ApplicationConfigurationBuilder` trace setters (`SetOutputFilePath`, `SetDeleteOnLoad`, `SetTraceMasks`). Configure logging through `ITelemetryContext` instead.
## Migrating from 1.04 to 1.05

A few features are still missing to fully comply with 1.05, but
certification for v1.04 is still possible with the 1.05 release.

## Support

For additional migration support:

- Review sample applications in the repository.
- Check unit tests for usage patterns.
- Use the
  [`OPCFoundation.NetStandard.Opc.Ua.MigrationAnalyzer`](https://www.nuget.org/packages/OPCFoundation.NetStandard.Opc.Ua.MigrationAnalyzer)
  package — analyzer rules `UA0001`-`UA0020` map to the patterns in
  [`migrate/2.0.x/types.md`](migrate/2.0.x/types.md) and apply most
  edits via a code-fixer.
- Open an issue on
  [OPCFoundation/UA-.NETStandard](https://github.com/OPCFoundation/UA-.NETStandard/issues).
