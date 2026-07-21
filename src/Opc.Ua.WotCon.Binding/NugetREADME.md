# OPC UA WoT Connectivity — Protocol Binding Abstractions

`OPCFoundation.NetStandard.Opc.Ua.WotCon.Binding` is the dependency-light
abstraction and planner layer for the OPC UA WoT Connectivity V2 runtime.

It defines the stable, replaceable protocol-binder contracts used by the
materialization coordinator:

* binding identification, version and capability descriptors;
* form validation and compilation into immutable binding plans;
* payload codec selection;
* credential / trust reference lookup (no secrets in Thing Descriptions);
* Prepare / Activate / Deactivate lifecycle;
* read / write / observe / action / event operations;
* structured diagnostics with RFC 6901 JSON Pointers.

The assembly carries **no transport dependencies**. Planner/validator binders
for HTTP, CoAP, MQTT, Modbus TCP, BACnet, PROFINET, LoRaWAN and OPC UA ship
here so unsupported runtime protocols can still validate and compile plans.
Concrete executors live in the optional focused
`Opc.Ua.WotCon.Binding.Http` / `.Mqtt` / `.Modbus` / `.OpcUa` packages.

See `docs/WoTProtocolBindings.md` for the developer guide and a sample custom
binder.
