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
- Alarms and conditions client (`Libraries/Opc.Ua.Client.Alarms`)
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
| `Opc.Ua.Client.Alarms` (within `Opc.Ua.Client`) | `builder.AddAlarms()`        | `IOpcUaBuilder`          | —       | —                        |
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

The `IConfiguration` / `IConfigurationSection` overloads are
**AOT-safe** on every library. Each DI-emitting library opts into the
.NET 8+ [Configuration Binding Source Generator](https://learn.microsoft.com/dotnet/core/extensions/configuration-generator)
(`<EnableConfigurationBindingGenerator>true</EnableConfigurationBindingGenerator>`),
which replaces the reflection-based binder with statically-generated
[C# 12 interceptors](https://learn.microsoft.com/dotnet/csharp/whats-new/csharp-12#interceptors).
The `Tests/Opc.Ua.Aot.Tests` project verifies that `dotnet publish`
under `PublishAot=true` produces zero `IL2026` / `IL3050` warnings
from the DI surface.

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

### First-class server options

In addition to the basic application-identity / endpoint / PKI knobs
covered above, `OpcUaServerOptions` exposes the following first-class
properties (bindable from `IConfiguration` or set via the
`Action<OpcUaServerOptions>` overload). Anything not listed here remains
reachable through `ConfigureBuilder`.

| Property | Underlying builder call | Purpose |
|----------|-------------------------|---------|
| `IncludeEccPolicies` | `AddEccSignAndEncryptPolicies()` | Add ECC sign-and-encrypt security policies. Off by default. |
| `UserTokenPolicies` | `AddUserTokenPolicy(UserTokenType)` | List of user-token policies advertised on every endpoint. Defaults to `Anonymous` when empty. |
| `MaxMessageSize` | `SetMaxMessageSize(int)` | Transport quota in bytes; `null` keeps the stack default. |
| `OperationTimeoutMs` | `SetOperationTimeout(int)` | Transport operation timeout in ms; `null` keeps the stack default. |
| `RejectSHA1Certificates` | `SetRejectSHA1SignedCertificates(bool)` | Security hardening — defaults to `true`. |
| `MinCertificateKeySize` | `SetMinimumCertificateKeySize(ushort)` | Security hardening — defaults to `2048`. Set to `0` to keep the stack default. |
| `RegistrationEndpointUrl` | `SetRegistrationEndpoint(EndpointDescription)` | LDS/GDS endpoint URL the server registers itself with on startup. |
| `ReverseConnect` | `SetReverseConnect(ReverseConnectServerConfiguration)` | Server-side reverse-connect clients (see below). |
| `OperationLimits` | `SetOperationLimits(OperationLimits)` | Per-service node limits (max nodes per read/write/browse/...). |

### Server-side reverse connect

A server can dial back to clients via reverse-hello using
`OpcUaServerOptions.ReverseConnect`. The data binds directly from
`OpcUa:Server:ReverseConnect`:

```jsonc
{
  "OpcUa": {
    "Server": {
      "ReverseConnect": {
        "ConnectIntervalMs": 15000,
        "ConnectTimeoutMs": 30000,
        "RejectTimeoutMs": 60000,
        "Clients": [
          {
            "EndpointUrl": "opc.tcp://client.example.com:4841",
            "Timeout": 30000,
            "MaxSessionCount": 1,
            "Enabled": true
          }
        ]
      }
    }
  }
}
```

Equivalent code-only configuration:

```csharp
services.AddOpcUa().AddServer(o =>
{
    o.ReverseConnect = new ServerReverseConnectOptions();
    o.ReverseConnect.Clients.Add(new ServerReverseConnectClientOptions
    {
        EndpointUrl = "opc.tcp://client.example.com:4841",
        Enabled = true
    });
});
```

### Operation limits

```csharp
services.AddOpcUa().AddServer(o =>
{
    o.OperationLimits = new OperationLimitsOptions
    {
        MaxNodesPerRead = 1000,
        MaxNodesPerWrite = 1000,
        MaxNodesPerBrowse = 1000,
        MaxMonitoredItemsPerCall = 5000
    };
});
```

Bindable from `OpcUa:Server:OperationLimits`. Any value left at zero is
treated as "unlimited" by the OPC UA server stack.

### User token policies

```csharp
services.AddOpcUa().AddServer(o =>
{
    o.UserTokenPolicies.Add(new OpcUaUserTokenPolicy
    {
        TokenType = UserTokenType.UserName
    });
    o.UserTokenPolicies.Add(new OpcUaUserTokenPolicy
    {
        TokenType = UserTokenType.Certificate
    });
});
```

Bindable from `OpcUa:Server:UserTokenPolicies`. When the list is empty
the hosted service falls back to a single `Anonymous` policy.

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

## Alarms and conditions

```csharp
services.AddOpcUa().AddClient(/* … */).AddAlarms();
```

Registers a singleton `AlarmClientFactory` so DI-hosted client
applications can obtain a Part 9 `AlarmClient` per connected session
without `new`-ing one manually. Source-generated event records
(`ConditionTypeRecord`, `AlarmConditionTypeRecord`,
`DialogConditionTypeRecord`, all subtypes including vendor extensions)
and the streaming extensions (`SubscribeAlarmsAsync`,
`SubscribeConditionsAsync`, `SubscribeDialogsAsync`) require no
registration — they compose naturally with the
`ManagedSession.DefaultStreaming` surface that `AddClient(...)`
already wires up.

```csharp
ManagedSession session = await sessionFactory(ct);
var factory = sp.GetRequiredService<AlarmClientFactory>();
AlarmClient alarms = factory.Create(session);

// Acknowledge an alarm:
await alarms.AcknowledgeAsync(conditionId, eventId,
    new LocalizedText("en", "Acknowledged"), ct);

// Stream typed records via the session's default streaming subscription:
await foreach (ConditionTypeRecord record in session.DefaultStreaming
    .SubscribeAlarmsAsync(ObjectIds.Server, ct: ct))
{
    /* … */
}
```

The non-DI path (`session.GetAlarmClient()` extension and the public
`AlarmClient` constructor) remains available for callers that do not
use the DI infrastructure. See
[Alarms and Conditions](AlarmsAndConditions.md) for the full
developer guide.

### Client-side reverse connect

When `OpcUaClientOptions.ReverseConnect` is set, the DI container
registers a singleton `ReverseConnectManager` that opens the configured
listener endpoints on first resolution. Inbound reverse-hello messages
are surfaced via
`ReverseConnectManager.WaitForConnectionAsync(endpointUrl, serverUri, ct)`,
and the values are also mirrored into
`ApplicationConfiguration.ClientConfiguration.ReverseConnect`.

```jsonc
{
  "OpcUa": {
    "Client": {
      "ReverseConnect": {
        "HoldTimeMs": 15000,
        "WaitTimeoutMs": 20000,
        "ClientEndpointUrls": [
          "opc.tcp://0.0.0.0:4841"
        ]
      }
    }
  }
}
```

Equivalent code-only:

```csharp
services.AddOpcUa().AddClient(opt =>
{
    opt.Configuration = applicationConfiguration;
    opt.ReverseConnect = new ClientReverseConnectOptions();
    opt.ReverseConnect.ClientEndpointUrls.Add("opc.tcp://0.0.0.0:4841");
});

// Resolve the manager and await an inbound reverse-hello connection:
var reverseConnect = sp.GetRequiredService<ReverseConnectManager>();
ITransportWaitingConnection connection =
    await reverseConnect.WaitForConnectionAsync(endpointUrl, serverUri: null, ct);
// pass `connection` to Session.Create / DefaultSessionFactory.RecreateAsync
```

Note: the `Func<CancellationToken, Task<ManagedSession>>` delegate
registered by `AddClient` does **not** automatically consume the
reverse-connect manager for initial connection — it still dials the
configured endpoint outbound. Use the `ReverseConnectManager` directly
when the server initiates the session. The
`ReverseConnect` configuration is also consumed by `Session` /
`SessionReconnectHandler` for reconnect-via-reverse-hello scenarios.

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
        o.EnableMulticast = true;            // LDS-ME multicast advertisement
        o.MulticastLoopbackOnly = false;     // set true for in-process tests
        o.ServerCapabilities.Add("DA");      // extra capabilities (LDS / LDS-ME are always implicit)
    });
