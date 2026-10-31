# Runtime NodeSets

This guide explains how to import one or more NodeSet2 XML documents into a
server's address space at startup, without writing a source-generated or
hand-coded NodeManager. Configure the files or streams to load. The server
imports them in dependency order and registers the resulting nodes.

Within each imported set, types are registered in inheritance order rather than
XML record order. A derived type may precede its supertype in the document;
an inheritance cycle is rejected before registration. Imported namespace-zero
`InputArguments` and `OutputArguments` Properties populate their Method's typed
signature, retaining the declared Property NodeIds, argument order, and values.

## Contents

- [When to use the runtime NodeSet path](#when-to-use-the-runtime-nodeset-path)
- [Imported security policy](#imported-security-policy)
- [Startup and live lifecycle semantics](#startup-and-live-lifecycle-semantics)
  - [Shadow reload](#shadow-reload)
  - [Immediate reload](#immediate-reload)
- [Quick-start examples](#quick-start-examples)
  - [Single file](#single-file)
  - [Single file with a fluent callback](#single-file-with-a-fluent-callback)
  - [Group of dependent NodeSets](#group-of-dependent-nodesets)
  - [Custom stream source](#custom-stream-source)
  - [Direct factory registration](#direct-factory-registration)
- [Stream ownership contract](#stream-ownership-contract)
- [Default namespace for unqualified browse paths](#default-namespace-for-unqualified-browse-paths)
- [Dependency sorting](#dependency-sorting)
  - [Referencing a Node another NodeManager owns](#referencing-a-node-another-nodemanager-owns)
- [Complex types](#complex-types)
- [Comparison with source-generated NodeManagers](#comparison-with-source-generated-nodemanagers)
- [Related documentation](#related-documentation)

## When to use the runtime NodeSet path

Use `AddRuntimeNodeSet` when:

- You receive a NodeSet2 XML from a companion-specification vendor and want to host its nodes without regenerating source.
- Your information model changes frequently enough that rebuilding the source-generated manager for every XML update would be disruptive.
- You are prototyping or testing a new NodeSet2 design.

Use the [source-generated path](NodeManagers.md#source-generated-node-managers) when you want compile-time safety, strong typing, and AOT-safe named constants for every node in your model. The runtime path gives you generic `NodeState` objects and untyped browse-path wiring.

## Imported security policy

Runtime import retains node-local `RolePermissions` and `AccessRestrictions`.
When a node omits either attribute, import applies the corresponding default
from its owning `Models/Model` entry. Ownership follows the node's namespace
URI, not its BrowseName, the order of model entries, or the server's namespace
index. Defaults are materialized before the nodes enter the address space, so
ordinary Read, Write, Call and subscription authorization use the same policy.

An explicit empty node ACL or `AccessRestrictions="0"` remains an explicit
local override; it does not inherit the model setting. An omitted model
restriction has the NodeSet schema's default of zero and does not manufacture
a node-local restriction. Normal namespace defaults and application permission
callbacks still apply. Type-hierarchy visibility exemptions are enforced by the
server, not by removing imported policy: metadata remains available for
operations that require authorization.

## Startup and live lifecycle semantics

`AddRuntimeNodeSet` on `IOpcUaServerBuilder` remains the startup path: its factory is created before the server starts and its NodeSet is imported during `CreateAddressSpaceAsync`. After startup, the resulting generation-1 registration is available from `INodeManagerLifecycle.Registrations`, so a model composed at startup can use the same reload and removal APIs as one added while the server is already running.

For example, a startup task can locate the registration by an owned model namespace:

```csharp
public sealed class ModelRegistration(INodeManagerLifecycle lifecycle)
    : IServerStartupTask
{
    public NodeManagerRegistration? Registration { get; private set; }

    public ValueTask OnServerStartedAsync(
        IServerContext server,
        CancellationToken ct)
    {
        Registration = lifecycle.Registrations.Find(registration =>
            registration.NamespaceUris.Contains("urn:example:MyMachine"));
        return default;
    }
}
```

Running servers expose `INodeManagerLifecycle`. Resolve it from dependency injection in a hosted
server, or use `StandardServer.NodeManagerLifecycle` when constructing the server directly.
Prepare additions, replacements, and removals using `RuntimeNodeSetNodeManagerFactory`, then
commit the prepared owner without restarting the server.

```csharp
public sealed class ModelLoader(INodeManagerLifecycle lifecycle)
{
    public async ValueTask LoadAsync(CancellationToken ct)
    {
        var factory = new RuntimeNodeSetNodeManagerFactory(new RuntimeNodeSetOptions
        {
            Sources = [RuntimeNodeSetSource.FromFile("Models/MyMachine.NodeSet2.xml")]
        });
        await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
            [NodeManagerBatchChange.Add(factory)], ct);
        NodeManagerBatchResult result = await prepared.CommitAsync(static _ => default, ct);
        m_registration = result.Registrations[0];
        if (result.CleanupFailure is { } failure)
        {
            throw new NodeManagerAddCommittedException(
                m_registration, result, "The NodeSet is live but reconciliation failed.", failure);
        }
    }

    public async ValueTask ReloadAsync(CancellationToken ct)
    {
        var factory = new RuntimeNodeSetNodeManagerFactory(new RuntimeNodeSetOptions
        {
            Sources = [RuntimeNodeSetSource.FromFile("Models/MyMachine.NodeSet2.xml")]
        });
        await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
            [NodeManagerBatchChange.Replace(m_registration!, factory)], ct);
        NodeManagerBatchResult result = await prepared.CommitAsync(static _ => default, ct);
        m_registration = result.Registrations[0];
        if (result.CleanupFailure is { } failure)
        {
            throw new NodeManagerReloadCommittedException(
                m_registration, result, "The replacement is live but reconciliation failed.", failure);
        }
    }

    public async ValueTask RemoveAsync(CancellationToken ct)
    {
        NodeManagerRegistration current = m_registration!;
        await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
            [NodeManagerBatchChange.Remove(current)], ct);
        NodeManagerBatchResult result = await prepared.CommitAsync(static _ => default, ct);
        m_registration = null;
        if (result.CleanupFailure is { } failure)
        {
            throw new NodeManagerRemovalCommittedException(current, result, failure);
        }
    }

    private NodeManagerRegistration? m_registration;
}
```

Each add returns an immutable `NodeManagerRegistration`, and reload returns the next generation while invalidating the previous handle.

A registration obtained from `Registrations` for an `AddRuntimeNodeSet` model is already the first
live generation; pass it directly to `NodeManagerBatchChange.Replace` or `NodeManagerBatchChange.Remove`.

Preparation is context-free, including inside a NodeManager or Method callback; ordinary factories
require no callback-specific opt-in.
Commit awaits reconciliation and readiness but not the retiring generation's drain. Await the result's
`Retirement` task only outside callbacks or requests whose admissions must drain.
See [Registering NodeManagers](NodeManagers.md#runtime-registration).

The rules that apply to every NodeManager registered at runtime -- what happens to MonitoredItems, Browse continuation points, namespace indexes, DataTypes, and change notifications, and which NodeManagers may be reloaded at all -- are described once in [Registering NodeManagers](NodeManagers.md#registering-node-managers). Runtime NodeSets follow those rules, and the built-in runtime NodeSet manager already implements the `INodeManagerReloadParticipant` contract that reload requires.

### Migrating replacement

The default `Migrate` transition replaces a generation that still owns active monitored items.
Compatible items move to the replacement and retain their identities; incompatible nodes become
unresolved. The old generation is not retained for existing subscriptions or continuation points.

```csharp
public async ValueTask ReplaceAsync(CancellationToken ct)
{
    var factory = new RuntimeNodeSetNodeManagerFactory(new RuntimeNodeSetOptions
    {
        Sources = [RuntimeNodeSetSource.FromFile("Models/MyMachine.NodeSet2.xml")]
    });
    await using IPreparedNodeManagerBatch prepared = await lifecycle.PrepareAsync(
        [NodeManagerBatchChange.Replace(m_registration!, factory,
            NodeManagerMonitoredItemTransitionMode.Migrate)], ct);
    NodeManagerBatchResult result = await prepared.CommitAsync(static _ => default, ct);
    m_registration = result.Registrations[0];
    // Inspect result.CleanupFailure; publication cannot be undone by a readiness failure.
}
```

The returned registration replaces the old handle for lifecycle operations.
Retirement releases resources after admitted work on the old generation drains.

### Immediate reload

`NodeManagerBatchChange.Replace(..., NodeManagerMonitoredItemTransitionMode.Invalidate)`
does not migrate existing monitored items.
Affected data-change items receive `BadNodeIdUnknown`, and clients may recreate
items against the replacement model.

The [node-manager reload matrix](NodeManagers.md#reload-modes) is authoritative for
both transitions, including request lifetimes, continuation points, and generation cleanup.
Runtime NodeSet transactions use the same lifecycle pipeline.

## Quick-start examples

### Single file

```csharp
services.AddOpcUa()
    .AddServer(o => { /* … */ })
    .AddRuntimeNodeSet("Models/MyMachine.NodeSet2.xml");
```

### Single file with a fluent callback

Wire read/write/method handlers on top of the imported nodes using the existing untyped `INodeManagerBuilder` surface:

```csharp
services.AddOpcUa()
    .AddServer(o => { /* … */ })
    .AddRuntimeNodeSet(
        "Models/MyMachine.NodeSet2.xml",
        nodes =>
        {
            nodes.Node("Machines/Machine1/Start")
                .OnCall(StartMachineAsync);

            nodes.Variable<double>("Machines/Machine1/Temperature")
                .OnRead(ReadTemperature);
        });
```

Standard Method argument children are bound to their typed `NodeState` properties during import.
A NodeSet2 Method's `InputArguments` and `OutputArguments` Variables populate
`MethodState.InputArguments` and `MethodState.OutputArguments`, so normal Call argument validation
uses the declarations from the document.
Binding uses the authored `HasProperty` relationship, declared either on the Method or
as an inverse reference on the argument Property; `ParentNodeId` is optional. Local
namespace-URI reference targets are resolved against the import context's namespace table.
The Property must use the namespace-zero standard BrowseName, `PropertyType`, the `Argument`
DataType, and a one-dimensional value rank. Ambiguous or conflicting declarations and
custom or malformed properties do not bind the Method's typed signature; their authored
nodes and references are retained. Other authored reference types are preserved.

### Group of dependent NodeSets

Register multiple NodeSet2 sources that depend on each other. The factory resolves the import order automatically from the `RequiredModel` declarations in each document.

```csharp
services.AddOpcUa()
    .AddServer(o => { /* … */ })
    .AddRuntimeNodeSet(options =>
    {
        options.Sources =
        [
            RuntimeNodeSetSource.FromFile("Models/Opc.Ua.Di.NodeSet2.xml"),
            RuntimeNodeSetSource.FromFile("Models/MyMachine.NodeSet2.xml")
        ];
        options.DefaultNamespaceUri = "urn:example:MyMachine";
        options.Configure = nodes =>
        {
            nodes.Node("Machines/Machine1/Start").OnCall(StartMachineAsync);
        };
    });
```

### Custom stream source

Use `RuntimeNodeSetSource.FromStream` when you want to open the NodeSet2 document lazily — for example from a database, a blob store, or an assembly resource. The delegate is called for each startup load or live generation and must return a fresh, readable `Stream` each time.

```csharp
services.AddOpcUa()
    .AddServer(o => { /* … */ })
    .AddRuntimeNodeSet(options =>
    {
        options.Sources =
        [
            RuntimeNodeSetSource.FromStream(
                name: "MyMachine",
                openStream: ct => OpenNodeSetStreamAsync(ct),
                modelNamespaceUris: ["urn:example:MyMachine"])
        ];
    });
```

### Direct factory registration

If you prefer to create the factory manually — for example to inject it into an existing `StandardServer` subclass — construct a `RuntimeNodeSetNodeManagerFactory` directly:

```csharp
var factory = new RuntimeNodeSetNodeManagerFactory(new RuntimeNodeSetOptions
{
    Sources = [RuntimeNodeSetSource.FromFile("Models/MyMachine.NodeSet2.xml")]
});

// In a StandardServer subclass constructor:
AddNodeManager(factory);
```

## Stream ownership contract

When `FromStream` is used, the runtime loader calls the `openStream` delegate while each NodeManager generation is created and closes the returned stream after deserialization. You must ensure that:

1. Each call to `openStream` returns a **new** stream positioned at the beginning.
2. The stream is **readable** and contains a valid NodeSet2 XML document.
3. You do not close the stream yourself; the factory disposes it after `UANodeSet.Read`.

If the delegate returns `null` or the stream does not contain valid NodeSet2 XML, server startup fails with a clear `InvalidOperationException` that names the source.

## Default namespace for unqualified browse paths

When the `Configure` callback uses browse paths without an explicit `ns=N;` prefix (for example `"Machines/Machine1/Start"`), the runtime loader must know which namespace index to apply for the first path segment. The resolution is:

1. If `RuntimeNodeSetOptions.DefaultNamespaceUri` is set, that URI is used.
2. Otherwise the factory infers the **unique leaf model** — the one model in the loaded group that is not required by any other included source.
3. If inference is ambiguous (multiple leaf models and a `Configure` callback is present), startup fails with an error message that lists the candidates. In this case, set `DefaultNamespaceUri` explicitly.

When no `Configure` callback is registered, `DefaultNamespaceUri` has no effect and may be omitted.

## Dependency sorting

The factory reads the `Models/Model/RequiredModel` entries from each parsed NodeSet document and performs a topological sort (Kahn's algorithm) before importing. Import order guarantees that a required model's nodes are in the address space before any document that depends on them imports its nodes.

All documents of one manager are parsed into a single batch whose parent-child relationships are linked exactly once, after the last document has been parsed. A node may therefore declare a `ParentNodeId` that lives in another document of the group regardless of the order the two were parsed in; a duplicate NodeId across documents is rejected while parsing. A fluent `Configure` callback can add further documents to the manager with `builder.Import` — see [Importing a NodeSet2 overlay at runtime](NodeManagers.md#importing-a-nodeset2-overlay-at-runtime--builderimport).

Dependencies on models **not included in the group** — for example the OPC UA base namespace or a third-party model hosted by a generated NodeManager — are silently allowed and treated as external. The server resolves cross-manager references through the normal `AddReverseReferencesAsync` mechanism.

### Referencing a Node another NodeManager owns

A NodeSet may declare a Reference whose other endpoint belongs to a different NodeManager — an inverse `Organizes` placing an Object under a folder some other model defines, for instance. OPC 10000-3 requires such a Reference to be visible from both ends, so the forward edge has to appear on a Node this NodeManager does not own.

That works in both registration orders. At startup the master collects every NodeManager's external references first and applies them afterwards, so creation order does not matter. A NodeManager added **after** startup has no such second phase, so the master retains the startup references and each dynamic NodeManager's references, and **replays** them to any NodeManager registered later. Without that replay a Reference into a NodeManager that did not exist yet would simply be dropped — a reference to an absent Node is discarded rather than queued — leaving the two ends permanently disagreeing: the target browses to the source, the source does not list the target.

Both orders are pinned by `RuntimeNodeSetCrossSourceReferenceTests`.

Cycles among the included sources cause `InvalidOperationException` at startup with an error message that lists the participating sources.

## Complex types

Runtime complex type loading (structures, enumerations, union types) is **on by default** and requires no extra configuration. After all NodeManagers have built their address spaces, `StandardServer.OnNodeManagerStartedAsync` scans every DataType node whose `DataTypeDefinition` attribute is populated and registers a NativeAOT-safe stand-in encodeable in the server's factory. The same stand-ins are reused for client decode/encode via the OPC UA Part 6 binary protocol.

You can tune this behaviour through the existing `ServerComplexTypeOptions`:

```csharp
services.AddOpcUa()
    .AddServer(o => { /* … */ })
    .AddRuntimeNodeSet("Models/MyMachine.NodeSet2.xml")
    .AddComplexTypeSystem(o => o.Enabled = false); // disable if not needed
```

Setting `StandardServer.LoadComplexTypes = false` or disabling the complex type system entirely suppresses the stand-in loading without producing a warning. No second complex-type loading path is introduced by the runtime NodeSet feature.

For a complete description of the server-side complex type system, see [ComplexTypes.md](ComplexTypes.md).

## Comparison with source-generated NodeManagers

| Aspect | Runtime NodeSet (`AddRuntimeNodeSet`) | Source-generated (`[NodeManager]`) |
|--------|--------------------------------------|-------------------------------------|
| Node access in callbacks | Generic `NodeState` / `BaseVariableState` via untyped browse paths | Strongly typed, compiler-checked fluent accessors per node |
| Compilation required on model change | No — reload through `INodeManagerLifecycle` | Yes — regenerate and rebuild |
| AOT / trimming compatibility | Full (uses the existing `UANodeSet.Read` XmlSerializer path) | Full (generated code is static) |
| Named NodeId constants | Not generated | Generated (`Variables.*`, `Objects.*`, etc.) |
| Multiple namespaces in one manager | Yes — group multiple sources | One namespace per generator run |
| DI registration | `AddRuntimeNodeSet(...)` | `AddNodeManager<TFactory>()` |
| Stream / file input | Files and custom stream factories | MSBuild `AdditionalFiles` only |

Source-generated managers are the recommended path for production code where type safety and compile-time validation matter. Runtime NodeSets are the recommended path for rapid prototyping, model-file delivery scenarios, and cases where the XML content changes independently of the server binary.

## Related documentation

- [Source-Generated NodeManagers](NodeManagers.md#source-generated-node-managers) — strongly typed alternative.
- [Dependency Injection](DependencyInjection.md) — `IOpcUaServerBuilder` and service registration.
- [ComplexTypes.md](ComplexTypes.md) — server-side complex type loading and client-side decoding.
