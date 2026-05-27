# Companion specification libraries

This guide describes how to package an OPC UA companion specification
as a reusable .NET library set — a *model* assembly (source-generated
NodeId tables, DataTypes and ObjectType client proxies), a *server*
assembly (an `AsyncCustomNodeManager` subclass plus a factory), and a
*client* assembly (high-level helpers composing the generated proxies).
The shipped `Opc.Ua.WotCon`, `Opc.Ua.Gds.Common` and `Opc.Ua.Di`
libraries follow this pattern.

The running example is the **OPC UA Device Integration (DI)** library
trio that ships in the repo:

| Library | Role | Project file |
|---------|------|--------------|
| `Opc.Ua.Di` | Model: NodeId tables, DataTypes, ObjectType proxies | `Libraries/Opc.Ua.Di/Opc.Ua.Di.csproj` |
| `Opc.Ua.Di.Server` | `DiNodeManager` + `DiNodeManagerFactory` | `Libraries/Opc.Ua.Di.Server/Opc.Ua.Di.Server.csproj` |
| `Opc.Ua.Di.Client` | `DiDeviceClient`, `DiDiscoveryClient`, `DeviceIdentification` | `Libraries/Opc.Ua.Di.Client/Opc.Ua.Di.Client.csproj` |

A full consumer (server + simulation + alarms) using these libraries
lives at `Applications/MinimalPumpServer/`.

## 1. The model library

The model assembly holds the source-generated representation of the
companion spec: NodeId / BrowseName constants, DataType classes,
ObjectType client proxies, and the `Add{Prefix}` extension method
that populates a `NodeStateCollection` with the predefined nodes.

### Project file

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>$(AssemblyPrefix).Di</AssemblyName>
    <TargetFrameworks>$(LibTargetFrameworks)</TargetFrameworks>
    <PackageId>$(PackagePrefix).Opc.Ua.Di</PackageId>
    <RootNamespace>Opc.Ua.Di</RootNamespace>
    <Description>OPC UA Device Integration (DI, OPC 10000-100), Package Metadata, and Onboarding information models</Description>
  </PropertyGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="Opc.Ua.Di.Server" />
    <InternalsVisibleTo Include="Opc.Ua.Di.Client" />
    <InternalsVisibleTo Include="Opc.Ua.Di.Tests" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\Stack\Opc.Ua.Core\Opc.Ua.Core.csproj" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\Tools\Opc.Ua.SourceGeneration\Opc.Ua.SourceGeneration.csproj">
      <OutputItemType>Analyzer</OutputItemType>
      <ReferenceOutputAssembly>false</ReferenceOutputAssembly>
    </ProjectReference>
  </ItemGroup>
  <Import Project="..\..\Tools\Opc.Ua.SourceGeneration\OPCFoundation.Opc.Ua.SourceGeneration.props" />
  <PropertyGroup>
    <ModelSourceGeneratorVersion>v105</ModelSourceGeneratorVersion>
    <ModelSourceGeneratorExclude>Draft</ModelSourceGeneratorExclude>
    <ModelSourceGeneratorUseAllowSubtypes>true</ModelSourceGeneratorUseAllowSubtypes>
  </PropertyGroup>
  <ItemGroup>
    <AdditionalFiles Include="Design\OpcUaDiModel.xml" />
    <AdditionalFiles Include="Design\OpcUaDiModel.csv" />
    <!-- Optional additional model sources for the same library. -->
    <AdditionalFiles Include="Design\OpcUaOnboardingModel.xml" />
    <AdditionalFiles Include="Design\OpcUaOnboardingModel.csv" />
  </ItemGroup>
</Project>
```

### ModelDesign XML vs NodeSet2 XML

Two input shapes work with the source generator:

| Input | Where it comes from | Pros |
|-------|---------------------|------|
| ModelDesign XML (`OpcUaXxxModel.xml` + `.csv`) | UA-ModelCompiler `Design.v105/` directory | Designed for code-gen; richer metadata (`Prefix`, `XmlPrefix`); deterministic namespace mapping |
| NodeSet2 XML (`Opc.Ua.Xxx.NodeSet2.xml`) | OPCFoundation/UA-Nodeset GitHub repo | Authoritative spec deliverable; one file per companion spec |

**Prefer ModelDesign XML for new libraries.** It gives you control
over the generated C# namespace via the `<opc:Namespace Prefix="...">`
attribute. The DI library uses `Prefix="Opc.Ua.Di"` so its generated
types live under `Opc.Ua.Di` instead of an arbitrary short prefix.

NodeSet2 XML still works for consumers (see the
[Multi-model composition](SourceGeneratedNodeManagers.md#multi-model-composition)
loader for the runtime-load path).

### What the model library exposes

Once built, the consumer gets:

```csharp
// NodeId / BrowseName tables
Opc.Ua.Di.ObjectTypes.DeviceType
Opc.Ua.Di.ObjectTypes.FunctionalGroupType
Opc.Ua.Di.ObjectTypes.TopologyElementType
Opc.Ua.Di.Variables.<…>
Opc.Ua.Di.BrowseNames.<…>