```

Throws on a second `.AddLdsServer(...)`. The server is advertised as
`ApplicationType.DiscoveryServer` (the LDS class promotes the type after
`ApplicationConfiguration.CreateAsync`).

`MulticastLoopbackOnly` restricts the mDNS announcer to the loopback NIC
— intended for in-process tests where LDS-ME traffic must stay local.
`ServerCapabilities` is additive — `LDS` is always included, plus
`LDS-ME` when `EnableMulticast` is `true`.

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

Every `.AddXxx(...)` overload — both the `Action<TOptions>` shape and
the `IConfiguration` / `IConfigurationSection` shapes — is AOT-safe.
The .NET 8+ Configuration Binding Source Generator is enabled on every
library that performs configuration binding
(`<EnableConfigurationBindingGenerator>true</EnableConfigurationBindingGenerator>`),
so the reflection-based binder is replaced by statically-generated
[C# 12 interceptors](https://learn.microsoft.com/dotnet/csharp/whats-new/csharp-12#interceptors)
at compile time.

```csharp
services.AddOpcUa()
    .AddServer(builder.Configuration.GetSection("OpcUa:Server"))  // bind from appsettings.json — AOT-safe
    .AddNodeManager<MyAotNodeManagerFactory>();
```

`Action<TOptions>` (code-only) and `IConfiguration` overloads can be
mixed freely; consumers no longer need to choose between them for AOT
compatibility.

Notes:

- The source generator targets net8.0+. On older TFMs (net48 /
  netstandard2.0 / netstandard2.1) the generator is a no-op and the
  reflection-based binder is used — those TFMs don't support
  PublishAot anyway.
- Options properties whose type is an interface or a non-default-
  constructible class (e.g. `ApplicationConfiguration`,
  `IUserIdentity`, `ISubscriptionEngineFactory`) are silently skipped
  by the generator with an informational `SYSLIB1100` / `SYSLIB1101`
  diagnostic. Those properties are runtime-only — set them in code,
  not in `appsettings.json`. The affected libraries suppress those
  diagnostics in their csproj.
- The `Tests/Opc.Ua.Aot.Tests` project verifies the end-to-end AOT
  path: build + AOT publish produce **zero** `IL2026` / `IL3050`
  warnings from any DI extension.

For AOT consumers, configure options through code or configuration —
both are supported:

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
