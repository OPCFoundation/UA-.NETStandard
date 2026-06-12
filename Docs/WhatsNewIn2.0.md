# What's New in OPC UA .NET Standard 2.0

This document is a developer-facing tour of the changes between **1.5.378** and
**2.0**. It is organised by theme and layer; each section describes the
broad-stroke change with a paragraph and links to the deeper feature
documentation in this folder.

If you are migrating an existing application, the companion
[Migration Guide](MigrationGuide.md) is the prescriptive, API-level reference.

## At a glance

- **The OPC UA built-in types are now allocation-friendly value types**, with
  a `Variant`-first public API, a new `ByteString` type, and `ArrayOf<T>` /
  `MatrixOf<T>` replacing untyped array shapes.
- **Server- and client-side stacks are now fully `async`/`await`** with
  cancellation flowing through the request lifetime, `TimeProvider` everywhere,
  and the new `AsyncCustomNodeManager` powering all built-in NodeManagers.
- **A first-class hosting story**: `services.AddOpcUa()` and `IOpcUaBuilder`
  plug servers and clients into `Microsoft.Extensions.DependencyInjection` and
  the .NET Generic Host, complemented by a fluent server and `ManagedSession`
  fluent client builder.
- **Native AOT support across the stack**, including AOT-clean source
  generators, NodeSet export/import, and reference servers/clients.
- **New companion-spec coverage**: Part 9 (Alarms & Conditions), Part 11
  (Historical Access) + Part 13 (Aggregates), Part 16 (State Machines),
  Part 17 (Alias Names), Part 18 (Role Management), Part 20 (File Transfer),
  Part 100 (Device Integration), plus OPC 10100-1 WoT Connectivity and a
  Local Discovery Server.
- **Source generators emit NodeManagers, typed `ObjectType` proxies, and
  `IEncodeable` data types from model design XML**, removing hand-written
  boilerplate while staying AOT-clean.
- **GDS is now Part 12 full-compliance**, with arbitrary certificate groups,
  custom group support, and modernised Push/Pull APIs.
- **An MCP server** ships in the box so an LLM/Copilot can drive an OPC UA
  client; and a **2.0 Migration Analyzer + code fixer** automates the
  mechanical parts of upgrading to v2.

## Breaking changes at a glance

