# Source-Generated NodeManagers

This guide explains how to use the OPC UA stack source generator to emit a
ready-to-host `AsyncCustomNodeManager` for an information model design XML, and
how to wire callbacks (read/write/method/lifecycle) using the fluent
`INodeManagerBuilder` API. The combination is designed for **single-file,
NativeAOT-friendly** servers — see
`Applications/ConsoleBoilerServer` for the canonical sample.

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

        builder
            .Node("Boilers/Boiler #1")
            .OnNodeAdded((ctx, node) => /* ... */);
    }
}
```

Path syntax is `/`-separated **BrowseNames**, rooted at the model
namespace's predefined nodes. Optional `ns=N;` prefix lets you target a
different namespace.

The builder exposes:

| Method | Wires |
|--------|-------|
| `OnRead` / `OnReadAsync` | `BaseVariableState.OnReadValue` |
| `OnWrite` / `OnWriteAsync` | `BaseVariableState.OnWriteValue` |
| `OnCall` / `OnCallAsync` | `MethodState.OnCallMethod*` |
| `OnNodeAdded` / `OnNodeRemoved` | Lifecycle dispatch from `NotifyNodeAdded` |
| `OnEvent`, `OnConditionRefresh`, `OnHistoryRead`, `OnHistoryUpdate`, `OnMonitoredItemCreated` | Manager-level dispatch keyed by `NodeId` |

`INodeManagerBuilder.NodeManager` is typed as `object` because both
`INodeManager` and `IAsyncNodeManager` implementations are supported.
Cast it to your concrete manager type if you need direct access.

All resolution happens **once** during `CreateAddressSpaceAsync`,
against the in-memory predefined-node tree. There is no reflection, no
`Activator.CreateInstance`, no `Expression.Compile` — the whole pipeline
is NativeAOT-safe.

## Single-file `Program.cs` — what it looks like

```csharp
ITelemetryContext telemetry = DefaultTelemetry.Create(b => b.AddConsole());

var application = new ApplicationInstance(telemetry)
{
    ApplicationName = "MyServer",
    ApplicationType = ApplicationType.Server
};

await application.Build("urn:localhost:MyServer", "uri:opcfoundation.org:MyServer")
    .AsServer([$"opc.tcp://localhost:51210/MyServer"])
    .AddSignAndEncryptPolicies()
    .AddSecurityConfiguration(applicationCerts, "%LocalAppData%/MyServer/pki")
    .CreateAsync();

await application.CheckApplicationInstanceCertificatesAsync(/* … */);

var server = new MyServer(telemetry);
await application.StartAsync(server);

await Task.Delay(Timeout.Infinite, ctsCtrlC.Token);
await server.StopAsync();

internal sealed class MyServer : StandardServer
{
    public MyServer(ITelemetryContext t) : base(t) { }

    protected override void OnServerStarting(ApplicationConfiguration cfg)
    {
        base.OnServerStarting(cfg);
        // Resolves to the IAsyncNodeManagerFactory overload.
        AddNodeManager(new MyModel.MyModelNodeManagerFactory());
    }
}
```

That's the whole server. The Boiler version is in
`Applications/ConsoleBoilerServer/Program.cs`.

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

`Applications/ConsoleBoilerServer` publishes cleanly with **zero AOT/trim
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

`Applications/ConsoleBoilerServer/` — a fully self-contained, NativeAOT
single-file Boiler server. Read it top-to-bottom in &lt;200 lines.