// Namespace constants
Opc.Ua.Di.Namespaces.OpcUaDi  // "http://opcfoundation.org/UA/DI/"

// Encodeable / NodeState population
new NodeStateCollection().AddOpcUaDi(systemContext);

// ObjectType client proxies (for the client library to compose)
Opc.Ua.Di.DeviceTypeClient
Opc.Ua.Di.TopologyElementTypeClient
Opc.Ua.Di.FunctionalGroupTypeClient
```

## 2. The server library

The server assembly contributes an `AsyncCustomNodeManager` subclass
plus an `IAsyncNodeManagerFactory`. The node manager loads the
predefined nodes from the model library's `Add{Prefix}` extension and
exposes the conventional override points (`AddBehaviourToPredefinedNodeAsync`,
`CreateAddressSpaceAsync`) for late binding.

### Project file

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>$(AssemblyPrefix).Di.Server</AssemblyName>
    <DefineConstants>$(DefineConstants);OPCUA_INCLUDE_ASYNC</DefineConstants>
    <TargetFrameworks>$(LibTargetFrameworks)</TargetFrameworks>
    <RootNamespace>Opc.Ua.Di.Server</RootNamespace>
    <Description>OPC UA Device Integration (DI, OPC 10000-100) server class library</Description>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Opc.Ua.Server\Opc.Ua.Server.csproj" />
    <ProjectReference Include="..\Opc.Ua.Di\Opc.Ua.Di.csproj" />
  </ItemGroup>
</Project>
```

### Node manager pattern

```csharp
public class DiNodeManager : AsyncCustomNodeManager, INodeIdFactory
{
    public const string DiNamespaceUri = global::Opc.Ua.Di.Namespaces.OpcUaDi;

    public DiNodeManager(IServerInternal server, ApplicationConfiguration cfg)
        : base(server, cfg, server.Telemetry.CreateLogger<DiNodeManager>(),
               DiNamespaceUri)
    {
        SystemContext.NodeIdFactory = this;
    }

    protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
        ISystemContext context, CancellationToken ct = default)
    {
        return new ValueTask<NodeStateCollection>(
            new NodeStateCollection().AddOpcUaDi(context));
    }

    public override NodeId New(ISystemContext ctx, NodeState node)
    {
        if (node is BaseInstanceState instance && instance.Parent != null)
        {
            string parentId = instance.Parent.NodeId.IdentifierAsString;
            return new NodeId(
                $"{parentId}_{instance.SymbolicName}",
                DiNamespaceIndex);
        }
        return node.NodeId;
    }
}

public sealed class DiNodeManagerFactory : IAsyncNodeManagerFactory
{
    public ArrayOf<string> NamespacesUris =>
        new string[] { DiNodeManager.DiNamespaceUri };

    public ValueTask<IAsyncNodeManager> CreateAsync(
        IServerInternal server,
        ApplicationConfiguration configuration,
        CancellationToken ct = default)
        => new(new DiNodeManager(server, configuration));
}
```

### Promoting passive nodes to active behaviour

Override `AddBehaviourToPredefinedNodeAsync` to attach typed behaviour
on top of generic state classes — useful when the model declares an
abstract base but you want a concrete subclass at runtime. See
`WotConnectivityNodeManager.cs` and
`Libraries/Opc.Ua.Gds.Server.Common/ApplicationsNodeManager.cs` for
worked examples.

## 3. The client library

The client assembly **composes** the source-generated ObjectType
client proxies (no inheritance) and exposes a developer-friendly
surface over the raw `ISession` calls.

