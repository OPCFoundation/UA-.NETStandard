# Source-Generated NodeManagers

This guide explains how to use the OPC UA stack source generator to emit a
ready-to-host `AsyncCustomNodeManager` for an information model design XML, and
how to wire callbacks (read/write/method/lifecycle) using the fluent
`INodeManagerBuilder` API. The combination is designed for **single-file,
NativeAOT-friendly** servers — see
`Applications/MinimalBoilerServer` for the canonical sample.

## What the generator produces

The base source generator already emits, for each model design:

- `Add{Ns}(NodeStateCollection, ISystemContext)` — populates a node
  collection.
- `Add{Ns}(INodeStateFactoryBuilder)` — registers strongly-typed activators.
- `Add{Ns}DataTypes(IEncodeableFactoryBuilder)` — registers encodeables.

When `ModelSourceGeneratorGenerateNodeManager=true` is set **or** a
class is annotated with `[Opc.Ua.Server.Fluent.NodeManagerAttribute]`,
the generator **additionally** emits, in either the `{ModelNamespace}`
namespace (legacy MSBuild mode) or the user class's namespace
(attribute mode):

- `public partial class {Ns}NodeManager : AsyncCustomNodeManager` (legacy)
  or `public partial class {UserClass} : AsyncCustomNodeManager` (attribute)
  - Constructor `(IServerInternal, ApplicationConfiguration)`.
  - Pre-registers the model namespace URI.
  - `LoadPredefinedNodesAsync` returns
    `new NodeStateCollection().Add{Ns}(context)` wrapped in a
    `ValueTask<NodeStateCollection>`.
  - `CreateAddressSpaceAsync` `await`s `base.CreateAddressSpaceAsync`,
    then builds a fluent `INodeManagerBuilder`, invokes
    `Configure(builder)`, calls `builder.Seal()`, and replays
    `NotifyNodeAdded` for every predefined node so per-node lifecycle
    hooks fire deterministically.
  - `AddPredefinedNodeAsync` / `RemovePredefinedNodeAsync` overrides
    forward to base and then dispatch the lifecycle notification.
  - `OnMonitoredItemCreated` (still synchronous on the base) dispatches
    the per-node hook.
  - Declares `partial void Configure(INodeManagerBuilder builder);` for
    user wiring.
- `public class {Ns}NodeManagerFactory : IAsyncNodeManagerFactory`
  - Returns the namespace URI in `NamespacesUris`.
  - `CreateAsync(IServerInternal, ApplicationConfiguration, CancellationToken)`
    returns a `ValueTask<IAsyncNodeManager>` containing a new manager
    instance.
  - Both members are `virtual` so consumers can subclass to add a second
    namespace or swap in a manager subclass.

`AddNodeManager` on `StandardServer` has overloads for both
`INodeManagerFactory` and `IAsyncNodeManagerFactory`; the generated
async factory binds to the latter automatically.

## Opting in

Add the generator analyzer to your project (this is what
`OPCFoundation.Opc.Ua.SourceGeneration.props` is for) and choose **one**
of the two opt-in modes:

### Per-class opt-in via `[NodeManager]` (recommended)

Annotate the user-authored partial class that should host the generated
manager:

```csharp
using Opc.Ua.Server.Fluent;

namespace MyCompany.MyServer;

[NodeManager]
public partial class MyDeviceNodeManager
{
    partial void Configure(INodeManagerBuilder builder)
    {
        // wire your callbacks here
    }
}
```

The generator emits a sibling `partial class MyDeviceNodeManager :
AsyncCustomNodeManager` and a `MyDeviceNodeManagerFactory` (implementing
`IAsyncNodeManagerFactory`) in the same namespace as the user class. No
MSBuild flag is required.

When a project carries multiple model designs, disambiguate which
design the attribute targets via either:

```csharp
[NodeManager(NamespaceUri = "http://opcfoundation.org/UA/Boiler/")]
```

or by file stem:

```csharp
[NodeManager(Design = "BoilerDesign")]
```

