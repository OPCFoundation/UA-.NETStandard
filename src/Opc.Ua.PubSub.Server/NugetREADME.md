# OPC UA .NET Standard — PubSub server integration

`OPCFoundation.NetStandard.Opc.Ua.PubSub.Server` integrates the modern
`OPCFoundation.NetStandard.Opc.Ua.PubSub` stack into an OPC UA server: it
exposes the Part 14 PubSub address-space object model, the configuration
methods, per-component diagnostics, and hosting of the Security Key
Service (SKS).

## Getting started

Add the PubSub address space to an OPC UA server:

```csharp
using Microsoft.Extensions.DependencyInjection;

builder.Services.AddOpcUaServer()
    .AddPubSubServer();
```

Enabling `PubSubServerOptions.ExposeConfigurationMethods` materializes the
standard optional `AddConnection`, `RemoveConnection`, `SetSecurityKeys`, and
`PubSubConfiguration` nodes before binding their handlers. The configuration
file retains the standard namespace-zero browse names, method declarations, and
argument metadata so generated `PublishSubscribeTypeClient`,
`PubSubConfigurationTypeClient`, and `FileTypeClient` proxies can access it.
The existing vendor-namespace configuration file remains available for
compatibility.

## DataChannel-backed PubSub connections

The optional DataChannel integration uses the server's existing source
registry, authorization policy, and accepted-flow router:

```csharp
using Opc.Ua.PubSub.Server.DataChannels;
using Opc.Ua.Server.DataChannels;

sources.Register(PubSubDataChannelSource.Create(
    connection,
    new DataChannelApplicationPolicy(["urn:example:publisher"]),
    maximumFlows: 4));

var flows = new ServerDataChannelPubSubFlowProvider(serverDataChannels);
```

Pass `flows` to `UseDataChannelTransport` / `AddDataChannelTransport`, or
use `IPubSubBuilder.AddServerDataChannelTransport(carrierKind)` to resolve
the registered `ServerDataChannelProvider` from DI. Register real
carrier listeners and resource policies independently. This adapter
routes by **resource URI, payload protocol, and carrier kind**, so
different PubSub connections cannot consume each other's accepted flows.
It does not run a second `DataChannelContext.AcceptAsync` loop.

`PubSubDataChannelSource.Create` copies the connection's exact descriptor
into a source declaration; it does not infer permissions from publisher
identifiers or broaden a policy. Configured sources need no Session or
model projection. Optional source/content node identities and
Session-authorized flows continue through the existing Server control
provider. Disposing a PubSub flow does not dispose that provider or the
shared SecureChannel.

## Target frameworks

`net48`, `net8.0`, `net9.0`, `net10.0`.

## Additional documentation

See the [PubSub documentation](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/PubSub.md)
for the server-side address-space model and SKS hosting.