### Project file

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>$(AssemblyPrefix).Di.Client</AssemblyName>
    <TargetFrameworks>$(LibTargetFrameworks)</TargetFrameworks>
    <RootNamespace>Opc.Ua.Di.Client</RootNamespace>
    <Description>OPC UA Device Integration (DI, OPC 10000-100) client class library</Description>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Opc.Ua.Client\Opc.Ua.Client.csproj" />
    <ProjectReference Include="..\Opc.Ua.Di\Opc.Ua.Di.csproj" />
  </ItemGroup>
</Project>
```

### Client wrapper pattern

A high-level wrapper holds the session, the target node id, and an
inner proxy instance. Method wrappers forward to the proxy; browse /
read helpers bypass the proxy when they need raw session access.

```csharp
public sealed class DiDeviceClient
{
    public DiDeviceClient(
        ISession session, NodeId deviceId, ITelemetryContext telemetry)
    {
        Session = session;
        DeviceNodeId = deviceId;
        Proxy = new global::Opc.Ua.Di.DeviceTypeClient(
            session, deviceId, telemetry);
    }

    public ISession Session { get; }
    public NodeId DeviceNodeId { get; }
    public global::Opc.Ua.Di.DeviceTypeClient Proxy { get; }

    public async ValueTask<DeviceIdentification> ReadIdentificationAsync(
        CancellationToken ct = default)
    {
        // Use TranslateBrowsePaths + Read to fetch the 8 nameplate
        // properties in a single network round-trip.
        // ...
    }

    public async ValueTask<IReadOnlyList<NodeId>> BrowseFunctionalGroupsAsync(
        CancellationToken ct = default) { ... }
}
```

The full implementation lives in
`Libraries/Opc.Ua.Di.Client/DiDeviceClient.cs`.

### Browse-by-type discovery

Use a `BrowseDescription` filtered on the model's TypeDefinitionId to
discover all instances of a given type:

```csharp
public static IAsyncEnumerable<(NodeId Id, string Name)>
    EnumerateDevicesAsync(
        ISession session, ITelemetryContext telemetry,
        CancellationToken ct = default)
{
    ExpandedNodeId deviceTypeId = global::Opc.Ua.Di.ObjectTypeIds.DeviceType;
    // ... BrowseAsync starting from ObjectsFolder, filtering on TypeDef ...
}
```

See `Libraries/Opc.Ua.Di.Client/DiDiscoveryClient.cs` for the full
implementation.

## 4. The host application

A consumer combines one or more model libraries (DI here) with
either upper-layer companion-spec libraries (when available) or
**locally source-generated** types from NodeSet2 / ModelDesign
inputs embedded in the consuming application.

### Source-generated locally (preferred — no runtime XML parsing)

When ModelDesign XMLs or NodeSet2 files are added to the application
project as `<AdditionalFiles>`, the source generator emits typed
classes and `AddXxx(NodeStateCollection, ISystemContext)` extension
methods that consumers wire into `ModelLoaderBuilder`:

```xml
<ItemGroup>
  <!-- DI provided via ProjectReference; do not regenerate locally. -->
  <AdditionalFiles Include="Model/Opc.Ua.Di.NodeSet2.xml">
    <ModelSourceGeneratorIgnore>true</ModelSourceGeneratorIgnore>
  </AdditionalFiles>
  <!-- Source-generate Machinery + Pumps locally. -->
  <AdditionalFiles Include="Model/Opc.Ua.Machinery.NodeSet2.xml">
    <ModelSourceGeneratorPrefix>Opc.Ua.Machinery</ModelSourceGeneratorPrefix>
  </AdditionalFiles>
  <AdditionalFiles Include="Model/Opc.Ua.Pumps.NodeSet2.xml">
    <ModelSourceGeneratorPrefix>Opc.Ua.Pumps</ModelSourceGeneratorPrefix>
  </AdditionalFiles>
