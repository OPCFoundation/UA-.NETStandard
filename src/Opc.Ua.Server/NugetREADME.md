# OPC UA .NET Standard — Server

`OPCFoundation.NetStandard.Opc.Ua.Server` is the high-level OPC UA
server library. It contains `StandardServer`, the
`MasterNodeManager`, the core `NodeManager` / `INodeManager` surface,
the session manager, the subscription manager (including durable
subscriptions), the audit infrastructure, the request queue, and the
fluent `builder.AddServer(...)` DI surface.

## Overview

Reference this package from every OPC UA server executable. It
provides everything you need to implement a custom server on top of
the UASC channel pipeline shipped by
`OPCFoundation.NetStandard.Opc.Ua.Core`.

## Getting started

Implement your own `NodeManager` and register it with
`StandardServer`:

```csharp
public sealed class MyServer : StandardServer
{
    protected override MasterNodeManager CreateMasterNodeManager(
        IServerInternal server,
        ApplicationConfiguration configuration)
    {
        return new MasterNodeManager(server, configuration, null,
            new MyNodeManager(server, configuration));
    }
}
```

Then start the server via `ApplicationInstance.StartAsync` (in the
`OPCFoundation.NetStandard.Opc.Ua.Configuration` package), or host it
through the fluent `services.AddOpcUa().AddServer(...)` DI surface. The
raw-socket `opc.tcp://` binding is built in; reference the
`OPCFoundation.NetStandard.Opc.Ua.Bindings.Https` package to additionally
expose an `https://` endpoint.

To expose a NodeSet2 XML document without generating a NodeManager, register it through the startup-time runtime loader:

```csharp
services.AddOpcUa()
    .AddServer(options => { /* server options */ })
    .AddRuntimeNodeSet(
        "Models/MyModel.NodeSet2.xml",
        nodes => nodes.Variable<double>("Machines/Machine1/Temperature")
            .OnRead(ReadTemperature));
```

The runtime loader supports grouped file and stream sources, orders included models by `RequiredModel`, and uses the server's default runtime complex-type support. See the [Runtime NodeSets guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/RuntimeNodeSets.md).

To add or reload a NodeSet after the server is running, inject `INodeManagerLifecycle` (or use `StandardServer.NodeManagerLifecycle`) and call `AddRuntimeNodeSetAsync` / `ReloadRuntimeNodeSetAsync`. The returned generation-aware registration can later be removed with `RemoveAsync`. Active monitored items retain their identity when compatible nodes remain, publish one `BadNodeIdUnknown` when their node disappears, and automatically recover when a compatible Node with the same NodeId returns. Namespace indexes and compatible runtime DataType registrations remain stable for the server lifetime.

## Target frameworks

`net472`, `net48`, `netstandard2.1`, `net8.0`, `net9.0`,
`net10.0`.

## Additional documentation

See the [main repository README](https://github.com/OPCFoundation/UA-.NETStandard)
and the [Sessions guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/Sessions.md)
for the session / subscription model. Browse the
[documentation folder](https://github.com/OPCFoundation/UA-.NETStandard/tree/master/docs)
for guides on certificate management, transports, identity providers,
alarms & conditions, historical access, and more.
