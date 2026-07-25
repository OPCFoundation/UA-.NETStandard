# OPC UA WoT Connectivity — Protocol Binding Abstractions

`OPCFoundation.NetStandard.Opc.Ua.WotCon.Bindings` is the protocol-binding abstraction and planner layer for the OPC UA WoT Connectivity 1.1 runtime.

It defines the stable, replaceable protocol-binder contracts used by the materialization coordinator:

* binding identification, version and capability descriptors;
* form validation and compilation into immutable binding plans;
* payload codec selection;
* credential / trust reference lookup (no secrets in Thing Descriptions);
* Prepare / Activate / Deactivate lifecycle;
* read / write / observe / action / event operations;
* structured diagnostics with RFC 6901 JSON Pointers.

Planner/validator binders for HTTP, CoAP, MQTT, Modbus TCP, BACnet, PROFINET, LoRaWAN and OPC UA ship on every supported target framework. HTTP, Modbus TCP, and OPC UA executors are included when targeting net8.0 or later. MQTT remains in the optional `OPCFoundation.NetStandard.Opc.Ua.WotCon.Bindings.Mqtt` package because it carries the external MQTT transport dependency.

## Target frameworks

The base package targets `net472`, `net48`, `netstandard2.1`, `net8.0`, `net9.0`, and `net10.0`. The planner, plan, codec, credential, diagnostics, and registry APIs are available on all targets. The concrete `Opc.Ua.WotCon.Bindings.Http`, `Opc.Ua.WotCon.Bindings.Modbus`, and `Opc.Ua.WotCon.Bindings.OpcUa` namespaces are available only on `net8.0`, `net9.0`, and `net10.0`.

OPC 10101 target mapping is protocol-neutral and authored on property affordances: `uav:mapToNodeId`, `uav:mapToType`, and `uav:mapByFieldPath` are validated centrally before protocol planning.

See the [protocol binding overview](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/WoTProtocolBindings.md) and [binding-authoring guide](https://github.com/OPCFoundation/UA-.NETStandard/blob/master/docs/WoTBindingDevelopment.md).
