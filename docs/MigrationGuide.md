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

<a id="ua0024"></a>

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
  package — analyzer rules `UA0001`-`UA0020` map to the patterns in
  [`migrate/2.0.x/types.md`](migrate/2.0.x/types.md) and apply most
  edits via a code-fixer.
- Open an issue on
  [OPCFoundation/UA-.NETStandard](https://github.com/OPCFoundation/UA-.NETStandard/issues).
