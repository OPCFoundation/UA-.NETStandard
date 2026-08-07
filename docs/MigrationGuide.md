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
`UpdateServerDiagnostics` all moved *down* to `IServerContext`. They remain
reachable through `IServerInternal` unchanged. `CurrentState` is read-only
on the ambient interface; the server itself still sets it.

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