2.0 is the first major break of the public API since the project moved to
.NET Standard. The biggest sources of breakage are the readonly-struct
built-ins (`NodeId`, `ExtensionObject`, `Variant`, `DataValue`,
`QualifiedName`, `LocalizedText`, `ArrayOf<T>`), the `Variant`-for-`object`
pivot in code and API, the new `IEncodeableFactoryBuilder` and IType
hierarchy, the removal of Newtonsoft.Json from `Opc.Ua.Core`, the
`ManagedSession` family next to the classic `Session`, and the move to
`AsyncCustomNodeManager` for server extensibility. The
[Migration Guide](MigrationGuide.md) walks every break in detail, with the
companion [2.0 Migration Analyzer](#tooling) handling most of the mechanical
edits automatically.

## Cross-cutting themes

### Type system and immutability

The built-in OPC UA types have been redesigned around immutability and
allocation-light value semantics. `NodeId`, `ExpandedNodeId`,
`QualifiedName`, `LocalizedText`, `Variant`, `ExtensionObject`,
`DataValue`, and `ArrayOf<T>` are now `readonly struct`s; `null` checks are
replaced with `IsNull` / `.Null` to align with `INullable`. A new
`ByteString` is preferred over `byte[]` in public API, and `ArrayOf<T>` /
`MatrixOf<T>` provide first-class array and matrix abstractions. The
`object`-typed public surface has been replaced with `Variant` across the
stack and in source-generated code, eliminating boxing in encoders, decoders,
and node-state read/write paths. Equality on `ExtensionObject`,
`StatusCode`, and numeric ranges has been tightened, and `DateTimeUtc`
makes timestamp intent explicit. See the
[2.0 migration guide — Improved Type Safety](migrate/2.0.x/types.md)
section for the per-type deltas.

### Async, cancellation, and `TimeProvider`

The server now runs fully on the Task-based asynchronous pattern. The new
[`AsyncCustomNodeManager`](AsyncServerSupport.md) is the canonical base
class for NodeManagers and threads `CancellationToken` through every
service call; built-in managers (`CoreNodeManager`, `DiagnosticsNodeManager`,
`ReferenceNodeManager`) have all migrated to it, and `MonitoredNode` is
`IAsyncNodeManager`-aware. A new server-side `RequestLifetime` propagates
the request-scoped cancellation token through the call stack, so a
client-cancelled request short-circuits cleanly. Synchronous-over-async
patterns have been removed from non-obsolete public APIs. The stack also
adopts `System.TimeProvider` for all timing primitives, replacing direct
`DateTime.UtcNow` and `Timer` use; this is what makes server logic
deterministic under test and tolerant of system-clock changes.

### Dependency injection and hosting

The stack now offers a unified
[`Microsoft.Extensions.DependencyInjection`](DependencyInjection.md) surface:
a single `services.AddOpcUa()` returns an `IOpcUaBuilder`, and every feature
library hangs its own `.AddXxx(...)` extension off it. Servers run as
`IHostedService`s under the .NET Generic Host; options bind from
`Action<T>` or `IConfiguration`; identity providers, certificate manager,
secret store, file system, historian, alarms, and the GDS extensions all
register through the same builder. Alongside DI, a
[source-generated fluent server API](SourceGeneratedNodeManagers.md) lets
applications stand up a server from a model design XML with a few
`.AddXxx().WithYyy()` calls; the
[`ManagedSession`](Sessions.md#3-managedsession--the-connection-state-machine-facade)
fluent builder is the equivalent on the client.

### Native AOT

The full stack — Core, Types, Client, Server, ComplexTypes, GDS, PubSub,
and the source generators — is now [Native AOT](NativeAoT.md) friendly.
Public API avoids reflection paths that require trimming suppression, the
source generators emit AOT-clean code, and the reference servers/clients
can be published as self-contained single-file native binaries. UANodeSet
import/export and the encodeable factory have been reworked to function
under AOT, and the test matrix includes AOT smoke tests for each shipped
library.

### Source generators and modeling

The new source-generation pipeline emits the typical OPC UA boilerplate
from model design XML rather than hand-written code. The
[NodeManager generator](SourceGeneratedNodeManagers.md) produces a fully
async, fluent NodeManager skeleton plus typed `*State` properties for every
node; the [DataType generator](SourceGeneratedDataTypes.md) emits
`IEncodeable` implementations from POCO classes; and a new generator emits
**typed method proxies on `ObjectType`**s so callers invoke methods with a
strongly-typed signature rather than a generic `Call` plus variant arrays.
The encodeable factory build path uses an `IEncodeableFactoryBuilder` so
factories can be assembled deterministically and AOT-cleanly; OPC UA
`OptionSet` data types are now backed by generated structures, and
cross-assembly model references are tracked via the
[`ModelDependencyAttribute`](ModelDependencies.md). The generator can also
default a model's instance modelling rules to its type-definition rules,
which the stack now opts into.

### OPC UA companion-spec coverage

This release substantially extends companion-spec coverage with full
server- and client-side implementations:

- **Part 9 — Alarms and Conditions**: full server + client implementation
  with latched / silenced / out-of-service variants, alarm groups and a
  suppression engine, rate metrics, a typed `AlarmClient`, the
  `AlarmEventFilterBuilder`, and `IAsyncEnumerable` alarm streaming. See
  [Alarms and Conditions](AlarmsAndConditions.md).
- **Part 11 — Historical Access** + **Part 13 — Aggregates**: a provider
  model with an in-memory historian and a `HistoryClient` for raw,
  modified, at-time, processed, and annotation reads/updates. All 37
  standard v1.05.07 aggregate functions, with native push-down where
  available and a framework fallback otherwise. See
  [Historical Access](HistoricalAccess.md) and
  [Aggregates](Aggregates.md).
- **Part 16 — State Machines**: a unified fluent
  [`StateMachineBuilder`](StateMachines.md) with both *definition*
  (`FluentFiniteStateMachineState`) and *lifecycle* (attach behaviour to
  stack-shipped or generator-emitted FSMs) modes, plus client-side
  streaming / read helpers on the generated `*TypeClient` proxies.
- **Part 17 — Alias Names**: full server + client support for
  `AliasNameType`, `AliasNameCategoryType`, `FindAlias`, `FindAliasVerbose`,
  `AddAliasesToCategory`, `DeleteAliasesFromCategory`, and `LastChange`.
  See [Alias Names](AliasNames.md).
- **Part 18 — Role Management**: full server-side role administration
  surface, with the server automatically assigning the OPC UA Part 3 §4.9
  `TrustedApplication` role, and a pluggable
  [identity-provider model](IdentityProviders.md) that supports anonymous,
  username, X.509, and token-issuer flows (OAuth2 / OIDC / Entra / JWT).
- **Part 20 — File Transfer**: a server-side FileSystem library, with a
  matching System.IO-style [`FileSystemClient`](FileSystemClient.md) on
  the client.
- **Part 100 — Device Integration**: the `Opc.Ua.Di` / `Opc.Ua.Di.Server` /
  `Opc.Ua.Di.Client` library trio with a fluent `IDeviceBuilder`, device
  sub-type extensions, lock service, software-update package store, and
  client helpers. See [Device Integration](DeviceIntegration.md) and
  [Software Update](SoftwareUpdate.md).
- **OPC 10100-1 — WoT Connectivity**: model, server, and client libraries
  for surfacing OPC UA servers as Web of Things Thing Descriptions. See
  [WoT Connectivity](WoTConnectivity.md).
- **Local Discovery Server**: a built-in LDS implementation usable
  standalone or as part of a hosted server.

Other server-side feature work:
[NodeManagement service set](NodeManagement.md) (`AddNodes`,
`DeleteNodes`, `AddReferences`, `DeleteReferences` with an
`INodeManagementAsyncNodeManager` opt-in and a per-manager
`AllowNodeManagement` gate),
[Model Change Tracking](ModelChangeTracking.md) (server-side
`ModelChangeAggregator` and auto-emitted `GeneralModelChangeEvent`s, with
client-side per-node `INodeCache.InvalidateNode`), and the
[Subscriptions and Monitored Items](Subscriptions.md) service set
(V2 subscription engine, declarative + imperative `SetTriggering` with
N:M support, and `IAsyncEnumerable`-based streaming subscriptions for
state-machine waits and short-lived monitoring).

### Performance, memory, and pooling

The type-system rework eliminates a large class of allocations: every
encode/decode of `NodeId`, `Variant`, `DataValue`, `ExtensionObject`, and
their collections now stays on the stack or in pooled buffers. A new
`IPooledEncodeable` activator pool further reduces GC pressure on hot
encode paths. Server-side, `MonitoredNode2` caches role-permission
validation event-driven (no more per-publish recomputation), publishing
queues use channel-based consumers, and `NodeState.ReadAttributes` has
been optimised. Lifetime bugs that surfaced under load were fixed in
several places: socket and event-handler leaks during server restart,
timer leaks in `ChannelAsyncOperation.EndAsync`, `TcpTransportListener`
resource leakage in `ServerBase.StopAsync`, undeleted subscription
diagnostic nodes, and the abandoned-subscription map migrated from a
locked `List` to a `ConcurrentDictionary`.

### Security and certificates

A new ref-counted [`Certificate`](Certificates.md) wrapper and the
`CertificateManager` segregated-interface design replace the older
`X509Certificate2` exposure: certificates are tracked deterministically,
shared safely, and disposed predictably across stores, channels, and
identity flows. Secure-channel negotiation has been hardened; the client
now auto-detects and force-renegotiates on server certificate rotation,
and application-certificate lookup can fall back from a concrete
`ApplicationCertificateType` to the abstract type when no concrete entry
matches. The `EncryptedSecret` machinery now supports both RSA and ECC and
is bridge-compatible with legacy .NET / OPC UA implementations. The server
ships with [client lockout](RoleBasedUserManagement.md) for failed
authentication attempts, and `SubCA` revocation no longer auto-creates an
empty CRL on the issuing CA.

## By layer

### Server

`AsyncCustomNodeManager` is now the recommended base for all custom
NodeManagers, and every NodeManager shipped with the stack has migrated to
it. `MonitoredNode` is `IAsyncNodeManager`-aware and uses channel-based
queuing under load; events from the server node are dispatched through
multiple channel consumers; role-permission validation is cached
event-driven in `MonitoredNode2`; and the request queue, event handling,
and publishing path have all been hardened with new test coverage. Server
identity is now pluggable end-to-end (anonymous / username / X.509 / token
issuer) and persistent users are loaded through
`IUserDatabase.GetUsers`. Per OPC UA Part 3 §4.9 the server assigns the
`TrustedApplication` role automatically. The
[NodeManagement service set](NodeManagement.md), per-NodeManager `Allow…`
gates, and `ModelChangeAggregator` round out the server extensibility
surface. The server, the audit/redaction APIs, and the publishing path
have been audited for sync-over-async and converted to TAP. See
[Sessions](Sessions.md) for the matching session-/subscription-engine
story.

### Client, sessions, subscriptions

The client now offers two coexisting paths. The
[classic `Session`](Sessions.md#1-session--the-opc-ua-session-primitive)
remains for callers that own session lifecycle, with a fixed
`SessionReconnectHandler` (the infinite-loop on endpoint changes is gone,
and excessive task spawning on connection loss has been eliminated). The
new [`ManagedSession`](Sessions.md#3-managedsession--the-connection-state-machine-facade)
encapsulates the connection state machine, reconnect policy, and pluggable
subscription engine behind a fluent builder; it is the recommended path for
new code. The **V2 subscription engine** runs alongside the classic engine
and now has feature and test parity with it; an opt-in
`SubscriptionRecoveryPolicy` lets servers signal `Good_SubscriptionTransferred`
without surprising the client; sequential publishing no longer freezes;
deadband handling on `MonitoredItem` has been corrected; and the user
token policy used on `Connect` is now re-used during `Reconnect` /
`ReactivateSession`. New client-side features include
[`FileSystemClient`](FileSystemClient.md) (a `System.IO`-style async client
over OPC UA File methods), [`HistoryClient`](HistoricalAccess.md),
[`AlarmClient`](AlarmsAndConditions.md), and source-generated typed
ObjectType proxies. Client-side [NodeSet export](NodeSetExport.md) extracts
a server's address space to NodeSet2 XML, and
[`ModelChangeTracking`](ModelChangeTracking.md) keeps the local
`INodeCache` consistent with server-side model changes.

### PubSub

PubSub gains certificate-based MQTT authentication, considers MQTT
`WriterGroup`s in keep-alive calculations, and stops `MqttPubSubConnection`
gracefully without spurious error logs. See [PubSub](PubSub.md).

### Global Discovery Server

The GDS implementation is now **full OPC UA Part 12 compliance**, including
modern `StartRequestToken` / `FinishRequestToken` flows
([AuthorizationService](AuthorizationService.md)) and the pull/push
[KeyCredentialService](KeyCredentialService.md). The client supports
pushing to arbitrary certificate groups; the server supports custom
certificate groups; SubCAs can be revoked without auto-creating an empty
CRL; the applications-database `QueryServers` pagination has been
corrected; and method-call validation is strict. The full developer guide
is in [GDS](GDS.md).

### Tooling

A new **MCP server** (see [MCP Server](McpServer.md)) exposes OPC UA client
operations as Model Context Protocol tools, so an LLM or Copilot can
browse, read, write, subscribe, and call methods on any OPC UA server. A
**2.0 Migration Analyzer + code fixer** ships as a Roslyn analyser package
(`OPCFoundation.NetStandard.Opc.Ua.MigrationAnalyzer`) that detects the
typical 1.5.378 → 2.0 patterns and applies most of the mechanical edits
automatically; see the [Migration Guide](MigrationGuide.md) for the
opt-in workflow.

### Build, CI, and observability

The build pipeline now runs on a managed DevOps pool with a per-TFM
build/test matrix and parallelized client tests. `Nullable` is enabled
across `Stack/`, the nine `Libraries/`, and the `Applications/` projects;
dispose-analyzers (`CA2000`, `CA2213`) are on and clean. The repository
follows a strict `dotnet format` baseline (whitespace, IDE, RCS) enforced
by the `opc-ua-codestyle-enforcer` agent. Code analysis runs at "preview"
level with "all" mode, package validation is on, and treat-warnings-as-
errors is set repo-wide. On the runtime side,
[Observability](Observability.md) is plumbed through `ITelemetryContext`:
loggers, meters, and activities all hang off the same context object, and
log redaction is wired through the audit APIs. Tests have been
reorganised for faster CI, with several integration suites separated from
unit suites, and code-coverage gates apply to all non-test, non-application
projects.

## Further reading

- [Migration Guide](MigrationGuide.md) — prescriptive, per-API migration
  reference from 1.5.378 to 2.0.
- [Profiles](Profiles.md) — supported OPC UA profiles, facets, and
  security policies in 2.0.
- [Sessions, Reconnection, and Subscription Engines](Sessions.md) —
  architectural overview of `Session` vs `ManagedSession` and the
  classic vs V2 subscription engines.
- [Dependency Injection](DependencyInjection.md),
  [Native AOT](NativeAoT.md),
  [Observability](Observability.md),
  [Source-Generated NodeManagers](SourceGeneratedNodeManagers.md),
  [Source-Generated DataTypes](SourceGeneratedDataTypes.md).
- Companion specs:
  [Alarms and Conditions](AlarmsAndConditions.md),
  [Historical Access](HistoricalAccess.md),
  [Aggregates](Aggregates.md),
  [State Machines](StateMachines.md),
  [Alias Names](AliasNames.md),
  [Device Integration](DeviceIntegration.md),
  [Software Update](SoftwareUpdate.md),
  [WoT Connectivity](WoTConnectivity.md),
  [Subscriptions and Monitored Items](Subscriptions.md),
  [Node Management](NodeManagement.md),
  [Model Change Tracking](ModelChangeTracking.md),
  [Model Dependencies](ModelDependencies.md).
- Security, identity, and certificates:
  [Certificates](Certificates.md),
  [Certificate Manager](CertificateManager.md),
  [ECC Profiles](EccProfiles.md),
  [Role-Based User Management](RoleBasedUserManagement.md),
  [Identity Providers](IdentityProviders.md),
  [Authorization Service](AuthorizationService.md),
  [Key Credential Service](KeyCredentialService.md),
  [GDS Developer Guide](GDS.md).
- Client features:
  [File System Client](FileSystemClient.md),
  [NodeSet Export](NodeSetExport.md),
  [Complex Types](ComplexTypes.md),
  [Transfer Subscription](TransferSubscription.md),
  [Reverse Connect](ReverseConnect.md),
  [Durable Subscription](DurableSubscription.md).
- Tooling: [MCP Server](McpServer.md),
  [Container Reference Server](ContainerReferenceServer.md),
  [Provisioning Mode](ProvisioningMode.md).
- PubSub: [PubSub library](PubSub.md).
