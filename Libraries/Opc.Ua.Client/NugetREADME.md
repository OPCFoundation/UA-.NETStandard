# OPC UA .NET Standard — Client

`OPCFoundation.NetStandard.Opc.Ua.Client` is the high-level OPC UA
client library. It contains the `Session` and `ManagedSession`
implementations, the V2 subscription engine, the central
`ClientChannelManager` (channel sharing + transparent reconnect), the
`ReverseConnectManager`, and the fluent `services.AddOpcUaClient()`
DI surface.

## Overview

Use this package for any application that needs to:

- open OPC UA sessions to a server (forward or reverse-connect),
- subscribe to monitored items (with the V2 streaming engine or the
  classic Session.AddSubscription API),
- call methods, browse the address space, read / write attributes,
- transfer subscriptions between sessions,
- discover endpoints via `DiscoveryClient`.

## Getting started

```csharp
using Microsoft.Extensions.DependencyInjection;
using Opc.Ua;
using Opc.Ua.Client;

services
    .AddOpcUa()
    .AddOpcTcpTransport()
    .AddOpcUaClient();

await using IClientChannelManager manager =
    provider.GetRequiredService<IClientChannelManager>();
ISession session = await manager.OpenSessionAsync(
    "opc.tcp://localhost:62541/MyServer");
```

## Target frameworks

`net472`, `net48`, `netstandard2.1`, `net8.0`, `net9.0`,
`net10.0`.

## Additional documentation

See the [main repository README](https://github.com/OPCFoundation/UA-.NETStandard)
and the [Sessions guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/Sessions.md)
for the session / subscription / channel-manager design, the
[Reverse Connect guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/ReverseConnect.md),
and the
[Transfer Subscription guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/TransferSubscription.md).
