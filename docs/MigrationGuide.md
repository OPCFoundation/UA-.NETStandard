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

`ISessionManager` gained `ShutdownAsync(CancellationToken)`, and the
synchronous `Shutdown()` is `[Obsolete]`. `SessionManager` previously
started its session monitor loop with a discarded
`Task.Factory.StartNew(...)`, so `Shutdown()` only *signalled* the loop
and returned: the server could finish tearing down while the monitor was
still closing expired sessions and raising keep-alive events against
half-disposed state. `ShutdownAsync` cancels the loop and awaits it
before disposing the sessions, which matches
`ISubscriptionManager.ShutdownAsync`.

**Callers** need no change beyond preferring the async overload —
`Shutdown()` still signals and closes the sessions, it just does not wait
for the loop:

```csharp
// before
server.SessionManager.Shutdown();

// after
await server.SessionManager.ShutdownAsync(cancellationToken)
    .ConfigureAwait(false);
```

**Implementers** of `ISessionManager` (for example a manager registered
through `services.AddSessionManager<T>()`) must add `ShutdownAsync`. If
your implementation has no background work, delegate:

```csharp
public ValueTask ShutdownAsync(CancellationToken cancellationToken = default)
{
    CloseAllSessions();
    return default;
}
```

Deriving from `SessionManager` requires no change: both members are
`virtual` and the base implementations already cooperate.

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
