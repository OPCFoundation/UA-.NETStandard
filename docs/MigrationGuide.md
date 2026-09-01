# Migration Guide

This document is the landing page for migrating your application between
versions of the OPC UA .NET Standard Stack. The detailed per-version
content lives in the [`migrate/`](migrate/) sub-folder; this page is the
index that points you at the right version folder and keeps cross-cutting
migration notes inline.

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

Copilot CLI discovers the skill automatically inside a clone. To install it
for use in any repository:

```bash
copilot plugin marketplace add OPCFoundation/UA-.NETStandard
copilot plugin install opcua-v20-migration@opcua-dotnet
```

## Per-version migration index

| From | To | Where to read |
| --- | --- | --- |
| `1.5.378` | `2.0.x` | [`migrate/2.0.x/`](migrate/2.0.x/README.md) — landing page and symptom-based thematic sub-doc index. |
| `1.05.377` | `1.05.378` | [§ inline below](#migrating-from-105377-to-105378) — small enough to keep on this page. |
| `1.04` | `1.05` | [§ inline below](#migrating-from-104-to-105) — small enough to keep on this page. |

Looking for the broader narrative (non-prescriptive overview of what
changed in a release)? See
[What's New in 2.0](WhatsNewIn2.0.md).

## Migrating Robotics and Vision MCP requests

The Robotics and Vision MCP tool names remain stable, but their request schemas
are now strongly typed. Robotics tools no longer accept JSON encoded inside a
string, and controller-scoped values can use an exact, unambiguous published
name instead of copying every NodeId.

For example, the old Pick request nested one JSON document inside another:

```json
{
  "controllerId": "ns=3;s=7001_Controllers_BinPickingController",
  "intentJson": "{\"intentId\":\"pick-red\",\"source\":\"ns=3;s=Bin\",\"tool\":\"ns=3;s=Gripper\",\"objectClass\":\"RedCube\"}"
}
```

Pass the typed object directly now:

```json
{
  "controller": "BinPickingController",
  "input": {
    "intentId": "pick-red",
    "source": "Bin",
    "tool": "ParallelGripper",
    "objectClass": "RedCube"
  }
}
```

The same change applies to every `robotics_submit_*` tool. Motion poses,
trajectory points, process attributes and program arguments are nested typed
objects or arrays. Values that become OPC UA Variants use an explicit
`dataType` plus `value`; they are never inferred through an `object`-typed API.

Mission steps and transitions are arrays rather than stringified arrays. The
intent `kind` is a closed discriminator. Kind-specific fields now sit directly
beside it, so agents do not pay for or navigate 20 nested payload wrappers:

```json
{
  "controller": "BinPickingController",
  "missionId": "move-red",
  "missionUpdateId": 1,
  "steps": [
    {
      "stepId": "pick",
      "released": true,
      "intent": {
        "kind": "Pick",
        "source": "Bin",
        "tool": "ParallelGripper",
        "objectClass": "RedCube"
      }
    },
    {
      "stepId": "place",
      "released": true,
      "intent": {
        "kind": "Place",
        "destination": "Fixture",
        "tool": "ParallelGripper"
      }
    }
  ],
  "transitions": []
}
```

`robotics_list_operations` and `robotics_list_missions` now return bounded
pages. Their optional `query` selects active or terminal work, filters by
identifier/state, chooses `Summary` or `Full`, and carries an opaque
continuation cursor. Use `robotics_wait_mission` with the MissionId and mission
operation NodeId returned by `robotics_submit_mission`; timeout returns the
current snapshot with `completed=false`, just like
`robotics_wait_operation`.

One-shot Vision inference also uses one structured request. Replace the old
`pipelineNodeId` scalar:

```json
{
  "pipelineNodeId": "ns=3;s=Vision/Pipelines/BinPickingPipeline"
}
```

with:

```json
{
  "request": {
    "pipeline": "BinPickingPipeline",
    "expectedKind": "Detection",
    "detail": "Summary",
    "maxItems": 20
  }
}
```

The result still includes the ResultId and result NodeId, and now also includes
authoritative result kind/provenance plus a bounded detection, inspection or
segmentation summary. The `vision_read_*_result` tools remain available when a
caller needs the complete result.

Callers that previously chained inference, detection selection, Pick and Place
can instead use `robotics_vision_pick`. Its one structured request names the
controller, pipeline, source, tool and optional destination plus detection
filters. The result carries the selected detection provenance and either the
intent operation or mission operation needed by the corresponding bounded wait
tool. Command authority is still requested separately.

Name matching is exact and ordinal after trimming. A missing or ambiguous name
is an error that lists the matching candidates and NodeIds; the MCP layer never
chooses the first candidate or requests command authority as a side effect.

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

To read a value derived from the session or subscription diagnostics, use the
read counterpart, which holds the same lock for the duration of the projection:

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

## Migrating code that locked on a NodeState or a NodeBrowser

A `NodeState` guards its own attributes, children, notifiers and references, and
`NodeState.CreateBrowser` guards the browser it builds. Nothing in the stack
takes a lock on a node instance any more, so neither should a caller:

```csharp
// was — the node manager reached for the node's monitor from outside the node
lock (source)
{
    browser = source.CreateBrowser(context, view, referenceType, includeSubtypes,
        browseDirection, default, null, false);
}

// now
INodeBrowser browser = source.CreateBrowser(context, view, referenceType,
    includeSubtypes, browseDirection, default, null, false);
```

`lock (node)` was also the only way to make a check-then-act pair atomic. Use
`NodeState.AddReferenceIfMissing` instead, which does the check and the insert
under the node's own lock:

```csharp
// was
lock (node)
{
    if (!node.ReferenceExists(ReferenceTypeIds.HasNotifier, true, ObjectIds.Server))
    {
        node.AddReference(ReferenceTypeIds.HasNotifier, true, ObjectIds.Server);
    }
}

// now
node.AddReferenceIfMissing(ReferenceTypeIds.HasNotifier, true, ObjectIds.Server);
```

`NodeBrowser.DataLock` is gone with it. A browser is **single-consumer**: it
belongs to whoever created it and performs no synchronization of its own. A
derived browser that took `DataLock` inside its `Next()` override drops the
`lock` statement and keeps the body. Where a browser genuinely outlives one
service call — the instance parked in a continuation point for `BrowseNext` —
its owner serializes it, as the stack does for its own continuation points.

A node type that overrides `CreateBrowser` and builds its own browser instead of
delegating to the base implementation must fill it through
`PopulateBrowserSynchronized` rather than calling `PopulateBrowser` directly,
otherwise its browser is assembled from separately locked reads and can observe
a node halfway through a change.

Analyzer `UA0027` flags `NodeBrowser.DataLock`. See
[migrate/2.0.x/node-states.md](migrate/2.0.x/node-states.md).

## Migrating code that used ApplicationConfiguration.PropertiesLock

`ApplicationConfiguration.PropertiesLock` was removed. It returned the
properties dictionary itself, so `lock (configuration.PropertiesLock)` was
`lock (configuration.Properties)`: the lock was the data it guarded, shared
between the configuration and every caller, taken in an order none of them could
see.

`Properties` is now a concurrent dictionary, so each individual operation is
already atomic and most callers simply drop the `lock`:

```csharp
// was
lock (configuration.PropertiesLock)
{
    configuration.Properties["MyKey"] = value;
}

// now
configuration.Properties["MyKey"] = value;
```

The one combination that needs more than a single operation is get-or-add, which
has its own member:

```csharp
// was
lock (configuration.PropertiesLock)
{
    if (configuration.Properties.TryGetValue("MyKey", out object? existing))
    {
        return (MyType)existing;
    }
    var created = Build();
    configuration.Properties["MyKey"] = created;
    return created;
}

// now
return configuration.GetOrAddProperty("MyKey", Build);
```

`GetOrAddProperty` deliberately does **not** invoke the factory under a lock, so
a caller cannot hold a critical section across a callback. Under contention the
factory may run more than once; only one result is published and every caller
receives that same instance, so keep the factory free of side effects that would
matter if it ran twice.

One behaviour change is worth knowing: enumerating `Properties` while another
thread writes no longer throws `InvalidOperationException`. It yields a
moment-in-time view instead. Code that relied on the exception to detect
concurrent modification has to detect it some other way.

Analyzer `UA0028` flags the removed member.

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

## Removed members on ISession

`ISession.SessionDiagnostics` is removed. It handed out the whole mutable
`SessionDiagnosticsDataType` — the structure the session's diagnostics lock
protects — so a caller could read a field while the owner was writing it.

Every server-side reader wanted one value out of it, and those two values are
now on the interface directly:

```csharp
// was
string? uri = session.SessionDiagnostics?.ClientDescription?.ApplicationUri;
string name = session.SessionDiagnostics?.SessionName ?? string.Empty;

// now
string? uri = session.ClientApplicationUri;
string name = session.SessionName;
```

For anything else in the structure, project it inside `ReadDiagnostics`, which
holds the lock for the duration of the projection:

```csharp
uint reads = session.ReadDiagnostics(diagnostics => diagnostics.ReadCount.TotalCount);
```

`SessionName` is read from the field it was always a copy of rather than from
the diagnostics, because it is assigned once during construction and a value
that cannot change should not cost a lock.

The concrete `Session` still exposes `SessionDiagnostics`; only the interface
loses it.

`ISession.ValidateBeforeActivate` — the synchronous overload with
`out IUserIdentityTokenHandler?` and `out UserTokenPolicy?` parameters — is
removed. It had no caller anywhere in the stack, its samples or its tests other
than tests written for it, and it had been `[Obsolete]` since 1.5.378.

Use `ValidateBeforeActivateAsync`, which returns the same two values as a tuple:

```csharp
(IUserIdentityTokenHandler identityToken, UserTokenPolicy? userTokenPolicy) =
    await session.ValidateBeforeActivateAsync(
        context, clientSignature, userIdentityToken, userTokenSignature, ct)
    .ConfigureAwait(false);
```

The synchronous overload could not verify a user token that required
decryption, so on a secure endpoint it failed closed and directed callers to
the asynchronous path anyway.

The history continuation points moved off `ISession` onto
`ISession.ContinuationPoints`, and no longer pass `object`. `SaveHistory` and
`RestoreHistory` use `IHistoryContinuationPoint`, which carries the point's own
`Guid Id` and extends `IDisposable`:

```csharp
// was
session.SaveHistoryContinuationPoint(state.Id, state);
object? restored = session.RestoreHistoryContinuationPoint(bytes);

// now
session.ContinuationPoints.SaveHistory(state);   // the point carries its Id
IHistoryContinuationPoint? restored = session.ContinuationPoints.RestoreHistory(bytes);
```

Implement `IHistoryContinuationPoint` on whatever type you store. The session
previously disposed only those points that happened to implement `IDisposable`
and silently leaked the rest; every point is now disposed.

## Migrating code that called IServerInternal.Set* mutators

`IServerInternal` no longer exposes the twelve `Set*` binding methods or
`CreateServerObjectAsync`. They were startup plumbing: `StandardServer` calls
each exactly once, in one block, to carry a `Create*` factory result into the
datastore. Publishing them on the interface let any holder rewire a running
server, which would leave every component that had already resolved a subsystem
holding the previous instance.

The supported seam is the factory seam, which already existed for every
subsystem here:

| Instead of | Override | Or register in DI |
| --- | --- | --- |
| `SetRoleManager` | `StandardServer.CreateRoleManager` | `IRoleManager` |
| `SetUserManagement` | `StandardServer.CreateUserManagement` (new) | `IUserManagement` |
| `SetMonitoredItemQueueFactory` | `StandardServer.CreateMonitoredItemQueueFactory` | `IMonitoredItemQueueFactory` |
| `SetSubscriptionStore` | `StandardServer.CreateSubscriptionStore` | `ISubscriptionStore` |
| `SetMainNodeManagerFactory` | `StandardServer.CreateMainNodeManagerFactory` | — |
| `SetNodeManager` | `StandardServer.CreateMasterNodeManager` | — |
| `SetSessionManager` | `StandardServer.CreateSessionManager` / `CreateSubscriptionManager` | `ISessionManager`, `ISubscriptionManager` |
| `SetAggregateManager` | `StandardServer.CreateAggregateManagerAsync` | — |
| `SetModellingRulesManager` | `StandardServer.CreateModellingRulesManagerAsync` | — |
| `SetConformanceUnitsManager` | `StandardServer.CreateConformanceUnitsManagerAsync` | — |

`CreateUserManagement` is new in this release, because user management was the
one subsystem with no factory seam. Registering an `IUserManagement` in the
container also switches on the username/password authenticator; override
`CreateUserManagement` if you want the Part 18 §5 model without that.

`SetIdentityRegistry` is **removed with no replacement**. Nothing ever called
it: the supported route has always been
`ServerIdentityRegistryExtensions.RegisterDefaultAuthenticators`, which adds
authenticators to the default registry rather than replacing it.

The methods remain on the concrete `ServerInternalData`, so code that already
held that type keeps compiling. Binding is now refused once the server has
finished starting — a late `Set*` throws `ServiceResultException` with
`BadInvalidState` naming the operation.

## Migrating IServerStartupTask implementations to IServerContext

`IServerStartupTask.OnServerStartedAsync` now receives an `IServerContext`
instead of an `IServerInternal`. `IServerInternal` derives from
`IServerContext`, so the host still passes the same object; only the
declared parameter type changes and an implementation fails to compile
(`CS0535`) until its signature is updated.

`IServerContext` is the ambient view of a running server. It carries what
is genuinely server-wide and nothing else — it deliberately does not hand
out the server's subsystems. A startup task that needs a subsystem takes it
as a constructor dependency, which every implementation in this repository
already did for its other dependencies.

Rewrite the member reads that no longer resolve:

| Was | Now |
| --- | --- |
| `server.Telemetry` | `server.DefaultSystemContext.Telemetry` |
| `server.NamespaceUris` | `server.DefaultSystemContext.NamespaceUris` |
| `server.ServerUris` | `server.DefaultSystemContext.ServerUris` |
| `server.TypeTree` | `server.DefaultSystemContext.TypeTable` |
| `server.Factory` | `server.DefaultSystemContext.EncodeableFactory` |
| `server.DiagnosticsNodeManager.FindPredefinedNode<T>(id)` | `server.FindPredefinedNode<T>(id)` |
| `server.NodeManager.NodeManagers` + a type test | `server.FindNodeManagers<TCapability>()` |
| `server.SessionManager`, `server.SubscriptionManager`, `server.RequestManager`, `server.AggregateManager`, `server.RoleManager`, `server.IdentityRegistry`, … | constructor injection |

`server.MessageContext` is unchanged and remains on `IServerContext`. Do
**not** substitute `server.DefaultSystemContext.AsMessageContext()` for it:
that conversion produces a context with *default* decoding limits rather
than the server's configured `MaxStringLength`, `MaxArrayLength` and
`MaxByteStringLength`, which silently widens what your component accepts.

Tests that hand a `Mock<IServerInternal>` to a startup task keep compiling,
because the mock still satisfies `IServerContext`. Stub the members the task
actually reads now — typically `DefaultSystemContext` and any
`FindNodeManagers<T>()` — or the mock returns `null` and the task fails at
run time rather than at build time.

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

## Migrating callers of the synchronous MonitoredNode2 notification wrappers

`MonitoredNode2.OnReportEvent` and `MonitoredNode2.OnMonitoredNodeChanged`
are `[Obsolete]`; use `OnReportEventAsync` and
`OnMonitoredNodeChangedAsync`. Nothing in the stack wires the synchronous
pair any more — notifiers are attached through
`NodeState.OnReportEventAsync` and `NodeState.OnStateChangedAsync` — and
both wrappers block the calling thread whenever the bounded notification
channel is full, or whenever the node has an asynchronous read handler.
Blocking there occupies a thread while waiting for a consumer that needs
a thread of its own, which starves the thread pool under load.

```csharp
// before
monitoredNode.OnReportEvent(context, node, e);

// after
await monitoredNode.OnReportEventAsync(context, node, e, cancellationToken)
    .ConfigureAwait(false);
```

The wrappers still work and are unchanged in behaviour, so this is a
warning to act on rather than a break.

<a id="ua0029"></a>

## Migrating callers of the SecurityPolicies lookup and cryptography statics

The static lookup and cryptography methods on `SecurityPolicies` have
**moved** to `ISecurityPolicyRegistry`. They read the set of registered
security policies, so they are members of the registry that owns that set
rather than free functions on a constants class.

Moved: `GetUri`, `GetDisplayName`, `GetDisplayNames`,
`IsValidSecurityPolicyUri`, `GetDefaultUris`, `GetDefaultEccUris`,
`GetDefaultDeprecatedUris`, `Encrypt` and `Decrypt`.

**The policy URI constants are unaffected.** `SecurityPolicies.None`,
`SecurityPolicies.Basic256Sha256` and the rest stay exactly where they are,
which is the overwhelming majority of references to this type.

Resolve an `ISecurityPolicyRegistry` where a container is in scope, so the
policies that application registered are the ones used:

```csharp
// before
string uri = SecurityPolicies.GetUri("Basic256Sha256");

// after - the application's own policy set
public sealed class MyService(ISecurityPolicyRegistry policies)
{
    public string? Uri => policies.GetUri("Basic256Sha256");
}
```

Where there is no container — configuration loading, for instance — use
the fallback, which carries the built-in policies:

```csharp
string? uri = SecurityPolicies.Default.GetUri("Basic256Sha256");
```

`Encrypt` and `Decrypt` additionally **lose their `ILogger` argument**. The
registry is created with an `ITelemetryContext` and reports through the
logger it made from it:

```csharp
// before
EncryptedData data = SecurityPolicies.Encrypt(certificate, uri, plainText, logger);

// after
EncryptedData data = policies.Encrypt(certificate, uri, plainText);
```

Registering a policy through the container no longer changes what other
code in the same process sees. `AddSecurityPolicy` applies the policy to
the registry that container owns, so two applications hosted together keep
separate policy sets. Use `AddSecurityPolicyRegistry()` to resolve a
registry without contributing a policy of your own.

The `OPCFoundation.NetStandard.Opc.Ua.MigrationAnalyzer` package restores
the removed members as `[Obsolete]` extension members that forward to
`SecurityPolicies.Default`, so a 1.05.378 application compiles with
a warning rather than an error. The `ILogger` argument on the `Encrypt` and
`Decrypt` shims is accepted and **ignored**.

<a id="ua0030"></a>

## Migrating code that drove the server subscription publish pipeline

Twelve members left the server `Opc.Ua.Server.ISubscription`, and the
`SessionPublishQueue` class became internal. The publish pipeline — timer
expiry, message acknowledgement, notification consumption, session release —
is now driven exclusively by `SubscriptionManager` and the publish queue
through an internal contract that only `Subscription` implements. Any holder
of an `ISubscription` (for example via
`IServerInternal.SubscriptionManager.GetSubscriptions()`) could previously
call these members and corrupt the publishing state machine: consume
notifications a client never saw, advance sequence numbers, or release a
subscription its session still owned.

**Deleted outright — remove the call.** `ItemReadyToPublish` and
`ItemNotificationsAvailable` had commented-out bodies since 1.5.x, so every
call was already a no-op. The obsolete parameterless `SessionClosed()` is
gone with them, and so is `TransferSessionAsync`: the server transfers
subscriptions through its internal claim/prepare/commit protocol, reached
via the `TransferSubscriptions` service
(`ISubscriptionManager.TransferSubscriptionsAsync`) — the one-shot direct
transfer bypassed the reservation that protects a transfer against a
concurrently closing source session.

**Internalized — use the service operations.** `PublishTimerExpired`,
`Acknowledge`, `PublishTimeout`, `SubscriptionTransferred`,
`AvailableSequenceNumbersForRetransmission`, `QueueOverflowHandler`,
`SessionClosed(ISession)` and `Publish` are no longer on the interface.
Code that called them was reimplementing a slice of the server; the
supported path is the service set (`Publish`, `Republish`,
`TransferSubscriptions`) and the `ISubscriptionManager` surface
(`SessionClosingAsync` for session teardown). `ResendData`,
`GetMonitoredItems` and the monitored-item service operations remain on
`ISubscription` — resolve the subscription with
`ISubscriptionManager.TryGetSubscription` first, the way the
`Server_ResendData` method handler does.

```csharp
// was: drive the pipeline directly
if (server.SubscriptionManager.TryGetSubscription(subscriptionId, out ISubscription? subscription))
{
    subscription.PublishTimerExpired();
    subscription.Acknowledge(context, sequenceNumber);
}

// now: the pipeline is server-internal; acknowledgements travel with the
// Publish service request, and the publish timer belongs to the server
Task<PublishResponse> response = server.SubscriptionManager.PublishAsync(
    context, subscriptionAcknowledgements, parkSink, cancellationToken);
```

**Custom subscription implementations must derive from `Subscription`.**
`SubscriptionManager.CreateSubscription` and `RestoreSubscriptionAsync`
still return `ISubscription`, but an override returning a type that does not
derive from `Subscription` now fails at creation with `Bad_InternalError`
instead of publishing partially (transfer already required the concrete
type). Derive from `Subscription` and override the behaviour you need; the
pipeline members are explicit implementations of the internal contract and
are not virtual.

The no-`[Obsolete]`-shim rule [above](#why-there-is-no-obsolete-shim)
applies here for the same reason: `ISubscription` is implemented by
downstream code, and re-adding an interface member later would break every
implementer. Analyzer `UA0030` flags each removed member on the migration
path and names the replacement.

## Migrating channel subclasses that guarded state with DataLock

`UaSCBinaryChannel.DataLock` has been **removed**. The channel no longer
serialises any of its own state on it, so taking it excluded nothing.

A monitor cannot be held across an `await`, and the secure channel open
path has to be able to await once a private key may be served over a
network — see [Crypto provider](CryptoProvider.md). The channel now uses
an internal gate that can be entered from a synchronous or an
asynchronous path. Unlike a monitor the gate is **not re-entrant**: every
channel path that used to take the lock while already holding it now
calls a lock-free `Core` variant instead.

That has one consequence for subclasses. The channel calls
`HandleSocketError`, `NotifyMonitors` and `CompleteReverseHello` from
paths that may already hold the gate, so an override of any of them must
not call back into a channel method that takes it — `ForceChannelFault`
and `SendResponse` in particular. Such an override would have silently
nested before and will now block.

For the same reason `SaveIntermediateChunk`, `GetSavedChunks` and
`DoMessageLimitsExceeded` take an additional `gateHeld` argument. It says
whether the calling frame already holds the gate, so that an override
which tears the channel down can pick the locking or the lock-free path:

```csharp
// before
protected override void DoMessageLimitsExceeded()
{
    base.DoMessageLimitsExceeded();
    Shutdown(new ServiceResult(StatusCodes.BadResponseTooLarge));
}

// after
protected override void DoMessageLimitsExceeded(bool gateHeld)
{
    base.DoMessageLimitsExceeded(gateHeld);

    if (gateHeld)
    {
        ShutdownCore(new ServiceResult(StatusCodes.BadResponseTooLarge));
        return;
    }

    Shutdown(new ServiceResult(StatusCodes.BadResponseTooLarge));
}
```

There is no drop-in replacement to offer across an assembly boundary,
because the gate has to be entered asynchronously on the open path and
its correctness depends on rules that only hold inside the channel
implementation. A subclass outside this stack that guarded **its own**
state with `DataLock` should introduce its own synchronisation:

```csharp
// before
lock (DataLock)
{
    m_myState = value;
}

// after
private readonly System.Threading.Lock m_myLock = new();

using (m_myLock.EnterScope())
{
    m_myState = value;
}
```

A subclass that took `DataLock` in order to be mutually exclusive with
the **channel's** state transitions was already relying on an
implementation detail, and can no longer do so.

## Migrating channel subclasses that override HandleIncomingMessage

`UaSCBinaryChannel.HandleIncomingMessage` and `OnChunkReceived` have been
**removed**, as has the `protected WriteAsymmetricMessage` overload that
returned the signature through an `out` parameter. The receive loop calls
`HandleIncomingMessageAsync` and `OnChunkReceivedAsync`, so that the
secure channel open path can await a private key served over a network —
see [Crypto provider](CryptoProvider.md).

A synchronous override cannot be kept working underneath the
asynchronous path without defeating the point of it, so **an existing
override must be moved**. The signature gains a `CancellationToken` and
returns `ValueTask<bool>`:

```csharp
// before
protected override bool HandleIncomingMessage(
    uint messageType, ArraySegment<byte> messageChunk)
{
    ...
}

// after
protected override async ValueTask<bool> HandleIncomingMessageAsync(
    uint messageType, ArraySegment<byte> messageChunk, CancellationToken ct)
{
    ...
}
```

An override that has nothing to await can return a completed value
without going asynchronous at all:

```csharp
protected override ValueTask<bool> HandleIncomingMessageAsync(
    uint messageType, ArraySegment<byte> messageChunk, CancellationToken ct)
{
    return new ValueTask<bool>(HandleSynchronously(messageType, messageChunk));
}
```

The buffer-ownership contract is unchanged: return `true` when the
implementor takes ownership of the chunk, and it will not be returned to
the buffer manager for you.

`ReadAsymmetricMessageAsync` and `WriteAsymmetricMessageAsync` return
`AsymmetricMessage` and `AsymmetricWriteResult` rather than using `out`
parameters, which an asynchronous method cannot have. Use
`WriteAsymmetricMessageAsync` in place of the removed synchronous
overload.

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
  package — 26 implemented analyzer rules through `UA0030` (excluding
  `UA0013`, `UA0016`, `UA0017`, and the shim-only `UA0029`) map across the
  [`migrate/2.0.x/`](migrate/2.0.x/README.md) guides and the cross-cutting
  notes on this page; 14 rules apply safe edits via a code-fixer. `UA0029`
  is currently a runtime-shim/manual marker surfaced through `CS0618`, not an
  analyzer diagnostic.
- Open an issue on
  [OPCFoundation/UA-.NETStandard](https://github.com/OPCFoundation/UA-.NETStandard/issues).