</ItemGroup>
```

The `LoadPredefinedNodesAsync` override then has no runtime XML
parsing — the generator-emitted `AddOpcUaMachinery` /
`AddOpcUaPumps` extension methods stamp the predefined nodes:

```csharp
protected override ValueTask<NodeStateCollection> LoadPredefinedNodesAsync(
    ISystemContext context, CancellationToken ct = default)
{
    NodeStateCollection nodes = new ModelLoaderBuilder()
        .AddModel((coll, ctx) => coll.AddOpcUaDi(ctx))
        .AddModel((coll, ctx) => coll.AddOpcUaMachinery(ctx))
        .AddModel((coll, ctx) => coll.AddOpcUaPumps(ctx))
        .Build(new NodeStateCollection(), context);
    return new ValueTask<NodeStateCollection>(nodes);
}
```

This pattern is used by `Applications/MinimalPumpServer` and is
equivalent to a library-per-spec layout but avoids splitting the
consumer into multiple assemblies. Cross-namespace type references
(e.g. Machinery types inheriting from DI types) resolve correctly
because the generator reads `[assembly: ModelDependencyAttribute]`
from referenced assemblies and rewrites dependency prefixes
accordingly — see `Docs/ModelDependencies.md`.

### Runtime NodeSet2 import fallback

For cases where neither a library reference nor a local source-gen
input is acceptable (e.g. dynamic / pluggable specs not known at
compile time), the runtime `IModelLoaderBuilder.ImportNodeSet`
overload accepts an embedded resource stream:

```csharp
Assembly asm = typeof(PumpNodeManager).Assembly;
NodeStateCollection nodes = new ModelLoaderBuilder()
    .AddModel((coll, ctx) => coll.AddOpcUaDi(ctx))
    .ImportEmbeddedNodeSet(asm, "Opc.Ua.Machinery.NodeSet2.xml")
    .Build(new NodeStateCollection(), context);
```

This path parses the NodeSet2 XML at startup, so it costs more
allocations and CPU. Prefer the source-generated path whenever the
spec is known at compile time.

See `Applications/MinimalPumpServer/PumpNodeManager.cs` for the full
end-to-end source-generated consumer.

## 4a. DI hosting integration (`AddOpcUa()` extensions)

Each DI library trio plugs into the unified
`AddOpcUa().AddServer(...)` Microsoft.Extensions DI hosting pattern via
two extension method bundles:

| Extension | Library | Purpose |
|-----------|---------|---------|
| `IOpcUaServerBuilder.AddOpcUaDi()` | `Opc.Ua.Di.Server` | Registers `DiNodeManagerFactory` so a plain DI server (no companion specs) can be hosted. |
| `IOpcUaServerBuilder.ConfigureDevicesFor<TNodeManager>(action)` | `Opc.Ua.Di.Server` | Declaratively materialises devices at startup. The delegate runs once the manager's address space is fully wired. |
| `IOpcUaClientBuilder.AddOpcUaDi()` | `Opc.Ua.Di.Client` | Registers `IDiDiscoveryService` and a lazy `Func<NodeId, CancellationToken, ValueTask<DiDeviceClient>>` factory. |

### Plain DI server (no companion spec)

```csharp
services.AddOpcUa()
    .AddServer(o =>
    {
        o.ApplicationName = "MyDiServer";
        o.EndpointUrls.Add("opc.tcp://localhost:48010/MyDiServer");
    })
    .AddOpcUaDi()
    .ConfigureDevicesFor<DiNodeManager>(async ctx =>
    {
        var sensor = await ctx.CreateDeviceAsync(
            new QualifiedName("Sensor #1", ctx.Manager.DiNamespaceIndex));
        sensor.WithIdentification(id =>
        {
            id.Manufacturer = new LocalizedText("Acme");
            id.SerialNumber = "SN-001";
        });
    });
```

### Companion-spec server (Pumps + DI)

`AddOpcUaDi()` is for the plain DI manager. Companion specs that
already include DI in their composite manager (e.g. the pump server,
which loads DI + Machinery + Pumps via `IModelLoaderBuilder`) skip
`AddOpcUaDi()` and instead register their own factory directly:

```csharp
services.AddOpcUa()
    .AddServer(o => { o.ApplicationName = "MinimalPumpServer"; ... })
    .AddNodeManager<Pumps.PumpNodeManagerFactory>()
    .ConfigureDevicesFor<Pumps.PumpNodeManager>(async ctx =>
    {
        var pump = await ctx.CreateDeviceAsync(
            new QualifiedName("Pump #2", ctx.Manager.DiNamespaceIndex));
        pump.WithIdentification(id =>
        {
            id.Manufacturer = new LocalizedText("Acme Pumps Inc.");
            id.SerialNumber = "SN-DI-2";
            id.DeviceClass = "Pump";
        });
    });
