# OPC UA .NET Standard — Client

`OPCFoundation.NetStandard.Opc.Ua.Client` is the high-level OPC UA
client library. It contains the `Session` and `ManagedSession`
implementations, the V2 subscription engine, the central
`ClientChannelManager` (channel sharing + transparent reconnect), the
`ReverseConnectManager`, and the fluent `builder.AddClient(...)`
DI surface.

## Overview

Use this package for any application that needs to:

- open OPC UA sessions to a server (forward or reverse-connect),
- subscribe to monitored items (with the V2 streaming engine or the
  classic Session.AddSubscription API),
- call methods, browse the address space, read / write attributes,
- transfer subscriptions between sessions,
- discover endpoints via `DiscoveryClient`.

`Session` and `ManagedSession` also implement the optional
`ISessionBindingProvider` capability. `CreateBindingAsync` returns an
`ISessionClient` pinned to its authenticated native channel generation, session
incarnation and namespace/server maps. Bound requests reject invalidation rather
than transparently retrying on a replacement peer. Generated ObjectType clients
consume the binding directly; closing it does not close the owning session.
Unsupported custom adapters/transports report `BadNotSupported`.

Model libraries can opt into `ModelSourceGeneratorClientAccessorsOnly` to emit
typed variable views and placeholder enumerators over their existing generated
proxies. This package supplies the reusable `ClientVariable<T>`, `ClientRead<T>`
and `ClientModelAccess` runtime. These helpers use the proxy's existing `ISession`
or `ManagedSession`, preserve value quality and timestamps, and reuse streaming
subscriptions. See the [source generator guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/tools/Opc.Ua.SourceGeneration/readme.md#optional-client-member-accessors)
for configuration.

## Getting started

```csharp
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua;
using Opc.Ua.Client;

services
    .AddOpcUa()
    .AddClient(opt =>
    {
        opt.ApplicationName = "MyClient";
        opt.ApplicationUri = "urn:localhost:MyClient";
        opt.ProductUri = "uri:example.com:MyClient";
        opt.Session = new ManagedSessionOptions
        {
            Endpoint = endpoint,
        };
    });

// Resolve and connect on first use; the connected ManagedSession is cached:
var sessionFactory =
    provider.GetRequiredService<Func<CancellationToken, Task<ManagedSession>>>();
ManagedSession session = await sessionFactory(ct);
```

Applications that already own a classic configuration XML file can pass
it directly — `AddClient("MyClient.Config.xml", ...)`, or as a `Stream`
for embedded resources — and keep every setting from the file while
adopting dependency injection. The document loads on first use; set
`OpcUaClientOptions.LoadConfigurationOnStart` to have the host load and
validate it during `StartAsync`, and read it back through the public
`OpcUaClientOptions.ConfigurationProvider`.

## Experimental recoverable execution

`ManagedSessionBuilder.PreferRecoverableExecution()` or
`ManagedSessionOptions.RecoverableExecution` selects the default-off RSE prototype
behind the existing asynchronous `ISession` Read/Write/Call interface. Typed
helpers and generated Method proxies retain their normal signatures.
Ordinary fallback is permitted only after conclusive unsupported discovery
**before Submit is attempted**. Once Submit has been attempted, the Client
never falls back or replays the original request—even if Submit returns
`BadServiceUnsupported`.

Use `session.BeginAcknowledgementScope(requireRecoverableReceipt: true)` when
external storage or an RPC handoff must finish before acknowledgement. An
ordinary fallback has no receipt and cannot produce a successful no-op ACK.
The binding uses the experimental OPC 10000-29 draft 0.4.0 manager Methods:
`Submit`, `GetResult`, `Acknowledge`, `Cancel`, and `Abandon`. Session recovery
remains the default; Owner and Durable are strict opt-ins that require an
application-qualified identity provider for authenticated application and user
continuity. Durable additionally requires protected, qualified checkpoints;
Owner can use checkpoints as well. The experimental contract is not an adopted
standard. See the
[RSE guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/rse.md)
for the complete Client workflow, qualification requirements, and validation
scope.

## Reverse connect lifecycle

`ReverseConnectManager` uses an asynchronous, cancellation-aware lifecycle:

```csharp
await using var manager = new ReverseConnectManager(telemetry);
manager.AddEndpoint(new Uri("opc.tcp://client-host:65300"));
await manager.StartServiceAsync(reverseConnectConfiguration, ct);

ITransportWaitingConnection connection =
    await manager.WaitForConnectionAsync(serverEndpoint, serverUri, ct);
```

The synchronous `StartService`, `RegisterWaitingConnection`, and `Dispose` methods remain as obsolete compatibility wrappers. New applications should use `StartServiceAsync`, `RegisterWaitingConnectionAsync`, and `DisposeAsync`.

When configured through `AddClient(...)`, a Generic Host starts the singleton manager eagerly through an internal hosted service. Applications using only `ServiceCollection` get lazy startup from `WaitForConnectionAsync` or `RegisterWaitingConnectionAsync`. Register an `IReverseConnectConfigurationProvider` to asynchronously validate or transform the effective listener configuration before activation.

## Programs

`ProgramClient` adds Part 10 discovery, lifecycle and diagnostic/result reads
to generated Program proxies. Program event streams retain captured
intermediate results instead of rereading changing state Variables. See the
[Programs guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/Programs.md).

## Target frameworks

`net48`, `net8.0`, `net9.0`,
`net10.0`.

## Additional documentation

See the [main repository README](https://github.com/OPCFoundation/UA-.NETStandard)
and the [Sessions guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/Sessions.md)
for the session / subscription / channel-manager design, the
[Reverse Connect guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/ReverseConnect.md),
and the
[Transfer Subscription guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/TransferSubscription.md).
