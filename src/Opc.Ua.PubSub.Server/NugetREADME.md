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

## Experimental UAFX verification and reservation binding

`WithFxCommunicationIdReservations` registers `PubSubFxReservationMethods` for
the host to bind an initialized local AutomationComponent. The binding handles
`EstablishConnections` with `VerifyAssetCmd`, `VerifyFunctionalEntityCmd`, and
`ReserveCommunicationIdsCmd`. Verification is delegated to each addressed
asset's `VerifyAsset` Method and each related functional entity's `Verify`
Method; communication-ID reservation uses the same Session-owned transaction
as the Part 14 methods. It requires an actual authorized Session and does not
install or advertise the remaining ConnectionEndpoint lifecycle or
`CloseConnections`.

See [UAFX model and binding](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/UAFX.md)
for model registration, host ownership, optional listening information and abort
semantics.

`PubSubNodeManager.BindFxEndpointStatus` also projects the associated runtime
reader/writer health onto a host-owned PubSub ConnectionEndpoint's Status using
the Part 81 state matrices. The host owns membership and disposes/rebinds the
projection when replacing an association. This does not establish or enable a
connection, reset receive timeouts, or implement the remaining endpoint lifecycle.

## Target frameworks

`net48`, `net8.0`, `net9.0`, `net10.0`.

## Additional documentation

See the [PubSub documentation](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/PubSub.md)
for the server-side address-space model and SKS hosting.
