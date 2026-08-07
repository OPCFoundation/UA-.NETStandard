# Server Runtime Seam Analysis

> **Status: design analysis / proposal — not shipped API.**
> This document records an evidence-based assessment of the server *runtime* cluster in
> `Opc.Ua.Server`: `StandardServer`, `ServerInternalData`, `Session`, `SessionManager`,
> `Subscription`, `SubscriptionManager`, `ApplicationInstance`, and related machinery.
> Nothing here has been implemented. For shipped documentation see
> [Sessions.md](../docs/Sessions.md) and [Subscriptions.md](../docs/Subscriptions.md).

Analysis performed against commit `e73e71184` on `master`.

**Explicitly out of scope:** node managers (`INodeManager`, `AsyncCustomNodeManager`,
`CustomNodeManager2`, `MasterNodeManager`) and node states. These are plugin API and
breaking them is not in play. They are assessed separately in
[NodeManagerAnalysis.md](NodeManagerAnalysis.md).

## Table of contents

- [Purpose](#purpose)
- [Vocabulary](#vocabulary)
- [Method](#method)
- [Surface inventory](#surface-inventory)
- [Finding 1: the diagnostics lock leak](#finding-1-the-diagnostics-lock-leak)
  - [The lock is the guarded public data object](#the-lock-is-the-guarded-public-data-object)
  - [The lock getter has a side effect that runs outside the lock](#the-lock-getter-has-a-side-effect-that-runs-outside-the-lock)
  - [Cross-object lock nesting with no documented order](#cross-object-lock-nesting-with-no-documented-order)
  - [Tests cannot catch any of this](#tests-cannot-catch-any-of-this)
  - [Rules violated](#rules-violated)
  - [The wider inventory — this leak is repo-wide](#the-wider-inventory--this-leak-is-repo-wide)
- [Finding 2: IServerInternal is a service locator](#finding-2-iserverinternal-is-a-service-locator)
- [Finding 3: ISubscription publishes its own state machine](#finding-3-isubscription-publishes-its-own-state-machine)
- [Finding 4: ISubscriptionManager re-declares ISubscription](#finding-4-isubscriptionmanager-re-declares-isubscription)
- [Finding 5: ISession mixes five concerns](#finding-5-isession-mixes-five-concerns)
- [Finding 6: ISessionManager extends through events](#finding-6-isessionmanager-extends-through-events)
- [Finding 7: IApplicationInstance](#finding-7-iapplicationinstance)
- [What is healthy — do not touch](#what-is-healthy--do-not-touch)
- [Deletion tests](#deletion-tests)
- [Dependency categories and testing strategy](#dependency-categories-and-testing-strategy)
- [Ranked recommendations](#ranked-recommendations)
- [Constraints and risks](#constraints-and-risks)
- [Open questions](#open-questions)
- [Reproducing the evidence](#reproducing-the-evidence)
- [See also](#see-also)

## Purpose

The node-manager surface is frozen as plugin API. That leaves the server runtime — the
machinery a plugin author never implements but every request flows through — as the
remaining place where the seams can still be re-cut without breaking consumers.

This document quantifies where that cluster's interfaces are mis-shaped, and separates
the parts that are genuinely well-designed from the parts that are not. Two of the seven
findings recommend *no change*; that is deliberate.

## Vocabulary

The analysis uses deep-module design terms consistently:

| Term | Meaning here |
|---|---|
| **Module** | Anything with an interface and an implementation. |
| **Interface** | Everything a caller must know to use the module correctly: the type signature *plus* invariants, ordering constraints, error modes, and side effects. |
| **Depth** | Leverage at the interface — behaviour obtained per unit of interface learned. |
| **Seam** | The place where a module's interface lives. |
| **Adapter** | A concrete thing satisfying an interface at a seam. |

Applied throughout:

- **The deletion test.** Imagine deleting the module. If complexity vanishes, it was a
  pass-through. If complexity reappears across N callers, it was earning its keep.
- **One adapter means a hypothetical seam. Two adapters means a real one.**

## Method

Evidence gathered mechanically over `src`, `samples`, and `tests`, excluding `obj` and
`bin`. Interface member counts come from language-server document symbols rather than
regular expressions, so interface members without access modifiers are counted correctly.
Lock-statement counts come from a targeted pattern match per file. Commands are listed
under [Reproducing the evidence](#reproducing-the-evidence).

## Surface inventory

| Module | Interface members | Implementation | Assessment |
|---|---:|---|---|
| `IServerInternal` | **57** (34 properties, 23 methods) | `ServerInternalData` — 1,355 lines, 70 public | Service locator |
| `ISubscription` | **42** | `Subscription` — 3,471 lines, 51 public | Publishes its own state machine |
| `ISession` | **37** | `Session` — 1,526 lines, 41 public | Five concerns in one interface |
| `ISubscriptionManager` | **26** (2 events, 24 methods) | `SubscriptionManager` — 2,858 lines, 60 public | Re-declares `ISubscription` |
| `ISessionManager` | **18** (7 events, 11 methods) | `SessionManager` — 1,940 lines, 29 public | Extends through events |
| `IApplicationInstance` | **17** | `ApplicationInstance` — 1,387 lines, 36 public | Three concerns, narrower than its class |
| `IStandardServer` | **9** | `StandardServer` — 5,575 lines, 96 public / 53 protected / 37 virtual | **Healthy** |

## Finding 1: the diagnostics lock leak

An `object`-typed lock is published on **three public interfaces** —
`IServerInternal.DiagnosticsLock`, `IServerInternal.DiagnosticsWriteLock`,
`ISession.DiagnosticsLock`, `ISubscription.DiagnosticsLock`,
`ISubscription.DiagnosticsWriteLock` — and taken in **88 `lock` statements** across
7 files:

| File | `lock` statements |
|---|---:|
| `Server/StandardServer.cs` | 31 |
| `Subscription/Subscription.cs` | 28 |
| `Subscription/SubscriptionManager.cs` | 15 |
| `Session/Session.cs` | 9 |
| `Session/SessionManager.cs` | 2 |
| `NodeManager/CoreNodeManager.cs` | 1 |
| **`samples/ConsoleReferenceServer/UAServer.cs`** | 2 |
| **Total** | **88** |

The sample-code entry matters: this is not an internal detail, it is a lock that external
consumers have been shown how to acquire.

### The lock is the guarded public data object

```csharp
// Subscription.cs:415
public object DiagnosticsLock => Diagnostics;

// Session.cs:332
public object DiagnosticsLock => SessionDiagnostics;
```

`ISubscription.Diagnostics` (a `SubscriptionDiagnosticsDataType`) and
`ISession.SessionDiagnostics` (a `SessionDiagnosticsDataType`) are themselves public
interface members. The lock object and the data it guards are the same reference, and both
are publicly reachable. Any caller can write `lock (subscription.Diagnostics)` — never
touching `DiagnosticsLock` at all — and contend with the server's internal critical
sections.

### The lock getter has a side effect that runs outside the lock

```csharp
// ServerInternalData.cs:656 — and structurally identical in Subscription.cs:420
public object DiagnosticsWriteLock
{
    get
    {
        // implicitly force diagnostics update
        DiagnosticsNodeManager?.ForceDiagnosticsScan();
        return DiagnosticsLock;
    }
}
```

Therefore every one of the 31 `lock (ServerInternal.DiagnosticsWriteLock)` sites in
`StandardServer` performs an unsynchronised write to diagnostics-scan state **before** the
lock is acquired — unsynchronised with respect to the very lock it is about to hand out.

`ForceDiagnosticsScan()` is a single flag assignment
(`DiagnosticsNodeManager.cs:700-703`, `m_forceDiagnosticsScan = true`); the flag is consumed
by the attribute read paths and the periodic scan timer (`:1772`, `:1798`, `:1895-1898`),
which run one fresh scan on demand. The comments at `:1008-1011` and `:1145-1148` show the
flag exists precisely to *avoid* a synchronous O(N²) scan. So the cost on the lock path is a
`bool` write, not a rescan — but it is still a side effect on a property getter, and still a
racy write on a path whose entire purpose is synchronisation.

Nothing in the type signature `object DiagnosticsWriteLock { get; }` reveals this. Under
the definition of *interface* used here, the real interface of this member is
"reading this property marks the diagnostics arrays for a forced rescan, then returns a lock
aliased to the diagnostics data object" — none of which is expressible in, or visible
from, the declaration.

### Cross-object lock nesting with no documented order

`SubscriptionManager` acquires four distinct lock objects, nested, with no stated
hierarchy:

```csharp
lock (m_server.DiagnosticsWriteLock)      // SubscriptionManager.cs:476, 563, 747, 925, 1331
lock (context.Session.DiagnosticsLock)    // 756, 935, 1158, 1261, 1828, 2036, 2101
lock (ownerSession.DiagnosticsLock)       // 1843
lock (subscription.DiagnosticsLock)       // 1498, 1905
```

Several of these are nested inside one another (for example `m_server.DiagnosticsWriteLock`
at 747 enclosing `context.Session.DiagnosticsLock` at 756). No lock-ordering contract is
documented anywhere, which is precisely the situation the repository rule against exposing
locks exists to prevent.

### Tests cannot catch any of this

```csharp
// tests/Opc.Ua.Server.Tests/SubscriptionTests.cs:70, 86
m_serverMock.Setup(s => s.DiagnosticsWriteLock).Returns(new object());
m_sessionMock.Setup(s => s.DiagnosticsLock).Returns(new object());
```

A mocked lock returns an object that nothing else in the system contends on. The tests
therefore exercise **zero mutual exclusion** — and, because the mock replaces the property,
they also never execute the `ForceDiagnosticsScan()` flag write. A diagnostics race or a
lock-ordering deadlock cannot be reproduced by this suite.

### Rules violated

This one design decision breaches three explicit repository rules:

1. *"NEVER expose 'locks' or locking in any internal or public API surface because nobody
   can effectively reason over the lock behavior."*
2. *"NEVER use `private readonly object m_lock = new()` or any other `object`-typed sync
   root. All synchronous locks MUST use `System.Threading.Lock`."*
3. *"NEVER use `object` or `object?` in public API unless overriding `Equals`."*

The `System.Threading.Lock` polyfill already exists at
`src/Opc.Ua.Types/Polyfills/System.Threading.cs:85` and is available on every supported
TFM, so the replacement type is in place today.

**The fix pattern already exists on the same interface.**
`IServerInternal.UpdateServerStatus(Action<ServerStatusValue>)` demonstrates exactly the
right shape: the module owns its lock, the caller passes a delegate, and no sync root
crosses the seam. Applying that shape to diagnostics removes all 88 lock statements, both
side-effecting getters, and all five leaked members.

### The wider inventory — this leak is repo-wide

The diagnostics locks are the largest instance, not the only one. A sweep for `object`-typed
lock members exposed as public or protected properties finds **ten across three assemblies**:

| Assembly | Member | Kind |
|---|---|---|
| `Opc.Ua.Core` | `AsyncResultBase.Lock` (`:144`) | `public object` |
| `Opc.Ua.Core` | `UaSCBinaryChannel.DataLock` (`:1045`) | `protected object` |
| `Opc.Ua.Core` | `ApplicationConfiguration.PropertiesLock => m_properties` (`:104`) | **lock is the guarded data** |
| `Opc.Ua.Types` | `BaseVariableState.Lock` (`:2122`) | `public object` |
| `Opc.Ua.Types` | `NodeBrowser.DataLock` (`:242`) | `protected object` |
| `Opc.Ua.Types` | `Node.DataLock => this` (`:476`) | **lock on `this`** |
| `Opc.Ua.Server` | `ServerInternalData.DiagnosticsLock` (`:649`) | `public object` |
| `Opc.Ua.Server` | `Subscription.DiagnosticsLock => Diagnostics` (`:415`) | **lock is the guarded data** |
| `Opc.Ua.Server` | `Session.DiagnosticsLock => SessionDiagnostics` (`:332`) | **lock is the guarded data** |
| `Opc.Ua.Server` | `CustomNodeManager.Lock` (`:237`) | `public object` |

`Node.DataLock => this` and `ApplicationConfiguration.PropertiesLock => m_properties` are the
same defect class as `Subscription.DiagnosticsLock => Diagnostics`: the published sync root
is the guarded object, so a caller can contend without ever naming the lock member.

The compliant pattern is also already present —
`Opc.Ua.Redundancy.Server/Subscriptions/SharedKeyValueSubscriptionStore.cs:1467`:

```csharp
public Lock Lock { get; } = new();
```

There is a **third** lock leak beyond these ten, documented separately because it is a
published *contract* rather than a member: `NodeState.ReadAttributeAsync` and
`WriteAttributeAsync` take `lock (this)` under a `CA2002`/`RCS1059` suppression whose comment
states *"external callers synchronise via `lock(source)`"*, and **15 call sites honour it** —
6 `lock(this)` inside the node types (`BaseVariableState` 4, `NodeState` 2) and 9 external
`lock(source)`/`lock(node)` statements (`AsyncCustomNodeManager` 8, `CustomNodeManager` 1).
A naive text search reports 19; ten of those matches are the TODO comments and `#pragma`
lines that *mention* `lock(source)` rather than take it. The command below excludes them.
That contract cannot cross a replica and must be removed before any new node representation
exists.

Reproduce the sweep:

```powershell
# the ten object-typed lock members
Get-ChildItem src -Recurse -Filter *.cs -File |
    Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' } |
    Select-String -Pattern '^\s*(public|protected|internal)\s+object\s+\w*[Ll]ock\w*\s*(\{|=>)'

# the 15 lock-contract sites, excluding comment and pragma mentions
$files = Get-ChildItem src -Recurse -Filter *.cs -File |
    Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' }
$sites = @()
foreach ($f in $files) {
    $n = 0
    foreach ($line in [IO.File]::ReadAllLines($f.FullName)) {
        $n++
        if ($line -match 'lock\s*\(\s*(this|source|node|m_node)\s*\)') {
            $trim = $line.TrimStart()
            if (-not ($trim.StartsWith('//') -or $trim.StartsWith('*') -or
                      $trim.StartsWith('#pragma'))) {
                $sites += "$($f.Name):$n  $($line.Trim())"
            }
        }
    }
}
$sites.Count   # 15
$sites
```

## Finding 2: IServerInternal is a service locator

`IServerInternal` has **57 members**: 34 properties and 23 methods.

The 34 properties hand out more than 20 subsystems — `NodeManager`, `CoreNodeManager`,
`DiagnosticsNodeManager`, `ConfigurationNodeManager`, `MainNodeManagerFactory`,
`EventManager`, `ResourceManager`, `RequestManager`, `AggregateManager`,
`ModellingRulesManager`, `ConformanceUnitsManager`, `SessionManager`,
`SubscriptionManager`, `RoleManager`, `IdentityRegistry`, `UserManagement`,
`MonitoredItemQueueFactory`, `SubscriptionStore`, `Telemetry`, `ServerObject`, and more.

Twelve of the 23 methods are `Set*` mutators: `SetNodeManager`,
`SetMainNodeManagerFactory`, `SetSessionManager`, `SetMonitoredItemQueueFactory`,
`SetSubscriptionStore`, `SetRoleManager`, `SetIdentityRegistry`, `SetUserManagement`,
`SetAggregateManager`, `SetModellingRulesManager`, `SetConformanceUnitsManager`, and
`UpdateServerStatus`.

Three consequences:

- **Depth is zero.** The interface *is* the implementation surface. `ServerInternalData`
  is 1,355 lines of largely property storage behind a 57-member interface.
- **It is the universal ambient handle.** `IServerInternal` is referenced across more than
  200 files in `src`, `samples`, and `tests`; `NodeManagerLifecycle.cs` alone references it
  55 times. Anything holding it can reach anything else in the server.
- **Two-phase construction with implicit ordering.** `SetNodeManager` silently reaches into
  its argument to populate three further properties:

  ```csharp
  // ServerInternalData.cs:338
  public void SetNodeManager(IMasterNodeManager nodeManager)
  {
      NodeManager = nodeManager;
      DiagnosticsNodeManager = nodeManager.DiagnosticsNodeManager!;
      ConfigurationNodeManager = nodeManager.ConfigurationNodeManager!;
      CoreNodeManager = nodeManager.CoreNodeManager!;
  }
  ```

  and `SetSessionManager` unhooks previously registered event handlers, so it is designed
  to be called more than once. The required call order is interface complexity documented
  nowhere in the types. Mutable-after-construction global state is also directly hostile to
  the stated high-availability and distributed-server goals (see
  [HighAvailability.md](../docs/HighAvailability.md)).

**However:** `IServerInternal` is what every node manager receives. Narrowing it *is* a
plugin-API break and therefore out of scope. The recommendation is containment, not
re-cutting — see [Ranked recommendations](#ranked-recommendations).

## Finding 3: ISubscription publishes its own state machine

Of `ISubscription`'s 42 members, eleven are collaboration protocol with
`SubscriptionManager` and `SessionPublishQueue` rather than caller-facing API:

`ItemReadyToPublish` · `ItemNotificationsAvailable` · `QueueOverflowHandler` ·
`PublishTimerExpired` · `PublishTimeout` · `SubscriptionTransferred` ·
`AvailableSequenceNumbersForRetransmission` · `Acknowledge` · `ResendData` ·
`SessionClosed()` · `SessionClosed(ISession)`

These sit on the public interface, where any holder of an `ISubscription` — including a
node manager reaching through `IServerInternal.SubscriptionManager.GetSubscriptions()` —
can call `PublishTimerExpired()` or `QueueOverflowHandler()` directly and corrupt the
publishing state machine.

The behaviour behind them is real and substantial: `Subscription` (3,471 lines),
`SessionPublishQueue` (1,071), and `SentMessageQueue` (449) are roughly 5,000 lines of
genuine publishing machinery. This is the strongest *deepenable* candidate in scope —
moving the publish-pipeline protocol to an internal seam takes `ISubscription` from 42 to
roughly 25 members with no behaviour change and no plugin-API impact.

## Finding 4: ISubscriptionManager re-declares ISubscription

Ten of `ISubscriptionManager`'s 26 members restate an `ISubscription` operation with an
extra `uint subscriptionId` parameter: `CreateMonitoredItemsAsync`,
`ModifyMonitoredItemsAsync`, `DeleteMonitoredItemsAsync`, `SetMonitoringModeAsync`,
`SetTriggering`, `SetPublishingMode`, `Republish`, `ConditionRefresh`,
`ConditionRefresh2`, `DeleteSubscriptionAsync`.

Each implementation is a dictionary lookup followed by delegation:

```csharp
// SubscriptionManager.cs:2050
public ValueTask<ModifyMonitoredItemsResponse> ModifyMonitoredItemsAsync(
    OperationContext context,
    uint subscriptionId,
    TimestampsToReturn timestampsToReturn,
    ArrayOf<MonitoredItemModifyRequest> itemsToModify,
    CancellationToken cancellationToken = default)
{
    // find subscription.
    if (!m_subscriptions.TryGetValue(subscriptionId, out ISubscription? subscription))
    {
        throw new ServiceResultException(StatusCodes.BadSubscriptionIdInvalid);
    }

    // modify the items.
    return subscription.ModifyMonitoredItemsAsync(
        context, timestampsToReturn, itemsToModify, cancellationToken);
}
```

Applying the deletion test to these ten members: deleting them moves an
id-to-subscription resolution into the caller. That resolution is genuinely needed —
it is where `BadSubscriptionIdInvalid` is produced — so the members are not pure waste, but
they are a thin routing layer that doubles the surface a reader must learn. A single
`TryGetSubscription`-plus-operate shape (which `ISubscriptionManager.TryGetSubscription`
already provides) would collapse ten members into one.

## Finding 5: ISession mixes five concerns

`ISession`'s 37 members span identity and authentication (8), continuation points (4),
diagnostics (2), lifecycle (5), request validation (3), plus certificates, nonces, locales,
and endpoint description.

Two specific problems:

- **Two more `object` leaks.** `RestoreHistoryContinuationPoint(ByteString) : object?` and
  `SaveHistoryContinuationPoint(Guid, object)` put untyped state in a public interface,
  against the repository rule.
- **Continuation points already have a seam.** `SessionContinuationPoints.cs` (472 lines)
  and `Session/Persistence/IContinuationPointStore.cs` exist. The four continuation-point
  members on `ISession` are a pass-through to a seam that is already in place, so they can
  be routed rather than reimplemented.

- **Paired sync and async variants with divergent signatures.**
  `ValidateBeforeActivateAsync` returns
  `ValueTask<(IUserIdentityTokenHandler, UserTokenPolicy?)>` while the synchronous
  `ValidateBeforeActivate` takes those same two values as trailing parameters. A caller must
  learn both shapes.

## Finding 6: ISessionManager extends through events

Seven of `ISessionManager`'s 18 members are events: `SessionCreated`, `SessionActivated`,
`SessionClosing`, `SessionDiagnosticsChanged`, `SessionChannelKeepAlive`,
`ImpersonateUser`, `ValidateSessionLessRequest`.

The last two are not notifications — they are behavioural hooks. `ImpersonateUser` carries
`ImpersonateEventArgs` with settable `Identity`, `EffectiveIdentity`, and
`IdentityValidationError`, so a handler *decides* the authentication outcome by mutating
event args. `ValidateSessionLessRequest` works the same way through its `Error` property.

Extension through mutable event args is invisible to the type system, unordered when
multiple handlers subscribe, and cannot express failure other than by side effect. The
repository already has the right pattern for this: pluggable identity providers and
`IUserTokenAuthenticator` (see [IdentityProviders.md](../docs/IdentityProviders.md)).
These two events predate that model and should route to it.

## Finding 7: IApplicationInstance

`IApplicationInstance` has 17 members covering three concerns: configuration loading
(`LoadApplicationConfigurationAsync` ×3, `Build`), certificate lifecycle
(`CheckApplicationInstanceCertificatesAsync`, `DeleteApplicationInstanceCertificateAsync`,
`AddOwnCertificateToTrustedStoreAsync`, `CertificatePasswordProvider`,
`DisableCertificateAutoCreation`), and server lifecycle (`StartAsync`, `StopAsync`,
`Server`).

Two observations, both minor:

- **The interface is narrower than the class** (17 versus 36 public members) — the
  certificate-prompt surface (`MessageDlg`, `ApproveMessageAsync`) and `CertificateManager`
  are on `ApplicationInstance` but not on `IApplicationInstance`. That is the right
  direction and needs no change.
- **Return types are inconsistent across overloads of the same method.**
  `LoadApplicationConfigurationAsync(Stream, bool, CancellationToken)` returns `Task<T>`
  while the `(bool, …)` and `(string, bool, …)` overloads return `ValueTask<T>`. Likewise
  `StartAsync` returns `Task` and `StopAsync` returns `ValueTask`. Harmless at runtime,
  but it is interface complexity a caller must memorise.

The certificate half of this class overlaps `CertificateManager` (see
[CertificateManager.md](../docs/CertificateManager.md)), which is the deeper seam, but
`IApplicationInstance` is a widely used entry point and the overlap is not currently
causing duplication of logic. **Third tier; no action recommended.**

For completeness: the synchronous `Stop()` is sync-over-async
(`Server?.StopAsync().AsTask().GetAwaiter().GetResult()`), but it is already marked
`[Obsolete("Use StopAsync")]`, which is the correct handling under the compatibility policy.
No action needed.

## What is healthy — do not touch

`IStandardServer` is **9 members**. `StandardServer` declares 37 `virtual`/`abstract`
members, and its three real (non-test) subclasses — `GlobalDiscoverySampleServer`,
`DependencyInjectionStandardServer`, and `ReverseConnectServer` — override **16** of them:

`CreateMainNodeManagerFactory` · `CreateMasterNodeManagerAsync` ·
`CreateMonitoredItemQueueFactory` · `CreateRoleManager` · `CreateSessionManager` ·
`CreateSubscriptionManager` · `CreateSubscriptionStore` · `Dispose` ·
`LoadServerProperties` · `OnConnectionStatusChanged` · `OnNodeManagerStarted` ·
`OnRequestValidatedAsync` · `OnServerStarted` · `OnServerStarting` ·
`OnServerStoppingAsync` · `OnUpdateConfigurationAsync`

That is a 43% utilisation rate on a clean factory-method-plus-lifecycle-hook shape, with a
9-member interface in front of 5,575 lines of implementation. It is the best-shaped seam in
the cluster and should be left alone. (For contrast, the node-manager base class runs at
25 of 80, with only about a dozen caller-facing — see
[NodeManagerAnalysis.md](NodeManagerAnalysis.md).)

## Deletion tests

| Module | Delete it | Verdict |
|---|---|---|
| The five diagnostics lock members | Nothing reappears — the guarded state is owned by `Subscription` / `Session` / `ServerInternalData` themselves, and `UpdateServerStatus(Action<T>)` already shows the replacement shape | **Pure leak. Remove.** |
| `ISubscription` publish-protocol members (11) | Complexity reappears — but only inside `SubscriptionManager` and `SessionPublishQueue`, which are the only legitimate callers | **Move to an internal seam.** |
| `ISubscriptionManager` routing members (10) | Id-to-subscription resolution moves to callers; `BadSubscriptionIdInvalid` handling would be duplicated | **Thin, but not free. Collapse rather than delete.** |
| `ISession` continuation-point members (4) | Nothing reappears — `SessionContinuationPoints` and `IContinuationPointStore` already hold the behaviour | **Route to the existing seam.** |
| `IServerInternal` | Complexity reappears everywhere; 200+ files depend on it | **Load-bearing, but for the wrong reason.** Contain, do not re-cut. |
| `IStandardServer` / `StandardServer` virtuals | Complexity reappears in all three subclasses | **Earns its keep.** |

## Dependency categories and testing strategy

| Dependency | Category | Consequence |
|---|---|---|
| Diagnostics data structures | **In-process** | Deepenable directly; no port needed |
| Publish pipeline, sent-message queue | **In-process** | Internal seam only |
| Continuation points | Already behind `IContinuationPointStore` | Seam exists |
| Subscription persistence | Already behind `ISubscriptionStore` | Seam exists |
| Monitored item queues | Already behind `IMonitoredItemQueueFactory` | Seam exists |

Every dependency in this cluster is either in-process or already behind a provider seam,
so **no new port is required** for any recommendation below.

Testing should **replace, not layer**. The current suite mocks the diagnostics locks, which
means it verifies neither mutual exclusion nor the `ForceDiagnosticsScan` flag write. Tests
for the replacement should assert observable diagnostics outcomes through the owning
module's update method, so they survive the internal change.

## Ranked recommendations

| # | Work | Plugin-API break? | Payoff |
|---|---|---|---|
| **1** | Replace the five diagnostics lock members with owner-side update methods backed by `System.Threading.Lock` | No — these members are server-internal, not node-manager-facing | Removes 88 lock statements, a real deadlock surface, two side-effecting getters, and three rule violations |
| **2** | Move the 11 publish-pipeline members off `ISubscription` to an internal seam | No | 42 → ~25 members over ~5,000 lines of real behaviour |
| **3** | Route the 4 `ISession` continuation-point members through the existing `IContinuationPointStore`; remove the two `object` leaks | No | 37 → ~33, kills two `object` leaks |
| **4** | Collapse the 10 routing members on `ISubscriptionManager` behind `TryGetSubscription` | No | 26 → ~17 |
| **5** | Route `ImpersonateUser` / `ValidateSessionLessRequest` to `IUserTokenAuthenticator` | Yes, for those two events | Removes mutable-event-args extension |
| **6** | Freeze `IServerInternal`; new subsystems get injected interfaces rather than another property | Deferred | Stops the growth without a break |

Item 1 is the recommended starting point: highest severity, strongest evidence, no plugin
impact, and the replacement pattern already exists on the same interface.

## Constraints and risks

- **Backward compatibility.** The repository requires compatibility with 1.5.378
  (`master378`); replaced API must be marked `[Obsolete]` rather than removed. All
  recommendations are therefore additive in the first pass.
- **`ConsoleReferenceServer` takes the lock.** `samples/ConsoleReferenceServer/UAServer.cs`
  has two `lock (session.DiagnosticsLock)` sites. The sample must migrate to the replacement
  method in the same change, since it is the worked example consumers copy.
- **Tests mock the locks.** `SubscriptionTests.cs` and `SessionSecurityTests.cs` set up
  `DiagnosticsLock` / `DiagnosticsWriteLock` on mocks. Those setups must be removed rather
  than adapted, or they will silently keep passing against a removed concept.
- **`Subscription.DiagnosticsLock => Diagnostics` means the lock and data are one object.**
  Any migration must confirm no external code relies on locking `Diagnostics` directly.
- **`IServerInternal` is reachable from node managers**, so anything reached *through* it
  is indirectly plugin-visible even when the member itself is not on a node-manager
  interface. Each removal in item 1 needs that check.

## Open questions

1. Should diagnostics updates be exposed as `UpdateDiagnostics(Action<T>)` (matching
   `UpdateServerStatus`) or as intention-revealing named methods
   (`RecordRequestCompleted`, `RecordSubscriptionTransferred`, …)? The latter is deeper but
   is a larger change.
2. Does the `ForceDiagnosticsScan()` flag write need to happen at all 88 sites, or is it a
   blanket safety net that a narrower set of explicit invalidation points would replace?
   (Note it is a `bool` assignment, not a scan — see
   [the side-effect section](#the-lock-getter-has-a-side-effect-that-runs-outside-the-lock).)
3. Can the publish-pipeline protocol become an internal interface consumed only by
   `SubscriptionManager` and `SessionPublishQueue`, or does durable-subscription restore
   (see [DurableSubscription.md](../docs/DurableSubscription.md)) require external access?
4. Is there an existing documented lock hierarchy for the four nested diagnostics locks,
   or has ordering been maintained by convention only?
5. Should `ISubscriptionManager`'s routing members be collapsed, or is the flat
   id-addressed surface load-bearing for the service-set dispatch in `StandardServer`?

## Reproducing the evidence

Run from the repository root in PowerShell.

Count the diagnostics lock statements per file:

```powershell
$total = 0
$files = @(
    'src\Opc.Ua.Server\Server\StandardServer.cs',
    'src\Opc.Ua.Server\Subscription\Subscription.cs',
    'src\Opc.Ua.Server\Subscription\SubscriptionManager.cs',
    'src\Opc.Ua.Server\Session\Session.cs',
    'src\Opc.Ua.Server\Session\SessionManager.cs',
    'src\Opc.Ua.Server\NodeManager\CoreNodeManager.cs',
    'samples\ConsoleReferenceServer\UAServer.cs')
foreach ($f in $files) {
    $c = [IO.File]::ReadAllText($f)
    $n = ([regex]::Matches($c, 'lock\s*\([^)]*Diagnostics(Write)?Lock\s*\)')).Count
    $total += $n
    '{0,-40} {1,3}' -f (Split-Path $f -Leaf), $n
}
"TOTAL: $total"
```

Find every declaration and use of the leaked locks:

```powershell
Get-ChildItem 'src','samples','tests' -Recurse -Filter *.cs -File |
    Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' } |
    Select-String -Pattern 'DiagnosticsLock|DiagnosticsWriteLock'
```

Measure the `StandardServer` virtual surface against its three real subclasses:

```powershell
$t = [IO.File]::ReadAllText('src\Opc.Ua.Server\Server\StandardServer.cs')
'virtual/abstract declared: ' +
    ([regex]::Matches($t, '(?m)^        (?:public|protected)(?: internal)? (?:virtual|abstract)(?: async)? ')).Count
$names = @()
foreach ($f in 'src\Opc.Ua.Gds.Server.Common\GlobalDiscoverySampleServer.cs',
               'src\Opc.Ua.Server\Hosting\DependencyInjectionStandardServer.cs',
               'src\Opc.Ua.Server\Server\ReverseConnectServer.cs') {
    $c = [IO.File]::ReadAllText($f)
    foreach ($m in [regex]::Matches($c, '\boverride\s+[\w<>\?\[\]\.,\s]{0,80}?(\w+)\s*[\(\{]')) {
        $names += $m.Groups[1].Value
    }
}
'distinct overrides: ' + ($names | Sort-Object -Unique).Count
```

Interface member counts were taken from language-server document symbols on
`IServerInternal.cs`, `ISession.cs`, `ISubscription.cs`, `ISessionManager.cs`,
`ISubscriptionManager.cs`, `IStandardServer.cs`, and `IApplicationInstance.cs` rather than
by pattern matching, because interface members carry no access modifier.

## See also

- [Node Manager Seam Analysis](NodeManagerAnalysis.md) — the sibling analysis for the
  plugin-API surface deliberately excluded here.
- [Sessions, Reconnection, and Subscription Engines](../docs/Sessions.md) — shipped
  architectural overview.
- [Subscriptions and Monitored Items Service Set](../docs/Subscriptions.md) — the V2
  subscription engine.
- [Diagnostics](../docs/Diagnostics.md) — server diagnostics nodes and audit events, the
  data guarded by the locks in Finding 1.
- [Identity Providers](../docs/IdentityProviders.md) — the pluggable authentication model
  that Finding 6 recommends routing to.
- [High Availability and OPC UA Redundancy](../docs/HighAvailability.md) — the distributed
  goals that Finding 2 works against.
- [Dependency Injection](../docs/DependencyInjection.md) — the injection surface referenced
  by recommendation 6.
- [Developer Guide](../docs/DeveloperGuide.md) — coding standards cited in Finding 1.
