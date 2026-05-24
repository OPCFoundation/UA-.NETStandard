# Dependency Injection

This document describes the unified `Microsoft.Extensions.DependencyInjection`
surface for the OPC UA .NET Standard libraries. The surface is rooted in
a single `services.AddOpcUa()` call that returns an `IOpcUaBuilder` on
which every feature library hangs its own fluent `.AddXxx(...)` extension.

The DI surface is consistent across:

- The OPC UA Core stack (`Stack/Opc.Ua.Core`)
- Application configuration (`Libraries/Opc.Ua.Configuration`)
- The client (`Libraries/Opc.Ua.Client`)
- The complex types client (`Libraries/Opc.Ua.Client.ComplexTypes`)
- The server (`Libraries/Opc.Ua.Server`)
- The GDS client (`Libraries/Opc.Ua.Gds.Client.Common`)
- The GDS server (`Libraries/Opc.Ua.Gds.Server.Common`)
- The LDS server (`Libraries/Opc.Ua.Lds.Server`)
- The WoT Connectivity server (`Libraries/Opc.Ua.WotCon.Server`)
- The WoT Connectivity client (`Libraries/Opc.Ua.WotCon.Client`)

PubSub is **not** part of the DI surface.

The non-DI public constructors and factories of every library
(`new ApplicationInstance(telemetry)`, `new StandardServer(telemetry)`,
`new LdsServer(telemetry)`, `new ManagedSession(...)` etc.) remain
unchanged. Use DI when you want the .NET Generic Host to own application
lifetime, logging, and configuration; use the manual constructors when
you need finer control.

## Quick reference

| Feature library                | Method on `IOpcUaBuilder`               | Returns                  | Hosted? | Section                  |
|--------------------------------|------------------------------------------|--------------------------|---------|--------------------------|
| `Opc.Ua.Core` (root)           | `services.AddOpcUa()`                    | `IOpcUaBuilder`          | —       | —                        |
| `Opc.Ua.Configuration`         | `builder.AddApplicationInstance()`       | `IOpcUaBuilder`          | —       | —                        |
| `Opc.Ua.Client`                | `builder.AddClient(opt => …)`            | `IOpcUaClientBuilder`    | —       | `OpcUa:Client`           |
| `Opc.Ua.Client.ComplexTypes`   | `builder.AddComplexTypes()`              | `IOpcUaBuilder`          | —       | —                        |
| `Opc.Ua.Server`                | `builder.AddServer(opt => …)`            | `IOpcUaServerBuilder`    | yes     | `OpcUa:Server`           |
| `Opc.Ua.Gds.Client.Common`     | `builder.AddGdsClient(opt => …)`         | `IGdsClientBuilder`      | —       | `OpcUa:Gds:Client`       |
| `Opc.Ua.Gds.Server.Common`     | `builder.AddGdsServer(opt => …)`         | `IGdsServerBuilder`      | yes     | `OpcUa:Gds:Server`       |
| `Opc.Ua.Lds.Server`            | `builder.AddLdsServer(opt => …)`         | `ILdsServerBuilder`      | yes     | `OpcUa:Lds`              |
| `Opc.Ua.WotCon.Server`         | `builder.AddWotConServer(opt => …)`      | `IWotConServerBuilder`   | yes (via `AddServer`) | `OpcUa:WotCon:Server` |
| `Opc.Ua.WotCon.Client`         | `builder.AddWotConClient(opt => …)`      | `IOpcUaBuilder`          | —       | `OpcUa:WotCon:Client`    |

Server features marked **Hosted? = yes** register an `IHostedService` so
the .NET Generic Host (`Host.CreateApplicationBuilder(args)`) owns their
lifetime, certificate setup, and Ctrl+C / SIGTERM handling.

## Root: `services.AddOpcUa()`

`services.AddOpcUa()` is the only entry point. It does two things:

1. Registers `ITelemetryContext` as a `ServiceProviderTelemetryContext`
   singleton (via `TryAddSingleton`, so any prior user registration
   wins).
2. Returns an `IOpcUaBuilder` whose `.Services` property exposes the
   underlying `IServiceCollection` for advanced scenarios.