Set `GenerateFactory = false` to suppress factory emission when you want
to ship a hand-written `IAsyncNodeManagerFactory`.

### Project-wide opt-in via MSBuild property (legacy)

If you prefer a generator-derived class identity (`{Prefix}NodeManager` /
`{Prefix}NodeManagerFactory`) without authoring a stub partial, set the
opt-in property:

```xml
<PropertyGroup>
  <ModelSourceGeneratorGenerateNodeManager>true</ModelSourceGeneratorGenerateNodeManager>
</PropertyGroup>

<ItemGroup>
  <AdditionalFiles Include="Generated\MyModelDesign.xml" />
  <AdditionalFiles Include="Generated\MyModelDesign.csv" />
</ItemGroup>
```

This emits `{Prefix}NodeManager` + `{Prefix}NodeManagerFactory` for
every design in the project. Wire callbacks by adding a sibling
`partial class {Prefix}NodeManager` that implements `Configure`.

Without either opt-in, only the existing `Add{Ns}*` extensions are
emitted — hand-written `AsyncCustomNodeManager` (or legacy
`CustomNodeManager2`) subclasses keep working unchanged.

## Wiring callbacks: the `Configure` partial

Author a sibling partial that fills in `Configure`:

```csharp
namespace MyModel;

public partial class MyModelNodeManager
{
    partial void Configure(INodeManagerBuilder builder)
    {
        builder
            .Node("Boilers/Boiler #1/Drum1001/LevelIndicator/Output")
            .OnRead(MyReadHandler);

        // Resolve a singleton instance by its TypeDefinitionId — stable
        // across deployments and independent of where the instance sits
        // in the tree. Ideal for well-known types like
        // HistoryServerCapabilities or a single BoilerType instance.
        builder
            .NodeFromTypeId(ExpandedNodeId.ToNodeId(
                MyModel.ObjectTypeIds.BoilerType, Server.NamespaceUris))
            .OnNodeAdded((ctx, node) => /* ... */);

        // For multi-instance types, disambiguate with a BrowseName:
        builder
            .NodeFromTypeId(
                ExpandedNodeId.ToNodeId(MyModel.ObjectTypeIds.BoilerType, Server.NamespaceUris),
                new QualifiedName("Boiler #2", nsIndex))
            .OnRead(MyReadHandler);
    }
}
```

Path syntax is `/`-separated **BrowseNames**, rooted at the model
namespace's predefined nodes. Optional `ns=N;` prefix lets you target a
different namespace.

### Addressing modes

| Method | Resolves by | Use when |
|--------|-------------|----------|
| `Node(string path)` | BrowseName path | Deterministic tree layout, multiple siblings |
| `Node(NodeId id)` / `Node<TState>(NodeId id)` | Absolute NodeId | You own the id (e.g. generated `Variables.*`) |
| `NodeFromTypeId(NodeId typeId)` / `NodeFromTypeId<TState>(NodeId typeId)` | `BaseInstanceState.TypeDefinitionId` | Singleton instance of a well-known type |
| `NodeFromTypeId(NodeId typeId, QualifiedName browseName)` | TypeDefinitionId + BrowseName | Multi-instance types — pick one |

`NodeFromTypeId` walks every predefined node owned by this manager
(and their sub-trees) at Configure-time. Error matrix:

* `BadNodeIdInvalid` — `typeId` is null or `IsNull`.
* `BadNodeIdUnknown` — no instance carries that `TypeDefinitionId`, or
  the optional `browseName` disambiguator finds no match.
* `BadBrowseNameDuplicated` — more than one candidate and no
  disambiguator was supplied (or multiple candidates share the same
  `browseName`).
* `BadTypeMismatch` — typed overload's `TState` cast fails.

The builder exposes:

| Method | Wires |
|--------|-------|
| `OnRead` / `OnReadAsync` | `BaseVariableState.OnReadValue` |
| `OnWrite` / `OnWriteAsync` | `BaseVariableState.OnWriteValue` |
| `OnCall` / `OnCallAsync` | `MethodState.OnCallMethod*` |
| `OnNodeAdded` / `OnNodeRemoved` | Lifecycle dispatch from `NotifyNodeAdded` |
| `OnEvent`, `OnConditionRefresh`, `OnHistoryRead`, `OnHistoryUpdate`, `OnMonitoredItemCreated` | Manager-level dispatch keyed by `NodeId` |

`INodeManagerBuilder.NodeManager` is typed as `IAsyncNodeManager`. Use
`builder.NodeManager.SyncNodeManager` to obtain the synchronous
`INodeManager` facade for legacy interop, or cast it to your concrete
manager type if you need direct access.

All resolution happens **once** during `CreateAddressSpaceAsync`,
against the in-memory predefined-node tree. There is no reflection, no
`Activator.CreateInstance`, no `Expression.Compile` — the whole pipeline
is NativeAOT-safe.

## Typed model-traversal — the `Configure(I{Manager}NodeManagerBuilder)` partial

Alongside the string/NodeId/TypeId addressing surface above, the
generator emits a **second** `Configure` partial whose builder parameter
exposes one IntelliSense-aware accessor per predefined instance, child,
variable and method in the model. Every wiring site becomes a chain of
properties — typos are compile-time errors, not startup-time
`ServiceResultException`s.

```csharp
public partial class BoilerNodeManager
{
    // Untyped Configure remains available for nodes outside the model
    // (e.g. dynamic instances, foreign-namespace nodes, or just to keep
    // hand-written wiring side-by-side with typed wiring).
    partial void Configure(INodeManagerBuilder builder)
    {
        builder
            .Node("Boilers/Boiler #1/DrumX001/LIX001/Output")
            .OnRead(GenerateDrumLevel);
    }

    // Typed Configure: every accessor below is a generated property
    // resolved against the model. The compiler enforces both the path
    // shape AND the value type of every leaf.
    partial void Configure(IBoilerNodeManagerBuilder builder)
    {
        // Variable: typed Func<double> handler — the generator removed
        // the ref-Variant boilerplate.
        builder.Boilers.Boiler__1.LCX001.Measurement
            .OnRead(GenerateLevelMeasurement);

        // Variable, async: routes through BaseVariableState.ReadAttributeAsync
        // outside the lock so the lambda may freely await.
        builder.Boilers.Boiler__1.PipeX002.FTX002.Output
            .OnRead(GenerateOutputFlowAsync);

        // Method, async: typed OnCall(Func<CancellationToken,ValueTask>)
        // overload. Bind sync Action variants the same way.
        builder.Boilers.Boiler__1.Simulation.Halt
            .OnCall(HaltSimulationAsync);
    }
}
```

Both partials are optional and both run; wiring the same node from
both is illegal and throws at startup. Choose whichever shape best fits
each call site — typed for everything declared in the model, untyped
for everything else.

### What the generator emits per model

For a model with `N` ObjectTypes and `M` predefined instances/children
the generator emits, into a single `{Manager}.FluentBuilders.g.cs`:

- `internal interface I{Manager}NodeManagerBuilder : INodeManagerBuilder`
  — one accessor per top-level predefined instance.
- `internal sealed class {Manager}NodeManagerTypedBuilder` — proxy that
  forwards `INodeManagerBuilder` members to the runtime builder while
  surfacing the typed accessors.
- One `internal sealed class` per instance node — whose properties map
  to typed `IVariableBuilder<TValue>`, child wrapper instances, and
  method wrappers.
