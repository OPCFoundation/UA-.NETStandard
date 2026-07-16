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

## Target frameworks

`net472`, `net48`, `netstandard2.1`, `net8.0`, `net9.0`, `net10.0`.

## Additional documentation

See the [PubSub documentation](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/Docs/PubSub.md)
for the server-side address-space model and SKS hosting.