```csharp
using Microsoft.Extensions.DependencyInjection;

services.AddOpcUa();
```

`AddOpcUa` is idempotent. The returned `IOpcUaBuilder` derives from the
older `IDependencyInjectionBuilder` so existing
`builder.AddLogging()` / `builder.AddMetrics()` extensions still compile
unchanged. New `AddLogging(this IOpcUaBuilder)` /
`AddMetrics(this IOpcUaBuilder)` overloads return `IOpcUaBuilder` for
fluent chaining into feature methods:

```csharp
services.AddOpcUa()
    .AddLogging(b => b.AddConsole())
    .AddMetrics()
    .AddServer(o => /* … */)
    .AddNodeManager<MyNodeManagerFactory>();
```

## Options binding

Every feature `.AddXxx(...)` has three overloads:

```csharp
// 1. Action — AOT-safe, recommended for all consumers.
builder.AddServer(o => { o.ApplicationName = "MyServer"; /* … */ });

// 2. IConfiguration — bind from a configuration root's default section.
builder.AddServer(builder.Configuration);

// 3. IConfigurationSection — bind from an explicit section.
builder.AddServer(builder.Configuration.GetSection("MyApp:Server"));
```

The default section names are:

| Feature                        | Section                  |
|--------------------------------|--------------------------|
| Server                         | `OpcUa:Server`           |
| Client                         | `OpcUa:Client`           |
| GDS Client                     | `OpcUa:Gds:Client`       |
| GDS Server                     | `OpcUa:Gds:Server`       |
| LDS Server                     | `OpcUa:Lds`              |
| WoT Connectivity Server        | `OpcUa:WotCon:Server`    |
| WoT Connectivity Client        | `OpcUa:WotCon:Client`    |

The `IConfiguration` and `IConfigurationSection` overloads are
annotated `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`
because they use reflection-based binding. For Native AOT
applications, use the `Action<TOptions>` overload exclusively — the
existing `Opc.Ua.Aot.Tests` project demonstrates this pattern.

## Server feature

`builder.AddServer(o => …)` registers an OPC UA `StandardServer` as an
`IHostedService` via a private `OpcUaServerHostedService`. Endpoints,
PKI root, security policies, and the application instance certificate
are all set up on host startup.

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
        o.ApplicationUri = "urn:localhost:MyOrg:MyServer";
        o.ProductUri = "uri:myorg:myserver";
        o.AutoAcceptUntrustedCertificates = true;
        o.EndpointUrls.Add("opc.tcp://localhost:51210/MyServer");
    })
    .AddNodeManager<MyNodeManagerFactory>()       // IAsyncNodeManagerFactory
    .AddSyncNodeManager<LegacyNodeManagerFactory>(); // INodeManagerFactory

await builder.Build().RunAsync();
```

`.AddNodeManager<T>()` and `.AddSyncNodeManager<T>()` register the
factory under an `OpcUaServerNodeManagerRegistration` wrapper that is
**scoped to the regular server feature**. Node managers registered this
way are **not** visible to the GDS / LDS hosted services running in the
same container. See *Combined hosts* below.

`.AddServer(...)` throws `InvalidOperationException` on a second call:
at most one regular server may be registered per service collection.

For advanced configuration (custom security policies, custom security
stores), set `OpcUaServerOptions.ConfigureBuilder` — it receives the
underlying `IApplicationConfigurationBuilderServerSelected` between the
default policy/quota steps and `CreateAsync`.

## Client feature

`builder.AddClient(opt => …)` registers a lazy `ManagedSession` factory
delegate. The factory caches the connected session — subsequent awaits
return the same instance.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua.Client;

services
    .AddOpcUa()
    .AddClient(opt =>
    {
        opt.Configuration = applicationConfiguration;
        opt.Session = new ManagedSessionOptions
        {
            Endpoint = endpoint,
            ReconnectPolicy = new ReconnectPolicyOptions
            {
                Strategy = BackoffStrategy.Exponential
            }
        };
    });

// Resolve and connect on first use:
var sessionFactory = sp.GetRequiredService<Func<CancellationToken, Task<ManagedSession>>>();
ManagedSession session = await sessionFactory(ct);
```

