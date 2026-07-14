# Runtime NodeSets

This guide explains how to load one or more NodeSet2 XML documents into the server's address space at startup without writing a source-generated or hand-coded NodeManager. You configure which files or streams to load; the server imports them in dependency order and registers the resulting nodes.

## When to use the runtime NodeSet path

Use `AddRuntimeNodeSet` when:

- You receive a NodeSet2 XML from a companion-specification vendor and want to host its nodes without regenerating source.
- Your information model changes frequently enough that rebuilding the source-generated manager for every XML update would be disruptive.
- You are prototyping or testing a new NodeSet2 design.

Use the [source-generated path](SourceGeneratedNodeManagers.md) when you want compile-time safety, strong typing, and AOT-safe named constants for every node in your model. The runtime path gives you generic `NodeState` objects and untyped browse-path wiring.

## Startup and live lifecycle semantics

`AddRuntimeNodeSet` on `IOpcUaServerBuilder` remains the startup path: its factory is created before the server starts and its NodeSet is imported during `CreateAddressSpaceAsync`.

Running servers also expose `INodeManagerLifecycle`. Resolve it from dependency injection in a hosted server, or use `StandardServer.NodeManagerLifecycle` when constructing the server directly. The lifecycle provider can add, reload, and remove runtime NodeSets without restarting the server.

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
            ct);
    }

    public ValueTask RemoveAsync(CancellationToken ct)
    {
        return lifecycle.RemoveAsync(m_registration!, ct);
    }
}
```

Each add returns an immutable `NodeManagerRegistration`. Reload returns the next generation and invalidates the previous handle. Only registrations created by the lifecycle provider can be reloaded or removed; startup, diagnostics, and core NodeManagers are protected.

Reload and removal fail when the current NodeManager owns active monitored items. Delete those monitored items first, then retry. This fail-closed rule prevents a live subscription from retaining a stale manager handle.

Treat `INodeManagerLifecycle` as a host control-plane API. Do not invoke reload or removal from inside an OPC UA service or Method callback: teardown waits for requests that already captured the retired routing generation to complete before disposing it.

The built-in runtime NodeSet manager implements `INodeManagerReloadParticipant`, which transfers inbound cross-manager references to retained NodeIds and removes counterparts for dropped nodes. A custom NodeManager can be added and removed through the lifecycle provider, but must implement this participant contract before it can be reloaded safely.

Reload and removal invalidate saved Browse continuation points owned by the retired manager. A later `BrowseNext` with one of those tokens returns `BadContinuationPointInvalid` instead of invoking a disposed generation.

Namespace indexes are append-only for the lifetime of a running server. Removing a model removes its nodes and routing but leaves its namespace URI in `NamespaceArray`; a later reload or add reuses the same index. When a live add appends a URI, the server updates `NamespaceArray` and `UrisVersion`.

Runtime DataType registrations are also additive. Reload accepts an existing DataType only when its definition is structurally compatible, rejects incompatible changes, and retains removed stand-in encodeables so existing sessions and in-flight values remain decodable.

Every committed lifecycle transaction emits one compressed model-change notification. Reload also emits a semantic-change notification when values of properties marked with the `SemanticChange` access-level bit changed.

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

- [Source-Generated NodeManagers](SourceGeneratedNodeManagers.md) — strongly typed alternative.
- [Dependency Injection](DependencyInjection.md) — `IOpcUaServerBuilder` and service registration.
- [ComplexTypes.md](ComplexTypes.md) — server-side complex type loading and client-side decoding.
