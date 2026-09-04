# Historical Access (OPC UA Part 11)

## Overview

The .NET Standard stack implements OPC UA Part 11 Historical Access end-to-end via a clean **provider model** that lets you back historizing variables with any time-series storage engine. A reference-quality **in-memory engine** ships in `Opc.Ua.Server` so getting started requires no extra packages.

This document is split into:

- **[Architecture](#architecture)** and **[Scope](#scope)** — what's implemented and how it fits together.
- **[Server developer guide](#server-developer-guide)** — quick start, fluent builder, the registry, per-NodeManager wiring, annotations, configuration node.
- **[Provider author guide](#provider-author-guide)** — interface-by-interface contract for writing a custom historian, including resume tokens, pagination, status codes, error semantics, thread-safety.
- **[Client developer guide](#client-developer-guide)** — `HistoryClient` usage patterns.
- **[Capability discovery](#capability-discovery)** and **[Auditing](#auditing)**.
- **[Limitations and roadmap](#limitations-and-roadmap)**.

## Architecture

```
┌───────────────────────────────────────────────────────────────┐
│                      OPC UA Client                            │
│                                                               │
│  Opc.Ua.Client.Historian.HistoryClient                        │
│    ├ ReadRawAsync / ReadModifiedAsync     (IAsyncEnumerable)  │
│    ├ ReadAtTimeAsync / ReadProcessedAsync (IAsyncEnumerable)  │
│    ├ ReadAnnotationsAsync / ReadEventsAsync                   │
│    ├ InsertAsync / ReplaceAsync / UpdateAsync                 │
│    ├ InsertEventsAsync / ReplaceEventsAsync                   │
│    ├ UpdateEventsAsync / DeleteEventsAsync                    │
│    ├ DeleteRawAsync / DeleteAtTimeAsync                       │
│    ├ UpdateStructureDataAsync / WriteAnnotationsAsync         │
│    ├ GetServerCapabilitiesAsync / GetConfigurationAsync       │
│    └ session.Historian() extension                            │
└──────────┬────────────────────────────────────────────────────┘
           │  HistoryRead / HistoryUpdate (binary or HTTPS)
           ▼
┌───────────────────────────────────────────────────────────────┐
│                      OPC UA Server                            │
│                                                               │
│  Opc.Ua.Server.AsyncCustomNodeManager / CustomNodeManager2    │
│    HistoryReadAsync / HistoryUpdateAsync                      │
│      ↓ validates + dispatches by detail type                  │
│  Opc.Ua.Server.Historian.HistorianDispatcher                  │
│    ├ resolves provider per node                               │
│    │   (per-NM override → registry → fallback)                │
│    └ reports audit events on update                           │
│  Opc.Ua.Server.Historian.IHistorianProvider                   │
│    ├ IHistorianDataProvider          (raw + Insert/Replace…)  │
│    ├ IHistorianModifiedProvider                               │
│    ├ IHistorianAtTimeProvider             (optional)          │
│    ├ IHistorianProcessedProvider          (optional)          │
│    ├ IHistorianAnnotationProvider                             │
│    ├ IHistorianEventProvider                                  │
│    └ IHistorianTransactionalProvider      (optional, atomic)  │
│      │                                                        │
│      ↓ provider implementations:                              │
│   InMemoryHistorianProvider, your tsdb adapter, …             │
└───────────────────────────────────────────────────────────────┘
```

## Scope

This release ships the following Part 11 capabilities:

| Feature                                | Status     |
| -------------------------------------- | ---------- |
| Read raw history                       | ✅ Shipped |
| Read modified history                  | ✅ Shipped — `HistoryClient.ReadModifiedAsync` returns each `DataValue` with its `ModificationInfo`. |
| Read processed (aggregates)            | ✅ Shipped — the server rejects aggregate identifiers not registered with `AggregateManager` before provider execution, then uses native provider push-down or the streaming calculator fallback. All 37 Part 13 v1.05.07 functions; see the [Aggregates (Part 13)](Aggregates.md) guide. |
| Read at-time                           | ✅ Shipped via interpolation fallback or provider push-down |
| Insert / Replace / Update raw values   | ✅ Shipped — per-value best-effort by default, atomic via `IHistorianTransactionalProvider` |
| Delete raw / Delete at-time            | ✅ Shipped |
| Annotations (read / write / delete)    | ✅ Shipped — server dispatcher routes the `Annotations` Property to the parent variable's `IHistorianAnnotationProvider`. Fluent `Historize(...)` auto-creates the Annotations property and sets its access-level bits when the provider advertises `InsertAnnotation`. Client supports single and batched writes/removes through `WriteAnnotationAsync`, `WriteAnnotationsAsync`, and `UpdateStructureDataAsync`. |
| `HistoryServerCapabilities` population | ✅ Shipped (union of registered providers). `AggregateFunctions` folder populated by `AggregateManager.RegisterFactoryAsync` → `DiagnosticsNodeManager.AddAggregateFunctionAsync`. |
| Event history (read / write / delete)  | ✅ Shipped — `IHistorianEventProvider`, queued automatic capture after both synchronous and asynchronous live event forwarding, dispatcher event paths, `InMemoryHistorianProvider` event store, and full `HistoryClient` read/insert/replace/update/delete APIs. Select clauses use complete type/attribute/path/range identity and `WhereClause` is evaluated server-side. |
| `HistoricalDataConfigurationType`      | ✅ Shipped — install asynchronously with `await HistorianBuilder.HistorizeAsync(variable, systemContext, ...)`. |
| Fluent registration                    | ✅ Shipped — `server.UseHistorian().UseInMemoryProvider().RegisterAsDefault()`. |
| Audit events for HistoryUpdate         | ✅ Shipped — dispatcher reports value, annotation, raw-delete, at-time-delete, event-update, and event-delete audit events using provider-returned old values. |

## Server developer guide

### Quick start: in-memory provider

```csharp
using Opc.Ua.Server;
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Historian.InMemory;

// inside your NodeManager (subclass of AsyncCustomNodeManager):
private InMemoryHistorianProvider _historian = new();

public MyNodeManager(IServerInternal server, ApplicationConfiguration configuration, string ns)
    : base(server, configuration, ns)
{
    // ...standard setup...
}

protected override IHistorianProvider? GetHistorianProvider(NodeState node)
{
    return _historian;
}

// Mark a variable as historizing and (optionally) seed initial samples:
var temperature = new BaseDataVariableState<double>(parent)
{
    NodeId = new NodeId("Temperature", NamespaceIndex),
    BrowseName = "Temperature",
    DataType = DataTypeIds.Double,
    Historizing = true,
    AccessLevel = (byte)(AccessLevels.CurrentRead
                        | AccessLevels.HistoryRead
                        | AccessLevels.HistoryWrite),
};
_historian.Register(temperature.NodeId);
```

The bundled provider retains one hour of raw data per variable by default. Configure a different source-timestamp
window, combine it with a hard sample cap, or explicitly restore process-lifetime retention:

```csharp
var bounded = new InMemoryHistorianProvider(new InMemoryHistorianOptions
{
    RawDataRetentionPeriod = TimeSpan.FromHours(24),
    MaxSamplesPerNode = 100_000
});

var unbounded = new InMemoryHistorianProvider(new InMemoryHistorianOptions
{
    RawDataRetentionPeriod = TimeSpan.Zero
});
```

When both limits are set, the provider enforces both and the stricter limit wins. The retention window is measured from
the newest source timestamp observed for that node, so deterministic replay and backfill do not depend on server wall
clock time.

### Fluent builder (`server.UseHistorian()…`)

For the most common case — one provider per server, default fallback — use the fluent builder. It rolls up provider registration, per-variable `Historizing`/access-level flags, optional `Annotations` property creation, and asynchronous `HistoricalDataConfigurationType` installation:

```csharp
using Opc.Ua.Server.Historian;
using Opc.Ua.Server.Historian.InMemory;

// inside your NodeManager.CreateAddressSpace:
HistorianBuilder historian = Server
    .UseHistorian()
    .UseInMemoryProvider()          // or .UseProvider(yourProvider)
    .RegisterAsDefault();           // make the provider the server-wide fallback

await historian.HistorizeAsync(
    temperature,
    SystemContext,
    historyAccessLevel: AccessLevels.HistoryRead | AccessLevels.HistoryWrite,
    capabilities: HistorianNodeCapabilities.ReadWrite);

historian.Historize(
    pressure,
    systemContext: SystemContext,
    capabilities: HistorianNodeCapabilities.ReadWrite);
```

`HistorianBuilder` ships three explicit registration scopes — pick the one that matches the scope of your storage backend:

| Method | Binds | Use when |
| --- | --- | --- |
| `RegisterAsDefault()` | All historizing nodes not covered by a more specific binding. | One global store for everything. |
| `RegisterForNamespace("urn:my-app:vars")` | Every node whose NodeId namespace matches. | Per-tenant or per-domain stores. |
| `RegisterForNode(nodeId)` | Single node. | Specialised stores for high-frequency signals. |

Resolution precedence is deterministic — see [Provider resolution order](#provider-resolution-order) below.

### Fluent server API integration (`builder.Variable<T>(...).Historize()`)

The historian surface is also reachable from the standard `Opc.Ua.Server.Fluent` builder used by source-generated node managers — historization fits in the same `Configure(INodeManagerBuilder)` chain as `OnRead` / `OnWrite` / `OnCall` / `Publish<TEvent>`:

```csharp
using Opc.Ua.Server.Fluent;       // exposes UseHistorian/Historize/WithHistorian
using Opc.Ua.Server.Historian;

[NodeManager(NamespaceUri = "http://example.com/Plant/")]
public partial class PlantNodeManager
{
    partial void Configure(INodeManagerBuilder builder)
    {
        // Provision once: in-memory engine, registered as the default.
        HistorianBuilder hist = builder.UseHistorian();
        hist.UseInMemory();
        hist.RegisterAsDefault();

        // Variable-typed Historize() — most common form.
        builder.Variable<double>("Temperature")
               .OnRead(GetTemperature)
               .Historize();
    }
}
```

The fluent surface ships three entry points (extension methods on the standard builder interfaces):

| Method | Use case |
| --- | --- |
| `builder.UseHistorian()` | Returns a per-manager `HistorianBuilder` cached in a `ConditionalWeakTable` keyed by the `INodeManagerBuilder`. Subsequent `Historize()` calls inherit this default. Calling it more than once on the same manager returns the same instance. |
| `variable.Historize(...)` | Opts the variable in to historization. Without a prior `UseHistorian()` call (and without a per-call `provider` argument), the call lazily creates an in-memory provider and registers it as the server-wide default — the "just works" path. Returns the same builder so the chain can continue. |
| `variable.WithHistorian(provider)` | Binds a specific `IHistorianProvider` to the variable's `NodeId`. Per-node bindings take precedence over the default, so this is the right knob for per-signal storage routing. |

Per-call overrides on `Historize(...)`:

```csharp
// Per-call provider — bypasses the cached default for this one variable.
builder.Variable<int>("AuditLog")
       .OnRead(GetAuditValue)
       .Historize(provider: mySqliteProvider);

// Per-call capabilities — the same HistorianNodeCapabilities POCO the
// HistorianBuilder uses; propagated to the provider's GetCapabilitiesAsync.
builder.Variable<double>("ReadOnlySensor")
       .OnRead(GetReading)
       .Historize(capabilities: new HistorianNodeCapabilities
       {
           InsertData = false, ReplaceData = false,
           DeleteRaw = false, DeleteAtTime = false,
       });

// The archive owns the live Historizing state. Register history access and
// provider routing without overwriting the current attribute value.
builder.Variable<double>("ArchiveControlled")
       .Historize(
           historizing: null,
           autoCapture: false);
```

The nullable `historizing` argument controls only the OPC UA
`BaseVariableState.Historizing` attribute:

| Value | Behavior |
| --- | --- |
| `true` (default) | Sets `Historizing = true`, preserving existing fluent behavior. |
| `false` | Explicitly sets `Historizing = false`. |
| `null` | Leaves the current provider- or application-owned value unchanged. |

Provider registration, requested `HistoryRead` / `HistoryWrite` access bits,
historical configuration installation, and automatic capture are independent
from this attribute choice.

Source-generated typed traversal works the same way — the trailing `.Historize()` attaches to any `IVariableBuilder<T>`:

```csharp
partial void Configure(IBoilerNodeManagerBuilder builder)
{
    builder.Boilers.Boiler__1.LCX001.Measurement
           .OnRead(GenerateLevelControlMeasurement)
           .Historize();
}
```

No `GetHistorianProvider(...)` override is required on the manager — the fluent path writes into the server-wide `IHistorianProviderRegistry` so the dispatcher's normal precedence (per-NM override → registry NodeId → namespace → default) picks the provider automatically.

### Per-NodeManager `GetHistorianProvider` override

Both `AsyncCustomNodeManager` and `CustomNodeManager2` expose a virtual hook that lets a node manager veto / customise provider resolution for nodes it owns:

```csharp
protected override IHistorianProvider? GetHistorianProvider(NodeState node)
{
    if (node.NodeId.NamespaceIndex == NamespaceIndex)
    {
        return _historian;          // my variables go to my historian
    }
    return null;                    // fall through to the server-wide registry
}
```

Returning `null` lets the dispatcher fall through to the server-wide registry; returning non-null short-circuits the registry lookup for that node.

### Server-wide registry (`IHistorianRegistryProvider`)

`ServerInternalData` exposes the historian registry via the optional side-door interface `IHistorianRegistryProvider`:

```csharp
if (server is IHistorianRegistryProvider registryProvider)
{
    IHistorianProviderRegistry registry = registryProvider.HistorianRegistry;
    registry.RegisterDefault(_historian);
    registry.RegisterForNamespace("urn:my-app:vars", _vendorTsdb);
    registry.RegisterForNode(myNodeId, _archive);

    // Unregister is supported too:
    registry.UnregisterForNamespace("urn:my-app:vars");
    registry.UnregisterForNode(myNodeId);
    registry.ClearDefault();
}
```

The interface lives next to the historian types in `Opc.Ua.Server.Historian` so that mocks of `IServerInternal` don't need to implement it; production servers built on `ServerInternalData` always satisfy the check.

### Provider resolution order

For every `HistoryRead` / `HistoryUpdate` operation the dispatcher resolves the provider in this order:

1. **Per-NodeManager override** — `GetHistorianProvider(node)` is called first. Non-null wins.
2. **Server-wide registry** — searched in `RegisterForNode → RegisterForNamespace → RegisterDefault` order.
3. **Fallback** — if all of the above return `null`, the dispatcher returns `BadHistoryOperationUnsupported`.

### Startup advertisement reconciliation

At server startup, `AsyncCustomNodeManager` reconciles every variable
that advertises history (`Historizing`, `AccessLevels.HistoryRead`, or
`AccessLevels.HistoryWrite`) with the provider resolution order above.
If no `IHistorianProvider` is wired for a variable, the server clears
`Historizing` and masks the history bits from `AccessLevel`,
`UserAccessLevel`, `AccessLevelEx`, and the attribute read callbacks.

This is intentional for companion NodeSets: many official models mark
variables as historizing because the model permits history, but a
particular server instance may not archive those variables. Variables
configured with `.Historize(...)`, `WithHistorian(...)`, a namespace or
default historian registration, or a node-manager
`GetHistorianProvider(...)` override keep their advertised history
surface. See [Server address-space metadata](NodeManagers.md#server-address-space-metadata)
for the startup sequence.

### Annotations

The historian framework natively understands the OPC UA convention that annotations live on the `Annotations` property of a historizing variable (`HasProperty` reference, `BrowseName = "Annotations"`, `DataType = Annotation`, `ValueRank = OneDimension`). Clients address the property NodeId, the framework translates property → parent variable NodeId before calling `IHistorianAnnotationProvider`, so providers only ever see the variable NodeId.

The fluent builder auto-creates the property when the supplied capabilities advertise `InsertAnnotation = true`:

```csharp
.Historize(
    temperature,
    systemContext: SystemContext,
    capabilities: new HistorianNodeCapabilities { InsertAnnotation = true, /* … */ })
```

When the capability is set but the property already exists (e.g. from a nodeset XML), the builder reuses it and only adjusts access-level bits.

### `HistoricalDataConfigurationType` companion object

The Part 11 §5.2.3 companion object is installed asynchronously:

| Mode | Argument | Behavior |
| --- | --- | --- |
| Installed | `await builder.HistorizeAsync(variable, context, ...)` | Awaits provider capability discovery and installs the companion before returning. |
| Off | `builder.Historize(variable, ...)` | No companion object. Clients see only the historical value. |

The companion's property values are populated from `HistorianNodeCapabilities.Stepped / Definition / MaxTimeInterval / MinTimeInterval / ExceptionDeviation / StartOfArchive` — populate these on the capability set you pass to `Historize(...)`.

### Aggregate-filtered monitored items

`AsyncCustomNodeManager` resolves the node's historian when it validates an
`AggregateFilter`. When a historian resolves, the revised processing interval
honors the monitored-item sampling interval, the server-wide minimum, and the
provider's
`MinTimeInterval` (or `MaxTimeInterval` when no minimum is supplied).
`ServerAggregateFilter.Stepped` and server-default
`AggregateConfiguration` values come from the same per-node
`HistorianNodeCapabilities`. The returned `AggregateFilterResult` contains
these revised values. If the queue window requires advancing `StartTime`, it is
clamped directly to the earliest time retained by the queue.

Without a resolved historian, the existing aggregate revision behavior is
preserved: the processing interval is at least the monitored-item sampling
interval and the start time is clamped directly to the retained queue window.

When the revised aggregate `StartTime` is in the past and the provider
implements `IHistorianDataProvider`, the async node-manager path pages raw
history from `StartTime` to one captured server time and queues it
oldest-to-newest before live delivery begins. Live values arriving while the
provider is being read are buffered and delivered after the historical window;
the current value is never queued ahead of that history. Future start times,
non-Value attributes, and nodes without a raw historian retain the normal
current-value initialization. Provider read failures queue a visible error
notification and leave the monitored item active for later live values rather
than silently falling back. A live-value buffer overflow fails creation because
the history-to-live ordering can no longer be guaranteed. Required priming-error
notifications remain protected across queue resizing and durable subscription
restore.

Custom monitored-item implementations can preserve the same ordering by
implementing `IInitialValueMonitoredItem`.

The synchronous `CustomNodeManager` path retains its synchronous aggregate
revision and current-value initialization behavior; it does not block on
asynchronous provider capabilities.

### `ServerSystemContext.Server`

`ServerSystemContext` now exposes the underlying `IServerInternal Server` property, populated by every ctor. Use it instead of `SystemHandle as IServerInternal` — node managers freely set `SystemHandle` for their own per-manager state, so the cast is essentially never the server in production.

The historian dispatcher relies on `systemContext.Server` for:

- `AggregateManager` — used by the streaming processed-read fallback and aggregate defaults.
- `Telemetry` — used by audit-event reporting and the dispatcher's diagnostic logger.
- `NamespaceUris` / `TypeTree` — used by event `WhereClause` evaluation (when the provider doesn't push it down).
- `IAuditEventServer` — `IServerInternal : IAuditEventServer`, so the same reference doubles as the audit sink.

## Provider author guide

A provider is a class that implements `IHistorianProvider` (the umbrella) plus any subset of the seven narrow capability interfaces. The dispatcher type-tests the concrete provider at runtime and routes calls accordingly — there is no central registration of capabilities, no virtual base class with thirty `NotImplementedException`s, and no factory.

### Recommended base class

`HistorianProviderBase` is a tiny abstract base that pre-implements `IHistorianProvider` with sensible defaults (`IsHistorizingAsync` → `true`, `GetCapabilitiesAsync` → `HistorianNodeCapabilities.ReadOnly`) and exposes one helper:

```csharp
public abstract class HistorianProviderBase : IHistorianProvider
{
    public virtual ValueTask<bool> IsHistorizingAsync(NodeId nodeId, CancellationToken ct);
    public virtual ValueTask<HistorianNodeCapabilities> GetCapabilitiesAsync(NodeId nodeId, CancellationToken ct);

    protected static ArrayOf<StatusCode> RepeatStatus(StatusCode code, int count);
}
```

Inherit from it and **add** the capability interfaces you support:

```csharp
public sealed class MyTsdbProvider :
    HistorianProviderBase,
    IHistorianDataProvider,
    IHistorianModifiedProvider,
    IHistorianAnnotationProvider
{
    public override ValueTask<HistorianNodeCapabilities> GetCapabilitiesAsync(
        NodeId nodeId, CancellationToken ct)
        => new(new HistorianNodeCapabilities
        {
            InsertData = true,
            ReplaceData = true,
            UpdateData = true,
            DeleteRaw = true,
            DeleteAtTime = true,
            InsertAnnotation = true,
            ServerTimestampSupported = true,
            // Stepped / Definition / MaxTimeInterval / … are used to populate
            // HistoricalDataConfigurationType when installed.
        });

    // … capability methods …
}
```

### Capability interfaces

| Interface | Required for | Notes |
| --- | --- | --- |
| `IHistorianProvider` | Every provider | Umbrella — `IsHistorizingAsync`, `GetCapabilitiesAsync`. |
| `IHistorianDataProvider` | Raw read + Insert / Replace / Update / DeleteRaw / DeleteAtTime | Core read+write surface. |
| `IHistorianModifiedProvider` | Read modified history (Part 11 §5.2.5) | Stores prior versions of replaced/deleted values plus `ModificationInfo`. |
| `IHistorianAtTimeProvider` | Native at-time reads | Optional. Framework falls back to interpolation over raw reads if absent. |
| `IHistorianProcessedProvider` | Native aggregate push-down | Optional. Framework falls back to streaming through `AggregateManager` if absent. |
| `IHistorianAnnotationProvider` | Annotations | Read / Insert / Replace / Update / Delete annotations keyed by `AnnotationTime`. |
| `IHistorianEventProvider` | Event history | Read / Insert / Replace / Update / Delete events keyed by `EventId`. |
| `IHistorianStructuredDataProvider` | StructuredHistoryData (Part 11 §6.8.3) | Update-only. Entries are keyed by the composite `HistoricalValueKey`; reads go through the raw / modified / at-time interfaces. |
| `IHistorianTransactionalProvider` | Atomic batch updates | Optional. Per-value best-effort is the default. |

Implement only what your backend supports. The dispatcher returns `BadHistoryOperationUnsupported` for operations the resolved provider doesn't implement.

### `HistorianOperationContext`

Every read/write method receives a `HistorianOperationContext`:

```csharp
public sealed class HistorianOperationContext
{
    public ServerSystemContext SystemContext { get; }   // includes Server, NamespaceUris, TypeTable, Telemetry
    public OperationContext   OperationContext { get; } // session, request type, locales
    public NodeState?         Node { get; }             // resolved variable / notifier, may be null
    public ModificationInfo   DefaultModificationInfo { get; }
}
```

`DefaultModificationInfo` is pre-populated with the current UTC time, the calling session's `UserName`, and the appropriate `HistoryUpdateType` (Insert / Replace / Update / Delete) for the call. Stamp it verbatim on modified-history entries unless your backend supplies stronger audit metadata.

`Node` may be `null` — historians that hold data for nodes no longer in the address space (deleted variables, archived devices) must still service read requests for them, so don't deref `Node` unconditionally.

### Read pagination — `HistorianResumeToken` and `HistorianPage<T>`

Read methods return `ValueTask<HistorianPage<T>>`. A page is:

```csharp
public readonly record struct HistorianPage<T>(
    ArrayOf<T> Values,
    HistorianResumeToken NextToken = default)
{
    public bool IsFinal => NextToken.IsEmpty;   // computed
}
```

A resume token is opaque bytes:

```csharp
public readonly record struct HistorianResumeToken(ByteString State)
{
    public bool IsEmpty => State.IsEmpty;
}
```

Pagination rules:

1. On the first page, the framework passes a default (empty) token.
2. On subsequent pages, the framework passes back the exact `NextToken` from the previous page.
3. To indicate "no more pages", return a page with a default (empty) `NextToken`. `IsFinal` becomes true automatically.
4. **Do not** put live resources (cursors, transactions, connections) into a token. The token is serialised to the wire as the OPC UA continuation point and can outlive the originating task. Encode just enough state (typically the next timestamp or a keyset offset) to resume cleanly.

A typical token encoding is the next sample's timestamp:

```csharp
private static HistorianResumeToken EncodeTimestamp(DateTime ts)
{
    var bytes = new byte[8];
    BinaryPrimitives.WriteInt64LittleEndian(bytes, ts.ToBinary());
    return new HistorianResumeToken(ByteString.From(bytes));
}

private static DateTime DecodeTimestamp(HistorianResumeToken token)
    => token.IsEmpty
        ? DateTime.MinValue
        : DateTime.FromBinary(BinaryPrimitives.ReadInt64LittleEndian(token.State.Span));
```

### Update semantics and status codes

All update methods return a `HistorianUpdateOutcome<T>`. Its
`OperationResults` is an `ArrayOf<StatusCode>` with exactly one entry per
input value; `OldValues` carries replaced or deleted values for auditing;
`DiagnosticInfos` is either empty or aligned with `OperationResults`; and
`TransactionRolledBack` identifies an atomic rollback. Providers **never throw
for per-value failures**. Validate inputs and surface the per-value outcome:

| Operation | Per-value success | Per-value expected failures |
| --- | --- | --- |
| `InsertAsync` | `Good` / `GoodEntryInserted` | `BadEntryExists` when the source timestamp is already present. |
| `ReplaceAsync` | `Good` / `GoodEntryReplaced` | `BadNoEntryExists` when no entry exists at the timestamp. |
| `UpdateAsync` (upsert) | `GoodEntryInserted` or `GoodEntryReplaced` | n/a (insert if absent, replace if present). |
| `DeleteRawAsync` | `Good` for the whole range | `BadInvalidArgument` etc. for malformed ranges; do not fail per-sample. Returns a single `StatusCode`, not a list. |
| `DeleteAtTimeAsync` | `Good` per timestamp | `BadNoEntryExists` when the exact timestamp has nothing to delete. |
| Annotation variants | Same patterns, keyed by `AnnotationTime`. | Same status codes. |
| Event variants | Same patterns, keyed by `EventId`. | `BadNoEntryExists` / `BadEntryExists`. |

**`SourceTimestamp` uniqueness rule**: a historizing variable has at most one live value per source timestamp. Replace logs the prior value in modified history; subsequent replaces overwrite the live value but each Replace adds another modification entry.

**`DeleteRaw` interval semantics**: the framework supplies a half-open interval `[startTime, endTime)`. Providers must delete every value whose `SourceTimestamp` falls in that interval. The `isDeleteModified` flag selects whether the modified-history log is cleared instead of the live values.

### Atomic batch updates (`IHistorianTransactionalProvider`)

The default contract is per-value best-effort. If your backend supports atomic batch commits, additionally implement `IHistorianTransactionalProvider`. The dispatcher automatically calls the atomic Insert, Replace, or Update method when the resolved provider implements both interfaces. The atomic contract:

- If every input value is applicable, return one success status per value and commit.
- If any value cannot be applied, return the per-value failure code(s) and roll back the entire batch — the archive is left in its pre-call state.

### Modified history

Implement `IHistorianModifiedProvider` if your backend retains prior versions of replaced/deleted values plus the audit `ModificationInfo`. `ReadModifiedAsync` returns `HistorianPage<ModifiedDataValue>`, where:

```csharp
public readonly record struct ModifiedDataValue(DataValue Value, ModificationInfo Info);
```

`Info.UpdateType` distinguishes replaced (`Replace`) values from deleted (`Delete`) entries; `Info.UserName` and `Info.ModificationTime` come from the original update's `HistorianOperationContext.DefaultModificationInfo`.

### Processed (aggregate) reads

If your backend can compute aggregates server-side (Cassandra / Influx / TimescaleDB downsampling, ksql window functions, etc.) implement `IHistorianProcessedProvider`. Otherwise omit the interface — the framework will:

1. Iterate `IHistorianDataProvider.ReadRawAsync` pages with `ReturnBounds = true`.
2. Stream raw values through the `AggregateManager`'s `IAggregateCalculator`.
3. Buffer the calculator output and emit it page-by-page back to the client (`MaxValuesPerPage = 1000` per buffered page).

### At-time reads

`IHistorianAtTimeProvider` returns exactly one `DataValue` per requested timestamp, in input order. Providers without this interface get a streaming framework fallback that interpolates linearly between numeric raw samples (or returns the nearest bound for `UseSimpleBounds`).

### Annotations

`IHistorianAnnotationProvider` exposes Read/Insert/Replace/Update/Delete. Annotations are keyed by `Annotation.AnnotationTime`. The framework translates the property NodeId → variable NodeId before calling the provider, so the `nodeId` parameter is always the variable.

### Structured history data

`IHistorianStructuredDataProvider` is the update surface for StructuredHistoryData (Part 11 §6.8.3, `UpdateStructureDataDetails`). It is deliberately **update only** — structured entries are read back through `ReadRawAsync`, `ReadModifiedAsync` and the at-time path, so raw and structured history share one read pipeline and one continuation-point format.

The difference to raw history is *identity*. Raw values are unique by `SourceTimestamp`; a structured variable may hold several entries at one instant (for example one `KeyValuePair` per named reading of a capture). Entries are therefore addressed by the composite key:

```csharp
public readonly record struct HistoricalValueKey(DateTimeUtc SourceTimestamp, ByteString UniquenessKey);
```

`UniquenessKey` comes from an `IHistorianStructuredDataKeySelector`, the seam that defines "the same entry" for a structure type:

```csharp
public interface IHistorianStructuredDataKeySelector
{
    ArrayOf<QualifiedName> UniquenessFields { get; }
    bool TryGetUniquenessKey(in DataValue value, out ByteString uniquenessKey);
}
```

Two selectors ship with the stack:

| Selector | Uniqueness | Use for |
| --- | --- | --- |
| `TimestampStructuredDataKeySelector` | `SourceTimestamp` | Default. One entry per timestamp — the raw-history rule (annotations, single-structure archives). |
| `KeyValuePairStructuredDataKeySelector` | `SourceTimestamp` + `Key` | Archives of standard `Opc.Ua.KeyValuePair` values. |

Ordinary raw variables use an empty uniqueness key, so the composite key degenerates to the source timestamp and existing behaviour (including bounds, retention and paging) is unchanged. The in-memory provider opts a node into structured storage with:

```csharp
provider.RegisterStructured(nodeId, KeyValuePairStructuredDataKeySelector.Instance);

await provider.InsertStructuredDataAsync(context, nodeId,
    new[] { temperatureAtT0, pressureAtT0 }, ct);   // same SourceTimestamp, different Key
```

Update semantics (per entry, best effort):

| Operation | Status |
| --- | --- |
| `Insert` on an existing composite key | `BadEntryExists` |
| `Replace` / `Remove` on an absent composite key | `BadNoEntryExists` |
| `Update` | Upsert on the composite key |
| Value that is not the node's structure | `BadTypeMismatch` |

Changing a uniqueness field changes the entry identity: `Replace` then returns `BadNoEntryExists` and the client must `Remove` the old entry and `Insert` the new one. Every replaced, updated and removed version is retained in modified history, so `ReadModifiedAsync` returns the full trail even when several entries share a timestamp. Raw and modified reads page with exclusive composite cursors, so entries that share a timestamp are never lost or duplicated across a page boundary. `DeleteAtTime` removes the complete set of entries stored at a timestamp, and `ReturnBounds` yields the adjacent entry in composite-key order on each side of the window.

### Event history

`IHistorianEventProvider` works on notifier NodeIds. Events are keyed by `HistorianEventRecord.EventId` (within a notifier) and timestamped by `SourceTimestamp`:

```csharp
public sealed record HistorianEventRecord(
    ByteString EventId,
    NodeId EventType,
    DateTimeUtc SourceTimestamp,
    ArrayOf<KeyValuePair<string, Variant>> Fields)
{
    public ArrayOf<KeyValuePair<HistorianEventFieldKey, Variant>>
        QualifiedFields { get; init; }

    public bool TryGetField(string path, out Variant value);
    public bool TryGetQualifiedField(
        HistorianEventFieldKey key,
        out Variant value);
}
```

`QualifiedFields` preserves the select clause's type definition, attribute,
browse path, and index range. `Fields` is a compatibility view keyed by the
slash-separated browse path.

`HistorianNodeCapabilities.EventFields` lists additional fields the historian
can retain, while `MandatoryEventFields` lists additional fields that an
Insert/Update filter must provide. Fields that are invalid for the selected
EventType or cannot be retained are omitted and return `GoodDataIgnored`;
requested operation diagnostics identify their select-clause indexes and
symbolic browse paths.

WhereClause evaluation runs on the framework side via `FilterEvaluator` + the `HistorianEventFilterTarget` adapter, but providers that can push filters down should evaluate `request.Filter.WhereClause` themselves and return only matching events (the framework re-evaluates the clause for correctness, so push-down is purely an optimisation).

Use `HistorizeEventsAsync` after the notifier is part of the address space.
Events reported through either node API are forwarded to the normal live-event
sink first, then snapshotted into a bounded queue so historian I/O cannot
suppress live delivery:

```csharp
await historian.HistorizeEventsAsync(
    notifier,
    SystemContext,
    capabilities: eventCapabilities);

await notifier.ReportEventAsync(SystemContext, eventState, cancellationToken);
```

The capture path always retains the mandatory `BaseEventType` fields and adds
any operands configured through `HistorianNodeCapabilities.EventFields`.

### Per-node capabilities

`GetCapabilitiesAsync(nodeId, ct)` may return different sets per node. Static
presets keep ordinary data, structured data, and event registration truthful:

```csharp
HistorianNodeCapabilities.ReadOnly;            // data reads only
HistorianNodeCapabilities.DataReadWrite;       // ordinary data CRUD
HistorianNodeCapabilities.StructuredReadWrite; // structured CRUD/read paths
HistorianNodeCapabilities.EventReadWrite;      // historical event CRUD
HistorianNodeCapabilities.ReadWrite;            // combined adapter/test preset
```

Always set the `HistoricalDataConfigurationType` advisory fields (`Stepped`, `Definition`, `MaxTimeInterval`, `MinTimeInterval`, `ExceptionDeviation`, `StartOfArchive`, `StartOfOnlineArchive`) on the capability set you return for nodes whose companion object you want the framework to install.

For the server-wide `HistoryServerCapabilities` rollup the dispatcher calls `GetCapabilitiesAsync(NodeId.Null, ...)`. Treat `NodeId.Null` as "default — what does this provider support generally?" — the rollup unions the result across every registered provider.

### Thread safety

Providers are called concurrently from multiple sessions. Concrete expectations:

- **All methods must be safe to call from multiple threads simultaneously.**
- Read methods (`ReadRawAsync`, `ReadModifiedAsync`, …) should never block writes; prefer reader-writer locking or copy-on-read.
- Write methods can serialise per-node if your backend doesn't natively serialise.
- Methods should not block on `lock` for long stretches — prefer `SemaphoreSlim` (or asynchronous primitives) over `lock { ... await ... }` patterns.

The bundled `InMemoryHistorianProvider` uses a single `lock` for simplicity. Production stores should use storage-engine primitives (per-node partitioning, MVCC, etc.) for higher throughput.

### Cancellation

Every method takes a `CancellationToken`. Honour it — long-running queries against a remote store must abort cleanly when the OPC UA session terminates or the client cancels.

### Example: minimal `IHistorianDataProvider` skeleton

```csharp
public sealed class MyTsdbProvider :
    HistorianProviderBase,
    IHistorianDataProvider,
    IHistorianModifiedProvider
{
    public override ValueTask<HistorianNodeCapabilities> GetCapabilitiesAsync(
        NodeId nodeId, CancellationToken ct)
        => new(new HistorianNodeCapabilities
        {
            InsertData = true, ReplaceData = true, UpdateData = true,
            DeleteRaw = true, DeleteAtTime = true,
            ServerTimestampSupported = true,
            Stepped = false,
        });

    public async ValueTask<HistorianPage<HistoricalDataValue>> ReadRawAsync(
        HistorianOperationContext context,
        HistorianRawReadRequest request,
        HistorianResumeToken resumeToken,
        CancellationToken ct)
    {
        DateTime resumeAt = DecodeTimestamp(resumeToken);
        DateTime windowStart = resumeAt > DateTime.MinValue ? resumeAt : request.StartTime;

        // Issue the time-range query against your store, page-sized.
        ArrayOf<DataValue> raw = await _backend.QueryAsync(
            request.NodeId, windowStart, request.EndTime,
            limit: PageSize, isForward: request.IsForward, ct).ConfigureAwait(false);

        var values = new List<HistoricalDataValue>(raw.Count);
        foreach (DataValue dv in raw)
        {
            values.Add(new HistoricalDataValue(dv));
        }

        HistorianResumeToken next = raw.Count == PageSize
            ? EncodeTimestamp(raw[^1].SourceTimestamp)
            : default;
        return new HistorianPage<HistoricalDataValue>(values, next);
    }

    public async ValueTask<HistorianUpdateOutcome<DataValue>> InsertAsync(
        HistorianOperationContext context, NodeId nodeId,
        ArrayOf<DataValue> values, CancellationToken ct)
    {
        var statuses = new StatusCode[values.Count];
        for (int i = 0; i < values.Count; i++)
        {
            DataValue v = values[i];
            statuses[i] = await _backend.TryInsertAsync(nodeId, v, ct).ConfigureAwait(false)
                ? StatusCodes.GoodEntryInserted
                : StatusCodes.BadEntryExists;
        }
        return new HistorianUpdateOutcome<DataValue>(statuses);
    }

    // … Replace / Update / DeleteRaw / DeleteAtTime / ReadModified
}
```

## Client developer guide

### Constructing a `HistoryClient`

```csharp
using Opc.Ua.Client.Historian;

// Idiomatic shortcut:
HistoryClient historian = session.Historian();

// Or explicit:
HistoryClient historian = new HistoryClient(session, new HistoryClientOptions
{
    // optional knobs (default page size, timeout, etc.)
});
```

### Reads — `IAsyncEnumerable<T>` with automatic continuation handling

Every read method returns an `IAsyncEnumerable<…>`. The client transparently follows server-issued continuation points so iteration ends only when the time window is exhausted (or you `break;` out of the loop, which calls back to release the continuation):

```csharp
// Raw read
await foreach (DataValue v in historian.ReadRawAsync(
    nodeId: temperatureNodeId,
    startTime: DateTime.UtcNow.AddHours(-1),
    endTime: DateTime.UtcNow,
    maxValuesPerNode: 500))                 // 0 = let the server decide
{
    Console.WriteLine($"{v.SourceTimestamp:o}  {v.Value}");
}

// Modified history: prior value plus who/when/how it was changed
await foreach (ModifiedHistoryValue modified in historian.ReadModifiedAsync(
    nodeId: temperatureNodeId,
    startTime: DateTime.UtcNow.AddDays(-1),
    endTime: DateTime.UtcNow))
{
    Console.WriteLine(
        $"{modified.Value.SourceTimestamp:o} " +
        $"{modified.Info.UpdateType} by {modified.Info.UserName}");
}

// Processed (aggregate) — 1-minute Average buckets
await foreach (DataValue v in historian.ReadProcessedAsync(
    nodeId: temperatureNodeId,
    aggregateFunctionId: ObjectIds.AggregateFunction_Average,
    startTime: DateTime.UtcNow.AddHours(-24),
    endTime: DateTime.UtcNow,
    processingInterval: 60_000))
{
    Console.WriteLine($"{v.SourceTimestamp:o}  avg={v.Value}");
}

// At-time — one value per requested timestamp
await foreach (DataValue v in historian.ReadAtTimeAsync(
    nodeId: temperatureNodeId,
    times: new[] { t1, t2, t3 })) { /* … */ }

// Annotations on a variable
await foreach (Annotation a in historian.ReadAnnotationsAsync(
    variableId: temperatureNodeId,
    startTime: DateTime.UtcNow.AddDays(-7),
    endTime: DateTime.UtcNow))
{
    Console.WriteLine($"{a.AnnotationTime:o} {a.UserName}: {a.Message}");
}

// Historical events. Each field list follows the select-clause order.
var eventFilter = new EventFilter();
eventFilter.AddSelectClause(
    ObjectTypeIds.BaseEventType,
    BrowseNames.EventId,
    Attributes.Value);
eventFilter.AddSelectClause(
    ObjectTypeIds.BaseEventType,
    BrowseNames.EventType,
    Attributes.Value);
eventFilter.AddSelectClause(
    ObjectTypeIds.BaseEventType,
    BrowseNames.Time,
    Attributes.Value);
eventFilter.AddSelectClause(
    ObjectTypeIds.BaseEventType,
    BrowseNames.Message,
    Attributes.Value);

await foreach (HistoryEventFieldList fields in historian.ReadEventsAsync(
    nodeId: notifierId,
    startTime: DateTime.UtcNow.AddHours(-1),
    endTime: DateTime.UtcNow,
    filter: eventFilter))
{
    fields.EventFields[0].TryGetValue(out ByteString eventId);
    fields.EventFields[3].TryGetValue(out LocalizedText message);
    Console.WriteLine($"{eventId}: {message}");
}
```

### Writes

```csharp
// Insert
await historian.InsertAsync(temperatureNodeId, new[]
{
    new DataValue
    {
        WrappedValue = new Variant(42.0),
        SourceTimestamp = DateTime.UtcNow,
        StatusCode = StatusCodes.Good,
    },
});

// Replace / Update follow the same shape, with the documented BadEntryExists / BadNoEntryExists semantics.

// Delete by range
await historian.DeleteRawAsync(temperatureNodeId, fromUtc, untilUtc);

// Delete by exact timestamps
await historian.DeleteAtTimeAsync(temperatureNodeId, new[] { tsA, tsB });

// Single annotation
await historian.WriteAnnotationAsync(
    variableId: temperatureNodeId,
    annotationTime: DateTime.UtcNow,
    message: "calibration applied",
    userName: "alice");

await historian.DeleteAnnotationAsync(
    variableId: temperatureNodeId,
    annotationTime: when);

// Batched annotations use one UpdateStructureDataDetails service operation.
ArrayOf<Annotation> annotations =
[
    new Annotation
    {
        AnnotationTime = t1,
        Message = "calibration started",
        UserName = "alice"
    },
    new Annotation
    {
        AnnotationTime = t2,
        Message = "calibration completed",
        UserName = "alice"
    }
];
await historian.WriteAnnotationsAsync(temperatureNodeId, annotations);
await historian.WriteAnnotationsAsync(
    temperatureNodeId,
    annotations,
    PerformUpdateType.Remove);

// Event writes use the same filter and field order as event reads.
var historicalEvent = new HistoryEventFieldList
{
    EventFields =
    [
        new Variant(eventId),
        new Variant(ObjectTypeIds.BaseEventType),
        new Variant((DateTimeUtc)DateTime.UtcNow),
        new Variant(new LocalizedText("maintenance started"))
    ]
};
await historian.InsertEventsAsync(notifierId, eventFilter, [historicalEvent]);
await historian.ReplaceEventsAsync(notifierId, eventFilter, [historicalEvent]);
await historian.UpdateEventsAsync(notifierId, eventFilter, [historicalEvent]);
await historian.DeleteEventsAsync(notifierId, [eventId]);
```

`PerformUpdateType.Remove` is valid for `UpdateStructureDataDetails`, including
annotation removal through `UpdateStructureDataAsync` or
`WriteAnnotationsAsync`. It is not valid for `UpdateDataDetails` in Part 11
v1.05.07. Use `DeleteAtTimeAsync` to remove exact raw values and
`DeleteRawAsync` to remove a raw time range.

### Discovery — capabilities and configuration

```csharp
HistoryServerCapabilitiesInfo caps = await historian.GetServerCapabilitiesAsync();
Console.WriteLine($"InsertAnnotation? {caps.InsertAnnotation}");

HistoricalDataConfigurationInfo cfg =
    await historian.GetConfigurationAsync(temperatureNodeId);
if (cfg.HasConfiguration)
{
    Console.WriteLine($"Stepped={cfg.Stepped}, MaxInterval={cfg.MaxTimeInterval}");
}
```

`HasConfiguration = false` simply means the variable does not expose a `HistoricalDataConfigurationType` companion object (typical for read-only archives).

## Automatic value capture

`Historize(...)` is **opt-out**: live updates to a historized variable are automatically captured into the archive. The capture pipeline is driven by `NodeState.StateChanged`, batched through a bounded `System.Threading.Channels.Channel<T>`, and flushed by a single per-historian-builder consumer task.

### Pipeline

```
  variable.Value = sample;
  variable.Timestamp = now;
  variable.ClearChangeMasks(systemContext, false);   ← fires StateChanged(Value)
                            │
                            ▼
   StateChanged handler  (O(1), lock-free)
                            │
                            ▼
   bounded Channel<CaptureEvent>   ← MaxQueuedSamples (default 4096)
                            │
                            ▼
   HistorianCaptureSink consumer task
       • drain up to BatchTarget samples (default 64)
       • or wait BatchWindow (default 25 ms) for more
       • group by NodeId
       • flush:
            provider is IHistorianBulkInsertProvider  → InsertBatchAsync
            else                                      → per-node InsertAsync
       • failed provider outcomes fault the consumer and surface on disposal
```

The capture path is **best-effort under overload**. When the queue is full,
`CaptureFullMode.DropOldest` keeps the freshest data and
`CaptureFullMode.DropNewest` preserves the queued backlog. A synchronous value
callback is never blocked on asynchronous storage. Applications that cannot
drop samples must use the explicit `HistoryUpdate` Insert path or an
application-owned durable queue, which provides awaited persistence and full
per-value status feedback.

### What triggers a capture

Auto-capture observes the **Value** bit of `NodeStateChangeMasks`. It fires when:

- Server code sets `variable.Value` and calls `variable.ClearChangeMasks(ctx, includeChildren: false)` — the standard simulation pattern in `ReferenceNodeManager.DoSimulation`.
- A client writes the variable via the `Write` service (the framework path also walks `ClearChangeMasks`).

It does **not** fire when:

- The variable uses an `OnRead` callback that returns a fresh value to a client read without ever storing it on `variable.Value`. Use the explicit `HistoryUpdate` Insert path, or store the value on the variable before returning.
- Only non-value attributes change (e.g. `DisplayName`).

### Opting out

Disable capture per variable:

```csharp
builder.Variable<double>("ExternalSink")
       .OnRead(GetReading)
       .Historize(autoCapture: false);
```

### Tuning

Pass a `HistorianCaptureOptions` to override the defaults (per builder — the first opt-in call wins; subsequent calls share the same sink):

```csharp
builder.Variable<double>("FastSignal")
       .Historize(captureOptions: new HistorianCaptureOptions
       {
           MaxQueuedSamples = 16_384,
           BatchTarget = 256,
           BatchWindow = TimeSpan.FromMilliseconds(10),
           FullMode = CaptureFullMode.DropOldest,
       });
```

| Knob | Default | Effect |
| --- | --- | --- |
| `MaxQueuedSamples` | 4096 | Queue depth before `FullMode` kicks in. |
| `BatchTarget` | 64 | Sample count per flush. Bigger → fewer provider calls. |
| `BatchWindow` | 25 ms | Max wait for a partial batch. Bigger → fewer flushes; smaller → lower capture latency. |
| `FullMode` | `DropOldest` | `DropOldest` or `DropNewest`. |

### Implementing `IHistorianBulkInsertProvider`

Custom providers should implement `IHistorianBulkInsertProvider` to amortise per-batch overhead (database round-trips, lock acquisition, transaction setup):

```csharp
public sealed class MyTsdbProvider :
    HistorianProviderBase,
    IHistorianDataProvider,
    IHistorianBulkInsertProvider
{
    public ValueTask<ArrayOf<HistorianUpdateOutcome<DataValue>>> InsertBatchAsync(
        HistorianOperationContext context,
        ArrayOf<HistorianDataBatch> batch,
        CancellationToken ct)
    {
        // One transaction, one round-trip — far cheaper than N InsertAsync calls.
        // Apply per-value semantics (BadEntryExists when the SourceTimestamp
        // already exists, GoodEntryInserted on success), then return one
        // HistorianUpdateOutcome in the same position as each input batch entry.
    }

    // ... per-node InsertAsync / Replace / Update fallback ...
}
```

The bundled `InMemoryHistorianProvider` implements the interface and acquires its single lock once per flush instead of once per node.

## Capability discovery

When at least one provider is registered, `Server.ServerCapabilities.HistoryServerCapabilities` is populated as the union of every provider's `GetCapabilitiesAsync(NodeId.Null, …)`. Clients can read this through normal `Read` requests or via `session.Historian().Session.ReadValueAsync(...)`.

The rollup runs both when the capabilities node is freshly created by `DiagnosticsNodeManager` and when it was loaded from the predefined nodeset XML — so a stock server picks up the registered providers' capability set on every startup, without further wiring.

## Auditing

The dispatcher reports the following audit events for successful and failed
`HistoryUpdate` attempts, using `IServerInternal.ReportAuditEvent`:

| Update kind | Audit event type |
| --- | --- |
| Insert/Replace/Update raw values | `AuditHistoryValueUpdateEventType` |
| Insert/Replace/Update annotations | `AuditHistoryAnnotationUpdateEventType` |
| Delete raw (range) | `AuditHistoryRawModifyDeleteEventType` |
| Delete at-time | `AuditHistoryAtTimeDeleteEventType` |
| Event insert/replace/update | `AuditHistoryEventUpdateEventType` |
| Event delete | `AuditHistoryEventDeleteEventType` |

Provider update methods return `HistorianUpdateOutcome<T>`, including the
prior values replaced or deleted by the same atomic storage operation. The
dispatcher uses those values directly, avoiding a racy read-before-write.

## Part 11 profile conformance catalog

`Opc.Ua.HistoricalAccessProfileCatalog` (in `Opc.Ua.Core`) is a machine-readable
inventory of every released UACore 1.05 profile whose name contains
"Historical" — 15 Server facets and 22 Client facets, 37 in total. Each
`HistoricalAccessProfileDescriptor` records the profile's name, URI, side
(`Server`/`Client`), functional family (raw/server-timestamp, modified,
at-time, aggregate, annotation, structured, raw updates, or events), and its
mandatory conformance unit names. `Opc.Ua.HistoricalAggregateFunctionCatalog`
separately maps all 37 standard OPC UA aggregate functions to the optional
conformance units of the two Aggregate profiles.

All 15 Server descriptors are verified for capability-gated advertisement.
`HistorianProfileCatalog` requires the exact provider interface and exact
capability flags for each profile URI; an Insert-only provider cannot claim
Replace, Update, or Delete, and event read does not imply event write. The
ReferenceServer derives its complete `ServerProfileArray` and mandatory
conformance-unit set from this catalog, then adds the optional conformance unit
for each of the 37 aggregate functions it exposes.

Client descriptors remain `IsAdvertised == false` because clients do not write
Server `ServerProfileArray` entries. Their implementation is verified through
the client and integration suites. `HistoricalAccessProfileEvidenceCatalog`
maps every one of the 37 profiles to production modules, automated tests, and
the in-repository reference samples.

## Limitations and roadmap

- **Formal CTT certification is external** — repository tests mirror the
  required conformance units and the ReferenceServer is CTT-ready, but the
  licensed UACTT run is not performed or claimed by this repository.
- **Eventual active/active history is unsupported** — ordered history,
  modification chains, deletes, events, and atomic batches have no CRDT merge
  contract. `UseDistributedHistorian` therefore requires a linearizable
  active/passive topology and fails closed otherwise.
- **Framework aggregate fallback buffers output** — providers without
  `IHistorianProcessedProvider` use the correct bounded framework calculator
  fallback, which buffers at most 100,000 output values. Distributed or
  high-volume providers should implement native processed paging.
- **The in-memory adapter is intentionally ephemeral** — use
  `SharedKeyValueHistorianProvider` for protected, strongly consistent
  active/passive persistence, or implement the provider interfaces for another
  durable backend.