`AddClient` also registers `ITelemetryContext`, `ISessionFactory` (a
`DefaultSessionFactory` configured with the V2 subscription engine),
`ManagedSessionFactory`, and the top-level `OpcUaClientOptions`.

## Complex types

```csharp
services.AddOpcUa().AddClient(/* … */).AddComplexTypes();
```

Registers a `ComplexTypeSystemFactory` (transient) that can be resolved
and used to build a `ComplexTypeSystem` for a connected session:

```csharp
var factory = sp.GetRequiredService<ComplexTypeSystemFactory>();
ComplexTypeSystem cts = factory.Create(session);
await cts.LoadAsync(...);
```

## Application instance (advanced)

```csharp
services.AddOpcUa().AddApplicationInstance();
```

Registers an `IApplicationInstanceFactory` singleton. Hosted server
services (regular, GDS, LDS, WotCon) call the factory to create their
own per-host `IApplicationInstance` instead of sharing a process-wide
singleton — this avoids configuration overwrite and certificate-lifecycle
conflicts when multiple servers coexist in one process. `AddServer` /
`AddGdsServer` / `AddLdsServer` register the factory automatically, so
calling `AddApplicationInstance()` explicitly is only needed when you
want the factory available outside the hosted-server context.

## GDS Client

```csharp
services.AddOpcUa().AddGdsClient(opt =>
{
    opt.SessionTimeout = TimeSpan.FromMinutes(2);
    opt.MaxConnectAttempts = 10;
});
```

Requires an `ApplicationConfiguration` registered in the container.
Resolves `GlobalDiscoveryServerClient` and `ServerPushConfigurationClient`
singletons.

## GDS Server

```csharp
services
    .AddOpcUa()
    .AddGdsServer(o =>
    {
        o.ApplicationName = "MyGds";
        o.ApplicationUri = "urn:localhost:MyOrg:MyGds";
        o.ProductUri = "uri:myorg:mygds";
        o.AutoAcceptUntrustedCertificates = true;
        o.EndpointUrls.Add("opc.tcp://localhost:58810/GlobalDiscoveryServer");
        o.AuthoritiesStorePath = "%LocalApplicationData%/OPC Foundation/pki/CA";
    })
    .AddApplicationsDatabase<MyApplicationsDatabase>()
    .AddCertificateGroup<MyCertificateGroup>()
    .AddCertificateRequest<MyCertificateRequest>()
    .AddUserDatabase<MyUserDatabase>();
    // Optionally:
    // .AddAccessTokenProvider<MyAccessTokenProvider>()
    // .AddKeyCredentialRequestStore<MyKeyCredentialRequestStore>()
    // .AddConfigurationDataStore<MyConfigurationDataStore>();
```

Throws on a second `.AddGdsServer(...)`. Auto-registers an internal
`GdsHostedServer : GlobalDiscoverySampleServer` and its
`ApplicationsNodeManager`. The pluggable services (`IApplicationsDatabase`,
`ICertificateGroup`, `ICertificateRequest`, etc.) are resolved from the
container at startup.

## LDS Server

```csharp
services
    .AddOpcUa()
    .AddLdsServer(o =>
    {
        o.ApplicationName = "MyLds";
        o.ApplicationUri = "urn:localhost:MyOrg:MyLds";
        o.ProductUri = "uri:myorg:mylds";
        o.EndpointUrls.Add("opc.tcp://localhost:4840/UADiscovery");
        o.EnableMulticast = true;  // optional LDS-ME multicast advertisement
    });
```

Throws on a second `.AddLdsServer(...)`. The server is advertised as
`ApplicationType.DiscoveryServer` (the LDS class promotes the type after
`ApplicationConfiguration.CreateAsync`).

## WoT Connectivity Server

The WoT Connectivity server lives inside a regular `StandardServer`, so
`AddWotConServer` must be combined with `AddServer`:

```csharp
services
    .AddOpcUa()
    .AddServer(o => /* regular server options */)
    .AddNodeManager<MyNodeManagerFactory>()  // your domain node managers
    .Services
    .AddOpcUa()                              // returns the same builder
    .AddWotConServer(o =>
    {
        o.AssetNamespaceUri = "http://myorg/UA/WoT-Con/Assets/";
        o.ThingDescriptionStorageFolder = "tds";
    })
    .AddAssetProvider<MyAssetProviderFactory>()
    .AddDiscoveryProvider<MyDiscoveryProvider>();
```

`AddWotConServer` registers the WoT `WotConnectivityNodeManagerFactory`
as an `OpcUaServerNodeManagerRegistration` so it attaches to the regular
server feature. `IWotAssetProviderFactory` and `IWotAssetDiscoveryProvider`
services registered in DI are picked up automatically.

## WoT Connectivity Client

```csharp
services.AddOpcUa().AddClient(/* … */).AddWotConClient();

// Resolve and connect on first use:
var wotClient = sp.GetRequiredService<Func<CancellationToken, Task<WotConnectivityClient>>>();
WotConnectivityClient client = await wotClient(ct);
```

The WoT client reuses the connected `ManagedSession` registered by
`AddClient(...)`.

## Combined hosts

You can run a regular server, a GDS server, and an LDS server inside a
single Generic Host. Each owns its own:

- `OpcUaServerOptions` / `GdsServerOptions` / `LdsServerOptions`
- PKI root and certificate
- Endpoint URLs and ports
- `IApplicationInstance` (created from the shared
  `IApplicationInstanceFactory`)
- `BackgroundService` (the registrations don't collide)

```csharp
builder.Services
    .AddOpcUa()
    .AddServer(o => /* regular server on 51210 */)
    .AddNodeManager<MyNodeManagerFactory>()
    .Services
    .AddOpcUa()
    .AddLdsServer(o => /* LDS on 4840 */)
    .Services
    .AddOpcUa()
    .AddGdsServer(o => /* GDS on 58810 */)
    .AddApplicationsDatabase<MyApplicationsDatabase>()
    .AddCertificateGroup<MyCertificateGroup>()
    .AddCertificateRequest<MyCertificateRequest>()
    .AddUserDatabase<MyUserDatabase>();
```

Node managers registered under a feature wrapper (e.g.
`OpcUaServerNodeManagerRegistration` for the regular server) are
isolated: the GDS / LDS hosted services do not see them.

## Native AOT

All `.AddXxx(...)` methods are AOT-compatible **when called with the
`Action<TOptions>` overload**. The `IConfiguration`-bound overloads use
reflection and are marked `[RequiresUnreferencedCode]` /
`[RequiresDynamicCode]`.

For AOT consumers, configure options through code:

```csharp
services.AddOpcUa()
    .AddServer(o =>
    {
        o.ApplicationName = "AotServer";
        o.ApplicationUri = "urn:host:AotServer";
        o.EndpointUrls.Add("opc.tcp://localhost:51210/AotServer");
        o.AutoAcceptUntrustedCertificates = true;
    })
    .AddNodeManager<MyAotNodeManagerFactory>();
```

The `Tests/Opc.Ua.Aot.Tests` project exercises this path end-to-end
under `PublishAot=true`.

## Telemetry

`AddOpcUa()` registers an `ITelemetryContext` that resolves the host's
`ILoggerFactory` on first use. To override:

```csharp
services.AddSingleton<ITelemetryContext>(myCustomTelemetry);
services.AddOpcUa();   // TryAddSingleton — custom one wins
```

To configure logging fluently:

```csharp
services.AddOpcUa()
    .AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information))
    .AddServer(/* … */);
```

## See also

- [Sessions](Sessions.md) — `ManagedSession`, reconnect, subscription engines.
- [Source Generated NodeManagers](SourceGeneratedNodeManagers.md) — `IAsyncNodeManagerFactory` from a model design XML.
- [Native AOT](NativeAoT.md) — AOT testing setup.
- [GDS Developer Guide](GDS.md) — GDS service interfaces and provider patterns.
- [WoT Connectivity](WoTConnectivity.md) — OPC 10100-1 information model.
- [Observability](Observability.md) — `ITelemetryContext` end-to-end.
