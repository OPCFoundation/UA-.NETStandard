# Runtime NodeSets

This guide explains how to load one or more NodeSet2 XML documents into the server's address space at startup without writing a source-generated or hand-coded NodeManager. You configure which files or streams to load; the server imports them in dependency order and registers the resulting nodes.

## When to use the runtime NodeSet path

Use `AddRuntimeNodeSet` when:

- You receive a NodeSet2 XML from a companion-specification vendor and want to host its nodes without regenerating source.
- Your information model changes frequently enough that rebuilding the source-generated manager for every XML update would be disruptive.
- You are prototyping or testing a new NodeSet2 design.

Use the [source-generated path](NodeManagers.md#source-generated-node-sources) when you want compile-time safety, strong typing, and AOT-safe named constants for every node in your model. The runtime path gives you generic `NodeState` objects and untyped browse-path wiring.

## Startup and live lifecycle semantics

`AddRuntimeNodeSet` on `IOpcUaServerBuilder` remains the startup path: its factory is created before the server starts and its NodeSet is imported during `CreateAddressSpaceAsync`.

Running servers also expose `INodeManagerLifecycle`. Resolve it from dependency injection in a hosted server, or use `StandardServer.NodeManagerLifecycle` when constructing the server directly. The lifecycle provider can add, reload, shadow-reload, and remove runtime NodeSets without restarting the server.

```csharp
public sealed class ModelLoader(INodeManagerLifecycle lifecycle)
{
    private NodeManagerRegistration? m_registration;

    public async ValueTask LoadAsync(CancellationToken ct)
    {
        m_registration = await lifecycle.AddRuntimeNodeSetAsync(
            new RuntimeNodeSetOptions
            {
                Sources = [RuntimeNodeSetSource.FromFile("Models/MyMachine.NodeSet2.xml")]
            },
            callerContext: null,
            ct);
    }

    public async ValueTask ReloadAsync(CancellationToken ct)
    {
        m_registration = await lifecycle.ReloadRuntimeNodeSetAsync(
            m_registration!,
            new RuntimeNodeSetOptions
            {
                Sources = [RuntimeNodeSetSource.FromFile("Models/MyMachine.NodeSet2.xml")]
            },
            callerContext: null,
            ct);
    }

    public ValueTask RemoveAsync(CancellationToken ct)
    {
        return lifecycle.RemoveAsync(m_registration!, callerContext: null, ct);
    }
}
```

Each add returns an immutable `NodeManagerRegistration`, and reload returns the next generation while invalidating the previous handle.

`AddRuntimeNodeSetAsync`, `ReloadRuntimeNodeSetAsync`, and `RemoveAsync` take the operation the caller is running under. Pass `context.GetOperationContext()` when calling from a NodeManager or Method callback: a lifecycle operation drains the requests that are in flight, so one started from inside a request would wait for itself and is rejected with an `InvalidOperationException`. A control-plane caller such as the `ModelLoader` above is not serving a request and passes `null`. See [Registering NodeManagers](NodeManagers.md#runtime-registration).

The rules that apply to every NodeManager registered at runtime -- what happens to MonitoredItems, Browse continuation points, namespace indexes, DataTypes, and change notifications, and which NodeManagers may be reloaded at all -- are described once in [Registering NodeManagers](NodeManagers.md#registering-node-managers). Runtime NodeSets follow those rules, and the built-in runtime NodeSet manager already implements the `INodeManagerReloadParticipant` contract that reload requires.

### Shadow reload

`ShadowReloadRuntimeNodeSetAsync` (backed by `INodeManagerLifecycle.ShadowReloadAsync`) replaces a live registration the same way `ReloadRuntimeNodeSetAsync` does, but without the active-monitored-item guard:

```csharp
public async ValueTask ShadowReloadAsync(CancellationToken ct)
{
    m_registration = await lifecycle.ShadowReloadRuntimeNodeSetAsync(
        m_registration!,
        new RuntimeNodeSetOptions
        {
            Sources = [RuntimeNodeSetSource.FromFile("Models/MyMachine.NodeSet2.xml")]
        },
        ct);
}
```

The replacement generation is prepared and published through the same transactional prepare/publish/commit/rollback path as `ReloadAsync`, so a failure during preparation, publication, or the routing switch leaves the current generation fully active and cleans up the replacement, exactly as a normal reload does. Once committed, every new service request is atomically routed to the replacement generation, including for namespaces the current and replacement generations share.

The current generation is not torn down immediately. It is moved to the same retired-generation bookkeeping used for an ordinary reload, but its existing monitored items and any request or continuation point that already captured it keep being served by it, unaffected by the routing switch. The retired generation is disposed automatically, without deleting any client subscription, once its monitored items and in-flight state drain; a later lifecycle operation (or shutdown) opportunistically retries that cleanup until it succeeds. `ShadowReloadAsync` returns the replacement `NodeManagerRegistration` immediately and invalidates the current handle for further lifecycle mutations, the same as `ReloadAsync`.

Use `ShadowReloadAsync` when a model update must take effect for new requests without waiting for existing subscriptions to unsubscribe first; use the fail-closed `ReloadAsync` when a stale generation must never remain reachable, even briefly, for already-open monitored items.

### Immediate reload

`ImmediateReloadRuntimeNodeSetAsync` (backed by `INodeManagerLifecycle.ImmediateReloadAsync`) performs the same atomic replacement but does not retain the previous generation until monitored items drain. After requests that already captured the old routing generation finish, every affected data-change monitored item is made publishable with `BadNodeIdUnknown`, event monitored items stop producing events, continuation points are invalidated, and the old NodeManager is disposed. The subscription and monitored-item records remain available so clients can receive the status and delete or recreate the affected items.

Use immediate reload only when continuity through the previous generation is not required. Durable monitored items are not eligible for immediate retirement because their terminal state would have to survive restart; choose shadow reload for any generation that owns them.


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

## Comparison with source-generated node sources

| Aspect | Runtime NodeSet (`AddRuntimeNodeSet`) | Source-generated (`[NodeManager]` + `Configure(INodeGraphBuilder)`) |
|--------|--------------------------------------|-------------------------------------|
| Node access in callbacks | Generic `NodeState` / `BaseVariableState` via untyped browse paths | Strongly typed, compiler-checked fluent accessors per node |
| Compilation required on model change | No — reload through `INodeManagerLifecycle` | Yes — regenerate and rebuild |
| AOT / trimming compatibility | Full (uses the existing `UANodeSet.Read` XmlSerializer path) | Full (generated code is static) |
| Named NodeId constants | Not generated | Generated (`Variables.*`, `Objects.*`, etc.) |
| Multiple namespaces in one manager | Yes — group multiple sources | One namespace per generator run |
| DI registration | `AddRuntimeNodeSet(...)` | `AddNodeSource<TSource>()` |
| Stream / file input | Files and custom stream factories | MSBuild `AdditionalFiles` only |

Source-generated node sources are the recommended path for production code where type safety and compile-time validation matter. Runtime NodeSets are the recommended path for rapid prototyping, model-file delivery scenarios, and cases where the XML content changes independently of the server binary.

## Related documentation

- [Source-generated node sources](NodeManagers.md#source-generated-node-sources) — strongly typed alternative.
- [Dependency Injection](DependencyInjection.md) — `IOpcUaServerBuilder` and service registration.
- [ComplexTypes.md](ComplexTypes.md) — server-side complex type loading and client-side decoding.
