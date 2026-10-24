# UAFX information models

`OPCFoundation.NetStandard.Opc.Ua.Fx` supplies generated OPC 10000-81 model
contracts for the FX Data, AutomationComponent and ConnectionManager
namespaces. It uses the existing Device Integration model and the stack's
source generator; it adds no native runtime or third-party package.

## Register the models

Register dependencies before their consumers. The same registration order
applies to encodeable factories, node-state factories and predefined nodes.
The namespace table may already contain application namespaces.

```csharp
using Opc.Ua;
using Opc.Ua.Di;
using Opc.Ua.Fx.Data;
using Opc.Ua.Fx.Ac;
using Opc.Ua.Fx.Cm;

messageContext.Factory.Builder
    .AddOpcUaDi()
    .AddOpcUaFxData()
    .AddOpcUaFxAc()
    .AddOpcUaFxCm()
    .Commit();

nodes.AddOpcUaDi(systemContext)
    .AddOpcUaFxData(systemContext)
    .AddOpcUaFxAc(systemContext)
    .AddOpcUaFxCm(systemContext);
```

The generated types include asynchronous `EstablishConnections` and
`CloseConnections` Method handlers and client proxies, endpoint configuration
and result structures, and PubSub communication-identifier reservation
structures. `PubSubReserveCommunicationIds2DataType` and
`PubSubReserveCommunicationIdsResult2DataType` preserve the standard
`RequestTransportSpecificInfo` and `TransportSpecificInfo` fields.

## Source provenance

The three unmodified UAFX 1.00.04 NodeSets in `src/Opc.Ua.Fx/Model` come from
[UA-Nodeset at dd178cf655ed3d74f1d085ef04e0e226a1d3ed9f](https://github.com/OPCFoundation/UA-Nodeset/tree/dd178cf655ed3d74f1d085ef04e0e226a1d3ed9f/UAFX).
Their Git blob identifiers are:

| Input | Git blob |
| --- | --- |
| `opc.ua.fx.data.nodeset2.xml` | `12212843b7499c48059ca081963d349cd06d19a4` |
| `opc.ua.fx.ac.nodeset2.xml` | `224276cd06fb200f7421d5370698fd187b199893` |
| `opc.ua.fx.cm.nodeset2.xml` | `674ed1aa7ec9baad7119eba35ed1b87c10d2f0a8` |

Generated C# is produced during compilation and is not checked in.

## Implementation boundary

These are model contracts, not a ConnectionManager or AutomationComponent
lifecycle implementation. Applications must implement authorization,
verification, reservation, endpoint association, enablement, status,
persistence and cleanup according to Part 81 and their selected Part 84
profiles. Loading a model or completing a Method call is not evidence of
valid PubSub DataSet reception or UAFX conformance.

The experimental [Data Channel binding](DataChannels.md) provides the
`ConfigureUafx` transport-configuration bridge. It does not install Part 81
configuration or grant a ConnectionManager authority over authenticated data
peers.