```

`ConfigureDevicesFor<TNodeManager>` targets the supplied node-manager
type **including derived classes** (matching follows
`Type.IsAssignableFrom`). A delegate targeting `DiNodeManager` will run
against pump managers as well; the inverse (target `PumpNodeManager`)
will not run against plain `DiNodeManager` instances.

### How it wires together

1. `AddOpcUaDi()` (or, for companion specs, the registration of a
   DI-aware factory) places a singleton
   `IDiPostSetupRunner` into the DI container.
2. `ConfigureDevicesFor<TNodeManager>(action)` wraps the delegate in
   an `IDiPostSetupConfigurator` keyed by the manager type and adds it
   to the service collection.
3. `DiNodeManagerFactory` (and `PumpNodeManagerFactory`, etc.) inject
   the runner into the manager they create.
4. The manager's `CreateAddressSpaceAsync` calls `runner.RunAsync(this, ct)`
   after its address space is fully populated and (for companion-spec
   managers) the fluent builder has been sealed. The runner filters
   configurators by `TargetManagerType` and invokes each one in
   registration order.
5. Exceptions thrown from a configurator abort hosted-server startup
   with a diagnostic identifying the failing configurator.

### Client-side hosting

```csharp
services.AddOpcUa()
    .AddClient(o => { o.Configuration = ...; o.Session.Endpoint = ...; })
    .AddOpcUaDi();

// Then inject into your services:
public sealed class MyAppService(
    IDiDiscoveryService discovery,
    Func<NodeId, CancellationToken, ValueTask<DiDeviceClient>> deviceFactory)
{
    public async Task ReportAsync(CancellationToken ct)
    {
        await foreach (DeviceEntry entry in discovery.EnumerateDevicesAsync(ct))
        {
            DiDeviceClient device = await deviceFactory(entry.DeviceId, ct);
            DeviceIdentification id = await device.ReadIdentificationAsync(ct);
            // ...
        }
    }
}
```

`AddOpcUaDi()` on the client builder requires `AddClient(...)` to have
been called first — it depends on the managed-session accessor
registered by the client services.

## 5. Testing

Each library gets its own test project. Recommended split:

- `Tests/Opc.Ua.{Spec}.Tests/{Spec}ModelLoadingTests.cs` — verify that
  the `Add{Prefix}` extension populates a non-empty collection and that
  well-known TypeDefinitionIds are reachable from the generated tables.
- `Tests/Opc.Ua.{Spec}.Tests/{Spec}NodeManagerTests.cs` — server-side
  smoke tests: factory creates a manager, `LoadPredefinedNodesAsync`
  returns non-empty, namespace URIs match the spec.
- `Tests/Opc.Ua.{Spec}.Tests/Client/...` — client-side: use Moq to
  fake `ISession` and verify the high-level helpers issue the expected
  Read / Browse / Call requests.

`Tests/Opc.Ua.Di.Tests/` and `Tests/Opc.Ua.Pumps.Tests/` follow this
template.

## 6. Common pitfalls

- **Cross-namespace prefix mismatch**: source-generating a NodeSet2
  that imports DI emits `global::DI.*` references (the XmlPrefix), but
  the DI library uses `Opc.Ua.Di` (the ModelDesign Prefix). For
  consumer applications that import DI as a library, runtime-load
  upper-layer NodeSet2 XMLs via `IModelLoaderBuilder.ImportNodeSet`
  instead of source-generating them.
- **Generator analyzer reference**: the `<Analyzer>` reference to
  `Opc.Ua.SourceGeneration` is required for the
  `Add{Prefix}` extension to be emitted. Without it the library
  compiles but no generated code is present in the output.
- **Pre-Pumps Machinery dependency**: the Pumps companion spec
  (OPC 40223 v1.0) requires Machinery v1.01 (without IA). Newer
  Machinery (v1.04+) introduces an IA dependency. Pin against the
  `Pumps-1.0.0-2021-04-19` tag from `OPCFoundation/UA-Nodeset` when
  building a Pumps server.

## See also

- [Source-generated NodeManagers](SourceGeneratedNodeManagers.md) —
  the underlying source generator + fluent API.
- [Fluent API Gap Analysis](FluentApiGaps.md) — historical record of
  the gaps identified during the Pumps companion spec exercise and how
  each was resolved.
- [WoT Connectivity](WoTConnectivity.md) — second worked example of a
  companion spec library trio (model + server + client).
- [GDS Developer Guide](GDS.md) — third companion library trio, with
  the additional twist that the GDS itself defines part of the spec.