- One `internal sealed class` per method — exposing typed
  `OnCall(Func<TIn1, …, TResult>)` and async
  `OnCall(Func<TIn1, …, CancellationToken, ValueTask<TResult>>)`
  overloads when the model declares input/output arguments
  (the generator handles `Variant.TryGetValue` unpacking and
  `Variant.From<T>` boxing — see [Methods with arguments](#methods-with-arguments--typed-oncall-overloads)).
  Argument-less methods keep the no-arg `OnCall(Action)` /
  `OnCall(Func<CancellationToken, ValueTask>)` overloads.

All emitted types are `internal sealed` because `Configure` is a
private partial — the surface never escapes the assembly. Child
accessors resolve namespace indices lazily through
`ISystemContext.NamespaceUris.GetIndexOrAppend(...)` so the wrappers
work regardless of the namespace-table order at runtime.

### Methods with arguments — typed `OnCall` overloads

When a model method declares input or output arguments the generator
emits **typed `OnCall` overloads** that bind directly to the user
handler's parameters and return value. Inputs are unboxed via
`Variant.TryGetValue<T>(out T)`, the boxed result is written back
through `Variant.From<T>(value)`, and `BadInvalidArgument` /
`BadArgumentsMissing` is returned when the wire shape does not match
the declared signature — none of which the user has to spell out.

Two overloads are emitted per method:

- `OnCall(Func<TIn1, TIn2, …, TResult> handler)` — synchronous
  dispatch through `MethodState.OnCallMethod2`.
- `OnCall(Func<TIn1, TIn2, …, CancellationToken, ValueTask<TResult>>
  handler)` — async dispatch through `MethodState.OnCallMethod2Async`,
  awaited inside `AsyncCustomNodeManager.CallAsync` so the lambda may
  freely `await`.

Methods with multiple output arguments are bound to a `ValueTuple`
return — slot `i` is written from `__r.Item{i+1}`. Methods with no
return value (action-only) keep the existing `OnCall(Action)` /
`OnCall(Func<CancellationToken, ValueTask>)` overloads.

```csharp
[NodeManager(NamespaceUri = "http://opcfoundation.org/UA/Calc/")]
public partial class CalcNodeManager
{
    partial void Configure(ICalcNodeManagerBuilder builder)
    {
        // Sync int+int → int. The generator unpacks each Variant
        // through Variant.TryGetValue<int> and boxes the result back
        // through Variant.From<int>.
        builder.Calculator.Add
            .OnCall((int a, int b) => a + b);

        // Async double+double → double. The CancellationToken is
        // forwarded by AsyncCustomNodeManager.CallAsync so the
        // handler may freely await and honour cancellation.
        builder.Calculator.Multiply
            .OnCall(async (double x, double y, CancellationToken ct) =>
            {
                await Task.Yield();
                ct.ThrowIfCancellationRequested();
                return x * y;
            });

        // Sync string+string → string. Reference-typed inputs and
        // return values use the same Variant.TryGetValue / Variant.From
        // path; the handler can null-coalesce safely because a missing
        // input is reported as BadInvalidArgument before the lambda
        // ever runs.
        builder.Calculator.Concat
            .OnCall((string left, string right) =>
                (left ?? string.Empty) + (right ?? string.Empty));
    }
}
```

The end-to-end sample lives in
`Applications/MinimalCalcServer/` (model in `Model/Calc.xml`, wiring
in `CalcNodeManager.Configure.cs`). The companion AOT round-trip tests
in `Tests/Opc.Ua.Aot.Tests/CalculatorNodeManagerAotTests.cs` exercise
each shape over a real `Session.CallAsync(...)`.

## Event sources — typed `Publish<TEvent>` on notifier wrappers

Beyond reads, writes and method calls, the fluent API lets callers
register an `IAsyncEnumerable<TEvent>` against any notifier object so
events flow into the standard `NodeState.ReportEvent` path
automatically. The runtime owns the entire lifecycle: it starts the
iterator the first time a client subscribes to events on the notifier
(or any ancestor that walks via inverse `HasNotifier` /
`HasEventSource` references), cancels it when the last interested
monitored item disappears, and disposes it on manager teardown.

Generated managers derive from `Opc.Ua.Server.Fluent.FluentNodeManagerBase`
out of the box, so wiring is one call:

```csharp
partial void Configure(IBoilerNodeManagerBuilder builder)
{
    // The DrumX001 wrapper exposes Publish<TEvent> because the model
    // declares EventNotifier=SubscribeToEvents on the node. Lazy by
    // default — the iterator only runs while a client is monitoring.
    builder.Boilers.Boiler__1.DrumX001
        .Publish<BaseEventState>(GenerateDrumHeartbeatAsync);
}

private async IAsyncEnumerable<BaseEventState> GenerateDrumHeartbeatAsync(
    BaseObjectState notifier,
    ISystemContext context,
    [EnumeratorCancellation] CancellationToken cancellationToken)
{
    while (!cancellationToken.IsCancellationRequested)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) { yield break; }

        var ev = new BaseEventState(parent: notifier);
        ev.Severity = PropertyState<ushort>.With<VariantBuilder>(
            ev, (ushort)EventSeverity.Medium);
        ev.Message = PropertyState<LocalizedText>.With<VariantBuilder>(
            ev, new LocalizedText("Drum heartbeat"));
        yield return ev;
    }
}
```

The runtime auto-populates `EventId`, `EventType`, `SourceNode`,
`SourceName` (browse name of the notifier), `Time`, `ReceiveTime`,
`Severity` (Medium when 0) and `Message` (empty `LocalizedText` when
unset) on the way out, so the iterator only sets the user-meaningful
fields.

### Where the typed overload appears

The generator emits `Publish<TEvent>` on a wrapper **only** when the
underlying node qualifies as an event source:

- `ObjectDesign.SupportsEvents == true` (i.e. the model declares
  `EventNotifier=SubscribeToEvents`, `HasNotifier`, or
  `HasEventSource`), or
- The node has a forward `GeneratesEvent` / `AlwaysGeneratesEvent`
  reference.

`TEvent` is constrained to `BaseEventState` — pass any subtype that
fits the model's event hierarchy. For nodes outside the model, or
hand-written managers, the same `Publish<TNotifier, TEvent>` extension
is available directly on `INodeBuilder<TNotifier>` where
`TNotifier : BaseObjectState`.

### Two registration shapes

```csharp
// Direct stream — registry uses the same instance for every activation.
builder.Boilers.Boiler__1.DrumX001
    .Publish<BaseEventState>(channel.Reader.ReadAllAsync(default));

// Factory — registry calls the factory each time a client subscribes,
// so the iterator can capture the live notifier / context / token.
builder.Boilers.Boiler__1.DrumX001
    .Publish<BaseEventState>(
        (notifier, context, ct) => GenerateAsync(notifier, context, ct));
```

### Tuning lifecycle with `EventPublishOptions`

```csharp
builder.Boilers.Boiler__1.DrumX001
    .Publish<BaseEventState>(GenerateDrumHeartbeatAsync,
        new EventPublishOptions
        {
            // Keep iterator running even with no monitored items.
            AlwaysOn               = false,

            // Skip default population of EventId / EventType / Time /
            // ReceiveTime / SourceNode / SourceName / Severity / Message.
            SkipDefaultPopulation  = false,

            // Register the notifier as a server-wide root notifier so
            // clients can monitor events on the Server object itself.
            RegisterAsRootNotifier = true,

            // Bound how long the registry waits for the iterator to
            // honour cancellation on deactivation.
            CancellationTimeout    = TimeSpan.FromSeconds(5),

            // Optional fault-handler invoked when the iterator throws.
            OnError = (notifier, exception, context) => { /* log */ }
        });
```

### Hand-written node managers

Managers that don't use the source generator can opt in by deriving
from `Opc.Ua.Server.Fluent.FluentNodeManagerBase` and calling
`AttachToBuilder(builder)` from inside their address-space-build
callback. Once attached, all `Publish` extensions resolve against the
manager's registry exactly as for generated managers.

The end-to-end sample lives in
`Applications/MinimalBoilerServer/BoilerNodeManager.Configure.cs`
(wiring `GenerateDrumHeartbeatAsync` on the drum). The companion AOT
round-trip test in
`Tests/Opc.Ua.Aot.Tests/PublishedEventsAotTests.cs` subscribes a
real client `MonitoredItem` with an `EventFilter` and asserts the
heartbeats arrive end-to-end under NativeAOT constraints (no JIT, no
reflection).

## Single-file `Program.cs` — what it looks like

The shipping `services.AddOpcUa().AddServer(...)` extension wires the
server into the .NET Generic Host: configuration, certificate check,
`ApplicationInstance` lifetime and Ctrl+C/SIGTERM handling are all owned
by the host. User code stays at ~12 lines.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole();

builder.Services
    .AddOpcUa()
    .AddServer(o =>
    {
        o.ApplicationName = "MyServer";
        o.ApplicationUri  = "urn:localhost:MyServer";
        o.ProductUri      = "uri:opcfoundation.org:MyServer";
        o.AutoAcceptUntrustedCertificates = true;
        o.EndpointUrls.Add("opc.tcp://localhost:51210/MyServer");
    })
    .AddNodeManager<MyModel.MyModelNodeManagerFactory>();

await builder.Build().RunAsync();
```

`AddOpcUa()` registers a `ServiceProviderTelemetryContext` that adapts
the host's `ILoggerFactory` to `ITelemetryContext` — no separate logging
pipeline is required. `IOpcUaServerBuilder.AddNodeManager<T>()` registers
an `IAsyncNodeManagerFactory`; use `AddSyncNodeManager<T>()` for the
legacy `INodeManagerFactory`. For advanced configuration (custom security
policies, additional builder calls), set `OpcUaServerOptions.ConfigureBuilder`.

That's the whole server. The Boiler version is in
`Applications/MinimalBoilerServer/Program.cs`.

## Multi-namespace and manager-swap subclassing

Because the generated factory members are `virtual`, you can extend
without forking:

```csharp
public sealed class MyExtendedFactory : MyModel.MyModelNodeManagerFactory
{
    public override ArrayOf<string> NamespacesUris
    {
        get
        {
            var ns = base.NamespacesUris;
            ns.Add("urn:my:second:namespace");
            return ns;
        }
    }

    public override ValueTask<IAsyncNodeManager> CreateAsync(
        IServerInternal server,
        ApplicationConfiguration cfg,
        CancellationToken cancellationToken = default)
        => new(new MyExtendedNodeManager(server, cfg));
}
```

The `Tests/Opc.Ua.Server.Tests/Fluent/GeneratedManagerHybridTests.cs`
suite verifies these subclassing scenarios.

## NativeAOT publishing

The project that hosts the generated manager only needs the standard AOT
settings:

```xml
<PropertyGroup>
  <PublishAot>true</PublishAot>
  <InvariantGlobalization>true</InvariantGlobalization>
  <TargetFramework>net10.0</TargetFramework>
</PropertyGroup>
```

Use `Microsoft.Extensions.Logging.Console` for AOT-friendly logging
(Serilog providers vary in AOT compatibility). Validate with:

```cmd
dotnet publish -c Release -r win-x64
```

`Applications/MinimalBoilerServer` publishes cleanly with **zero AOT/trim
warnings** (~29 MB self-contained EXE).

## Current limitations

- **Predefined-node-only wiring.** `Configure` runs after the predefined
  tree is loaded. To inject dynamic nodes, override
  `CreateAddressSpaceAsync` in another partial (the generator's
  `CreateAddressSpaceAsync` is virtual).
- **Multi-namespace requires factory subclassing.** The default factory
  reports a single namespace; subclass `NamespacesUris` to add more.
- **No browse-path wildcards** (`*`, `**`). Wire each path explicitly.
- **HistoryRead/Update** integration is delegate-only in v1; deeper
  paging/queueing still requires `INodeManager2` work.

## Sample

- `Applications/MinimalBoilerServer/` — a fully self-contained,
  NativeAOT single-file Boiler server. Read it top-to-bottom in
  &lt;200 lines.
- `Applications/MinimalCalcServer/` — a calculator server that
  exercises the typed
  [methods-with-arguments OnCall overloads](#methods-with-arguments--typed-oncall-overloads)
  end-to-end (sync `int+int → int`, async `double+double → double`,
  sync `string+string → string`).
